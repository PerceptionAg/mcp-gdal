# InFarm.Mcp.Gdal

InFarm.Mcp.Gdal is a [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that gives AI agents access to GDAL and OGR. Agents can inspect, convert, reproject and analyse raster and vector data, work with coordinate reference systems, and read data from archives and cloud storage.

The server is a .NET 10 tool. It bundles its own GDAL 3.13 native libraries through [MaxRev.Gdal.Universal](https://github.com/MaxRev-Dev/gdal.netcore), so you do not need to install GDAL, Python or any GDAL command-line utilities. The server communicates over standard I/O and runs every operation through the GDAL C# bindings inside its own process.

<!-- mcp-name: io.infarm/mcp-gdal -->

## Contents

- [What the server can do](#what-the-server-can-do)
- [Installation](#installation)
- [Configuring MCP clients](#configuring-mcp-clients)
- [Tool reference](#tool-reference)
- [Example operations](#example-operations)
- [Supported drivers](#supported-drivers)
- [Configuration](#configuration)
- [Security](#security)
- [Cross-platform support](#cross-platform-support)
- [Building, testing and packaging](#building-testing-and-packaging)
- [Known limitations](#known-limitations)

## What the server can do

The server exposes 93 tools. Each tool has a typed schema with parameter descriptions and returns structured JSON.

- **Dataset inspection** is equivalent to `gdalinfo` and `ogrinfo`. It reports drivers, metadata domains, subdatasets, bands, NoData values, geotransforms, coordinate reference systems, statistics, histograms, overviews, masks, color tables, raster attribute tables, ground control points, layers, schemas, feature counts and extents.
- **Raster processing** wraps the native utilities `gdal_translate`, `gdalwarp`, `gdalbuildvrt`, `gdaladdo`, `gdal_edit`, `gdal_merge`, `gdal_calc`, `gdal_rasterize`, `gdal_contour`, `gdaldem`, `gdal_fillnodata`, `gdal_sieve`, `gdal_polygonize`, `gdal_proximity`, `gdal_retile`, `gdal_viewshed`, `gdal_grid`, `nearblack`, `gdal_footprint` and `gdaltindex`. It also writes Cloud Optimized GeoTIFFs.
- **Vector processing** covers `ogr2ogr` conversion with filters, SQL and reprojection, feature reading with paging, feature insert, update and delete, schema editing, buffering, overlay (intersection, union, difference, symmetric difference, identity, update and clip), dissolve, simplification, validation, repair, merging (`ogrmerge`), tile indexes (`ogrtindex`) and spatial indexes.
- **Coordinate reference systems** cover parsing of EPSG codes, WKT, PROJ strings and PROJJSON, database searches, comparison, point and bounding box transformation with explicit axis order, UTM zone lookup, and control over the PROJ coordinate operation.
- **Virtual filesystems** let every tool read `/vsimem/`, `/vsizip/`, `/vsitar/`, `/vsigzip/`, `/vsicurl/`, `/vsis3/`, `/vsigs/`, `/vsiaz/` and the other GDAL filesystems. Separate tools list, copy, move and delete files on these filesystems.
- **The unified `gdal` command** of GDAL 3.11 and later is available through `gdal_algorithm_list`, `gdal_algorithm_describe` and `gdal_algorithm_run`. These three tools give access to more than 130 algorithms, such as `raster zonal-stats`, `raster reclassify`, `vector grid`, `vector partition` and `vsi sync`, that have no dedicated tool.

[docs/CapabilityMatrix.md](docs/CapabilityMatrix.md) maps each GDAL capability to its C# API, its MCP tool and its limitations.

## Installation

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later. The SDK provides both the `dnx` command and the `dotnet tool` command.

### Running with dnx

`dnx` downloads the package on first use, caches it, and starts the server:

```bash
dnx InFarm.Mcp.Gdal --yes
```

The `--yes` option skips the confirmation prompt, which an MCP client cannot answer. Pin a version with `InFarm.Mcp.Gdal@0.1.0`. Arguments that are meant for the server follow `--`, for example `dnx InFarm.Mcp.Gdal --yes -- --read-only`.

### Installing as a global tool

```bash
dotnet tool install --global InFarm.Mcp.Gdal
infarm-mcp-gdal --help
```

Update the tool with `dotnet tool update --global InFarm.Mcp.Gdal`.

### Using a package that is not on nuget.org

Until the package is published, or when you test a local build, add the folder that holds the `.nupkg` files as a source:

```bash
dnx InFarm.Mcp.Gdal@0.1.0 --yes --add-source ./artifacts/packages
dotnet tool install --global InFarm.Mcp.Gdal --add-source ./artifacts/packages
```

The package is a .NET 10 RID-specific tool. The top-level package `InFarm.Mcp.Gdal` points to one package for each platform, such as `InFarm.Mcp.Gdal.linux-x64`, and the .NET CLI downloads only the package for the current platform. Each platform package holds about 40 to 55 MB of native GDAL, PROJ and GEOS libraries.

## Configuring MCP clients

### Claude Code

Add the server from the command line:

```bash
claude mcp add gdal -- dnx InFarm.Mcp.Gdal --yes
```

To share the server with everyone who works on a repository, add it to `.mcp.json` in the repository root instead:

```json
{
  "mcpServers": {
    "gdal": {
      "type": "stdio",
      "command": "dnx",
      "args": ["InFarm.Mcp.Gdal", "--yes", "--", "--allowed-roots", "/data/projects"]
    }
  }
}
```

### Claude Desktop

Open the configuration file from **Settings > Developer > Edit Config**. The file is `claude_desktop_config.json` in `%APPDATA%\Claude` on Windows and in `~/Library/Application Support/Claude` on macOS. Add the server and restart Claude Desktop:

```json
{
  "mcpServers": {
    "gdal": {
      "command": "dnx",
      "args": ["InFarm.Mcp.Gdal", "--yes"],
      "env": {
        "INFARM_MCP_GDAL_ALLOWEDROOTS": "C:\\GIS;D:\\Imagery"
      }
    }
  }
}
```

Claude Desktop does not always inherit the `PATH` of a terminal. If it reports that `dnx` cannot be found, use the full path, which is usually `C:\\Program Files\\dotnet\\dnx.cmd` on Windows and `/usr/local/share/dotnet/dnx` on macOS.

### Other MCP clients

Any client that supports the stdio transport can start the server with the command `dnx` and the arguments `InFarm.Mcp.Gdal --yes`, or with the command `infarm-mcp-gdal` after a global installation. For example, Visual Studio Code reads `.vscode/mcp.json`:

```json
{
  "servers": {
    "gdal": {
      "type": "stdio",
      "command": "dnx",
      "args": ["InFarm.Mcp.Gdal", "--yes"]
    }
  }
}
```

## Tool reference

Every tool name starts with `gdal_`. Tools that write data create a new output and refuse to replace an existing one unless the call passes `overwrite=true`. Tools that modify a dataset in place say so in their description.

| Area | Tools |
| --- | --- |
| Server | `gdal_server_info`, `gdal_config_get`, `gdal_config_set` |
| Drivers | `gdal_driver_list`, `gdal_driver_info` |
| Datasets | `gdal_dataset_info`, `gdal_dataset_metadata`, `gdal_dataset_set_metadata`, `gdal_dataset_subdatasets`, `gdal_dataset_identify`, `gdal_dataset_copy`, `gdal_dataset_delete` |
| Raster inspection | `gdal_raster_info`, `gdal_raster_info_json`, `gdal_raster_statistics`, `gdal_raster_histogram`, `gdal_raster_pixel_values`, `gdal_raster_read_window`, `gdal_raster_color_table`, `gdal_raster_attribute_table`, `gdal_raster_gcps` |
| Raster conversion | `gdal_raster_translate`, `gdal_raster_warp`, `gdal_raster_crop`, `gdal_raster_cog`, `gdal_raster_create`, `gdal_raster_align` |
| Raster editing | `gdal_raster_edit`, `gdal_raster_overviews` |
| Mosaics and tiles | `gdal_raster_build_vrt`, `gdal_raster_mosaic`, `gdal_raster_retile`, `gdal_raster_tile_index` |
| Raster analysis | `gdal_raster_calculate`, `gdal_raster_dem`, `gdal_raster_contours`, `gdal_raster_polygonize`, `gdal_raster_rasterize`, `gdal_raster_proximity`, `gdal_raster_sieve`, `gdal_raster_fill_nodata`, `gdal_raster_viewshed`, `gdal_raster_grid`, `gdal_raster_nearblack`, `gdal_raster_footprint` |
| Multidimensional | `gdal_mdim_info`, `gdal_mdim_translate` |
| Vector inspection | `gdal_vector_info`, `gdal_vector_info_json`, `gdal_vector_features`, `gdal_vector_feature_count`, `gdal_vector_sql` |
| Vector editing | `gdal_vector_create`, `gdal_vector_layer_create`, `gdal_vector_layer_delete`, `gdal_vector_layer_rename`, `gdal_vector_field_create`, `gdal_vector_field_alter`, `gdal_vector_field_delete`, `gdal_vector_geometry_field`, `gdal_vector_features_write`, `gdal_vector_features_delete` |
| Vector processing | `gdal_vector_convert`, `gdal_vector_reproject`, `gdal_vector_filter`, `gdal_vector_clip`, `gdal_vector_buffer`, `gdal_vector_geometry_process`, `gdal_vector_overlay`, `gdal_vector_dissolve`, `gdal_vector_validate`, `gdal_vector_repair`, `gdal_vector_merge`, `gdal_vector_tile_index`, `gdal_vector_spatial_index` |
| Geometry | `gdal_geometry_info`, `gdal_geometry_operation`, `gdal_geometry_relate` |
| Coordinate systems | `gdal_crs_info`, `gdal_crs_lookup`, `gdal_crs_compare`, `gdal_crs_transform_points`, `gdal_crs_transform_bounds`, `gdal_crs_utm_zone` |
| Virtual filesystems | `gdal_vsi_list`, `gdal_vsi_filesystems`, `gdal_vsi_file_metadata`, `gdal_vsi_copy`, `gdal_vsi_delete`, `gdal_vsi_mkdir` |
| Unified `gdal` command | `gdal_algorithm_list`, `gdal_algorithm_describe`, `gdal_algorithm_run` |

### Advanced options

The tools expose the common options of each utility as typed parameters. Three mechanisms keep the remaining GDAL options available:

- Parameters named `creationOptions`, `layerCreationOptions`, `openOptions`, `warpOptions` and `transformerOptions` take `NAME=VALUE` strings. `gdal_driver_info` lists the options that each driver accepts, with their types, defaults and allowed values.
- The utility tools accept `additionalArguments`, which holds extra utility switches with one token per array element, for example `["-tap", "-wo", "CUTLINE_ALL_TOUCHED=TRUE"]`. Switches that change process-wide state, such as `--config` and `--optfile`, are rejected.
- `gdal_algorithm_run` runs any algorithm of the unified `gdal` command with arguments that are passed by their long names.

### Coordinate order

Coordinates use x = longitude or easting and y = latitude or northing everywhere, even for systems such as EPSG:4326 whose authority definition lists latitude first. `gdal_crs_transform_points` and `gdal_crs_transform_bounds` accept `axisOrder: "authority"` to switch to the authority order, and every transformation result states the axis order that it used. `gdal_crs_info` reports the authority axis order of a system.

### Large datasets and response limits

Raster processing runs inside GDAL, which streams the data block by block, so the size of a raster is limited only by disk space and the GTiff BigTIFF limits. The server never returns whole datasets. Feature queries are paged with `offset`, `limit` and `nextOffset`. A geometry whose text exceeds the configured length is replaced by its envelope. Window reads are limited to a configurable number of pixels and can be resampled to a smaller grid. Long-running tools report progress and stop when the client cancels the request. When a tool fails or is cancelled, the server deletes the partial output.

## Example operations

The examples below show requests that an agent might make, with the tool calls that answer them.

**Reproject a GeoTIFF and write it as a Cloud Optimized GeoTIFF**

```json
{ "tool": "gdal_raster_cog",
  "arguments": { "input": "/data/ortho.tif", "output": "/data/ortho_cog.tif", "targetCrs": "EPSG:3857", "compression": "WEBP", "quality": 85 } }
```

**Compute NDVI from two bands of one raster, then threshold it**

```json
{ "tool": "gdal_raster_calculate",
  "arguments": { "inputs": { "S": "/data/sentinel.tif" }, "expressions": ["(S[4] - S[3]) / (S[4] + S[3])"],
                 "output": "/data/ndvi.tif", "outputType": "Float32" } }
```

```json
{ "tool": "gdal_algorithm_run",
  "arguments": { "algorithm": "raster reclassify",
                 "arguments": { "input": "/data/ndvi.tif", "output": "/data/vegetation.tif",
                                "mapping": "[-inf,0.3)=0; [0.3,inf]=1", "output-data-type": "Byte" } } }
```

**Find parcels larger than five hectares near a point**

```json
{ "tool": "gdal_vector_features",
  "arguments": { "path": "/data/parcels.gpkg", "where": "area_ha > 5",
                 "intersects": "POLYGON ((147.0 -36.2, 147.1 -36.2, 147.1 -36.1, 147.0 -36.1, 147.0 -36.2))",
                 "filterCrs": "EPSG:4326", "fields": ["parcel_id", "area_ha"], "geometryFormat": "none" } }
```

**Read a remote Cloud Optimized GeoTIFF without downloading it**

```json
{ "tool": "gdal_raster_pixel_values",
  "arguments": { "path": "/vsicurl/https://example.com/dem_cog.tif", "points": [[147.25, -36.12]], "crs": "EPSG:4326" } }
```

**Compute zonal statistics with an algorithm that has no dedicated tool**

```json
{ "tool": "gdal_algorithm_run",
  "arguments": { "algorithm": "raster zonal-stats",
                 "arguments": { "input": "/data/ndvi.tif", "zones": "/data/paddocks.gpkg", "output": "/data/paddock_ndvi.csv",
                                "stat": ["mean", "min", "max"] } } }
```

## Supported drivers

The bundled build registers about 213 drivers on Windows and 214 on Linux. These include 145 raster drivers and 83 vector drivers. Some drivers support both kinds of data, so the two counts overlap. Call `gdal_driver_list` for the exact list on a machine. Commonly used drivers include:

- Raster drivers such as GTiff, COG, VRT, GTI, PNG, JPEG, JPEG2000 (OpenJPEG), WEBP, netCDF, HDF4, HDF5, Zarr, GRIB, ERDAS Imagine (HFA), ENVI, AAIGrid, MBTiles, GPKG rasters, WMS and WMTS.
- Vector drivers such as GPKG, ESRI Shapefile, GeoJSON, GeoJSONSeq, FlatGeobuf, Parquet, Arrow, CSV, KML, LIBKML, GML, GPX, DXF, OpenFileGDB, SQLite and SpatiaLite, MVT, PMTiles, PostgreSQL and PostGIS, MySQL, OGC API Features and WFS.

The package does not include proprietary SDK drivers such as ECW, MrSID and Esri FileGDB. It also lacks the muparser expression engine, which affects raster expressions as described under [Known limitations](#known-limitations).

## Configuration

The server works without configuration. You can set each option either as a command-line switch or as an environment variable named `INFARM_MCP_GDAL_<NAME>`. Run `infarm-mcp-gdal --help` for the full list.

| Switch | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `--allowed-roots` | `INFARM_MCP_GDAL_ALLOWEDROOTS` | unrestricted | Directories that tools may read and write, separated by `;`. |
| `--read-only` | `INFARM_MCP_GDAL_READONLY` | `false` | Rejects every write outside `/vsimem/`. |
| `--allow-overwrite` | `INFARM_MCP_GDAL_ALLOWOVERWRITE` | `true` | Lets a call replace an existing output when it passes `overwrite=true`. |
| `--allow-remote-access` | `INFARM_MCP_GDAL_ALLOWREMOTEACCESS` | `true` | Allows URLs, cloud filesystems and database connections. |
| `--allowed-drivers` | `INFARM_MCP_GDAL_ALLOWEDDRIVERS` | all | Keeps only the listed drivers registered, for example `GTiff;COG;GPKG;GeoJSON`. |
| `--cache-size` | `INFARM_MCP_GDAL_CACHESIZE` | GDAL default (5% of RAM) | GDAL block cache, for example `1024MB` or `10%`. |
| `--gdal-data` | `INFARM_MCP_GDAL_GDALDATADIRECTORY` | bundled | Replaces the GDAL data directory. |
| `--proj-data` | `INFARM_MCP_GDAL_PROJDATADIRECTORY` | bundled | Adds a PROJ resource directory, for example one that holds `proj-data` grids. |
| `--temp-dir` | `INFARM_MCP_GDAL_TEMPDIRECTORY` | system default | Directory for GDAL temporary files. |
| `--num-threads` | `INFARM_MCP_GDAL_NUMTHREADS` | `ALL_CPUS` | Value of `GDAL_NUM_THREADS`. |
| `--default-feature-count` | `INFARM_MCP_GDAL_DEFAULTFEATURECOUNT` | `50` | Page size when a call gives no limit. |
| `--max-feature-count` | `INFARM_MCP_GDAL_MAXFEATURECOUNT` | `1000` | Largest page, point list or feature batch in one call. |
| `--max-geometry-characters` | `INFARM_MCP_GDAL_MAXGEOMETRYCHARACTERS` | `20000` | Longest geometry text returned for one feature. |
| `--max-response-characters` | `INFARM_MCP_GDAL_MAXRESPONSECHARACTERS` | `400000` | Approximate limit on the size of one response. |
| `--max-window-pixels` | `INFARM_MCP_GDAL_MAXWINDOWPIXELS` | `65536` | Largest pixel grid returned by `gdal_raster_read_window`. |
| `--timeout` | `INFARM_MCP_GDAL_OPERATIONTIMEOUTSECONDS` | `3600` | Timeout of one tool call in seconds. `0` disables it. |
| `--max-concurrent-operations` | `INFARM_MCP_GDAL_MAXCONCURRENTOPERATIONS` | `2` | Number of GDAL operations that run at the same time. |
| `--allow-sql-writes` | `INFARM_MCP_GDAL_ALLOWSQLWRITES` | `false` | Allows SQL statements other than `SELECT` and `WITH`. |
| `--allow-config-changes` | `INFARM_MCP_GDAL_ALLOWCONFIGCHANGES` | `true` | Enables the `gdal_config_set` tool. |
| `--proj-network` | `INFARM_MCP_GDAL_PROJNETWORK` | `false` | Lets PROJ download transformation grids from the PROJ CDN. |
| `--log-level` | `INFARM_MCP_GDAL_LOGLEVEL` | `Information` | Log level. Logs always go to standard error. |

Standard GDAL environment variables, such as `GDAL_HTTP_TIMEOUT`, `AWS_REGION` and `AZURE_STORAGE_ACCOUNT`, also apply because GDAL reads them directly.

### Cloud storage authentication

GDAL authenticates to cloud storage with its usual configuration options. The most reliable way to supply credentials is through environment variables in the configuration of the MCP client, because those values never pass through the conversation:

- For `/vsis3/`, set `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_REGION`, or `AWS_PROFILE` together with a credentials file. Set `AWS_NO_SIGN_REQUEST=YES` for public buckets.
- For `/vsigs/`, set `GOOGLE_APPLICATION_CREDENTIALS`, or `GS_ACCESS_KEY_ID` and `GS_SECRET_ACCESS_KEY`.
- For `/vsiaz/` and `/vsiadls/`, set `AZURE_STORAGE_CONNECTION_STRING`, or `AZURE_STORAGE_ACCOUNT` with `AZURE_STORAGE_ACCESS_KEY` or `AZURE_STORAGE_SAS_TOKEN`.

`gdal_vsi_filesystems` with a prefix such as `/vsis3/` lists every option of that filesystem. An agent can also set options for the session with `gdal_config_set`, optionally limited to one bucket through `pathPrefix`. Values typed into a conversation are visible to the model, however, so environment variables are the better choice for secrets.

## Security

The server reads and writes files with the permissions of the user who runs it. The following safeguards apply:

- **Allowed roots** confine every local path, every path inside an archive and every dataset argument of `gdal_algorithm_run` to the configured directories. The server resolves relative paths, `..` segments and symbolic links before it checks them. While roots are configured, the server also rejects connection strings and unrecognised driver prefixes, because it cannot check them against the roots, and pipeline algorithms, because they embed paths in free text.
- **Read-only mode** rejects every operation that writes outside `/vsimem/`, limits `gdal_algorithm_run` to inspection algorithms, and disables the `.aux.xml` sidecar files that GDAL would otherwise write.
- **Overwrite protection** refuses to replace an existing output unless the call passes `overwrite=true` and the configuration allows it.
- **Remote access** can be disabled, which rejects URLs, `/vsicurl/`, cloud filesystems and database connections.
- **Driver restrictions** deregister every driver that is not on the allow list. Use them when the server processes untrusted files, because GDAL parses many complex formats and not every driver has been hardened against hostile input.
- **SQL** accepts only `SELECT` and `WITH` statements by default. The server always rejects statement chaining, `ATTACH`, `PRAGMA`, `load_extension` and other SQLite functions that can reach outside the dataset.
- **Configuration options** that load code or relocate data, such as `GDAL_DRIVER_PATH` and `GDAL_VRT_ENABLE_PYTHON`, are protected, and the server disables Python pixel functions in VRT files. `--config` and `--optfile` are rejected in additional utility arguments.
- **Standard streams** are reserved for the MCP protocol, so `/vsistdin/` and `/vsistdout/` are never available, and all logging goes to standard error.
- **Credentials** are masked in error messages, in logs and in `gdal_config_get` results. The server masks URL user information, signed URL parameters and password parameters in connection strings.

These safeguards limit what an agent can do through the server. They do not replace operating system permissions, so run the server as a user whose file access matches what the agent should be allowed to do.

## Cross-platform support

The package contains native builds for five platforms. The status of each platform reflects the testing done for version 0.1.0.

| Platform | Status |
| --- | --- |
| Windows x64 | Tested. The full test suite passes, and the tool runs after installation with `dotnet tool install` and with `dnx`. |
| Linux x64 | Tested in the `mcr.microsoft.com/dotnet/sdk:10.0` container. The packaged tool runs through `dnx` and answers MCP requests, including PROJ transformations and raster creation. The full test suite has not been run on Linux yet. |
| Linux ARM64 | Packaged but not tested. |
| macOS x64 and ARM64 | Packaged but not tested. |

The GitHub Actions workflow in `.github/workflows/ci.yml` runs the tests and an installed-package smoke test on Windows, Linux x64, Linux ARM64 and macOS ARM64.

Some platform details are worth knowing:

- The Linux libraries require glibc 2.35 or later, which means Ubuntu 22.04, Debian 12 or newer. Alpine and other musl-based distributions are not supported.
- The macOS libraries are not code-signed. NuGet extraction does not set the quarantine attribute, but files copied from a browser download can be blocked by Gatekeeper.
- Windows ARM64 has no native build. The .NET CLI reports that no package is available for that platform.

## Building, testing and packaging

The solution is in `src/InFarm.Mcp.Gdal`.

```bash
cd src/InFarm.Mcp.Gdal
dotnet build InFarm.Mcp.Gdal.slnx
dotnet test InFarm.Mcp.Gdal.slnx
```

The tests create their datasets programmatically and need no network access. `McpProtocolTests` starts the built server as a separate process and talks to it with the MCP SDK client, so these tests cover tool discovery, structured results, error results and cancellation over the real stdio transport.

To run the server from source, use:

```bash
dotnet run --project InFarm.Mcp.Gdal -- --log-level Debug
```

To create the packages, run:

```bash
dotnet pack InFarm.Mcp.Gdal/InFarm.Mcp.Gdal.csproj -c Release
```

The packages are written to `src/InFarm.Mcp.Gdal/artifacts/packages`. The command produces the pointer package and one package for each runtime identifier, and it can produce all of them on any operating system because the tool is framework-dependent.

### Publishing to NuGet

The workflow publishes to nuget.org with [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so the repository stores no NuGet API key. A release runs when a tag such as `v0.1.0` is pushed. The workflow checks that the tag matches the package version, runs the tests on every platform, tests the installed tool, and then waits for approval in the `nuget` environment. After approval, it pushes the five platform packages, then the top-level package, and finally creates a GitHub release with the packages attached.

The one-time setup has three steps:

1. On nuget.org, open **Trusted Publishing** from the account menu and add a policy with the repository owner `PerceptionAg`, the repository `mcp-gdal`, the workflow file `ci.yml` and the environment `nuget`.
2. In the GitHub repository, open **Settings > Environments**, create an environment called `nuget`, and add yourself as a required reviewer.
3. In that environment, add the secret `NUGET_USER` with your nuget.org user name, not your email address.

Each release then takes three steps:

1. Set the same version in `<Version>` in `InFarm.Mcp.Gdal.csproj` and in both `version` fields of `.mcp/server.json`.
2. Commit the change, then run `git tag v0.2.0` and `git push origin v0.2.0`.
3. Approve the **Publish to nuget.org** job in the Actions tab when it asks.

nuget.org validates new packages for a few minutes before `dnx` can install them. A published version cannot be replaced, so a fix needs a new version number.

## Known limitations

- **No raster comparisons or conditionals.** The bundled GDAL build has no muparser, so `gdal raster calc`, the comparison and logical operators of the band algebra, and `IfThenElse` are unavailable. `gdal_raster_calculate` therefore evaluates arithmetic expressions only. Use the `raster reclassify` algorithm for thresholds and classes.
- **Some utilities have no C# binding.** `gdal_pansharpen`, `gdallocationinfo`, `gdalcompare`, `gdal2tiles`, `gdal2xyz` and `ogrlineref` have no direct binding. The server covers them through `gdal_algorithm_run` (`raster pansharpen`, `raster pixel-info`, `raster compare`, `raster tile`) or through `gdal_raster_pixel_values`. It does not replace `ogrlineref`.
- **No coordinate operation listing.** The bindings do not expose PROJ's list of candidate coordinate operations with their accuracies. `gdal_crs_transform_points` lets the caller constrain the choice with an area of interest, a desired accuracy, `onlyBest` or an explicit PROJ pipeline instead.
- **Grid-based datum shifts need grids.** The package contains `proj.db` but no transformation grids. Enable `--proj-network`, or point `--proj-data` at a local copy of `proj-data`, for the most accurate datum and geoid transformations.
- **Single-threaded native code.** GDAL objects are not thread-safe, so every operation opens its own datasets, and the number of operations that run at the same time is limited by `--max-concurrent-operations`. A cancelled operation stops at the next progress callback, so a GDAL routine that reports no progress runs to completion before it notices the cancellation.
- **Spatial indexes for two formats only.** `gdal_vector_spatial_index` supports ESRI Shapefile and GeoPackage. Database formats create indexes through SQL, which needs `--allow-sql-writes`.
