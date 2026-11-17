using System.Xml.Linq;

namespace InFarm.Mcp.Gdal.Tools;

[McpServerToolType]
public sealed class DriverTools(GdalToolContext context)
{
    private static readonly HashSet<string> StructuredItems = new(StringComparer.Ordinal)
    {
        GdalConst.DMD_LONGNAME, GdalConst.DMD_HELPTOPIC, GdalConst.DMD_MIMETYPE, GdalConst.DMD_EXTENSION, GdalConst.DMD_EXTENSIONS,
        GdalConst.DMD_CONNECTION_PREFIX, GdalConst.DMD_CREATIONDATATYPES, GdalConst.DMD_CREATIONFIELDDATATYPES,
        GdalConst.DMD_CREATIONFIELDDATASUBTYPES, GdalConst.DMD_SUPPORTED_SQL_DIALECTS, GdalConst.DMD_CREATIONOPTIONLIST,
        GdalConst.DMD_OPENOPTIONLIST, GdalConst.DMD_OVERVIEW_CREATIONOPTIONLIST, "DS_LAYER_CREATIONOPTIONLIST"
    };

    [McpServerTool(Name = "gdal_driver_list", Title = "List GDAL drivers", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Lists the registered GDAL raster and vector drivers (formats) with their read, create and virtual I/O capabilities and file extensions. " +
        "Equivalent to 'gdalinfo --formats' and 'ogrinfo --formats'.")]
    public Task<DriverListResult> ListAsync(
        [Description("Kind of driver to return: all, raster, vector or multidimensional.")] string kind = "all",
        [Description("Returns only drivers that can create new datasets, either directly or by copying.")] bool writableOnly = false,
        [Description("Returns only drivers that handle this file extension, for example tif or gpkg.")] string? extension = null,
        [Description("Returns only drivers whose short or long name contains this text.")] string? nameContains = null,
        [Description("Number of drivers to skip.")] int offset = 0,
        [Description("Maximum number of drivers to return.")] int limit = 300,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_driver_list", cancellationToken, _ =>
        {
            var wantedExtension = extension?.TrimStart('.');
            var matches = new List<DriverSummary>();
            for (var i = 0; i < GdalApi.GetDriverCount(); i++)
            {
                using var driver = GdalApi.GetDriver(i);
                var summary = Summarize(driver);
                var kindMatches = kind.ToLowerInvariant() switch
                {
                    "raster" => summary.Raster,
                    "vector" => summary.Vector,
                    "multidimensional" => summary.Multidimensional,
                    "all" => true,
                    _ => throw new McpException("The kind must be one of all, raster, vector or multidimensional.")
                };

                if (kindMatches
                    && (!writableOnly || summary.CanCreate || summary.CanCreateCopy)
                    && (wantedExtension is null || (summary.Extensions?.Contains(wantedExtension, StringComparer.OrdinalIgnoreCase) ?? false))
                    && (nameContains is null
                        || summary.ShortName.Contains(nameContains, StringComparison.OrdinalIgnoreCase)
                        || (summary.LongName?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) ?? false)))
                {
                    matches.Add(summary);
                }
            }

            return new DriverListResult
            {
                Total = matches.Count,
                Offset = offset,
                Drivers = [.. matches.OrderBy(driver => driver.ShortName, StringComparer.OrdinalIgnoreCase).Skip(offset).Take(limit)]
            };
        });
    }

    [McpServerTool(Name = "gdal_driver_info", Title = "Describe a GDAL driver", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Returns the capabilities, file extensions, MIME type, supported data types, creation options, layer creation options and open options of one GDAL driver. " +
        "Call this tool before a conversion to learn which options, compression methods and data types the output format accepts.")]
    public Task<DriverInfo> InfoAsync(
        [Description("Short name of the driver, for example GTiff, COG, GPKG or FlatGeobuf.")] string name,
        [Description("Includes the creation, layer creation and open option lists.")] bool includeOptions = true,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_driver_info", cancellationToken, _ =>
        {
            using var driver = GetDriver(name);
            var metadata = Describe.KeyValues(driver.GetMetadata(null));
            return new DriverInfo
            {
                Summary = Summarize(driver),
                HelpUrl = metadata.TryGetValue(GdalConst.DMD_HELPTOPIC, out var help) ? $"https://gdal.org/{help}" : null,
                MimeType = metadata.GetValueOrDefault(GdalConst.DMD_MIMETYPE),
                ConnectionPrefix = metadata.GetValueOrDefault(GdalConst.DMD_CONNECTION_PREFIX),
                CreationDataTypes = SplitList(metadata.GetValueOrDefault(GdalConst.DMD_CREATIONDATATYPES)),
                CreationFieldDataTypes = SplitList(metadata.GetValueOrDefault(GdalConst.DMD_CREATIONFIELDDATATYPES)),
                CreationFieldSubTypes = SplitList(metadata.GetValueOrDefault(GdalConst.DMD_CREATIONFIELDDATASUBTYPES)),
                SqlDialects = SplitList(metadata.GetValueOrDefault(GdalConst.DMD_SUPPORTED_SQL_DIALECTS)),
                Capabilities = metadata.Where(item => item.Key.StartsWith("DCAP_", StringComparison.Ordinal)).ToDictionary(),
                CreationOptions = includeOptions ? ParseOptions(metadata.GetValueOrDefault(GdalConst.DMD_CREATIONOPTIONLIST)) : null,
                LayerCreationOptions = includeOptions ? ParseOptions(metadata.GetValueOrDefault("DS_LAYER_CREATIONOPTIONLIST")) : null,
                OpenOptions = includeOptions ? ParseOptions(metadata.GetValueOrDefault(GdalConst.DMD_OPENOPTIONLIST)) : null,
                OverviewCreationOptions = includeOptions ? ParseOptions(metadata.GetValueOrDefault(GdalConst.DMD_OVERVIEW_CREATIONOPTIONLIST)) : null,
                OtherMetadata = metadata
                    .Where(item => !item.Key.StartsWith("DCAP_", StringComparison.Ordinal) && !StructuredItems.Contains(item.Key) && !item.Key.EndsWith("OPTIONLIST", StringComparison.Ordinal))
                    .ToDictionary()
            };
        });
    }

    internal static GdalDriver GetDriver(string name)
    {
        return GdalApi.GetDriverByName(name)
            ?? throw new McpException($"The GDAL driver '{name}' is not available on this server. Use gdal_driver_list to see the registered drivers.");
    }

    // Parses the XML option list that GDAL drivers publish in their metadata.
    internal static IReadOnlyList<OptionInfo>? ParseOptions(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            return [.. XElement.Parse(xml).Elements("Option").Select(option => new OptionInfo
            {
                Name = (string?)option.Attribute("name") ?? string.Empty,
                Type = (string?)option.Attribute("type"),
                Description = (string?)option.Attribute("description"),
                Default = (string?)option.Attribute("default"),
                Scope = (string?)option.Attribute("scope"),
                Min = (string?)option.Attribute("min"),
                Max = (string?)option.Attribute("max"),
                Values = option.Elements("Value").Any() ? [.. option.Elements("Value").Select(value => value.Value)] : null
            })];
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static DriverSummary Summarize(GdalDriver driver)
    {
        bool Has(string item)
        {
            return driver.GetMetadataItem(item, null) == "YES";
        }

        return new DriverSummary
        {
            ShortName = driver.ShortName,
            LongName = driver.LongName,
            Raster = Has(GdalConst.DCAP_RASTER),
            Vector = Has(GdalConst.DCAP_VECTOR),
            Multidimensional = Has(GdalConst.DCAP_MULTIDIM_RASTER),
            CanOpen = Has(GdalConst.DCAP_OPEN),
            CanCreate = Has(GdalConst.DCAP_CREATE),
            CanCreateCopy = Has(GdalConst.DCAP_CREATECOPY),
            VirtualIo = Has(GdalConst.DCAP_VIRTUALIO),
            Extensions = SplitList(driver.GetMetadataItem(GdalConst.DMD_EXTENSIONS, null) ?? driver.GetMetadataItem(GdalConst.DMD_EXTENSION, null))
        };
    }

    private static IReadOnlyList<string>? SplitList(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
