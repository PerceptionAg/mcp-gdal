namespace InFarm.Mcp.Gdal.Tools;

// Multidimensional raster tools for formats such as netCDF, HDF5 and Zarr.
[McpServerToolType]
public sealed class MultidimTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_mdim_info", Title = "Describe a multidimensional dataset (gdalmdiminfo)", ReadOnly = true, Idempotent = true)]
    [Description(
        "Returns the JSON report of the native gdalmdiminfo utility for a multidimensional dataset such as netCDF, HDF5 or Zarr: its groups, dimensions, arrays, " +
        "data types and attributes. Pass array to describe one array only.")]
    public Task<string> InfoAsync(
        [Description("Multidimensional dataset path.")] string path,
        [Description("Name of the array to describe. Every array is described when omitted.")] string? array = null,
        [Description("Includes the values of the indexing variables and more attribute detail.")] bool detailed = false,
        [Description("Maximum number of values to show for each array.")] int? valueLimit = null,
        [Description("Computes statistics of the arrays.")] bool statistics = false,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_mdim_info", cancellationToken, _ =>
        {
            var arguments = new ArgList()
                .Flag(detailed, "-detailed")
                .Option("-array", array)
                .Option("-limit", valueLimit)
                .Flag(statistics, "-stats");
            using var dataset = Open.Multidim(context.Paths.Input(path), openOptions);
            using var options = new GDALMultiDimInfoOptions(arguments.ToArray());
            return Limits.EnsureText(
                GdalApi.GDALMultiDimInfo(dataset, options),
                context.Options.MaxResponseCharacters,
                "Describe a single array, or lower valueLimit.");
        });
    }

    [McpServerTool(Name = "gdal_mdim_translate", Title = "Convert or subset a multidimensional dataset (gdalmdimtranslate)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Converts a multidimensional dataset between formats, or extracts arrays, groups and subsets of it, with the native gdalmdimtranslate utility. " +
        "An array specification can select a view, for example 'name=temperature,view=[0,:,:]' to extract one time step as a two-dimensional raster.")]
    public Task<ProcessingResult> TranslateAsync(
        [Description("Input multidimensional dataset path.")] string input,
        [Description("Output dataset path.")] string output,
        [Description("Output driver short name, for example netCDF, Zarr, VRT or GTiff.")] string? format = null,
        [Description("Array specifications to extract. Each one is an array name, optionally with transpose, view and resample settings.")] string[]? arrays = null,
        [Description("Group specifications to extract.")] string[]? groups = null,
        [Description("Subset specifications, for example 'time(\"2020-01-01\",\"2020-12-31\")' or 'lat(-40,-30)'.")] string[]? subsets = null,
        [Description("Scale axes specification, for example 'lon(2),lat(2)' to keep every second value.")] string? scaleAxes = null,
        [Description("Driver creation options in NAME=VALUE form.")] string[]? creationOptions = null,
        [Description("Open options of the input driver in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Additional gdalmdimtranslate arguments, one array element per token.")] string[]? additionalArguments = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_mdim_translate", progress, cancellationToken, operation =>
        {
            var resolvedInput = context.Paths.Input(input);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var arguments = new ArgList()
                .Option("-of", format)
                .Each("-array", arrays)
                .Each("-group", groups)
                .Each("-subset", subsets)
                .Option("-scaleaxes", scaleAxes)
                .Each("-co", creationOptions)
                .Extra(additionalArguments, context.Paths);

            using var source = Open.Multidim(resolvedInput, openOptions, true);
            using var options = new GDALMultiDimTranslateOptions(arguments.ToArray());
            return Processing.Produce(
                operation,
                resolvedOutput,
                () => GdalApi.MultiDimTranslate(resolvedOutput, [source], options, operation.Progress, null));
        });
    }
}
