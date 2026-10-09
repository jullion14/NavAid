namespace NavAid.Api.Models;

public sealed class ExplainRequest
{
    public int PlanningAreaId { get; set; }

    /// <summary>Defaults to Area when omitted.</summary>
    public ExplanationMode Mode { get; set; } = ExplanationMode.Area;

    /// <summary>Required for Compare mode, ignored otherwise.</summary>
    public int? ComparisonAreaId { get; set; }

    public Dictionary<string, double>? Weights { get; set; }
    public string? Model { get; set; }
}