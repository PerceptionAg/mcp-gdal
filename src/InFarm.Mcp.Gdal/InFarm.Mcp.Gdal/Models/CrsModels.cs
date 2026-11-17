namespace InFarm.Mcp.Gdal.Models;

public sealed record CrsAxis(string Name, string Direction);

public sealed record AreaOfUseModel(string? Name, double West, double South, double East, double North);

public sealed record CrsMatch(string? Code, string? Name, int Confidence);

public sealed record CrsInfo
{
    public string? Name { get; init; }

    public string? Code { get; init; }

    [Description("One of geographic, projected, geocentric, vertical, compound, local or unknown.")]
    public required string Kind { get; init; }

    [Description("True when coordinates of the system change over time, as with ITRF and WGS 84 realisations.")]
    public bool IsDynamic { get; init; }

    public string? Datum { get; init; }

    public string? Ellipsoid { get; init; }

    public double? SemiMajorAxis { get; init; }

    public double? InverseFlattening { get; init; }

    public string? LinearUnits { get; init; }

    public double? LinearUnitsToMetres { get; init; }

    public string? AngularUnits { get; init; }

    [Description("Axes in the order that the authority defines.")]
    public required IReadOnlyList<CrsAxis> Axes { get; init; }

    [Description("True when the authority lists latitude before longitude, or northing before easting.")]
    public bool AuthorityOrderIsYThenX { get; init; }

    [Description("Explains how coordinate order differs between the authority definition and the traditional GIS order that most files and tools use.")]
    public required string AxisOrderNote { get; init; }

    [Description("UTM zone number when the system is a UTM projection.")]
    public int? UtmZone { get; init; }

    [Description("Hemisphere of the UTM zone: north or south.")]
    public string? UtmHemisphere { get; init; }

    public AreaOfUseModel? AreaOfUse { get; init; }

    public double? CoordinateEpoch { get; init; }

    public string? Wkt2 { get; init; }

    public string? Wkt1 { get; init; }

    public string? EsriWkt { get; init; }

    public string? Proj4 { get; init; }

    public string? ProjJson { get; init; }

    [Description("Database entries that match the definition, with a confidence from 0 to 100.")]
    public IReadOnlyList<CrsMatch>? Matches { get; init; }
}

public sealed record CrsListItem
{
    public required string Code { get; init; }

    public string? Name { get; init; }

    public required string Type { get; init; }

    public bool Deprecated { get; init; }

    public string? AreaName { get; init; }

    [Description("Area of use in longitude and latitude.")]
    public Bounds? AreaBounds { get; init; }

    public string? ProjectionMethod { get; init; }
}

public sealed record CrsLookupResult
{
    public int Total { get; init; }

    public int Offset { get; init; }

    public required IReadOnlyList<CrsListItem> Items { get; init; }
}

public sealed record CrsComparison
{
    [Description("True when the two systems are equivalent, including their axis order.")]
    public bool Equivalent { get; init; }

    [Description("True when the two systems are equivalent apart from the axis order of a geographic system.")]
    public bool EquivalentIgnoringAxisOrder { get; init; }

    public bool SameGeographicCrs { get; init; }

    public bool SameVerticalCrs { get; init; }

    public CrsSummary? First { get; init; }

    public CrsSummary? Second { get; init; }
}

public sealed record TransformedPoint
{
    public double? X { get; init; }

    public double? Y { get; init; }

    public double? Z { get; init; }

    [Description("Set when the point could not be transformed.")]
    public string? Error { get; init; }
}

public sealed record TransformResult
{
    public CrsSummary? SourceCrs { get; init; }

    public CrsSummary? TargetCrs { get; init; }

    [Description("Meaning of the first and second input coordinate.")]
    public required string SourceAxisOrder { get; init; }

    [Description("Meaning of the x and y values of the output.")]
    public required string TargetAxisOrder { get; init; }

    public IReadOnlyList<TransformedPoint>? Points { get; init; }

    public Bounds? Bounds { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

public sealed record UtmZoneResult
{
    public int Zone { get; init; }

    public required string Hemisphere { get; init; }

    public required string Code { get; init; }

    public string? Name { get; init; }

    public double CentralMeridian { get; init; }
}
