namespace InFarm.Mcp.Gdal.Tools;

// Operations on geometries that are passed as text, without reading or writing a dataset.
[McpServerToolType]
public sealed class GeometryTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_geometry_info", Title = "Measure and validate a geometry", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Measures a geometry given as WKT or GeoJSON text: type, validity with the reason, area, length, vertex count, envelope and centroid. " +
        "Pass crs to also get the geodesic area in square metres and the geodesic length in metres on the ellipsoid.")]
    public Task<GeometryInfo> InfoAsync(
        [Description("Geometry as WKT or GeoJSON geometry text.")] string geometry,
        [Description("Coordinate reference system of the geometry, in longitude/easting then latitude/northing order.")] string? crs = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_geometry_info", cancellationToken, operation =>
        {
            using var srs = crs is null ? null : Crs.Parse(crs);
            using var parsed = Geometries.Parse(geometry, srs);
            var isEmpty = parsed.IsEmpty();
            var isValid = parsed.IsValid();
            using var centroid = isEmpty ? null : parsed.Centroid();
            string? centroidText = null;
            centroid?.ExportToWkt(out centroidText);
            double? geodesicArea = null;
            double? geodesicLength = null;
            if (srs is not null && !isEmpty)
            {
                geodesicArea = parsed.GetDimension() == 2 ? Num.Finite(parsed.GeodesicArea()) : null;
                geodesicLength = parsed.GetDimension() >= 1 ? Num.Finite(parsed.GeodesicLength()) : null;
            }

            // GEOS reports an invalid geometry as a warning, which would repeat the structured result.
            operation.Warnings.Clear();
            return new GeometryInfo
            {
                Type = Geometries.TypeName(parsed.GetGeometryType()),
                Dimension = parsed.GetDimension(),
                IsEmpty = isEmpty,
                IsValid = isValid,
                InvalidityReason = isValid ? null : parsed.GetInvalidityReason(),
                IsSimple = parsed.IsSimple(),
                Is3D = parsed.Is3D() != 0,
                IsMeasured = parsed.IsMeasured() != 0,
                PartCount = Math.Max(1, parsed.GetGeometryCount()),
                PointCount = GeometryOps.CountPoints(parsed),
                Area = parsed.Area(),
                Length = parsed.Length(),
                GeodesicArea = geodesicArea,
                GeodesicLength = geodesicLength,
                Envelope = Geometries.Envelope(parsed),
                Centroid = centroidText
            };
        });
    }

    [McpServerTool(Name = "gdal_geometry_operation", Title = "Compute a geometry from one or two geometries", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Applies a geometry operation to geometries given as WKT or GeoJSON text and returns the resulting geometry. Operations on one geometry are " +
        GeometryOps.UnaryNames + ". Operations on two geometries are union, intersection, difference and sym_difference. " +
        "The operation 'transform' reprojects the geometry from crs to targetCrs. Use 'convert' to change only the text format.")]
    public Task<GeometryResult> OperationAsync(
        [Description("Operation to apply.")] string operation,
        [Description("Geometry as WKT or GeoJSON geometry text.")] string geometry,
        [Description("Second geometry for union, intersection, difference and sym_difference.")] string? other = null,
        [Description("Distance for buffer and segmentize.")] double? distance = null,
        [Description("Tolerance for simplify, simplify_preserve_topology, set_precision and delaunay.")] double? tolerance = null,
        [Description("Ratio between 0 and 1 for concave_hull.")] double? ratio = null,
        [Description("Allows holes in a concave hull.")] bool allowHoles = false,
        [Description("Target geometry type for force_type.")] string? targetType = null,
        [Description("Options of the operation in NAME=VALUE form.")] string[]? options = null,
        [Description("Coordinate reference system of the input geometries, in longitude/easting then latitude/northing order.")] string? crs = null,
        [Description("Target coordinate reference system for the transform operation.")] string? targetCrs = null,
        [Description("Format of the returned geometry: wkt or geojson.")] GeometryFormat outputFormat = GeometryFormat.Wkt,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_geometry_operation", cancellationToken, _ =>
        {
            using var srs = crs is null ? null : Crs.Parse(crs);
            using var first = Geometries.Parse(geometry, srs);
            var name = operation.Trim().ToLowerInvariant();
            Geometry? result;
            if (name == "convert")
            {
                result = first.Clone();
            }
            else if (name == "transform")
            {
                using var target = Crs.Parse(targetCrs ?? throw new McpException("The transform operation needs targetCrs."));
                if (srs is null)
                {
                    throw new McpException("The transform operation needs crs, the coordinate reference system of the input geometry.");
                }

                using var transformation = new CoordinateTransformation(srs, target);
                result = first.Clone();
                result.Transform(transformation);
            }
            else if (GeometryOps.IsBinary(name))
            {
                using var second = Geometries.Parse(other ?? throw new McpException($"The {name} operation needs a second geometry in 'other'."), srs);
                result = GeometryOps.ApplyBinary(name, first, second);
            }
            else
            {
                result = GeometryOps.Apply(name, first, new GeometryOpParameters
                {
                    Distance = distance,
                    Tolerance = tolerance,
                    Ratio = ratio,
                    AllowHoles = allowHoles,
                    TargetType = targetType,
                    Options = options
                });
            }

            using (result ?? throw new McpException($"The {name} operation produced no geometry. Check the validity of the input with gdal_geometry_info."))
            {
                var text = Geometries.Format(
                    result, outputFormat == GeometryFormat.Geojson ? GeometryFormat.Geojson : GeometryFormat.Wkt, context.Options.MaxResponseCharacters, out var omitted);
                return new GeometryResult
                {
                    Geometry = text,
                    Type = Geometries.TypeName(result.GetGeometryType()),
                    IsEmpty = result.IsEmpty(),
                    Area = result.Area(),
                    Length = result.Length(),
                    Envelope = Geometries.Envelope(result),
                    GeometryOmitted = omitted ? true : null
                };
            }
        });
    }

    [McpServerTool(Name = "gdal_geometry_relate", Title = "Test the spatial relationship of two geometries", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates the spatial predicates equals, intersects, disjoint, touches, crosses, within, contains and overlaps between two geometries, and their distance.")]
    public Task<GeometryRelation> RelateAsync(
        [Description("First geometry as WKT or GeoJSON geometry text.")] string geometry,
        [Description("Second geometry as WKT or GeoJSON geometry text.")] string other,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_geometry_relate", cancellationToken, _ =>
        {
            using var first = Geometries.Parse(geometry);
            using var second = Geometries.Parse(other);
            return new GeometryRelation
            {
                AreEqual = first.Equals(second),
                Intersects = first.Intersects(second),
                Disjoint = first.Disjoint(second),
                Touches = first.Touches(second),
                Crosses = first.Crosses(second),
                Within = first.Within(second),
                Contains = first.Contains(second),
                Overlaps = first.Overlaps(second),
                Distance = first.Distance(second)
            };
        });
    }
}
