using Microsoft.Extensions.Options;

namespace InFarm.Mcp.Gdal.Infrastructure;

// Groups the services that every tool class needs.
public sealed class GdalToolContext(GdalRunner runner, PathPolicy paths, IOptions<GdalServerOptions> options)
{
    public GdalRunner Runner { get; } = runner;

    public PathPolicy Paths { get; } = paths;

    public GdalServerOptions Options { get; } = options.Value;

    public Task<T> RunAsync<T>(
        string operation,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken,
        Func<GdalOperation, T> action)
    {
        return Runner.RunAsync(operation, progress, cancellationToken, action);
    }

    public Task<T> RunAsync<T>(string operation, CancellationToken cancellationToken, Func<GdalOperation, T> action)
    {
        return Runner.RunAsync(operation, null, cancellationToken, action);
    }

    // Returns the number of items to return for a paged request after applying the configured default and maximum.
    public int ClampLimit(int? requested)
    {
        var limit = requested ?? Options.DefaultFeatureCount;
        if (limit < 1)
        {
            throw new McpException("The limit must be at least 1.");
        }

        return Math.Min(limit, Options.MaxFeatureCount);
    }
}
