namespace InFarm.Mcp.Gdal.Tools;

public sealed record OverviewResult
{
    public required string Path { get; init; }

    public required string Action { get; init; }

    [Description("Decimation factors that were built.")]
    public IReadOnlyList<int>? Levels { get; init; }

    [Description("Width and height of each overview of the first band after the operation.")]
    public required IReadOnlyList<int[]> OverviewSizes { get; init; }

    public IReadOnlyList<string>? Warnings { get; init; }
}

// Tools that modify an existing raster in place, like gdal_edit and gdaladdo.
[McpServerToolType]
public sealed class RasterEditTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_raster_edit", Title = "Edit raster georeferencing and band properties in place", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Edits an existing raster in place without rewriting its pixels, like gdal_edit: assigns or removes the coordinate reference system, sets the extent or the geotransform, " +
        "sets or removes NoData values, sets the scale, offset, unit, color interpretation, description and category names of bands, assigns ground control points, " +
        "clears statistics and creates a dataset mask. Use gdal_dataset_set_metadata to edit metadata.")]
    public Task<ProcessingResult> EditAsync(RasterEditRequest request, CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_edit", cancellationToken, operation =>
        {
            var path = context.Paths.Update(request.Path);
            using (var dataset = Open.Raster(path, true))
            {
                if (request.Crs is not null)
                {
                    using var srs = request.Crs.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : Crs.Parse(request.Crs);
                    dataset.SetSpatialRef(srs);
                }

                if (request.Bounds is not null)
                {
                    if (request.Bounds.Length != 4)
                    {
                        throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
                    }

                    var bounds = request.Bounds;
                    dataset.SetGeoTransform(
                        [bounds[0], (bounds[2] - bounds[0]) / dataset.RasterXSize, 0, bounds[3], 0, -(bounds[3] - bounds[1]) / dataset.RasterYSize]);
                }

                if (request.GeoTransform is not null)
                {
                    if (request.GeoTransform.Length != 6)
                    {
                        throw new McpException("The parameter 'geoTransform' needs exactly six numbers.");
                    }

                    dataset.SetGeoTransform(request.GeoTransform);
                }

                if (request.Gcps is not null)
                {
                    using var gcpSrs = request.GcpCrs is null ? dataset.GetSpatialRef() : Crs.Parse(request.GcpCrs);
                    var gcps = request.Gcps.Select(gcp => new GCP(gcp.X, gcp.Y, gcp.Z, gcp.Pixel, gcp.Line, gcp.Info ?? string.Empty, gcp.Id ?? string.Empty)).ToArray();
                    dataset.SetGCPs(gcps, gcpSrs is null ? string.Empty : Crs.Wkt(gcpSrs));
                }

                if (request.NoData is not null)
                {
                    for (var number = 1; number <= dataset.RasterCount; number++)
                    {
                        using var band = dataset.GetRasterBand(number);
                        ApplyNoData(band, request.NoData);
                    }
                }

                foreach (var edit in request.Bands ?? [])
                {
                    using var band = RasterTools.GetBand(dataset, edit.Band);
                    ApplyBandEdit(band, edit);
                }

                if (request.ClearStatistics)
                {
                    dataset.ClearStatistics();
                }

                if (request.CreateMask)
                {
                    dataset.CreateMaskBand(GdalConst.GMF_PER_DATASET);
                }

                dataset.FlushCache();
            }

            return Describe.Output(path, operation, "The raster was edited in place.");
        });
    }

    [McpServerTool(Name = "gdal_raster_overviews", Title = "Build or remove raster overviews (gdaladdo)", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Builds or removes the overview pyramid of a raster, like gdaladdo. Overviews make display and approximate statistics of large rasters fast. " +
        "The levels are chosen automatically when they are omitted. Pass external=true to write the overviews to a separate .ovr file and leave the raster unchanged.")]
    public Task<OverviewResult> OverviewsAsync(
        [Description("Raster dataset path.")] string path,
        [Description("Action to perform: build or clean.")] string action = "build",
        [Description("Decimation factors, for example [2, 4, 8, 16]. Powers of two are generated until the smallest overview is below minimumSize when omitted.")] int[]? levels = null,
        [Description("Resampling method: nearest, average, rms, gauss, bilinear, cubic, cubicspline, lanczos or mode.")] string resampling = "average",
        [Description("Writes the overviews to an external .ovr file instead of into the raster.")] bool external = false,
        [Description("Size in pixels below which automatic level generation stops.")] int minimumSize = 256,
        [Description("Overview creation options in NAME=VALUE form, for example COMPRESS=DEFLATE or BLOCKSIZE=512.")] string[]? options = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_raster_overviews", progress, cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            var clean = action.ToLowerInvariant() switch
            {
                "build" => false,
                "clean" => true,
                _ => throw new McpException("The action must be build or clean.")
            };

            using var dataset = Open.Raster(resolved, !external);
            int[] factors = [];
            if (clean)
            {
                dataset.BuildOverviews("NONE", 0, IntPtr.Zero, null, null, []);
            }
            else
            {
                factors = levels ?? AutomaticLevels(dataset, Math.Max(1, minimumSize));
                if (factors.Any(factor => factor < 2))
                {
                    throw new McpException("Every overview level must be 2 or greater.");
                }

                dataset.BuildOverviews(resampling.ToUpperInvariant(), factors, operation.Progress, null, options ?? []);
            }

            operation.ThrowIfCancelled();
            dataset.FlushCache();
            var sizes = new List<int[]>();
            if (dataset.RasterCount > 0)
            {
                using var band = dataset.GetRasterBand(1);
                for (var i = 0; i < band.GetOverviewCount(); i++)
                {
                    using var overview = band.GetOverview(i);
                    sizes.Add([overview.XSize, overview.YSize]);
                }
            }

            return new OverviewResult
            {
                Path = resolved,
                Action = clean ? "clean" : "build",
                Levels = clean ? null : factors,
                OverviewSizes = sizes,
                Warnings = operation.WarningsOrNull
            };
        });
    }

    internal static void ApplyNoData(Band band, string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            band.DeleteNoDataValue();
            return;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new McpException($"The NoData value '{value}' is not a number. Pass a number, 'nan' or 'none'.");
        }

        band.SetNoDataValue(number);
    }

    private static void ApplyBandEdit(Band band, BandEdit edit)
    {
        if (edit.NoData is not null)
        {
            ApplyNoData(band, edit.NoData);
        }

        if (edit.Scale is not null)
        {
            band.SetScale(edit.Scale.Value);
        }

        if (edit.Offset is not null)
        {
            band.SetOffset(edit.Offset.Value);
        }

        if (edit.Unit is not null)
        {
            band.SetUnitType(edit.Unit);
        }

        if (edit.Description is not null)
        {
            band.SetDescription(edit.Description);
        }

        if (edit.CategoryNames is not null)
        {
            band.SetRasterCategoryNames(edit.CategoryNames);
        }

        if (edit.ColorInterpretation is not null)
        {
            var name = edit.ColorInterpretation.Trim();
            var interpretation = GdalApi.GetColorInterpretationByName(char.ToUpperInvariant(name[0]) + name[1..]);
            if (interpretation == ColorInterp.GCI_Undefined && !name.Equals("undefined", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException(
                    $"'{name}' is not a color interpretation. Use a name such as Gray, Palette, Red, Green, Blue, Alpha, NIR, SWIR or Undefined.");
            }

            band.SetRasterColorInterpretation(interpretation);
        }
    }

    private static int[] AutomaticLevels(Dataset dataset, int minimumSize)
    {
        var factors = new List<int>();
        var largest = Math.Max(dataset.RasterXSize, dataset.RasterYSize);
        for (var factor = 2; largest / factor >= minimumSize || factors.Count == 0; factor *= 2)
        {
            factors.Add(factor);
            if (largest / factor <= 1)
            {
                break;
            }
        }

        return [.. factors];
    }
}
