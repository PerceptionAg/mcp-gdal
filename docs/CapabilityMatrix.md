# GDAL Capability Matrix

This matrix maps GDAL functionality to the C# API of `MaxRev.Gdal.Universal` 3.13.3.611 (GDAL 3.13.3, PROJ 9.8.1, GEOS 3.14.1) and to the MCP tool that exposes it. The status column uses three values:

- **Implemented** means that a dedicated tool exists and an automated test covers it.
- **Via algorithm** means that the capability is reached through `gdal_algorithm_run`, which runs the unified `gdal` command natively.
- **Gap** means that the capability is not available, and the limitation column explains why.

## Dataset inspection and drivers

| Capability | Utility | C# API | MCP tool | Status | Limitations |
| --- | --- | --- | --- | --- | --- |
| Dataset summary | `gdalinfo`, `ogrinfo` | `Gdal.OpenEx`, `Dataset` | `gdal_dataset_info` | Implemented | |
| Raster structure, bands, NoData, masks, overviews | `gdalinfo` | `Dataset`, `Band` | `gdal_raster_info` | Implemented | The first 32 bands are described unless bands are selected. |
| Native gdalinfo JSON | `gdalinfo -json` | `Gdal.GDALInfo` | `gdal_raster_info_json` | Implemented | The response size limit applies. |
| Metadata domains, read and write | `gdalinfo -mdd`, `gdal_edit -mo` | `MajorObject` | `gdal_dataset_metadata`, `gdal_dataset_set_metadata` | Implemented | |
| Subdatasets | `gdalinfo` | `GetMetadata("SUBDATASETS")` | `gdal_dataset_subdatasets` | Implemented | |
| Statistics | `gdalinfo -stats` | `Band.ComputeStatistics` | `gdal_raster_statistics` | Implemented | |
| Histograms | `gdalinfo -hist` | `Band.GetHistogram` | `gdal_raster_histogram` | Implemented | |
| Pixel values at points | `gdallocationinfo` | `Band.ReadRaster`, `InterpolateAtPoint` | `gdal_raster_pixel_values` | Implemented | At most `max-feature-count` points in one call. |
| Pixel window | | `Band.ReadRaster` with `RasterIOExtraArg` | `gdal_raster_read_window` | Implemented | Limited to `max-window-pixels` values. |
| Color tables, attribute tables, GCPs | `gdalinfo` | `ColorTable`, `RasterAttributeTable`, `GetGCPs` | `gdal_raster_color_table`, `gdal_raster_attribute_table`, `gdal_raster_gcps` | Implemented | |
| Vector layers, schemas, counts, extents | `ogrinfo -so` | `Layer`, `FeatureDefn` | `gdal_vector_info`, `gdal_vector_feature_count` | Implemented | |
| Native ogrinfo JSON | `ogrinfo -json` | `Gdal.GDALVectorInfo` | `gdal_vector_info_json` | Implemented | |
| Multidimensional info | `gdalmdiminfo` | `Gdal.GDALMultiDimInfo` | `gdal_mdim_info` | Implemented | |
| Driver list and capabilities | `--formats` | `Gdal.GetDriver`, driver metadata | `gdal_driver_list` | Implemented | |
| Creation, layer and open options | `--format` | `DMD_CREATIONOPTIONLIST` and related items | `gdal_driver_info` | Implemented | |
| Driver identification | `gdal dataset identify` | `Gdal.IdentifyDriver` | `gdal_dataset_identify` | Implemented | |
| Dataset copy, rename and delete | `gdal dataset copy/rename/delete` | `Driver.CopyFiles`, `Rename`, `Delete` | `gdal_dataset_copy`, `gdal_dataset_delete` | Implemented | |
| Dataset integrity check | `gdal dataset check` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |

## Raster processing

