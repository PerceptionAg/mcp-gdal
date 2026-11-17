namespace InFarm.Mcp.Gdal.Tools;

[McpServerToolType]
public sealed class DatasetTools(GdalToolContext context)
{
    private const int MaxLayersInSummary = 25;

    [McpServerTool(Name = "gdal_dataset_info", Title = "Describe a dataset", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Opens any GDAL or OGR dataset and returns its driver, files, raster summary, vector layer summaries, subdataset count and metadata domains. " +
        "Start here when the kind of a dataset is unknown, then use gdal_raster_info or gdal_vector_info for full detail.")]
    public Task<DatasetInfo> InfoAsync(
        [Description("Dataset path, virtual path or connection string.")] string path,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Includes the metadata items of the default domain.")] bool includeMetadata = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_info", cancellationToken, _ =>
        {
            var resolved = context.Paths.Input(path);
            using var dataset = Open.Any(resolved, openOptions);
            using var driver = dataset.GetDriver();
            var layerCount = dataset.GetLayerCount();
            var layers = new List<LayerSummary>();
            for (var i = 0; i < Math.Min(layerCount, MaxLayersInSummary); i++)
            {
                using var layer = dataset.GetLayer(i);
                layers.Add(Describe.Layer(layer));
            }

            var kinds = new List<string>();
            if (dataset.RasterCount > 0)
            {
                kinds.Add("raster");
            }

            if (layerCount > 0)
            {
                kinds.Add("vector");
            }

            return new DatasetInfo
            {
                Path = resolved,
                Driver = driver.ShortName,
                DriverLongName = driver.LongName,
                Kinds = kinds,
                Files = dataset.GetFileList(),
                Raster = dataset.RasterCount > 0 ? Describe.Raster(dataset) : null,
                LayerCount = layerCount,
                Layers = layers.Count > 0 ? layers : null,
                SubdatasetCount = (dataset.GetMetadata("SUBDATASETS")?.Length ?? 0) / 2,
                MetadataDomains = dataset.GetMetadataDomainList(),
                Metadata = includeMetadata ? Describe.KeyValues(dataset.GetMetadata(null)) : null
            };
        });
    }

    [McpServerTool(Name = "gdal_dataset_metadata", Title = "Read dataset or band metadata", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Reads the metadata of one domain of a dataset or of a raster band, and lists the available domains. " +
        "Common domains are the default domain (empty string), IMAGE_STRUCTURE, RPC, GEOLOCATION, SUBDATASETS, EXIF and xml:XMP.")]
    public Task<MetadataResult> MetadataAsync(
        [Description("Dataset path.")] string path,
        [Description("Metadata domain. Omit it for the default domain.")] string? domain = null,
        [Description("Raster band number, starting at 1. Omit it to read dataset metadata.")] int? band = null,
        [Description("Returns only the item with this name.")] string? key = null,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_metadata", cancellationToken, _ =>
        {
            using var dataset = Open.Any(context.Paths.Input(path), openOptions);
            using var bandObject = band is null ? null : RasterTools.GetBand(dataset, band.Value);
            MajorObject target = bandObject is null ? dataset : bandObject;
            var domainName = domain ?? string.Empty;
            var raw = target.GetMetadata(domainName) ?? [];
            var isText = domainName.StartsWith("xml:", StringComparison.OrdinalIgnoreCase) || domainName.StartsWith("json:", StringComparison.OrdinalIgnoreCase);
            var items = isText ? null : Describe.KeyValues(raw);
            if (items is not null && key is not null)
            {
                items = items.Where(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToDictionary();
            }

            return new MetadataResult
            {
                Target = band is null ? "dataset" : $"band {band}",
                Domains = target.GetMetadataDomainList(),
                Domain = domainName,
                Items = items,
                Text = isText ? Limits.EnsureText(string.Join("\n", raw), context.Options.MaxResponseCharacters, "The metadata domain is too large to return.") : null
            };
        });
    }

    [McpServerTool(Name = "gdal_dataset_set_metadata", Title = "Write dataset or band metadata", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Sets, replaces or removes metadata items on a dataset or raster band. An item with a null value is removed. " +
        "Formats that cannot store the metadata themselves keep it in an .aux.xml sidecar file.")]
    public Task<MetadataResult> SetMetadataAsync(
        [Description("Dataset path.")] string path,
        [Description("Items to set. A null value removes the item.")] Dictionary<string, string?> items,
        [Description("Metadata domain. Omit it for the default domain.")] string? domain = null,
        [Description("Raster band number, starting at 1. Omit it to write dataset metadata.")] int? band = null,
        [Description("Removes every existing item of the domain before the new items are written.")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_set_metadata", cancellationToken, _ =>
        {
            var domainName = domain ?? string.Empty;
            using (var dataset = Open.Any(context.Paths.Update(path), update: true))
            {
                using var bandObject = band is null ? null : RasterTools.GetBand(dataset, band.Value);
                MajorObject target = bandObject is null ? dataset : bandObject;
                if (replaceAll)
                {
                    target.SetMetadata(Array.Empty<string>(), domainName);
                }

                foreach (var (name, value) in items)
                {
                    target.SetMetadataItem(name, value, domainName);
                }

                dataset.FlushCache();
            }

            using var reopened = Open.Any(context.Paths.Input(path));
            using var reopenedBand = band is null ? null : RasterTools.GetBand(reopened, band.Value);
            MajorObject result = reopenedBand is null ? reopened : reopenedBand;
            return new MetadataResult
            {
                Target = band is null ? "dataset" : $"band {band}",
                Domains = result.GetMetadataDomainList(),
                Domain = domainName,
                Items = Describe.KeyValues(result.GetMetadata(domainName))
            };
        });
    }

    [McpServerTool(Name = "gdal_dataset_subdatasets", Title = "List subdatasets", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Lists the subdatasets of a container format such as netCDF, HDF5, GeoPackage rasters, Sentinel-2 or multi-page TIFF. " +
        "Each returned name can be passed as the path of any other tool.")]
    public Task<SubdatasetListResult> SubdatasetsAsync(
        [Description("Dataset path.")] string path,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Number of subdatasets to skip.")] int offset = 0,
        [Description("Maximum number of subdatasets to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_subdatasets", cancellationToken, _ =>
        {
            using var dataset = Open.Any(context.Paths.Input(path), openOptions);
            var items = Describe.KeyValues(dataset.GetMetadata("SUBDATASETS"));
            var all = new List<SubdatasetInfoModel>();
            for (var i = 1; items.TryGetValue($"SUBDATASET_{i}_NAME", out var name); i++)
            {
                all.Add(new SubdatasetInfoModel { Index = i, Name = name, Description = items.GetValueOrDefault($"SUBDATASET_{i}_DESC") });
            }

            return new SubdatasetListResult { Total = all.Count, Subdatasets = [.. all.Skip(offset).Take(context.ClampLimit(limit))] };
        });
    }

    [McpServerTool(Name = "gdal_dataset_identify", Title = "Identify the driver of a file", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Reports which GDAL driver would open a file, without opening the dataset fully. Equivalent to 'gdal dataset identify'.")]
    public Task<IdentifyResult> IdentifyAsync(
        [Description("File path or virtual path.")] string path,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_identify", cancellationToken, _ =>
        {
            var resolved = context.Paths.Input(path);
            using var driver = GdalApi.IdentifyDriver(resolved, null);
            if (driver is null)
            {
                return new IdentifyResult { Path = resolved, Recognized = false };
            }

            return new IdentifyResult
            {
                Path = resolved,
                Recognized = true,
                Driver = new DriverSummary
                {
                    ShortName = driver.ShortName,
                    LongName = driver.LongName,
                    Raster = driver.GetMetadataItem(GdalConst.DCAP_RASTER, null) == "YES",
                    Vector = driver.GetMetadataItem(GdalConst.DCAP_VECTOR, null) == "YES",
                    Multidimensional = driver.GetMetadataItem(GdalConst.DCAP_MULTIDIM_RASTER, null) == "YES",
                    CanOpen = true,
                    CanCreate = driver.GetMetadataItem(GdalConst.DCAP_CREATE, null) == "YES",
                    CanCreateCopy = driver.GetMetadataItem(GdalConst.DCAP_CREATECOPY, null) == "YES",
                    VirtualIo = driver.GetMetadataItem(GdalConst.DCAP_VIRTUALIO, null) == "YES"
                }
            };
        });
    }

    [McpServerTool(Name = "gdal_dataset_delete", Title = "Delete a dataset", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Deletes a dataset together with the sidecar files that its driver manages, such as .ovr, .aux.xml, .shx and .dbf files. Equivalent to 'gdal dataset delete'.")]
    public Task<FileOperationResult> DeleteAsync(
        [Description("Dataset path.")] string path,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_delete", cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            PathPolicy.Delete(resolved);
            return new FileOperationResult { Path = resolved, Action = "deleted", Warnings = operation.WarningsOrNull };
        });
    }

    [McpServerTool(Name = "gdal_dataset_copy", Title = "Copy or rename a dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Copies or renames all the files of a dataset through its driver, without converting the content. Equivalent to 'gdal dataset copy' and 'gdal dataset rename'. " +
        "Use gdal_raster_translate or gdal_vector_convert to change the format.")]
    public Task<FileOperationResult> CopyAsync(
        [Description("Existing dataset path.")] string source,
        [Description("New dataset path.")] string destination,
        [Description("Moves the dataset instead of copying it.")] bool rename = false,
        [Description("Replaces the destination when it already exists.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_dataset_copy", cancellationToken, operation =>
        {
            var resolvedSource = rename ? context.Paths.Update(source) : context.Paths.Input(source);
            var resolvedDestination = context.Paths.Output(destination, overwrite);
            using var driver = GdalApi.IdentifyDriver(resolvedSource, null)
                ?? throw new McpException($"No GDAL driver recognises '{source}', so its files cannot be determined.");
            var result = rename ? driver.Rename(resolvedDestination, resolvedSource) : driver.CopyFiles(resolvedDestination, resolvedSource);
            if (result != CPLErr.CE_None)
            {
                throw new McpException($"The {driver.ShortName} driver could not {(rename ? "rename" : "copy")} the dataset: {GdalApi.GetLastErrorMsg()}");
            }

            return new FileOperationResult { Path = resolvedDestination, Action = rename ? "renamed" : "copied", Warnings = operation.WarningsOrNull };
        });
    }
}
