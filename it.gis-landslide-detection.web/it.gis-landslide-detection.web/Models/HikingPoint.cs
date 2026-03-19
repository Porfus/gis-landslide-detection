using NetTopologySuite.Geometries;

namespace it.gis_landslide_detection.web.Models;

public class HikingPoint
{
    public long Id   { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public Geometry? Geom { get; set; }

}