namespace InFarm.Mcp.Gdal.Tools;

public sealed record RetileResult
{
    public required string Directory { get; init; }

    public int TileCount { get; init; }

    [Description("Number of pyramid levels written in addition to the full-resolution level.")]
    public int Levels { get; init; }

    public string? TileIndex { get; init; }

    [Description("Paths of the first tiles.")]
    public required IReadOnlyList<string> SampleTiles { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

// Virtual rasters, mosaics, tile sets and tile indexes.
[McpServerToolType]
public sealed class VrtTools(GdalToolContext context)
{
    private const int MaxSampleTiles = 20;

    [McpServerTool(Name = "gdal_raster_build_vrt", Title = "Build a virtual raster (gdalbuildvrt)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Builds a VRT virtual raster that mosaics or stacks several rasters without copying pixel data, with the native gdalbuildvrt utility. " +
        "The VRT can be passed as the input of any other raster tool. Pass separate=true to stack the inputs as bands.")]
    public Task<ProcessingResult> BuildVrtAsync(
        [Description("Input raster paths. Later inputs are drawn over earlier ones.")] string[] inputs,
        [Description("Output path of the VRT, usually with the .vrt extension. A path under /vsimem/ keeps the VRT in memory for the session.")] string output,
        [Description("Places each input in its own band instead of mosaicking the inputs.")] bool separate = false,
        [Description("How the output resolution is chosen: highest, lowest, average, same or user. With user, pass resolution.")] string? resolutionStrategy = null,
        [Description("Output resolution as [xResolution, yResolution].")] double[]? resolution = null,
        [Description("Output extent as [minX, minY, maxX, maxY].")] double[]? bounds = null,
        [Description("Aligns the output extent to multiples of the resolution.")] bool targetAlignedPixels = false,
        [Description("Input bands to use, starting at 1.")] int[]? bands = null,
        [Description("NoData values of the inputs, separated by spaces.")] string? sourceNoData = null,
        [Description("NoData values of the VRT bands, separated by spaces.")] string? vrtNoData = null,
        [Description("Adds an alpha band that marks the pixels covered by an input.")] bool addAlpha = false,
        [Description("Resampling method: nearest, bilinear, cubic, cubicspline, lanczos, average or mode.")] string? resampling = null,
        [Description("Overrides the coordinate reference system of the output.")] string? assignCrs = null,
        [Description("Accepts inputs whose coordinate reference systems differ, without reprojecting them.")] bool allowProjectionDifference = false,
        [Description("Additional gdalbuildvrt arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_build_vrt", progress, cancellationToken, operation =>
        {
            var resolvedInputs = ResolveInputs(inputs);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Flag(separate, "-separate")
                .Option("-resolution", resolutionStrategy ?? (resolution is null ? null : "user"))
                .Numbers("-tr", resolution, 2, "resolution")
                .Numbers("-te", bounds, 4, "bounds")
                .Flag(targetAlignedPixels, "-tap")
                .Each("-b", bands?.Select(band => band.ToString(CultureInfo.InvariantCulture)))
                .Option("-srcnodata", sourceNoData)
                .Option("-vrtnodata", vrtNoData)
                .Flag(addAlpha, "-addalpha")
                .Option("-r", resampling)
                .Option("-a_srs", assignCrs)
                .Flag(allowProjectionDifference, "-allow_projection_difference")
                .Extra(additionalArguments, context.Paths);
            using var options = new GDALBuildVRTOptions(arguments.ToArray());
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.BuildVRT(resolvedOutput, resolvedInputs, options, operation.Progress, null));
        });
    }

    [McpServerTool(Name = "gdal_raster_mosaic", Title = "Mosaic or stack rasters into one file", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Merges several rasters that share a coordinate reference system into one output file, like gdal_merge. Pass separate=true to stack the inputs as bands instead. " +
        "The inputs are combined through a temporary virtual raster, so the pixel data is streamed and never held in memory. " +
        "Use gdal_raster_warp to mosaic rasters that have different coordinate reference systems.")]
    public Task<ProcessingResult> MosaicAsync(
        [Description("Input raster paths. Later inputs are drawn over earlier ones.")] string[] inputs,
        [Description("Output raster path.")] string output,
        [Description("Places each input in its own band instead of mosaicking the inputs.")] bool separate = false,
        [Description("How the output resolution is chosen: highest, lowest, average or user. With user, pass resolution.")] string? resolutionStrategy = null,
        [Description("Output resolution as [xResolution, yResolution].")] double[]? resolution = null,
        [Description("Output extent as [minX, minY, maxX, maxY].")] double[]? bounds = null,
        [Description("NoData values of the inputs, separated by spaces.")] string? sourceNoData = null,
        [Description("NoData values of the output bands, separated by spaces.")] string? outputNoData = null,
        [Description("Adds an alpha band that marks the pixels covered by an input.")] bool addAlpha = false,
        [Description("Output data type.")] string? outputType = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_mosaic", progress, cancellationToken, operation =>
        {
            var resolvedInputs = ResolveInputs(inputs);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var vrtArguments = new ArgList()
                .Flag(separate, "-separate")
                .Option("-resolution", resolutionStrategy ?? (resolution is null ? null : "user"))
                .Numbers("-tr", resolution, 2, "resolution")
                .Numbers("-te", bounds, 4, "bounds")
                .Option("-srcnodata", sourceNoData)
                .Option("-vrtnodata", outputNoData)
                .Flag(addAlpha, "-addalpha");
            var translateArguments = new ArgList()
                .Option("-of", format)
                .Option("-ot", outputType)
                .Each("-co", creationOptions);

            var temporaryVrt = VsiTemp.Create(".vrt");
            try
            {
                using var vrtOptions = new GDALBuildVRTOptions(vrtArguments.ToArray());
                using var vrt = GdalApi.BuildVRT(temporaryVrt, resolvedInputs, vrtOptions, null, null)
                    ?? throw new McpException("The inputs could not be combined. Check that they share one coordinate reference system and one band layout.");
                using var translateOptions = new GDALTranslateOptions(translateArguments.ToArray());
                return Processing.Produce(operation, resolvedOutput, () => GdalApi.wrapper_GDALTranslate(resolvedOutput, vrt, translateOptions, operation.Progress, null));
            }
            finally
            {
                VsiTemp.Delete(temporaryVrt);
            }
        });
    }

    [McpServerTool(Name = "gdal_raster_retile", Title = "Cut a raster into tiles", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Cuts a raster into regular tiles written as separate files in a directory, optionally with reduced-resolution pyramid levels and a tile index, like gdal_retile. " +
        "Build a VRT with gdal_raster_build_vrt first when the source consists of several files. " +
        "For web map tiles in XYZ or TMS layout, run the algorithm 'raster tile' through gdal_algorithm_run instead.")]
    public Task<RetileResult> RetileAsync(
        [Description("Input raster or VRT path.")] string input,
        [Description("Directory that receives the tiles. Pyramid levels are written to subdirectories named 1, 2 and so on.")] string outputDirectory,
        [Description("Tile width in pixels.")] int tileWidth = 1024,
        [Description("Tile height in pixels.")] int tileHeight = 1024,
        [Description("Overlap between neighbouring tiles in pixels.")] int overlap = 0,
        [Description("Number of pyramid levels to write. Each level halves the resolution of the previous one.")] int levels = 0,
        [Description("Resampling method for the pyramid levels: nearest, bilinear, cubic, cubicspline, lanczos, average or mode.")] string resampling = "average",
        [Description("Driver short name of the tiles.")] string format = "GTiff",
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Path of a vector tile index to create for the full-resolution tiles, for example tiles.gpkg.")] string? tileIndex = null,
        [Description("Replaces tiles that already exist.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_retile", progress, cancellationToken, operation =>
        {
            if (tileWidth < 1 || tileHeight < 1 || overlap < 0 || overlap >= Math.Min(tileWidth, tileHeight) || levels is < 0 or > 20)
            {
                throw new McpException("The tile size must be positive, the overlap must be smaller than the tile size and levels must be between 0 and 20.");
            }

            using var source = Open.Raster(context.Paths.Input(input));
            using var driver = DriverTools.GetDriver(format);
            var extension = (driver.GetMetadataItem(GdalConst.DMD_EXTENSION, null) ?? "tif").Split(' ')[0];
            var baseName = Path.GetFileNameWithoutExtension(input);
            var samples = new List<string>();
            var baseTiles = new List<string>();
            var tileCount = 0;
            var totalTiles = Enumerable.Range(0, levels + 1).Sum(level => CountTiles(source, tileWidth, tileHeight, overlap, 1 << level));

            for (var level = 0; level <= levels; level++)
            {
                var factor = 1 << level;
                var directory = level == 0 ? outputDirectory : Path.Combine(outputDirectory, level.ToString(CultureInfo.InvariantCulture));
                var stepX = (tileWidth - overlap) * factor;
                var stepY = (tileHeight - overlap) * factor;
                for (int yOffset = 0, row = 1; yOffset < source.RasterYSize; yOffset += stepY, row++)
                {
                    for (int xOffset = 0, column = 1; xOffset < source.RasterXSize; xOffset += stepX, column++)
                    {
                        operation.ThrowIfCancelled();
                        var sourceWidth = Math.Min(tileWidth * factor, source.RasterXSize - xOffset);
                        var sourceHeight = Math.Min(tileHeight * factor, source.RasterYSize - yOffset);
                        var tile = context.Paths.Output(Path.Combine(directory, $"{baseName}_{row:D4}_{column:D4}.{extension}"), overwrite);
                        var arguments = new ArgList()
                            .Add("-of", format)
                            .Numbers("-srcwin", [xOffset, yOffset, sourceWidth, sourceHeight], 4, "window")
                            .Each("-co", creationOptions);
                        if (level > 0)
                        {
                            arguments
                                .Numbers("-outsize", [Math.Max(1, Math.Ceiling(sourceWidth / (double)factor)), Math.Max(1, Math.Ceiling(sourceHeight / (double)factor))], 2, "size")
                                .Add("-r", resampling);
                        }

                        using var options = new GDALTranslateOptions(arguments.ToArray());
                        using (var result = GdalApi.wrapper_GDALTranslate(tile, source, options, null, null))
                        {
                            result?.FlushCache();
                        }

                        tileCount++;
                        operation.Progress(tileCount / (double)totalTiles, IntPtr.Zero, IntPtr.Zero);
                        if (level == 0)
                        {
                            baseTiles.Add(tile);
                        }

                        if (samples.Count < MaxSampleTiles)
                        {
                            samples.Add(tile);
                        }
                    }
                }
            }

            string? resolvedIndex = null;
            if (tileIndex is not null)
            {
                resolvedIndex = context.Paths.Output(tileIndex, overwrite);
                using var indexOptions = new GDALTileIndexOptions([]);
                using var index = GdalApi.wrapper_TileIndex_names(resolvedIndex, [.. baseTiles], indexOptions, null, null);
                index?.FlushCache();
            }

            return new RetileResult
            {
                Directory = Path.GetFullPath(outputDirectory),
                TileCount = tileCount,
                Levels = levels,
                TileIndex = resolvedIndex,
                SampleTiles = samples,
                Warnings = operation.WarningsOrNull
            };
        });
    }

    [McpServerTool(Name = "gdal_raster_tile_index", Title = "Build a raster tile index (gdaltindex)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a vector dataset with one polygon footprint for each input raster and the raster path as an attribute, with the native gdaltindex utility. " +
        "The index can be opened as a mosaic by the GTI driver and by MapServer.")]
    public Task<ProcessingResult> TileIndexAsync(
        [Description("Input raster paths.")] string[] inputs,
        [Description("Output vector dataset path, for example index.gpkg or index.fgb.")] string output,
        [Description("Output driver short name.")] string? format = null,
        [Description("Name of the index layer.")] string? layerName = null,
        [Description("Name of the field that receives the raster path.")] string? locationField = null,
        [Description("Reprojects the footprints to this coordinate reference system.")] string? targetCrs = null,
        [Description("Name of a field that receives the coordinate reference system of each raster.")] string? sourceCrsField = null,
        [Description("Writes absolute raster paths.")] bool absolutePaths = false,
        [Description("Additional gdaltindex arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_tile_index", progress, cancellationToken, operation =>
        {
            var resolvedInputs = ResolveInputs(inputs);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Option("-f", format)
                .Option("-lyr_name", layerName)
                .Option("-tileindex", locationField)
                .Option("-t_srs", targetCrs)
                .Option("-src_srs_name", sourceCrsField)
                .Flag(absolutePaths, "-write_absolute_path")
                .Extra(additionalArguments, context.Paths);
            using var options = new GDALTileIndexOptions(arguments.ToArray());
            return Processing.Produce(operation, resolvedOutput, () => GdalApi.wrapper_TileIndex_names(resolvedOutput, resolvedInputs, options, operation.Progress, null));
        });
    }

    private string[] ResolveInputs(string[] inputs)
    {
        if (inputs.Length == 0)
        {
            throw new McpException("At least one input raster is required.");
        }

        return context.Paths.Inputs(inputs);
    }

    private static int CountTiles(Dataset source, int tileWidth, int tileHeight, int overlap, int factor)
    {
        var columns = (int)Math.Ceiling(source.RasterXSize / (double)((tileWidth - overlap) * factor));
        var rows = (int)Math.Ceiling(source.RasterYSize / (double)((tileHeight - overlap) * factor));
        return columns * rows;
    }
}
