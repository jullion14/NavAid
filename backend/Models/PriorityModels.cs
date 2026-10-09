using System.Text.Json.Serialization;

namespace NavAid.Api.Models;

/// <summary>
/// Whether a higher raw value pushes an area UP the priority list (Benefit)
/// or DOWN it (Cost). Standard MCDA terminology.
///
/// NOTE ON FRAMING: this score models "priority for intervention", not
/// "accessibility". A HIGH score means an area is comparatively worse off.
/// So distance-to-facility is a Benefit criterion here (farther = higher
/// priority), which reads backwards if you think of it as an accessibility
/// index. Document this framing in the report.
/// </summary>
public enum MetricDirection
{
    Benefit,
    Cost
}


/// <summary>
/// One criterion in the weighted linear combination. The Select delegate is
/// deliberately NOT serialised — the wire format is MetricDescriptor.
/// </summary>
public sealed class MetricDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Unit { get; init; }
    public required MetricDirection Direction { get; init; }
    public required double DefaultWeight { get; init; }
    public required string Rationale { get; init; }

    [JsonIgnore]
    public required Func<AccessibilityMetrics, double?> Select { get; init; }
}

/// <summary>
/// The single source of truth for what goes into the score. The UI renders the
/// formula from this via GET /api/analysis/priority-config — do not restate
/// weights or directions in frontend code, or the "visible formula" stops
/// being the formula that actually ran.
/// </summary>
public static class MetricCatalog
{
    public static readonly IReadOnlyList<MetricDefinition> All = new List<MetricDefinition>
    {
        new()
        {
            Key = "dist_healthcare",
            Label = "Distance to nearest healthcare facility",
            Unit = "m",
            Direction = MetricDirection.Benefit,
            DefaultWeight = 0.35,
            Rationale = "Farther travel to the nearest clinic or polyclinic indicates a larger access gap.",
            Select = m => m.NearestFacilityMeters
        },
        new()
        {
            Key = "pop_density",
            Label = "Population density",
            Unit = "people/km²",
            Direction = MetricDirection.Benefit,
            DefaultWeight = 0.25,
            Rationale = "More residents per km² means any access gap affects more people.",
            Select = m => m.PopulationDensity
        },
        new()
        {
            Key = "facilities_per_10k",
            Label = "Healthcare facilities per 10,000 residents",
            Unit = "per 10k",
            Direction = MetricDirection.Cost,
            DefaultWeight = 0.25,
            Rationale = "Better existing provision lowers the case for further intervention.",
            Select = m => m.FacilitiesPer10k
        },
        new()
        {
            Key = "dist_mrt",
            Label = "Distance to nearest MRT exit",
            Unit = "m",
            Direction = MetricDirection.Benefit,
            DefaultWeight = 0.15,
            Rationale = "Weaker rail access compounds the difficulty of reaching facilities elsewhere.",
            Select = m => m.NearestMrtMeters
        }
    };

    public static MetricDefinition? Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    public static Dictionary<string, double> DefaultWeights() =>
        All.ToDictionary(m => m.Key, m => m.DefaultWeight);
}

// ---------------------------------------------------------------------------
// Wire DTOs
// ---------------------------------------------------------------------------

/// <summary>Serialisable view of a criterion, plus the scale bounds actually observed.</summary>
public sealed class MetricDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Unit { get; init; }
    public required string Direction { get; init; }        // "benefit" | "cost"
    public required double DefaultWeight { get; init; }
    public required string Rationale { get; init; }

    /// <summary>Effective weight after server-side normalisation. Null on the config endpoint.</summary>
    public double? Weight { get; init; }

    /// <summary>Min/max used for rescaling this run. Null on the config endpoint.</summary>
    public double? ObservedMin { get; init; }
    public double? ObservedMax { get; init; }
}

public sealed class PriorityConfigResponse
{
    public required string Method { get; init; }
    public required string Normalisation { get; init; }
    public required string FormulaTemplate { get; init; }
    public required IReadOnlyList<MetricDescriptor> Metrics { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed class PriorityScoreRequest
{
    /// <summary>
    /// Metric key -> weight. Omitted keys fall back to their default weight.
    /// Values need not sum to 1; the server rescales and says so in the response.
    /// </summary>
    public Dictionary<string, double>? Weights { get; init; }
}

public sealed class MetricComponent
{
    public required string Key { get; init; }
    public required double? RawValue { get; init; }
    public required double NormalisedValue { get; init; }
    public required double Weight { get; init; }
    public required double Contribution { get; init; }

    /// <summary>True when RawValue was missing and NormalisedValue fell back to the 0.5 neutral.</summary>
    public required bool IsImputed { get; init; }
}

public sealed class AreaPriorityScore
{
    public required int PlanningAreaId { get; init; }
    public required string Name { get; init; }
    public string? Region { get; init; }
    public required double TotalScore { get; init; }
    public required int Rank { get; init; }
    public required IReadOnlyList<MetricComponent> Components { get; init; }
}

public sealed class PriorityScoreResponse
{
    public required string Method { get; init; }
    public required string Normalisation { get; init; }

    /// <summary>Human-readable formula with the weights that actually ran, e.g. "0.35 × …".</summary>
    public required string Formula { get; init; }

    public required IReadOnlyList<MetricDescriptor> Metrics { get; init; }
    public required IReadOnlyList<AreaPriorityScore> Results { get; init; }

    public required int AreaCount { get; init; }
    public required bool WeightsWereRescaled { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}