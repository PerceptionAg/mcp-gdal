namespace InFarm.Mcp.Gdal.Tools;

// Coordinate reference system tools built on OSR and PROJ.
[McpServerToolType]
public sealed class ProjectionTools(GdalToolContext context)
{
    private const string TraditionalOrderText = "first = longitude or easting (x), second = latitude or northing (y)";

    [McpServerTool(Name = "gdal_crs_info", Title = "Describe a coordinate reference system", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Parses a coordinate reference system given as an authority code such as EPSG:4326, WKT, a PROJ string, PROJJSON or a name, and returns its kind, datum, units, " +
        "axes with the authority axis order, UTM zone, area of use and its definition in the requested formats. Pass identify=true to find the matching EPSG entries of a WKT or PROJ definition.")]
    public Task<CrsInfo> InfoAsync(
        [Description("Coordinate reference system definition.")] string crs,
        [Description("Definition formats to include: wkt2, wkt1, esri, proj4 and projjson.")] string[]? formats = null,
        [Description("Searches the PROJ database for entries that match the definition.")] bool identify = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_info", cancellationToken, _ =>
        {
            using var srs = Crs.Parse(crs, false);
            var wanted = new HashSet<string>(formats ?? [], StringComparer.OrdinalIgnoreCase);
            var axes = new List<CrsAxis>();
            for (var i = 0; i < srs.GetAxesCount(); i++)
            {
                axes.Add(new CrsAxis(srs.GetAxisName(null, i) ?? $"axis {i + 1}", srs.GetAxisOrientation(null, i).ToString().Replace("OAO_", string.Empty).ToLowerInvariant()));
            }

            var yThenX = srs.EPSGTreatsAsLatLong() == 1 || srs.EPSGTreatsAsNorthingEasting() == 1;
            var isGeographic = srs.IsGeographic() == 1;
            var isProjected = srs.IsProjected() == 1;
            var utmZone = isProjected ? srs.GetUTMZone() : 0;
            using var area = srs.GetAreaOfUse();
            string? proj4 = null;
            string? projJson = null;
            if (wanted.Contains("proj4"))
            {
                srs.ExportToProj4(out proj4);
            }

            if (wanted.Contains("projjson"))
            {
                srs.ExportToPROJJSON(out projJson, []);
            }

            List<CrsMatch>? matches = null;
            if (identify)
            {
                matches = [];
                var candidates = srs.FindMatches([], out var count, out var confidences) ?? [];
                for (var i = 0; i < Math.Min(count, candidates.Length); i++)
                {
                    using var candidate = candidates[i];
                    matches.Add(new CrsMatch(Crs.Code(candidate), candidate.GetName(), confidences[i]));
                }
            }

            var epoch = srs.GetCoordinateEpoch();
            return new CrsInfo
            {
                Name = srs.GetName(),
                Code = Crs.Code(srs),
                Kind = Crs.Kind(srs),
                IsDynamic = srs.IsDynamic(),
                Datum = srs.GetAttrValue("DATUM", 0),
                Ellipsoid = srs.GetAttrValue("SPHEROID", 0),
                SemiMajorAxis = Num.Finite(srs.GetSemiMajor()),
                InverseFlattening = Num.Finite(srs.GetInvFlattening()),
                LinearUnits = isGeographic ? null : srs.GetLinearUnitsName(),
                LinearUnitsToMetres = isGeographic ? null : srs.GetLinearUnits(),
                AngularUnits = isGeographic ? srs.GetAngularUnitsName() : null,
                Axes = axes,
                AuthorityOrderIsYThenX = yThenX,
                AxisOrderNote = yThenX
                    ? "The authority lists the second horizontal coordinate (latitude or northing) first. The tools of this server use x = longitude or easting and y = latitude or northing unless authority axis order is requested."
                    : "The authority order is x = longitude or easting, then y = latitude or northing, which matches the order that the tools of this server use.",
                UtmZone = utmZone == 0 ? null : Math.Abs(utmZone),
                UtmHemisphere = utmZone == 0 ? null : utmZone > 0 ? "north" : "south",
                AreaOfUse = area is null ? null : new AreaOfUseModel(area.name, area.west_lon_degree, area.south_lat_degree, area.east_lon_degree, area.north_lat_degree),
                CoordinateEpoch = epoch > 0 ? epoch : null,
                Wkt2 = wanted.Contains("wkt2") || wanted.Contains("wkt") ? Crs.Wkt(srs, "WKT2_2019", true) : null,
                Wkt1 = wanted.Contains("wkt1") ? Crs.Wkt(srs, "WKT1_GDAL", true) : null,
                EsriWkt = wanted.Contains("esri") ? Crs.Wkt(srs, "WKT1_ESRI") : null,
                Proj4 = proj4,
                ProjJson = projJson,
                Matches = matches
            };
        });
    }

    [McpServerTool(Name = "gdal_crs_lookup", Title = "Search the coordinate reference system database", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Searches the PROJ database for coordinate reference systems by name, code, type or location. Use it to find the EPSG code of a named system " +
        "such as 'GDA2020 / MGA zone 55', or the systems whose area of use contains a point.")]
    public Task<CrsLookupResult> LookupAsync(
        [Description("Text that the name must contain, or a code. Several words must all be present.")] string? query = null,
        [Description("Authority to search, for example EPSG, ESRI, IGNF or OGC.")] string authority = "EPSG",
        [Description("Type to return: geographic_2d, geographic_3d, geocentric, projected, vertical, compound or other.")] string? type = null,
        [Description("Longitude of a point that the area of use must contain.")] double? longitude = null,
        [Description("Latitude of a point that the area of use must contain.")] double? latitude = null,
        [Description("Includes deprecated entries.")] bool includeDeprecated = false,
        [Description("Number of matches to skip.")] int offset = 0,
        [Description("Maximum number of matches to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_lookup", cancellationToken, _ =>
        {
            if (longitude is null != latitude is null)
            {
                throw new McpException("Pass both longitude and latitude, or neither of them.");
            }

            CRSType? wantedType = type?.ToLowerInvariant() switch
            {
                null => null,
                "geographic_2d" => CRSType.OSR_CRS_TYPE_GEOGRAPHIC_2D,
                "geographic_3d" => CRSType.OSR_CRS_TYPE_GEOGRAPHIC_3D,
                "geocentric" => CRSType.OSR_CRS_TYPE_GEOCENTRIC,
                "projected" => CRSType.OSR_CRS_TYPE_PROJECTED,
                "vertical" => CRSType.OSR_CRS_TYPE_VERTICAL,
                "compound" => CRSType.OSR_CRS_TYPE_COMPOUND,
                "other" => CRSType.OSR_CRS_TYPE_OTHER,
                _ => throw new McpException("The type must be geographic_2d, geographic_3d, geocentric, projected, vertical, compound or other.")
            };

            var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            using var list = Osr.GetCRSInfoListFromDatabase(authority, out var count);
            var matches = new List<CrsListItem>();
            for (var i = 0; i < count; i++)
            {
                using var item = list.get(i);
                if ((!includeDeprecated && item.deprecated)
                    || (wantedType is not null && item.type != wantedType)
                    || !words.All(word => (item.name ?? string.Empty).Contains(word, StringComparison.OrdinalIgnoreCase) || item.code == word)
                    || (longitude is not null && !AreaContains(item, longitude.Value, latitude!.Value)))
                {
                    continue;
                }

                matches.Add(new CrsListItem
                {
                    Code = $"{item.auth_name}:{item.code}",
                    Name = item.name,
                    Type = item.type.ToString().Replace("OSR_CRS_TYPE_", string.Empty).ToLowerInvariant(),
                    Deprecated = item.deprecated,
                    AreaName = item.area_name,
                    AreaBounds = item.bbox_valid ? new Bounds(item.west_lon_degree, item.south_lat_degree, item.east_lon_degree, item.north_lat_degree) : null,
                    ProjectionMethod = item.projection_method
                });
            }

            return new CrsLookupResult { Total = matches.Count, Offset = offset, Items = [.. matches.Skip(offset).Take(context.ClampLimit(limit))] };
        });
    }

    [McpServerTool(Name = "gdal_crs_compare", Title = "Compare two coordinate reference systems", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Reports whether two coordinate reference system definitions describe the same system, with and without regard to axis order, and whether their geographic and vertical parts match.")]
    public Task<CrsComparison> CompareAsync(
        [Description("First coordinate reference system definition.")] string first,
        [Description("Second coordinate reference system definition.")] string second,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_compare", cancellationToken, _ =>
        {
            using var a = Crs.Parse(first, false);
            using var b = Crs.Parse(second, false);
            return new CrsComparison
            {
                Equivalent = a.IsSame(b, ["CRITERION=EQUIVALENT"]) == 1,
                EquivalentIgnoringAxisOrder = a.IsSame(b, ["CRITERION=EQUIVALENT_EXCEPT_AXIS_ORDER_GEOGCRS"]) == 1,
                SameGeographicCrs = a.IsSameGeogCS(b) == 1,
                SameVerticalCrs = a.IsSameVertCS(b) == 1,
                First = Crs.Summarize(a),
                Second = Crs.Summarize(b)
            };
        });
    }

    [McpServerTool(Name = "gdal_crs_transform_points", Title = "Transform coordinates between coordinate reference systems", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Transforms points from one coordinate reference system to another with PROJ, including datum and vertical transformations when the systems and the installed grids support them. " +
        "By default the first coordinate of a point is the longitude or easting and the second is the latitude or northing, for both input and output. " +
        "Pass axisOrder='authority' to use the order that the authority defines, for example latitude then longitude for EPSG:4326. " +
        "The result states the axis order that was applied.")]
    public Task<TransformResult> TransformPointsAsync(
        [Description("Source coordinate reference system.")] string sourceCrs,
        [Description("Target coordinate reference system.")] string targetCrs,
        [Description("Points as [x, y], [x, y, z] or [x, y, z, time] arrays. The time is a decimal year and matters for dynamic systems.")] double[][] points,
        [Description("Axis order of the input and the output: traditional_gis or authority.")] AxisOrder axisOrder = AxisOrder.TraditionalGis,
        [Description("Area of interest as [west, south, east, north] in degrees, which lets PROJ choose the most accurate operation for that area.")] double[]? areaOfInterest = null,
        [Description("Rejects operations whose accuracy is worse than this value in metres.")] double? desiredAccuracy = null,
        [Description("Allows approximate transformations when no accurate one is available.")] bool allowBallpark = true,
        [Description("Uses only the most accurate operation and fails when its grids are missing.")] bool onlyBest = false,
        [Description("Explicit PROJ pipeline or operation code to use instead of the automatic choice.")] string? coordinateOperation = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_transform_points", cancellationToken, operation =>
        {
            if (points.Length > context.Options.MaxFeatureCount)
            {
                throw new McpException($"At most {context.Options.MaxFeatureCount} points can be transformed in one call.");
            }

            var traditional = axisOrder == AxisOrder.TraditionalGis;
            using var source = Crs.Parse(sourceCrs, traditional);
            using var target = Crs.Parse(targetCrs, traditional);
            using var transformation = CreateTransformation(source, target, areaOfInterest, desiredAccuracy, allowBallpark, onlyBest, coordinateOperation);
            var results = new List<TransformedPoint>(points.Length);
            var output = new double[4];
            foreach (var point in points)
            {
                if (point.Length < 2)
                {
                    throw new McpException("Every point needs at least two coordinates.");
                }

                var hasZ = point.Length > 2;
                var time = point.Length > 3 ? point[3] : double.PositiveInfinity;
                int errorCode;
                string? message = null;
                try
                {
                    errorCode = transformation.TransformPointWithErrorCode(output, point[0], point[1], hasZ ? point[2] : 0, time);
                }
                catch (ApplicationException ex)
                {
                    // With exceptions enabled, PROJ reports a point outside the domain of the projection by throwing.
                    errorCode = -1;
                    message = ex.Message;
                }

                results.Add(errorCode == 0 && double.IsFinite(output[0]) && double.IsFinite(output[1])
                    ? new TransformedPoint { X = output[0], Y = output[1], Z = hasZ ? Num.Finite(output[2]) : null }
                    : new TransformedPoint { Error = message ?? $"The point is outside the domain of the transformation (PROJ error code {errorCode})." });
            }

            return new TransformResult
            {
                SourceCrs = Crs.Summarize(source),
                TargetCrs = Crs.Summarize(target),
                SourceAxisOrder = DescribeAxisOrder(source, traditional),
                TargetAxisOrder = DescribeAxisOrder(target, traditional),
                Points = results,
                Warnings = operation.WarningsOrNull?.Where(warning => !results.Any(point => point.Error == warning)).ToList() is { Count: > 0 } remaining ? remaining : null
            };
        });
    }

    [McpServerTool(Name = "gdal_crs_transform_bounds", Title = "Transform a bounding box between coordinate reference systems", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Transforms a bounding box to another coordinate reference system by densifying its edges, so that the result encloses the curved edges of the reprojected box. " +
        "The box is [minX, minY, maxX, maxY] with x as the longitude or easting, unless axisOrder='authority' is passed.")]
    public Task<TransformResult> TransformBoundsAsync(
        [Description("Source coordinate reference system.")] string sourceCrs,
        [Description("Target coordinate reference system.")] string targetCrs,
        [Description("Bounding box as [minX, minY, maxX, maxY].")] double[] bounds,
        [Description("Number of points added along each edge of the box.")] int densifyPoints = 21,
        [Description("Axis order of the input and the output: traditional_gis or authority.")] AxisOrder axisOrder = AxisOrder.TraditionalGis,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_transform_bounds", cancellationToken, operation =>
        {
            if (bounds.Length != 4)
            {
                throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
            }

            var traditional = axisOrder == AxisOrder.TraditionalGis;
            using var source = Crs.Parse(sourceCrs, traditional);
            using var target = Crs.Parse(targetCrs, traditional);
            using var transformation = new CoordinateTransformation(source, target);
            var output = new double[4];
            transformation.TransformBounds(output, bounds[0], bounds[1], bounds[2], bounds[3], densifyPoints);
            return new TransformResult
            {
                SourceCrs = Crs.Summarize(source),
                TargetCrs = Crs.Summarize(target),
                SourceAxisOrder = DescribeAxisOrder(source, traditional),
                TargetAxisOrder = DescribeAxisOrder(target, traditional),
                Bounds = new Bounds(output[0], output[1], output[2], output[3]),
                Warnings = operation.WarningsOrNull
            };
        });
    }

    [McpServerTool(Name = "gdal_crs_utm_zone", Title = "Find the UTM zone of a location", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the UTM zone, the hemisphere and the WGS 84 UTM EPSG code for a longitude and a latitude, including the exceptions for Norway and Svalbard.")]
    public Task<UtmZoneResult> UtmZoneAsync(
        [Description("Longitude in degrees.")] double longitude,
        [Description("Latitude in degrees.")] double latitude,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_crs_utm_zone", cancellationToken, _ =>
        {
            if (longitude is < -180 or > 180 || latitude is < -90 or > 90)
            {
                throw new McpException("The longitude must be between -180 and 180, and the latitude between -90 and 90.");
            }

            var zone = Math.Min(60, (int)Math.Floor((longitude + 180) / 6) + 1);
            if (latitude is >= 56 and < 64 && longitude is >= 3 and < 12)
            {
                zone = 32;
            }
            else if (latitude is >= 72 and < 84 && longitude is >= 0 and < 42)
            {
                zone = longitude < 9 ? 31 : longitude < 21 ? 33 : longitude < 33 ? 35 : 37;
            }

            var north = latitude >= 0;
            var epsg = (north ? 32600 : 32700) + zone;
            using var srs = new SpatialReference(string.Empty);
            srs.ImportFromEPSG(epsg);
            return new UtmZoneResult
            {
                Zone = zone,
                Hemisphere = north ? "north" : "south",
                Code = $"EPSG:{epsg}",
                Name = srs.GetName(),
                CentralMeridian = zone * 6 - 183
            };
        });
    }

    internal static CoordinateTransformation CreateTransformation(
        SpatialReference source,
        SpatialReference target,
        double[]? areaOfInterest,
        double? desiredAccuracy,
        bool allowBallpark,
        bool onlyBest,
        string? coordinateOperation)
    {
        using var options = new CoordinateTransformationOptions();
        if (areaOfInterest is not null)
        {
            if (areaOfInterest.Length != 4)
            {
                throw new McpException("The area of interest needs exactly four numbers: west, south, east and north.");
            }

            options.SetAreaOfInterest(areaOfInterest[0], areaOfInterest[1], areaOfInterest[2], areaOfInterest[3]);
        }

        if (desiredAccuracy is not null)
        {
            options.SetDesiredAccuracy(desiredAccuracy.Value);
        }

        options.SetBallparkAllowed(allowBallpark);
        if (onlyBest)
        {
            options.SetOnlyBest(true);
        }

        if (coordinateOperation is not null)
        {
            options.SetOperation(coordinateOperation, false);
        }

        return new CoordinateTransformation(source, target, options);
    }

    private static string DescribeAxisOrder(SpatialReference srs, bool traditional)
    {
        if (traditional)
        {
            return TraditionalOrderText;
        }

        var names = Enumerable.Range(0, Math.Min(2, srs.GetAxesCount())).Select(index => srs.GetAxisName(null, index) ?? $"axis {index + 1}").ToList();
        return names.Count == 2 ? $"first = {names[0]}, second = {names[1]} (authority order)" : "authority order";
    }

    private static bool AreaContains(CRSInfo item, double longitude, double latitude)
    {
        if (!item.bbox_valid || latitude < item.south_lat_degree || latitude > item.north_lat_degree)
        {
            return false;
        }

        // An area of use that crosses the antimeridian has a western bound that is greater than its eastern bound.
        return item.west_lon_degree <= item.east_lon_degree
            ? longitude >= item.west_lon_degree && longitude <= item.east_lon_degree
            : longitude >= item.west_lon_degree || longitude <= item.east_lon_degree;
    }
}
