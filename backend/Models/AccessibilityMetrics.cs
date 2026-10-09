namespace NavAid.Api.Models
{
    public class AccessibilityMetrics
    {
        public int PlanningAreaId { get; set; }
        public string Name { get; set; } = null!;
        public string? Region { get; set; }

        public double RepPointLat { get; set; }
        public double RepPointLng { get; set; }
        public double? NearestFacilityLat { get; set; }
        public double? NearestFacilityLng { get; set; }
        public double? NearestMrtLat { get; set; }
        public double? NearestMrtLng { get; set; }

        public int? Population { get; set; }
        public double AreaSqKm { get; set; }
        public double? PopulationDensity { get; set; }   // residents per km²

        public int GpCount { get; set; }
        public int PolyclinicCount { get; set; }
        public int TotalFacilities { get; set; }
        public double? FacilitiesPer10k { get; set; }

        public double? NearestFacilityMeters { get; set; }
        public string? NearestFacilityName { get; set; }
        public string? NearestFacilityType { get; set; }

        public int MrtExitCount { get; set; }
        public double? NearestMrtMeters { get; set; }
        public string? NearestMrtStation { get; set; }
        public int BusStopCount { get; set; }
        public int WellServedBusStops { get; set; }
        public int? BusiestStopServices { get; set; }
    }
}
