namespace InFarm.Mcp.Gdal.Infrastructure;

internal static class VectorSupport
{
    private static readonly string[] LayerCapabilities =
    [
        "RandomRead", "SequentialWrite", "RandomWrite", "FastSpatialFilter", "FastFeatureCount", "FastGetExtent", "FastSetNextByIndex",
        "CreateField", "DeleteField", "ReorderFields", "AlterFieldDefn", "CreateGeomField", "DeleteFeature", "UpsertFeature", "Transactions",
        "Rename", "StringsAsUTF8", "IgnoreFields", "CurveGeometries", "MeasuredGeometries", "ZGeometries"
    ];

    // Returns the named layer, or the only layer of the dataset when no name is given.
    public static Layer GetLayer(Dataset dataset, string? name)
    {
        var count = dataset.GetLayerCount();
        if (name is not null)
        {
            return dataset.GetLayerByName(name) ?? throw new McpException($"The layer '{name}' does not exist. Layers: {LayerNames(dataset)}.");
        }

        return count switch
        {
            1 => dataset.GetLayer(0),
            0 => throw new McpException("The dataset has no vector layers."),
            _ => throw new McpException($"The dataset has {count} layers, so a layer name is required. Layers: {LayerNames(dataset)}.")
        };
    }

    public static Layer GetLayer(DataSource dataSource, string? name)
    {
        var count = dataSource.GetLayerCount();
        if (name is not null)
        {
            return dataSource.GetLayerByName(name) ?? throw new McpException($"The layer '{name}' does not exist. Layers: {LayerNames(dataSource)}.");
        }

        return count switch
        {
            1 => dataSource.GetLayerByIndex(0),
            0 => throw new McpException("The dataset has no vector layers."),
            _ => throw new McpException($"The dataset has {count} layers, so a layer name is required. Layers: {LayerNames(dataSource)}.")
        };
    }

    public static LayerInfo DescribeLayer(Layer layer, bool includeFields, bool includeExtent, bool exactCount)
    {
        var definition = layer.GetLayerDefn();
        using var srs = layer.GetSpatialRef();
        Bounds? extent = null;
        if (includeExtent)
        {
            extent = Extent(layer);
        }

        var fidColumn = layer.GetFIDColumn();
        var geometryColumn = layer.GetGeometryColumn();
        return new LayerInfo
        {
            Name = layer.GetName(),
            GeometryType = Geometries.TypeName(layer.GetGeomType()),
            FeatureCount = layer.GetFeatureCount(exactCount ? 1 : 0),
            Extent = extent,
            Crs = Crs.Summarize(srs),
            FidColumn = string.IsNullOrEmpty(fidColumn) ? null : fidColumn,
            GeometryColumn = string.IsNullOrEmpty(geometryColumn) ? null : geometryColumn,
            Fields = includeFields ? DescribeFields(definition) : null,
            GeometryFields = includeFields ? DescribeGeometryFields(definition) : null,
            Capabilities = includeFields ? [.. LayerCapabilities.Where(layer.TestCapability)] : null
        };
    }

    public static Bounds? Extent(Layer layer)
    {
        try
        {
            using var envelope = new Envelope();
            return layer.GetExtent(envelope, 1) == 0 ? new Bounds(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY) : null;
        }
        catch (ApplicationException)
        {
            // An empty layer and a layer without geometry have no extent.
            return null;
        }
    }

    public static IReadOnlyList<FieldInfo> DescribeFields(FeatureDefn definition)
    {
        var fields = new List<FieldInfo>();
        for (var i = 0; i < definition.GetFieldCount(); i++)
        {
            using var field = definition.GetFieldDefn(i);
            var subType = field.GetSubType();
            var defaultValue = field.GetDefault();
            var alternativeName = field.GetAlternativeName();
            var comment = field.GetComment();
            var domain = field.GetDomainName();
            fields.Add(new FieldInfo
            {
                Name = field.GetName(),
                Type = field.GetFieldTypeName(field.GetFieldType()),
                SubType = subType == FieldSubType.OFSTNone ? null : Ogr.GetFieldSubTypeName(subType),
                Width = field.GetWidth() > 0 ? field.GetWidth() : null,
                Precision = field.GetPrecision() > 0 ? field.GetPrecision() : null,
                Nullable = field.IsNullable() != 0,
                Unique = field.IsUnique() != 0,
                Default = string.IsNullOrEmpty(defaultValue) ? null : defaultValue,
                AlternativeName = string.IsNullOrEmpty(alternativeName) ? null : alternativeName,
                Comment = string.IsNullOrEmpty(comment) ? null : comment,
                Domain = string.IsNullOrEmpty(domain) ? null : domain
            });
        }

        return fields;
    }

