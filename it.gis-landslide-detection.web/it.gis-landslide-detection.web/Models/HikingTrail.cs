using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace it.gis_landslide_detection.web.Models
{
    [Table("hiking_trails")] 
    public class HikingTrail
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("osm_id")]
        public long? OsmId { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("sac_scale")]
        public string? SacScale { get; set; }

        [Column("geom", TypeName = "geometry")]
        public Geometry? Geom { get; set; }
    }
}