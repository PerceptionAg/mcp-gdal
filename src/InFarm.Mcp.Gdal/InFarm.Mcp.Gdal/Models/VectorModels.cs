namespace InFarm.Mcp.Gdal.Models;

public sealed record FieldInfo
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public string? SubType { get; init; }

    public int? Width { get; init; }

    public int? Precision { get; init; }

    public bool Nullable { get; init; }

    public bool Unique { get; init; }

    public string? Default { get; init; }

    public string? AlternativeName { get; init; }

    public string? Comment { get; init; }

    public string? Domain { get; init; }
}

public sealed record GeometryFieldInfo
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Nullable { get; init; }

    public CrsSummary? Crs { get; init; }
}

public sealed record LayerInfo
{
    public required string Name { get; init; }

    public required string GeometryType { get; init; }

    [Description("Number of features. The value is -1 when the driver cannot report the count without scanning the layer.")]
    public long FeatureCount { get; init; }

    public Bounds? Extent { get; init; }

    public CrsSummary? Crs { get; init; }

    public string? FidColumn { get; init; }

    public string? GeometryColumn { get; init; }

    public IReadOnlyList<FieldInfo>? Fields { get; init; }

    public IReadOnlyList<GeometryFieldInfo>? GeometryFields { get; init; }

    [Description("Capabilities that the layer supports, such as RandomWrite, CreateField, Transactions and FastSpatialFilter.")]
    public IReadOnlyList<string>? Capabilities { get; init; }
}

public sealed record VectorInfo
{
    public required string Path { get; init; }

    public required string Driver { get; init; }

    public int LayerCount { get; init; }

    public required IReadOnlyList<LayerInfo> Layers { get; init; }

    public IReadOnlyList<string>? FieldDomains { get; init; }

    public IReadOnlyList<string>? Relationships { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record FeatureModel
{
    public long Fid { get; init; }

    public IReadOnlyDictionary<string, object?>? Properties { get; init; }

    [Description("Geometry text in the requested format. Null when the feature has no geometry, when no geometry was requested or when the text exceeds the size limit.")]
    public string? Geometry { get; init; }

    public string? GeometryType { get; init; }

    public Bounds? Envelope { get; init; }

    [Description("True when the geometry was left out because its text exceeds the size limit. The envelope is still returned.")]
    public bool? GeometryOmitted { get; init; }
}

public sealed record FeaturePage
{
    public required string Layer { get; init; }

    public int Returned { get; init; }

    public long Offset { get; init; }

    [Description("Offset to pass in order to read the next page. Null when no further features exist.")]
    public long? NextOffset { get; init; }

    [Description("Number of features that match the filters, when it was requested.")]
    public long? TotalMatching { get; init; }

    public CrsSummary? Crs { get; init; }

    public required IReadOnlyList<FeatureModel> Features { get; init; }

    [Description("True when the page ended early because the response size limit was reached.")]
    public bool SizeLimited { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

public sealed record FeatureCountResult
{
    public required string Layer { get; init; }

    public long FeatureCount { get; init; }

    [Description("Extent of the features that match the filters.")]
    public Bounds? Extent { get; init; }

    public CrsSummary? Crs { get; init; }
}

public sealed record FeatureWriteResult
{
    public required string Path { get; init; }

    public required string Layer { get; init; }

    public int Inserted { get; init; }

    public int Updated { get; init; }

    public long Deleted { get; init; }

    [Description("Identifiers of the features that were inserted or updated.")]
    public IReadOnlyList<long>? Fids { get; init; }

    public long FeatureCount { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

public sealed record InvalidFeature
{
    public long Fid { get; init; }

    public required string Problem { get; init; }

    public string? GeometryType { get; init; }
}

public sealed record ValidationResult
{
    public required string Layer { get; init; }

    public long Checked { get; init; }

    public long InvalidCount { get; init; }

    public long EmptyOrNullCount { get; init; }

    [Description("The first invalid features, up to the requested limit.")]
    public required IReadOnlyList<InvalidFeature> Invalid { get; init; }
}

public sealed record GeometryInfo
{
    public required string Type { get; init; }

    [Description("Topological dimension: 0 for points, 1 for lines and 2 for polygons.")]
    public int Dimension { get; init; }

    public bool IsEmpty { get; init; }

    public bool IsValid { get; init; }

    public string? InvalidityReason { get; init; }

    public bool IsSimple { get; init; }

    public bool Is3D { get; init; }

    public bool IsMeasured { get; init; }

    public int PartCount { get; init; }

    public long PointCount { get; init; }

    [Description("Planar area in squared units of the coordinate reference system.")]
    public double Area { get; init; }

    [Description("Planar length or perimeter in units of the coordinate reference system.")]
    public double Length { get; init; }

    [Description("Area on the ellipsoid in square metres. Reported when a coordinate reference system is given.")]
    public double? GeodesicArea { get; init; }

    [Description("Length on the ellipsoid in metres. Reported when a coordinate reference system is given.")]
    public double? GeodesicLength { get; init; }

    public Bounds? Envelope { get; init; }

    public string? Centroid { get; init; }
}

public sealed record GeometryResult
{
    [Description("Result geometry in the requested format.")]
    public string? Geometry { get; init; }

    public required string Type { get; init; }

    public bool IsEmpty { get; init; }

    public double Area { get; init; }

    public double Length { get; init; }

    public Bounds? Envelope { get; init; }

    public bool? GeometryOmitted { get; init; }
}

public sealed record GeometryRelation
{
    public bool AreEqual { get; init; }

    public bool Intersects { get; init; }

    public bool Disjoint { get; init; }

    public bool Touches { get; init; }

    public bool Crosses { get; init; }

    public bool Within { get; init; }

    public bool Contains { get; init; }

    public bool Overlaps { get; init; }

    [Description("Shortest planar distance between the two geometries in units of the coordinate reference system.")]
    public double Distance { get; init; }
}
