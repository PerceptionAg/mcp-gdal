namespace InFarm.Mcp.Gdal.Tools;

// Raster conversion, reprojection, cropping and creation, built on the native gdal_translate and gdalwarp utilities.
[McpServerToolType]
public sealed class ConversionTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_raster_translate", Title = "Convert a raster (gdal_translate)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Converts a raster to another format, data type, band selection, window, size or value scaling with the native gdal_translate utility. " +
        "Use it for format conversion, compression, tiling, band selection, subsetting by pixel or georeferenced window, resizing and NoData assignment. " +
        "Use gdal_raster_warp instead when the coordinate reference system changes.")]
    public Task<ProcessingResult> TranslateAsync(
        RasterTranslateRequest request,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_translate", progress, cancellationToken, operation =>
        {
            var input = context.Paths.Input(request.Input);
            var output = context.Paths.Output(request.Output, request.Overwrite);
            var arguments = new ArgList()
                .Option("-of", request.Format)
                .Option("-ot", request.OutputType)
                .Each("-b", request.Bands)
                .Numbers("-srcwin", request.SourceWindow, 4, "sourceWindow")
                .Numbers("-projwin", request.ProjectedWindow, 4, "projectedWindow")
                .Option("-projwin_srs", request.ProjectedWindowCrs)
                .Numbers("-tr", request.Resolution, 2, "resolution")
                .Option("-r", request.Resampling)
                .Option("-exponent", request.Exponent)
                .Flag(request.Unscale, "-unscale")
                .Option("-a_nodata", request.NoData)
                .Option("-a_srs", request.AssignCrs)
                .Numbers("-a_ullr", request.AssignBounds, 4, "assignBounds")
                .Option("-expand", request.Expand)
                .Each("-mo", request.Metadata)
                .Flag(request.Statistics, "-stats")
                .Each("-co", request.CreationOptions);

            if (request.OutputSize is { Length: > 0 })
            {
                if (request.OutputSize.Length != 2)
                {
                    throw new McpException("The parameter 'outputSize' needs exactly two values: the width and the height.");
                }

                arguments.Add("-outsize", request.OutputSize[0], request.OutputSize[1]);
            }

            if (request.Scale is not null)
            {
                if (request.Scale.Length is not (0 or 2 or 4))
                {
                    throw new McpException("The parameter 'scale' needs zero, two or four numbers.");
                }

                arguments.Add("-scale").Add([.. request.Scale.Select(Num.Text)]);
            }

            if (request.ColorInterpretation is { Length: > 0 })
            {
                arguments.Add("-colorinterp", string.Join(',', request.ColorInterpretation));
            }

            arguments.Extra(request.AdditionalArguments, context.Paths);
            using var source = Open.Raster(input, false, request.OpenOptions);
            using var options = new GDALTranslateOptions(arguments.ToArray());
            return Processing.Produce(operation, output, () => GdalApi.wrapper_GDALTranslate(output, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_warp", Title = "Reproject, resample or mosaic rasters (gdalwarp)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Reprojects, resamples, mosaics and clips rasters with the native gdalwarp utility. Use it to change the coordinate reference system, the resolution or the extent, " +
        "to align a raster to a grid, to mosaic rasters with different projections and to clip a raster with a vector cutline. " +
        "The warp processes the raster in chunks, so inputs that are larger than memory are supported.")]
    public Task<ProcessingResult> WarpAsync(
        RasterWarpRequest request,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_warp", progress, cancellationToken, operation =>
        {
            return Warp(request, operation);
        });
    }

    [McpServerTool(Name = "gdal_raster_crop", Title = "Crop or clip a raster", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Crops a raster to a bounding box or a pixel window, or clips it to the polygons of a vector dataset. Pass exactly one of bounds, pixelWindow or cutline. " +
        "A bounding box and a pixel window keep the original pixel grid. A cutline masks the pixels outside the polygons.")]
    public Task<ProcessingResult> CropAsync(
        [Description("Input raster path.")] string input,
        [Description("Output raster path.")] string output,
        [Description("Bounding box as [minX, minY, maxX, maxY].")] double[]? bounds = null,
        [Description("Coordinate reference system of bounds when it differs from the raster.")] string? boundsCrs = null,
        [Description("Pixel window as [xOffset, yOffset, width, height].")] double[]? pixelWindow = null,
        [Description("Vector dataset whose polygons define the area to keep.")] string? cutline = null,
        [Description("Layer of the cutline dataset.")] string? cutlineLayer = null,
        [Description("Attribute filter that selects the cutline features.")] string? cutlineWhere = null,
        [Description("NoData value for pixels outside the cutline. An alpha band is added when the value is omitted and the raster has no NoData value.")] string? noData = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_crop", progress, cancellationToken, operation =>
        {
            var selections = (bounds is null ? 0 : 1) + (pixelWindow is null ? 0 : 1) + (cutline is null ? 0 : 1);
            if (selections != 1)
            {
                throw new McpException("Pass exactly one of bounds, pixelWindow or cutline.");
            }

            if (cutline is not null)
            {
                var hasNoData = noData is not null;
                if (!hasNoData)
                {
                    using var probe = Open.Raster(context.Paths.Input(input));
                    using var band = probe.GetRasterBand(1);
                    band.GetNoDataValue(out _, out var sourceHasNoData);
                    hasNoData = sourceHasNoData != 0;
                }

                return Warp(
                    new RasterWarpRequest
                    {
                        Inputs = [input],
                        Output = output,
                        Overwrite = overwrite,
                        Format = format,
                        Cutline = cutline,
                        CutlineLayer = cutlineLayer,
                        CutlineWhere = cutlineWhere,
                        CropToCutline = true,
                        DestinationNoData = noData,
                        DestinationAlpha = !hasNoData,
                        CreationOptions = creationOptions
                    },
                    operation);
            }

            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Option("-of", format)
                .Numbers("-srcwin", pixelWindow, 4, "pixelWindow")
                .Option("-projwin_srs", boundsCrs)
                .Option("-a_nodata", noData)
                .Each("-co", creationOptions);
            if (bounds is not null)
            {
                if (bounds.Length != 4)
                {
                    throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
                }

                arguments.Numbers("-projwin", [bounds[0], bounds[3], bounds[2], bounds[1]], 4, "bounds");
            }

            using var source = Open.Raster(resolvedInput);
            using var options = new GDALTranslateOptions(arguments.ToArray());
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.wrapper_GDALTranslate(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_cog", Title = "Create a Cloud Optimized GeoTIFF", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Writes a raster as a Cloud Optimized GeoTIFF with internal tiling and overviews, using the GDAL COG driver. " +
        "Pass targetCrs or tilingScheme to reproject while writing. Validate the result with gdal_algorithm_run and the algorithm 'driver cog validate'.")]
    public Task<ProcessingResult> CogAsync(
        [Description("Input raster path.")] string input,
        [Description("Output path of the Cloud Optimized GeoTIFF.")] string output,
        [Description("Compression: NONE, LZW, DEFLATE, ZSTD, JPEG, WEBP, LERC, LERC_DEFLATE, LERC_ZSTD or JXL.")] string compression = "DEFLATE",
        [Description("Tile size in pixels.")] int blockSize = 512,
        [Description("Compression level for DEFLATE, ZSTD or LZMA.")] int? level = null,
        [Description("Quality from 1 to 100 for JPEG or WEBP compression.")] int? quality = null,
        [Description("Predictor: YES, NO, STANDARD or FLOATING_POINT.")] string? predictor = null,
        [Description("Resampling method for the overviews, for example nearest, average, bilinear or cubic.")] string? overviewResampling = null,
        [Description("Reprojects the raster to this coordinate reference system.")] string? targetCrs = null,
        [Description("Tiling scheme to align the output to, for example GoogleMapsCompatible.")] string? tilingScheme = null,
        [Description("Resampling method used when the raster is reprojected.")] string? warpResampling = null,
        [Description("BigTIFF mode: YES, NO, IF_NEEDED or IF_SAFER.")] string bigTiff = "IF_SAFER",
        [Description("Further COG creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Open options of the input driver in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_cog", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Add("-of", "COG")
                .Add("-co", $"COMPRESS={compression}")
                .Add("-co", $"BLOCKSIZE={blockSize}")
                .Add("-co", $"BIGTIFF={bigTiff}")
                .Add("-co", "NUM_THREADS=ALL_CPUS");
            (string Name, string? Value)[] optional =
            [
                ("LEVEL", level?.ToString(CultureInfo.InvariantCulture)),
                ("QUALITY", quality?.ToString(CultureInfo.InvariantCulture)),
                ("PREDICTOR", predictor),
                ("OVERVIEW_RESAMPLING", overviewResampling),
                ("TARGET_SRS", targetCrs),
                ("TILING_SCHEME", tilingScheme),
                ("WARP_RESAMPLING", warpResampling)
            ];
            foreach (var (name, value) in optional)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    arguments.Add("-co", $"{name}={value}");
                }
            }

            arguments.Each("-co", creationOptions);
            using var source = Open.Raster(resolvedInput, false, openOptions);
            using var options = new GDALTranslateOptions(arguments.ToArray());
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.wrapper_GDALTranslate(resolvedOutput, source, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_create", Title = "Create a new raster", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a new raster with a given size, band count, data type, georeferencing and fill value, like gdal_create. " +
        "Pass template to copy the size, the georeferencing and the coordinate reference system of an existing raster.")]
    public Task<ProcessingResult> CreateAsync(
        [Description("Output raster path.")] string output,
        [Description("Width in pixels. Taken from the template when omitted.")] int? width = null,
        [Description("Height in pixels. Taken from the template when omitted.")] int? height = null,
        [Description("Number of bands.")] int bandCount = 1,
        [Description("Data type: Byte, Int8, UInt16, Int16, UInt32, Int32, UInt64, Int64, Float32, Float64, CInt16, CInt32, CFloat32 or CFloat64.")] string dataType = "Byte",
        [Description("Coordinate reference system, for example EPSG:32755.")] string? crs = null,
        [Description("Extent as [minX, minY, maxX, maxY]. The pixel size is derived from the extent and the raster size.")] double[]? bounds = null,
        [Description("NoData value of every band.")] double? noData = null,
        [Description("Initial value of each band. A single value is used for every band.")] double[]? fillValues = null,
        [Description("Existing raster to copy the size, the georeferencing and the coordinate reference system from.")] string? template = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_create", cancellationToken, operation =>
        {
            var resolvedOutput = context.Paths.Output(output, overwrite);
            using var templateDataset = template is null ? null : Open.Raster(context.Paths.Input(template));
            var rasterWidth = width ?? templateDataset?.RasterXSize ?? throw new McpException("Pass width and height, or a template raster.");
            var rasterHeight = height ?? templateDataset?.RasterYSize ?? throw new McpException("Pass width and height, or a template raster.");
            if (rasterWidth < 1 || rasterHeight < 1 || bandCount < 1)
            {
                throw new McpException("The width, the height and the band count must be at least 1.");
            }

            var type = GdalApi.GetDataTypeByName(dataType);
            if (type == DataType.GDT_Unknown)
            {
                throw new McpException($"'{dataType}' is not a GDAL data type.");
            }

            if (bounds is not null && bounds.Length != 4)
            {
                throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
            }

            var driver = Processing.DriverForOutput(resolvedOutput, format, false);
            return Processing.Produce(operation, resolvedOutput, () =>
            {
                var dataset = driver.Create(resolvedOutput, rasterWidth, rasterHeight, bandCount, type, creationOptions);
                using var cleanup = new DisposeOnFailure(dataset);
                if (bounds is not null)
                {
                    dataset.SetGeoTransform([bounds[0], (bounds[2] - bounds[0]) / rasterWidth, 0, bounds[3], 0, -(bounds[3] - bounds[1]) / rasterHeight]);
                }
                else if (templateDataset is not null)
                {
                    var geoTransform = new double[6];
                    templateDataset.GetGeoTransform(geoTransform);
                    dataset.SetGeoTransform(geoTransform);
                }

                using var srs = crs is not null ? Crs.Parse(crs) : templateDataset?.GetSpatialRef();
                if (srs is not null)
                {
                    dataset.SetSpatialRef(srs);
                }

                for (var number = 1; number <= bandCount; number++)
                {
                    using var band = dataset.GetRasterBand(number);
                    if (noData is not null)
                    {
                        band.SetNoDataValue(noData.Value);
                    }

                    if (fillValues is { Length: > 0 })
                    {
                        band.Fill(fillValues[Math.Min(number - 1, fillValues.Length - 1)], 0);
                    }
                }

                cleanup.Completed = true;
                return dataset;
            });
        });
    }

    [McpServerTool(Name = "gdal_raster_align", Title = "Align a raster to a reference grid", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Warps a raster so that it has exactly the coordinate reference system, the extent, the resolution and the size of a reference raster. " +
        "Use it before pixel-wise calculations between rasters that come from different sources.")]
    public Task<ProcessingResult> AlignAsync(
        [Description("Input raster path.")] string input,
        [Description("Reference raster whose grid the output adopts.")] string reference,
        [Description("Output raster path.")] string output,
        [Description("Resampling method: near, bilinear, cubic, cubicspline, lanczos, average, rms, mode, max, min, med, q1, q3 or sum.")] string resampling = "near",
        [Description("NoData value of the output bands.")] string? destinationNoData = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_align", progress, cancellationToken, operation =>
        {
            string wkt;
            Bounds bounds;
            int[] size;
            using (var referenceDataset = Open.Raster(context.Paths.Input(reference)))
            {
                bounds = Describe.GeoBounds(referenceDataset, out var geoTransform)
                    ?? throw new McpException("The reference raster is not georeferenced.");
                if (geoTransform[2] != 0 || geoTransform[4] != 0)
                {
                    throw new McpException("The reference raster is rotated, which this tool does not support. Use gdal_raster_warp with explicit parameters.");
                }

                using var srs = referenceDataset.GetSpatialRef()
                    ?? throw new McpException("The reference raster has no coordinate reference system.");
                wkt = Crs.Wkt(srs);
                size = [referenceDataset.RasterXSize, referenceDataset.RasterYSize];
            }

            return Warp(
                new RasterWarpRequest
                {
                    Inputs = [input],
                    Output = output,
                    Overwrite = overwrite,
                    Format = format,
                    TargetCrs = wkt,
                    Bounds = [bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY],
                    Size = size,
                    Resampling = resampling,
                    DestinationNoData = destinationNoData,
                    CreationOptions = creationOptions
                },
                operation);
        });
    }

    private ProcessingResult Warp(RasterWarpRequest request, GdalOperation operation)
    {
        if (request.Inputs.Length == 0)
        {
            throw new McpException("At least one input raster is required.");
        }

        var inputs = context.Paths.Inputs(request.Inputs);
        var output = context.Paths.Output(request.Output, request.Overwrite);
        var arguments = new ArgList()
            .Option("-of", request.Format)
            .Option("-t_srs", request.TargetCrs)
            .Option("-s_srs", request.SourceCrs)
            .Numbers("-tr", request.Resolution, 2, "resolution")
            .Numbers("-te", request.Bounds, 4, "bounds")
            .Option("-te_srs", request.BoundsCrs)
            .Flag(request.TargetAlignedPixels, "-tap")
            .Option("-r", request.Resampling)
            .Option("-srcnodata", request.SourceNoData)
            .Option("-dstnodata", request.DestinationNoData)
            .Flag(request.DestinationAlpha, "-dstalpha")
            .Flag(request.SourceAlpha, "-srcalpha")
            .Option("-ot", request.OutputType)
            .Each("-b", request.Bands?.Select(band => band.ToString(CultureInfo.InvariantCulture)))
            .Option("-cutline", request.Cutline is null ? null : context.Paths.Input(request.Cutline))
            .Option("-cl", request.CutlineLayer)
            .Option("-cwhere", request.CutlineWhere)
            .Flag(request.CropToCutline, "-crop_to_cutline")
            .Option("-cblend", request.CutlineBlend)
            .Option("-ovr", request.OverviewLevel)
            .Flag(request.Multithreaded, "-multi")
            .Option("-wm", request.WarpMemoryMegabytes)
            .Option("-et", request.ErrorThreshold)
            .Each("-wo", request.WarpOptions)
            .Each("-to", request.TransformerOptions)
            .Each("-co", request.CreationOptions);

        if (request.Size is { Length: > 0 })
        {
            arguments.Numbers("-ts", [.. request.Size.Select(value => (double)value)], 2, "size");
        }

        arguments.Extra(request.AdditionalArguments, context.Paths);
        var sources = new List<Dataset>();
        try
        {
            foreach (var input in inputs)
            {
                sources.Add(Open.Raster(input, false, request.OpenOptions));
            }

            using var options = new GDALWarpAppOptions(arguments.ToArray());
            return Processing.Produce(operation, output, () => GdalApi.Warp(output, [.. sources], options, operation.Progress, null));
        }
        finally
        {
            sources.ForEach(source => source.Dispose());
        }
    }
}
