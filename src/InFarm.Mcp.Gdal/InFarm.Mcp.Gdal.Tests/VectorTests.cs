using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tests;

[Collection(GdalCollection.Name)]
public sealed class VectorTests(GdalFixture fixture)
{
    private const string Bowtie = "POLYGON ((0 0,10 10,10 0,0 10,0 0))";

    private readonly VectorTools vector = new(fixture.Context);
    private readonly VectorEditTools edit = new(fixture.Context);
    private readonly VectorProcessingTools processing = new(fixture.Context);

    [Fact]
    public async Task CreateAndInfoDescribeTheLayerSchema()
    {
        var path = await fixture.CreateParcelsAsync();

        var info = await vector.InfoAsync(path);

        Assert.Equal("GPKG", info.Driver);
        var layer = Assert.Single(info.Layers);
        Assert.Equal("parcels", layer.Name);
        Assert.Equal("Polygon", layer.GeometryType);
        Assert.Equal(4, layer.FeatureCount);
        Assert.Equal(GdalFixture.ProjectedCrs, layer.Crs!.Code);
        Assert.Equal(new Bounds(500000, 6000000, 500400, 6000100), layer.Extent);
        Assert.Equal(["name", "zone", "value"], layer.Fields!.Select(field => field.Name));
        Assert.Equal("Real", layer.Fields![2].Type);
        Assert.Contains("FastSpatialFilter", layer.Capabilities!);
    }

    [Fact]
    public async Task InfoJsonReturnsTheNativeOgrinfoReport()
    {
        var json = await vector.InfoJsonAsync(await fixture.CreateParcelsAsync());

        using var document = JsonDocument.Parse(json);
        Assert.Equal("parcels", document.RootElement.GetProperty("layers")[0].GetProperty("name").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("layers")[0].GetProperty("featureCount").GetInt32());
    }

    [Fact]
    public async Task FeaturesArePagedAndCarryAttributesAndGeometry()
    {
        var path = await fixture.CreateParcelsAsync();

        var first = await vector.FeaturesAsync(path, limit: 3, countMatching: true);
        var second = await vector.FeaturesAsync(path, limit: 3, offset: first.NextOffset!.Value, geometryFormat: GeometryFormat.Geojson);

        Assert.Equal(3, first.Returned);
        Assert.Equal(4, first.TotalMatching);
        Assert.Equal(3, first.NextOffset);
        Assert.Equal("parcel 1", first.Features[0].Properties!["name"]);
        Assert.Equal(10.5, first.Features[0].Properties!["value"]);
        Assert.StartsWith("POLYGON", first.Features[0].Geometry);
        Assert.Single(second.Features);
        Assert.Null(second.NextOffset);
        Assert.Contains("\"Polygon\"", second.Features[0].Geometry);
    }

    [Fact]
    public async Task AttributeAndSpatialFiltersSelectFeatures()
    {
        var path = await fixture.CreateParcelsAsync();

        var byAttribute = await vector.FeaturesAsync(path, where: "zone = 'east' AND value > 35", fields: ["name"]);
        var byBounds = await vector.FeaturesAsync(path, bounds: [500010, 6000010, 500090, 6000090], geometryFormat: GeometryFormat.Envelope);
        var byGeographicPoint = await vector.FeatureCountAsync(path, intersects: "POINT (147.0028 -36.1445)", filterCrs: "EPSG:4326");

        Assert.Equal("parcel 4", Assert.Single(byAttribute.Features).Properties!["name"]);
        Assert.Equal(["name"], byAttribute.Features[0].Properties!.Keys);
        Assert.Equal(new Bounds(500000, 6000000, 500100, 6000100), Assert.Single(byBounds.Features).Envelope);
        Assert.Equal(1, byGeographicPoint.FeatureCount);
        await Assert.ThrowsAsync<McpException>(() => vector.FeaturesAsync(path, where: "no_such_field = 1"));
    }

    [Fact]
    public async Task LargeGeometriesAreReplacedByTheirEnvelope()
    {
        var path = await fixture.CreateParcelsAsync();
        var limited = new VectorTools(fixture.CreateContext(options => options.MaxGeometryCharacters = 20));

        var page = await limited.FeaturesAsync(path, limit: 1);

        Assert.Null(page.Features[0].Geometry);
        Assert.True(page.Features[0].GeometryOmitted);
        Assert.NotNull(page.Features[0].Envelope);
    }

