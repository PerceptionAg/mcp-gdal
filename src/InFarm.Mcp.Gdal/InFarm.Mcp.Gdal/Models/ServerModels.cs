namespace InFarm.Mcp.Gdal.Models;

public sealed record ServerInfo
{
    public required string ServerVersion { get; init; }

    public required string GdalVersion { get; init; }

    public required string GdalReleaseDate { get; init; }

    public required string ProjVersion { get; init; }

    public required string GeosVersion { get; init; }

    public required string RuntimeIdentifier { get; init; }

    public required string DotnetVersion { get; init; }

    public int DriverCount { get; init; }

    public int RasterDriverCount { get; init; }

    public int VectorDriverCount { get; init; }

    [Description("False when PROJ cannot find proj.db, in which case coordinate reference system tools fail.")]
    public bool ProjDatabaseAvailable { get; init; }

    public IReadOnlyList<string>? ProjSearchPaths { get; init; }

    public bool ProjNetworkEnabled { get; init; }

    public string? GdalDataDirectory { get; init; }

    [Description("Value of GDAL_CACHEMAX. GDAL uses 5% of the physical memory when the value is not set.")]
    public string? CacheSize { get; init; }

    public string? NumThreads { get; init; }

    public bool ReadOnly { get; init; }

    [Description("Directories that tools may access. An empty list means that the local filesystem is unrestricted.")]
    public required IReadOnlyList<string> AllowedRoots { get; init; }

    public bool AllowRemoteAccess { get; init; }

    public bool AllowOverwrite { get; init; }

    public bool AllowSqlWrites { get; init; }

    public bool AllowConfigChanges { get; init; }

    public int DefaultFeatureCount { get; init; }

    public int MaxFeatureCount { get; init; }

    public int MaxGeometryCharacters { get; init; }

    public int MaxResponseCharacters { get; init; }

    public int MaxWindowPixels { get; init; }

    public int OperationTimeoutSeconds { get; init; }

    public int MaxConcurrentOperations { get; init; }

    [Description("Prefixes of the GDAL virtual filesystems that this build provides.")]
    public required IReadOnlyList<string> VirtualFilesystems { get; init; }
}

public sealed record ConfigOptionValue
{
    public required string Name { get; init; }

    [Description("Current value. Values of credential options are replaced with ***.")]
    public string? Value { get; init; }

    public bool IsSet { get; init; }
}

public sealed record DriverSummary
{
    public required string ShortName { get; init; }

    public string? LongName { get; init; }

    public bool Raster { get; init; }

    public bool Vector { get; init; }

    public bool Multidimensional { get; init; }

    public bool CanOpen { get; init; }

    public bool CanCreate { get; init; }

    public bool CanCreateCopy { get; init; }

    public bool VirtualIo { get; init; }

    public IReadOnlyList<string>? Extensions { get; init; }
}

public sealed record DriverListResult
{
    public int Total { get; init; }

    public int Offset { get; init; }

    public required IReadOnlyList<DriverSummary> Drivers { get; init; }
}

public sealed record DriverInfo
{
    public required DriverSummary Summary { get; init; }

    public string? HelpUrl { get; init; }

    public string? MimeType { get; init; }

    public string? ConnectionPrefix { get; init; }

    [Description("Raster data types that the driver can create.")]
    public IReadOnlyList<string>? CreationDataTypes { get; init; }

    [Description("Vector field types that the driver can create.")]
    public IReadOnlyList<string>? CreationFieldDataTypes { get; init; }

    public IReadOnlyList<string>? CreationFieldSubTypes { get; init; }

    public IReadOnlyList<string>? SqlDialects { get; init; }

    [Description("Driver capability flags such as DCAP_CREATE_LAYER, with their values.")]
    public IReadOnlyDictionary<string, string>? Capabilities { get; init; }

    [Description("Dataset creation options, passed as creationOptions in NAME=VALUE form.")]
    public IReadOnlyList<OptionInfo>? CreationOptions { get; init; }

    [Description("Layer creation options of a vector driver, passed as layerCreationOptions in NAME=VALUE form.")]
    public IReadOnlyList<OptionInfo>? LayerCreationOptions { get; init; }

    [Description("Options that apply when the driver opens a dataset, passed as openOptions in NAME=VALUE form.")]
    public IReadOnlyList<OptionInfo>? OpenOptions { get; init; }

    public IReadOnlyList<OptionInfo>? OverviewCreationOptions { get; init; }

    [Description("Remaining driver metadata items.")]
    public IReadOnlyDictionary<string, string>? OtherMetadata { get; init; }
}
