using NavAid.Api.Models;
using NavAid.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace NavAid.Api.Controllers;

/// <summary>
/// Decision-support endpoints. Kept alongside the existing AnalysisController
/// routes (/api/analysis/area/{id}, /api/analysis/areas) rather than folded
/// into it — the spatial metrics and the scoring model change for different
/// reasons and are worth separating.
/// </summary>
[ApiController]
[Route("api/analysis")]
public sealed class PriorityController : ControllerBase
{
    private readonly IPriorityScoringService _scoring;

    public PriorityController(IPriorityScoringService scoring) => _scoring = scoring;

    /// <summary>
    /// The scoring model itself: criteria, directions, default weights, and the
    /// formula template. The UI renders its formula panel from this, so the
    /// documented model and the executed model cannot drift apart.
    /// </summary>
    [HttpGet("priority-config")]
    [ProducesResponseType(typeof(PriorityConfigResponse), StatusCodes.Status200OK)]
    public ActionResult<PriorityConfigResponse> GetConfig() => Ok(_scoring.GetConfig());

    /// <summary>
    /// Score and rank every planning area with population data.
    /// POST rather than GET because the weight set is a structured body, but the
    /// call is side-effect free and safe to repeat.
    /// </summary>
    [HttpPost("priority-score")]
    [ProducesResponseType(typeof(PriorityScoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriorityScoreResponse>> Score(
        [FromBody] PriorityScoreRequest? request,
        CancellationToken ct)
    {
        var result = await _scoring.ScoreAsync(request ?? new PriorityScoreRequest(), ct);
        return Ok(result);
    }

    /// <summary>Convenience GET for the default-weight ranking — handy in the .http file and for smoke tests.</summary>
    [HttpGet("priority-score")]
    [ProducesResponseType(typeof(PriorityScoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriorityScoreResponse>> ScoreWithDefaults(CancellationToken ct)
    {
        var result = await _scoring.ScoreAsync(new PriorityScoreRequest(), ct);
        return Ok(result);
    }
}