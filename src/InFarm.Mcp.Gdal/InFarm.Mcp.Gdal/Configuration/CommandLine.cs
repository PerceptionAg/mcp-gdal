namespace InFarm.Mcp.Gdal.Configuration;

internal static class CommandLine
{
    public const string EnvironmentPrefix = "INFARM_MCP_GDAL_";

    public static readonly IReadOnlyDictionary<string, string> SwitchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["--allowed-roots"] = nameof(GdalServerOptions.AllowedRoots),
        ["--read-only"] = nameof(GdalServerOptions.ReadOnly),
        ["--allow-overwrite"] = nameof(GdalServerOptions.AllowOverwrite),
        ["--allow-remote-access"] = nameof(GdalServerOptions.AllowRemoteAccess),
        ["--allowed-drivers"] = nameof(GdalServerOptions.AllowedDrivers),
        ["--cache-size"] = nameof(GdalServerOptions.CacheSize),
        ["--gdal-data"] = nameof(GdalServerOptions.GdalDataDirectory),
        ["--proj-data"] = nameof(GdalServerOptions.ProjDataDirectory),
        ["--temp-dir"] = nameof(GdalServerOptions.TempDirectory),
        ["--num-threads"] = nameof(GdalServerOptions.NumThreads),
        ["--default-feature-count"] = nameof(GdalServerOptions.DefaultFeatureCount),
        ["--max-feature-count"] = nameof(GdalServerOptions.MaxFeatureCount),
        ["--max-geometry-characters"] = nameof(GdalServerOptions.MaxGeometryCharacters),
        ["--max-response-characters"] = nameof(GdalServerOptions.MaxResponseCharacters),
        ["--max-window-pixels"] = nameof(GdalServerOptions.MaxWindowPixels),
        ["--timeout"] = nameof(GdalServerOptions.OperationTimeoutSeconds),
        ["--max-concurrent-operations"] = nameof(GdalServerOptions.MaxConcurrentOperations),
        ["--allow-sql-writes"] = nameof(GdalServerOptions.AllowSqlWrites),
        ["--allow-config-changes"] = nameof(GdalServerOptions.AllowConfigChanges),
        ["--proj-network"] = nameof(GdalServerOptions.ProjNetwork),
        ["--log-level"] = nameof(GdalServerOptions.LogLevel)
    };

    private static readonly HashSet<string> BooleanSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "--read-only", "--allow-overwrite", "--allow-remote-access", "--allow-sql-writes", "--allow-config-changes", "--proj-network"
    };

    public const string HelpText = """
        infarm-mcp-gdal: a Model Context Protocol server for GDAL and OGR that communicates over standard I/O.

        Options (each option can also be set with the environment variable INFARM_MCP_GDAL_<NAME>):
          --allowed-roots <dirs>            Directories that tools may read and write, separated by ';'. Unrestricted when omitted. (ALLOWEDROOTS)
          --read-only                       Rejects every operation that writes outside /vsimem/. (READONLY)
          --allow-overwrite <bool>          Lets a tool call replace an existing output when the call passes overwrite=true. Default true. (ALLOWOVERWRITE)
          --allow-remote-access <bool>      Allows network filesystems, URLs and database connections. Default true. (ALLOWREMOTEACCESS)
          --allowed-drivers <names>         Keeps only these GDAL drivers registered, separated by ';' or ','. (ALLOWEDDRIVERS)
          --cache-size <size>               GDAL block cache size, for example 512MB or 10%. (CACHESIZE)
          --gdal-data <dir>                 Overrides the bundled GDAL data directory. (GDALDATADIRECTORY)
          --proj-data <dir>                 Adds a PROJ resource directory, for example one that holds proj-data grids. (PROJDATADIRECTORY)
          --temp-dir <dir>                  Directory for temporary files. (TEMPDIRECTORY)
          --num-threads <n|ALL_CPUS>        Value for GDAL_NUM_THREADS. Default ALL_CPUS. (NUMTHREADS)
          --default-feature-count <n>       Features returned when a call gives no limit. Default 50. (DEFAULTFEATURECOUNT)
          --max-feature-count <n>           Largest number of features a single response may hold. Default 1000. (MAXFEATURECOUNT)
          --max-geometry-characters <n>     Longest geometry text returned for one feature. Default 20000. (MAXGEOMETRYCHARACTERS)
          --max-response-characters <n>     Approximate limit on the size of a response. Default 400000. (MAXRESPONSECHARACTERS)
          --max-window-pixels <n>           Largest pixel window returned by a raster read. Default 65536. (MAXWINDOWPIXELS)
          --timeout <seconds>               Processing timeout for one tool call. 0 disables the timeout. Default 3600. (OPERATIONTIMEOUTSECONDS)
          --max-concurrent-operations <n>   Number of GDAL operations that may run at the same time. Default 2. (MAXCONCURRENTOPERATIONS)
          --allow-sql-writes                Allows SQL statements other than SELECT. (ALLOWSQLWRITES)
          --allow-config-changes <bool>     Allows the gdal_config_set tool. Default true. (ALLOWCONFIGCHANGES)
          --proj-network                    Lets PROJ download transformation grids on demand. (PROJNETWORK)
          --log-level <level>               Trace, Debug, Information, Warning, Error or None. Logs go to stderr. (LOGLEVEL)
          --version                         Prints the server version to stderr and exits.
          --help                            Prints this text to stderr and exits.
        """;

    // Returns the arguments with a value appended to every boolean switch that was given without one.
    public static string[] Normalize(string[] args)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var hasNextValue = i + 1 < args.Length && bool.TryParse(args[i + 1], out _);
            result.Add(BooleanSwitches.Contains(arg) && !hasNextValue ? $"{arg}=true" : arg);
        }

        return [.. result];
    }
}
