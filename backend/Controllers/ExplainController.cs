using GeoDSS.Api.Models;
using GeoDSS.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeoDSS.Api.Controllers;

/// <summary>
/// HTTP surface for the explanation module.
///
/// This controller does no analysis and no formatting. It validates the
/// request, asks the payload builder for a fact ledger, hands that ledger to
/// the explanation service, and returns the result. POST rather than GET
/// because weight overrides travel in the body, matching SensitivityController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ExplainController : ControllerBase
{
    private readonly IExplanationPayloadBuilder _builder;
    private readonly IAIExplanationService _ai;
    private readonly ILogger<ExplainController> _logger;

    public ExplainController(
        IExplanationPayloadBuilder builder,
        IAIExplanationService ai,
        ILogger<ExplainController> logger)
    {
        _builder = builder;
        _ai = ai;
        _logger = logger;
    }

    /// <summary>Explain the analysis results for a single planning area.</summary>
    [HttpPost("area")]
    [ProducesResponseType(typeof(ExplanationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExplanationResult>> ExplainArea(
        [FromBody] ExplainRequest request,
        CancellationToken ct)
    {
        if (request.PlanningAreaId <= 0)
            return BadRequest(new { message = "A planning area id is required." });

        if (request.Mode == ExplanationMode.Compare && request.ComparisonAreaId is null)
            return BadRequest(new { message = "Compare mode needs a second planning area." });

        var payload = await _builder.BuildAsync(
            request.Mode, request.PlanningAreaId, request.ComparisonAreaId, request.Weights, ct);

        if (payload is null)
            return NotFound(new { message = "One or both planning areas could not be found, or the two are the same area." });

        var result = await _ai.ExplainAsync(payload, request.Model, ct);

        _logger.LogInformation(
            "Explained {Area} from {Source}; {Summary}",
            payload.Subject.Name, result.Source, result.Verification?.Summary);

        return Ok(result);
    }
}