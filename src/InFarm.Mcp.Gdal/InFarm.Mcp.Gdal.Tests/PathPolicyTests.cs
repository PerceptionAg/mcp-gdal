namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class PathPolicyTests(GdalFixture fixture)
{
    [Fact]
    public void AllowedRootsAcceptPathsInsideAndRejectPathsOutside()
    {
        var inside = fixture.CreateGradientRaster();
        var policy = fixture.CreateContext(options => options.AllowedRoots = fixture.Root).Paths;

        Assert.Equal(inside, policy.Input(inside));
        var outside = Path.Combine(Path.GetTempPath(), "outside-the-root.tif");
        var error = Assert.Throws<McpException>(() => policy.Output(outside, false));
        Assert.Contains("outside the allowed roots", error.Message);
        Assert.Throws<McpException>(() => policy.Input(Path.Combine(fixture.Root, "..", "escape.tif")));
    }

    [Fact]
    public void AllowedRootsApplyToFilesInsideArchives()
    {
        var policy = fixture.CreateContext(options => options.AllowedRoots = fixture.Root).Paths;
        var insideArchive = "/vsizip/" + Path.Combine(fixture.Root, "data.zip").Replace('\\', '/') + "/inner.tif";

        Assert.Equal(insideArchive, policy.Input(insideArchive));
        Assert.Throws<McpException>(() => policy.Input("/vsizip/" + Path.GetTempPath().Replace('\\', '/') + "other.zip/inner.tif"));
    }

    [Fact]
    public void ReadOnlyModeRejectsOutputsExceptInMemory()
    {
        var policy = fixture.CreateContext(options => options.ReadOnly = true).Paths;

        var error = Assert.Throws<McpException>(() => policy.Output(fixture.NewPath(".tif"), false));
        Assert.Contains("read-only", error.Message);
        Assert.Equal("/vsimem/result.tif", policy.Output("/vsimem/result.tif", false));
    }

    [Fact]
    public void ExistingOutputNeedsExplicitOverwrite()
    {
        var existing = fixture.CreateGradientRaster();

        var error = Assert.Throws<McpException>(() => fixture.Context.Paths.Output(existing, false));
        Assert.Contains("overwrite=true", error.Message);

        var locked = fixture.CreateContext(options => options.AllowOverwrite = false).Paths;
        Assert.Throws<McpException>(() => locked.Output(existing, true));
        Assert.True(File.Exists(existing));

        fixture.Context.Paths.Output(existing, true);
        Assert.False(File.Exists(existing));
    }

    [Theory]
    [InlineData("/vsistdout/")]
    [InlineData("/vsistdin/")]
    [InlineData("/vsistdout_redirect//vsimem/x")]
    public void StandardStreamsAreNeverAvailable(string path)
    {
        Assert.Throws<McpException>(() => fixture.Context.Paths.Input(path));
        Assert.Throws<McpException>(() => fixture.Context.Paths.Output(path, true));
    }

    [Theory]
    [InlineData("/vsicurl/https://example.com/data.tif")]
    [InlineData("/vsis3/bucket/data.tif")]
    [InlineData("https://example.com/data.geojson")]
    [InlineData("PG:host=db.example.com dbname=gis")]
    [InlineData("/vsizip//vsicurl/https://example.com/data.zip/inner.shp")]
    public void RemoteNamesNeedRemoteAccess(string path)
    {
        var offline = fixture.CreateContext(options => options.AllowRemoteAccess = false).Paths;

        Assert.Equal(path, fixture.Context.Paths.Input(path));
        var error = Assert.Throws<McpException>(() => offline.Input(path));
        Assert.Contains("Remote access is disabled", error.Message);
    }

    [Fact]
    public void MissingInputGivesAClearError()
    {
        var error = Assert.Throws<McpException>(() => fixture.Context.Paths.Input(Path.Combine(fixture.Root, "missing.tif")));

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void RedactorMasksCredentials()
    {
        var text = Redactor.Redact("HTTP error for https://user:hunter2@example.com/a.tif?X-Amz-Signature=abcdef&x=1 password=topsecret");

        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("abcdef", text);
        Assert.DoesNotContain("topsecret", text);
        Assert.True(Redactor.IsSecretKey("AWS_SECRET_ACCESS_KEY"));
        Assert.False(Redactor.IsSecretKey("GDAL_CACHEMAX"));
    }

    [Fact]
    public void AdditionalArgumentsRejectProcessWideSwitches()
    {
        var error = Assert.Throws<McpException>(() => new ArgList().Extra(["--config", "GDAL_DRIVER_PATH", "/tmp"], fixture.Context.Paths));

        Assert.Contains("not allowed", error.Message);
        Assert.Throws<McpException>(() => new ArgList().Extra(["-cutline", "/vsistdin/"], fixture.Context.Paths));
    }
}
