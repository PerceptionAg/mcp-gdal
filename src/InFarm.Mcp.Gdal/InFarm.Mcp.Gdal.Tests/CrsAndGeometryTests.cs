namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class CrsAndGeometryTests(GdalFixture fixture)
{
    private readonly ProjectionTools projection = new(fixture.Context);
    private readonly GeometryTools geometry = new(fixture.Context);

    [Fact]
    public async Task InfoParsesAnEpsgCodeAndReportsItsAxisOrder()
    {
        var info = await projection.InfoAsync("EPSG:4326", formats: ["wkt2", "proj4", "projjson"]);

        Assert.Equal("WGS 84", info.Name);
        Assert.Equal("EPSG:4326", info.Code);
        Assert.Equal("geographic", info.Kind);
        Assert.True(info.AuthorityOrderIsYThenX);
        Assert.Equal("north", info.Axes[0].Direction);
        Assert.Equal("east", info.Axes[1].Direction);
        Assert.StartsWith("GEOGCRS", info.Wkt2);
        Assert.Contains("+proj=longlat", info.Proj4);
        Assert.Contains("\"GeographicCRS\"", info.ProjJson);
    }

    [Fact]
    public async Task InfoParsesProjStringsWktAndIdentifiesThem()
    {
        var fromProj = await projection.InfoAsync("+proj=utm +zone=55 +south +datum=WGS84 +units=m +no_defs", identify: true);
        var wkt = (await projection.InfoAsync("EPSG:7855", formats: ["wkt1"])).Wkt1!;
        var fromWkt = await projection.InfoAsync(wkt);

        Assert.Equal("projected", fromProj.Kind);
        Assert.Equal(55, fromProj.UtmZone);
        Assert.Equal("south", fromProj.UtmHemisphere);
        Assert.Contains(fromProj.Matches!, match => match.Code == "EPSG:32755");
        Assert.Equal("GDA2020 / MGA zone 55", fromWkt.Name);
        Assert.Equal("metre", fromWkt.LinearUnits);
    }

    [Fact]
    public async Task LookupFindsCodesByNameTypeAndLocation()
    {
        var byName = await projection.LookupAsync("GDA2020 MGA zone 55");
        var byLocation = await projection.LookupAsync("UTM zone", type: "projected", longitude: 147, latitude: -36, limit: 500);

        Assert.Contains(byName.Items, item => item.Code == "EPSG:7855");
        Assert.Contains(byLocation.Items, item => item.Code == "EPSG:32755");
        Assert.DoesNotContain(byLocation.Items, item => item.Code == "EPSG:32633");
    }

    [Fact]
    public async Task InvalidCoordinateReferenceSystemGivesAnActionableError()
    {
        var unknownCode = await Assert.ThrowsAsync<McpException>(() => projection.InfoAsync("EPSG:999999"));
        var nonsense = await Assert.ThrowsAsync<McpException>(() => projection.InfoAsync("+proj=nonsense +datum=WGS84"));

        Assert.Contains("gdal_crs_lookup", unknownCode.Message);
        Assert.Contains("could not be interpreted", nonsense.Message);
    }

    [Fact]
    public async Task TransformUsesLongitudeLatitudeOrderByDefault()
    {
        var result = await projection.TransformPointsAsync("EPSG:4326", "EPSG:3857", [[147, -36]]);

        Assert.Contains("longitude", result.SourceAxisOrder);
        Assert.InRange(result.Points![0].X!.Value, 16363965.0, 16363965.3);
        Assert.InRange(result.Points[0].Y!.Value, -4300700, -4300500);
    }

    [Fact]
    public async Task TransformHonoursAuthorityAxisOrderWhenRequested()
    {
        var traditional = await projection.TransformPointsAsync("EPSG:4326", "EPSG:32755", [[147, -36]]);
        var authority = await projection.TransformPointsAsync("EPSG:4326", "EPSG:32755", [[-36, 147]], axisOrder: AxisOrder.Authority);
        var back = await projection.TransformPointsAsync(
            "EPSG:32755", "EPSG:4326", [[traditional.Points![0].X!.Value, traditional.Points[0].Y!.Value]], axisOrder: AxisOrder.Authority);

        Assert.Equal(500000, traditional.Points![0].X!.Value, 3);
        Assert.Equal(traditional.Points[0].X!.Value, authority.Points![0].X!.Value, 6);
        Assert.Equal(traditional.Points[0].Y!.Value, authority.Points[0].Y!.Value, 6);
        Assert.Contains("latitude", authority.SourceAxisOrder, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-36, back.Points![0].X!.Value, 3);
        Assert.Equal(147, back.Points[0].Y!.Value, 3);
    }

    [Fact]
    public async Task TransformReportsPointsOutsideTheProjectionDomain()
    {
        var result = await projection.TransformPointsAsync("EPSG:4326", "EPSG:3857", [[10, 95], [10, 45]]);

        Assert.NotNull(result.Points![0].Error);
        Assert.Null(result.Points[1].Error);
    }

    [Fact]
    public async Task BoundsCompareAndUtmZoneWork()
    {
        var bounds = await projection.TransformBoundsAsync("EPSG:32755", "EPSG:4326", [500000, 6000000, 501000, 6000800]);
        var same = await projection.CompareAsync("EPSG:4326", "+proj=longlat +datum=WGS84 +no_defs");
        var different = await projection.CompareAsync("EPSG:4326", "EPSG:4283");
        var zone = await projection.UtmZoneAsync(147.3, -36.1);
        var norway = await projection.UtmZoneAsync(5.3, 60.4);

        Assert.InRange(bounds.Bounds!.MinX, 146.99, 147.01);
        Assert.InRange(bounds.Bounds.MaxY, -36.14, -36.13);
        Assert.False(same.Equivalent);
        Assert.True(same.EquivalentIgnoringAxisOrder);
        Assert.False(different.EquivalentIgnoringAxisOrder);
        Assert.Equal(55, zone.Zone);
        Assert.Equal("EPSG:32755", zone.Code);
        Assert.Equal(147, zone.CentralMeridian);
        Assert.Equal(32, norway.Zone);
    }

    [Fact]
    public async Task GeometryInfoMeasuresAndValidates()
    {
        var square = await geometry.InfoAsync("POLYGON ((147 -36,147.01 -36,147.01 -35.99,147 -35.99,147 -36))", crs: "EPSG:4326");
        var bowtie = await geometry.InfoAsync("POLYGON ((0 0,10 10,10 0,0 10,0 0))");

        Assert.Equal("Polygon", square.Type);
        Assert.True(square.IsValid);
        Assert.Equal(5, square.PointCount);
        Assert.Equal(0.0001, square.Area, 8);
        Assert.InRange(square.GeodesicArea!.Value, 990_000, 1_010_000);
        Assert.False(bowtie.IsValid);
        Assert.Contains("Self-intersection", bowtie.InvalidityReason);
    }

    [Fact]
    public async Task GeometryOperationsAndRelationsAreEvaluated()
    {
        const string left = "POLYGON ((0 0,10 0,10 10,0 10,0 0))";
        const string right = "POLYGON ((5 5,15 5,15 15,5 15,5 5))";

        var intersection = await geometry.OperationAsync("intersection", left, other: right);
        var buffer = await geometry.OperationAsync("buffer", "POINT (0 0)", distance: 10, outputFormat: GeometryFormat.Geojson);
        var transformed = await geometry.OperationAsync("transform", "POINT (147 -36)", crs: "EPSG:4326", targetCrs: "EPSG:32755");
        var relation = await geometry.RelateAsync(left, right);

        Assert.Equal(25, intersection.Area);
        Assert.InRange(buffer.Area, 310, 315);
        Assert.Contains("\"Polygon\"", buffer.Geometry);
        Assert.InRange(transformed.Envelope!.MinX, 499999.9, 500000.1);
        Assert.True(relation.Intersects);
        Assert.True(relation.Overlaps);
        Assert.False(relation.Contains);
        Assert.Equal(0, relation.Distance);
        await Assert.ThrowsAsync<McpException>(() => geometry.OperationAsync("buffer", "POINT (0 0)"));
        await Assert.ThrowsAsync<McpException>(() => geometry.InfoAsync("POLYGON ((not wkt"));
    }
}
