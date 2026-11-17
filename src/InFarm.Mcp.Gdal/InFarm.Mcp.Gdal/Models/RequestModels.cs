namespace InFarm.Mcp.Gdal.Models;

public sealed record RasterTranslateRequest
{
    [Description("Input raster path.")]
    public required string Input { get; init; }

    [Description("Output raster path. The format is taken from the extension unless format is given.")]
    public required string Output { get; init; }

    [Description("Replaces the output when it already exists.")]
    public bool Overwrite { get; init; }

    [Description("Output driver short name, for example GTiff, COG, PNG, JPEG, VRT or netCDF.")]
    public string? Format { get; init; }

    [Description("Output data type: Byte, Int8, UInt16, Int16, UInt32, Int32, UInt64, Int64, Float32, Float64, CInt16, CInt32, CFloat32 or CFloat64.")]
    public string? OutputType { get; init; }

    [Description("Input bands to copy, in output order and starting at 1. A band can be repeated. Use the text 'mask' or 'mask,1' to select a mask band.")]
    public string[]? Bands { get; init; }

    [Description("Pixel window to copy as [xOffset, yOffset, width, height].")]
    public double[]? SourceWindow { get; init; }

    [Description("Georeferenced window to copy as [upperLeftX, upperLeftY, lowerRightX, lowerRightY].")]
    public double[]? ProjectedWindow { get; init; }

    [Description("Coordinate reference system of projectedWindow when it differs from the raster.")]
    public string? ProjectedWindowCrs { get; init; }

    [Description("Output size as [width, height]. Each value is a pixel count or a percentage such as 50%. Use 0 for one value to keep the aspect ratio.")]
    public string[]? OutputSize { get; init; }

    [Description("Output resolution as [xResolution, yResolution] in georeferenced units.")]
    public double[]? Resolution { get; init; }

    [Description("Resampling method: nearest, bilinear, cubic, cubicspline, lanczos, average, rms or mode.")]
    public string? Resampling { get; init; }

    [Description("Rescales values. Give [sourceMin, sourceMax, outputMin, outputMax], [sourceMin, sourceMax] for an output range of 0 to 255, or an empty array to use the actual range of each band.")]
    public double[]? Scale { get; init; }

    [Description("Exponent of the power function used with scale.")]
    public double? Exponent { get; init; }

    [Description("Applies the scale and offset of the bands to produce unscaled values.")]
    public bool Unscale { get; init; }

    [Description("NoData value to assign to the output bands, or 'none' to remove it.")]
    public string? NoData { get; init; }

    [Description("Coordinate reference system to assign to the output without reprojecting.")]
    public string? AssignCrs { get; init; }

    [Description("Georeferenced bounds to assign to the output as [upperLeftX, upperLeftY, lowerRightX, lowerRightY].")]
    public double[]? AssignBounds { get; init; }

    [Description("Expands a paletted band to gray, rgb or rgba.")]
    public string? Expand { get; init; }

    [Description("Color interpretation of each output band, for example red, green, blue, alpha, gray or undefined.")]
    public string[]? ColorInterpretation { get; init; }

    [Description("Metadata items to set on the output in NAME=VALUE form.")]
    public string[]? Metadata { get; init; }

    [Description("Computes statistics and stores them in the output.")]
    public bool Statistics { get; init; }

    [Description("Driver creation options in NAME=VALUE form, for example COMPRESS=DEFLATE or TILED=YES. Use gdal_driver_info to list them.")]
    public string[]? CreationOptions { get; init; }

    [Description("Open options of the input driver in NAME=VALUE form.")]
    public string[]? OpenOptions { get; init; }

    [Description("Additional gdal_translate arguments, one array element per token, for example ['-mask', '4'].")]
    public string[]? AdditionalArguments { get; init; }
}

public sealed record RasterWarpRequest
{
    [Description("Input raster paths. Several inputs are mosaicked, and later inputs are drawn over earlier ones.")]
    public required string[] Inputs { get; init; }

    [Description("Output raster path.")]
    public required string Output { get; init; }

    [Description("Replaces the output when it already exists.")]
    public bool Overwrite { get; init; }

    [Description("Output driver short name. The format is taken from the output extension when omitted.")]
    public string? Format { get; init; }

    [Description("Target coordinate reference system, for example EPSG:3857. Omit it to keep the source system.")]
    public string? TargetCrs { get; init; }

    [Description("Overrides the coordinate reference system of the inputs.")]
    public string? SourceCrs { get; init; }

    [Description("Output resolution as [xResolution, yResolution] in target units.")]
    public double[]? Resolution { get; init; }

    [Description("Output size in pixels as [width, height]. Use 0 for one value to derive it from the other.")]
    public int[]? Size { get; init; }

    [Description("Output extent as [minX, minY, maxX, maxY] in the target coordinate reference system unless boundsCrs is given.")]
    public double[]? Bounds { get; init; }

    [Description("Coordinate reference system of bounds.")]
    public string? BoundsCrs { get; init; }

