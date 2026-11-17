namespace InFarm.Mcp.Gdal.Models;

public enum GeometryFormat
{
    None,
    Wkt,
    Geojson,
    Envelope
}

public enum AxisOrder
{
    // Longitude or easting first, then latitude or northing, whatever the authority defines.
    TraditionalGis,

    // The order that the authority defines, for example latitude then longitude for EPSG:4326.
    Authority
}
