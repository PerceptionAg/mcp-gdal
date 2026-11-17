namespace InFarm.Mcp.Gdal.Models;

public sealed record DatasetInfo
{
    public required string Path { get; init; }

    public required string Driver { get; init; }

    public string? DriverLongName { get; init; }

    [Description("Content kinds found in the dataset: raster, vector, or both.")]
    public required IReadOnlyList<string> Kinds { get; init; }

    public IReadOnlyList<string>? Files { get; init; }

    public RasterSummary? Raster { get; init; }

    public int LayerCount { get; init; }

    [Description("Summaries of the first vector layers. Use gdal_vector_info for the full list.")]
    public IReadOnlyList<LayerSummary>? Layers { get; init; }

    public int SubdatasetCount { get; init; }

    public IReadOnlyList<string>? MetadataDomains { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record MetadataResult
{
    public required string Target { get; init; }

    [Description("Metadata domains available on the target.")]
    public IReadOnlyList<string>? Domains { get; init; }

    [Description("The domain that was read. An empty string is the default domain.")]
    public required string Domain { get; init; }

    public IReadOnlyDictionary<string, string>? Items { get; init; }

    [Description("Raw content of an xml: or json: domain.")]
    public string? Text { get; init; }
}

public sealed record SubdatasetInfoModel
{
    public int Index { get; init; }

    [Description("Name to pass as the path of another tool in order to open this subdataset.")]
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public sealed record SubdatasetListResult
{
    public int Total { get; init; }

    public required IReadOnlyList<SubdatasetInfoModel> Subdatasets { get; init; }
}

public sealed record IdentifyResult
{
    public required string Path { get; init; }

    public bool Recognized { get; init; }

    public DriverSummary? Driver { get; init; }
}

public sealed record FileOperationResult
{
    public required string Path { get; init; }

    public required string Action { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}
