// Controllers/HealthcareController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class HealthcareController : ControllerBase
{
    private readonly NavAidDbContext _db;
    public HealthcareController(NavAidDbContext db) => _db = db;

    [HttpGet("geojson")]
    public async Task<IActionResult> GetGeoJson([FromQuery] string? type)
    {
        var query = _db.HealthcareFacilities.AsQueryable();

        if (!string.IsNullOrEmpty(type))
            query = query.Where(f => f.FacilityType == type);

        var facilities = await query.ToListAsync();

        var features = facilities.Select(f => new
        {
            type = "Feature",
            geometry = f.Geom,
            properties = new
            {
                id = f.Id,
                name = f.Name,
                facilityType = f.FacilityType,
                address = f.Address,
                postalCode = f.PostalCode
            }
        });

        return Ok(new { type = "FeatureCollection", features });
    }
}