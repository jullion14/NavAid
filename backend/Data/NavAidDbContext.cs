using NavAid.Api.Models;
using Microsoft.EntityFrameworkCore;

public class NavAidDbContext : DbContext
{
    public NavAidDbContext(DbContextOptions<NavAidDbContext> options) : base(options) { }

    public DbSet<PlanningArea> PlanningAreas => Set<PlanningArea>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlanningArea>().ToTable("planning_areas");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Id).HasColumnName("id");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Name).HasColumnName("name");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Region).HasColumnName("region");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Geom).HasColumnName("geom");
    }
}