| Capability | Utility | C# API | MCP tool | Status | Limitations |
| --- | --- | --- | --- | --- | --- |
| Format conversion, band selection, windows, scaling, compression | `gdal_translate` | `Gdal.wrapper_GDALTranslate` | `gdal_raster_translate` | Implemented | |
| Reprojection, resampling, alignment, cutlines | `gdalwarp` | `Gdal.Warp` | `gdal_raster_warp`, `gdal_raster_align` | Implemented | |
| Cropping and clipping | `gdal_translate -projwin`, `gdalwarp -cutline` | as above | `gdal_raster_crop` | Implemented | |
| Cloud Optimized GeoTIFF | `gdal_translate -of COG` | COG driver | `gdal_raster_cog` | Implemented | |
| Raster creation | `gdal_create` | `Driver.Create` | `gdal_raster_create` | Implemented | |
| Virtual rasters | `gdalbuildvrt` | `Gdal.BuildVRT` | `gdal_raster_build_vrt` | Implemented | |
| Mosaic and band stacking | `gdal_merge` | `BuildVRT` followed by `Translate` | `gdal_raster_mosaic` | Implemented | The inputs must share one coordinate reference system. Use `gdal_raster_warp` otherwise. |
| Overviews | `gdaladdo` | `Dataset.BuildOverviews` | `gdal_raster_overviews` | Implemented | |
| In-place editing | `gdal_edit` | `SetGeoTransform`, `SetSpatialRef`, band setters | `gdal_raster_edit` | Implemented | |
| Raster arithmetic | `gdal_calc` | `Band.BinaryOpBand`, `UnaryOp`, `MinimumOfNBands` and related methods | `gdal_raster_calculate` | Implemented | Arithmetic only, because comparisons, logical operators, `IfThenElse`, `log(band)` and band-to-band `pow` need muparser, which the bundled build lacks. `log` is derived from `log10`. |
| Expression-based calculation | `gdal raster calc` | `Algorithm` | none | Gap | The algorithm requires muparser, which the bundled build lacks. |
| Reclassification and thresholds | `gdal raster reclassify` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | Tested. |
| Rasterization | `gdal_rasterize` | `wrapper_GDALRasterizeDestName`, `DestDS` | `gdal_raster_rasterize` | Implemented | |
| Contours | `gdal_contour` | `wrapper_GDALContourDestName` | `gdal_raster_contours` | Implemented | |
| Hillshade, slope, aspect, color relief, TRI, TPI, roughness | `gdaldem` | `wrapper_GDALDEMProcessing` | `gdal_raster_dem` | Implemented | |
| Fill NoData | `gdal_fillnodata` | `Gdal.FillNodata` | `gdal_raster_fill_nodata` | Implemented | |
| Sieve filter | `gdal_sieve` | `Gdal.SieveFilter` | `gdal_raster_sieve` | Implemented | |
| Polygonization | `gdal_polygonize` | `Gdal.Polygonize`, `FPolygonize` | `gdal_raster_polygonize` | Implemented | |
| Proximity | `gdal_proximity` | `Gdal.ComputeProximity` | `gdal_raster_proximity` | Implemented | |
| Viewshed | `gdal_viewshed` | `Gdal.ViewshedGenerate` | `gdal_raster_viewshed` | Implemented | Single observer. Use `gdal raster viewshed` through the algorithm tool for its other options. |
| Retiling and pyramids | `gdal_retile` | `Translate` per tile | `gdal_raster_retile` | Implemented | No Python dependency. Web map tiles use `raster tile`. |
| Gridding scattered points | `gdal_grid` | `wrapper_GDALGrid` | `gdal_raster_grid` | Implemented | |
| Collar cleaning | `nearblack` | `wrapper_GDALNearblackDestName` | `gdal_raster_nearblack` | Implemented | |
| Footprints | `gdal_footprint` | `wrapper_GDALFootprintDestName` | `gdal_raster_footprint` | Implemented | |
| Raster tile index | `gdaltindex` | `wrapper_TileIndex_names` | `gdal_raster_tile_index` | Implemented | |
| Multidimensional translation | `gdalmdimtranslate` | `Gdal.MultiDimTranslate` | `gdal_mdim_translate` | Implemented | |
| Web map tiles | `gdal2tiles`, `gdal raster tile` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Zonal statistics | `gdal raster zonal-stats` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Pansharpening | `gdal_pansharpen` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Raster comparison | `gdalcompare` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Focal statistics, colour maps, blending, RGB to palette, NoData to alpha, resize, unscale | `gdal raster neighbors` and others | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |

