namespace InFarm.Mcp.Gdal.Tools;

// Raster inspection tools, which never modify the dataset unless a parameter asks for statistics to be saved.
[McpServerToolType]
public sealed class RasterTools(GdalToolContext context)
{
    private const int DefaultMaxBands = 32;
    private const int MaxCategoryNames = 256;

    [McpServerTool(Name = "gdal_raster_info", Title = "Describe a raster", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Returns the structure of a raster dataset: size, coordinate reference system, geotransform, extent, and for each band the data type, NoData value, " +
        "block size, color interpretation, scale and offset, overviews, mask flags and optional statistics. This is the structured equivalent of gdalinfo.")]
    public Task<RasterInfo> InfoAsync(
        [Description("Raster dataset path, virtual path or subdataset name.")] string path,
        [Description("Statistics to include: none, cached (only statistics that are already stored), approximate (computed from overviews or samples) or exact (reads every pixel).")] string statistics = "cached",
        [Description("Band numbers to describe, starting at 1. The first 32 bands are described when omitted.")] int[]? bands = null,
        [Description("Includes dataset and band metadata of the default domain.")] bool includeMetadata = false,
        [Description("Includes the WKT of the coordinate reference system.")] bool includeWkt = false,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_info", progress, cancellationToken, operation =>
        {
            var resolved = context.Paths.Input(path);
            var computes = statistics.ToLowerInvariant() is "approximate" or "exact";
            if (computes)
            {
                // Computed statistics would otherwise be written to an .aux.xml file when the dataset closes.
                GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", "NO");
            }

            try
            {
                return DescribeRaster(resolved, statistics, bands, includeMetadata, includeWkt, openOptions, operation);
            }
            finally
            {
                if (computes)
                {
                    GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", null);
                }
            }
        });
    }

    private static RasterInfo DescribeRaster(
        string resolved, string statistics, int[]? bands, bool includeMetadata, bool includeWkt, string[]? openOptions, GdalOperation operation)
    {
        {
            using var dataset = Open.Raster(resolved, false, openOptions);
            using var driver = dataset.GetDriver();
            using var srs = dataset.GetSpatialRef();
            var bounds = Describe.GeoBounds(dataset, out var geoTransform);
            var bandNumbers = bands ?? [.. Enumerable.Range(1, Math.Min(dataset.RasterCount, DefaultMaxBands))];
            var bandInfos = new List<BandInfo>();
            foreach (var number in bandNumbers)
            {
                operation.ThrowIfCancelled();
                using var band = GetBand(dataset, number);
                bandInfos.Add(DescribeBand(band, number, statistics, includeMetadata, operation));
            }

            return new RasterInfo
            {
                Path = resolved,
                Driver = driver.ShortName,
                Width = dataset.RasterXSize,
                Height = dataset.RasterYSize,
                BandCount = dataset.RasterCount,
                Crs = Crs.Summarize(srs, includeWkt),
                GeoTransform = bounds is null ? null : geoTransform,
                Bounds = bounds,
                Wgs84Bounds = Wgs84Bounds(dataset),
                PixelSize = bounds is null ? null : [Math.Abs(geoTransform[1]), Math.Abs(geoTransform[5])],
                GcpCount = dataset.GetGCPCount(),
                SubdatasetCount = (dataset.GetMetadata("SUBDATASETS")?.Length ?? 0) / 2,
                Files = dataset.GetFileList(),
                MetadataDomains = dataset.GetMetadataDomainList(),
                Metadata = includeMetadata ? Describe.KeyValues(dataset.GetMetadata(null)) : null,
                ImageStructure = Describe.KeyValues(dataset.GetMetadata("IMAGE_STRUCTURE")),
                Bands = bandInfos,
                BandsTruncated = bands is null && dataset.RasterCount > DefaultMaxBands
            };
        }
    }

    [McpServerTool(Name = "gdal_raster_info_json", Title = "Run gdalinfo and return its JSON", ReadOnly = true, Idempotent = true)]
    [Description(
        "Runs the native gdalinfo report on a raster and returns its JSON text unchanged. Use this tool for details that gdal_raster_info does not model, " +
        "such as corner coordinates, checksums, RPC metadata, color tables and raster attribute tables.")]
    public Task<string> InfoJsonAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Computes exact statistics for every band.")] bool statistics = false,
        [Description("Computes approximate statistics from overviews or samples.")] bool approximateStatistics = false,
        [Description("Computes the minimum and maximum of every band.")] bool minMax = false,
        [Description("Includes a histogram for every band.")] bool histogram = false,
        [Description("Computes a checksum for every band.")] bool checksum = false,
        [Description("Lists the metadata domains.")] bool listMetadataDomains = false,
        [Description("Metadata domains to report in addition to the default domain. Use 'all' for every domain.")] string[]? metadataDomains = null,
        [Description("Omits metadata.")] bool noMetadata = false,
        [Description("Omits color tables and raster attribute tables.")] bool noTables = false,
        [Description("WKT format of the coordinate system: WKT1, WKT2, WKT2_2015 or WKT2_2019.")] string? wktFormat = null,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Additional gdalinfo arguments.")] string[]? additionalArguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_info_json", progress, cancellationToken, _ =>
        {
            var arguments = new ArgList()
                .Add("-json")
                .Flag(statistics, "-stats")
                .Flag(approximateStatistics, "-approx_stats")
                .Flag(minMax, "-mm")
                .Flag(histogram, "-hist")
                .Flag(checksum, "-checksum")
                .Flag(listMetadataDomains, "-listmdd")
                .Each("-mdd", metadataDomains)
                .Flag(noMetadata, "-nomd")
                .Flag(noTables, "-noct")
                .Flag(noTables, "-norat")
                .Option("-wkt_format", wktFormat)
                .Extra(additionalArguments, context.Paths);

            using var dataset = Open.Raster(context.Paths.Input(path), false, openOptions);
            using var options = new GDALInfoOptions(arguments.ToArray());
            return Limits.EnsureText(
                GdalApi.GDALInfo(dataset, options),
                context.Options.MaxResponseCharacters,
                "Pass noMetadata=true or noTables=true, or use gdal_raster_info with a band selection.");
        });
    }

    [McpServerTool(Name = "gdal_raster_statistics", Title = "Compute raster band statistics", ReadOnly = false, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Computes the minimum, maximum, mean and standard deviation of raster bands with GDAL block-based statistics. " +
        "The dataset is left untouched unless save=true, in which case the statistics are stored in the dataset or in its .aux.xml sidecar file.")]
    public Task<StatisticsResult> StatisticsAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Band numbers, starting at 1. Every band is processed when omitted.")] int[]? bands = null,
        [Description("Computes the statistics from overviews or a subset of blocks, which is much faster on large rasters.")] bool approximate = false,
        [Description("Stores the statistics with the dataset so that later calls can reuse them.")] bool save = false,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_statistics", progress, cancellationToken, operation =>
        {
            var resolved = save ? context.Paths.Update(path) : context.Paths.Input(path);
            if (!save)
            {
                // Disabling the persistent auxiliary metadata for this thread stops GDAL from writing an .aux.xml file when the dataset closes.
                GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", "NO");
            }

            try
            {
                using var dataset = Open.Raster(resolved, false, openOptions);
                var results = new List<BandStatistics>();
                foreach (var number in bands ?? [.. Enumerable.Range(1, dataset.RasterCount)])
                {
                    using var band = GetBand(dataset, number);
                    band.ComputeStatistics(approximate, out var min, out var max, out var mean, out var stdDev, operation.Progress, null);
                    operation.ThrowIfCancelled();
                    results.Add(new BandStatistics
                    {
                        Band = number,
                        Min = Num.Finite(min),
                        Max = Num.Finite(max),
                        Mean = Num.Finite(mean),
                        StdDev = Num.Finite(stdDev),
                        ValidPercent = double.TryParse(band.GetMetadataItem("STATISTICS_VALID_PERCENT", null), CultureInfo.InvariantCulture, out var valid) ? valid : null,
                        Approximate = approximate
                    });
                }

                return new StatisticsResult { Path = resolved, Bands = results, Warnings = operation.WarningsOrNull };
            }
            finally
            {
                GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", null);
            }
        });
    }

    [McpServerTool(Name = "gdal_raster_histogram", Title = "Compute a raster band histogram", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Computes the histogram of one raster band. GDAL chooses the range and the bucket count when min and max are omitted.")]
    public Task<HistogramResult> HistogramAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Band number, starting at 1.")] int band = 1,
        [Description("Lower bound of the histogram. Omit both bounds to let GDAL choose them.")] double? min = null,
        [Description("Upper bound of the histogram.")] double? max = null,
        [Description("Number of buckets when min and max are given.")] int buckets = 256,
        [Description("Counts values outside the range in the first and last buckets.")] bool includeOutOfRange = false,
        [Description("Allows GDAL to compute the histogram from overviews or samples.")] bool approximate = true,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_histogram", progress, cancellationToken, operation =>
        {
            if (min is null != max is null)
            {
                throw new McpException("Pass both min and max, or neither of them.");
            }

            if (buckets is < 1 or > 65536)
            {
                throw new McpException("The bucket count must be between 1 and 65536.");
            }

            GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", "NO");
            try
            {
                using var dataset = Open.Raster(context.Paths.Input(path), false, openOptions);
                using var rasterBand = GetBand(dataset, band);
                int[] counts;
                double low;
                double high;
                if (min is null)
                {
                    rasterBand.GetDefaultHistogram(out low, out high, out _, out counts, 1, operation.Progress, null);
                }
                else
                {
                    low = min.Value;
                    high = max!.Value;
                    counts = new int[buckets];
                    rasterBand.GetHistogram(low, high, buckets, counts, includeOutOfRange ? 1 : 0, approximate ? 1 : 0, operation.Progress, null);
                }

                operation.ThrowIfCancelled();
                return new HistogramResult
                {
                    Band = band,
                    Min = low,
                    Max = high,
                    BucketCount = counts.Length,
                    BucketWidth = counts.Length == 0 ? 0 : (high - low) / counts.Length,
                    Counts = counts
                };
            }
            finally
            {
                GdalApi.SetThreadLocalConfigOption("GDAL_PAM_ENABLED", null);
            }
        });
    }

    [McpServerTool(Name = "gdal_raster_pixel_values", Title = "Sample raster values at points", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Returns the band values of a raster at one or more points, like gdallocationinfo. Points are given in the raster coordinate reference system by default, " +
        "in pixel and line coordinates when coordinates='pixel', or in another coordinate reference system when crs is passed.")]
    public Task<PixelValuesResult> PixelValuesAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Points as [x, y] pairs. With a geographic coordinate reference system x is the longitude and y is the latitude.")] double[][] points,
        [Description("How the points are expressed: georeferenced or pixel.")] string coordinates = "georeferenced",
        [Description("Coordinate reference system of the points, for example EPSG:4326. The raster coordinate reference system is assumed when omitted.")] string? crs = null,
        [Description("Band numbers, starting at 1. Every band is sampled when omitted.")] int[]? bands = null,
        [Description("Interpolation method: nearest, bilinear, cubic or cubicspline.")] string interpolation = "nearest",
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_pixel_values", cancellationToken, operation =>
        {
            if (points.Length > context.Options.MaxFeatureCount)
            {
                throw new McpException($"At most {context.Options.MaxFeatureCount} points can be sampled in one call.");
            }

            var pixelMode = coordinates.ToLowerInvariant() switch
            {
                "pixel" => true,
                "georeferenced" => false,
                _ => throw new McpException("The coordinates parameter must be georeferenced or pixel.")
            };

            RIOResampleAlg? algorithm = interpolation.ToLowerInvariant() switch
            {
                "nearest" => null,
                "bilinear" => RIOResampleAlg.GRIORA_Bilinear,
                "cubic" => RIOResampleAlg.GRIORA_Cubic,
                "cubicspline" => RIOResampleAlg.GRIORA_CubicSpline,
                _ => throw new McpException("The interpolation must be nearest, bilinear, cubic or cubicspline.")
            };

            using var dataset = Open.Raster(context.Paths.Input(path), false, openOptions);
            var geoTransform = new double[6];
            var inverse = new double[6];
            dataset.GetGeoTransform(geoTransform);
            if (!pixelMode && GdalApi.InvGeoTransform(geoTransform, inverse) == 0)
            {
                throw new McpException("The geotransform of the raster cannot be inverted, so georeferenced points cannot be located. Use coordinates='pixel'.");
            }

            using var transformation = crs is null || pixelMode ? null : CreateTransformationToDataset(dataset, crs);
            var bandNumbers = bands ?? [.. Enumerable.Range(1, dataset.RasterCount)];
            var rasterBands = bandNumbers.Select(number => GetBand(dataset, number)).ToList();
            try
            {
                var samples = new List<PixelSample>(points.Length);
                var buffer = new double[1];
                foreach (var point in points)
                {
                    operation.ThrowIfCancelled();
                    if (point.Length < 2)
                    {
                        throw new McpException("Every point needs an x and a y value.");
                    }

                    double[] position = [point[0], point[1], 0];
                    transformation?.TransformPoint(position);
                    var pixel = position[0];
                    var line = position[1];
                    if (!pixelMode)
                    {
                        GdalApi.ApplyGeoTransform(inverse, position[0], position[1], out pixel, out line);
                    }

                    var inBounds = pixel >= 0 && line >= 0 && pixel < dataset.RasterXSize && line < dataset.RasterYSize;
                    List<double?>? values = null;
                    if (inBounds)
                    {
                        values = [];
                        foreach (var rasterBand in rasterBands)
                        {
                            if (algorithm is null)
                            {
                                rasterBand.ReadRaster((int)Math.Floor(pixel), (int)Math.Floor(line), 1, 1, buffer, 1, 1, 0, 0);
                                values.Add(Num.Finite(buffer[0]));
                            }
                            else
                            {
                                rasterBand.InterpolateAtPoint(pixel, line, algorithm.Value, out var real, out _);
                                values.Add(Num.Finite(real));
                            }
                        }
                    }

                    samples.Add(new PixelSample { X = point[0], Y = point[1], Pixel = pixel, Line = line, InBounds = inBounds, Values = values });
                }

                return new PixelValuesResult { Bands = bandNumbers, Samples = samples };
            }
            finally
            {
                rasterBands.ForEach(rasterBand => rasterBand.Dispose());
            }
        });
    }

    [McpServerTool(Name = "gdal_raster_read_window", Title = "Read a window of raster values", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Reads the pixel values of a rectangular window of one band, optionally resampled to a smaller grid. " +
        "The number of returned values is limited, so pass outputWidth and outputHeight to preview a large window at reduced resolution.")]
    public Task<WindowResult> ReadWindowAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Band number, starting at 1.")] int band = 1,
        [Description("Column of the left edge of the window.")] int xOffset = 0,
        [Description("Row of the top edge of the window.")] int yOffset = 0,
        [Description("Width of the window in pixels. The window extends to the right edge when omitted.")] int? width = null,
        [Description("Height of the window in pixels. The window extends to the bottom edge when omitted.")] int? height = null,
        [Description("Width of the returned grid. Defaults to the window width.")] int? outputWidth = null,
        [Description("Height of the returned grid. Defaults to the window height.")] int? outputHeight = null,
        [Description("Resampling used when the returned grid is smaller or larger than the window: nearest, bilinear, cubic, cubicspline, lanczos, average, mode, gauss or rms.")] string resampling = "nearest",
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_read_window", cancellationToken, _ =>
        {
            using var dataset = Open.Raster(context.Paths.Input(path), false, openOptions);
            using var rasterBand = GetBand(dataset, band);
            var windowWidth = width ?? dataset.RasterXSize - xOffset;
            var windowHeight = height ?? dataset.RasterYSize - yOffset;
            if (xOffset < 0 || yOffset < 0 || windowWidth < 1 || windowHeight < 1
                || xOffset + (long)windowWidth > dataset.RasterXSize || yOffset + (long)windowHeight > dataset.RasterYSize)
            {
                throw new McpException($"The window must lie inside the raster, which is {dataset.RasterXSize} by {dataset.RasterYSize} pixels.");
            }

            var gridWidth = outputWidth ?? windowWidth;
            var gridHeight = outputHeight ?? windowHeight;
            if (gridWidth < 1 || gridHeight < 1 || (long)gridWidth * gridHeight > context.Options.MaxWindowPixels)
            {
                throw new McpException(
                    $"The returned grid would hold {(long)gridWidth * gridHeight} values, and the limit is {context.Options.MaxWindowPixels}. " +
                    "Pass a smaller outputWidth and outputHeight, or read a smaller window.");
            }

            using var extra = new RasterIOExtraArg
            {
                nVersion = 1,
                eResampleAlg = ParseResampling(resampling)
            };
            var buffer = new double[gridWidth * gridHeight];
            rasterBand.ReadRaster(xOffset, yOffset, windowWidth, windowHeight, buffer, gridWidth, gridHeight, 0, 0, extra);
            rasterBand.GetNoDataValue(out var noData, out var hasNoData);
            var rows = new List<double?[]>(gridHeight);
            for (var row = 0; row < gridHeight; row++)
            {
                var values = new double?[gridWidth];
                for (var column = 0; column < gridWidth; column++)
                {
                    values[column] = Num.Finite(buffer[row * gridWidth + column]);
                }

                rows.Add(values);
            }

            return new WindowResult
            {
                Band = band,
                XOffset = xOffset,
                YOffset = yOffset,
                Width = windowWidth,
                Height = windowHeight,
                OutputWidth = gridWidth,
                OutputHeight = gridHeight,
                NoData = hasNoData != 0 ? Num.Finite(noData) : null,
                Rows = rows
            };
        });
    }

    [McpServerTool(Name = "gdal_raster_color_table", Title = "Read a raster color table", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the color table (palette) of a raster band and its category names.")]
    public Task<ColorTableResult> ColorTableAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Band number, starting at 1.")] int band = 1,
        [Description("Number of entries to skip.")] int offset = 0,
        [Description("Maximum number of entries to return.")] int limit = 256,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_color_table", cancellationToken, _ =>
        {
            using var dataset = Open.Raster(context.Paths.Input(path));
            using var rasterBand = GetBand(dataset, band);
            using var table = rasterBand.GetRasterColorTable()
                ?? throw new McpException($"Band {band} has no color table.");
            var entries = new List<ColorEntryModel>();
            var count = table.GetCount();
            for (var i = offset; i < Math.Min(count, offset + Math.Min(limit, context.Options.MaxFeatureCount)); i++)
            {
                using var entry = table.GetColorEntry(i);
                entries.Add(new ColorEntryModel(i, entry.c1, entry.c2, entry.c3, entry.c4));
            }

            return new ColorTableResult
            {
                Band = band,
                PaletteInterpretation = GdalApi.GetPaletteInterpretationName(table.GetPaletteInterpretation()),
                Total = count,
                Entries = entries
            };
        });
    }

    [McpServerTool(Name = "gdal_raster_attribute_table", Title = "Read a raster attribute table", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the columns and a page of rows of the raster attribute table of a band.")]
    public Task<AttributeTableResult> AttributeTableAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Band number, starting at 1.")] int band = 1,
        [Description("Number of rows to skip.")] int offset = 0,
        [Description("Maximum number of rows to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_attribute_table", cancellationToken, _ =>
        {
            using var dataset = Open.Raster(context.Paths.Input(path));
            using var rasterBand = GetBand(dataset, band);
            using var table = rasterBand.GetDefaultRAT()
                ?? throw new McpException($"Band {band} has no raster attribute table.");
            var columnCount = table.GetColumnCount();
            var columns = new List<AttributeTableColumn>();
            for (var column = 0; column < columnCount; column++)
            {
                columns.Add(new AttributeTableColumn(
                    table.GetNameOfCol(column),
                    table.GetTypeOfCol(column).ToString().Replace("GFT_", string.Empty),
                    table.GetUsageOfCol(column).ToString().Replace("GFU_", string.Empty)));
            }

            var rows = new List<object?[]>();
            var rowCount = table.GetRowCount();
            for (var row = offset; row < Math.Min(rowCount, offset + context.ClampLimit(limit)); row++)
            {
                var values = new object?[columnCount];
                for (var column = 0; column < columnCount; column++)
                {
                    values[column] = table.GetTypeOfCol(column) switch
                    {
                        RATFieldType.GFT_Integer => table.GetValueAsInt(row, column),
                        RATFieldType.GFT_Real => Num.Finite(table.GetValueAsDouble(row, column)),
                        RATFieldType.GFT_Boolean => table.GetValueAsBoolean(row, column),
                        _ => table.GetValueAsString(row, column)
                    };
                }

                rows.Add(values);
            }

            return new AttributeTableResult
            {
                Band = band,
                TableType = table.GetTableType() == RATTableType.GRTT_THEMATIC ? "thematic" : "athematic",
                RowCount = rowCount,
                Columns = columns,
                Rows = rows
            };
        });
    }

    [McpServerTool(Name = "gdal_raster_gcps", Title = "Read ground control points", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the ground control points of a raster and their coordinate reference system.")]
    public Task<GcpResult> GcpsAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Number of points to skip.")] int offset = 0,
        [Description("Maximum number of points to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_gcps", cancellationToken, _ =>
        {
            using var dataset = Open.Raster(context.Paths.Input(path));
            var gcps = dataset.GetGCPs() ?? [];
            var projection = dataset.GetGCPProjection();
            using var srs = string.IsNullOrEmpty(projection) ? null : new SpatialReference(projection);
            return new GcpResult
            {
                Total = gcps.Length,
                Crs = Crs.Summarize(srs),
                Gcps = [.. gcps.Skip(offset).Take(context.ClampLimit(limit))
                    .Select(gcp => new GcpModel(gcp.Id, gcp.Info, gcp.GCPPixel, gcp.GCPLine, gcp.GCPX, gcp.GCPY, gcp.GCPZ))]
            };
        });
    }

    internal static Band GetBand(Dataset dataset, int band)
    {
        if (band < 1 || band > dataset.RasterCount)
        {
            throw new McpException($"Band {band} does not exist. The raster has {dataset.RasterCount} band(s), numbered from 1.");
        }

        return dataset.GetRasterBand(band);
    }

    internal static RIOResampleAlg ParseResampling(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "nearest" or "near" => RIOResampleAlg.GRIORA_NearestNeighbour,
            "bilinear" => RIOResampleAlg.GRIORA_Bilinear,
            "cubic" => RIOResampleAlg.GRIORA_Cubic,
            "cubicspline" => RIOResampleAlg.GRIORA_CubicSpline,
            "lanczos" => RIOResampleAlg.GRIORA_Lanczos,
            "average" => RIOResampleAlg.GRIORA_Average,
            "mode" => RIOResampleAlg.GRIORA_Mode,
            "gauss" => RIOResampleAlg.GRIORA_Gauss,
            "rms" => RIOResampleAlg.GRIORA_RMS,
            _ => throw new McpException("The resampling must be nearest, bilinear, cubic, cubicspline, lanczos, average, mode, gauss or rms.")
        };
    }

    // Builds a transformation from a caller-supplied coordinate reference system to the one of the raster, with x and y in traditional GIS order.
    internal static CoordinateTransformation CreateTransformationToDataset(Dataset dataset, string crs)
    {
        using var source = Crs.Parse(crs);
        using var datasetSrs = dataset.GetSpatialRef()
            ?? throw new McpException("The raster has no coordinate reference system, so points in another coordinate reference system cannot be located.");
        using var target = datasetSrs.Clone();
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        return new CoordinateTransformation(source, target);
    }

    private static Bounds? Wgs84Bounds(Dataset dataset)
    {
        try
        {
            using var envelope = new Envelope();
            return dataset.GetExtentWGS84LongLat(envelope) == CPLErr.CE_None
                ? new Bounds(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY)
                : null;
        }
        catch (ApplicationException)
        {
            // The raster has no coordinate reference system or cannot be transformed to WGS 84.
            return null;
        }
    }

    private static BandInfo DescribeBand(Band band, int number, string statistics, bool includeMetadata, GdalOperation operation)
    {
        band.GetBlockSize(out var blockWidth, out var blockHeight);
        band.GetNoDataValue(out var noData, out var hasNoData);
        band.GetScale(out var scale, out var hasScale);
        band.GetOffset(out var offset, out var hasOffset);

        var overviewSizes = new List<int[]>();
        for (var i = 0; i < band.GetOverviewCount(); i++)
        {
            using var overview = band.GetOverview(i);
            overviewSizes.Add([overview.XSize, overview.YSize]);
        }

        var maskFlags = new List<string>();
        var flags = band.GetMaskFlags();
        if ((flags & GdalConst.GMF_ALL_VALID) != 0)
        {
            maskFlags.Add("all_valid");
        }

        if ((flags & GdalConst.GMF_PER_DATASET) != 0)
        {
            maskFlags.Add("per_dataset");
        }

        if ((flags & GdalConst.GMF_ALPHA) != 0)
        {
            maskFlags.Add("alpha");
        }

        if ((flags & GdalConst.GMF_NODATA) != 0)
        {
            maskFlags.Add("nodata");
        }

        using var colorTable = band.GetRasterColorTable();
        using var attributeTable = band.GetDefaultRAT();
        var description = band.GetDescription();
        var unit = band.GetUnitType();
        var categories = band.GetRasterCategoryNames();
        return new BandInfo
        {
            Band = number,
            DataType = GdalApi.GetDataTypeName(band.DataType),
            ColorInterpretation = GdalApi.GetColorInterpretationName(band.GetRasterColorInterpretation()),
            Description = string.IsNullOrEmpty(description) ? null : description,
            BlockWidth = blockWidth,
            BlockHeight = blockHeight,
            HasNoData = hasNoData != 0,
            NoData = hasNoData != 0 ? Num.Finite(noData) : null,
            NoDataText = hasNoData != 0 && !double.IsFinite(noData) ? noData.ToString(CultureInfo.InvariantCulture) : null,
            Scale = hasScale != 0 ? scale : null,
            Offset = hasOffset != 0 ? offset : null,
            Unit = string.IsNullOrEmpty(unit) ? null : unit,
            OverviewCount = overviewSizes.Count,
            OverviewSizes = overviewSizes.Count > 0 ? overviewSizes : null,
            MaskFlags = maskFlags,
            ColorTableEntries = colorTable?.GetCount(),
            HasAttributeTable = attributeTable is not null,
            CategoryNames = categories is { Length: > 0 } ? [.. categories.Take(MaxCategoryNames)] : null,
            Statistics = ReadStatistics(band, number, statistics, operation),
            Metadata = includeMetadata ? Describe.KeyValues(band.GetMetadata(null)) : null
        };
    }

    private static BandStatistics? ReadStatistics(Band band, int number, string mode, GdalOperation operation)
    {
        double min;
        double max;
        double mean;
        double stdDev;
        var approximate = false;
        switch (mode.ToLowerInvariant())
        {
            case "none":
                return null;
            case "cached":
                if (band.GetStatistics(1, 0, out min, out max, out mean, out stdDev) != CPLErr.CE_None)
                {
                    return null;
                }

                break;
            case "approximate":
                approximate = true;
                band.ComputeStatistics(true, out min, out max, out mean, out stdDev, operation.Progress, null);
                break;
            case "exact":
                band.ComputeStatistics(false, out min, out max, out mean, out stdDev, operation.Progress, null);
                break;
            default:
                throw new McpException("The statistics parameter must be none, cached, approximate or exact.");
        }

        return new BandStatistics
        {
            Band = number,
            Min = Num.Finite(min),
            Max = Num.Finite(max),
            Mean = Num.Finite(mean),
            StdDev = Num.Finite(stdDev),
            Approximate = approximate
        };
    }
}
