namespace InFarm.Mcp.Gdal.Tools;

// Tools that create vector datasets and edit their layers, schemas and features in place.
[McpServerToolType]
public sealed class VectorEditTools(GdalToolContext context)
{
    private const int AlterName = 1;
    private const int AlterType = 2;
    private const int AlterWidthPrecision = 4;
    private const int AlterNullable = 8;
    private const int AlterDefault = 16;
    private const int AlterUnique = 32;
    private const int AlterAlternativeName = 128;
    private const int AlterComment = 256;
    private const int AlterGeometryName = 0x1000;
    private const int AlterGeometryType = 0x2000;
    private const int AlterGeometryNullable = 0x4000;
    private const int AlterGeometrySrs = 0x8000;

    [McpServerTool(Name = "gdal_vector_create", Title = "Create a vector dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a new vector dataset with one empty layer that has a geometry type, a coordinate reference system and attribute fields. " +
        "Add features with gdal_vector_features_write and further layers with gdal_vector_layer_create.")]
    public Task<ProcessingResult> CreateAsync(
        [Description("Output dataset path, for example parcels.gpkg, points.geojson or roads.shp.")] string output,
        [Description("Name of the layer. The file name is used when omitted.")] string? layerName = null,
        [Description("Geometry type: None, Unknown, Point, LineString, Polygon, MultiPoint, MultiLineString, MultiPolygon or GeometryCollection, optionally followed by Z, M or ZM.")] string geometryType = "Unknown",
        [Description("Coordinate reference system, for example EPSG:4326.")] string? crs = null,
        [Description("Attribute fields of the layer.")] FieldDefinition[]? fields = null,
        [Description("Output driver short name. The driver is taken from the file extension when omitted.")] string? format = null,
        [Description("Dataset creation options in NAME=VALUE form.")] string[]? datasetCreationOptions = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_create", cancellationToken, operation =>
        {
            var resolved = context.Paths.Output(output, overwrite);
            return Processing.Guard(operation, resolved, () =>
            {
                using var dataSource = Processing.CreateDataSource(resolved, format, datasetCreationOptions);
                using var layer = CreateLayer(dataSource, Processing.LayerNameFor(resolved, layerName), geometryType, crs, fields, layerCreationOptions);
                dataSource.FlushCache();
            });
        });
    }

    [McpServerTool(Name = "gdal_vector_layer_create", Title = "Add a layer to a vector dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Adds an empty layer to an existing vector dataset whose format supports several layers, such as GeoPackage, PostGIS or a directory of shapefiles.")]
    public Task<ProcessingResult> LayerCreateAsync(
        [Description("Existing vector dataset path.")] string path,
        [Description("Name of the new layer.")] string layerName,
        [Description("Geometry type: None, Unknown, Point, LineString, Polygon, MultiPoint, MultiLineString, MultiPolygon or GeometryCollection, optionally followed by Z, M or ZM.")] string geometryType = "Unknown",
        [Description("Coordinate reference system, for example EPSG:4326.")] string? crs = null,
        [Description("Attribute fields of the layer.")] FieldDefinition[]? fields = null,
        [Description("Layer creation options in NAME=VALUE form. Pass OVERWRITE=YES to replace an existing layer of the same name.")] string[]? layerCreationOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_layer_create", cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            using (var dataSource = Open.DataSource(resolved, true))
            {
                using var layer = CreateLayer(dataSource, layerName, geometryType, crs, fields, layerCreationOptions);
                dataSource.FlushCache();
            }

            return Describe.Output(resolved, operation, $"The layer '{layerName}' was created.");
        });
    }

    [McpServerTool(Name = "gdal_vector_layer_delete", Title = "Delete a layer from a vector dataset", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description("Deletes a layer and all of its features from a vector dataset.")]
    public Task<ProcessingResult> LayerDeleteAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Name of the layer to delete.")] string layerName,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_layer_delete", cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            using (var dataSource = Open.DataSource(resolved, true))
            {
                var index = -1;
                for (var i = 0; i < dataSource.GetLayerCount() && index < 0; i++)
                {
                    using var layer = dataSource.GetLayerByIndex(i);
                    index = layer.GetName().Equals(layerName, StringComparison.Ordinal) ? i : -1;
                }

                if (index < 0)
                {
                    throw new McpException($"The layer '{layerName}' does not exist.");
                }

                dataSource.DeleteLayer(index);
                dataSource.FlushCache();
            }

            return Describe.Output(resolved, operation, $"The layer '{layerName}' was deleted.");
        });
    }

    [McpServerTool(Name = "gdal_vector_layer_rename", Title = "Rename a layer", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Renames a layer of a vector dataset, in formats that support renaming such as GeoPackage.")]
    public Task<ProcessingResult> LayerRenameAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Current name of the layer.")] string layerName,
        [Description("New name of the layer.")] string newName,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_layer_rename", cancellationToken, operation =>
        {
            var resolved = context.Paths.Update(path);
            using (var dataSource = Open.DataSource(resolved, true))
            {
                using var layer = VectorSupport.GetLayer(dataSource, layerName);
                layer.Rename(newName);
                dataSource.FlushCache();
            }

            return Describe.Output(resolved, operation, $"The layer '{layerName}' was renamed to '{newName}'.");
        });
    }

    [McpServerTool(Name = "gdal_vector_field_create", Title = "Add an attribute field to a layer", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Adds an attribute field to a vector layer. Existing features receive a null value, or the default value where the format applies defaults.")]
    public Task<LayerInfo> FieldCreateAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Definition of the new field.")] FieldDefinition field,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_field_create", cancellationToken, _ =>
        {
            using var dataSource = Open.DataSource(context.Paths.Update(path), true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            using var definition = VectorSupport.BuildField(field);
            vectorLayer.CreateField(definition, 1);
            dataSource.FlushCache();
            return VectorSupport.DescribeLayer(vectorLayer, true, false, false);
        });
    }

    [McpServerTool(Name = "gdal_vector_field_delete", Title = "Delete an attribute field from a layer", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description("Deletes an attribute field and its values from a vector layer.")]
    public Task<LayerInfo> FieldDeleteAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Name of the field to delete.")] string fieldName,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_field_delete", cancellationToken, _ =>
        {
            using var dataSource = Open.DataSource(context.Paths.Update(path), true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            vectorLayer.DeleteField(FieldIndex(vectorLayer, fieldName));
            dataSource.FlushCache();
            return VectorSupport.DescribeLayer(vectorLayer, true, false, false);
        });
    }

    [McpServerTool(Name = "gdal_vector_field_alter", Title = "Rename or change an attribute field", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(
        "Changes the name, type, width, precision, nullability, uniqueness, default value, alternative name or comment of an attribute field. " +
        "Only the properties that are passed are changed. The format decides which changes it supports.")]
    public Task<LayerInfo> FieldAlterAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Current name of the field.")] string fieldName,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("New name of the field.")] string? newName = null,
        [Description("New field type: Integer, Integer64, Real, String, Date, Time, DateTime or Binary.")] string? type = null,
        [Description("New field subtype: None, Boolean, Int16, Float32, JSON or UUID.")] string? subType = null,
        [Description("New width.")] int? width = null,
        [Description("New precision.")] int? precision = null,
        [Description("Whether the field accepts null values.")] bool? nullable = null,
        [Description("Whether the values of the field must be unique.")] bool? unique = null,
        [Description("New default value expression.")] string? defaultValue = null,
        [Description("New alternative name (alias).")] string? alternativeName = null,
        [Description("New comment.")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_field_alter", cancellationToken, _ =>
        {
            using var dataSource = Open.DataSource(context.Paths.Update(path), true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            var index = FieldIndex(vectorLayer, fieldName);
            using var current = vectorLayer.GetLayerDefn().GetFieldDefn(index);
            using var definition = VectorSupport.BuildField(new FieldDefinition
            {
                Name = newName ?? fieldName,
                Type = type ?? current.GetFieldTypeName(current.GetFieldType()),
                SubType = subType,
                Width = width ?? current.GetWidth(),
                Precision = precision ?? current.GetPrecision(),
                Nullable = nullable,
                Unique = unique,
                Default = defaultValue,
                AlternativeName = alternativeName,
                Comment = comment
            });
            var flags = (newName is null ? 0 : AlterName)
                | (type is null && subType is null ? 0 : AlterType)
                | (width is null && precision is null ? 0 : AlterWidthPrecision)
                | (nullable is null ? 0 : AlterNullable)
                | (defaultValue is null ? 0 : AlterDefault)
                | (unique is null ? 0 : AlterUnique)
                | (alternativeName is null ? 0 : AlterAlternativeName)
                | (comment is null ? 0 : AlterComment);
            if (flags == 0)
            {
                throw new McpException("Pass at least one property to change.");
            }

            vectorLayer.AlterFieldDefn(index, definition, flags);
            dataSource.FlushCache();
            return VectorSupport.DescribeLayer(vectorLayer, true, false, false);
        });
    }

    [McpServerTool(Name = "gdal_vector_geometry_field", Title = "Add or change a geometry field", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description(
        "Adds a geometry field to a layer, or changes the name, geometry type, nullability or assigned coordinate reference system of an existing geometry field. " +
        "Assigning a coordinate reference system does not reproject the coordinates. Use gdal_vector_reproject for that.")]
    public Task<LayerInfo> GeometryFieldAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Action to perform: create or alter.")] string action,
        [Description("Name of the geometry field. For alter, the first geometry field is used when omitted.")] string? fieldName = null,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Geometry type, for example Point, Polygon or MultiPolygon.")] string? geometryType = null,
        [Description("Coordinate reference system to assign.")] string? crs = null,
        [Description("Whether the field accepts null geometries.")] bool? nullable = null,
        [Description("New name of the field, for alter.")] string? newName = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_geometry_field", cancellationToken, _ =>
        {
            using var dataSource = Open.DataSource(context.Paths.Update(path), true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            using var srs = crs is null ? null : Crs.Parse(crs);
            var type = geometryType is null ? wkbGeometryType.wkbUnknown : Geometries.ParseType(geometryType);
            switch (action.ToLowerInvariant())
            {
                case "create":
                {
                    using var definition = new GeomFieldDefn(fieldName ?? throw new McpException("A field name is required to create a geometry field."), type);
                    if (srs is not null)
                    {
                        definition.SetSpatialRef(srs);
                    }

                    if (nullable is not null)
                    {
                        definition.SetNullable(nullable.Value ? 1 : 0);
                    }

                    vectorLayer.CreateGeomField(definition, 1);
                    break;
                }
                case "alter":
                {
                    var layerDefinition = vectorLayer.GetLayerDefn();
                    var index = fieldName is null ? 0 : layerDefinition.GetGeomFieldIndex(fieldName);
                    if (index < 0 || index >= layerDefinition.GetGeomFieldCount())
                    {
                        throw new McpException($"The geometry field '{fieldName}' does not exist.");
                    }

                    using var current = layerDefinition.GetGeomFieldDefn(index);
                    using var definition = new GeomFieldDefn(newName ?? current.GetName(), geometryType is null ? current.GetFieldType() : type);
                    if (srs is not null)
                    {
                        definition.SetSpatialRef(srs);
                    }

                    definition.SetNullable(nullable ?? current.IsNullable() != 0 ? 1 : 0);
                    var flags = (newName is null ? 0 : AlterGeometryName)
                        | (geometryType is null ? 0 : AlterGeometryType)
                        | (nullable is null ? 0 : AlterGeometryNullable)
                        | (crs is null ? 0 : AlterGeometrySrs);
                    if (flags == 0)
                    {
                        throw new McpException("Pass at least one property to change.");
                    }

                    vectorLayer.AlterGeomFieldDefn(index, definition, flags);
                    break;
                }
                default:
                    throw new McpException("The action must be create or alter.");
            }

            dataSource.FlushCache();
            return VectorSupport.DescribeLayer(vectorLayer, true, false, false);
        });
    }

    [McpServerTool(Name = "gdal_vector_features_write", Title = "Insert or update features", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Inserts new features into a vector layer, updates existing features by feature identifier, or upserts them. The write runs in one transaction where the format supports it, " +
        "so a failure leaves the layer unchanged. An update changes only the properties and the geometry that are passed. Use gdal_vector_convert to load large datasets.")]
    public Task<FeatureWriteResult> FeaturesWriteAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Features to write.")] FeatureInput[] features,
        [Description("Write mode: insert, update or upsert.")] string mode = "insert",
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Coordinate reference system of the geometries when it differs from the layer. With a geographic system x is the longitude and y is the latitude.")] string? geometryCrs = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_features_write", cancellationToken, operation =>
        {
            if (mode is not ("insert" or "update" or "upsert"))
            {
                throw new McpException("The mode must be insert, update or upsert.");
            }

            if (features.Length > context.Options.MaxFeatureCount)
            {
                throw new McpException(
                    $"At most {context.Options.MaxFeatureCount} features can be written in one call. Split the features over several calls, or load a file with gdal_vector_convert.");
            }

            var resolved = context.Paths.Update(path);
            using var dataSource = Open.DataSource(resolved, true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            var definition = vectorLayer.GetLayerDefn();
            using var transformation = geometryCrs is null ? null : TransformationToLayer(vectorLayer, geometryCrs);
            var fids = new List<long>();
            var inserted = 0;
            var updated = 0;
            var transactional = dataSource.TestCapability("Transactions");
            if (transactional)
            {
                dataSource.StartTransaction(0);
            }

            try
            {
                foreach (var input in features)
                {
                    operation.ThrowIfCancelled();
                    var isUpdate = mode == "update";
                    if (isUpdate && input.Fid is null)
                    {
                        throw new McpException("Every feature of an update needs a fid.");
                    }

                    using var feature = isUpdate
                        ? vectorLayer.GetFeature(input.Fid!.Value) ?? throw new McpException($"The feature with fid {input.Fid} does not exist.")
                        : new Feature(definition);
                    if (!isUpdate && input.Fid is not null)
                    {
                        feature.SetFID(input.Fid.Value);
                    }

                    foreach (var (name, value) in input.Properties ?? [])
                    {
                        var index = definition.GetFieldIndex(name);
                        if (index < 0)
                        {
                            throw new McpException(
                                $"The field '{name}' does not exist. Fields: {string.Join(", ", VectorSupport.DescribeFields(definition).Select(field => field.Name))}.");
                        }

                        using var fieldDefinition = definition.GetFieldDefn(index);
                        FieldValues.Set(feature, index, fieldDefinition, value);
                    }

                    if (input.Geometry is not null)
                    {
                        using var geometry = input.Geometry.Trim().Equals("null", StringComparison.OrdinalIgnoreCase) ? null : Geometries.Parse(input.Geometry);
                        if (geometry is not null && transformation is not null)
                        {
                            geometry.Transform(transformation);
                        }

                        feature.SetGeometry(geometry);
                    }

                    switch (mode)
                    {
                        case "insert":
                            vectorLayer.CreateFeature(feature);
                            inserted++;
                            break;
                        case "update":
                            vectorLayer.SetFeature(feature);
                            updated++;
                            break;
                        default:
                            vectorLayer.UpsertFeature(feature);
                            inserted++;
                            break;
                    }

                    fids.Add(feature.GetFID());
                }

                if (transactional)
                {
                    dataSource.CommitTransaction();
                }
            }
            catch
            {
                if (transactional)
                {
                    dataSource.RollbackTransaction();
                }

                throw;
            }

            vectorLayer.SyncToDisk();
            return new FeatureWriteResult
            {
                Path = resolved,
                Layer = vectorLayer.GetName(),
                Inserted = inserted,
                Updated = updated,
                Fids = fids,
                FeatureCount = vectorLayer.GetFeatureCount(1),
                Warnings = operation.WarningsOrNull
            };
        });
    }

    [McpServerTool(Name = "gdal_vector_features_delete", Title = "Delete features", ReadOnly = false, Destructive = true, UseStructuredContent = true)]
    [Description("Deletes features from a vector layer, selected by feature identifiers or by an attribute filter. Exactly one of fids and where is required.")]
    public Task<FeatureWriteResult> FeaturesDeleteAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Identifiers of the features to delete.")] long[]? fids = null,
        [Description("Attribute filter in OGR SQL WHERE syntax that selects the features to delete.")] string? where = null,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_features_delete", cancellationToken, operation =>
        {
            if (fids is null == where is null)
            {
                throw new McpException("Pass exactly one of fids and where.");
            }

            var resolved = context.Paths.Update(path);
            using var dataSource = Open.DataSource(resolved, true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            var targets = fids?.ToList() ?? [];
            if (where is not null)
            {
                VectorSupport.ApplyFilters(vectorLayer, where, null, null, null);
                vectorLayer.ResetReading();
                while (vectorLayer.GetNextFeature() is { } feature)
                {
                    using (feature)
                    {
                        targets.Add(feature.GetFID());
                    }
                }

                vectorLayer.SetAttributeFilter(null);
            }

            var transactional = dataSource.TestCapability("Transactions");
            if (transactional)
            {
                dataSource.StartTransaction(0);
            }

            try
            {
                foreach (var fid in targets)
                {
                    operation.ThrowIfCancelled();
                    vectorLayer.DeleteFeature(fid);
                }

                if (transactional)
                {
                    dataSource.CommitTransaction();
                }
            }
            catch
            {
                if (transactional)
                {
                    dataSource.RollbackTransaction();
                }

                throw;
            }

            vectorLayer.SyncToDisk();
            return new FeatureWriteResult
            {
                Path = resolved,
                Layer = vectorLayer.GetName(),
                Deleted = targets.Count,
                FeatureCount = vectorLayer.GetFeatureCount(1),
                Warnings = operation.WarningsOrNull
            };
        });
    }

    private static Layer CreateLayer(
        DataSource dataSource, string name, string geometryType, string? crs, FieldDefinition[]? fields, string[]? layerCreationOptions)
    {
        using var srs = crs is null ? null : Crs.Parse(crs);
        var layer = dataSource.CreateLayer(name, srs, Geometries.ParseType(geometryType), layerCreationOptions ?? [])
            ?? throw new McpException($"The layer '{name}' could not be created.");
        foreach (var field in fields ?? [])
        {
            using var definition = VectorSupport.BuildField(field);
            layer.CreateField(definition, 1);
        }

        return layer;
    }

    private static int FieldIndex(Layer layer, string fieldName)
    {
        var definition = layer.GetLayerDefn();
        var index = definition.GetFieldIndex(fieldName);
        if (index < 0)
        {
            throw new McpException(
                $"The field '{fieldName}' does not exist. Fields: {string.Join(", ", VectorSupport.DescribeFields(definition).Select(field => field.Name))}.");
        }

        return index;
    }

    private static CoordinateTransformation TransformationToLayer(Layer layer, string crs)
    {
        using var source = Crs.Parse(crs);
        using var layerSrs = layer.GetSpatialRef()
            ?? throw new McpException("The layer has no coordinate reference system, so geometries in another coordinate reference system cannot be converted.");
        using var target = layerSrs.Clone();
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        return new CoordinateTransformation(source, target);
    }
}
