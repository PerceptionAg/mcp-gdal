using System.Text.RegularExpressions;

namespace InFarm.Mcp.Gdal.Tools;

// Vector inspection and querying tools, which never modify the dataset.
[McpServerToolType]
public sealed partial class VectorTools(GdalToolContext context)
{
    [McpServerTool(Name = "gdal_vector_info", Title = "Describe a vector dataset and its layers", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Returns the layers of a vector dataset with their geometry types, feature counts, coordinate reference systems, attribute schemas, geometry fields and capabilities. " +
        "This is the structured equivalent of 'ogrinfo -so'. Pass layers to describe selected layers only.")]
    public Task<VectorInfo> InfoAsync(
        [Description("Vector dataset path, virtual path or connection string.")] string path,
        [Description("Names of the layers to describe. Every layer is described when omitted.")] string[]? layers = null,
        [Description("Includes the attribute fields, the geometry fields and the capabilities of each layer.")] bool includeFields = true,
        [Description("Includes the extent of each layer, which can require a full scan in some formats.")] bool includeExtent = true,
        [Description("Counts the features exactly, even when that requires a full scan.")] bool exactCounts = false,
        [Description("Includes the dataset metadata of the default domain.")] bool includeMetadata = false,
        [Description("Number of layers to skip.")] int offset = 0,
        [Description("Maximum number of layers to describe.")] int limit = 50,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_info", cancellationToken, operation =>
        {
            var resolved = context.Paths.Input(path);
            using var dataset = Open.Vector(resolved, false, openOptions);
            using var driver = dataset.GetDriver();
            var infos = new List<LayerInfo>();
            if (layers is not null)
            {
                foreach (var name in layers)
                {
                    using var layer = VectorSupport.GetLayer(dataset, name);
                    infos.Add(VectorSupport.DescribeLayer(layer, includeFields, includeExtent, exactCounts));
                }
            }
            else
            {
                for (var i = offset; i < Math.Min(dataset.GetLayerCount(), offset + limit); i++)
                {
                    operation.ThrowIfCancelled();
                    using var layer = dataset.GetLayer(i);
                    infos.Add(VectorSupport.DescribeLayer(layer, includeFields, includeExtent, exactCounts));
                }
            }

            var domains = dataset.GetFieldDomainNames(null);
            var relationships = dataset.GetRelationshipNames(null);
            return new VectorInfo
            {
                Path = resolved,
                Driver = driver.ShortName,
                LayerCount = dataset.GetLayerCount(),
                Layers = infos,
                FieldDomains = domains is { Length: > 0 } ? domains : null,
                Relationships = relationships is { Length: > 0 } ? relationships : null,
                Metadata = includeMetadata ? Describe.KeyValues(dataset.GetMetadata(null)) : null
            };
        });
    }

    [McpServerTool(Name = "gdal_vector_info_json", Title = "Run ogrinfo and return its JSON", ReadOnly = true, Idempotent = true)]
    [Description(
        "Runs the native ogrinfo report on a vector dataset and returns its JSON text unchanged. Use this tool for details that gdal_vector_info does not model, " +
        "such as layer metadata, field domains, relationships and coordinate precision. Features are left out unless features=true.")]
    public Task<string> InfoJsonAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Names of the layers to report. Every layer is reported when omitted.")] string[]? layers = null,
        [Description("Includes the features of the layers.")] bool features = false,
        [Description("Maximum number of features to report for each layer.")] int? limit = null,
        [Description("Attribute filter in OGR SQL WHERE syntax.")] string? where = null,
        [Description("Lists the metadata domains.")] bool listMetadataDomains = false,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        [Description("Additional ogrinfo arguments, one array element per token.")] string[]? additionalArguments = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_info_json", cancellationToken, _ =>
        {
            var arguments = new ArgList()
                .Add("-json")
                .Flag(layers is null or { Length: 0 }, "-al")
                .Flag(!features, "-so")
                .Flag(features, "-features")
                .Option("-limit", features ? Math.Min(limit ?? context.Options.DefaultFeatureCount, context.Options.MaxFeatureCount) : null)
                .Option("-where", where)
                .Flag(listMetadataDomains, "-listmdd")
                .Extra(additionalArguments, context.Paths);
            using var dataset = Open.Vector(context.Paths.Input(path), false, openOptions);
            if (layers is { Length: > 0 })
            {
                // The option parser expects a dataset name before the layer names, although the open dataset is what it reports on.
                arguments.Add("dataset").Add(layers);
            }

            using var options = new GDALVectorInfoOptions(arguments.ToArray());
            return Limits.EnsureText(
                GdalApi.GDALVectorInfo(dataset, options),
                context.Options.MaxResponseCharacters,
                "Select fewer layers, or read features in pages with gdal_vector_features.");
        });
    }

    [McpServerTool(Name = "gdal_vector_features", Title = "Read features from a vector layer", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(
        "Reads a page of features from a vector layer with optional attribute and spatial filters. Results are paged: pass the returned nextOffset as offset to continue. " +
        "Geometries are returned as WKT or GeoJSON text, and a geometry that exceeds the size limit is replaced by its envelope.")]
    public Task<FeaturePage> FeaturesAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter in OGR SQL WHERE syntax, for example \"population > 1000 AND name LIKE 'A%'\".")] string? where = null,
        [Description("Spatial filter as [minX, minY, maxX, maxY].")] double[]? bounds = null,
        [Description("Spatial filter as WKT or GeoJSON geometry text. Features that intersect the geometry are returned.")] string? intersects = null,
        [Description("Coordinate reference system of bounds or intersects when it differs from the layer. With a geographic system x is the longitude and y is the latitude.")] string? filterCrs = null,
        [Description("Fields to return. Every field is returned when omitted.")] string[]? fields = null,
        [Description("Geometry output: wkt, geojson, envelope or none.")] GeometryFormat geometryFormat = GeometryFormat.Wkt,
        [Description("Reprojects the returned geometries to this coordinate reference system, in longitude/easting then latitude/northing order.")] string? targetCrs = null,
        [Description("Number of matching features to skip.")] long offset = 0,
        [Description("Maximum number of features to return. The server default and maximum apply.")] int? limit = null,
        [Description("Also counts every feature that matches the filters.")] bool countMatching = false,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_features", cancellationToken, operation =>
        {
            using var dataset = Open.Vector(context.Paths.Input(path), false, openOptions);
            using var vectorLayer = VectorSupport.GetLayer(dataset, layer);
            VectorSupport.ApplyFilters(vectorLayer, where, bounds, intersects, filterCrs);
            return VectorSupport.ReadPage(
                vectorLayer, offset, context.ClampLimit(limit), fields, geometryFormat, targetCrs, countMatching, context, operation);
        });
    }

    [McpServerTool(Name = "gdal_vector_feature_count", Title = "Count features and compute their extent", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Counts the features of a layer that match optional attribute and spatial filters, and optionally computes the extent of the matching features.")]
    public Task<FeatureCountResult> FeatureCountAsync(
        [Description("Vector dataset path.")] string path,
        [Description("Layer name. It can be omitted when the dataset has a single layer.")] string? layer = null,
        [Description("Attribute filter in OGR SQL WHERE syntax.")] string? where = null,
        [Description("Spatial filter as [minX, minY, maxX, maxY].")] double[]? bounds = null,
        [Description("Spatial filter as WKT or GeoJSON geometry text.")] string? intersects = null,
        [Description("Coordinate reference system of bounds or intersects when it differs from the layer.")] string? filterCrs = null,
        [Description("Computes the extent of the matching features.")] bool includeExtent = true,
        [Description("Driver open options in NAME=VALUE form.")] string[]? openOptions = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_feature_count", cancellationToken, operation =>
        {
            using var dataset = Open.Vector(context.Paths.Input(path), false, openOptions);
            using var vectorLayer = VectorSupport.GetLayer(dataset, layer);
            VectorSupport.ApplyFilters(vectorLayer, where, bounds, intersects, filterCrs);
            using var srs = vectorLayer.GetSpatialRef();
            var filtered = where is not null || bounds is not null || intersects is not null;
            Bounds? extent = null;
            if (includeExtent && !filtered)
            {
                extent = VectorSupport.Extent(vectorLayer);
            }
            else if (includeExtent)
            {
                // The layer extent ignores filters, so the extent of a filtered selection is accumulated feature by feature.
                using var envelope = new Envelope();
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                vectorLayer.ResetReading();
                while (vectorLayer.GetNextFeature() is { } feature)
                {
                    using (feature)
                    {
                        operation.ThrowIfCancelled();
                        var geometry = feature.GetGeometryRef();
                        if (geometry is null || geometry.IsEmpty())
                        {
                            continue;
                        }

                        geometry.GetEnvelope(envelope);
                        minX = Math.Min(minX, envelope.MinX);
                        minY = Math.Min(minY, envelope.MinY);
                        maxX = Math.Max(maxX, envelope.MaxX);
                        maxY = Math.Max(maxY, envelope.MaxY);
                    }
                }

                extent = minX <= maxX ? new Bounds(minX, minY, maxX, maxY) : null;
            }

            return new FeatureCountResult
            {
                Layer = vectorLayer.GetName(),
                FeatureCount = vectorLayer.GetFeatureCount(1),
                Extent = extent,
                Crs = Crs.Summarize(srs)
            };
        });
    }

    [McpServerTool(Name = "gdal_vector_sql", Title = "Run an SQL query on a vector dataset", ReadOnly = false, Destructive = false, UseStructuredContent = true)]
    [Description(
        "Runs an SQL statement against a vector dataset and returns a page of the result. The OGRSQL dialect supports SELECT with WHERE, ORDER BY, GROUP BY, joins and " +
        "aggregate functions. The SQLITE dialect adds spatial functions such as ST_Area, ST_Buffer and ST_Intersects. Database drivers run the statement natively. " +
        "Only SELECT statements are accepted unless the server configuration allows SQL writes.")]
    public Task<FeaturePage> SqlAsync(
        [Description("Vector dataset path or connection string.")] string path,
        [Description("SQL statement, for example \"SELECT name, COUNT(*) AS n FROM roads GROUP BY name ORDER BY n DESC\".")] string sql,
        [Description("SQL dialect: OGRSQL, SQLITE or INDIRECT_SQLITE. The native dialect of the driver is used when omitted.")] string? dialect = null,
        [Description("Spatial filter as WKT or GeoJSON geometry text, in the coordinate reference system of the layer.")] string? intersects = null,
        [Description("Geometry output: wkt, geojson, envelope or none.")] GeometryFormat geometryFormat = GeometryFormat.Wkt,
        [Description("Number of result rows to skip.")] long offset = 0,
        [Description("Maximum number of result rows to return.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return context.RunAsync("gdal_vector_sql", cancellationToken, operation =>
        {
            var isQuery = QueryPattern().IsMatch(sql);
            if (!isQuery && (!context.Options.AllowSqlWrites || context.Options.ReadOnly))
            {
                throw new McpException(
                    "Only SELECT statements are allowed. SQL statements that modify data need the server option --allow-sql-writes and a server that is not read-only.");
            }

            if (UnsafeSqlPattern().IsMatch(sql))
            {
                throw new McpException("The statement uses a function or command that can reach files outside the dataset, which is not allowed.");
            }

            var resolved = isQuery ? context.Paths.Input(path) : context.Paths.Update(path);
            using var dataSource = Open.DataSource(resolved, !isQuery);
            using var filter = intersects is null ? null : Geometries.Parse(intersects);
            var result = dataSource.ExecuteSQL(sql, filter, dialect ?? string.Empty);
            if (result is null)
            {
                return new FeaturePage
                {
                    Layer = string.Empty,
                    Features = [],
                    Message = "The statement ran and returned no result set.",
                    Warnings = operation.WarningsOrNull
                };
            }

            try
            {
                return VectorSupport.ReadPage(result, offset, context.ClampLimit(limit), null, geometryFormat, null, false, context, operation);
            }
            finally
            {
                dataSource.ReleaseResultSet(result);
            }
        });
    }

    [GeneratedRegex(@"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QueryPattern();

    // These constructs let a SQLite statement open other files or load native code.
    [GeneratedRegex(@"\b(load_extension|ogr_datasource_load_layers|readfile|writefile|ATTACH|PRAGMA|VirtualOGR)\b|;\s*\S", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeSqlPattern();
}
