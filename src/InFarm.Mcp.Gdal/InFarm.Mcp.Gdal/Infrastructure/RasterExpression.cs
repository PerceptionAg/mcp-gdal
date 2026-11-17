namespace InFarm.Mcp.Gdal.Infrastructure;

// Translates an arithmetic expression into a tree of lazy GDAL computed bands, which GDAL evaluates block by block when the result is written.
internal sealed class RasterExpression : IDisposable
{
    private const double NaturalLogOfTen = 2.302585092994046;

    private static readonly Dictionary<string, DataType> Casts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["byte"] = DataType.GDT_Byte, ["int8"] = DataType.GDT_Int8, ["uint16"] = DataType.GDT_UInt16, ["int16"] = DataType.GDT_Int16,
        ["uint32"] = DataType.GDT_UInt32, ["int32"] = DataType.GDT_Int32, ["float32"] = DataType.GDT_Float32, ["float64"] = DataType.GDT_Float64
    };

    private readonly IReadOnlyDictionary<string, Dataset> inputs;
    private readonly List<IDisposable> owned = [];
    private string text = string.Empty;
    private int position;

    public RasterExpression(IReadOnlyDictionary<string, Dataset> inputs)
    {
        this.inputs = inputs;
    }

    // A value is either a constant or a band. A band is computed when the expression created it.
    private readonly record struct Operand(double Constant, Band? Band, bool Computed)
    {
        public bool IsConstant => Band is null;
    }

    // Returns a computed band for the expression. The band stays valid until this object is disposed.
    public Band Evaluate(string expression)
    {
        text = expression;
        position = 0;
        var result = ParseSum();
        SkipWhitespace();
        if (position < text.Length)
        {
            throw Error($"Unexpected '{text[position]}'");
        }

        if (result.IsConstant)
        {
            throw new McpException($"The expression '{expression}' does not reference a raster, so it has no extent.");
        }

        // A plain source band belongs to a multi-band dataset, so it is wrapped in a computed band that stands alone.
        return result.Computed ? result.Band! : Own(result.Band!.AsType(result.Band.DataType));
    }

    public void Dispose()
    {
        for (var i = owned.Count - 1; i >= 0; i--)
        {
            owned[i].Dispose();
        }

        owned.Clear();
    }

    private Operand ParseSum()
    {
        var left = ParseProduct();
        while (true)
        {
            if (TryConsume('+'))
            {
                left = Binary(RasterAlgebraBinaryOperation.GRABO_ADD, left, ParseProduct());
            }
            else if (TryConsume('-'))
            {
                left = Binary(RasterAlgebraBinaryOperation.GRABO_SUB, left, ParseProduct());
            }
            else
            {
                return left;
            }
        }
    }

    private Operand ParseProduct()
    {
        var left = ParseUnary();
        while (true)
        {
            if (TryConsume('*'))
            {
                left = Binary(RasterAlgebraBinaryOperation.GRABO_MUL, left, ParseUnary());
            }
            else if (TryConsume('/'))
            {
                left = Binary(RasterAlgebraBinaryOperation.GRABO_DIV, left, ParseUnary());
            }
            else
            {
                return left;
            }
        }
    }

    private Operand ParseUnary()
    {
        if (TryConsume('-'))
        {
            return Binary(RasterAlgebraBinaryOperation.GRABO_SUB, new Operand(0, null, false), ParseUnary());
        }

        if (TryConsume('+'))
        {
            return ParseUnary();
        }

        var operand = ParsePrimary();
        return TryConsume('^') ? Binary(RasterAlgebraBinaryOperation.GRABO_POW, operand, ParseUnary()) : operand;
    }

    private Operand ParsePrimary()
    {
        SkipWhitespace();
        if (position >= text.Length)
        {
            throw Error("The expression ends unexpectedly");
        }

        if (TryConsume('('))
        {
            var inner = ParseSum();
            Expect(')');
            return inner;
        }

        var current = text[position];
        if (char.IsAsciiDigit(current) || current == '.')
        {
            var start = position;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '.'
                || ((text[position] is '+' or '-') && text[position - 1] is 'e' or 'E')))
            {
                position++;
            }

            if (!double.TryParse(text[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw Error($"'{text[start..position]}' is not a number");
            }

            return new Operand(number, null, false);
        }

        if (!char.IsAsciiLetter(current) && current != '_')
        {
            throw Error($"Unexpected '{current}'");
        }

        var nameStart = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        var name = text[nameStart..position];
        if (TryConsume('('))
        {
            var arguments = new List<Operand>();
            if (!TryConsume(')'))
            {
                do
                {
                    arguments.Add(ParseSum());
                }
                while (TryConsume(','));
                Expect(')');
            }

            return Call(name, arguments);
        }

        var dataset = inputs.FirstOrDefault(input => input.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value
            ?? throw Error($"'{name}' is not an input name. The inputs are: {string.Join(", ", inputs.Keys)}");
        var bandNumber = 1;
        if (TryConsume('['))
        {
            var start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (!int.TryParse(text[start..position], out bandNumber))
            {
                throw Error("A band number is expected inside the brackets");
            }

            Expect(']');
        }

        if (bandNumber < 1 || bandNumber > dataset.RasterCount)
        {
            throw Error($"The input '{name}' has {dataset.RasterCount} band(s), so band {bandNumber} does not exist");
        }

        return new Operand(0, Own(dataset.GetRasterBand(bandNumber)), false);
    }

    private Operand Call(string name, List<Operand> arguments)
    {
        var function = name.ToLowerInvariant();
        if (Casts.TryGetValue(function, out var type))
        {
            var operand = Single(name, arguments);
            return operand.IsConstant ? operand : Computed(operand.Band!.AsType(type));
        }

        switch (function)
        {
            case "abs":
                return Unary(name, arguments, RasterAlgebraUnaryOperation.GRAUO_ABS, Math.Abs);
            case "sqrt":
                return Unary(name, arguments, RasterAlgebraUnaryOperation.GRAUO_SQRT, Math.Sqrt);
            case "log10":
                return Unary(name, arguments, RasterAlgebraUnaryOperation.GRAUO_LOG10, Math.Log10);
            case "log" or "ln":
                // The natural logarithm of a band needs muparser, which the bundled GDAL lacks, so it is derived from the base-10 logarithm.
                return Binary(
                    RasterAlgebraBinaryOperation.GRABO_MUL,
                    Unary(name, arguments, RasterAlgebraUnaryOperation.GRAUO_LOG10, Math.Log10),
                    new Operand(NaturalLogOfTen, null, false));
            case "exp":
                return Binary(RasterAlgebraBinaryOperation.GRABO_POW, new Operand(Math.E, null, false), Single(name, arguments));
            case "pow":
                if (arguments.Count != 2)
                {
                    throw Error("The function pow needs two arguments");
                }

                return Binary(RasterAlgebraBinaryOperation.GRABO_POW, arguments[0], arguments[1]);
            case "min":
                return Extreme(name, arguments, true);
            case "max":
                return Extreme(name, arguments, false);
            case "sum":
                return Sum(name, arguments);
            case "mean" or "avg":
                return Binary(RasterAlgebraBinaryOperation.GRABO_DIV, Sum(name, arguments), new Operand(arguments.Count, null, false));
            default:
                throw Error(
                    $"'{name}' is not a supported function. The functions are abs, sqrt, log, log10, exp, pow, min, max, sum, mean and the casts " +
                    string.Join(", ", Casts.Keys));
        }
    }

    private Operand Unary(string name, List<Operand> arguments, RasterAlgebraUnaryOperation operation, Func<double, double> fold)
    {
        var operand = Single(name, arguments);
        return operand.IsConstant
            ? new Operand(fold(operand.Constant), null, false)
            : Computed(operand.Band!.UnaryOp(operation));
    }

    private Operand Sum(string name, List<Operand> arguments)
    {
        if (arguments.Count == 0)
        {
            throw Error($"The function {name} needs at least one argument");
        }

        return arguments.Aggregate((left, right) => Binary(RasterAlgebraBinaryOperation.GRABO_ADD, left, right));
    }

    private Operand Extreme(string name, List<Operand> arguments, bool minimum)
    {
        if (arguments.Count == 0)
        {
            throw Error($"The function {name} needs at least one argument");
        }

        var constants = arguments.Where(argument => argument.IsConstant).Select(argument => argument.Constant).ToList();
        var bands = arguments.Where(argument => !argument.IsConstant).Select(argument => argument.Band!).ToArray();
        double? constant = constants.Count == 0 ? null : minimum ? constants.Min() : constants.Max();
        if (bands.Length == 0)
        {
            return new Operand(constant!.Value, null, false);
        }

        var result = arguments.First(argument => !argument.IsConstant);
        if (bands.Length > 1)
        {
            result = Computed(minimum ? Band.MinimumOfNBands(bands.Length, bands) : Band.MaximumOfNBands(bands.Length, bands));
        }

        if (constant is null)
        {
            return result;
        }

        return Computed(minimum ? result.Band!.MinConstant(constant.Value) : result.Band!.MaxConstant(constant.Value));
    }

    private Operand Binary(RasterAlgebraBinaryOperation operation, Operand left, Operand right)
    {
        if (left.IsConstant && right.IsConstant)
        {
            return new Operand(
                operation switch
                {
                    RasterAlgebraBinaryOperation.GRABO_ADD => left.Constant + right.Constant,
                    RasterAlgebraBinaryOperation.GRABO_SUB => left.Constant - right.Constant,
                    RasterAlgebraBinaryOperation.GRABO_MUL => left.Constant * right.Constant,
                    RasterAlgebraBinaryOperation.GRABO_DIV => left.Constant / right.Constant,
                    _ => Math.Pow(left.Constant, right.Constant)
                },
                null,
                false);
        }

        if (right.IsConstant)
        {
            return Computed(left.Band!.BinaryOpDouble(operation, right.Constant));
        }

        if (left.IsConstant)
        {
            return Computed(Band.BinaryOpDoubleToBand(left.Constant, operation, right.Band!));
        }

        if (operation == RasterAlgebraBinaryOperation.GRABO_POW)
        {
            throw Error("Raising one raster to the power of another raster is not supported by the bundled GDAL build. One side of '^' must be a number");
        }

        return Computed(left.Band!.BinaryOpBand(operation, right.Band!));
    }

    private Operand Single(string name, List<Operand> arguments)
    {
        return arguments.Count == 1 ? arguments[0] : throw Error($"The function {name} needs exactly one argument");
    }

    private Operand Computed(Band band)
    {
        return new Operand(0, Own(band), true);
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    private void SkipWhitespace()
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private bool TryConsume(char expected)
    {
        SkipWhitespace();
        if (position < text.Length && text[position] == expected)
        {
            position++;
            return true;
        }

        return false;
    }

    private void Expect(char expected)
    {
        if (!TryConsume(expected))
        {
            throw Error($"'{expected}' is expected");
        }
    }

    private McpException Error(string message)
    {
        return new McpException(
            $"{message} at position {position + 1} of the expression '{text}'. Expressions support + - * / ^, parentheses, numbers, " +
            "input names with an optional band index such as A[2], and the functions abs, sqrt, log, log10, exp, pow, min, max, sum and mean. " +
            "Comparisons and conditionals are not available because the bundled GDAL build has no muparser. Use the 'raster reclassify' algorithm for thresholds.");
    }
}