    [Description("Aligns the output extent to multiples of the resolution.")]
    public bool TargetAlignedPixels { get; init; }

    [Description("Resampling method: near, bilinear, cubic, cubicspline, lanczos, average, rms, mode, max, min, med, q1, q3 or sum.")]
    public string? Resampling { get; init; }

    [Description("NoData values of the input bands, separated by spaces.")]
    public string? SourceNoData { get; init; }

    [Description("NoData values of the output bands, separated by spaces.")]
    public string? DestinationNoData { get; init; }

    [Description("Adds an alpha band to the output that marks pixels without data.")]
    public bool DestinationAlpha { get; init; }

    [Description("Treats the last band of the inputs as an alpha band.")]
    public bool SourceAlpha { get; init; }

    [Description("Output data type.")]
    public string? OutputType { get; init; }

    [Description("Input bands to warp, starting at 1.")]
    public int[]? Bands { get; init; }

    [Description("Vector dataset whose polygons limit the area that is written.")]
    public string? Cutline { get; init; }

    [Description("Layer of the cutline dataset.")]
    public string? CutlineLayer { get; init; }

    [Description("Attribute filter that selects the cutline features.")]
    public string? CutlineWhere { get; init; }

    [Description("Shrinks the output extent to the extent of the cutline.")]
    public bool CropToCutline { get; init; }

    [Description("Blend distance in pixels along the cutline.")]
    public double? CutlineBlend { get; init; }

    [Description("Overview level to read from: AUTO, AUTO-n, NONE or a level number.")]
    public string? OverviewLevel { get; init; }

    [Description("Uses several threads for the warp computation and the I/O.")]
    public bool Multithreaded { get; init; } = true;

    [Description("Memory for the warp buffers in megabytes.")]
    public double? WarpMemoryMegabytes { get; init; }

    [Description("Error threshold of the transformation approximation in pixels. Use 0 for exact transformation.")]
    public double? ErrorThreshold { get; init; }

    [Description("Warp options in NAME=VALUE form, for example NUM_THREADS=ALL_CPUS or CUTLINE_ALL_TOUCHED=TRUE.")]
    public string[]? WarpOptions { get; init; }

    [Description("Transformer options in NAME=VALUE form, for example RPC_DEM=dem.tif or COORDINATE_OPERATION=<PROJ pipeline>.")]
    public string[]? TransformerOptions { get; init; }

    [Description("Driver creation options in NAME=VALUE form.")]
    public string[]? CreationOptions { get; init; }

    [Description("Open options of the input driver in NAME=VALUE form.")]
    public string[]? OpenOptions { get; init; }

    [Description("Additional gdalwarp arguments, one array element per token.")]
    public string[]? AdditionalArguments { get; init; }
}

public sealed record VectorConvertRequest
{
    [Description("Input vector dataset path or connection string.")]
    public required string Input { get; init; }

    [Description("Output vector dataset path or connection string.")]
    public required string Output { get; init; }

    [Description("How the output is written: create (new dataset), append (add features to existing layers), overwrite_layer (replace layers in an existing dataset), update (add new layers to an existing dataset) or upsert (insert or update features by key).")]
    public string Mode { get; init; } = "create";

    [Description("Replaces the whole output dataset when mode is create and the output exists.")]
    public bool Overwrite { get; init; }

    [Description("Output driver short name, for example GPKG, GeoJSON, FlatGeobuf, 'ESRI Shapefile', Parquet, CSV or PostgreSQL.")]
    public string? Format { get; init; }

    [Description("Names of the input layers to convert. Every layer is converted when omitted.")]
    public string[]? Layers { get; init; }

    [Description("Name of the output layer. Use it with a single input layer or with sql.")]
    public string? OutputLayerName { get; init; }

    [Description("Attribute filter in OGR SQL WHERE syntax, for example \"population > 1000 AND name LIKE 'A%'\".")]
    public string? Where { get; init; }

    [Description("SQL statement to run against the input. Its result becomes the output layer.")]
    public string? Sql { get; init; }

    [Description("SQL dialect: OGRSQL or SQLITE.")]
    public string? SqlDialect { get; init; }

    [Description("Spatial filter as [minX, minY, maxX, maxY].")]
    public double[]? SpatialFilter { get; init; }

    [Description("Coordinate reference system of spatialFilter when it differs from the layer.")]
    public string? SpatialFilterCrs { get; init; }

    [Description("Reprojects the features to this coordinate reference system.")]
    public string? TargetCrs { get; init; }

    [Description("Overrides the coordinate reference system of the input.")]
    public string? SourceCrs { get; init; }

    [Description("Assigns this coordinate reference system to the output without reprojecting.")]
    public string? AssignCrs { get; init; }

    [Description("Fields to copy. Every field is copied when omitted.")]
    public string[]? SelectFields { get; init; }

    [Description("Output geometry type, for example POINT, MULTIPOLYGON, PROMOTE_TO_MULTI, CONVERT_TO_LINEAR or NONE.")]
    public string? GeometryType { get; init; }

