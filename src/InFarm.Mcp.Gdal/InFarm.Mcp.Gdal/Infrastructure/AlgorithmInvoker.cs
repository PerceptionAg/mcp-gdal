using System.Text.Json;
using System.Text.RegularExpressions;

namespace InFarm.Mcp.Gdal.Infrastructure;

public sealed record AlgorithmRunResult
{
    [Description("Algorithm that ran, as its path below the 'gdal' command.")]
    public required string Algorithm { get; init; }

    [Description("Text that the algorithm returned, which is JSON for the info algorithms.")]
    public string? Text { get; init; }

    [Description("Description of the output dataset, when the algorithm wrote one.")]
    public ProcessingResult? Output { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

// Runs the algorithms of the unified 'gdal' command through the GDAL algorithm registry, after checking every dataset argument against the path policy.
internal static partial class AlgorithmInvoker
{
    // These algorithms only read their inputs, so they stay available in read-only mode.
    private static readonly HashSet<string> ReadOnlyAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "raster info", "vector info", "mdim info", "raster pixel-info", "raster compare", "dataset identify", "dataset check",
        "vsi list", "vsi sozip list", "vsi sozip validate", "driver cog validate", "driver gpkg validate", "driver pdf list-layers"
    };

    private static readonly HashSet<string> ReservedArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "stdout", "help", "help-doc", "json-usage", "progress", "version", "drivers"
    };

