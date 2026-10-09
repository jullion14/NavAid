using NavAid.Api.Models;
using NavAid.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NavAid.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly NavAidDbContext _db;
    public SearchController(NavAidDbContext db) => _db = db;

    public record SearchHit(
        string Type, long Id, string Name, string? Subtitle,
        double Lat, double Lng);

    /// <summary>
    /// Cross-dataset name lookup. Each source contributes at most `perType`
    /// rows so one large dataset (5k bus stops) can't crowd out the others.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SearchHit>>> Search(
        [FromQuery] string q,
        [FromQuery] string? types,
        [FromQuery] int perType = 5)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<SearchHit>());

        var term = $"%{q.Trim()}%";
        var want = (types ?? "gp,polyclinic,mrt,bus")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .ToHashSet();

        var hits = new List<SearchHit>();

        if (want.Contains("gp") || want.Contains("polyclinic"))
        {
            var facilities = await _db.HealthcareFacilities
                .Where(f => EF.Functions.ILike(f.Name, term)
                         || EF.Functions.ILike(f.Address ?? "", term))
                .Where(f => want.Contains(f.FacilityType == "Polyclinic" ? "polyclinic" : "gp"))
                .OrderBy(f => f.Name)
                .Take(perType * 2)
                .Select(f => new SearchHit(
                    f.FacilityType == "Polyclinic" ? "polyclinic" : "gp",
                    f.Id, f.Name, f.Address,
                    f.Geom.Y, f.Geom.X))
                .ToListAsync();
            hits.AddRange(facilities);
        }

        if (want.Contains("mrt"))
        {
            hits.AddRange(await _db.TransitExits
                .Where(e => EF.Functions.ILike(e.StationName, term))
                .OrderBy(e => e.StationName).ThenBy(e => e.ExitCode)
                .Take(perType)
                .Select(e => new SearchHit(
                    "mrt", e.Id, e.StationName, e.ExitCode,
                    e.Geom.Y, e.Geom.X))
                .ToListAsync());
        }

        if (want.Contains("bus"))
        {
            hits.AddRange(await _db.BusStops
                .Where(b => EF.Functions.ILike(b.Description, term)
                         || EF.Functions.ILike(b.RoadName ?? "", term)
                         || b.BusStopCode == q.Trim())
                .OrderBy(b => b.Description)
                .Take(perType)
                .Select(b => new SearchHit(
                    "bus", b.Id, b.Description,
                    $"{b.RoadName} · {b.ServiceCount} services",
                    b.Geom.Y, b.Geom.X))
                .ToListAsync());
        }

        return Ok(hits);
    }
}