    [Description("Coordinate dimension: XY, XYZ, XYM, XYZM or layer_dim.")]
    public string? Dimension { get; init; }

    [Description("Clips the input geometries. Give a bounding box 'minX minY maxX maxY', a WKT polygon or the path of a vector dataset, in the source coordinate reference system.")]
    public string? ClipSource { get; init; }

    [Description("Clips the output geometries after reprojection. Give a bounding box 'minX minY maxX maxY', a WKT polygon or the path of a vector dataset.")]
    public string? ClipDestination { get; init; }

    [Description("Simplification tolerance in the units of the output coordinate reference system.")]
    public double? Simplify { get; init; }

    [Description("Maximum distance between two consecutive vertices.")]
    public double? Segmentize { get; init; }

    [Description("Repairs invalid geometries.")]
    public bool MakeValid { get; init; }

    [Description("Writes one feature for each part of a geometry collection.")]
    public bool ExplodeCollections { get; init; }

    [Description("Continues after a feature fails instead of stopping.")]
    public bool SkipFailures { get; init; }

    [Description("Maximum number of features to copy from each layer.")]
    public int? Limit { get; init; }

    [Description("Number of features in each transaction.")]
    public int? TransactionSize { get; init; }

    [Description("Keeps the feature identifiers of the input.")]
    public bool PreserveFid { get; init; }

    [Description("Layer creation options in NAME=VALUE form, for example SPATIAL_INDEX=YES or GEOMETRY_NAME=geom.")]
    public string[]? LayerCreationOptions { get; init; }

    [Description("Dataset creation options in NAME=VALUE form.")]
    public string[]? DatasetCreationOptions { get; init; }

    [Description("Open options of the input driver in NAME=VALUE form.")]
    public string[]? OpenOptions { get; init; }

    [Description("Additional ogr2ogr arguments, one array element per token, for example ['-fieldTypeToString', 'Date'].")]
    public string[]? AdditionalArguments { get; init; }
}

public sealed record BandEdit
{
    [Description("Band number, starting at 1.")]
    public int Band { get; init; }

    [Description("NoData value, or 'none' to remove it.")]
    public string? NoData { get; init; }

    public double? Scale { get; init; }

    public double? Offset { get; init; }

    public string? Unit { get; init; }

    [Description("Color interpretation, for example red, green, blue, alpha, gray, palette, nir or undefined.")]
    public string? ColorInterpretation { get; init; }

    public string? Description { get; init; }

    public string[]? CategoryNames { get; init; }
}

public sealed record RasterEditRequest
{
    [Description("Raster dataset path. The dataset is modified in place.")]
    public required string Path { get; init; }

    [Description("Coordinate reference system to assign, or 'none' to remove it. The pixel values are not reprojected.")]
    public string? Crs { get; init; }

    [Description("Extent to assign as [minX, minY, maxX, maxY]. The geotransform is derived from the extent and the raster size.")]
    public double[]? Bounds { get; init; }

    [Description("Affine geotransform to assign as [originX, pixelWidth, rowRotation, originY, columnRotation, pixelHeight].")]
    public double[]? GeoTransform { get; init; }

    [Description("NoData value to assign to every band, or 'none' to remove it.")]
    public string? NoData { get; init; }

    [Description("Edits of individual bands.")]
    public BandEdit[]? Bands { get; init; }

    [Description("Ground control points to assign.")]
    public GcpModel[]? Gcps { get; init; }

    [Description("Coordinate reference system of the ground control points.")]
    public string? GcpCrs { get; init; }

    [Description("Removes the stored statistics of every band.")]
    public bool ClearStatistics { get; init; }

    [Description("Creates a mask band for the whole dataset.")]
    public bool CreateMask { get; init; }
}

public sealed record FieldDefinition
{
    public required string Name { get; init; }

    [Description("Field type: Integer, Integer64, Real, String, Date, Time, DateTime, Binary, IntegerList, Integer64List, RealList or StringList.")]
    public string Type { get; init; } = "String";

    [Description("Field subtype: None, Boolean, Int16, Float32, JSON or UUID.")]
    public string? SubType { get; init; }

    public int? Width { get; init; }

    public int? Precision { get; init; }

    public bool? Nullable { get; init; }

    public bool? Unique { get; init; }

    [Description("Default value expression, for example 0, 'text' or CURRENT_TIMESTAMP.")]
    public string? Default { get; init; }

    public string? AlternativeName { get; init; }

    public string? Comment { get; init; }
}

public sealed record FeatureInput
{
    [Description("Feature identifier. Required for updates. Omit it on insert to let the driver assign one.")]
    public long? Fid { get; init; }

    [Description("Attribute values by field name. A null value sets the field to null.")]
    public Dictionary<string, System.Text.Json.JsonElement>? Properties { get; init; }

    [Description("Geometry as WKT or as GeoJSON geometry text. Use the text 'null' to clear the geometry in an update.")]
    public string? Geometry { get; init; }
}
