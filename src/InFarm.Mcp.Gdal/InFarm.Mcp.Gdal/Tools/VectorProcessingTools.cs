using System.Text.Json;

namespace InFarm.Mcp.Gdal.Tools;

// Vector conversion and geoprocessing, built on the native ogr2ogr utility and the OGR and GEOS geometry functions.
[McpServerToolType]
public sealed class VectorProcessingTools(GdalToolContext context)
{
    private const int FeaturesPerTransaction = 20_000;

    [McpServerTool(Name = "gdal_vector_convert", Title = "Convert, filter and reproject vector data (ogr2ogr)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Converts vector data between formats with the native ogr2ogr utility, with optional layer selection, attribute and spatial filters, SQL, reprojection, " +
        "field selection, geometry type conversion, clipping, simplification and repair. The mode selects whether the output is created, appended to, or updated. " +
        "The features are streamed, so datasets that are larger than memory are supported.")]
    public Task<ProcessingResult> ConvertAsync(
        VectorConvertRequest request,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_convert", progress, cancellationToken, operation =>
        {
            return Convert(request, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_reproject", Title = "Reproject a vector dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Reprojects the layers of a vector dataset to another coordinate reference system and writes them to a new dataset.")]
    public Task<ProcessingResult> ReprojectAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Target coordinate reference system, for example EPSG:3857.")] string targetCrs,
        [Description("Overrides the coordinate reference system of the input.")] string? sourceCrs = null,
        [Description("Names of the layers to reproject. Every layer is reprojected when omitted.")] string[]? layers = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_reproject", progress, cancellationToken, operation =>
        {
            return Convert(
                new VectorConvertRequest
                {
                    Input = input,
                    Output = output,
                    Overwrite = overwrite,
                    Format = format,
                    Layers = layers,
                    TargetCrs = targetCrs,
                    SourceCrs = sourceCrs,
                    LayerCreationOptions = layerCreationOptions
                },
                operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_filter", Title = "Extract features by attribute or location", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description("Writes the features that match an attribute filter, a bounding box or both to a new dataset, optionally keeping only selected fields.")]
    public Task<ProcessingResult> FilterAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Attribute filter in OGR SQL WHERE syntax, for example \"landuse = 'forest' AND area > 5000\".")] string? where = null,
        [Description("Spatial filter as [minX, minY, maxX, maxY]. Features that intersect the box are kept whole.")] double[]? bounds = null,
        [Description("Coordinate reference system of bounds when it differs from the layer.")] string? boundsCrs = null,
        [Description("Fields to keep. Every field is kept when omitted.")] string[]? fields = null,
        [Description("Names of the layers to filter. Every layer is filtered when omitted.")] string[]? layers = null,
        [Description("Maximum number of features to write for each layer.")] int? limit = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_filter", progress, cancellationToken, operation =>
        {
            if (where is null && bounds is null && fields is null && limit is null)
            {
                throw new McpException("Pass where, bounds, fields or limit to define the selection.");
            }

            return Convert(
                new VectorConvertRequest
                {
                    Input = input,
                    Output = output,
                    Overwrite = overwrite,
                    Format = format,
                    Layers = layers,
                    Where = where,
                    SpatialFilter = bounds,
                    SpatialFilterCrs = boundsCrs,
                    SelectFields = fields,
                    Limit = limit
                },
                operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_clip", Title = "Clip a vector dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Clips the geometries of a vector dataset to a bounding box, to a polygon given as WKT, or to the polygons of another vector dataset. " +
        "Geometries that cross the clip boundary are cut. Pass exactly one of bounds, clipGeometry or clipDataset.")]
    public Task<ProcessingResult> ClipAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Clip box as [minX, minY, maxX, maxY] in the coordinate reference system of the input.")] double[]? bounds = null,
        [Description("Clip polygon as WKT in the coordinate reference system of the input.")] string? clipGeometry = null,
        [Description("Vector dataset whose polygons are the clip area.")] string? clipDataset = null,
        [Description("Layer of the clip dataset.")] string? clipLayer = null,
        [Description("Attribute filter that selects the clip features.")] string? clipWhere = null,
        [Description("Names of the layers to clip. Every layer is clipped when omitted.")] string[]? layers = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_clip", progress, cancellationToken, operation =>
        {
            var selections = (bounds is null ? 0 : 1) + (clipGeometry is null ? 0 : 1) + (clipDataset is null ? 0 : 1);
            if (selections != 1)
            {
                throw new McpException("Pass exactly one of bounds, clipGeometry or clipDataset.");
            }

            if (bounds is not null && bounds.Length != 4)
            {
                throw new McpException("The parameter 'bounds' needs exactly four numbers: minX, minY, maxX and maxY.");
            }

            var additional = new ArgList().Option("-clipsrclayer", clipLayer).Option("-clipsrcwhere", clipWhere);
            return Convert(
                new VectorConvertRequest
                {
                    Input = input,
                    Output = output,
                    Overwrite = overwrite,
                    Format = format,
                    Layers = layers,
                    ClipSource = bounds is not null ? string.Join(' ', bounds.Select(Num.Text)) : clipGeometry ?? clipDataset,
                    AdditionalArguments = additional.ToArray()
                },
                operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_geometry_process", Title = "Apply a geometry operation to every feature", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Applies one geometry operation to every feature of a layer and writes the results, with the attributes, to a new dataset. The operations are " +
        GeometryOps.UnaryNames + ". Buffer and segmentize need distance. Simplify, simplify_preserve_topology and set_precision need tolerance. " +
        "Force_type needs targetType. Distances and tolerances are in the units of the coordinate reference system of the layer, so reproject geographic data to a projected system first.")]
    public Task<ProcessingResult> GeometryProcessAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Geometry operation to apply.")] string operation,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter that selects the features to process.")] string? where = null,
        [Description("Distance for buffer and segmentize, in layer units. A negative buffer distance shrinks polygons.")] double? distance = null,
        [Description("Tolerance for simplify, simplify_preserve_topology, set_precision and delaunay, in layer units.")] double? tolerance = null,
        [Description("Ratio between 0 and 1 for concave_hull. Smaller values give a tighter hull.")] double? ratio = null,
        [Description("Allows holes in a concave hull.")] bool allowHoles = false,
        [Description("Target geometry type for force_type, for example MultiPolygon or LineString.")] string? targetType = null,
        [Description("Options of the operation in NAME=VALUE form, for example QUADRANT_SEGMENTS=16, ENDCAP_STYLE=FLAT or JOIN_STYLE=MITRE for buffer, and METHOD=STRUCTURE for make_valid.")] string[]? options = null,
        [Description("Name of the output layer.")] string? outputLayerName = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_geometry_process", progress, cancellationToken, gdalOperation =>
        {
            var parameters = new GeometryOpParameters
            {
                Distance = distance,
                Tolerance = tolerance,
                Ratio = ratio,
                AllowHoles = allowHoles,
                TargetType = targetType,
                Options = options
            };
            return ProcessGeometries(input, output, operation, layer, where, parameters, outputLayerName, format, layerCreationOptions, overwrite, gdalOperation);
        });
    }

    [McpServerTool(Name = "gdal_vector_buffer", Title = "Buffer the features of a layer", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a buffer polygon around every feature of a layer. The distance is in the units of the coordinate reference system of the layer, " +
        "so reproject geographic data to a projected system first to buffer in metres.")]
    public Task<ProcessingResult> BufferAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Buffer distance in layer units. A negative distance shrinks polygons.")] double distance,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter that selects the features to buffer.")] string? where = null,
        [Description("Number of segments that approximate a quarter circle.")] int quadrantSegments = 8,
        [Description("End cap style of line buffers: ROUND, FLAT or SQUARE.")] string? endCapStyle = null,
        [Description("Join style at corners: ROUND, MITRE or BEVEL.")] string? joinStyle = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_buffer", progress, cancellationToken, operation =>
        {
            var options = new List<string> { $"QUADRANT_SEGMENTS={quadrantSegments}" };
            if (endCapStyle is not null)
            {
                options.Add($"ENDCAP_STYLE={endCapStyle.ToUpperInvariant()}");
            }

            if (joinStyle is not null)
            {
                options.Add($"JOIN_STYLE={joinStyle.ToUpperInvariant()}");
            }

            return ProcessGeometries(
                input, output, "buffer", layer, where, new GeometryOpParameters { Distance = distance, Options = options }, null, format, null, overwrite, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_validate", Title = "Find invalid geometries", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Checks every geometry of a layer for validity with GEOS and reports the invalid features with the reason, for example a self-intersection or an unclosed ring. " +
        "Repair the reported features with gdal_vector_repair.")]
    public Task<ValidationResult> ValidateAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter that selects the features to check.")] string? where = null,
        [Description("Maximum number of invalid features to list.")] int? limit = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_validate", progress, cancellationToken, operation =>
        {
            using var dataset = Open.Vector(context.Paths.Input(path));
            using var vectorLayer = VectorSupport.GetLayer(dataset, layer);
            VectorSupport.ApplyFilters(vectorLayer, where, null, null, null);
            var maximum = context.ClampLimit(limit);
            var invalid = new List<InvalidFeature>();
            long checkedCount = 0;
            long invalidCount = 0;
            long emptyCount = 0;
            var total = Math.Max(1, vectorLayer.GetFeatureCount(0));
            vectorLayer.ResetReading();
            while (vectorLayer.GetNextFeature() is { } feature)
            {
                using (feature)
                {
                    operation.ThrowIfCancelled();
                    checkedCount++;
                    var geometry = feature.GetGeometryRef();
                    if (geometry is null || geometry.IsEmpty())
                    {
                        emptyCount++;
                    }
                    else if (!geometry.IsValid())
                    {
                        invalidCount++;
                        if (invalid.Count < maximum)
                        {
                            invalid.Add(new InvalidFeature
                            {
                                Fid = feature.GetFID(),
                                Problem = geometry.GetInvalidityReason() ?? "Invalid geometry",
                                GeometryType = Geometries.TypeName(geometry.GetGeometryType())
                            });
                        }
                    }

                    if (checkedCount % 1000 == 0)
                    {
                        operation.Progress(Math.Min(1, checkedCount / (double)total), IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }

            // GEOS reports each invalid geometry as a warning, which would repeat the structured result.
            operation.Warnings.Clear();
            return new ValidationResult
            {
                Layer = vectorLayer.GetName(),
                Checked = checkedCount,
                InvalidCount = invalidCount,
                EmptyOrNullCount = emptyCount,
                Invalid = invalid
            };
        });
    }

    [McpServerTool(Name = "gdal_vector_repair", Title = "Repair invalid geometries", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Repairs the invalid geometries of a layer with the GEOS MakeValid algorithm and writes every feature to a new dataset. " +
        "The linework method keeps every vertex, and the structure method keeps the intended area of polygons.")]
    public Task<ProcessingResult> RepairAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Repair method: linework or structure.")] string method = "linework",
        [Description("Keeps parts that collapse to a lower dimension, with the structure method.")] bool keepCollapsed = false,
        [Description("Output driver short name.")] string? format = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_repair", progress, cancellationToken, operation =>
        {
            string[] options = [$"METHOD={method.ToUpperInvariant()}", $"KEEP_COLLAPSED={(keepCollapsed ? "YES" : "NO")}"];
            return ProcessGeometries(
                input, output, "make_valid", layer, null, new GeometryOpParameters { Options = options }, null, format, null, overwrite, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_overlay", Title = "Overlay two vector layers", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Combines an input layer with a method layer using the native OGR layer algebra and writes the result to a new dataset. The operations are " +
        "intersection (areas in both layers), union (areas in either layer), difference (input areas outside the method layer, also called erase), " +
        "sym_difference (areas in exactly one layer), identity (input features split by the method layer), update (method features replace the input where they overlap) " +
        "and clip (input features cut to the method layer, keeping only the input attributes). Both layers should share one coordinate reference system.")]
    public Task<ProcessingResult> OverlayAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Method (overlay) vector dataset path.")] string method,
        [Description("Output vector dataset path.")] string output,
        [Description("Overlay operation: intersection, union, difference, sym_difference, identity, update or clip.")] string operation,
        [Description("Layer of the input dataset. It can be omitted when the dataset has a single layer.")] string? inputLayer = null,
        [Description("Layer of the method dataset. It can be omitted when the dataset has a single layer.")] string? methodLayer = null,
        [Description("Name of the output layer.")] string? outputLayerName = null,
        [Description("Layer algebra options in NAME=VALUE form, for example SKIP_FAILURES=YES, PROMOTE_TO_MULTI=YES, INPUT_PREFIX=a_, METHOD_PREFIX=b_ or KEEP_LOWER_DIMENSION_GEOMETRIES=NO.")] string[]? options = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_overlay", progress, cancellationToken, gdalOperation =>
        {
            using var inputDataset = Open.Vector(context.Paths.Input(input));
            using var methodDataset = Open.Vector(context.Paths.Input(method));
            using var source = VectorSupport.GetLayer(inputDataset, inputLayer);
            using var overlay = VectorSupport.GetLayer(methodDataset, methodLayer);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            return Processing.Guard(gdalOperation, resolvedOutput, () =>
            {
                using var destination = Processing.CreateDataSource(resolvedOutput, format, null);
                using var srs = source.GetSpatialRef();
                using var result = destination.CreateLayer(
                    Processing.LayerNameFor(resolvedOutput, outputLayerName), srs, wkbGeometryType.wkbUnknown, layerCreationOptions ?? []);
                var algebraOptions = options ?? [];
                switch (operation.ToLowerInvariant())
                {
                    case "intersection":
                        source.Intersection(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "union":
                        source.Union(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "difference" or "erase":
                        source.Erase(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "sym_difference" or "symmetric_difference":
                        source.SymDifference(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "identity":
                        source.Identity(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "update":
                        source.Update(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    case "clip":
                        source.Clip(overlay, result, algebraOptions, gdalOperation.OgrProgress, null);
                        break;
                    default:
                        throw new McpException("The operation must be intersection, union, difference, sym_difference, identity, update or clip.");
                }

                result.SyncToDisk();
            });
        });
    }

    [McpServerTool(Name = "gdal_vector_dissolve", Title = "Dissolve features by attribute", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Merges the geometries of the features that share the same values in the given fields into one feature for each group, using the GEOS union. " +
        "Every feature is merged into a single feature when no fields are given. The output keeps the grouping fields and adds a feature count.")]
    public Task<ProcessingResult> DissolveAsync(
        [Description("Input vector dataset path.")] string input,
        [Description("Output vector dataset path.")] string output,
        [Description("Fields whose values define the groups. Every feature is merged together when omitted.")] string[]? byFields = null,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter that selects the features to dissolve.")] string? where = null,
        [Description("Name of the field that receives the number of merged features.")] string countField = "feature_count",
        [Description("Name of the output layer.")] string? outputLayerName = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_dissolve", progress, cancellationToken, operation =>
        {
            using var dataSource = Open.DataSource(context.Paths.Input(input));
            using var sourceLayer = VectorSupport.GetLayer(dataSource, layer);
            var sourceDefinition = sourceLayer.GetLayerDefn();
            var groupFields = byFields ?? [];
            foreach (var field in groupFields)
            {
                if (sourceDefinition.GetFieldIndex(field) < 0)
                {
                    throw new McpException($"The field '{field}' does not exist in the layer '{sourceLayer.GetName()}'.");
                }
            }

            // Ordering the features by the grouping fields keeps only one group of geometries in memory at a time.
            var statement = $"SELECT * FROM \"{sourceLayer.GetName().Replace("\"", "\"\"")}\""
                + (where is null ? string.Empty : $" WHERE {where}")
                + (groupFields.Length == 0 ? string.Empty : $" ORDER BY {string.Join(", ", groupFields.Select(field => $"\"{field}\""))}");
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var total = Math.Max(1, sourceLayer.GetFeatureCount(0));
            var ordered = dataSource.ExecuteSQL(statement, null, "OGRSQL")
                ?? throw new McpException("The features could not be ordered for dissolving.");
            try
            {
                return Processing.Guard(operation, resolvedOutput, () =>
                {
                    using var destination = Processing.CreateDataSource(resolvedOutput, format, null);
                    using var srs = sourceLayer.GetSpatialRef();
                    using var result = destination.CreateLayer(
                        Processing.LayerNameFor(resolvedOutput, outputLayerName), srs, wkbGeometryType.wkbUnknown, layerCreationOptions ?? []);
                    foreach (var field in groupFields)
                    {
                        using var definition = sourceDefinition.GetFieldDefn(sourceDefinition.GetFieldIndex(field));
                        result.CreateField(definition, 1);
                    }

                    using var countDefinition = new FieldDefn(countField, FieldType.OFTInteger64);
                    result.CreateField(countDefinition, 1);
                    var resultDefinition = result.GetLayerDefn();

                    string? currentKey = null;
                    Feature? representative = null;
                    var collection = new Geometry(wkbGeometryType.wkbGeometryCollection);
                    long members = 0;
                    long processed = 0;

                    void Flush()
                    {
                        if (representative is null)
                        {
                            return;
                        }

                        using var union = collection.UnaryUnion();
                        using var merged = new Feature(resultDefinition);
                        for (var i = 0; i < groupFields.Length; i++)
                        {
                            merged.SetField(i, representative.GetFieldAsString(groupFields[i]));
                            if (representative.IsFieldNull(groupFields[i]))
                            {
                                merged.SetFieldNull(i);
                            }
                        }

                        merged.SetFieldInteger64(groupFields.Length, members);
                        merged.SetGeometry(union);
                        result.CreateFeature(merged);
                        representative.Dispose();
                        representative = null;
                        collection.Dispose();
                        collection = new Geometry(wkbGeometryType.wkbGeometryCollection);
                        members = 0;
                    }

                    try
                    {
                        while (ordered.GetNextFeature() is { } feature)
                        {
                            operation.ThrowIfCancelled();
                            var key = string.Join('\u001f', groupFields.Select(field => feature.IsFieldNull(field) ? "\u0000" : feature.GetFieldAsString(field)));
                            if (representative is not null && key != currentKey)
                            {
                                Flush();
                            }

                            currentKey = key;
                            var geometry = feature.GetGeometryRef();
                            if (geometry is not null && !geometry.IsEmpty())
                            {
                                collection.AddGeometry(geometry);
                            }

                            members++;
                            if (representative is null)
                            {
                                representative = feature;
                            }
                            else
                            {
                                feature.Dispose();
                            }

                            if (++processed % 1000 == 0)
                            {
                                operation.Progress(Math.Min(1, processed / (double)total), IntPtr.Zero, IntPtr.Zero);
                            }
                        }

                        Flush();
                    }
                    finally
                    {
                        representative?.Dispose();
                        collection.Dispose();
                    }

                    result.SyncToDisk();
                });
            }
            finally
            {
                dataSource.ReleaseResultSet(ordered);
            }
        });
    }

    [McpServerTool(Name = "gdal_vector_merge", Title = "Merge vector datasets (ogrmerge)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Merges the layers of several vector datasets into one dataset, like ogrmerge. With singleLayer=true every feature goes into one layer, and fields that are " +
        "missing from some inputs are added. Otherwise each input layer becomes its own output layer, named after its dataset and layer.")]
    public Task<ProcessingResult> MergeAsync(
        [Description("Input vector dataset paths.")] string[] inputs,
        [Description("Output vector dataset path.")] string output,
        [Description("Writes every input layer into a single output layer.")] bool singleLayer = true,
        [Description("Name of the output layer when singleLayer is true.")] string? layerName = null,
        [Description("Name of a field that receives the name of the source dataset of each feature.")] string? sourceField = null,
        [Description("Reprojects every input to this coordinate reference system.")] string? targetCrs = null,
        [Description("Output geometry type, for example PROMOTE_TO_MULTI or MULTIPOLYGON.")] string? geometryType = null,
        [Description("Output driver short name.")] string? format = null,
        [Description("Layer creation options in NAME=VALUE form.")] string[]? layerCreationOptions = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_merge", progress, cancellationToken, operation =>
        {
            if (inputs.Length == 0)
            {
                throw new McpException("At least one input dataset is required.");
            }

            var resolvedInputs = context.Paths.Inputs(inputs);
            var resolvedOutput = context.Paths.Output(output, overwrite);
            var mergedName = Processing.LayerNameFor(resolvedOutput, layerName);
            var created = false;
            try
            {
                foreach (var input in resolvedInputs)
                {
                    using var source = Open.Vector(input);
                    var datasetName = Path.GetFileNameWithoutExtension(input.TrimEnd('/', '\\'));
                    for (var i = 0; i < source.GetLayerCount(); i++)
                    {
                        operation.ThrowIfCancelled();
                        string sourceLayerName;
                        using (var sourceLayer = source.GetLayer(i))
                        {
                            sourceLayerName = sourceLayer.GetName();
                        }

                        var arguments = new ArgList()
                            .Option("-f", created ? null : format)
                            .Add("-nln", singleLayer ? mergedName : $"{datasetName}_{sourceLayerName}")
                            .Option("-t_srs", targetCrs)
                            .Option("-nlt", geometryType)
                            .Each("-lco", created && singleLayer ? null : layerCreationOptions);
                        if (created)
                        {
                            arguments.Add(singleLayer ? "-append" : "-update").Flag(singleLayer, "-addfields");
                        }

                        if (sourceField is null)
                        {
                            arguments.Add(sourceLayerName);
                        }
                        else
                        {
                            var quotedLayer = sourceLayerName.Replace("\"", "\"\"");
                            arguments.Add("-sql", $"SELECT *, '{datasetName.Replace("'", "''")}' AS \"{sourceField}\" FROM \"{quotedLayer}\"");
                        }

                        using var options = new GDALVectorTranslateOptions(arguments.ToArray());
                        if (!created)
                        {
                            using var first = GdalApi.wrapper_GDALVectorTranslateDestName(resolvedOutput, source, options, null, null)
                                ?? throw new McpException($"The output '{output}' could not be created.");
                            first.FlushCache();
                            created = true;
                        }
                        else
                        {
                            using var destination = Open.Vector(resolvedOutput, true);
                            GdalApi.wrapper_GDALVectorTranslateDestDS(destination, source, options, null, null);
                            destination.FlushCache();
                        }
                    }
                }
            }
            catch
            {
                Processing.DeleteQuietly(resolvedOutput);
                throw;
            }

            return Describe.Output(resolvedOutput, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_tile_index", Title = "Build a vector tile index (ogrtindex)", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a vector dataset with one polygon for the extent of each layer of the input datasets and the dataset path as an attribute, " +
        "with the native 'gdal vector index' algorithm, the successor of ogrtindex.")]
    public Task<AlgorithmRunResult> TileIndexAsync(
        [Description("Input vector dataset paths.")] string[] inputs,
        [Description("Output vector dataset path.")] string output,
        [Description("Name of the field that receives the dataset path.")] string? locationField = null,
        [Description("Reprojects the extents to this coordinate reference system.")] string? targetCrs = null,
        [Description("Writes absolute dataset paths.")] bool absolutePaths = false,
        [Description("Output driver short name.")] string? format = null,
        [Description("Replaces the output when it already exists.")] bool overwrite = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_tile_index", progress, cancellationToken, operation =>
        {
            var arguments = new Dictionary<string, JsonElement>
            {
                ["input"] = JsonSerializer.SerializeToElement(inputs),
                ["output"] = JsonSerializer.SerializeToElement(output),
                ["overwrite"] = JsonSerializer.SerializeToElement(overwrite)
            };
            (string Name, object? Value)[] optional =
            [
                ("location-name", locationField),
                ("dst-crs", targetCrs),
                ("absolute-path", absolutePaths ? true : null),
                ("output-format", format)
            ];
            foreach (var (name, value) in optional)
            {
                if (value is not null)
                {
                    arguments[name] = JsonSerializer.SerializeToElement(value);
                }
            }

            return AlgorithmInvoker.Run(["vector", "index"], arguments, context, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_spatial_index", Title = "Create or drop a spatial index", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Creates or drops the spatial index of a layer in an ESRI Shapefile or a GeoPackage. FlatGeobuf builds its index when the file is written, " +
        "and database formats create indexes with SQL through gdal_vector_sql.")]
    public Task<FileOperationResult> SpatialIndexAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Action to perform: create or drop.")] string action = "create",
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_spatial_index", cancellationToken, operation =>
        {
            var create = action.ToLowerInvariant() switch
            {
                "create" => true,
                "drop" => false,
                _ => throw new McpException("The action must be create or drop.")
            };
            var resolved = context.Paths.Update(path);
            using var dataSource = Open.DataSource(resolved, true);
            using var vectorLayer = VectorSupport.GetLayer(dataSource, layer);
            using var driver = dataSource.GetDriver();
            var layerName = vectorLayer.GetName();
            var statement = driver.GetName() switch
            {
                "ESRI Shapefile" => $"{(create ? "CREATE" : "DROP")} SPATIAL INDEX ON \"{layerName}\"",
                "GPKG" => $"SELECT {(create ? "CreateSpatialIndex" : "DisableSpatialIndex")}('{layerName.Replace("'", "''")}', '{vectorLayer.GetGeometryColumn()}')",
                _ => throw new McpException(
                    $"The {driver.GetName()} driver has no spatial index command that this tool knows. ESRI Shapefile and GPKG are supported.")
            };
            var result = dataSource.ExecuteSQL(statement, null, string.Empty);
            if (result is not null)
            {
                dataSource.ReleaseResultSet(result);
            }

            return new FileOperationResult
            {
                Path = resolved,
                Action = create ? "spatial index created" : "spatial index dropped",
                Warnings = operation.WarningsOrNull
            };
        });
    }

    private ProcessingResult Convert(VectorConvertRequest request, GdalOperation operation)
    {
        var input = context.Paths.Input(request.Input);
        var mode = request.Mode.ToLowerInvariant();
        var modeFlag = mode switch
        {
            "create" => null,
            "append" => "-append",
            "overwrite_layer" => "-overwrite",
            "update" => "-update",
            "upsert" => "-upsert",
            _ => throw new McpException("The mode must be create, append, overwrite_layer, update or upsert.")
        };

        var arguments = new ArgList()
            .Option("-f", request.Format)
            .Option("-nln", request.OutputLayerName)
            .Option("-where", request.Where)
            .Option("-sql", request.Sql)
            .Option("-dialect", request.SqlDialect)
            .Numbers("-spat", request.SpatialFilter, 4, "spatialFilter")
            .Option("-spat_srs", request.SpatialFilterCrs)
            .Option("-t_srs", request.TargetCrs)
            .Option("-s_srs", request.SourceCrs)
            .Option("-a_srs", request.AssignCrs)
            .Option("-select", request.SelectFields is null ? null : string.Join(',', request.SelectFields))
            .Option("-nlt", request.GeometryType)
            .Option("-dim", request.Dimension)
            .Option("-simplify", request.Simplify)
            .Option("-segmentize", request.Segmentize)
            .Flag(request.MakeValid, "-makevalid")
            .Flag(request.ExplodeCollections, "-explodecollections")
            .Flag(request.SkipFailures, "-skipfailures")
            .Option("-limit", request.Limit)
            .Option("-gt", request.TransactionSize)
            .Flag(request.PreserveFid, "-preserve_fid")
            .Each("-lco", request.LayerCreationOptions)
            .Each("-dsco", request.DatasetCreationOptions);
        AddClip(arguments, "-clipsrc", request.ClipSource);
        AddClip(arguments, "-clipdst", request.ClipDestination);
        arguments.Extra(request.AdditionalArguments, context.Paths);

        var existing = modeFlag is not null && PathPolicy.Exists(request.Output);
        using var source = Open.Vector(input, false, request.OpenOptions);
        if (existing)
        {
            var destinationPath = context.Paths.Update(request.Output);
            arguments.Add(modeFlag!).Add(request.Layers ?? []);
            using var options = new GDALVectorTranslateOptions(arguments.ToArray());
            using (var destination = Open.Vector(destinationPath, true))
            {
                GdalApi.wrapper_GDALVectorTranslateDestDS(destination, source, options, operation.Progress, null);
                destination.FlushCache();
            }

            operation.ThrowIfCancelled();
            return Describe.Output(destinationPath, operation, $"The existing dataset was modified in '{mode}' mode.");
        }

        var output = context.Paths.Output(request.Output, request.Overwrite);
        arguments.Add(request.Layers ?? []);
        using var createOptions = new GDALVectorTranslateOptions(arguments.ToArray());
        return Processing.Produce(
            operation,
            output,
            () => GdalApi.wrapper_GDALVectorTranslateDestName(output, source, createOptions, operation.Progress, null));
    }

    // Adds a clip argument, whose value is a bounding box of four numbers, a WKT polygon or the path of a vector dataset.
    private void AddClip(ArgList arguments, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            arguments.Add(flag).Add(parts);
        }
        else if (value.Contains('(', StringComparison.Ordinal))
        {
            arguments.Add(flag, value);
        }
        else
        {
            arguments.Add(flag, context.Paths.Input(value));
        }
    }

    private ProcessingResult ProcessGeometries(
        string input,
        string output,
        string geometryOperation,
        string? layerName,
        string? where,
        GeometryOpParameters parameters,
        string? outputLayerName,
        string? format,
        string[]? layerCreationOptions,
        bool overwrite,
        GdalOperation operation)
    {
        using var dataset = Open.Vector(context.Paths.Input(input));
        using var source = VectorSupport.GetLayer(dataset, layerName);
        VectorSupport.ApplyFilters(source, where, null, null, null);
        var sourceDefinition = source.GetLayerDefn();
        var resultType = GeometryOps.ResultType(geometryOperation, source.GetGeomType(), parameters) ?? wkbGeometryType.wkbUnknown;
        var resolvedOutput = context.Paths.Output(output, overwrite);
        long skipped = 0;
        var result = Processing.Guard(operation, resolvedOutput, () =>
        {
            using var destination = Processing.CreateDataSource(resolvedOutput, format, null);
            using var srs = source.GetSpatialRef();
            using var target = destination.CreateLayer(
                Processing.LayerNameFor(resolvedOutput, outputLayerName), srs, resultType, layerCreationOptions ?? []);
            for (var i = 0; i < sourceDefinition.GetFieldCount(); i++)
            {
                using var field = sourceDefinition.GetFieldDefn(i);
                target.CreateField(field, 1);
            }

            var targetDefinition = target.GetLayerDefn();
            var transactional = destination.TestCapability("Transactions");
            var total = Math.Max(1, source.GetFeatureCount(0));
            long processed = 0;
            if (transactional)
            {
                destination.StartTransaction(0);
            }

            source.ResetReading();
            while (source.GetNextFeature() is { } feature)
            {
                using (feature)
                {
                    operation.ThrowIfCancelled();
                    var geometry = feature.GetGeometryRef();
                    using var processedGeometry = geometry is null ? null : GeometryOps.Apply(geometryOperation, geometry, parameters);
                    if (geometry is not null && processedGeometry is null)
                    {
                        skipped++;
                        continue;
                    }

                    using var written = new Feature(targetDefinition);
                    written.SetFrom(feature, 1);
                    written.SetGeometry(processedGeometry);
                    target.CreateFeature(written);
                    if (++processed % FeaturesPerTransaction == 0 && transactional)
                    {
                        destination.CommitTransaction();
                        destination.StartTransaction(0);
                    }

                    if (processed % 1000 == 0)
                    {
                        operation.Progress(Math.Min(1, processed / (double)total), IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }

            if (transactional)
            {
                destination.CommitTransaction();
            }

            target.SyncToDisk();
        });

        return skipped == 0 ? result : result with { Message = $"{skipped} feature(s) were skipped because the operation produced no geometry." };
    }
}