    public static FieldDefn BuildField(FieldDefinition definition)
    {
        if (!Enum.TryParse<FieldType>("OFT" + definition.Type, true, out var type))
        {
            throw new McpException(
                $"'{definition.Type}' is not a field type. The types are Integer, Integer64, Real, String, Date, Time, DateTime, Binary, IntegerList, Integer64List, RealList and StringList.");
        }

        var field = new FieldDefn(definition.Name, type);
        if (definition.SubType is not null)
        {
            if (!Enum.TryParse<FieldSubType>("OFST" + definition.SubType, true, out var subType))
            {
                field.Dispose();
                throw new McpException($"'{definition.SubType}' is not a field subtype. The subtypes are None, Boolean, Int16, Float32, JSON and UUID.");
            }

            field.SetSubType(subType);
        }

        if (definition.Width is not null)
        {
            field.SetWidth(definition.Width.Value);
        }

        if (definition.Precision is not null)
        {
            field.SetPrecision(definition.Precision.Value);
        }

        if (definition.Nullable is not null)
        {
            field.SetNullable(definition.Nullable.Value ? 1 : 0);
        }

        if (definition.Unique is not null)
        {
            field.SetUnique(definition.Unique.Value ? 1 : 0);
        }

        if (definition.Default is not null)
        {
            field.SetDefault(definition.Default);
        }

        if (definition.AlternativeName is not null)
        {
            field.SetAlternativeName(definition.AlternativeName);
        }

        if (definition.Comment is not null)
        {
            field.SetComment(definition.Comment);
        }

        return field;
    }

    // Applies an attribute filter and a spatial filter, given as a bounding box or as a geometry, to a layer.
    public static void ApplyFilters(Layer layer, string? where, double[]? bounds, string? geometry, string? filterCrs)
    {
        if (!string.IsNullOrWhiteSpace(where))
        {
            try
            {
                layer.SetAttributeFilter(where);
            }
            catch (ApplicationException ex)
            {
                throw new McpException(
                    $"The attribute filter is not valid: {ex.Message}. Use OGR SQL WHERE syntax, for example \"population > 1000 AND name = 'Paris'\".");
            }
        }

        if (bounds is null && geometry is null)
        {
            return;
        }

        if (bounds is not null && bounds.Length != 4)
        {
            throw new McpException("The bounding box needs exactly four numbers: minX, minY, maxX and maxY.");
        }

        using var filter = geometry is not null
            ? Geometries.Parse(geometry)
            : Geometries.Parse(string.Create(
                CultureInfo.InvariantCulture,
                $"POLYGON (({bounds![0]:R} {bounds[1]:R},{bounds[2]:R} {bounds[1]:R},{bounds[2]:R} {bounds[3]:R},{bounds[0]:R} {bounds[3]:R},{bounds[0]:R} {bounds[1]:R}))"));
        if (filterCrs is not null)
        {
            using var source = Crs.Parse(filterCrs);
            using var layerSrs = layer.GetSpatialRef()
                ?? throw new McpException("The layer has no coordinate reference system, so a filter in another coordinate reference system cannot be applied.");
            using var target = layerSrs.Clone();
            target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            using var transformation = new CoordinateTransformation(source, target);
            filter.Transform(transformation);
        }

        layer.SetSpatialFilter(filter);
    }

