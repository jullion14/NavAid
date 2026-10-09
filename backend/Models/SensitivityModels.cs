namespace NavAid.Api.Models;

/// <summary>
/// Rank stability for one area across many sampled weight vectors.
/// </summary>
public sealed class RankStability
{
    public required int PlanningAreaId { get; init; }
    public required string Name { get; init; }

    /// <summary>Rank under the user's current weights — the ranking actually displayed.</summary>
    public required int BaseRank { get; init; }

    public required int BestRank { get; init; }
    public required int WorstRank { get; init; }
    public required double MedianRank { get; init; }

    /// <summary>5th and 95th percentile ranks: the plausible band, excluding extreme samples.</summary>
    public required int P05Rank { get; init; }
    public required int P95Rank { get; init; }

    /// <summary>Share of samples in which the area kept its base rank exactly.</summary>
    public required double RankHeldShare { get; init; }

    /// <summary>
    /// "stable" | "moderate" | "volatile", from the width of the 5–95 band
    /// relative to the number of areas. A plain-language label so the UI does
    /// not have to reimplement the thresholds.
    /// </summary>
    public required string Stability { get; init; }
}

/// <summary>
/// How far one area's rank moves when a single criterion's weight is driven to
/// each extreme, with the other weights renormalised proportionally.
/// </summary>
public sealed class TornadoEffect
{
    public required string MetricKey { get; init; }
    public required string Label { get; init; }

    /// <summary>Rank when this weight goes to zero.</summary>
    public required int RankAtZero { get; init; }

    /// <summary>Rank when this criterion takes the entire weight.</summary>
    public required int RankAtFull { get; init; }

    public required int BestRank { get; init; }
    public required int WorstRank { get; init; }

    /// <summary>Rank positions spanned. The sort key — biggest swing first.</summary>
    public required int Swing { get; init; }
}

public sealed class AreaTornado
{
    public required int PlanningAreaId { get; init; }
    public required string Name { get; init; }
    public required int BaseRank { get; init; }
    public required IReadOnlyList<TornadoEffect> Effects { get; init; }

    /// <summary>Plain-language reading of the dominant driver, for the UI caption.</summary>
    public required string Summary { get; init; }
}

/// <summary>One step of a weight sweep: every area's rank at a given weight value.</summary>
public sealed class SweepStep
{
    public required double Weight { get; init; }
    /// <summary>planningAreaId -> rank at this weight.</summary>
    public required Dictionary<int, int> Ranks { get; init; }
}

public sealed class WeightSweep
{
    public required string MetricKey { get; init; }
    public required string Label { get; init; }
    public required double CurrentWeight { get; init; }
    public required IReadOnlyList<SweepStep> Steps { get; init; }

    /// <summary>Weight values where the leading area changes hands.</summary>
    public required IReadOnlyList<double> LeadChanges { get; init; }
}

public sealed class SensitivityRequest
{
    public Dictionary<string, double>? Weights { get; init; }

    /// <summary>Monte Carlo sample count. Clamped to 100–5000.</summary>
    public int? Samples { get; init; }

    /// <summary>
    /// Concentration for Dirichlet sampling around the current weights. Higher
    /// stays closer to the user's setting; lower explores the simplex more
    /// freely. Clamped to 1–200.
    /// </summary>
    public double? Concentration { get; init; }

    /// <summary>Fixed by default so results are reproducible.</summary>
    public int? Seed { get; init; }

    /// <summary>Optional: return a weight sweep for this criterion too.</summary>
    public string? SweepMetricKey { get; init; }
}

public sealed class SensitivityResponse
{
    public required string Method { get; init; }
    public required int Samples { get; init; }
    public required double Concentration { get; init; }

    /// <summary>Echoed so a reader can reproduce these exact figures.</summary>
    public required int Seed { get; init; }

    public required IReadOnlyList<RankStability> Stability { get; init; }
    public required IReadOnlyList<AreaTornado> Tornado { get; init; }
    public WeightSweep? Sweep { get; init; }

    /// <summary>One-line plain-language read of the overall result.</summary>
    public required string Headline { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}