    private static readonly HashSet<string> OutputStringArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "output", "destination", "output-directory"
    };

    private static readonly HashSet<string> InPlaceFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "append", "update", "overwrite-layer", "upsert"
    };

    // Returns the algorithm at a path such as ["raster", "reproject"]. The caller owns the returned object.
    public static Algorithm Instantiate(IReadOnlyList<string> path)
    {
        using var registry = GdalApi.GetGlobalAlgorithmRegistry();
        var algorithm = registry.InstantiateAlg("gdal")
            ?? throw new McpException("This GDAL build does not provide the unified 'gdal' algorithm registry.");
        var hasRootName = path.Count > 0 && path[0].Equals("gdal", StringComparison.OrdinalIgnoreCase);
        foreach (var name in hasRootName ? path.Skip(1) : path)
        {
            Algorithm? child;
            using (algorithm)
            {
                child = algorithm.HasSubAlgorithms() ? algorithm.InstantiateSubAlgorithm(name) : null;
                if (child is null)
                {
                    var available = string.Join(", ", algorithm.GetSubAlgorithmNames() ?? []);
                    throw new McpException(
                        $"The algorithm '{string.Join(' ', path)}' does not exist. '{name}' was not found. Available names at that level: {available}.");
                }
            }

            algorithm = child;
        }

        return algorithm;
    }

    public static string[] SplitPath(string algorithm)
    {
        return algorithm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static AlgorithmRunResult Run(
        string[] path,
        IReadOnlyDictionary<string, JsonElement> arguments,
        GdalToolContext context,
        GdalOperation operation)
    {
        var name = string.Join(' ', path.Where((part, index) => index > 0 || !part.Equals("gdal", StringComparison.OrdinalIgnoreCase)));
        if (context.Options.ReadOnly && !ReadOnlyAlgorithms.Contains(name))
        {
            throw new McpException(
                $"The server is running in read-only mode, in which only these algorithms are available: {string.Join(", ", ReadOnlyAlgorithms.Order())}.");
        }

        var isPipeline = path[^1].Equals("pipeline", StringComparison.OrdinalIgnoreCase);
        if (isPipeline && context.Paths.IsRestricted)
        {
            throw new McpException(
                "Pipeline algorithms embed dataset paths in free text that the server cannot check against the allowed roots, so they are unavailable while allowed roots are configured.");
        }

        using var algorithm = Instantiate(path);
        if (algorithm.HasSubAlgorithms())
        {
            throw new McpException(
                $"'{name}' is a group of algorithms. Choose one of: {string.Join(", ", algorithm.GetSubAlgorithmNames() ?? [])}.");
        }

        var overwrite = arguments.TryGetValue("overwrite", out var overwriteValue) && overwriteValue.ValueKind == JsonValueKind.True;
        var inPlace = arguments.Any(argument => InPlaceFlags.Contains(argument.Key) && argument.Value.ValueKind == JsonValueKind.True);
        string? outputDataset = null;
        foreach (var (argumentName, value) in arguments)
        {
            var resolved = SetArgument(algorithm, name, argumentName, value, overwrite, inPlace, context);
            outputDataset ??= resolved;
        }

        try
        {
            if (!algorithm.Run(operation.Progress, null))
            {
                throw new McpException($"The algorithm '{name}' failed. {Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
            }

            string? text = null;
            if ((algorithm.GetArgNames() ?? []).Contains("output-string"))
            {
                using var outputString = algorithm.GetArg("output-string");
                text = outputString.GetAsString();
            }

            algorithm.Finalize_();
            operation.ThrowIfCancelled();
            return new AlgorithmRunResult
            {
                Algorithm = name,
                Text = string.IsNullOrEmpty(text)
                    ? null
                    : Limits.EnsureText(text, context.Options.MaxResponseCharacters, "Narrow the request, or write the result to an output file."),
                Output = outputDataset is null ? null : Describe.Output(outputDataset, operation),
                Warnings = operation.WarningsOrNull
            };
        }
        catch
        {
            if (outputDataset is not null && !inPlace)
            {
                algorithm.Finalize_();
                Processing.DeleteQuietly(outputDataset);
            }

            throw;
        }
    }

    // Sets one argument and returns the resolved path when the argument names the output dataset.
    private static string? SetArgument(
        Algorithm algorithm, string algorithmName, string argumentName, JsonElement value, bool overwrite, bool inPlace, GdalToolContext context)
    {
        argumentName = argumentName.TrimStart('-');
        using var argument = ReservedArguments.Contains(argumentName) ? null : algorithm.GetArgNonConst(argumentName);
        if (argument is null || argument.IsHiddenForAPI())
        {
            var available = (algorithm.GetArgNames() ?? []).Where(candidate => !ReservedArguments.Contains(candidate) && candidate != "output-string");
            throw new McpException(
                $"The algorithm '{algorithmName}' has no argument '{argumentName}'. Available arguments: {string.Join(", ", available)}. " +
                "Use gdal_algorithm_describe for their types and meanings.");
        }

        var type = argument.GetType_();
        string? output = null;
        bool accepted;
        try
        {
            if (type == GdalConst.GAAT_BOOLEAN)
            {
                accepted = argument.SetAsBoolean(value.ValueKind == JsonValueKind.String ? bool.Parse(value.GetString()!) : value.GetBoolean());
            }
            else if (type == GdalConst.GAAT_INTEGER)
            {
                accepted = argument.SetAsInteger(value.GetInt32());
            }
            else if (type == GdalConst.GAAT_REAL)
            {
                accepted = argument.SetAsDouble(value.GetDouble());
            }
            else if (type == GdalConst.GAAT_STRING)
            {
                var text = Scalar(value);
                if (OutputStringArguments.Contains(argumentName))
                {
                    // The algorithm applies its own overwrite rules to a directory or file that is named by a plain string.
                    text = context.Paths.Writable(text);
                }
                else
                {
                    CheckFreeText(text, context);
                }

                accepted = argument.SetAsString(text);
            }
            else if (type == GdalConst.GAAT_STRING_LIST)
            {
                var items = Items(value).Select(Scalar).ToArray();
                foreach (var item in items)
                {
                    CheckFreeText(item, context);
                }

                accepted = argument.SetAsStringList(items);
            }
            else if (type == GdalConst.GAAT_INTEGER_LIST)
            {
                var items = Items(value).Select(item => item.GetInt32()).ToArray();
                accepted = argument.SetAsIntegerList(items.Length, items);
            }
            else if (type == GdalConst.GAAT_REAL_LIST)
            {
                var items = Items(value).Select(item => item.GetDouble()).ToArray();
                accepted = argument.SetAsDoubleList(items.Length, items);
            }
            else if (type == GdalConst.GAAT_DATASET)
            {
                var text = Scalar(value);
                string resolved;
                if (!argument.IsOutput())
                {
                    resolved = ResolveInputName(text, context);
                }
                else if (inPlace && PathPolicy.Exists(text))
                {
                    resolved = context.Paths.Update(text);
                    output = resolved;
                }
                else
                {
                    resolved = context.Paths.Output(text, overwrite);
                    output = resolved;
                }

                using var datasetValue = argument.GetAsDatasetValue();
                datasetValue.SetName(resolved);
                accepted = true;
            }
            else if (type == GdalConst.GAAT_DATASET_LIST)
            {
                accepted = argument.SetDatasetNames([.. Items(value).Select(item => ResolveInputName(Scalar(item), context))]);
            }
            else
            {
                throw new McpException($"The argument '{argumentName}' has a type that this server does not support.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new McpException(
                $"The value of the argument '{argumentName}' does not match its type '{GdalApi.AlgorithmArgTypeName(type)}'. {ex.Message}");
        }

        if (!accepted)
        {
            throw new McpException(
                $"GDAL rejected the value of the argument '{argumentName}'. {Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
        }

        return output;
    }

    // Resolves an input dataset name, which can carry a variable prefix such as "A=path" for raster calculations.
    private static string ResolveInputName(string name, GdalToolContext context)
    {
        var match = NamedInputPattern().Match(name);
        return match.Success
            ? $"{match.Groups[1].Value}={context.Paths.Input(match.Groups[2].Value)}"
            : context.Paths.Input(name);
    }

    private static void CheckFreeText(string text, GdalToolContext context)
    {
        if (text.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase)
            || text.Contains("://", StringComparison.Ordinal)
            || (Path.IsPathRooted(text) && (File.Exists(text) || Directory.Exists(text) || context.Paths.IsRestricted)))
        {
            context.Paths.Input(text);
        }
    }

    private static string Scalar(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    private static IEnumerable<JsonElement> Items(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)=(.+)$")]
    private static partial Regex NamedInputPattern();
}
