using System.IO.Compression;
using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class AlgorithmAndVsiTests(GdalFixture fixture)
{
    private readonly AlgorithmTools algorithms = new(fixture.Context);
    private readonly VsiTools vsi = new(fixture.Context);

    private static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] items)
    {
        return items.ToDictionary(item => item.Name, item => JsonSerializer.SerializeToElement(item.Value));
    }

    [Fact]
    public async Task ListAndDescribeExposeTheUnifiedCommand()
    {
        var all = await algorithms.ListAsync();
        var raster = await algorithms.ListAsync(group: "raster", contains: "reproject");
        var usage = await algorithms.DescribeAsync("raster reproject");

        Assert.True(all.Total > 100);
        Assert.Contains(all.Algorithms, item => item.Algorithm == "vector grid invdist");
        Assert.Equal("raster reproject", Assert.Single(raster.Algorithms).Algorithm);
        using var document = JsonDocument.Parse(usage);
        Assert.Contains(document.RootElement.GetProperty("input_arguments").EnumerateArray(), argument => argument.GetProperty("name").GetString() == "output-crs");
    }

    [Fact]
    public async Task RunExecutesProcessingAndInspectionAlgorithms()
    {
        var source = fixture.CreateGradientRaster();
        var output = fixture.NewPath(".tif");

        var reprojected = await algorithms.RunAsync("raster reproject", Arguments(("input", source), ("output", output), ("output-crs", "EPSG:4326"), ("resampling", "bilinear")));
        var info = await algorithms.RunAsync("raster info", Arguments(("input", output)));

        Assert.Equal("EPSG:4326", reprojected.Output!.Raster!.Crs!.Code);
        using var document = JsonDocument.Parse(info.Text!);
        Assert.Equal("GTiff", document.RootElement.GetProperty("driverShortName").GetString());
    }

    [Fact]
    public async Task RunValidatesArgumentsPathsAndOverwrite()
    {
        var source = fixture.CreateGradientRaster();
        var existing = fixture.CreateGradientRaster();

        var unknownArgument = await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster info", Arguments(("input", source), ("bogus", 1))));
        var reserved = await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster info", Arguments(("input", source), ("config", "GDAL_DRIVER_PATH=/tmp"))));
        var unknownAlgorithm = await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster frobnicate", Arguments()));
        var group = await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster", Arguments()));

        Assert.Contains("Available arguments", unknownArgument.Message);
        Assert.Contains("no argument 'config'", reserved.Message);
        Assert.Contains("does not exist", unknownAlgorithm.Message);
        Assert.Contains("group of algorithms", group.Message);
        await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster convert", Arguments(("input", source), ("output", existing))));
        await Assert.ThrowsAsync<McpException>(() => algorithms.RunAsync("raster info", Arguments(("input", "/vsistdin/"))));
    }

    [Fact]
    public async Task ReadOnlyServerLimitsAlgorithmsToInspection()
    {
        var source = fixture.CreateGradientRaster();
        var readOnly = new AlgorithmTools(fixture.CreateContext(options => options.ReadOnly = true));

        var info = await readOnly.RunAsync("raster info", Arguments(("input", source)));
        var error = await Assert.ThrowsAsync<McpException>(() => readOnly.RunAsync("raster convert", Arguments(("input", source), ("output", "/vsimem/x.tif"))));

        Assert.NotNull(info.Text);
        Assert.Contains("read-only mode", error.Message);
    }

    [Fact]
    public async Task RestrictedRootsApplyToAlgorithmInputsAndBlockPipelines()
    {
        var source = fixture.CreateGradientRaster();
        var restricted = new AlgorithmTools(fixture.CreateContext(options => options.AllowedRoots = Path.Combine(fixture.Root, "sandbox")));

        await Assert.ThrowsAsync<McpException>(() => restricted.RunAsync("raster info", Arguments(("input", source))));
        var pipeline = await Assert.ThrowsAsync<McpException>(() => restricted.RunAsync("raster pipeline", Arguments(("pipeline", $"read {source} ! write /vsimem/out.tif"))));
        Assert.Contains("Pipeline algorithms", pipeline.Message);
    }

    [Fact]
    public async Task VirtualFilesystemsAreListedCopiedAndDeleted()
    {
        var source = fixture.CreateGradientRaster();
        var directory = $"/vsimem/{Guid.NewGuid():N}";
        var copy = $"{directory}/copy.tif";

        await vsi.MkdirAsync(directory);
        await vsi.CopyAsync(source, copy);
        var listing = await vsi.ListAsync(directory, details: true);
        var info = await new RasterTools(fixture.Context).InfoAsync(copy);
        var filesystems = await vsi.FilesystemsAsync("/vsis3/");
        await vsi.DeleteAsync(directory, recursive: true);

        Assert.Equal(["copy.tif"], listing.Entries);
        Assert.Contains("copy.tif", listing.DetailsJson);
        Assert.Equal(GdalFixture.RasterWidth, info.Width);
        Assert.Contains("/vsizip/", filesystems.Prefixes);
        Assert.Contains(filesystems.Options!, option => option.Name == "AWS_SECRET_ACCESS_KEY");
        Assert.False(PathPolicy.Exists(copy));
    }

    [Fact]
    public async Task ArchivesAreReadThroughTheZipFilesystem()
    {
        var source = fixture.CreateGradientRaster();
        var archive = fixture.NewPath(".zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(source, "inner/raster.tif");
        }

        var virtualPath = $"/vsizip/{archive.Replace('\\', '/')}/inner/raster.tif";
        var info = await new RasterTools(fixture.Context).InfoAsync(virtualPath);
        var listing = await vsi.ListAsync($"/vsizip/{archive.Replace('\\', '/')}/inner");

        Assert.Equal(GdalFixture.RasterHeight, info.Height);
        Assert.Equal(["raster.tif"], listing.Entries);
    }

    [Fact]
    public async Task DatasetsAreCopiedIdentifiedAndDeleted()
    {
        var source = fixture.CreateGradientRaster();
        var copy = fixture.NewPath(".tif");
        var datasets = new DatasetTools(fixture.Context);

        await datasets.CopyAsync(source, copy);
        var identified = await datasets.IdentifyAsync(copy);
        await datasets.DeleteAsync(copy);

        Assert.True(identified.Recognized);
        Assert.Equal("GTiff", identified.Driver!.ShortName);
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MultidimensionalDatasetsAreDescribedAndConverted()
    {
        var zarr = Path.Combine(fixture.Root, $"{Guid.NewGuid():N}.zarr");
        var multidim = new MultidimTools(fixture.Context);

        var converted = await multidim.TranslateAsync(fixture.CreateGradientRaster(), zarr, format: "ZARR");
        var report = await multidim.InfoAsync(zarr);

        Assert.Equal("Zarr", converted.Driver);
        using var document = JsonDocument.Parse(report);
        Assert.Equal("group", document.RootElement.GetProperty("type").GetString());
        Assert.True(document.RootElement.GetProperty("arrays").EnumerateObject().Any());
    }
}
