using System.Text.Json;

namespace InFarm.Mcp.Gdal.Infrastructure;

internal static class Num
{
    // JSON cannot carry NaN or infinity, so non-finite values are reported as null.
    public static double? Finite(double value)
    {
        return double.IsFinite(value) ? value : null;
    }

    public static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

internal static class Limits
{
    public static string EnsureText(string text, int maxCharacters, string advice)
    {
        if (text.Length > maxCharacters)
        {
            throw new McpException(
                $"The result holds {text.Length} characters, which exceeds the response limit of {maxCharacters} characters. {advice}");
        }

        return text;
    }
}

internal static class VsiTemp
{
    public static string Create(string extension)
    {
        return $"{PathPolicy.MemoryPrefix}infarm-mcp-gdal/{Guid.NewGuid():N}{extension}";
    }

    public static void Delete(string path)
    {
        try
        {
            GdalApi.Unlink(path);
        }
        catch (ApplicationException)
        {
            // The temporary file was never created.
        }
    }
}

// Builds the argument list of a GDAL utility such as gdal_translate.
internal sealed class ArgList
{
    private static readonly HashSet<string> BlockedSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "--config", "--optfile", "--debug", "--help", "--help-general", "--long-usage", "--version", "--formats", "--format",
        "--license", "--build", "--locale", "--pause", "--mempreload", "--drivers"
    };

    private readonly List<string> items = [];

    public ArgList Add(params string[] values)
    {
        items.AddRange(values);
        return this;
    }

    public ArgList Flag(bool condition, string flag)
    {
        if (condition)
        {
            items.Add(flag);
        }

        return this;
    }

    public ArgList Option(string flag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add(flag);
            items.Add(value);
        }