## Vector processing

| Capability | Utility | C# API | MCP tool | Status | Limitations |
| --- | --- | --- | --- | --- | --- |
| Conversion, filtering, SQL, reprojection, clipping, simplification, repair | `ogr2ogr` | `wrapper_GDALVectorTranslateDestName`, `DestDS` | `gdal_vector_convert`, `gdal_vector_reproject`, `gdal_vector_filter`, `gdal_vector_clip` | Implemented | |
| Append, overwrite layer, update and upsert | `ogr2ogr -append/-overwrite/-update/-upsert` | as above | `gdal_vector_convert` | Implemented | |
| Feature reading with paging and filters | `ogrinfo` | `Layer.SetAttributeFilter`, `SetSpatialFilter`, `SetNextByIndex` | `gdal_vector_features` | Implemented | Page size and geometry length are limited. |
| SQL queries | `ogrinfo -sql` | `DataSource.ExecuteSQL` | `gdal_vector_sql` | Implemented | Writes need `--allow-sql-writes`. |
| Dataset and layer creation and deletion | `gdal vector create` | `Driver.CreateDataSource`, `CreateLayer`, `DeleteLayer` | `gdal_vector_create`, `gdal_vector_layer_create`, `gdal_vector_layer_delete`, `gdal_vector_layer_rename` | Implemented | |
| Field and geometry field management | `ogrinfo -sql ALTER` | `CreateField`, `AlterFieldDefn`, `DeleteField`, `CreateGeomField`, `AlterGeomFieldDefn` | `gdal_vector_field_create`, `gdal_vector_field_alter`, `gdal_vector_field_delete`, `gdal_vector_geometry_field` | Implemented | Support depends on the driver. |
| Feature insert, update, upsert and delete | | `CreateFeature`, `SetFeature`, `UpsertFeature`, `DeleteFeature` | `gdal_vector_features_write`, `gdal_vector_features_delete` | Implemented | The write runs in one transaction where the driver supports transactions. |
| Buffer, simplify, hulls, centroids, type conversion and more | `gdal vector buffer` and others | OGR and GEOS `Geometry` methods | `gdal_vector_buffer`, `gdal_vector_geometry_process` | Implemented | |
| Overlay | `ogr_layer_algebra` | `Layer.Intersection`, `Union`, `Erase`, `SymDifference`, `Identity`, `Update`, `Clip` | `gdal_vector_overlay` | Implemented | |
| Dissolve | `gdal vector dissolve` | ordered read followed by `UnaryUnion` | `gdal_vector_dissolve` | Implemented | |
| Validation and repair | `gdal vector check-geometry`, `make-valid` | `IsValid`, `GetInvalidityReason`, `MakeValid` | `gdal_vector_validate`, `gdal_vector_repair` | Implemented | |
| Layer merging | `ogrmerge` | repeated `VectorTranslate` | `gdal_vector_merge` | Implemented | |
| Vector tile index | `ogrtindex`, `gdal vector index` | `Algorithm` | `gdal_vector_tile_index` | Implemented | |
| Spatial index | `CREATE SPATIAL INDEX`, `CreateSpatialIndex()` | `ExecuteSQL` | `gdal_vector_spatial_index` | Implemented | ESRI Shapefile and GeoPackage only. |
| Coverage cleaning, partitioning, concatenation, sorting, gridding | `gdal vector clean-coverage` and others | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Linear referencing | `ogrlineref` | none | none | Gap | The bindings expose no API for it, and the unified command has no equivalent. |

