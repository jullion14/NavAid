using NavAid.Api.Models;
using Microsoft.EntityFrameworkCore;

public class NavAidDbContext : DbContext
{
    public NavAidDbContext(DbContextOptions<NavAidDbContext> options) : base(options) { }

    public DbSet<PlanningArea> PlanningAreas => Set<PlanningArea>();
    public DbSet<Population> Populations => Set<Population>();
    public DbSet<HealthcareFacility> HealthcareFacilities => Set<HealthcareFacility>();
    public DbSet<TransitExit> TransitExits => Set<TransitExit>();
    public DbSet<AccessibilityMetrics> AccessibilityMetrics => Set<AccessibilityMetrics>();
    public DbSet<BusStop> BusStops => Set<BusStop>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlanningArea>().ToTable("planning_areas");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Id).HasColumnName("id");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Name).HasColumnName("name");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Region).HasColumnName("region");
        modelBuilder.Entity<PlanningArea>().Property(p => p.Geom).HasColumnName("geom");

        modelBuilder.Entity<BusStop>().ToTable("bus_stops");
        modelBuilder.Entity<BusStop>().Property(b => b.Id).HasColumnName("id");
        modelBuilder.Entity<BusStop>().Property(b => b.BusStopCode).HasColumnName("bus_stop_code");
        modelBuilder.Entity<BusStop>().Property(b => b.RoadName).HasColumnName("road_name");
        modelBuilder.Entity<BusStop>().Property(b => b.Description).HasColumnName("description");
        modelBuilder.Entity<BusStop>().Property(b => b.ServiceCount).HasColumnName("service_count");
        modelBuilder.Entity<BusStop>().Property(b => b.PlanningAreaId).HasColumnName("planning_area_id");
        modelBuilder.Entity<BusStop>().Property(b => b.Geom).HasColumnName("geom");

        modelBuilder.Entity<Population>().ToTable("population");
        modelBuilder.Entity<Population>().HasKey(p => p.PlanningAreaId);

        modelBuilder.Entity<Population>().Property(p => p.PlanningAreaId).HasColumnName("planning_area_id");
        modelBuilder.Entity<Population>().Property(p => p.Year).HasColumnName("year");
        modelBuilder.Entity<Population>().Property(p => p.TotalPopulation).HasColumnName("total_population");

        modelBuilder.Entity<Population>().Property(p => p.PctBelow15).HasColumnName("pct_below15");
        modelBuilder.Entity<Population>().Property(p => p.Pct15_24).HasColumnName("pct_15_24");
        modelBuilder.Entity<Population>().Property(p => p.Pct25_34).HasColumnName("pct_25_34");
        modelBuilder.Entity<Population>().Property(p => p.Pct35_44).HasColumnName("pct_35_44");
        modelBuilder.Entity<Population>().Property(p => p.Pct45_54).HasColumnName("pct_45_54");
        modelBuilder.Entity<Population>().Property(p => p.Pct55_64).HasColumnName("pct_55_64");
        modelBuilder.Entity<Population>().Property(p => p.Pct65AndAbove).HasColumnName("pct_65andabove");
        modelBuilder.Entity<Population>().Property(p => p.Pct75AndAbove).HasColumnName("pct_75andabove");

        modelBuilder.Entity<Population>().Property(p => p.HdbTotal).HasColumnName("hdb_total");
        modelBuilder.Entity<Population>().Property(p => p.Hdb1_2Room).HasColumnName("hdb_1_2_room");
        modelBuilder.Entity<Population>().Property(p => p.Hdb3Room).HasColumnName("hdb_3_room");
        modelBuilder.Entity<Population>().Property(p => p.Hdb4Room).HasColumnName("hdb_4_room");
        modelBuilder.Entity<Population>().Property(p => p.Hdb5RoomExec).HasColumnName("hdb_5_room_exec");
        modelBuilder.Entity<Population>().Property(p => p.CondoOther).HasColumnName("condo_other");
        modelBuilder.Entity<Population>().Property(p => p.Landed).HasColumnName("landed");
        modelBuilder.Entity<Population>().Property(p => p.DwellingOthers).HasColumnName("dwelling_others");

        modelBuilder.Entity<Population>().Property(p => p.PctHdb).HasColumnName("pct_hdb");
        modelBuilder.Entity<Population>().Property(p => p.PctHdb1_2Room).HasColumnName("pct_hdb_1_2_room");

        modelBuilder.Entity<HealthcareFacility>().ToTable("healthcare_facilities");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.Id).HasColumnName("id");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.Name).HasColumnName("name");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.FacilityType).HasColumnName("facility_type");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.Address).HasColumnName("address");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.PostalCode).HasColumnName("postal_code");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.PlanningAreaId).HasColumnName("planning_area_id");
        modelBuilder.Entity<HealthcareFacility>().Property(p => p.Geom).HasColumnName("geom");

        modelBuilder.Entity<TransitExit>().ToTable("transit_exits");
        modelBuilder.Entity<TransitExit>().Property(p => p.Id).HasColumnName("id");
        modelBuilder.Entity<TransitExit>().Property(p => p.StationName).HasColumnName("station_name");
        modelBuilder.Entity<TransitExit>().Property(p => p.ExitCode).HasColumnName("exit_code");
        modelBuilder.Entity<TransitExit>().Property(p => p.PlanningAreaId).HasColumnName("planning_area_id");
        modelBuilder.Entity<TransitExit>().Property(p => p.Geom).HasColumnName("geom");

        modelBuilder.Entity<AccessibilityMetrics>().HasNoKey().ToView(null);

        modelBuilder.Entity<PointAccessibility>().HasNoKey().ToView(null);
    }
}