        return this;
    }

    public ArgList Option(string flag, double? value)
    {
        return value is null ? this : Add(flag, Num.Text(value.Value));
    }

    public ArgList Option(string flag, int? value)
    {
        return value is null ? this : Add(flag, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    // Adds a flag followed by a fixed number of numeric values, for example -te with four values.
    public ArgList Numbers(string flag, IReadOnlyList<double>? values, int expectedCount, string parameterName)
    {
        if (values is null || values.Count == 0)
        {
            return this;
        }

        if (values.Count != expectedCount)
        {
            throw new McpException($"The parameter '{parameterName}' needs exactly {expectedCount} numbers, but {values.Count} were given.");
        }

        items.Add(flag);
        items.AddRange(values.Select(Num.Text));
        return this;
    }

    public ArgList Each(string flag, IEnumerable<string>? values)
    {
        foreach (var value in values ?? [])
        {
            items.Add(flag);
            items.Add(value);
        }

        return this;
    }

    // Appends caller-supplied utility arguments after rejecting switches that change process-wide state.
    public ArgList Extra(IReadOnlyList<string>? additional, PathPolicy paths)
    {
        foreach (var argument in additional ?? [])
        {
            if (BlockedSwitches.Contains(argument) || BlockedSwitches.Contains(argument.Split('=')[0]))
            {
                throw new McpException(
                    $"The additional argument '{argument}' is not allowed. Use gdal_config_set to change GDAL configuration options.");
            }

            if (LooksLikePath(argument))
            {
                paths.Input(argument);
            }

            items.Add(argument);
        }

        return this;
    }

    public string[] ToArray()
    {
        return [.. items];
    }

    private static bool LooksLikePath(string argument)
    {
        return argument.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("://", StringComparison.Ordinal)
            || (Path.IsPathRooted(argument) && !argument.StartsWith('-'));
    }
}

internal static class Open
{
    public static Dataset Raster(string path, bool update = false, IReadOnlyList<string>? openOptions = null)
    {
        return Ex(path, GdalConst.OF_RASTER, update, openOptions);
    }

    public static Dataset Vector(string path, bool update = false, IReadOnlyList<string>? openOptions = null)
    {
        return Ex(path, GdalConst.OF_VECTOR, update, openOptions);
    }

    // A classic raster can also be the input of a multidimensional translation, which allowClassicRaster permits.
    public static Dataset Multidim(string path, IReadOnlyList<string>? openOptions = null, bool allowClassicRaster = false)
    {
        return Ex(path, GdalConst.OF_MULTIDIM_RASTER | (allowClassicRaster ? GdalConst.OF_RASTER : 0), false, openOptions);
    }

    // Opens a dataset of any kind, preferring the raster and vector interfaces of the driver.
    public static Dataset Any(string path, IReadOnlyList<string>? openOptions = null, bool update = false)
    {
        return Ex(path, 0, update, openOptions);
    }

    public static DataSource DataSource(string path, bool update = false)
    {
        return Ogr.Open(path, update ? 1 : 0)
            ?? throw new McpException($"'{Redactor.Redact(path)}' could not be opened as a vector dataset.");
    }

    private static Dataset Ex(string path, int kindFlags, bool update, IReadOnlyList<string>? openOptions)
    {
        var flags = kindFlags | GdalConst.OF_VERBOSE_ERROR | (update ? GdalConst.OF_UPDATE : GdalConst.OF_READONLY);
        return GdalApi.OpenEx(path, (uint)flags, null, openOptions?.ToArray(), null)
            ?? throw new McpException($"'{Redactor.Redact(path)}' could not be opened. Use gdal_driver_list to see the supported formats.");
    }
}

internal static class Crs
{
    private static readonly string[] SafeInputOptions = ["ALLOW_NETWORK_ACCESS=NO", "ALLOW_FILE_ACCESS=NO"];

    // Parses an EPSG code, WKT, PROJ string, PROJJSON or CRS name. The caller owns the returned object.
    public static SpatialReference Parse(string definition, bool traditionalAxisOrder = true)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new McpException("A coordinate reference system definition is required, for example EPSG:4326.");
        }

        var srs = new SpatialReference(string.Empty);
        try
        {
            srs.SetFromUserInput(definition.Trim(), SafeInputOptions);
        }
        catch (ApplicationException ex)
        {
            srs.Dispose();
            throw new McpException(
                $"The coordinate reference system '{Shorten(definition)}' could not be interpreted: {ex.Message}. " +
                "Pass an authority code such as EPSG:4326, a WKT string, a PROJ string or PROJJSON. Use gdal_crs_lookup to search for codes.");
        }

        srs.SetAxisMappingStrategy(traditionalAxisOrder
            ? AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER
            : AxisMappingStrategy.OAMS_AUTHORITY_COMPLIANT);
        return srs;
    }

    public static string? Code(SpatialReference srs)
    {
        var authority = srs.GetAuthorityName(null);
        var code = srs.GetAuthorityCode(null);
        return string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(code) ? null : $"{authority}:{code}";
    }

    public static string Kind(SpatialReference srs)
    {
        if (srs.IsCompound() == 1)
        {
            return "compound";
        }

        if (srs.IsProjected() == 1)
        {
            return "projected";
        }

        if (srs.IsGeographic() == 1)
        {
            return "geographic";
        }

        if (srs.IsGeocentric() == 1)
        {
            return "geocentric";
        }

        if (srs.IsVertical() == 1)
        {
            return "vertical";
        }

        return srs.IsLocal() == 1 ? "local" : "unknown";
    }

    public static string Wkt(SpatialReference srs, string format = "WKT2_2019", bool multiline = false)
    {
        srs.ExportToWkt(out var wkt, [$"FORMAT={format}", $"MULTILINE={(multiline ? "YES" : "NO")}"]);
        return wkt;
    }

    public static CrsSummary? Summarize(SpatialReference? srs, bool includeWkt = false)
    {
        if (srs is null)
        {
            return null;
        }

        return new CrsSummary
        {
            Name = srs.GetName(),
            Code = Code(srs),
            Kind = Kind(srs),
            Units = srs.IsGeographic() == 1 ? srs.GetAngularUnitsName() : srs.GetLinearUnitsName(),
            Wkt = includeWkt ? Wkt(srs) : null
        };
    }

    private static string Shorten(string text)
    {
        return text.Length <= 120 ? text : text[..120] + "...";
    }
}

internal static class Geometries
{
    // Parses WKT, GeoJSON or GML. The caller owns the returned geometry.
    public static Geometry Parse(string text, SpatialReference? srs = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new McpException("A geometry is required as WKT or GeoJSON text.");
        }

        text = text.Trim();
        Geometry? geometry;
        try
        {
            geometry = text[0] switch
            {
                '{' => Ogr.CreateGeometryFromJson(text),
                '<' => Ogr.CreateGeometryFromGML(text),
                _ => Geometry.CreateFromWkt(text)
            };
        }
        catch (ApplicationException ex)
        {
            throw new McpException($"The geometry could not be parsed: {ex.Message}. Pass WKT such as 'POINT (1 2)' or a GeoJSON geometry object.");
        }

