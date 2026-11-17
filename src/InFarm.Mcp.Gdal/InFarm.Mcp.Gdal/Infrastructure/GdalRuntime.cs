using System.Runtime.InteropServices;
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InFarm.Mcp.Gdal.Infrastructure;

// Owns the process-wide GDAL state, which GDAL only allows to be initialised once.
public sealed class GdalRuntime
{
    private const int MaxCapturedWarnings = 50;
    private const int ErrorClassDebug = 1;
    private const int ErrorClassWarning = 2;

    private static readonly Lock InitLock = new();
    private static bool nativeConfigured;
    private static ILogger? nativeLogger;

    // GDAL keeps a raw pointer to this delegate, so the delegate must stay reachable for the life of the process.
    private static readonly GdalApi.GDALErrorHandlerDelegate ErrorHandler = OnGdalError;

    [ThreadStatic]
    private static List<string>? threadWarnings;

    public GdalRuntime(IOptions<GdalServerOptions> options, ILogger<GdalRuntime> logger)
    {
        var settings = options.Value;
        lock (InitLock)
        {
            nativeLogger = logger;
            if (!nativeConfigured)
            {
                ConfigureNative();
                nativeConfigured = true;
            }

            ApplySettings(settings, logger);
        }

        GdalVersion = GdalApi.VersionInfo("RELEASE_NAME");
        GdalReleaseDate = GdalApi.VersionInfo("RELEASE_DATE");
        ProjVersion = $"{Osr.GetPROJVersionMajor()}.{Osr.GetPROJVersionMinor()}.{Osr.GetPROJVersionMicro()}";
        GeosVersion = $"{Ogr.GetGEOSVersionMajor()}.{Ogr.GetGEOSVersionMinor()}.{Ogr.GetGEOSVersionMicro()}";
        DriverCount = GdalApi.GetDriverCount();
        ProjDatabaseAvailable = VerifyProjDatabase(logger);

        if (DriverCount == 0)
        {
            throw new InvalidOperationException("GDAL was loaded, but no drivers are registered.");
        }

        logger.LogInformation(
            "GDAL {GdalVersion} is ready with {DriverCount} drivers, PROJ {ProjVersion} and GEOS {GeosVersion}.",
            GdalVersion, DriverCount, ProjVersion, GeosVersion);
    }

    public string GdalVersion { get; }

    public string GdalReleaseDate { get; }

    public string ProjVersion { get; }

    public string GeosVersion { get; }

    public int DriverCount { get; }

    public bool ProjDatabaseAvailable { get; }

    // Collects the warnings that GDAL raises on the calling thread until EndWarningCapture is called.
    internal static void BeginWarningCapture(List<string> warnings)
    {
        threadWarnings = warnings;
    }

    internal static void EndWarningCapture()
    {
        threadWarnings = null;
    }

    private static void ConfigureNative()
    {
        try
        {
            GdalBase.ConfigureAll();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The GDAL native libraries could not be loaded for {RuntimeInformation.RuntimeIdentifier}. " +
                "The bundled libraries support Windows x64, Linux x64 and ARM64 with glibc 2.35 or later, and macOS x64 and ARM64. " +
                $"Loader error: {ex.Message}", ex);
        }

        GdalApi.UseExceptions();
        Ogr.UseExceptions();
        Osr.UseExceptions();
        GdalApi.SetErrorHandler(ErrorHandler, IntPtr.Zero);
    }

    private static void ApplySettings(GdalServerOptions settings, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(settings.GdalDataDirectory))
        {
            GdalApi.SetConfigOption("GDAL_DATA", Path.GetFullPath(settings.GdalDataDirectory));
        }

        if (!string.IsNullOrWhiteSpace(settings.ProjDataDirectory))
        {
            string[] paths = [Path.GetFullPath(settings.ProjDataDirectory), .. Osr.GetPROJSearchPaths() ?? []];
            Osr.SetPROJSearchPaths([.. paths.Distinct()]);
        }

        if (!string.IsNullOrWhiteSpace(settings.TempDirectory))
        {
            var tempDirectory = Path.GetFullPath(settings.TempDirectory);
            Directory.CreateDirectory(tempDirectory);
            GdalApi.SetConfigOption("CPL_TMPDIR", tempDirectory);
        }

        if (!string.IsNullOrWhiteSpace(settings.CacheSize))
        {
            GdalApi.SetConfigOption("GDAL_CACHEMAX", settings.CacheSize);
        }

        if (!string.IsNullOrWhiteSpace(settings.NumThreads))
        {
            GdalApi.SetConfigOption("GDAL_NUM_THREADS", settings.NumThreads);
        }

        // Python pixel functions inside a VRT would let a dataset run arbitrary code.
        GdalApi.SetConfigOption("GDAL_VRT_ENABLE_PYTHON", "NO");

        if (settings.ReadOnly)
        {
            // Without this option GDAL writes .aux.xml sidecar files next to datasets that are only being read.
            GdalApi.SetConfigOption("GDAL_PAM_ENABLED", "NO");
        }

        Osr.SetPROJEnableNetwork(settings.ProjNetwork && settings.AllowRemoteAccess);

        var allowedDrivers = settings.GetAllowedDrivers();
        if (allowedDrivers.Count > 0)
        {
            RestrictDrivers(allowedDrivers, logger);
        }
    }

    private static void RestrictDrivers(IReadOnlyList<string> allowedDrivers, ILogger logger)
    {
        var allowed = new HashSet<string>(allowedDrivers, StringComparer.OrdinalIgnoreCase);
        var removed = new List<GdalDriver>();
        for (var i = 0; i < GdalApi.GetDriverCount(); i++)
        {
            var driver = GdalApi.GetDriver(i);
            if (!allowed.Contains(driver.ShortName))
            {
                removed.Add(driver);
            }
        }

        foreach (var driver in removed)
        {
            driver.Deregister();
        }

        logger.LogInformation("The driver restriction left {Count} GDAL drivers registered.", GdalApi.GetDriverCount());
    }

    private static bool VerifyProjDatabase(ILogger logger)
    {
        try
        {
            using var srs = new SpatialReference(string.Empty);
            srs.ImportFromEPSG(4326);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("PROJ could not resolve EPSG:4326, so coordinate reference system tools will fail. {Message}", ex.Message);
            return false;
        }
    }

    private static void OnGdalError(int errorClass, int errorCode, IntPtr messagePointer)
    {
        // An exception that escapes into native code would terminate the process.
        try
        {
            var message = Redactor.Redact(Marshal.PtrToStringUTF8(messagePointer) ?? string.Empty);
            if (errorClass == ErrorClassWarning && threadWarnings is { Count: < MaxCapturedWarnings } warnings)
            {
                warnings.Add(message);
            }

            var level = errorClass == ErrorClassDebug ? LogLevel.Trace : LogLevel.Debug;
            nativeLogger?.Log(level, "GDAL message (class {ErrorClass}, code {ErrorCode}): {Message}", errorClass, errorCode, message);
        }
        catch
        {
        }
    }
}
