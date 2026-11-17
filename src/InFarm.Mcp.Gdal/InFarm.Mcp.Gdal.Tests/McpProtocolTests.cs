using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InFarm.Mcp.Gdal.Tests;

// Starts the real server process and talks to it over standard I/O with the SDK client, as an MCP client application would.
[Collection(GdalCollection.Name)]
public sealed class McpProtocolTests(GdalFixture fixture) : IAsyncLifetime
{
    private McpClient? client;

    private McpClient Client => client ?? throw new InvalidOperationException("The client is not connected.");

    public async Task InitializeAsync()
    {
        var serverAssembly = typeof(ServerHost).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "infarm-mcp-gdal",
            Command = "dotnet",
            Arguments = [serverAssembly, "--log-level", "Warning"],
            WorkingDirectory = fixture.Root
        });
        client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public void ServerIdentifiesItself()
    {
        Assert.Equal("infarm-mcp-gdal", Client.ServerInfo.Name);
        Assert.Contains("gdal_dataset_info", Client.ServerInstructions);
    }

    [Fact]
    public async Task ToolsAreDiscoverableWithDescribedSchemas()
    {
        var tools = await Client.ListToolsAsync();

        string[] expected =
        [
            "gdal_server_info", "gdal_driver_list", "gdal_driver_info", "gdal_dataset_info", "gdal_raster_info", "gdal_raster_translate",
            "gdal_raster_warp", "gdal_raster_calculate", "gdal_raster_dem", "gdal_vector_info", "gdal_vector_features", "gdal_vector_convert",
            "gdal_vector_overlay", "gdal_crs_info", "gdal_crs_transform_points", "gdal_algorithm_run", "gdal_vsi_list"
        ];
        Assert.All(expected, name => Assert.Contains(tools, tool => tool.Name == name));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description."));
        var info = tools.Single(tool => tool.Name == "gdal_raster_info");
        var path = info.JsonSchema.GetProperty("properties").GetProperty("path");
        Assert.False(string.IsNullOrWhiteSpace(path.GetProperty("description").GetString()));
        Assert.True(info.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.NotNull(info.ProtocolTool.OutputSchema);
    }

    [Fact]
    public async Task ToolCallsReturnStructuredContent()
    {
        var raster = fixture.CreateGradientRaster();

        var result = await Client.CallToolAsync("gdal_raster_info", new Dictionary<string, object?> { ["path"] = raster, ["statistics"] = "exact" });

        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(GdalFixture.RasterWidth, content.GetProperty("width").GetInt32());
        Assert.Equal("EPSG:32755", content.GetProperty("crs").GetProperty("code").GetString());
        Assert.Equal(0, content.GetProperty("bands")[0].GetProperty("statistics").GetProperty("min").GetDouble());
    }

    [Fact]
    public async Task RequestModelsAndEnumerationsAreAcceptedAsJson()
    {
        var output = fixture.NewPath(".tif");

        var translated = await Client.CallToolAsync("gdal_raster_translate", new Dictionary<string, object?>
        {
            ["request"] = new { input = fixture.CreateGradientRaster(), output, outputType = "Int16", creationOptions = new[] { "COMPRESS=DEFLATE" } }
        });
        var transformed = await Client.CallToolAsync("gdal_crs_transform_points", new Dictionary<string, object?>
        {
            ["sourceCrs"] = "EPSG:4326",
            ["targetCrs"] = "EPSG:32755",
            ["points"] = new[] { new[] { -36.0, 147.0 } },
            ["axisOrder"] = "authority"
        });

        Assert.NotEqual(true, translated.IsError);
        Assert.True(File.Exists(output));
        var point = Assert.IsType<JsonElement>(transformed.StructuredContent).GetProperty("points")[0];
        Assert.Equal(500000, point.GetProperty("x").GetDouble(), 3);
    }

    [Fact]
    public async Task FailuresBecomeToolErrorsAndTheServerKeepsRunning()
    {
        var missing = await Client.CallToolAsync("gdal_raster_info", new Dictionary<string, object?> { ["path"] = Path.Combine(fixture.Root, "missing.tif") });
        var badCrs = await Client.CallToolAsync("gdal_crs_info", new Dictionary<string, object?> { ["crs"] = "EPSG:999999" });
        var afterwards = await Client.CallToolAsync("gdal_server_info", new Dictionary<string, object?>());

        Assert.True(missing.IsError);
        Assert.Contains("does not exist", Assert.IsType<TextContentBlock>(missing.Content[0]).Text);
        Assert.True(badCrs.IsError);
        Assert.Contains("gdal_crs_lookup", Assert.IsType<TextContentBlock>(badCrs.Content[0]).Text);
        Assert.NotEqual(true, afterwards.IsError);
    }

    [Fact]
    public async Task CancellationStopsALongOperationAndRemovesItsOutput()
    {
        var large = fixture.NewPath(".tif");
        await Client.CallToolAsync("gdal_raster_create", new Dictionary<string, object?>
        {
            ["output"] = large, ["width"] = 12000, ["height"] = 12000, ["crs"] = "EPSG:32755",
            ["bounds"] = new[] { 500000.0, 6000000.0, 620000.0, 6120000.0 }
        });
        var output = fixture.NewPath(".tif");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client.CallToolAsync(
            "gdal_raster_warp",
            new Dictionary<string, object?> { ["request"] = new { inputs = new[] { large }, output, targetCrs = "EPSG:4326", resampling = "lanczos" } },
            cancellationToken: cancellation.Token).AsTask());

        var stillRunning = await Client.CallToolAsync("gdal_server_info", new Dictionary<string, object?>());
        Assert.NotEqual(true, stillRunning.IsError);
        for (var attempt = 0; attempt < 50 && File.Exists(output); attempt++)
        {
            await Task.Delay(100);
        }

        Assert.False(File.Exists(output));
    }
}
