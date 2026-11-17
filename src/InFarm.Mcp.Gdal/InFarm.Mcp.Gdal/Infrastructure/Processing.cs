namespace InFarm.Mcp.Gdal.Infrastructure;

// Closes a native object when the code that fills it throws before handing it to its consumer.
internal sealed class DisposeOnFailure(IDisposable target) : IDisposable
{
    public bool Completed { get; set; }

    public void Dispose()
    {
        if (!Completed)
        {
            target.Dispose();
        }
    }
}

internal static class Processing
{
    // Runs a GDAL utility that creates an output dataset, and removes the partial output when the utility fails or is cancelled.
    public static ProcessingResult Produce(GdalOperation operation, string output, Func<Dataset?> run, string? message = null)
    {
        try
        {
            using (var result = run())
            {
                if (result is null)
                {
                    throw new McpException($"GDAL did not produce '{output}'. {Redactor.Redact(GdalApi.GetLastErrorMsg())}".TrimEnd());
                }

                result.FlushCache();
            }

            operation.ThrowIfCancelled();
        }
        catch
        {
            DeleteQuietly(output);
            throw;
        }

        return Describe.Output(output, operation, message);
    }

    // Runs work that writes an output through the GDAL object model, and removes the partial output when the work fails or is cancelled.
    public static ProcessingResult Guard(GdalOperation operation, string output, Action body, string? message = null)
    {
        try
        {
            body();
            operation.ThrowIfCancelled();
        }
        catch
        {
            DeleteQuietly(output);
            throw;
        }

        return Describe.Output(output, operation, message);
    }

    // Creates an empty vector dataset. The caller owns the returned object.
    public static DataSource CreateDataSource(string output, string? format, IReadOnlyList<string>? creationOptions)
    {
        var driverName = DriverForOutput(output, format, true).ShortName;
        var driver = Ogr.GetDriverByName(driverName)
            ?? throw new McpException($"The driver '{driverName}' cannot create vector datasets.");
        return driver.CreateDataSource(output, creationOptions?.ToArray() ?? [])
            ?? throw new McpException($"The {driverName} driver could not create '{output}'.");
    }

    // Returns the default layer name for a new vector output, which is the file name without its extension.
    public static string LayerNameFor(string output, string? layerName)
    {
        if (!string.IsNullOrWhiteSpace(layerName))
        {
            return layerName;
        }

        var name = Path.GetFileNameWithoutExtension(output.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(name) ? "layer" : name;
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            if (PathPolicy.Exists(path))
            {
                PathPolicy.Delete(path);
            }
        }
        catch (Exception ex) when (ex is ApplicationException or IOException or UnauthorizedAccessException)
        {
            // The partial output stays behind when it cannot be removed, and the original error is the one worth reporting.
        }
    }

    // Returns the driver for a new dataset, taken from the explicit format name or from the file extension of the output.
    public static GdalDriver DriverForOutput(string output, string? format, bool vector)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            return GdalApi.GetDriverByName(format)
                ?? throw new McpException($"The GDAL driver '{format}' is not available. Use gdal_driver_list to see the registered drivers.");
        }

        var extension = Path.GetExtension(output).TrimStart('.');
        var capability = vector ? GdalConst.DCAP_VECTOR : GdalConst.DCAP_RASTER;
        for (var i = 0; extension.Length > 0 && i < GdalApi.GetDriverCount(); i++)
        {
            var driver = GdalApi.GetDriver(i);
            var extensions = (driver.GetMetadataItem(GdalConst.DMD_EXTENSIONS, null) ?? driver.GetMetadataItem(GdalConst.DMD_EXTENSION, null) ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (driver.GetMetadataItem(capability, null) == "YES"
                && driver.GetMetadataItem(GdalConst.DCAP_CREATE, null) == "YES"
                && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return driver;
            }
        }

        if (!vector)
        {
            return GdalApi.GetDriverByName("GTiff") ?? throw new McpException("The GTiff driver is not available. Pass a format explicitly.");
        }

        throw new McpException(
            $"The output format cannot be determined from the extension of '{output}'. Pass a format such as GPKG, GeoJSON, FlatGeobuf or 'ESRI Shapefile'.");
    }

    // Creates an empty raster with the size, georeferencing and coordinate reference system of a source raster.
    public static Dataset CreateLike(Dataset source, string output, string? format, string dataType, int bandCount, IReadOnlyList<string>? creationOptions)
    {
        var type = GdalApi.GetDataTypeByName(dataType);
        if (type == DataType.GDT_Unknown)
        {
            throw new McpException($"'{dataType}' is not a GDAL data type. Use a name such as Byte, UInt16, Int16, UInt32, Int32, Float32 or Float64.");
        }

        var driver = DriverForOutput(output, format, false);
        var result = driver.Create(output, source.RasterXSize, source.RasterYSize, bandCount, type, creationOptions?.ToArray())
            ?? throw new McpException($"The {driver.ShortName} driver could not create '{output}'.");
        var geoTransform = new double[6];
        source.GetGeoTransform(geoTransform);
        result.SetGeoTransform(geoTransform);
        using var srs = source.GetSpatialRef();
        if (srs is not null)
        {
            result.SetSpatialRef(srs);
        }

        return result;
    }
}