    // Reads one page of features. The response size limit can end the page before the requested number of features is reached.
    public static FeaturePage ReadPage(
        Layer layer,
        long offset,
        int limit,
        IReadOnlyList<string>? fields,
        GeometryFormat geometryFormat,
        string? targetCrs,
        bool countMatching,
        GdalToolContext context,
        GdalOperation operation)
    {
        var definition = layer.GetLayerDefn();
        var fieldIndexes = new List<int>();
        if (fields is null)
        {
            fieldIndexes.AddRange(Enumerable.Range(0, definition.GetFieldCount()));
        }
        else
        {
            foreach (var name in fields)
            {
                var index = definition.GetFieldIndex(name);
                if (index < 0)
                {
                    throw new McpException($"The field '{name}' does not exist. Fields: {string.Join(", ", DescribeFields(definition).Select(field => field.Name))}.");
                }

                fieldIndexes.Add(index);
            }
        }

        var fieldDefinitions = fieldIndexes.Select(definition.GetFieldDefn).ToList();
        using var layerSrs = layer.GetSpatialRef();
        using var outputSrs = targetCrs is null ? null : Crs.Parse(targetCrs);
        using var sourceSrs = outputSrs is null ? null : layerSrs?.Clone()
            ?? throw new McpException("The layer has no coordinate reference system, so its geometries cannot be reprojected.");
        sourceSrs?.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using var transformation = outputSrs is null ? null : new CoordinateTransformation(sourceSrs!, outputSrs);

        long? total = countMatching ? layer.GetFeatureCount(1) : null;
        layer.ResetReading();
        if (offset > 0)
        {
            layer.SetNextByIndex(offset);
        }

        var features = new List<FeatureModel>();
        var characters = 0L;
        var reachedEnd = false;
        var sizeLimited = false;
        while (features.Count < limit)
        {
            operation.ThrowIfCancelled();
            using var feature = layer.GetNextFeature();
            if (feature is null)
            {
                reachedEnd = true;
                break;
            }

            var model = ToModel(feature, fieldIndexes, fieldDefinitions, geometryFormat, transformation, context.Options.MaxGeometryCharacters);
            characters += (model.Geometry?.Length ?? 0) + 40
                + (model.Properties?.Sum(property => property.Key.Length + (property.Value?.ToString()?.Length ?? 4) + 6) ?? 0);
            features.Add(model);
            if (characters > context.Options.MaxResponseCharacters)
            {
                sizeLimited = true;
                break;
            }
        }

        var hasMore = false;
        if (!reachedEnd)
        {
            using var next = layer.GetNextFeature();
            hasMore = next is not null;
        }

        fieldDefinitions.ForEach(field => field.Dispose());
        return new FeaturePage
        {
            Layer = layer.GetName(),
            Returned = features.Count,
            Offset = offset,
            NextOffset = hasMore ? offset + features.Count : null,
            TotalMatching = total,
            Crs = Crs.Summarize(outputSrs ?? layerSrs),
            Features = features,
            SizeLimited = sizeLimited,
            Warnings = operation.WarningsOrNull
        };
    }

    public static FeatureModel ToModel(
        Feature feature,
        IReadOnlyList<int> fieldIndexes,
        IReadOnlyList<FieldDefn> fieldDefinitions,
        GeometryFormat geometryFormat,
        CoordinateTransformation? transformation,
        int maxGeometryCharacters)
    {
        var properties = new Dictionary<string, object?>(fieldIndexes.Count);
        for (var i = 0; i < fieldIndexes.Count; i++)
        {
            properties[fieldDefinitions[i].GetName()] = FieldValues.Get(feature, fieldIndexes[i], fieldDefinitions[i]);
        }

        var geometry = geometryFormat == GeometryFormat.None ? null : feature.GetGeometryRef();
        if (geometry is not null && transformation is not null)
        {
            geometry.Transform(transformation);
        }

        var text = Geometries.Format(geometry, geometryFormat, maxGeometryCharacters, out var omitted);
        return new FeatureModel
        {
            Fid = feature.GetFID(),
            Properties = properties,
            Geometry = text,
            GeometryType = geometry is null ? null : Geometries.TypeName(geometry.GetGeometryType()),
            Envelope = geometry is not null && (omitted || geometryFormat == GeometryFormat.Envelope) ? Geometries.Envelope(geometry) : null,
            GeometryOmitted = omitted ? true : null
        };
    }

    private static IReadOnlyList<GeometryFieldInfo> DescribeGeometryFields(FeatureDefn definition)
    {
        var fields = new List<GeometryFieldInfo>();
        for (var i = 0; i < definition.GetGeomFieldCount(); i++)
        {
            using var field = definition.GetGeomFieldDefn(i);
            using var srs = field.GetSpatialRef();
            fields.Add(new GeometryFieldInfo
            {
                Name = field.GetName(),
                Type = Geometries.TypeName(field.GetFieldType()),
                Nullable = field.IsNullable() != 0,
                Crs = Crs.Summarize(srs)
            });
        }

        return fields;
    }

    private static string LayerNames(Dataset dataset)
    {
        var names = new List<string>();
        for (var i = 0; i < Math.Min(dataset.GetLayerCount(), 50); i++)
        {
            using var layer = dataset.GetLayer(i);
            names.Add(layer.GetName());
        }

        return string.Join(", ", names);
    }

    private static string LayerNames(DataSource dataSource)
    {
        var names = new List<string>();
        for (var i = 0; i < Math.Min(dataSource.GetLayerCount(), 50); i++)
        {
            using var layer = dataSource.GetLayerByIndex(i);
            names.Add(layer.GetName());
        }

        return string.Join(", ", names);
    }
}