        if (geometry is null)
        {
            throw new McpException("The geometry could not be parsed. Pass WKT such as 'POINT (1 2)' or a GeoJSON geometry object.");
        }

        if (srs is not null)
        {
            geometry.AssignSpatialReference(srs);
        }

        return geometry;
    }

    // Returns the geometry as text, or null with truncated set when the text exceeds the character limit.
    public static string? Format(Geometry? geometry, GeometryFormat format, int maxCharacters, out bool truncated)
    {
        truncated = false;
        if (geometry is null || format is GeometryFormat.None or GeometryFormat.Envelope)
        {
            return null;
        }

        string text;
        if (format == GeometryFormat.Geojson)
        {
            text = geometry.ExportToJson([]);
        }
        else
        {
            geometry.ExportToIsoWkt(out text);
        }

        if (text.Length > maxCharacters)
        {
            truncated = true;
            return null;
        }

        return text;
    }

    public static Bounds? Envelope(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty())
        {
            return null;
        }

        using var envelope = new Envelope();
        geometry.GetEnvelope(envelope);
        return new Bounds(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }

    public static string TypeName(wkbGeometryType type)
    {
        return Ogr.GeometryTypeToName(type);
    }

    // Resolves a geometry type name such as Polygon, MultiLineString or "Point Z" to the OGR enumeration.
    public static wkbGeometryType ParseType(string name)
    {
        var normalized = "wkb" + name.Replace(" ", string.Empty).Replace("_", string.Empty);
        if (normalized.EndsWith("Z", StringComparison.Ordinal) && !normalized.EndsWith("ZM", StringComparison.Ordinal)
            && Enum.TryParse<wkbGeometryType>(normalized[..^1] + "25D", true, out var legacy))
        {
            return legacy;
        }

        if (Enum.TryParse<wkbGeometryType>(normalized, true, out var type))
        {
            return type;
        }

        throw new McpException(
            $"'{name}' is not a geometry type. Use a name such as None, Unknown, Point, LineString, Polygon, MultiPoint, MultiLineString, " +
            "MultiPolygon or GeometryCollection, optionally followed by Z, M or ZM.");
    }
}

internal static class FieldValues
{
    public static object? Get(Feature feature, int index, FieldDefn definition)
    {
        if (!feature.IsFieldSetAndNotNull(index))
        {
            return null;
        }

        switch (definition.GetFieldType())
        {
            case FieldType.OFTInteger:
                var number = feature.GetFieldAsInteger(index);
                return definition.GetSubType() == FieldSubType.OFSTBoolean ? number != 0 : number;
            case FieldType.OFTInteger64:
                return feature.GetFieldAsInteger64(index);
            case FieldType.OFTReal:
                var real = feature.GetFieldAsDouble(index);
                return double.IsFinite(real) ? real : real.ToString(CultureInfo.InvariantCulture);
            case FieldType.OFTIntegerList:
                return feature.GetFieldAsIntegerList(index, out _);
            case FieldType.OFTRealList:
                return feature.GetFieldAsDoubleList(index, out _);
            case FieldType.OFTStringList:
                return feature.GetFieldAsStringList(index);
            case FieldType.OFTDateTime:
                return feature.GetFieldAsISO8601DateTime(index, null);
            case FieldType.OFTBinary:
                return "<binary>";
            default:
                return feature.GetFieldAsString(index);
        }
    }

    public static void Set(Feature feature, int index, FieldDefn definition, JsonElement value)
    {
        var type = definition.GetFieldType();
        switch (value.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                feature.SetFieldNull(index);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                feature.SetField(index, value.GetBoolean() ? 1 : 0);
                break;
            case JsonValueKind.Number when type == FieldType.OFTInteger && value.TryGetInt32(out var int32):
                feature.SetField(index, int32);
                break;
            case JsonValueKind.Number when type is FieldType.OFTInteger64 or FieldType.OFTInteger && value.TryGetInt64(out var int64):
                feature.SetFieldInteger64(index, int64);
                break;
            case JsonValueKind.Number when type is FieldType.OFTString or FieldType.OFTDate or FieldType.OFTTime or FieldType.OFTDateTime:
                feature.SetField(index, value.GetRawText());
                break;
            case JsonValueKind.Number:
                feature.SetField(index, value.GetDouble());
                break;
            case JsonValueKind.String:
                feature.SetField(index, value.GetString());
                break;
            case JsonValueKind.Array when type == FieldType.OFTIntegerList:
                var integers = value.EnumerateArray().Select(item => item.GetInt32()).ToArray();
                feature.SetFieldIntegerList(index, integers.Length, integers);
                break;
            case JsonValueKind.Array when type == FieldType.OFTRealList:
                var reals = value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
                feature.SetFieldDoubleList(index, reals.Length, reals);
                break;
            case JsonValueKind.Array when type == FieldType.OFTStringList:
                feature.SetFieldStringList(index, [.. value.EnumerateArray().Select(item => item.ToString())]);
                break;
            default:
                feature.SetField(index, value.GetRawText());
                break;
        }
    }
}

