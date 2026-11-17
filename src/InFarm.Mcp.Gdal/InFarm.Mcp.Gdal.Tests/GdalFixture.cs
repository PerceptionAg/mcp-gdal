global using InFarm.Mcp.Gdal.Configuration;
global using InFarm.Mcp.Gdal.Infrastructure;
global using InFarm.Mcp.Gdal.Models;
global using InFarm.Mcp.Gdal.Tools;
global using ModelContextProtocol;
global using OSGeo.GDAL;
global using OSGeo.OGR;
global using OSGeo.OSR;
global using GdalApi = OSGeo.GDAL.Gdal;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace InFarm.Mcp.Gdal.Tests;

[CollectionDefinition(Name)]
public sealed class GdalCollection : ICollectionFixture<GdalFixture>
{
    public const string Name = "gdal";
}

// Initialises GDAL once for the test run and creates the small datasets that the tests share.
public sealed class GdalFixture : IDisposable
{
    public const string ProjectedCrs = "EPSG:32755";
    public const int RasterWidth = 100;
    public const int RasterHeight = 80;

    public GdalFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "infarm-mcp-gdal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Runtime = new GdalRuntime(Options.Create(new GdalServerOptions()), NullLogger<GdalRuntime>.Instance);
        Context = CreateContext();
    }

    public string Root { get; }

    public GdalRuntime Runtime { get; }

    public GdalToolContext Context { get; }

    public GdalToolContext CreateContext(Action<GdalServerOptions>? configure = null)
    {
        var settings = new GdalServerOptions();
        configure?.Invoke(settings);
        var options = Options.Create(settings);
        return new GdalToolContext(new GdalRunner(Runtime, options, NullLogger<GdalRunner>.Instance), new PathPolicy(options), options);
    }

    public string NewPath(string extension)
    {
        return Path.Combine(Root, $"{Guid.NewGuid():N}{extension}");
    }

    // Writes a Float32 raster whose value is column + row, with a NoData block in the top-left corner when requested.
    public string CreateGradientRaster(bool withNoDataBlock = false, double originX = 500000, double originY = 6000800)
    {
        var path = NewPath(".tif");
        using var driver = GdalApi.GetDriverByName("GTiff");
        using var dataset = driver.Create(path, RasterWidth, RasterHeight, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([originX, 10, 0, originY, 0, -10]);
        using var srs = new SpatialReference(string.Empty);
        srs.ImportFromEPSG(32755);
        dataset.SetSpatialRef(srs);
        var values = new float[RasterWidth * RasterHeight];
        for (var row = 0; row < RasterHeight; row++)
        {
            for (var column = 0; column < RasterWidth; column++)
            {
                values[row * RasterWidth + column] = withNoDataBlock && row < 10 && column < 10 ? -9999 : column + row;
            }
        }

        using var band = dataset.GetRasterBand(1);
        band.SetNoDataValue(-9999);
        band.WriteRaster(0, 0, RasterWidth, RasterHeight, values, RasterWidth, RasterHeight, 0, 0);
        dataset.FlushCache();
        return path;
    }

    // Creates a GeoPackage with four adjacent 100 m squares that carry a name, a zone and a value.
    public async Task<string> CreateParcelsAsync(string extension = ".gpkg")
    {
        var path = NewPath(extension);
        await new VectorEditTools(Context).CreateAsync(
            path,
            layerName: "parcels",
            geometryType: "Polygon",
            crs: ProjectedCrs,
            fields:
            [
                new FieldDefinition { Name = "name", Type = "String" },
                new FieldDefinition { Name = "zone", Type = "String" },
                new FieldDefinition { Name = "value", Type = "Real" }
            ]);
        var features = new List<FeatureInput>();
        for (var i = 0; i < 4; i++)
        {
            var x = 500000 + i * 100;
            features.Add(new FeatureInput
            {
                Geometry = $"POLYGON (({x} 6000000,{x + 100} 6000000,{x + 100} 6000100,{x} 6000100,{x} 6000000))",
                Properties = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement($"parcel {i + 1}"),
                    ["zone"] = JsonSerializer.SerializeToElement(i < 2 ? "west" : "east"),
                    ["value"] = JsonSerializer.SerializeToElement((i + 1) * 10.5)
                }
            });
        }

        await new VectorEditTools(Context).FeaturesWriteAsync(path, [.. features], layer: "parcels");
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
            // A dataset that is still open on Windows keeps its file locked, and the temporary directory is cleaned by the system later.
        }
    }
}
