namespace InFarm.Mcp.Gdal.Infrastructure;

internal sealed record GeometryOpParameters
{
    public double? Distance { get; init; }

    public double? Tolerance { get; init; }

    public double? Ratio { get; init; }

    public bool AllowHoles { get; init; }

    public string? TargetType { get; init; }

    public IReadOnlyList<string>? Options { get; init; }
}

// Geometry operations that take one geometry, shared by the layer processing tools and the standalone geometry tools.
internal static class GeometryOps
{
    public const string UnaryNames =
        "buffer, simplify, simplify_preserve_topology, make_valid, convex_hull, concave_hull, centroid, point_on_surface, boundary, envelope, " +
        "segmentize, flatten_2d, swap_xy, force_type, delaunay, normalize, unary_union, build_area, polygonize, set_precision, to_linear, " +
        "to_curve, remove_lower_dimension and close_rings";

    // Returns the geometry type of the results of an operation, or null when the type depends on each input geometry.
    public static wkbGeometryType? ResultType(string operation, wkbGeometryType sourceType, GeometryOpParameters parameters)
    {
        return Normalize(operation) switch
        {
            "centroid" or "point_on_surface" => wkbGeometryType.wkbPoint,
            "simplify" or "simplify_preserve_topology" or "segmentize" or "swap_xy" or "normalize" or "set_precision" or "close_rings" => sourceType,
            "force_type" => Geometries.ParseType(parameters.TargetType ?? throw new McpException("The force_type operation needs a target geometry type.")),
            _ => null
        };
    }

    // Applies an operation and returns a new geometry that the caller owns, or null when GDAL produces no result.
    public static Geometry? Apply(string operation, Geometry geometry, GeometryOpParameters parameters)
    {
        switch (Normalize(operation))
        {
            case "buffer":
                return geometry.Buffer(Required(parameters.Distance, "distance", operation), parameters.Options?.ToArray() ?? ["QUADRANT_SEGMENTS=8"]);
            case "simplify":
                return geometry.Simplify(Required(parameters.Tolerance, "tolerance", operation));
            case "simplify_preserve_topology":
                return geometry.SimplifyPreserveTopology(Required(parameters.Tolerance, "tolerance", operation));
            case "make_valid":
                return geometry.MakeValid(parameters.Options?.ToArray() ?? []);
            case "convex_hull":
                return geometry.ConvexHull();
            case "concave_hull":
                return geometry.ConcaveHull(parameters.Ratio ?? 0.5, parameters.AllowHoles);
            case "centroid":
                return geometry.Centroid();
            case "point_on_surface":
                return geometry.PointOnSurface();
            case "boundary":
                return geometry.Boundary();
            case "envelope":
                return EnvelopePolygon(geometry);
            case "delaunay":
                return geometry.DelaunayTriangulation(parameters.Tolerance ?? 0, 0);
            case "normalize":
                return geometry.Normalize();
            case "unary_union":
                return geometry.UnaryUnion();
            case "build_area":
                return geometry.BuildArea();
            case "polygonize":
                return geometry.Polygonize();
            case "set_precision":
                return geometry.SetPrecision(Required(parameters.Tolerance, "tolerance", operation), 0);
            case "to_linear":
                return geometry.GetLinearGeometry(0, parameters.Options?.ToArray() ?? []);
            case "to_curve":
                return geometry.GetCurveGeometry(parameters.Options?.ToArray() ?? []);
            case "remove_lower_dimension":
                return geometry.RemoveLowerDimensionSubGeoms();
            case "force_type":
                var type = Geometries.ParseType(parameters.TargetType ?? throw new McpException("The force_type operation needs a target geometry type."));
                return Ogr.ForceTo(geometry, type, parameters.Options?.ToArray() ?? []);
            case "segmentize":
                var segmented = geometry.Clone();
                segmented.Segmentize(Required(parameters.Distance, "distance", operation));
                return segmented;
            case "flatten_2d":
                var flattened = geometry.Clone();
                flattened.FlattenTo2D();
                return flattened;
            case "swap_xy":
                var swapped = geometry.Clone();
                swapped.SwapXY();
                return swapped;
            case "close_rings":
                var closed = geometry.Clone();
                closed.CloseRings();
                return closed;
            default:
                throw new McpException($"'{operation}' is not a geometry operation. The operations are {UnaryNames}.");
        }
    }

    public static Geometry? ApplyBinary(string operation, Geometry left, Geometry right)
    {
        return Normalize(operation) switch
        {
            "union" => left.Union(right),
            "intersection" => left.Intersection(right),
            "difference" => left.Difference(right),
            "sym_difference" or "symmetric_difference" => left.SymDifference(right),
            _ => throw new McpException($"'{operation}' is not a binary geometry operation. The operations are union, intersection, difference and sym_difference.")
        };
    }

    public static bool IsBinary(string operation)
    {
        return Normalize(operation) is "union" or "intersection" or "difference" or "sym_difference" or "symmetric_difference";
    }

    // Counts the vertices of a geometry, including the vertices of its parts.
    public static long CountPoints(Geometry geometry)
    {
        var parts = geometry.GetGeometryCount();
        if (parts == 0)
        {
            return geometry.GetPointCount();
        }

        long total = 0;
        for (var i = 0; i < parts; i++)
        {
            using var part = geometry.GetGeometryRef(i);
            total += CountPoints(part);
        }

        return total;
    }

    private static string Normalize(string operation)
    {
        return operation.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }

    private static double Required(double? value, string name, string operation)
    {
        return value ?? throw new McpException($"The {operation} operation needs the parameter '{name}'.");
    }

    private static Geometry? EnvelopePolygon(Geometry geometry)
    {
        if (geometry.IsEmpty())
        {
            return null;
        }

        using var envelope = new Envelope();
        geometry.GetEnvelope(envelope);
        var wkt = string.Create(
            CultureInfo.InvariantCulture,
            $"POLYGON (({envelope.MinX:R} {envelope.MinY:R},{envelope.MaxX:R} {envelope.MinY:R},{envelope.MaxX:R} {envelope.MaxY:R},{envelope.MinX:R} {envelope.MaxY:R},{envelope.MinX:R} {envelope.MinY:R}))");
        var polygon = Geometry.CreateFromWkt(wkt);
        using var srs = geometry.GetSpatialReference();
        if (srs is not null)
        {
            polygon.AssignSpatialReference(srs);
        }

        return polygon;
    }
}
