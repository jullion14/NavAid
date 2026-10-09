using NetTopologySuite.Geometries;

public class PlanningArea
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Region { get; set; }
    public MultiPolygon Geom { get; set; } = null!;
}