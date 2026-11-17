using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tools;

public sealed record AlgorithmSummary
{
    [Description("Path of the algorithm below the 'gdal' command, for example 'raster reproject'.")]
    public required string Algorithm { get; init; }

    public string? Description { get; init; }

    [Description("True when the entry is a group that contains further algorithms.")]
    public bool IsGroup { get; init; }

    public string? HelpUrl { get; init; }
}

public sealed record AlgorithmListResult
{
    public int Total { get; init; }

    public required IReadOnlyList<AlgorithmSummary> Algorithms { get; init; }
}

// Access to every algorithm of the unified 'gdal' command that newer GDAL releases provide.
[McpServerToolType]
public sealed class AlgorithmTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_algorithm_list", Title = "List the algorithms of the unified gdal command", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Lists the algorithms of the unified 'gdal' command line interface, such as 'raster reproject', 'raster calc', 'raster zonal-stats', 'vector dissolve' and 'vsi sync'. " +
        "Every listed algorithm can be run with gdal_algorithm_run, which gives access to GDAL functionality that has no dedicated tool.")]
    public Task<AlgorithmListResult> ListAsync(
        [Description("Group to list, for example 'raster', 'vector', 'vector grid' or 'vsi'. Every algorithm is listed when omitted.")] string? group = null,
        [Description("Returns only algorithms whose path or description contains this text.")] string? contains = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_algorithm_list", cancellationToken, _ =>
        {
            var path = AlgorithmInvoker.SplitPath(group ?? string.Empty);
            using var root = AlgorithmInvoker.Instantiate(path);
            var results = new List<AlgorithmSummary>();
            Collect(root, string.Join(' ', path), results);
            var filtered = results
                .Where(item => contains is null
                    || item.Algorithm.Contains(contains, StringComparison.OrdinalIgnoreCase)
                    || (item.Description?.Contains(contains, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            return new AlgorithmListResult { Total = filtered.Count, Algorithms = filtered };
        });
    }

    [McpServerTool(Name = "gdal_algorithm_describe", Title = "Describe an algorithm of the unified gdal command", ReadOnly = true, Idempotent = true)]
    [Description(
        "Returns the JSON usage description of one algorithm of the unified 'gdal' command: its arguments with their types, defaults, allowed values and meanings. " +
        "Call this tool before gdal_algorithm_run to learn the argument names.")]
    public Task<string> DescribeAsync(
        [Description("Algorithm path below the 'gdal' command, for example 'raster reproject' or 'vector grid invdist'.")] string algorithm,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_algorithm_describe", cancellationToken, _ =>
        {
            using var instance = AlgorithmInvoker.Instantiate(AlgorithmInvoker.SplitPath(algorithm));
            return Limits.EnsureText(instance.GetUsageAsJSON(), context.Options.MaxResponseCharacters, "Describe a more specific algorithm.");
        });
    }

    [McpServerTool(Name = "gdal_algorithm_run", Title = "Run an algorithm of the unified gdal command", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Runs any algorithm of the unified 'gdal' command natively, without starting a process. Arguments are passed by their long names without leading dashes. " +
        "Dataset arguments take paths, list arguments take arrays and flags take booleans. Example: algorithm='raster reproject', " +
        "arguments={\"input\":\"in.tif\",\"output\":\"out.tif\",\"output-crs\":\"EPSG:3857\",\"resampling\":\"bilinear\"}. " +
        "Every dataset path is checked against the server path policy, and an existing output is replaced only when the argument overwrite=true is passed. " +
        "In read-only mode only the inspection algorithms are available.")]
    public Task<AlgorithmRunResult> RunAsync(
        [Description("Algorithm path below the 'gdal' command, for example 'raster calc', 'vector dissolve' or 'raster zonal-stats'.")] string algorithm,
        [Description("Argument values by long argument name. Use gdal_algorithm_describe to list the arguments of the algorithm.")] Dictionary<string, JsonElement> arguments,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_algorithm_run", progress, cancellationToken, operation =>
        {
            return AlgorithmInvoker.Run(AlgorithmInvoker.SplitPath(algorithm), arguments, context, operation);
        });
    }

    private static void Collect(Algorithm parent, string parentPath, List<AlgorithmSummary> results)
    {
        foreach (var name in parent.GetSubAlgorithmNames() ?? [])
        {
            using var child = parent.InstantiateSubAlgorithm(name);
            if (child is null)
            {
                continue;
            }

            var path = parentPath.Length == 0 ? name : $"{parentPath} {name}";
            var isGroup = child.HasSubAlgorithms();
            results.Add(new AlgorithmSummary
            {
                Algorithm = path,
                Description = child.GetDescription(),
                IsGroup = isGroup,
                HelpUrl = child.GetHelpFullURL()
            });
            if (isGroup)
            {
                Collect(child, path, results);
            }
        }
    }
}
