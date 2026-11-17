using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tools;

public sealed record VsiListResult
{
    public required string Path { get; init; }

    [Description("Names of the entries, relative to the listed path.")]
    public required IReadOnlyList<string> Entries { get; init; }

    [Description("True when more entries exist than were returned.")]
    public bool Truncated { get; init; }

    [Description("JSON text with the size, type and modification time of each entry, when details were requested.")]
    public string? DetailsJson { get; init; }
}

public sealed record VsiFilesystemInfo
{
    public required IReadOnlyList<string> Prefixes { get; init; }

    public string? Prefix { get; init; }

    [Description("Configuration options of the requested filesystem, which can be set with gdal_config_set.")]
    public IReadOnlyList<OptionInfo>? Options { get; init; }
}

// File operations on the GDAL virtual filesystems: local files, archives, memory, HTTP and cloud object storage.
[McpServerToolType]
public sealed class VsiTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_vsi_list", Title = "List files in a directory, archive or bucket", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Lists the entries of a local directory or of a GDAL virtual filesystem path, for example /vsizip/archive.zip, /vsitar/data.tar.gz, /vsimem/, " +
        "/vsis3/bucket/prefix, /vsigs/bucket, /vsiaz/container or /vsicurl/https://host/dir. Pass details=true for sizes and modification times.")]
    public Task<VsiListResult> ListAsync(
        [Description("Directory, archive or bucket path.")] string path,
        [Description("Lists the entries of subdirectories as well.")] bool recursive = false,
        [Description("Includes a JSON listing with the size, type and modification time of each entry.")] bool details = false,
        [Description("Maximum number of entries to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vsi_list", cancellationToken, operation =>
        {
            var resolved = context.Paths.Input(path);
            var maximum = context.ClampLimit(limit);
            var entries = (recursive ? GdalApi.ReadDirRecursive(resolved) : GdalApi.ReadDir(resolved, maximum + 1))
                ?? throw new McpException($"'{path}' is not a directory that can be listed, or it does not exist.");
            entries = [.. entries.Where(entry => entry is not ("." or ".."))];
            string? detailsJson = null;
            if (details)
            {
                var arguments = new Dictionary<string, JsonElement>
                {
                    ["filename"] = JsonSerializer.SerializeToElement(resolved),
                    ["output-format"] = JsonSerializer.SerializeToElement("json"),
                    ["long-listing"] = JsonSerializer.SerializeToElement(true),
                    ["recursive"] = JsonSerializer.SerializeToElement(recursive)
                };
                detailsJson = AlgorithmInvoker.Run(["vsi", "list"], arguments, context, operation).Text;
            }

            return new VsiListResult
            {
                Path = resolved,
                Entries = [.. entries.Take(maximum)],
                Truncated = entries.Length > maximum,
                DetailsJson = detailsJson
            };
        });
    }

    [McpServerTool(Name = "gdal_vsi_filesystems", Title = "List the GDAL virtual filesystems", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Lists the virtual filesystem prefixes of this GDAL build, such as /vsizip/, /vsicurl/, /vsis3/, /vsigs/ and /vsiaz/. " +
        "Pass a prefix to get the configuration options of that filesystem, including its authentication options.")]
    public Task<VsiFilesystemInfo> FilesystemsAsync(
        [Description("Filesystem prefix to describe, for example /vsis3/.")] string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vsi_filesystems", cancellationToken, _ =>
        {
            return new VsiFilesystemInfo
            {
                Prefixes = GdalApi.GetFileSystemsPrefixes() ?? [],
                Prefix = prefix,
                Options = prefix is null ? null : DriverTools.ParseOptions(GdalApi.GetFileSystemOptions(prefix))
            };
        });
    }

    [McpServerTool(Name = "gdal_vsi_file_metadata", Title = "Read the metadata of a remote object", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the metadata of a file on a virtual filesystem, such as the HTTP headers or the object tags of a cloud object. The domain is HEADERS, TAGS, STATUS, ACL or METADATA, depending on the filesystem.")]
    public Task<IReadOnlyDictionary<string, string>> FileMetadataAsync(
        [Description("File path on a virtual filesystem.")] string path,
        [Description("Metadata domain.")] string domain = "HEADERS",
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync<IReadOnlyDictionary<string, string>>("gdal_vsi_file_metadata", cancellationToken, _ =>
        {
            var items = Describe.KeyValues(GdalApi.GetFileMetadata(context.Paths.Input(path), domain, null));
            return items.ToDictionary(item => item.Key, item => Redactor.IsSecretKey(item.Key) ? Redactor.Mask : item.Value);
        });
    }

    [McpServerTool(Name = "gdal_vsi_copy", Title = "Copy or move a file between filesystems", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Copies or moves a single file between any two GDAL filesystems, for example from cloud storage to a local path or into /vsimem/. " +
        "Use the algorithm 'vsi sync' through gdal_algorithm_run to synchronise whole directories.")]
    public Task<FileOperationResult> CopyAsync(
        [Description("Source file path.")] string source,
        [Description("Destination file path.")] string destination,
        [Description("Moves the file instead of copying it.")] bool move = false,
        [Description("Replaces the destination when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vsi_copy", progress, cancellationToken, operation =>
        {
            var resolvedSource = move ? context.Paths.Update(source) : context.Paths.Input(source);
            var resolvedDestination = context.Paths.Output(destination, overwrite);
            var result = move
                ? GdalApi.Move(resolvedSource, resolvedDestination, [], operation.Progress, null)
                : GdalApi.CopyFile(resolvedSource, resolvedDestination, IntPtr.Zero, -1, [], operation.Progress, null);
            if (result != 0)
            {
                Processing.DeleteQuietly(resolvedDestination);
                throw new McpException($"The file could not be {(move ? "moved" : "copied")}. {Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
            }

            operation.ThrowIfCancelled();
            return new FileOperationResult { Path = resolvedDestination, Action = move ? "moved" : "copied", Warnings = operation.WarningsOrNull };
        });
    }

    [McpServerTool(Name = "gdal_vsi_delete", Title = "Delete a file or directory", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description("Deletes a file or a directory on a GDAL filesystem. A directory that is not empty is deleted only when recursive=true. Use gdal_dataset_delete to remove a dataset with its sidecar files.")]
    public Task<FileOperationResult> DeleteAsync(
        [Description("File or directory path.")] string path,
        [Description("Deletes a directory together with its content.")] bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vsi_delete", cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            var isDirectory = Directory.Exists(resolved) || (resolved.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase) && GdalApi.ReadDir(resolved, 1) is not null);
            var result = isDirectory
                ? recursive ? GdalApi.RmdirRecursive(resolved) : GdalApi.Rmdir(resolved)
                : GdalApi.Unlink(resolved);
            if (result != 0)
            {
                throw new McpException(
                    $"'{path}' could not be deleted. {(isDirectory && !recursive ? "The directory is not empty, so pass recursive=true. " : string.Empty)}{Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
            }

            return new FileOperationResult { Path = resolved, Action = "deleted", Warnings = operation.WarningsOrNull };
        });
    }

    [McpServerTool(Name = "gdal_vsi_mkdir", Title = "Create a directory", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Creates a directory, together with its missing parent directories, on a GDAL filesystem.")]
    public Task<FileOperationResult> MkdirAsync(
        [Description("Directory path.")] string path,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vsi_mkdir", cancellationToken, operation =>
        {
            var resolved = context.Paths.Writable(path);
            if (!PathPolicy.Exists(resolved) && GdalApi.MkdirRecursive(resolved, 493) != 0)
            {
                throw new McpException($"The directory '{path}' could not be created. {Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
            }

            return new FileOperationResult { Path = resolved, Action = "directory created", Warnings = operation.WarningsOrNull };
        });
    }
}