## Geometry and coordinate reference systems

| Capability | Utility | C# API | MCP tool | Status | Limitations |
| --- | --- | --- | --- | --- | --- |
| Geometry measurement and validation | | `Area`, `Length`, `GeodesicArea`, `IsValid` | `gdal_geometry_info` | Implemented | |
| Geometry operations and predicates | | `Geometry` methods | `gdal_geometry_operation`, `gdal_geometry_relate` | Implemented | |
| CRS parsing and export | `gdalsrsinfo` | `SpatialReference.SetFromUserInput`, `ExportToWkt`, `ExportToProj4`, `ExportToPROJJSON` | `gdal_crs_info` | Implemented | |
| CRS identification | `gdalsrsinfo -e` | `FindMatches` | `gdal_crs_info` with `identify` | Implemented | |
| Database search | `projinfo --list-crs` | `Osr.GetCRSInfoListFromDatabase` | `gdal_crs_lookup` | Implemented | |
| CRS comparison | | `IsSame`, `IsSameGeogCS`, `IsSameVertCS` | `gdal_crs_compare` | Implemented | |
| Point, datum and vertical transformation | `gdaltransform`, `cs2cs` | `CoordinateTransformation`, `CoordinateTransformationOptions` | `gdal_crs_transform_points` | Implemented | Accuracy depends on the PROJ grids that are available. |
| Bounding box transformation | | `TransformBounds` | `gdal_crs_transform_bounds` | Implemented | |
| Axis order | | `SetAxisMappingStrategy` | all CRS tools | Implemented | Traditional GIS order is the default, and authority order is selectable. |
| UTM zone | | computed, then `ImportFromEPSG` | `gdal_crs_utm_zone` | Implemented | |
| Listing candidate coordinate operations with their accuracy | `projinfo -s -t` | none | none | Gap | The bindings do not expose `proj_create_operations`. The transformation tools accept constraints and explicit pipelines instead. |

## Virtual filesystems and configuration

| Capability | Utility | C# API | MCP tool | Status | Limitations |
| --- | --- | --- | --- | --- | --- |
| Reading through `/vsizip/`, `/vsitar/`, `/vsigzip/`, `/vsimem/`, `/vsicurl/`, `/vsis3/`, `/vsigs/`, `/vsiaz/` | | GDAL filesystem layer | every dataset tool | Implemented | Remote access can be disabled. |
| Listing | `gdal vsi list` | `Gdal.ReadDir`, `ReadDirRecursive` | `gdal_vsi_list` | Implemented | |
| Copy, move, delete, mkdir | `gdal vsi copy/move/delete` | `Gdal.CopyFile`, `Move`, `Unlink`, `RmdirRecursive`, `MkdirRecursive` | `gdal_vsi_copy`, `gdal_vsi_delete`, `gdal_vsi_mkdir` | Implemented | |
| Synchronisation and seek-optimised ZIP | `gdal vsi sync`, `gdal vsi sozip` | `Algorithm` | `gdal_algorithm_run` | Via algorithm | |
| Object metadata | | `Gdal.GetFileMetadata` | `gdal_vsi_file_metadata` | Implemented | |
| Filesystem options | | `Gdal.GetFileSystemOptions` | `gdal_vsi_filesystems` | Implemented | |
| Configuration options | `--config` | `Gdal.SetConfigOption`, `SetPathSpecificOption` | `gdal_config_get`, `gdal_config_set` | Implemented | Options that load code or relocate data are protected. |
| Unified `gdal` command | `gdal` | `AlgorithmRegistry`, `Algorithm`, `AlgorithmArg` | `gdal_algorithm_list`, `gdal_algorithm_describe`, `gdal_algorithm_run` | Implemented | Pipelines are unavailable while allowed roots are configured. |