    [Fact]
    public async Task SqlRunsQueriesAndRejectsModifications()
    {
        var path = await fixture.CreateParcelsAsync();

        var grouped = await vector.SqlAsync(path, "SELECT zone, COUNT(*) AS n, SUM(value) AS total FROM parcels GROUP BY zone ORDER BY zone", dialect: "SQLITE");

        Assert.Equal(2, grouped.Returned);
        Assert.Equal("east", grouped.Features[0].Properties!["zone"]);
        Assert.Equal(73.5, grouped.Features[0].Properties!["total"]);
        await Assert.ThrowsAsync<McpException>(() => vector.SqlAsync(path, "DELETE FROM parcels"));
        await Assert.ThrowsAsync<McpException>(() => vector.SqlAsync(path, "SELECT load_extension('evil')", dialect: "SQLITE"));
        var writable = new VectorTools(fixture.CreateContext(options => options.AllowSqlWrites = true));
        await writable.SqlAsync(path, "DELETE FROM parcels WHERE zone = 'east'");
        Assert.Equal(2, (await vector.FeatureCountAsync(path)).FeatureCount);
    }

    [Fact]
    public async Task FeaturesAreInsertedUpdatedAndDeleted()
    {
        var path = await fixture.CreateParcelsAsync();

        var inserted = await edit.FeaturesWriteAsync(path,
        [
            new FeatureInput
            {
                Geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[147.0,-36.14],[147.001,-36.14],[147.001,-36.139],[147.0,-36.139],[147.0,-36.14]]]}",
                Properties = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("from geojson") }
            }
        ], geometryCrs: "EPSG:4326");
        var fid = Assert.Single(inserted.Fids!);
        var updated = await edit.FeaturesWriteAsync(path,
        [
            new FeatureInput { Fid = fid, Properties = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(99) } }
        ], mode: "update");
        var page = await vector.FeaturesAsync(path, where: $"fid = {fid}", geometryFormat: GeometryFormat.Envelope);
        var deleted = await edit.FeaturesDeleteAsync(path, where: "value >= 42");

        Assert.Equal(5, inserted.FeatureCount);
        Assert.Equal(1, updated.Updated);
        Assert.Equal(99.0, page.Features[0].Properties!["value"]);
        Assert.Equal("from geojson", page.Features[0].Properties!["name"]);
        Assert.InRange(page.Features[0].Envelope!.MinX, 499990, 500010);
        Assert.Equal(2, deleted.Deleted);
        Assert.Equal(3, deleted.FeatureCount);
        await Assert.ThrowsAsync<McpException>(() => edit.FeaturesWriteAsync(path,
            [new FeatureInput { Properties = new Dictionary<string, JsonElement> { ["missing"] = JsonSerializer.SerializeToElement(1) } }]));
    }

    [Fact]
    public async Task SchemaIsEditedInPlace()
    {
        var path = await fixture.CreateParcelsAsync();

        await edit.FieldCreateAsync(path, new FieldDefinition { Name = "surveyed", Type = "Integer", SubType = "Boolean" });
        await edit.FieldAlterAsync(path, "zone", newName: "region");
        var afterDelete = await edit.FieldDeleteAsync(path, "value");
        await edit.LayerCreateAsync(path, "points", geometryType: "Point", crs: "EPSG:4326");
        var withTwoLayers = await vector.InfoAsync(path, includeFields: false);
        await edit.LayerDeleteAsync(path, "points");
        var withOneLayer = await vector.InfoAsync(path, includeFields: false);

        Assert.Equal(["name", "region", "surveyed"], afterDelete.Fields!.Select(field => field.Name));
        Assert.Equal("Boolean", afterDelete.Fields![2].SubType);
        Assert.Equal(2, withTwoLayers.LayerCount);
        Assert.Equal(1, withOneLayer.LayerCount);
    }

    [Fact]
    public async Task ConvertChangesFormatAndAppendsToExistingData()
    {
        var path = await fixture.CreateParcelsAsync();
        var geoJson = fixture.NewPath(".geojson");
        var flatGeobuf = fixture.NewPath(".fgb");
        var shapefile = fixture.NewPath(".shp");
        var target = fixture.NewPath(".gpkg");

        var asGeoJson = await processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = geoJson, TargetCrs = "EPSG:4326" });
        var asFlatGeobuf = await processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = flatGeobuf, Where = "zone = 'west'" });
        var asShapefile = await processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = shapefile, SelectFields = ["name"] });
        await processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = target });
        var appended = await processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = target, Mode = "append" });

        Assert.Equal("GeoJSON", asGeoJson.Driver);
        Assert.Equal("EPSG:4326", asGeoJson.Layers![0].Crs!.Code);
        Assert.Equal("FlatGeobuf", asFlatGeobuf.Driver);
        Assert.Equal(2, asFlatGeobuf.Layers![0].FeatureCount);
        Assert.Equal("ESRI Shapefile", asShapefile.Driver);
        Assert.Equal(8, appended.Layers![0].FeatureCount);
        await Assert.ThrowsAsync<McpException>(() => processing.ConvertAsync(new VectorConvertRequest { Input = path, Output = target }));
    }

    [Fact]
    public async Task ReprojectFilterAndClipWriteNewDatasets()
    {
        var path = await fixture.CreateParcelsAsync();
        var reprojected = fixture.NewPath(".gpkg");
        var filtered = fixture.NewPath(".gpkg");
        var clipped = fixture.NewPath(".gpkg");

        await processing.ReprojectAsync(path, reprojected, "EPSG:4326");
        var filterResult = await processing.FilterAsync(path, filtered, where: "value < 30");
        await processing.ClipAsync(path, clipped, bounds: [500050, 6000000, 500150, 6000100]);

        var reprojectedInfo = await vector.InfoAsync(reprojected);
        Assert.Equal("EPSG:4326", reprojectedInfo.Layers[0].Crs!.Code);
        Assert.InRange(reprojectedInfo.Layers[0].Extent!.MinX, 146.99, 147.01);
        Assert.Equal(2, filterResult.Layers![0].FeatureCount);
        var clippedInfo = await vector.InfoAsync(clipped);
        Assert.Equal(new Bounds(500050, 6000000, 500150, 6000100), clippedInfo.Layers[0].Extent);
        Assert.Equal(2, clippedInfo.Layers[0].FeatureCount);
    }

    [Fact]
    public async Task GeometryOperationsProcessEveryFeature()
    {
        var path = await fixture.CreateParcelsAsync();
        var buffered = fixture.NewPath(".gpkg");
        var centroids = fixture.NewPath(".gpkg");
        var dissolved = fixture.NewPath(".gpkg");

        await processing.BufferAsync(path, buffered, distance: 10, joinStyle: "MITRE");
        var centroidResult = await processing.GeometryProcessAsync(path, centroids, "centroid");
        var dissolveResult = await processing.DissolveAsync(path, dissolved, byFields: ["zone"]);

        var bufferedInfo = await vector.InfoAsync(buffered);
        Assert.Equal(new Bounds(499990, 5999990, 500410, 6000110), bufferedInfo.Layers[0].Extent);
        Assert.Equal("Point", centroidResult.Layers![0].GeometryType);
        Assert.Equal(4, centroidResult.Layers[0].FeatureCount);
        Assert.Equal(2, dissolveResult.Layers![0].FeatureCount);
        var merged = await vector.FeaturesAsync(dissolved, where: "zone = 'west'", geometryFormat: GeometryFormat.Envelope);
        Assert.Equal(2L, merged.Features[0].Properties!["feature_count"]);
        Assert.Equal(new Bounds(500000, 6000000, 500200, 6000100), merged.Features[0].Envelope);
    }

    [Fact]
    public async Task OverlayIntersectsTwoLayers()
    {
        var parcels = await fixture.CreateParcelsAsync();
        var zone = fixture.NewPath(".gpkg");
        await edit.CreateAsync(zone, layerName: "zone", geometryType: "Polygon", crs: GdalFixture.ProjectedCrs,
            fields: [new FieldDefinition { Name = "label", Type = "String" }]);
        await edit.FeaturesWriteAsync(zone,
        [
            new FeatureInput
            {
                Geometry = "POLYGON ((500050 6000025,500250 6000025,500250 6000075,500050 6000075,500050 6000025))",
                Properties = new Dictionary<string, JsonElement> { ["label"] = JsonSerializer.SerializeToElement("flood") }
            }
        ]);
        var intersection = fixture.NewPath(".gpkg");
        var difference = fixture.NewPath(".gpkg");

        var intersected = await processing.OverlayAsync(parcels, zone, intersection, "intersection");
        var erased = await processing.OverlayAsync(parcels, zone, difference, "difference");

        Assert.Equal(3, intersected.Layers![0].FeatureCount);
        var page = await vector.FeaturesAsync(intersection, geometryFormat: GeometryFormat.None);
        Assert.Equal("flood", page.Features[0].Properties!["label"]);
        Assert.Equal(4, erased.Layers![0].FeatureCount);
    }

    [Fact]
    public async Task InvalidGeometriesAreFoundAndRepaired()
    {
        var path = fixture.NewPath(".gpkg");
        await edit.CreateAsync(path, layerName: "shapes", geometryType: "Unknown", crs: GdalFixture.ProjectedCrs);
        await edit.FeaturesWriteAsync(path, [new FeatureInput { Geometry = Bowtie }, new FeatureInput { Geometry = "POLYGON ((0 0,5 0,5 5,0 5,0 0))" }]);
        var repaired = fixture.NewPath(".gpkg");

        var before = await processing.ValidateAsync(path);
        await processing.RepairAsync(path, repaired);
        var after = await processing.ValidateAsync(repaired);

        Assert.Equal(2, before.Checked);
        Assert.Equal(1, before.InvalidCount);
        Assert.Contains("Self-intersection", Assert.Single(before.Invalid).Problem);
        Assert.Equal(0, after.InvalidCount);
        Assert.Equal(2, after.Checked);
    }

    [Fact]
    public async Task MergeTileIndexAndSpatialIndexWorkAcrossDatasets()
    {
        var first = await fixture.CreateParcelsAsync();
        var second = await fixture.CreateParcelsAsync();
        var merged = fixture.NewPath(".gpkg");
        var index = fixture.NewPath(".gpkg");
        var shapefile = fixture.NewPath(".shp");
        await processing.ConvertAsync(new VectorConvertRequest { Input = first, Output = shapefile });

        var mergeResult = await processing.MergeAsync([first, second], merged, layerName: "all_parcels", sourceField: "source");
        var indexResult = await processing.TileIndexAsync([first, second], index);
        await processing.SpatialIndexAsync(shapefile);

        Assert.Equal("all_parcels", mergeResult.Layers![0].Name);
        Assert.Equal(8, mergeResult.Layers[0].FeatureCount);
        var page = await vector.FeaturesAsync(merged, limit: 1, geometryFormat: GeometryFormat.None);
        Assert.Equal(Path.GetFileNameWithoutExtension(first), page.Features[0].Properties!["source"]);
        Assert.Equal(2, indexResult.Output!.Layers![0].FeatureCount);
        Assert.True(File.Exists(Path.ChangeExtension(shapefile, ".qix")));
    }

    [Fact]
    public async Task ReadOnlyServerRejectsVectorWrites()
    {
        var path = await fixture.CreateParcelsAsync();
        var readOnly = fixture.CreateContext(options => options.ReadOnly = true);

        await Assert.ThrowsAsync<McpException>(() => new VectorEditTools(readOnly).FeaturesDeleteAsync(path, fids: [1]));
        await Assert.ThrowsAsync<McpException>(() => new VectorProcessingTools(readOnly).BufferAsync(path, fixture.NewPath(".gpkg"), 5));
        var inMemory = await new VectorProcessingTools(readOnly).BufferAsync(path, $"/vsimem/{Guid.NewGuid():N}.gpkg", 5);
        Assert.Equal(4, inMemory.Layers![0].FeatureCount);
        Assert.Equal(4, (await new VectorTools(readOnly).FeatureCountAsync(path)).FeatureCount);
    }
}
