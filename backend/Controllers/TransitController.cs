// Controllers/TransitController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class TransitController : ControllerBase
{
    private readonly NavAidDbContext _db;
    public TransitController(NavAidDbContext db) => _db = db;

    [HttpGet("geojson")]
    public async Task<IActionResult> GetGeoJson()
    {
        var exits = await _db.TransitExits.ToListAsync();

        var features = exits.Select(e => new
        {
            type = "Feature",
            geometry = e.Geom,
            properties = new
            {
                id = e.Id,
                stationName = e.StationName,
                exitCode = e.ExitCode
            }
        });

        return Ok(new { type = "FeatureCollection", features });
    }
}