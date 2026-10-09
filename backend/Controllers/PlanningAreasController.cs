using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class PlanningAreasController : ControllerBase
{
    private readonly NavAidDbContext _db;
    public PlanningAreasController(NavAidDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var areas = await _db.PlanningAreas
            .Select(a => new { a.Id, a.Name, a.Region })
            .ToListAsync();
        return Ok(areas);
    }
    [HttpGet("geojson")]
    public async Task<IActionResult> GetGeoJson()
    {
        var areas = await _db.PlanningAreas
            .Include(a => a.Population)
            .ToListAsync();

        var features = areas.Select(a => new
        {
            type = "Feature",
            geometry = a.Geom,
            properties = new
            {
                id = a.Id,
                name = a.Name,
                region = a.Region,
                population = a.Population?.TotalPopulation,
                pct65AndAbove = a.Population?.Pct65AndAbove
            }
        });

        return Ok(new { type = "FeatureCollection", features });
    }
}