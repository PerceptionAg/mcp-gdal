using Microsoft.Extensions.Logging;

namespace InFarm.Mcp.Gdal.Configuration;

public sealed class GdalServerOptions
{
    // Each list option is a single string so that one environment variable or one command-line switch can carry it.
    private static readonly char[] ListSeparators = [';', '|'];

    public string? AllowedRoots { get; set; }

    public bool ReadOnly { get; set; }

    public bool AllowOverwrite { get; set; } = true;

    public bool AllowRemoteAccess { get; set; } = true;

    public string? AllowedDrivers { get; set; }

    public string? CacheSize { get; set; }

    public string? GdalDataDirectory { get; set; }

    public string? ProjDataDirectory { get; set; }

    public string? TempDirectory { get; set; }

    public string NumThreads { get; set; } = "ALL_CPUS";

    public int DefaultFeatureCount { get; set; } = 50;

    public int MaxFeatureCount { get; set; } = 1000;

    public int MaxGeometryCharacters { get; set; } = 20_000;

    public int MaxResponseCharacters { get; set; } = 400_000;

    public int MaxWindowPixels { get; set; } = 65_536;

    public int OperationTimeoutSeconds { get; set; } = 3600;

    public int MaxConcurrentOperations { get; set; } = 2;

    public bool AllowSqlWrites { get; set; }

    public bool AllowConfigChanges { get; set; } = true;

    public bool ProjNetwork { get; set; }

    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    public IReadOnlyList<string> GetAllowedRoots()
    {
        return [.. Split(AllowedRoots).Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))];
    }

    public IReadOnlyList<string> GetAllowedDrivers()
    {
        // A comma is also accepted here because driver names never contain one.
        return [.. Split(AllowedDrivers?.Replace(',', ';'))];
    }

    private static IEnumerable<string> Split(string? value)
    {
        return (value ?? string.Empty).Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
