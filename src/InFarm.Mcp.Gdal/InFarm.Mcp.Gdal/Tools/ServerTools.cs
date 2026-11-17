using System.Runtime.InteropServices;

namespace InFarm.Mcp.Gdal.Tools;

[McpServerToolType]
public sealed class ServerTools(GdalToolContext context)
{
    // Changing one of these options would let a dataset load code, redirect data directories or bypass the server safeguards.
    private static readonly HashSet<string> ProtectedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GDAL_DRIVER_PATH", "OGR_DRIVER_PATH", "GDAL_PYTHON_DRIVER_PATH", "GDAL_VRT_ENABLE_PYTHON", "GDAL_VRT_PYTHON_TRUSTED_MODULES",
        "PYTHONSO", "OGR_SQLITE_LOAD_EXTENSIONS", "GDAL_MEM_ENABLE_OPEN", "GDAL_DATA", "PROJ_DATA", "PROJ_LIB", "CPL_TMPDIR", "CPL_LOG",
        "CPL_LOG_ERRORS", "CPL_CURL_VERBOSE", "GDAL_SKIP", "OGR_SKIP", "GDAL_PAM_ENABLED", "GDAL_PAM_PROXY_DIR", "CPL_VSIL_CURL_NON_CACHED"
    };

    [McpServerTool(Name = "gdal_server_info", Title = "GDAL server information", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the GDAL, PROJ and GEOS versions, the platform, the driver counts, the active security configuration and the response limits of this server.")]
    public Task<ServerInfo> ServerInfoAsync(CancellationToken cancellationToken)
    {
        return context.RunAsync("gdal_server_info", cancellationToken, _ =>
        {
            var runtime = context.Runner.Runtime;
            var options = context.Options;
            var rasterDrivers = 0;
            var vectorDrivers = 0;
            for (var i = 0; i < GdalApi.GetDriverCount(); i++)
            {
                using var driver = GdalApi.GetDriver(i);
                rasterDrivers += driver.GetMetadataItem(GdalConst.DCAP_RASTER, null) == "YES" ? 1 : 0;
                vectorDrivers += driver.GetMetadataItem(GdalConst.DCAP_VECTOR, null) == "YES" ? 1 : 0;
            }

            return new ServerInfo
            {
                ServerVersion = ServerHost.Version,
                GdalVersion = runtime.GdalVersion,
                GdalReleaseDate = runtime.GdalReleaseDate,
                ProjVersion = runtime.ProjVersion,
                GeosVersion = runtime.GeosVersion,
                RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                DotnetVersion = RuntimeInformation.FrameworkDescription,
                DriverCount = GdalApi.GetDriverCount(),
                RasterDriverCount = rasterDrivers,
                VectorDriverCount = vectorDrivers,
                ProjDatabaseAvailable = runtime.ProjDatabaseAvailable,
                ProjSearchPaths = Osr.GetPROJSearchPaths(),
                ProjNetworkEnabled = Osr.GetPROJEnableNetwork(),
                GdalDataDirectory = GdalApi.GetConfigOption("GDAL_DATA", null),
                CacheSize = GdalApi.GetConfigOption("GDAL_CACHEMAX", null),
                NumThreads = GdalApi.GetConfigOption("GDAL_NUM_THREADS", null),
                ReadOnly = options.ReadOnly,
                AllowedRoots = options.GetAllowedRoots(),
                AllowRemoteAccess = options.AllowRemoteAccess,
                AllowOverwrite = options.AllowOverwrite,
                AllowSqlWrites = options.AllowSqlWrites,
                AllowConfigChanges = options.AllowConfigChanges,
                DefaultFeatureCount = options.DefaultFeatureCount,
                MaxFeatureCount = options.MaxFeatureCount,
                MaxGeometryCharacters = options.MaxGeometryCharacters,
                MaxResponseCharacters = options.MaxResponseCharacters,
                MaxWindowPixels = options.MaxWindowPixels,
                OperationTimeoutSeconds = options.OperationTimeoutSeconds,
                MaxConcurrentOperations = options.MaxConcurrentOperations,
                VirtualFilesystems = GdalApi.GetFileSystemsPrefixes() ?? []
            };
        });
    }

    [McpServerTool(Name = "gdal_config_get", Title = "Get GDAL configuration options", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the current values of GDAL configuration options such as GDAL_CACHEMAX, GDAL_NUM_THREADS or AWS_REGION. Values of credential options are masked.")]
    public Task<IReadOnlyList<ConfigOptionValue>> ConfigGetAsync(
        [Description("Names of the configuration options to read.")] string[] names,
        CancellationToken cancellationToken)
    {
        return context.RunAsync<IReadOnlyList<ConfigOptionValue>>("gdal_config_get", cancellationToken, _ =>
        {
            return [.. names.Select(name =>
            {
                var value = GdalApi.GetConfigOption(name, null);
                return new ConfigOptionValue
                {
                    Name = name,
                    IsSet = value is not null,
                    Value = value is not null && Redactor.IsSecretKey(name) ? Redactor.Mask : value
                };
            })];
        });
    }

    [McpServerTool(Name = "gdal_config_set", Title = "Set a GDAL configuration option", ReadOnly = false, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Sets or clears a GDAL configuration option for the rest of the server session, for example AWS_NO_SIGN_REQUEST=YES, GDAL_HTTP_TIMEOUT=30 or " +
        "GDAL_CACHEMAX=1024MB. Pass pathPrefix to limit a credential option to one bucket or URL prefix. Options that load code or relocate GDAL data are protected.")]
    public Task<ConfigOptionValue> ConfigSetAsync(
        [Description("Name of the configuration option.")] string name,
        [Description("New value. Omit the value to clear the option.")] string? value = null,
        [Description("Virtual path prefix, such as /vsis3/my-bucket, that the option applies to. Omit it to set the option globally.")] string? pathPrefix = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_config_set", cancellationToken, _ =>
        {
            if (!context.Options.AllowConfigChanges)
            {
                throw new McpException("The server configuration does not allow GDAL configuration options to be changed.");
            }

            name = name.Trim();
            if (ProtectedOptions.Contains(name))
            {
                throw new McpException($"The configuration option '{name}' is protected and can only be set when the server starts.");
            }

            if (value is not null && (name.EndsWith("_FILE", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_PATH", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_DIR", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_CREDENTIALS", StringComparison.OrdinalIgnoreCase)))
            {
                value = context.Paths.Input(value);
            }

            if (pathPrefix is null)
            {
                GdalApi.SetConfigOption(name, value);
            }
            else
            {
                GdalApi.SetPathSpecificOption(pathPrefix, name, value);
            }

            return new ConfigOptionValue
            {
                Name = name,
                IsSet = value is not null,
                Value = value is not null && Redactor.IsSecretKey(name) ? Redactor.Mask : value
            };
        });
    }
}
