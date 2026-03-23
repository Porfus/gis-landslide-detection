using it.gis_landslide_detection.web.Models;
using Microsoft.EntityFrameworkCore;

namespace it.gis_landslide_detection.web.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        { }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            
            //modelBuilder.Entity<HikingPoint>().Property(h => h.Geom)
            //    .HasColumnType("geometry");
            modelBuilder.Entity<HikingPoint>().ToTable("hiking_points");
            //base.OnModelCreating(modelBuilder);
            //modelBuilder.HasPostgresExtension("postgis");

            modelBuilder.Entity<HikingPoint>()
                .Property(h => h.Geom)
                .HasColumnType("geometry");

            modelBuilder.Entity<IffiZone>()
                .Property(z => z.Geom)
                .HasColumnType("geometry");

            modelBuilder.Entity<HikingTrail>()
                .Property(t => t.Geom)
                .HasColumnType("geometry");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
        }

        public DbSet<HikingPoint> HikingPoints { get; set; }

        public DbSet<IffiZone> IffiZones { get; set; }

        public DbSet<HikingTrail> HikingTrails { get; set; }
    }
}
