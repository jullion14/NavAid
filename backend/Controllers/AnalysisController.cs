using NavAid.Api.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly SpatialAnalysisService _analysis;
    public AnalysisController(SpatialAnalysisService analysis) => _analysis = analysis;

    [HttpGet("area/{id:int}")]
    public async Task<IActionResult> GetAreaMetrics(int id)
    {
        var metrics = await _analysis.GetForAreaAsync(id);
        return metrics is null ? NotFound() : Ok(metrics);
    }

    [HttpGet("areas")]
    public async Task<IActionResult> GetAllMetrics()
        => Ok(await _analysis.GetAllAsync());

    [HttpGet("point")]
    public async Task<ActionResult<PointAccessibility>> GetForPoint(
    [FromQuery] double lat, [FromQuery] double lng)
    {
        var result = await _analysis.GetForPointAsync(lat, lng);
        return result is null
            ? NotFound(new { message = "Point is outside the Singapore data extent." })
            : Ok(result);
    }
}