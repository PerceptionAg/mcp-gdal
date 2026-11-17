namespace InFarm.Mcp.Gdal.Models;

public sealed record RasterInfo
{
    public required string Path { get; init; }

    public required string Driver { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int BandCount { get; init; }

    public CrsSummary? Crs { get; init; }

    [Description("Affine geotransform [originX, pixelWidth, rowRotation, originY, columnRotation, pixelHeight]. Null when the raster is not georeferenced.")]
    public double[]? GeoTransform { get; init; }

    [Description("Extent in the raster coordinate reference system.")]
    public Bounds? Bounds { get; init; }

    [Description("Extent in WGS 84 longitude and latitude.")]
    public Bounds? Wgs84Bounds { get; init; }

    public double[]? PixelSize { get; init; }

    public int GcpCount { get; init; }

    public int SubdatasetCount { get; init; }

    public IReadOnlyList<string>? Files { get; init; }

    public IReadOnlyList<string>? MetadataDomains { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    [Description("Items of the IMAGE_STRUCTURE domain, such as COMPRESSION, INTERLEAVE and LAYOUT.")]
    public IReadOnlyDictionary<string, string>? ImageStructure { get; init; }

    public required IReadOnlyList<BandInfo> Bands { get; init; }

    [Description("True when the raster has more bands than this response describes.")]
    public bool BandsTruncated { get; init; }
}

public sealed record BandInfo
{
    public int Band { get; init; }

    public required string DataType { get; init; }

    public string? ColorInterpretation { get; init; }

    public string? Description { get; init; }

    public int BlockWidth { get; init; }

    public int BlockHeight { get; init; }

    public bool HasNoData { get; init; }

    [Description("NoData value. Null when the band has none or when the value is NaN, in which case noDataText holds it.")]
    public double? NoData { get; init; }

    public string? NoDataText { get; init; }

    public double? Scale { get; init; }

    public double? Offset { get; init; }

    public string? Unit { get; init; }

    public int OverviewCount { get; init; }

    [Description("Width and height of each overview level.")]
    public IReadOnlyList<int[]>? OverviewSizes { get; init; }

    [Description("Mask flags: all_valid, per_dataset, alpha or nodata.")]
    public IReadOnlyList<string>? MaskFlags { get; init; }

    public int? ColorTableEntries { get; init; }

    public bool HasAttributeTable { get; init; }

    public IReadOnlyList<string>? CategoryNames { get; init; }

    public BandStatistics? Statistics { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record BandStatistics
{
    public int Band { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Mean { get; init; }

    public double? StdDev { get; init; }

    [Description("Percentage of pixels that are not NoData, when GDAL reports it.")]
    public double? ValidPercent { get; init; }

    [Description("True when the statistics come from overviews or a sample of the blocks.")]
    public bool Approximate { get; init; }
}

public sealed record StatisticsResult
{
    public required string Path { get; init; }

    public required IReadOnlyList<BandStatistics> Bands { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

public sealed record HistogramResult
{
    public int Band { get; init; }

    public double Min { get; init; }

    public double Max { get; init; }

    public int BucketCount { get; init; }

    public double BucketWidth { get; init; }

    public required int[] Counts { get; init; }
}

public sealed record PixelSample
{
    [Description("Input x coordinate.")]
    public double X { get; init; }

    [Description("Input y coordinate.")]
    public double Y { get; init; }

    [Description("Column in pixel space, where 0 is the left edge of the raster.")]
    public double Pixel { get; init; }

    [Description("Row in pixel space, where 0 is the top edge of the raster.")]
    public double Line { get; init; }

    public bool InBounds { get; init; }

    [Description("One value for each requested band. A value is null when the point lies outside the raster.")]
    public IReadOnlyList<double?>? Values { get; init; }
}

public sealed record PixelValuesResult
{
    public required IReadOnlyList<int> Bands { get; init; }

    public required IReadOnlyList<PixelSample> Samples { get; init; }
}

public sealed record WindowResult
{
    public int Band { get; init; }

    public int XOffset { get; init; }

    public int YOffset { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    public double? NoData { get; init; }

    [Description("Pixel values as rows from top to bottom. Non-finite values are returned as null.")]
    public required IReadOnlyList<double?[]> Rows { get; init; }
}

public sealed record ColorEntryModel(int Index, int C1, int C2, int C3, int C4);

public sealed record ColorTableResult
{
    public int Band { get; init; }

    public required string PaletteInterpretation { get; init; }

    public int Total { get; init; }

    public required IReadOnlyList<ColorEntryModel> Entries { get; init; }
}

public sealed record AttributeTableColumn(string Name, string Type, string Usage);

public sealed record AttributeTableResult
{
    public int Band { get; init; }

    public required string TableType { get; init; }

    public int RowCount { get; init; }

    public required IReadOnlyList<AttributeTableColumn> Columns { get; init; }

    [Description("Rows of values in column order.")]
    public required IReadOnlyList<object?[]> Rows { get; init; }
}

public sealed record GcpModel(string? Id, string? Info, double Pixel, double Line, double X, double Y, double Z);

public sealed record GcpResult
{
    public int Total { get; init; }

    public CrsSummary? Crs { get; init; }

    public required IReadOnlyList<GcpModel> Gcps { get; init; }
}
