# GDAL MCP server initial implementation

**Date:** 2026-11-18 12:00:00

## Summary

The existing console project `InFarm.Mcp.Gdal` became a Model Context Protocol server that exposes GDAL and OGR to AI agents over standard I/O. The server uses the official C# MCP SDK, bundles GDAL 3.13.3 through `MaxRev.Gdal.Universal`, and is packaged as a RID-specific .NET 10 tool that runs with `dnx` or after `dotnet tool install`. The git repository root moved from `src/InFarm.Mcp.Gdal` to the `GDAL.NET.MCP` folder, so that the documentation, the changelogs and the CI workflow sit inside the repository.

## Changes

- Added 93 MCP tools that cover dataset inspection, drivers, raster conversion, warping, mosaics, terrain analysis, raster algebra, vector conversion, feature editing, geoprocessing, coordinate reference systems, geometry operations, virtual filesystems and the unified `gdal` command.
- Added GDAL initialisation that configures the native libraries once, enables exceptions, routes GDAL messages to the logger on standard error, and captures warnings for each tool call.
- Added `GdalRunner`, which runs each tool call on its own thread with a concurrency limit, a timeout, progress reporting, cancellation through the GDAL progress callback, and translation of native errors into actionable tool errors.
- Added `PathPolicy`, which enforces allowed roots, read-only mode, overwrite protection, remote access restrictions and the exclusion of the standard I/O filesystems.
- Added configuration through command-line switches and `INFARM_MCP_GDAL_*` environment variables.
- Added `gdal_raster_calculate`, which evaluates arithmetic expressions with the native GDAL band algebra, because the bundled build has no muparser for `gdal raster calc`.
- Added the xUnit project `InFarm.Mcp.Gdal.Tests` with 93 tests, including protocol tests that start the server process and use the SDK client.
- Configured NuGet tool packaging with the package type `McpServer`, the command `infarm-mcp-gdal`, five runtime identifiers and `.mcp/server.json`.
- Added the repository README, the capability matrix in `docs/CapabilityMatrix.md`, and the GitHub Actions workflow `.github/workflows/ci.yml`.
- Added the MIT license and set the license, copyright, project URL and repository URL in the package metadata for `github.com/PerceptionAg/mcp-gdal`.
- Set the MCP registry name to `io.infarm/mcp-gdal`. Publishing under that name requires verification of the `infarm.io` domain.
- Changed the release job so that a pushed `v*` tag publishes to nuget.org through trusted publishing after approval in the `nuget` environment. The job checks that the tag matches the package version, pushes the platform packages before the top-level package, and creates a GitHub release.
- Added the GitHub remote `origin` and renamed the local branch from `master` to `main`.

## Notes

- The full test suite passed on Windows x64. The packaged tool was installed and run with `dotnet tool install --tool-path` and with `dnx` on Windows x64, and it was run with `dnx` in the `mcr.microsoft.com/dotnet/sdk:10.0` Linux x64 container. Linux ARM64 and macOS have not been tested yet, and the CI workflow covers them once it runs.
