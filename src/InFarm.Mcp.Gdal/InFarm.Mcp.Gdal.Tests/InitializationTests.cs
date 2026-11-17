namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class InitializationTests(GdalFixture fixture)
{
    [Fact]
    public void RuntimeReportsNativeLibraryVersions()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", fixture.Runtime.GdalVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+$", fixture.Runtime.ProjVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+$", fixture.Runtime.GeosVersion);
        Assert.True(fixture.Runtime.ProjDatabaseAvailable);
    }

    [Theory]
    [InlineData("GTiff")]
    [InlineData("COG")]
    [InlineData("VRT")]
    [InlineData("GPKG")]
    [InlineData("GeoJSON")]
    [InlineData("ESRI Shapefile")]
    [InlineData("FlatGeobuf")]
    public void CommonDriversAreRegistered(string driver)
    {
        Assert.True(fixture.Runtime.DriverCount > 50);
        Assert.NotNull(GdalApi.GetDriverByName(driver));
    }

    [Fact]
    public async Task ServerInfoDescribesTheRuntimeAndTheConfiguration()
    {
        var info = await new ServerTools(fixture.Context).ServerInfoAsync(CancellationToken.None);

        Assert.Equal(fixture.Runtime.GdalVersion, info.GdalVersion);
        Assert.True(info.RasterDriverCount > 0);
        Assert.True(info.VectorDriverCount > 0);
        Assert.Contains("/vsimem/", info.VirtualFilesystems);
        Assert.False(info.ReadOnly);
    }

    [Fact]
    public async Task DriverInfoListsCreationOptions()
    {
        var info = await new DriverTools(fixture.Context).InfoAsync("GTiff");

        Assert.True(info.Summary.CanCreate);
        Assert.Contains("tif", info.Summary.Extensions!);
        Assert.Contains(info.CreationOptions!, option => option.Name == "COMPRESS" && option.Values!.Contains("DEFLATE"));
        Assert.Contains("Float32", info.CreationDataTypes!);
    }

    [Fact]
    public async Task DriverListFiltersByKindAndExtension()
    {
        var drivers = await new DriverTools(fixture.Context).ListAsync(kind: "vector", extension: "gpkg");

        Assert.Contains(drivers.Drivers, driver => driver.ShortName == "GPKG");
        Assert.All(drivers.Drivers, driver => Assert.True(driver.Vector));
    }

    [Fact]
    public async Task UnknownDriverGivesAnActionableError()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => new DriverTools(fixture.Context).InfoAsync("NoSuchDriver"));

        Assert.Contains("gdal_driver_list", error.Message);
    }

    [Fact]
    public async Task ConfigurationOptionsAreSetReadAndProtected()
    {
        var tools = new ServerTools(fixture.Context);

        await tools.ConfigSetAsync("GDAL_HTTP_TIMEOUT", "17");
        await tools.ConfigSetAsync("AWS_SECRET_ACCESS_KEY", "not-a-real-secret");
        var values = await tools.ConfigGetAsync(["GDAL_HTTP_TIMEOUT", "AWS_SECRET_ACCESS_KEY"], CancellationToken.None);
        await tools.ConfigSetAsync("AWS_SECRET_ACCESS_KEY");

        Assert.Equal("17", values[0].Value);
        Assert.Equal("***", values[1].Value);
        await Assert.ThrowsAsync<McpException>(() => tools.ConfigSetAsync("GDAL_DRIVER_PATH", "/tmp/plugins"));
    }
}
