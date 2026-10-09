using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class BusStopsController : ControllerBase
{
    private readonly NavAidDbContext _db;
    public BusStopsController(NavAidDbContext db) => _db = db;

    [HttpGet("geojson")]
    public async Task<IActionResult> GetGeoJson(
        [FromQuery] int? planningAreaId,
        [FromQuery] int? minServices)
    {
        var query = _db.BusStops.AsNoTracking().AsQueryable();

        if (planningAreaId.HasValue)
            query = query.Where(b => b.PlanningAreaId == planningAreaId);
        if (minServices.HasValue)
            query = query.Where(b => b.ServiceCount >= minServices);

        var stops = await query.ToListAsync();

        var features = stops.Select(b => new
        {
            type = "Feature",
            geometry = b.Geom,
            properties = new
            {
                id = b.Id,
                busStopCode = b.BusStopCode,
                roadName = b.RoadName,
                description = b.Description,
                serviceCount = b.ServiceCount
            }
        });

        return Ok(new { type = "FeatureCollection", features });
    }
}