internal static class Describe
{
    private const int MaxLayersInResult = 25;

    // Returns the georeferenced extent of a raster, or null when the raster has no geotransform.
    public static Bounds? GeoBounds(Dataset dataset, out double[] geoTransform)
    {
        geoTransform = new double[6];
        dataset.GetGeoTransform(geoTransform);
        double[] identity = [0, 1, 0, 0, 0, 1];
        if (geoTransform.SequenceEqual(identity))
        {
            return null;
        }

        var gt = geoTransform;
        double[] xs = [gt[0], gt[0] + gt[1] * dataset.RasterXSize, gt[0] + gt[2] * dataset.RasterYSize, gt[0] + gt[1] * dataset.RasterXSize + gt[2] * dataset.RasterYSize];
        double[] ys = [gt[3], gt[3] + gt[4] * dataset.RasterXSize, gt[3] + gt[5] * dataset.RasterYSize, gt[3] + gt[4] * dataset.RasterXSize + gt[5] * dataset.RasterYSize];
        return new Bounds(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    public static RasterSummary Raster(Dataset dataset)
    {
        var bounds = GeoBounds(dataset, out var gt);
        using var srs = dataset.GetSpatialRef();
        string? dataType = null;
        if (dataset.RasterCount > 0)
        {
            using var band = dataset.GetRasterBand(1);
            dataType = GdalApi.GetDataTypeName(band.DataType);
        }

        return new RasterSummary
        {
            Width = dataset.RasterXSize,
            Height = dataset.RasterYSize,
            BandCount = dataset.RasterCount,
            DataType = dataType,
            Crs = Crs.Summarize(srs),
            Bounds = bounds,
            PixelSize = bounds is null ? null : [Math.Sqrt(gt[1] * gt[1] + gt[4] * gt[4]), Math.Sqrt(gt[2] * gt[2] + gt[5] * gt[5])]
        };
    }

    public static LayerSummary Layer(Layer layer, bool forceCount = false)
    {
        using var srs = layer.GetSpatialRef();
        return new LayerSummary
        {
            Name = layer.GetName(),
            GeometryType = Geometries.TypeName(layer.GetGeomType()),
            FeatureCount = layer.GetFeatureCount(forceCount ? 1 : 0),
            Crs = Crs.Summarize(srs)
        };
    }

    // Reopens a finished output and reports what it contains.
    public static ProcessingResult Output(string output, GdalOperation operation, string? message = null)
    {
        try
        {
            using var dataset = Open.Any(output);
            using var driver = dataset.GetDriver();
            var layers = new List<LayerSummary>();
            for (var i = 0; i < Math.Min(dataset.GetLayerCount(), MaxLayersInResult); i++)
            {
                using var layer = dataset.GetLayer(i);
                layers.Add(Layer(layer));
            }

            return new ProcessingResult
            {
                Output = output,
                Driver = driver.ShortName,
                Raster = dataset.RasterCount > 0 ? Raster(dataset) : null,
                Layers = layers.Count > 0 ? layers : null,
                Message = message,
                Warnings = operation.WarningsOrNull
            };
        }
        catch (Exception ex) when (ex is ApplicationException or McpException)
        {
            // Some outputs, such as a directory of tiles, cannot be reopened as a single dataset.
            return new ProcessingResult { Output = output, Message = message, Warnings = operation.WarningsOrNull };
        }
    }

    public static Dictionary<string, string> KeyValues(string[]? items)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items ?? [])
        {
            var separator = item.IndexOf('=');
            if (separator > 0)
            {
                result[item[..separator]] = item[(separator + 1)..];
            }
            else
            {
                result[item] = string.Empty;
            }
        }

        return result;
    }
}
