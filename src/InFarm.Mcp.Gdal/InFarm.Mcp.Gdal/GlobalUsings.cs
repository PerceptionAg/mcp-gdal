global using System.ComponentModel;
global using System.Globalization;
global using InFarm.Mcp.Gdal.Configuration;
global using InFarm.Mcp.Gdal.Infrastructure;
global using InFarm.Mcp.Gdal.Models;
global using ModelContextProtocol;
global using ModelContextProtocol.Server;
global using OSGeo.GDAL;
global using OSGeo.OGR;
global using OSGeo.OSR;
// The project namespace ends in "Gdal", so the OSGeo.GDAL.Gdal class needs an alias to be reachable by a simple name.
global using GdalApi = OSGeo.GDAL.Gdal;
global using GdalDriver = OSGeo.GDAL.Driver;
