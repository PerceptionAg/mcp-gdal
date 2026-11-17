using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InFarm.Mcp.Gdal.Infrastructure;

// Carries the cancellation token, the progress callback and the captured warnings of one tool call.
public sealed class GdalOperation
{
    private readonly IProgress<ProgressNotificationValue>? progress;
    private readonly Stopwatch sinceLastReport = Stopwatch.StartNew();
    private double lastReported = -1;

    internal GdalOperation(string name, CancellationToken token, IProgress<ProgressNotificationValue>? progress)
    {
        Name = name;
        Token = token;
        this.progress = progress;
        Progress = OnProgress;
        OgrProgress = OnProgress;
    }

    public string Name { get; }

    public CancellationToken Token { get; }

    public List<string> Warnings { get; } = [];

    // GDAL stops the running algorithm when this callback returns zero.
    public GdalApi.GDALProgressFuncDelegate Progress { get; }

    // The OGR bindings declare their own delegate type for the same callback.
    public Ogr.GDALProgressFuncDelegate OgrProgress { get; }

    public IReadOnlyList<string>? WarningsOrNull => Warnings.Count > 0 ? [.. Warnings] : null;

    public void ThrowIfCancelled()
    {
        Token.ThrowIfCancellationRequested();
    }

    private int OnProgress(double complete, IntPtr message, IntPtr data)
    {
        if (Token.IsCancellationRequested)
        {
            return 0;
        }

        if (progress is not null && (complete >= 1 || (complete - lastReported >= 0.01 && sinceLastReport.ElapsedMilliseconds >= 500)))
        {
            lastReported = complete;
            sinceLastReport.Restart();
            progress.Report(new ProgressNotificationValue { Progress = (float)Math.Round(complete * 100, 1), Total = 100 });
        }

        return 1;
    }
}

// Runs GDAL work on a dedicated thread with a concurrency limit, a timeout, warning capture and uniform error translation.
public sealed class GdalRunner(GdalRuntime runtime, IOptions<GdalServerOptions> options, ILogger<GdalRunner> logger)
{
    private readonly SemaphoreSlim gate = new(Math.Max(1, options.Value.MaxConcurrentOperations));
    private readonly int timeoutSeconds = options.Value.OperationTimeoutSeconds;

    public GdalRuntime Runtime { get; } = runtime;

    public async Task<T> RunAsync<T>(
        string operation,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken,
        Func<GdalOperation, T> action)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutSeconds > 0)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            }

            var context = new GdalOperation(operation, timeout.Token, progress);
            return await Task.Factory.StartNew(
                () => Execute(context, action, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        finally
        {
            gate.Release();
        }
    }

    private T Execute<T>(GdalOperation context, Func<GdalOperation, T> action, CancellationToken callerToken)
    {
        GdalRuntime.BeginWarningCapture(context.Warnings);
        try
        {
            GdalApi.ErrorReset();
            return action(context);
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException($"The operation '{context.Name}' was cancelled.", ex, callerToken);
            }

            if (context.Token.IsCancellationRequested)
            {
                throw new McpException(
                    $"The operation '{context.Name}' exceeded the configured timeout of {timeoutSeconds} seconds and was stopped. " +
                    "Reduce the amount of work or raise the timeout in the server configuration.");
            }

            var message = Redactor.Redact(ex.Message);
            logger.LogWarning("The operation {Operation} failed: {Message}", context.Name, message);
            logger.LogDebug(ex, "Details of the failure in {Operation}.", context.Name);
            throw new McpException(Describe(context.Name, ex, message));
        }
        finally
        {
            GdalRuntime.EndWarningCapture();
        }
    }

    private static string Describe(string operation, Exception exception, string message)
    {
        var hint = exception switch
        {
            OutOfMemoryException => " The operation ran out of memory. Process a smaller window or lower the GDAL cache size.",
            UnauthorizedAccessException => " The server process does not have permission to access the file.",
            _ when message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
                => " Check that the path exists and that it is spelled as an absolute path.",
            _ when message.Contains("not recognized as being in a supported file format", StringComparison.OrdinalIgnoreCase)
                => " Use gdal_driver_list to see which formats this server can open.",
            _ when message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                => " The server process does not have permission to access the file.",
            _ when message.Contains("crs not found", StringComparison.OrdinalIgnoreCase)
                => " Use gdal_crs_lookup to search for a valid coordinate reference system code.",
            _ => string.Empty
        };

        return $"{operation} failed: {message}{hint}";
    }
}
