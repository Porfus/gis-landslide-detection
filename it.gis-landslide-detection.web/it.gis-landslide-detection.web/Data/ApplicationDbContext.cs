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
            modelBuilder.Entity<HikingPoint>().ToTable("hiking_points");
            modelBuilder.Entity<HikingPoint>().Property(h => h.Geom)
                .HasColumnType("geometry");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
        }

        public DbSet<HikingPoint> HikingPoints { get; set; }
    }
}
