using System.Text;

namespace InFarm.Mcp.Gdal.Tools;

// Raster analysis built on the native GDAL algorithms.
[McpServerToolType]
public sealed class AnalysisTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_raster_dem", Title = "Terrain analysis of an elevation raster (gdaldem)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Derives terrain products from a digital elevation model with the native gdaldem utility. The mode selects the product: hillshade, slope, aspect, " +
        "color-relief, TRI (terrain ruggedness index), TPI (topographic position index) or roughness.")]
    public Task<ProcessingResult> DemAsync(
        [Description("Input elevation raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Product to compute: hillshade, slope, aspect, color-relief, TRI, TPI or roughness.")] string mode,
        [Description("Band that holds the elevation, starting at 1.")] int band = 1,
        [Description("Vertical exaggeration applied to the elevation before a hillshade is computed.")] double? zFactor = null,
        [Description("Ratio of vertical units to horizontal units. Use 111120 for elevations in metres on a raster in degrees.")] double? scale = null,
        [Description("Azimuth of the light in degrees for a hillshade. The default is 315.")] double? azimuth = null,
        [Description("Altitude of the light in degrees for a hillshade. The default is 45.")] double? altitude = null,
        [Description("Hillshade variant: combined, multidirectional or igor.")] string? hillshadeVariant = null,
        [Description("Expresses the slope in percent instead of degrees.")] bool slopeInPercent = false,
        [Description("Returns the aspect as a trigonometric angle instead of an azimuth.")] bool trigonometricAspect = false,
        [Description("Returns 0 instead of -9999 as the aspect of flat areas.")] bool zeroForFlat = false,
        [Description("Gradient algorithm: Horn or ZevenbergenThorne for hillshade, slope and aspect. Wilson or Riley for TRI.")] string? algorithm = null,
        [Description("Computes values at the raster edges and next to NoData pixels.")] bool computeEdges = false,
        [Description("Path of a color configuration file for color-relief. Each line holds an elevation followed by red, green, blue and optionally alpha.")] string? colorFile = null,
        [Description("Inline content of a color configuration for color-relief, for example '0 0 0 255\\n1000 255 255 255\\nnv 0 0 0 0'.")] string? colorText = null,
        [Description("Color matching for color-relief: interpolate, exact or nearest.")] string colorMatching = "interpolate",
        [Description("Adds an alpha band to a color-relief output.")] bool alpha = false,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Additional gdaldem arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_dem", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var isColorRelief = mode.Equals("color-relief", StringComparison.OrdinalIgnoreCase);
            if (isColorRelief && colorFile is null == colorText is null)
            {
                throw new McpException("The color-relief mode needs exactly one of colorFile or colorText.");
            }

            var arguments = new ArgList()
                .Option("-of", format)
                .Add("-b", band.ToString(CultureInfo.InvariantCulture))
                .Option("-z", zFactor)
                .Option("-s", scale)
                .Option("-az", azimuth)
                .Option("-alt", altitude)
                .Flag(slopeInPercent, "-p")
                .Flag(trigonometricAspect, "-trigonometric")
                .Flag(zeroForFlat, "-zero_for_flat")
                .Option("-alg", algorithm)
                .Flag(computeEdges, "-compute_edges")
                .Flag(alpha, "-alpha")
                .Each("-co", creationOptions);
            if (hillshadeVariant is not null)
            {
                arguments.Add(hillshadeVariant.ToLowerInvariant() switch
                {
                    "combined" => "-combined",
                    "multidirectional" => "-multidirectional",
                    "igor" => "-igor",
                    _ => throw new McpException("The hillshade variant must be combined, multidirectional or igor.")
                });
            }

            switch (colorMatching.ToLowerInvariant())
            {
                case "interpolate":
                    break;
                case "exact":
                    arguments.Add("-exact_color_entry");
                    break;
                case "nearest":
                    arguments.Add("-nearest_color_entry");
                    break;
                default:
                    throw new McpException("The color matching must be interpolate, exact or nearest.");
            }

            arguments.Extra(additionalArguments, context.Paths);
            string? temporaryColors = null;
            var colors = colorFile is null ? null : context.Paths.Input(colorFile);
            if (colorText is not null)
            {
                temporaryColors = VsiTemp.Create(".txt");
                GdalApi.FileFromMemBuffer(temporaryColors, Encoding.UTF8.GetBytes(colorText.Replace("\\n", "\n")));
                colors = temporaryColors;
            }

            try
            {
                using var source = Open.Raster(resolvedInput);
                using var options = new GDALDEMProcessingOptions(arguments.ToArray());
                return Processing.Produce(
                    operation,
                    resolvedOutput,
                    () => GdalApi.wrapper_GDALDEMProcessing(resolvedOutput, source, mode, colors, options, operation.Progress, null));
            }
            finally
            {
                if (temporaryColors is not null)
                {
                    VsiTemp.Delete(temporaryColors);
                }
            }
        });
    }

    [McpServerTool(Name = "gdal_raster_contours", Title = "Generate contour lines or polygons (gdal_contour)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Generates vector contour lines or contour polygons from a raster surface with the native gdal_contour utility. " +
        "Pass interval for regularly spaced levels or fixedLevels for explicit levels.")]
    public Task<ProcessingResult> ContoursAsync(
        [Description("Input raster path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Elevation interval between contours.")] double? interval = null,
        [Description("Offset from zero relative to which the interval is applied.")] double? offset = null,
        [Description("Explicit contour levels. The names MIN and MAX are also accepted as text through additionalArguments.")] double[]? fixedLevels = null,
        [Description("Generates levels on an exponential scale with this base.")] double? exponentialBase = null,
        [Description("Band to contour, starting at 1.")] int band = 1,
        [Description("Name of the attribute that receives the elevation of a contour line.")] string elevationField = "elev",
        [Description("Writes polygons that cover the area between two levels instead of lines.")] bool polygons = false,
        [Description("Name of the attribute that receives the lower elevation of a contour polygon.")] string minimumField = "elev_min",
        [Description("Name of the attribute that receives the upper elevation of a contour polygon.")] string maximumField = "elev_max",
        [Description("Value to treat as NoData in the input.")] double? noData = null,
        [Description("Writes three-dimensional geometries.")] bool threeDimensional = false,
        [Description("Name of the output layer.")] string? layerName = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Dataset creation options in NAME=VALUE form.")] string[]? datasetCreationOptions = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Additional gdal_contour arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_contours", progress, cancellationToken, operation =>
        {
            if (interval is null && fixedLevels is null && exponentialBase is null)
            {
                throw new McpException("Pass interval, fixedLevels or exponentialBase to define the contour levels.");
            }

            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Add("-b", band.ToString(CultureInfo.InvariantCulture))
                .Option("-i", interval)
                .Option("-off", offset)
                .Option("-e", exponentialBase)
                .Option("-snodata", noData)
                .Flag(threeDimensional, "-3d")
                .Option("-nln", layerName)
                .Option("-f", format)
                .Each("-dsco", datasetCreationOptions)
                .Each("-lco", layerCreationOptions);
            if (polygons)
            {
                arguments.Add("-p", "-amin", minimumField, "-amax", maximumField);
            }
            else
            {
                arguments.Add("-a", elevationField);
            }

            if (fixedLevels is { Length: > 0 })
            {
                arguments.Add("-fl").Add([.. fixedLevels.Select(Num.Text)]);
            }

            arguments.Extra(additionalArguments, context.Paths);
            using var source = Open.Raster(resolvedInput);
            using var options = new GDALContourOptions(arguments.ToArray());
            return Processing.Produce(
                operation,
                resolvedOutput,
                () => GdalApi.wrapper_GDALContourDestName(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_polygonize", Title = "Convert raster regions to polygons (gdal_polygonize)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates one polygon for each connected region of pixels that share a value, with the native GDAL polygonize algorithm. " +
        "The pixel value is stored in an attribute. Pixels that the band mask marks as NoData are skipped unless useMask is false.")]
    public Task<ProcessingResult> PolygonizeAsync(
        [Description("Input raster path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Band to polygonize, starting at 1.")] int band = 1,
        [Description("Name of the attribute that receives the pixel value.")] string fieldName = "DN",
        [Description("Connectedness of pixels: 4 or 8.")] int connectedness = 4,
        [Description("Skips the pixels that the mask of the band marks as invalid.")] bool useMask = true,
        [Description("Name of the output layer.")] string? layerName = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Dataset creation options in NAME=VALUE form.")] string[]? datasetCreationOptions = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_polygonize", progress, cancellationToken, operation =>
        {
            if (connectedness is not (4 or 8))
            {
                throw new McpException("The connectedness must be 4 or 8.");
            }

            using var source = Open.Raster(context.Paths.Input(input));
            using var sourceBand = RasterTools.GetBand(source, band);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            return Processing.Guard(operation, resolvedOutput, () =>
            {
                using var destination = Processing.CreateDataSource(resolvedOutput, format, datasetCreationOptions);
                using var srs = source.GetSpatialRef();
                using var layer = destination.CreateLayer(
                    Processing.LayerNameFor(resolvedOutput, layerName), srs, wkbGeometryType.wkbPolygon, layerCreationOptions ?? []);
                var isFloat = sourceBand.DataType is DataType.GDT_Float32 or DataType.GDT_Float64 or DataType.GDT_Float16;
                using var field = new FieldDefn(fieldName, isFloat ? FieldType.OFTReal : FieldType.OFTInteger);
                layer.CreateField(field, 1);
                using var mask = useMask ? sourceBand.GetMaskBand() : null;
                string[] options = connectedness == 8 ? ["8CONNECTED=8"] : [];
                if (isFloat)
                {
                    GdalApi.FPolygonize(sourceBand, mask, layer, 0, options, operation.Progress, null);
                }
                else
                {
                    GdalApi.Polygonize(sourceBand, mask, layer, 0, options, operation.Progress, null);
                }

                layer.SyncToDisk();
            });
        });
    }

    [McpServerTool(Name = "gdal_raster_rasterize", Title = "Burn vector geometries into a raster (gdal_rasterize)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Burns vector geometries into a raster with the native gdal_rasterize utility. A new raster needs resolution or size. " +
        "Pass updateExisting=true to burn into the bands of an existing raster instead.")]
    public Task<ProcessingResult> RasterizeAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Layers to burn. Every layer is burned when omitted.")] string[]? layers = null,
        [Description("Attribute filter that selects the features to burn.")] string? where = null,
        [Description("SQL statement whose result is burned.")] string? sql = null,
        [Description("Fixed values to burn, one for each band.")] double[]? burnValues = null,
        [Description("Name of the attribute whose value is burned.")] string? attribute = null,
        [Description("Burns the Z values of the geometries.")] bool useZ = false,
        [Description("Burns every pixel that a geometry touches, not only pixels whose centre is inside.")] bool allTouched = false,
        [Description("Adds the burn value to the existing pixel value instead of replacing it.")] bool add = false,
        [Description("Burns the area outside the geometries.")] bool invert = false,
        [Description("Pixel size of a new raster as [xResolution, yResolution].")] double[]? resolution = null,
        [Description("Size of a new raster in pixels as [width, height].")] int[]? size = null,
        [Description("Extent of a new raster as [minX, minY, maxX, maxY]. The extent of the layers is used when omitted.")] double[]? bounds = null,
        [Description("Aligns the extent of a new raster to multiples of the resolution.")] bool targetAlignedPixels = false,
        [Description("Initial values of the bands of a new raster.")] double[]? initialValues = null,
        [Description("NoData value of a new raster.")] string? noData = null,
        [Description("Data type of a new raster.")] string? outputType = null,
        [Description("Bands of an existing raster to burn into, starting at 1.")] int[]? bands = null,
        [Description("Burns into an existing raster instead of creating a new one.")] bool updateExisting = false,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Additional gdal_rasterize arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_rasterize", progress, cancellationToken, operation =>
        {
            if (burnValues is null && attribute is null && !useZ)
            {
                throw new McpException("Pass burnValues, attribute or useZ=true to define the values that are burned.");
            }

            var arguments = new ArgList()
                .Each("-l", layers)
                .Option("-where", where)
                .Option("-sql", sql)
                .Each("-burn", burnValues?.Select(Num.Text))
                .Option("-a", attribute)
                .Flag(useZ, "-3d")
                .Flag(allTouched, "-at")
                .Flag(add, "-add")
                .Flag(invert, "-i")
                .Each("-b", bands?.Select(band => band.ToString(CultureInfo.InvariantCulture)));
            using var source = Open.Vector(context.Paths.Input(input));
            if (updateExisting)
            {
                var existing = context.Paths.Update(output);
                arguments.Extra(additionalArguments, context.Paths);
                using var options = new GDALRasterizeOptions(arguments.ToArray());
                using (var destination = Open.Raster(existing, true))
                {
                    GdalApi.wrapper_GDALRasterizeDestDS(destination, source, options, operation.Progress, null);
                    destination.FlushCache();
                }

                operation.ThrowIfCancelled();
                return Describe.Output(existing, operation, "The geometries were burned into the existing raster.");
            }

            if (resolution is null && size is null)
            {
                throw new McpException("A new raster needs resolution or size. Pass updateExisting=true to burn into an existing raster.");
            }

            var resolvedOutput = context.Paths.Output(output, overwrite);
            arguments
                .Numbers("-tr", resolution, 2, "resolution")
                .Numbers("-te", bounds, 4, "bounds")
                .Flag(targetAlignedPixels, "-tap")
                .Each("-init", initialValues?.Select(Num.Text))
                .Option("-a_nodata", noData)
                .Option("-ot", outputType)
                .Option("-of", format)
                .Each("-co", creationOptions);
            if (size is { Length: > 0 })
            {
                arguments.Numbers("-ts", [.. size.Select(value => (double)value)], 2, "size");
            }

            arguments.Extra(additionalArguments, context.Paths);
            using var createOptions = new GDALRasterizeOptions(arguments.ToArray());
            return Processing.Produce(
                operation,
                resolvedOutput,
                () => GdalApi.wrapper_GDALRasterizeDestName(resolvedOutput, source, createOptions, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_proximity", Title = "Compute a proximity raster (gdal_proximity)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Computes, for every pixel, the distance to the nearest target pixel with the native GDAL proximity algorithm. Target pixels are the non-zero pixels unless targetValues is given.")]
    public Task<ProcessingResult> ProximityAsync(
        [Description("Input raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Band to analyse, starting at 1.")] int band = 1,
        [Description("Pixel values that count as targets. Every non-zero pixel is a target when omitted.")] double[]? targetValues = null,
        [Description("Unit of the distances: GEO for georeferenced units or PIXEL.")] string distanceUnits = "GEO",
        [Description("Distance beyond which pixels receive the NoData value.")] double? maximumDistance = null,
        [Description("NoData value of the output.")] double? noData = null,
        [Description("Value written to every pixel within the maximum distance, instead of the distance.")] double? fixedBufferValue = null,
        [Description("Output data type.")] string outputType = "Float32",
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_proximity", progress, cancellationToken, operation =>
        {
            using var source = Open.Raster(context.Paths.Input(input));
            using var sourceBand = RasterTools.GetBand(source, band);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var options = new List<string> { $"DISTUNITS={distanceUnits.ToUpperInvariant()}" };
            if (targetValues is { Length: > 0 })
            {
                options.Add($"VALUES={string.Join(',', targetValues.Select(Num.Text))}");
            }

            if (maximumDistance is not null)
            {
                options.Add($"MAXDIST={Num.Text(maximumDistance.Value)}");
            }

            if (noData is not null)
            {
                options.Add($"NODATA={Num.Text(noData.Value)}");
            }

            if (fixedBufferValue is not null)
            {
                options.Add($"FIXED_BUF_VAL={Num.Text(fixedBufferValue.Value)}");
            }

            return Processing.Guard(operation, resolvedOutput, () =>
            {
                using var destination = Processing.CreateLike(source, resolvedOutput, format, outputType, 1, creationOptions);
                using var destinationBand = destination.GetRasterBand(1);
                if (noData is not null)
                {
                    destinationBand.SetNoDataValue(noData.Value);
                }

                GdalApi.ComputeProximity(sourceBand, destinationBand, [.. options], operation.Progress, null);
                destination.FlushCache();
            });
        });
    }

    [McpServerTool(Name = "gdal_raster_sieve", Title = "Remove small raster regions (gdal_sieve)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Replaces connected pixel regions that are smaller than a threshold with the value of their largest neighbour, with the native GDAL sieve filter.")]
    public Task<ProcessingResult> SieveAsync(
        [Description("Input raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Regions with fewer pixels than this threshold are removed.")] int threshold,
        [Description("Band to filter, starting at 1.")] int band = 1,
        [Description("Connectedness of pixels: 4 or 8.")] int connectedness = 4,
        [Description("Leaves the pixels that the mask of the band marks as invalid untouched.")] bool useMask = true,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_sieve", progress, cancellationToken, operation =>
        {
            if (connectedness is not (4 or 8))
            {
                throw new McpException("The connectedness must be 4 or 8.");
            }

            using var source = Open.Raster(context.Paths.Input(input));
            using var sourceBand = RasterTools.GetBand(source, band);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            return Processing.Guard(operation, resolvedOutput, () =>
            {
                using var destination = Processing.CreateLike(
                    source, resolvedOutput, format, GdalApi.GetDataTypeName(sourceBand.DataType), 1, creationOptions);
                using var destinationBand = destination.GetRasterBand(1);
                sourceBand.GetNoDataValue(out var noData, out var hasNoData);
                if (hasNoData != 0)
                {
                    destinationBand.SetNoDataValue(noData);
                }

                using var mask = useMask ? sourceBand.GetMaskBand() : null;
                GdalApi.SieveFilter(sourceBand, mask, destinationBand, threshold, connectedness, [], operation.Progress, null);
                destination.FlushCache();
            });
        });
    }

    [McpServerTool(Name = "gdal_raster_fill_nodata", Title = "Fill NoData regions by interpolation (gdal_fillnodata)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Copies a raster and fills its NoData regions by interpolating from the valid pixels at their edges, with the native GDAL fill algorithm.")]
    public Task<ProcessingResult> FillNoDataAsync(
        [Description("Input raster path.")] string input,
        [Description("Output raster path. The output format must support updates, so use GTiff rather than COG.")] string output,
        [Description("Bands to fill, starting at 1. Every band is filled when omitted.")] int[]? bands = null,
        [Description("Maximum distance in pixels over which values are interpolated.")] double maximumDistance = 100,
        [Description("Number of 3x3 smoothing passes applied to the filled areas.")] int smoothingIterations = 0,
        [Description("Interpolation method: inv_dist or nearest.")] string interpolation = "inv_dist",
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_fill_nodata", progress, cancellationToken, operation =>
        {
            using var source = Open.Raster(context.Paths.Input(input));
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList().Option("-of", format).Each("-co", creationOptions);
            return Processing.Guard(operation, resolvedOutput, () =>
            {
                using var options = new GDALTranslateOptions(arguments.ToArray());
                using var copy = GdalApi.wrapper_GDALTranslate(resolvedOutput, source, options, null, null);
                foreach (var number in bands ?? [.. Enumerable.Range(1, copy.RasterCount)])
                {
                    using var targetBand = RasterTools.GetBand(copy, number);
                    GdalApi.FillNodata(
                        targetBand, null, maximumDistance, smoothingIterations, [$"INTERPOLATION={interpolation.ToUpperInvariant()}"], operation.Progress, null);
                    operation.ThrowIfCancelled();
                }

                copy.FlushCache();
            });
        });
    }

    [McpServerTool(Name = "gdal_raster_viewshed", Title = "Compute a viewshed (gdal_viewshed)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Computes which pixels of an elevation raster are visible from an observer point, with the native GDAL viewshed algorithm.")]
    public Task<ProcessingResult> ViewshedAsync(
        [Description("Input elevation raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("X coordinate of the observer in the raster coordinate reference system, or in observerCrs when it is given.")] double observerX,
        [Description("Y coordinate of the observer.")] double observerY,
        [Description("Height of the observer above the surface.")] double observerHeight = 2,
        [Description("Height of the target above the surface.")] double targetHeight = 0,
        [Description("Coordinate reference system of the observer position. With a geographic system x is the longitude and y is the latitude.")] string? observerCrs = null,
        [Description("Band that holds the elevation, starting at 1.")] int band = 1,
        [Description("Maximum distance from the observer to analyse. 0 analyses the whole raster.")] double maximumDistance = 0,
        [Description("Value written to visible pixels.")] double visibleValue = 255,
        [Description("Value written to invisible pixels.")] double invisibleValue = 0,
        [Description("Value written to pixels beyond the maximum distance.")] double outOfRangeValue = 0,
        [Description("NoData value of the output. -1 leaves the output without a NoData value.")] double noDataValue = -1,
        [Description("Coefficient for the curvature of the earth and atmospheric refraction.")] double curvatureCoefficient = 0.85714,
        [Description("How cell heights are interpolated: edge, diagonal, max or min.")] string mode = "edge",
        [Description("Output content: normal (visibility), dem (minimum visible height above sea level) or ground (minimum visible height above the ground).")] string outputMode = "normal",
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_viewshed", progress, cancellationToken, operation =>
        {
            var viewshedMode = mode.ToLowerInvariant() switch
            {
                "edge" => ViewshedMode.GVM_Edge,
                "diagonal" => ViewshedMode.GVM_Diagonal,
                "max" => ViewshedMode.GVM_Max,
                "min" => ViewshedMode.GVM_Min,
                _ => throw new McpException("The mode must be edge, diagonal, max or min.")
            };
            var heightMode = outputMode.ToLowerInvariant() switch
            {
                "normal" => ViewshedOutputType.GVOT_NORMAL,
                "dem" => ViewshedOutputType.GVOT_MIN_TARGET_HEIGHT_FROM_DEM,
                "ground" => ViewshedOutputType.GVOT_MIN_TARGET_HEIGHT_FROM_GROUND,
                _ => throw new McpException("The output mode must be normal, dem or ground.")
            };

            using var source = Open.Raster(context.Paths.Input(input));
            using var sourceBand = RasterTools.GetBand(source, band);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            double[] observer = [observerX, observerY, 0];
            if (observerCrs is not null)
            {
                using var transformation = RasterTools.CreateTransformationToDataset(source, observerCrs);
                transformation.TransformPoint(observer);
            }

            var driverName = Processing.DriverForOutput(resolvedOutput, format, false).ShortName;
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.ViewshedGenerate(
                sourceBand, driverName, resolvedOutput, creationOptions ?? [], observer[0], observer[1], observerHeight, targetHeight,
                visibleValue, invisibleValue, outOfRangeValue, noDataValue, curvatureCoefficient, viewshedMode, maximumDistance,
                operation.Progress, null, heightMode, []));
        });
    }

    [McpServerTool(Name = "gdal_raster_grid", Title = "Interpolate scattered points to a raster (gdal_grid)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a regular raster grid from scattered point data with the native gdal_grid utility. The algorithm text selects the interpolation method and its parameters, " +
        "for example 'invdist:power=2.0:smoothing=1.0', 'invdistnn:max_points=12:radius=500', 'average:radius1=100:radius2=100', 'nearest', 'linear', 'minimum', 'maximum', 'range' or 'count'.")]
    public Task<ProcessingResult> GridAsync(
        [Description("Input vector dataset path with point geometries.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Interpolation algorithm and its parameters.")] string algorithm = "invdist",
        [Description("Attribute that holds the value to interpolate. The Z coordinate of the points is used when omitted.")] string? zField = null,
        [Description("Layers to read. Every layer is read when omitted.")] string[]? layers = null,
        [Description("Attribute filter that selects the points.")] string? where = null,
        [Description("SQL statement whose result provides the points.")] string? sql = null,
        [Description("Extent of the grid as [minX, minY, maxX, maxY]. The extent of the points is used when omitted.")] double[]? bounds = null,
        [Description("Size of the grid in pixels as [width, height].")] int[]? size = null,
        [Description("Pixel size as [xResolution, yResolution].")] double[]? resolution = null,
        [Description("Output data type.")] string? outputType = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Additional gdal_grid arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_grid", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Add("-a", algorithm)
                .Option("-zfield", zField)
                .Each("-l", layers)
                .Option("-where", where)
                .Option("-sql", sql)
                .Numbers("-tr", resolution, 2, "resolution")
                .Option("-ot", outputType)
                .Option("-of", format)
                .Each("-co", creationOptions);
            if (bounds is not null)
            {
                if (bounds.Length != 4)
                {
                    throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
                }

                arguments.Numbers("-txe", [bounds[0], bounds[2]], 2, "bounds").Numbers("-tye", [bounds[3], bounds[1]], 2, "bounds");
            }

            if (size is { Length: > 0 })
            {
                arguments.Numbers("-outsize", [.. size.Select(value => (double)value)], 2, "size");
            }

            arguments.Extra(additionalArguments, context.Paths);
            using var source = Open.Vector(resolvedInput);
            using var options = new GDALGridOptions(arguments.ToArray());
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.wrapper_GDALGrid(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_nearblack", Title = "Clean nearly black or white raster collars (nearblack)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Converts the nearly black or nearly white pixels around the edge of an image to an exact color, an alpha band or a mask, with the native nearblack utility.")]
    public Task<ProcessingResult> NearBlackAsync(
        [Description("Input raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Searches for nearly white pixels instead of nearly black ones.")] bool white = false,
        [Description("Colors to search for, each as comma-separated band values such as '0,0,0'.")] string[]? colors = null,
        [Description("How far a pixel value may be from the collar color and still count as collar.")] int? near = null,
        [Description("Number of non-collar pixels that may be crossed before the search stops.")] int? nonBlackPixels = null,
        [Description("Adds an alpha band that is transparent in the collar.")] bool setAlpha = false,
        [Description("Adds a mask band that is zero in the collar.")] bool setMask = false,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Additional nearblack arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_nearblack", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Option("-of", format)
                .Flag(white, "-white")
                .Each("-color", colors)
                .Option("-near", near)
                .Option("-nb", nonBlackPixels)
                .Flag(setAlpha, "-setalpha")
                .Flag(setMask, "-setmask")
                .Each("-co", creationOptions)
                .Extra(additionalArguments, context.Paths);
            using var source = Open.Raster(resolvedInput);
            using var options = new GDALNearblackOptions(arguments.ToArray());
            return Processing.Produce(
                operation,
                resolvedOutput,
                () => GdalApi.wrapper_GDALNearblackDestName(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_footprint", Title = "Compute the footprint of valid raster pixels (gdal_footprint)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Writes a polygon that outlines the valid pixels of a raster, taking NoData values, masks and alpha bands into account, with the native gdal_footprint utility.")]
    public Task<ProcessingResult> FootprintAsync(
        [Description("Input raster path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Bands whose masks are combined, starting at 1. Every band is used when omitted.")] int[]? bands = null,
        [Description("How the masks of the bands are combined: union or intersection.")] string? combineBands = null,
        [Description("Reprojects the footprint to this coordinate reference system.")] string? targetCrs = null,
        [Description("Writes one feature for each polygon instead of a single multipolygon.")] bool splitPolygons = false,
        [Description("Writes the convex hull of the footprint.")] bool convexHull = false,
        [Description("Maximum distance between two consecutive vertices.")] double? densify = null,
        [Description("Simplification tolerance.")] double? simplify = null,
        [Description("Minimum area of a ring that is kept.")] double? minimumRingArea = null,
        [Description("Maximum number of points of the footprint, or 'unlimited'.")] string? maximumPoints = null,
        [Description("Name of the output layer.")] string? layerName = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Additional gdal_footprint arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_footprint", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Each("-b", bands?.Select(band => band.ToString(CultureInfo.InvariantCulture)))
                .Option("-combine_bands", combineBands)
                .Option("-t_srs", targetCrs)
                .Flag(splitPolygons, "-split_polys")
                .Flag(convexHull, "-convex_hull")
                .Option("-densify", densify)
                .Option("-simplify", simplify)
                .Option("-min_ring_area", minimumRingArea)
                .Option("-max_points", maximumPoints)
                .Option("-lyr_name", layerName)
                .Option("-of", format)
                .Extra(additionalArguments, context.Paths);
            using var source = Open.Raster(resolvedInput);
            using var options = new GDALFootprintOptions(arguments.ToArray());
            return Processing.Produce(
                operation,
                resolvedOutput,
                () => GdalApi.wrapper_GDALFootprintDestName(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_calculate", Title = "Raster algebra (gdal_calc)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Evaluates arithmetic expressions over one or more rasters with the native GDAL raster algebra, which computes the result block by block, like gdal_calc. " +
        "Inputs are bound to variable names, and each expression produces one output band. Example: inputs={\"NIR\":\"nir.tif\",\"RED\":\"red.tif\"}, " +
        "expressions=[\"(NIR-RED)/(NIR+RED)\"]. A name refers to band 1 of its raster, and NAME[2] refers to another band. " +
        "Expressions support + - * / ^, parentheses, numbers and the functions abs, sqrt, log, log10, exp, pow, min, max, sum, mean, and the casts byte, int16, uint16, int32, uint32, float32 and float64. " +
        "Comparisons and conditionals are not available in the bundled GDAL build, so use the 'raster reclassify' algorithm through gdal_algorithm_run for thresholds. " +
        "The rasters must share one grid, so align them with gdal_raster_align first when they differ.")]
    public Task<ProcessingResult> CalculateAsync(
        [Description("Input rasters by variable name.")] Dictionary<string, string> inputs,
        [Description("Expressions to evaluate. Each expression produces one output band.")] string[] expressions,
        [Description("Output raster path.")] string output,
        [Description("Output data type. The type that the arithmetic produces is kept when omitted.")] string? outputType = null,
        [Description("NoData value to assign to the output bands, or 'none'.")] string? noData = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_calculate", progress, cancellationToken, operation =>
        {
            if (inputs.Count == 0 || expressions.Length == 0)
            {
                throw new McpException("At least one input raster and one expression are required.");
            }

            var resolvedOutput = context.Paths.Output(output, overwrite);
            var datasets = new Dictionary<string, Dataset>(StringComparer.OrdinalIgnoreCase);
            var results = new List<Dataset>();
            using var evaluator = new RasterExpression(datasets);
            try
            {
                foreach (var (name, path) in inputs)
                {
                    datasets[name] = Open.Raster(context.Paths.Input(path));
                }

                var first = datasets.Values.First();
                if (datasets.Values.Any(dataset => dataset.RasterXSize != first.RasterXSize || dataset.RasterYSize != first.RasterYSize))
                {
                    throw new McpException("The input rasters differ in size. Align them to one grid with gdal_raster_align before calculating.");
                }

                foreach (var expression in expressions)
                {
                    results.Add(evaluator.Evaluate(expression).GetDataset());
                }

                var arguments = new ArgList()
                    .Option("-of", format)
                    .Option("-ot", outputType)
                    .Option("-a_nodata", noData)
                    .Each("-co", creationOptions);
                using var translateOptions = new GDALTranslateOptions(arguments.ToArray());
                if (results.Count == 1)
                {
                    return Processing.Produce(
                        operation,
                        resolvedOutput,
                        () => GdalApi.wrapper_GDALTranslate(resolvedOutput, results[0], translateOptions, operation.Progress, null));
                }

                using var stackOptions = new GDALBuildVRTOptions(["-separate"]);
                using var stack = GdalApi.BuildVRT(string.Empty, [.. results], stackOptions, null, null);
                return Processing.Produce(
                    operation,
                    resolvedOutput,
                    () => GdalApi.wrapper_GDALTranslate(resolvedOutput, stack, translateOptions, operation.Progress, null));
            }
            finally
            {
                results.ForEach(result => result.Dispose());
                evaluator.Dispose();
                foreach (var dataset in datasets.Values)
                {
                    dataset.Dispose();
                }
            }
        });
    }
}
