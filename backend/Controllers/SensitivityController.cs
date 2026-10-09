using NavAid.Api.Models;
using NavAid.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace NavAid.Api.Controllers;

[ApiController]
[Route("api/analysis")]
public sealed class SensitivityController : ControllerBase
{
    private readonly ISensitivityService _sensitivity;

    public SensitivityController(ISensitivityService sensitivity) => _sensitivity = sensitivity;

    /// <summary>
    /// Rank stability, tornado effects and an optional weight sweep for the
    /// supplied weights. Side-effect free and safe to repeat; POST only because
    /// the weight set is a structured body.
    /// </summary>
    [HttpPost("sensitivity")]
    [ProducesResponseType(typeof(SensitivityResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SensitivityResponse>> Analyse(
        [FromBody] SensitivityRequest? request,
        CancellationToken ct)
    {
        var result = await _sensitivity.AnalyseAsync(request ?? new SensitivityRequest(), ct);
        return Ok(result);
    }

    /// <summary>Defaults, for smoke tests and the .http file.</summary>
    [HttpGet("sensitivity")]
    [ProducesResponseType(typeof(SensitivityResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SensitivityResponse>> AnalyseWithDefaults(CancellationToken ct)
        => Ok(await _sensitivity.AnalyseAsync(new SensitivityRequest(), ct));
}