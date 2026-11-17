using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace InFarm.Mcp.Gdal.Infrastructure;

// Decides whether a dataset name may be read or written under the configured roots, read-only mode and remote access setting.
public sealed partial class PathPolicy(IOptions<GdalServerOptions> options)
{
    public const string MemoryPrefix = "/vsimem/";

    private static readonly HashSet<string> ForbiddenHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        // These handlers read and write the process streams that carry the MCP protocol.
        "vsistdin", "vsistdout", "vsistdout_redirect"
    };

    private static readonly HashSet<string> NetworkHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        "vsicurl", "vsis3", "vsigs", "vsiaz", "vsiadls", "vsioss", "vsiswift", "vsiwebhdfs", "vsihdfs"
    };

    private static readonly HashSet<string> WrapperHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        "vsizip", "vsitar", "vsigzip", "vsi7z", "vsirar", "vsisubfile", "vsisparse", "vsicrypt", "vsicached", "vsipmtiles",
        "vsikerchunk_json_ref", "vsikerchunk_json_ref_cached", "vsikerchunk_parquet_ref"
    };

    // These driver prefixes always name a network service or a database server, never a local file.
    private static readonly HashSet<string> NetworkPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PG", "PGB", "PostgreSQL", "MYSQL", "MSSQL", "ODBC", "OCI", "HANA", "MongoDBv3", "ES", "Elasticsearch", "CouchDB", "AMIGOCLOUD",
        "Carto", "WMS", "WMTS", "WCS", "WFS", "OAPIF", "OGCAPI", "CSW", "NGW", "PLScenes", "EEDA", "EEDAI", "DAAS", "STACIT", "STACTA"
    };

    private readonly GdalServerOptions settings = options.Value;
    private readonly IReadOnlyList<string> roots = options.Value.GetAllowedRoots();

    private enum Access
    {
        Read,
        Update,
        Create
    }

    public bool IsRestricted => roots.Count > 0;

    // Returns the name that GDAL should open for a dataset that is only read.
    public string Input(string path)
    {
        return Resolve(path, Access.Read);
    }

    public string[] Inputs(IEnumerable<string> paths)
    {
        return [.. paths.Select(Input)];
    }

    // Returns the name that GDAL should open for an existing dataset that the call modifies in place.
    public string Update(string path)
    {
        return Resolve(path, Access.Update);
    }

    // Returns the name of a dataset that the call creates, and removes an existing dataset when the caller asked to overwrite it.
    public string Output(string path, bool overwrite)
    {
        var resolved = Resolve(path, Access.Create);
        if (!Exists(resolved))
        {
            return resolved;
        }

        if (!overwrite)
        {
            throw new McpException($"The output '{path}' already exists. Pass overwrite=true to replace it, or choose another output path.");
        }

        if (!settings.AllowOverwrite)
        {
            throw new McpException($"The output '{path}' already exists and the server configuration does not allow existing outputs to be overwritten.");
        }

        Delete(resolved);
        return resolved;
    }

    // Returns the name of a location that the call writes to, and leaves anything that already exists there in place.
    public string Writable(string path)
    {
        return Resolve(path, Access.Create);
    }

    // Checks that the server allows connections to network filesystems, URLs and databases.
    public void RequireRemoteAccess(string description)
    {
        if (!settings.AllowRemoteAccess)
        {
            throw new McpException($"Remote access is disabled in the server configuration, so '{Redactor.Redact(description)}' cannot be used.");
        }

    }

    // Rejects a name whose target the policy cannot determine while the server is confined to allowed roots.
    private void RequireUnrestricted(string description)
    {
        if (IsRestricted)
        {
            throw new McpException(
                $"The name '{Redactor.Redact(description)}' uses a prefix that the server cannot check against the allowed roots, so it is rejected.");
        }

        RequireRemoteAccess(description);
    }

    public void RequireWritable(string description)
    {
        if (settings.ReadOnly)
        {
            throw new McpException($"The server is running in read-only mode, so {description} is not allowed. Outputs under /vsimem/ remain available.");
        }
    }

    // Returns true when a file, a directory or a virtual filesystem entry exists at the resolved name.
    public static bool Exists(string resolved)
    {
        if (!resolved.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(resolved) || Directory.Exists(resolved);
        }

        try
        {
            var handle = GdalApi.VSIFOpenExL(resolved, "rb", 0, null);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            GdalApi.VSIFCloseL(handle);
            return true;
        }
        catch (ApplicationException)
        {
            return false;
        }
    }

    // Removes a dataset together with the sidecar files that its driver knows about.
    public static void Delete(string resolved)
    {
        try
        {
            using var driver = GdalApi.IdentifyDriver(resolved, null);
            if (driver is not null && driver.Delete(resolved) == CPLErr.CE_None && !Exists(resolved))
            {
                return;
            }
        }
        catch (ApplicationException)
        {
            // A file that no driver recognises is removed as a plain file below.
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
        else if (resolved.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
        {
            GdalApi.Unlink(resolved);
        }
        else
        {
            File.Delete(resolved);
        }
    }

    private string Resolve(string path, Access access)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("A dataset path is required.");
        }

        path = path.Trim();
        if (path.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
        {
            CheckVirtualPath(path, access);
            return path;
        }

        if (access != Access.Read)
        {
            RequireWritable($"writing '{Redactor.Redact(path)}'");
        }

        if (path.Contains("://", StringComparison.Ordinal))
        {
            RequireRemoteAccess(path);
            return path;
        }

        if (DriverPrefixPattern().IsMatch(path) && !File.Exists(path) && !Directory.Exists(path))
        {
            CheckPrefixedName(path, access);
            return path;
        }

        return ResolveLocal(path, access);
    }

    private void CheckVirtualPath(string path, Access access)
    {
        var separator = path.IndexOfAny(['/', '?'], 1);
        var handler = (separator < 0 ? path[1..] : path[1..separator]).ToLowerInvariant();
        var remainder = separator < 0 ? string.Empty : path[(separator + 1)..];

        if (ForbiddenHandlers.Contains(handler))
        {
            throw new McpException($"The /{handler}/ filesystem is not available because standard input and output carry the MCP protocol.");
        }

        if (handler == "vsimem")
        {
            return;
        }

        if (access != Access.Read)
        {
            RequireWritable($"writing '{Redactor.Redact(path)}'");
        }

        var baseHandler = handler.EndsWith("_streaming", StringComparison.Ordinal) ? handler[..^"_streaming".Length] : handler;
        if (NetworkHandlers.Contains(baseHandler))
        {
            RequireRemoteAccess(path);
            return;
        }

        if (!WrapperHandlers.Contains(handler))
        {
            RequireUnrestricted(path);
            return;
        }

        var inner = ExtractWrappedPath(handler, remainder);
        if (string.IsNullOrEmpty(inner))
        {
            throw new McpException($"The virtual path '{path}' does not name a file inside the /{handler}/ filesystem.");
        }

        if (inner.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
        {
            CheckVirtualPath(inner, access);
            return;
        }

        if (inner.Contains("://", StringComparison.Ordinal))
        {
            RequireRemoteAccess(inner);
            return;
        }

        // The wrapped name continues inside an archive, so only the allowed roots can be checked here.
        CheckRoots(Path.GetFullPath(inner, BaseDirectory()), path);
    }

    private static string ExtractWrappedPath(string handler, string remainder)
    {
        if (remainder.StartsWith('{'))
        {
            var close = remainder.IndexOf('}');
            return close > 0 ? remainder[1..close] : string.Empty;
        }

        switch (handler)
        {
            case "vsisubfile":
            {
                var comma = remainder.IndexOf(',');
                return comma < 0 ? string.Empty : remainder[(comma + 1)..];
            }
            case "vsicrypt" or "vsicached":
            {
                var file = remainder.IndexOf("file=", StringComparison.OrdinalIgnoreCase);
                return file < 0 ? remainder : remainder[(file + "file=".Length)..];
            }
            default:
                return remainder;
        }
    }

    private void CheckPrefixedName(string path, Access access)
    {
        string? component = null;
        try
        {
            using var info = GdalApi.GetSubdatasetInfo(path);
            component = info?.GetPathComponent();
        }
        catch (ApplicationException)
        {
            // A name that is not in subdataset syntax is treated as a connection string below.
        }

        if (string.IsNullOrEmpty(component))
        {
            var prefix = path[..path.IndexOf(':')];
            if (NetworkPrefixes.Contains(prefix))
            {
                RequireRemoteAccess(path);
            }
            else
            {
                RequireUnrestricted(path);
            }

            return;
        }

        Resolve(component, access == Access.Create ? Access.Update : access);
    }

    private string ResolveLocal(string path, Access access)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path, BaseDirectory());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new McpException($"The path '{path}' is not a valid filesystem path. {ex.Message}");
        }

        CheckRoots(full, path);

        var exists = File.Exists(full) || Directory.Exists(full);
        if (access != Access.Create && !exists)
        {
            throw new McpException($"The path '{full}' does not exist.");
        }

        if (exists && new FileInfo(full) is { LinkTarget: not null } link)
        {
            var target = link.ResolveLinkTarget(true);
            if (target is not null)
            {
                CheckRoots(target.FullName, path);
            }
        }

        if (access == Access.Create)
        {
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        return full;
    }

    private string BaseDirectory()
    {
        return roots.Count > 0 ? roots[0] : Directory.GetCurrentDirectory();
    }

    private void CheckRoots(string fullPath, string original)
    {
        if (roots.Count == 0)
        {
            return;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalized = Path.TrimEndingDirectorySeparator(fullPath);
        foreach (var root in roots)
        {
            if (normalized.Equals(root, comparison)
                || normalized.StartsWith(root + Path.DirectorySeparatorChar, comparison)
                || normalized.StartsWith(root + Path.AltDirectorySeparatorChar, comparison))
            {
                return;
            }
        }

        throw new McpException($"The path '{original}' is outside the allowed roots ({string.Join("; ", roots)}).");
    }

    // A name such as NETCDF:"file.nc":variable or PG:dbname=x starts with a driver prefix of at least two characters.
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]+:")]
    private static partial Regex DriverPrefixPattern();
}
