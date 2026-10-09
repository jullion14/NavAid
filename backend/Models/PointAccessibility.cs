namespace NavAid.Api.Models;

/// <summary>
/// Accessibility at an arbitrary point, as opposed to a whole planning area.
///
/// SCOPE NOTE: this is a query tool, not a scoring input. The priority score
/// remains area-level and deterministic. Point queries let a user probe
/// variation *within* an area — which is exactly the variation the
/// single-representative-point approach cannot capture — but they never feed
/// back into MetricCatalog or the ranking.
/// </summary>
public sealed class PointAccessibility
{
    public required double Lat { get; init; }
    public required double Lng { get; init; }

    /// <summary>Planning area containing the point, if any. Null over water or outside Singapore.</summary>
    public int? PlanningAreaId { get; init; }
    public string? PlanningAreaName { get; init; }
    public string? Region { get; init; }

    /// <summary>How far the point sits from the area's representative point, for comparison.</summary>
    public double? MetresFromAreaRepPoint { get; init; }

    public double? NearestFacilityMeters { get; init; }
    public string? NearestFacilityName { get; init; }
    public string? NearestFacilityType { get; init; }
    public double? NearestFacilityLat { get; init; }
    public double? NearestFacilityLng { get; init; }

    public double? NearestMrtMeters { get; init; }
    public string? NearestMrtStation { get; init; }
    public double? NearestMrtLat { get; init; }
    public double? NearestMrtLng { get; init; }

    public double? NearestBusStopMeters { get; init; }
    public string? NearestBusStopDescription { get; init; }
    public int? NearestBusStopServices { get; init; }
    public double? NearestBusStopLat { get; init; }
    public double? NearestBusStopLng { get; init; }
}