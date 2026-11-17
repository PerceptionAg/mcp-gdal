using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class RasterTests(GdalFixture fixture)
{
    private readonly RasterTools raster = new(fixture.Context);
    private readonly ConversionTools conversion = new(fixture.Context);
    private readonly AnalysisTools analysis = new(fixture.Context);
    private readonly VrtTools vrt = new(fixture.Context);
    private readonly RasterEditTools edit = new(fixture.Context);

    [Fact]
    public async Task CreateWritesARasterWithTheRequestedStructure()
    {
        var path = fixture.NewPath(".tif");

        var result = await conversion.CreateAsync(
            path, width: 40, height: 30, bandCount: 3, dataType: "UInt16", crs: GdalFixture.ProjectedCrs,
            bounds: [500000, 6000000, 500400, 6000300], noData: 0, fillValues: [7, 8, 9]);

        Assert.Equal("GTiff", result.Driver);
        Assert.Equal(40, result.Raster!.Width);
        Assert.Equal(3, result.Raster.BandCount);
        Assert.Equal("UInt16", result.Raster.DataType);
        Assert.Equal(GdalFixture.ProjectedCrs, result.Raster.Crs!.Code);
        Assert.Equal([10d, 10d], result.Raster.PixelSize!);
        var statistics = await raster.StatisticsAsync(path);
        Assert.Equal([7d, 8d, 9d], statistics.Bands.Select(band => band.Mean!.Value));
    }

    [Fact]
    public async Task InfoReportsGeoreferencingBandsAndStatistics()
    {
        var path = fixture.CreateGradientRaster();

        var info = await raster.InfoAsync(path, statistics: "exact");

        Assert.Equal(GdalFixture.RasterWidth, info.Width);
        Assert.Equal(GdalFixture.RasterHeight, info.Height);
        Assert.Equal(GdalFixture.ProjectedCrs, info.Crs!.Code);
        Assert.Equal(new Bounds(500000, 6000000, 501000, 6000800), info.Bounds);
        Assert.InRange(info.Wgs84Bounds!.MinX, 146.9, 147.1);
        var band = Assert.Single(info.Bands);
        Assert.Equal("Float32", band.DataType);
        Assert.Equal(-9999, band.NoData);
        Assert.Equal(0, band.Statistics!.Min);
        Assert.Equal(GdalFixture.RasterWidth + GdalFixture.RasterHeight - 2, band.Statistics.Max);
        Assert.False(File.Exists(path + ".aux.xml"));
    }

    [Fact]
    public async Task InfoJsonReturnsTheNativeGdalinfoReport()
    {
        var json = await raster.InfoJsonAsync(fixture.CreateGradientRaster(), minMax: true);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("GTiff", document.RootElement.GetProperty("driverShortName").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("bands")[0].GetProperty("computedMin").GetDouble());
    }

    [Fact]
    public async Task TranslateConvertsFormatTypeAndWindow()
    {
        var source = fixture.CreateGradientRaster();
        var output = fixture.NewPath(".png");

        var result = await conversion.TranslateAsync(new RasterTranslateRequest
        {
            Input = source,
            Output = output,
            OutputType = "Byte",
            SourceWindow = [10, 20, 50, 40],
            Scale = [0, 178, 0, 255]
        });

        Assert.Equal("PNG", result.Driver);
        Assert.Equal(50, result.Raster!.Width);
        Assert.Equal(40, result.Raster.Height);
        Assert.Equal("Byte", result.Raster.DataType);
    }

    [Fact]
    public async Task CogWritesATiledCompressedGeoTiffWithOverviews()
    {
        var output = fixture.NewPath(".tif");
        var large = fixture.NewPath(".tif");
        await conversion.CreateAsync(large, width: 2000, height: 1500, dataType: "Byte", crs: GdalFixture.ProjectedCrs,
            bounds: [500000, 6000000, 520000, 6015000], fillValues: [5]);

        await conversion.CogAsync(large, output, compression: "DEFLATE", blockSize: 256);

        var info = await raster.InfoAsync(output);
        Assert.Equal("COG", info.ImageStructure!["LAYOUT"]);
        Assert.Equal("DEFLATE", info.ImageStructure["COMPRESSION"]);
        Assert.Equal(256, info.Bands[0].BlockWidth);
        Assert.True(info.Bands[0].OverviewCount > 0);
    }

    [Fact]
    public async Task WarpReprojectsToAnotherCoordinateReferenceSystem()
    {
        var output = fixture.NewPath(".tif");

        var result = await conversion.WarpAsync(new RasterWarpRequest
        {
            Inputs = [fixture.CreateGradientRaster()],
            Output = output,
            TargetCrs = "EPSG:4326",
            Resampling = "bilinear"
        });

        Assert.Equal("EPSG:4326", result.Raster!.Crs!.Code);
        Assert.InRange(result.Raster.Bounds!.MinX, 146.9, 147.1);
        Assert.InRange(result.Raster.Bounds.MinY, -36.2, -36.0);
    }

    [Fact]
    public async Task CropByBoundsKeepsThePixelGrid()
    {
        var output = fixture.NewPath(".tif");

        var result = await conversion.CropAsync(fixture.CreateGradientRaster(), output, bounds: [500200, 6000300, 500500, 6000700]);

        Assert.Equal(30, result.Raster!.Width);
        Assert.Equal(40, result.Raster.Height);
        Assert.Equal(new Bounds(500200, 6000300, 500500, 6000700), result.Raster.Bounds);
    }

    [Fact]
    public async Task CropByCutlineMasksPixelsOutsideThePolygon()
    {
        var parcels = await fixture.CreateParcelsAsync();
        var output = fixture.NewPath(".tif");

        var result = await conversion.CropAsync(fixture.CreateGradientRaster(), output, cutline: parcels, cutlineWhere: "name = 'parcel 1'");

        Assert.Equal(10, result.Raster!.Width);
        Assert.Equal(10, result.Raster.Height);
    }

    [Fact]
    public async Task StatisticsAndHistogramDescribeTheValues()
    {
        var path = fixture.CreateGradientRaster(withNoDataBlock: true);

        var statistics = await raster.StatisticsAsync(path);
        var histogram = await raster.HistogramAsync(path, min: -0.5, max: 199.5, buckets: 200, approximate: false);

        var band = Assert.Single(statistics.Bands);
        Assert.Equal(GdalFixture.RasterWidth + GdalFixture.RasterHeight - 2, band.Max);
        Assert.Equal(98.75, band.ValidPercent);
        Assert.Equal(GdalFixture.RasterWidth * GdalFixture.RasterHeight - 100, histogram.Counts.Sum());
        Assert.Equal(200, histogram.BucketCount);
    }

    [Fact]
    public async Task PixelValuesAreSampledInSeveralCoordinateSystems()
    {
        var path = fixture.CreateGradientRaster();

        var byPixel = await raster.PixelValuesAsync(path, [[12.5, 7.5]], coordinates: "pixel");
        var byMap = await raster.PixelValuesAsync(path, [[500125, 6000725], [0, 0]]);
        var byGeographic = await raster.PixelValuesAsync(path, [[147.0014, -36.1385]], crs: "EPSG:4326");

        Assert.Equal(19, byPixel.Samples[0].Values![0]);
        Assert.Equal(19, byMap.Samples[0].Values![0]);
        Assert.False(byMap.Samples[1].InBounds);
        Assert.True(byGeographic.Samples[0].InBounds);
    }

    [Fact]
    public async Task ReadWindowReturnsRowsAndEnforcesTheSizeLimit()
    {
        var path = fixture.CreateGradientRaster();

        var window = await raster.ReadWindowAsync(path, xOffset: 2, yOffset: 3, width: 4, height: 2);
        var preview = await raster.ReadWindowAsync(path, outputWidth: 10, outputHeight: 8, resampling: "average");

        Assert.Equal([5d, 6d, 7d, 8d], window.Rows[0].Select(value => value!.Value));
        Assert.Equal([6d, 7d, 8d, 9d], window.Rows[1].Select(value => value!.Value));
        Assert.Equal(8, preview.Rows.Count);
        var limited = new RasterTools(fixture.CreateContext(options => options.MaxWindowPixels = 100));
        await Assert.ThrowsAsync<McpException>(() => limited.ReadWindowAsync(path));
    }

    [Fact]
    public async Task OverviewsAreBuiltAndRemoved()
    {
        var path = fixture.CreateGradientRaster();

        var built = await edit.OverviewsAsync(path, levels: [2, 4]);
        var cleaned = await edit.OverviewsAsync(path, action: "clean");

        Assert.Equal([[50, 40], [25, 20]], built.OverviewSizes);
        Assert.Empty(cleaned.OverviewSizes);
    }

    [Fact]
    public async Task EditChangesGeoreferencingAndBandPropertiesInPlace()
    {
        var path = fixture.CreateGradientRaster();

        await edit.EditAsync(new RasterEditRequest
        {
            Path = path,
            Crs = "EPSG:28355",
            Bounds = [0, 0, 1000, 800],
            Bands = [new BandEdit { Band = 1, NoData = "none", Unit = "m", Description = "elevation", Scale = 0.5 }]
        });

        var info = await raster.InfoAsync(path);
        Assert.Equal("EPSG:28355", info.Crs!.Code);
        Assert.Equal(new Bounds(0, 0, 1000, 800), info.Bounds);
        Assert.False(info.Bands[0].HasNoData);
        Assert.Equal("m", info.Bands[0].Unit);
        Assert.Equal("elevation", info.Bands[0].Description);
        Assert.Equal(0.5, info.Bands[0].Scale);
    }

    [Fact]
    public async Task MetadataIsWrittenAndRead()
    {
        var path = fixture.CreateGradientRaster();
        var datasets = new DatasetTools(fixture.Context);

        await datasets.SetMetadataAsync(path, new Dictionary<string, string?> { ["OWNER"] = "InFarm", ["SEASON"] = "2026" });
        await datasets.SetMetadataAsync(path, new Dictionary<string, string?> { ["SEASON"] = null });
        var metadata = await datasets.MetadataAsync(path);
        var info = await datasets.InfoAsync(path);

        Assert.Equal("InFarm", metadata.Items!["OWNER"]);
        Assert.DoesNotContain("SEASON", metadata.Items.Keys);
        Assert.Equal(["raster"], info.Kinds);
    }

    [Fact]
    public async Task CalculateEvaluatesExpressionsWithNativeRasterAlgebra()
    {
        var a = fixture.CreateGradientRaster();
        var output = fixture.NewPath(".tif");

        var result = await analysis.CalculateAsync(
            new Dictionary<string, string> { ["A"] = a, ["B"] = a },
            ["(A + B) / 2 * 3 - 1", "sqrt(max(A, 4)) + abs(-2)"],
            output);

        Assert.Equal(2, result.Raster!.BandCount);
        var sample = await raster.PixelValuesAsync(output, [[10.5, 6.5]], coordinates: "pixel");
        Assert.Equal(47, sample.Samples[0].Values![0]);
        Assert.Equal(6, sample.Samples[0].Values![1]);
        var error = await Assert.ThrowsAsync<McpException>(() => analysis.CalculateAsync(
            new Dictionary<string, string> { ["A"] = a }, ["A > 5"], fixture.NewPath(".tif")));
        Assert.Contains("raster reclassify", error.Message);
    }

    [Fact]
    public async Task DemProductsAreDerivedFromElevation()
    {
        var dem = fixture.CreateGradientRaster();
        var hillshade = fixture.NewPath(".tif");
        var slope = fixture.NewPath(".tif");
        var relief = fixture.NewPath(".tif");

        var shaded = await analysis.DemAsync(dem, hillshade, "hillshade", zFactor: 2, computeEdges: true);
        await analysis.DemAsync(dem, slope, "slope", computeEdges: true);
        var colored = await analysis.DemAsync(dem, relief, "color-relief", colorText: "0 0 0 255\n178 255 255 255");

        Assert.Equal("Byte", shaded.Raster!.DataType);
        var slopeStatistics = await raster.StatisticsAsync(slope);
        Assert.InRange(slopeStatistics.Bands[0].Mean!.Value, 8.0, 8.1);
        Assert.Equal(3, colored.Raster!.BandCount);
    }

    [Fact]
    public async Task ContoursAndPolygonsAreExtractedFromARaster()
    {
        var dem = fixture.CreateGradientRaster();
        var contours = fixture.NewPath(".gpkg");
        var polygons = fixture.NewPath(".gpkg");
        var classes = fixture.NewPath(".tif");
        await conversion.CreateAsync(classes, width: 20, height: 20, dataType: "Byte", crs: GdalFixture.ProjectedCrs,
            bounds: [0, 0, 200, 200], fillValues: [3]);

        var lines = await analysis.ContoursAsync(dem, contours, interval: 20);
        var areas = await analysis.PolygonizeAsync(classes, polygons, fieldName: "class");

        Assert.Equal(8, lines.Layers![0].FeatureCount);
        Assert.Equal(1, areas.Layers![0].FeatureCount);
        var features = await new VectorTools(fixture.Context).FeaturesAsync(polygons);
        Assert.Equal(3, features.Features[0].Properties!["class"]);
    }

    [Fact]
    public async Task RasterizeBurnsPolygonsIntoANewRaster()
    {
        var parcels = await fixture.CreateParcelsAsync();
        var output = fixture.NewPath(".tif");

        var result = await analysis.RasterizeAsync(parcels, output, attribute: "value", resolution: [10, 10], bounds: [500000, 6000000, 500400, 6000100], outputType: "Float32", noData: "-1", initialValues: [-1]);

        Assert.Equal(40, result.Raster!.Width);
        Assert.Equal(10, result.Raster.Height);
        var statistics = await raster.StatisticsAsync(output);
        Assert.Equal(10.5, statistics.Bands[0].Min);
        Assert.Equal(42, statistics.Bands[0].Max);
    }

    [Fact]
    public async Task ProximitySieveAndFillOperateOnRasterRegions()
    {
        var withHole = fixture.CreateGradientRaster(withNoDataBlock: true);
        var filled = fixture.NewPath(".tif");
        var proximity = fixture.NewPath(".tif");
        var sieved = fixture.NewPath(".tif");

        await analysis.FillNoDataAsync(withHole, filled, maximumDistance: 20);
        await analysis.ProximityAsync(withHole, proximity, targetValues: [10], distanceUnits: "PIXEL");
        await analysis.SieveAsync(withHole, sieved, threshold: 2);

        var filledStatistics = await raster.StatisticsAsync(filled);
        Assert.Equal(100, filledStatistics.Bands[0].ValidPercent);
        var distance = await raster.PixelValuesAsync(proximity, [[13.5, 0.5]], coordinates: "pixel");
        Assert.Equal(3, distance.Samples[0].Values![0]);
        Assert.True(File.Exists(sieved));
    }

    [Fact]
    public async Task ViewshedAndFootprintAreComputed()
    {
        var dem = fixture.CreateGradientRaster(withNoDataBlock: true);
        var viewshed = fixture.NewPath(".tif");
        var footprint = fixture.NewPath(".geojson");

        var visible = await analysis.ViewshedAsync(dem, viewshed, observerX: 500500, observerY: 6000400, observerHeight: 50);
        var outline = await analysis.FootprintAsync(dem, footprint);

        Assert.Equal("Byte", visible.Raster!.DataType);
        Assert.Equal(1, outline.Layers![0].FeatureCount);
    }

    [Fact]
    public async Task MosaicBuildVrtAndTileIndexCombineRasters()
    {
        var west = fixture.CreateGradientRaster();
        var east = fixture.CreateGradientRaster(originX: 501000);
        var virtualRaster = fixture.NewPath(".vrt");
        var mosaic = fixture.NewPath(".tif");
        var stack = fixture.NewPath(".tif");
        var index = fixture.NewPath(".gpkg");

        var virtualResult = await vrt.BuildVrtAsync([west, east], virtualRaster);
        var mosaicResult = await vrt.MosaicAsync([west, east], mosaic, creationOptions: ["COMPRESS=LZW"]);
        var stackResult = await vrt.MosaicAsync([west, west], stack, separate: true);
        var indexResult = await vrt.TileIndexAsync([west, east], index);

        Assert.Equal("VRT", virtualResult.Driver);
        Assert.Equal(200, virtualResult.Raster!.Width);
        Assert.Equal(200, mosaicResult.Raster!.Width);
        Assert.Equal(2, stackResult.Raster!.BandCount);
        Assert.Equal(2, indexResult.Layers![0].FeatureCount);
    }

    [Fact]
    public async Task RetileCutsTilesAndPyramidLevels()
    {
        var directory = Path.Combine(fixture.Root, Guid.NewGuid().ToString("N"));

        var result = await vrt.RetileAsync(fixture.CreateGradientRaster(), directory, tileWidth: 50, tileHeight: 40, levels: 1, tileIndex: Path.Combine(directory, "index.gpkg"));

        Assert.Equal(5, result.TileCount);
        Assert.Equal(4, Directory.GetFiles(directory, "*.tif").Length);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "1"), "*.tif"));
        Assert.True(File.Exists(result.TileIndex));
    }

    [Fact]
    public async Task AlignMatchesTheGridOfAReferenceRaster()
    {
        var reference = fixture.NewPath(".tif");
        await conversion.CreateAsync(reference, width: 25, height: 20, crs: GdalFixture.ProjectedCrs, bounds: [500000, 6000000, 501000, 6000800]);
        var output = fixture.NewPath(".tif");

        var result = await conversion.AlignAsync(fixture.CreateGradientRaster(), reference, output, resampling: "average");

        Assert.Equal(25, result.Raster!.Width);
        Assert.Equal(20, result.Raster.Height);
        Assert.Equal([40d, 40d], result.Raster.PixelSize!);
    }

    [Fact]
    public async Task ProcessingRefusesToReplaceAnExistingOutput()
    {
        var source = fixture.CreateGradientRaster();
        var existing = fixture.CreateGradientRaster();
        var request = new RasterTranslateRequest { Input = source, Output = existing };

        await Assert.ThrowsAsync<McpException>(() => conversion.TranslateAsync(request));
        var replaced = await conversion.TranslateAsync(request with { Overwrite = true, OutputType = "Int16" });

        Assert.Equal("Int16", replaced.Raster!.DataType);
    }

    [Fact]
    public async Task InvalidOptionsAndMissingFilesGiveToolErrors()
    {
        var source = fixture.CreateGradientRaster();

        var badOption = await Assert.ThrowsAsync<McpException>(() => conversion.TranslateAsync(new RasterTranslateRequest
        {
            Input = source,
            Output = fixture.NewPath(".tif"),
            AdditionalArguments = ["-nonsense"]
        }));
        var badBand = await Assert.ThrowsAsync<McpException>(() => raster.HistogramAsync(source, band: 9));
        var missing = await Assert.ThrowsAsync<McpException>(() => raster.InfoAsync(Path.Combine(fixture.Root, "missing.tif")));

        Assert.Contains("-nonsense", badOption.Message);
        Assert.Contains("Band 9 does not exist", badBand.Message);
        Assert.Contains("does not exist", missing.Message);
    }
}
