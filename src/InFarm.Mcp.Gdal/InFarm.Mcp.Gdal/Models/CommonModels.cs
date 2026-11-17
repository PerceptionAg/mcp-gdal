namespace InFarm.Mcp.Gdal.Models;

public sealed record Bounds(double MinX, double MinY, double MaxX, double MaxY);

public sealed record CrsSummary
{
    [Description("Name of the coordinate reference system.")]
    public string? Name { get; init; }

    [Description("Authority code such as EPSG:4326, when the system has one.")]
    public string? Code { get; init; }

    [Description("One of geographic, projected, geocentric, vertical, compound, local or unknown.")]
    public string? Kind { get; init; }

    [Description("Name of the linear unit for projected systems or the angular unit for geographic systems.")]
    public string? Units { get; init; }

    public string? Wkt { get; init; }
}

public sealed record RasterSummary
{
    public int Width { get; init; }

    public int Height { get; init; }

    public int BandCount { get; init; }

    [Description("GDAL data type of the first band.")]
    public string? DataType { get; init; }

    public CrsSummary? Crs { get; init; }

    [Description("Georeferenced extent in the dataset coordinate reference system.")]
    public Bounds? Bounds { get; init; }

    [Description("Pixel width and pixel height in georeferenced units.")]
    public double[]? PixelSize { get; init; }
}

public sealed record LayerSummary
{
    public required string Name { get; init; }

    public string? GeometryType { get; init; }

    [Description("Number of features. The value is -1 when the driver cannot report the count without scanning the layer.")]
    public long FeatureCount { get; init; }

    public CrsSummary? Crs { get; init; }
}

public sealed record ProcessingResult
{
    [Description("Path of the dataset that the operation created or modified.")]
    public required string Output { get; init; }

    [Description("Short name of the GDAL driver of the output.")]
    public string? Driver { get; init; }

    public RasterSummary? Raster { get; init; }

    public IReadOnlyList<LayerSummary>? Layers { get; init; }

    public string? Message { get; init; }

    [Description("Warnings that GDAL raised while the operation ran.")]
    public IReadOnlyList<string>? Warnings { get; init; }
}

public sealed record OptionInfo
{
    public required string Name { get; init; }

    public string? Type { get; init; }

    public string? Description { get; init; }

    public string? Default { get; init; }

    public string? Scope { get; init; }

    public string? Min { get; init; }

    public string? Max { get; init; }

    public IReadOnlyList<string>? Values { get; init; }
}
