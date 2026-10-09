using NavAid.Api.Models;

namespace NavAid.Api.Services;

public interface IPriorityScoringService
{
    PriorityConfigResponse GetConfig();
    Task<PriorityScoreResponse> ScoreAsync(PriorityScoreRequest request, CancellationToken ct = default);
}

/// <summary>
/// Weighted Linear Combination (WLC) over min-max normalised criteria.
///
/// All arithmetic happens here, deterministically, in one pass over ~25 rows.
/// Nothing is cached: at this size recomputing is free, and a stale
/// normalisation cache would be one more thing to explain when someone asks
/// why two runs disagree.
/// </summary>
public sealed class PriorityScoringService : IPriorityScoringService
{
    private const string MethodName = "Weighted Linear Combination (WLC)";
    private const string NormalisationName = "min-max rescaling to [0,1]";

    /// <summary>Fallback when a raw value is missing, or when every area shares the same value.</summary>
    private const double NeutralNormalised = 0.5;

    private readonly SpatialAnalysisService _analysis;
    private readonly ILogger<PriorityScoringService> _logger;

    public PriorityScoringService(
        SpatialAnalysisService analysis,
        ILogger<PriorityScoringService> logger)
    {
        _analysis = analysis;
        _logger = logger;
    }

    public PriorityConfigResponse GetConfig() => new()
    {
        Method = MethodName,
        Normalisation = NormalisationName,
        FormulaTemplate = "score = Σ wᵢ × normalise(metricᵢ),  where Σ wᵢ = 1 and normalise(x) = (x − min) / (max − min)",
        Metrics = MetricCatalog.All.Select(m => new MetricDescriptor
        {
            Key = m.Key,
            Label = m.Label,
            Unit = m.Unit,
            Direction = m.Direction == MetricDirection.Benefit ? "benefit" : "cost",
            DefaultWeight = m.DefaultWeight,
            Rationale = m.Rationale
        }).ToList(),
        Notes = new[]
        {
            "A higher score means higher priority for intervention, not better accessibility.",
            "Cost criteria are inverted after rescaling: norm = 1 − (x − min) / (max − min).",
            "Scores are relative to the set of areas scored, so they are not comparable across different area sets.",
            "Default weights are set by judgement, not derived from a formal elicitation method."
        }
    };

    public async Task<PriorityScoreResponse> ScoreAsync(
        PriorityScoreRequest request,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var areas = await LoadAreaMetricsAsync(ct);
        if (areas.Count == 0)
        {
            return Empty(warnings, "No planning areas with population data were returned.");
        }

        var (weights, rescaled, weightWarnings) = ResolveWeights(request.Weights);
        warnings.AddRange(weightWarnings);

        // --- Pass 1: observed range per metric (shared with sensitivity) -----
        var bounds = ScoringCore.ComputeBounds(areas);

        foreach (var metric in MetricCatalog.All)
        {
            var present = areas.Count(a => ScoringCore.IsUsable(metric.Select(a)));
            var b = bounds[metric.Key];

            if (present == 0)
            {
                warnings.Add($"No values available for '{metric.Label}'; every area was assigned the neutral {ScoringCore.NeutralNormalised:0.##} for this criterion.");
                continue;
            }

            if (Math.Abs(b.Max - b.Min) < double.Epsilon)
            {
                warnings.Add($"All areas share the same value for '{metric.Label}'; it cannot separate them and contributes {ScoringCore.NeutralNormalised:0.##} everywhere.");
            }

            var missing = areas.Count - present;
            if (missing > 0)
            {
                warnings.Add($"{missing} area(s) are missing '{metric.Label}'. They were assigned the neutral {ScoringCore.NeutralNormalised:0.##} rather than dropped.");
            }
        }

        // --- Pass 2: normalise, weight, sum ---------------------------------
        var scored = new List<AreaPriorityScore>(areas.Count);

        foreach (var area in areas)
        {
            var components = new List<MetricComponent>(MetricCatalog.All.Count);
            double total = 0;

            foreach (var metric in MetricCatalog.All)
            {
                var raw = metric.Select(area);
                var weight = weights[metric.Key];

                var isImputed = !ScoringCore.IsUsable(raw);
                var normalised = isImputed
                    ? ScoringCore.NeutralNormalised
                    : ScoringCore.Normalise(raw!.Value, bounds[metric.Key], metric.Direction);

                var contribution = weight * normalised;
                total += contribution;

                components.Add(new MetricComponent
                {
                    Key = metric.Key,
                    RawValue = isImputed ? null : raw,
                    NormalisedValue = Round(normalised),
                    Weight = Round(weight),
                    Contribution = Round(contribution),
                    IsImputed = isImputed
                });
            }

            scored.Add(new AreaPriorityScore
            {
                PlanningAreaId = area.PlanningAreaId,
                Name = area.Name,
                Region = area.Region,
                TotalScore = Round(total),
                Rank = 0, // assigned below
                Components = components
            });
        }

        // --- Rank via ScoringCore so ties resolve identically everywhere ----
        var rankVector = ScoringCore.Rank(scored.Select(s => s.TotalScore).ToArray());

        var results = scored
            .Select((s, i) => new AreaPriorityScore
            {
                PlanningAreaId = s.PlanningAreaId,
                Name = s.Name,
                Region = s.Region,
                TotalScore = s.TotalScore,
                Rank = rankVector[i],
                Components = s.Components
            })
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var descriptors = MetricCatalog.All.Select(m => new MetricDescriptor
        {
            Key = m.Key,
            Label = m.Label,
            Unit = m.Unit,
            Direction = m.Direction == MetricDirection.Benefit ? "benefit" : "cost",
            DefaultWeight = m.DefaultWeight,
            Rationale = m.Rationale,
            Weight = Round(weights[m.Key]),
            ObservedMin = bounds[m.Key].Min,
            ObservedMax = bounds[m.Key].Max
        }).ToList();

        _logger.LogInformation(
            "Priority score computed over {Count} areas with weights {Weights}",
            results.Count,
            string.Join(", ", weights.Select(kv => $"{kv.Key}={kv.Value:0.###}")));

        return new PriorityScoreResponse
        {
            Method = MethodName,
            Normalisation = NormalisationName,
            Formula = BuildFormula(weights),
            Metrics = descriptors,
            Results = results,
            AreaCount = results.Count,
            WeightsWereRescaled = rescaled,
            Warnings = warnings
        };
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Rescale to [0,1], then invert for cost criteria so that "1" always means
    /// "highest priority" regardless of the metric's natural direction.
    /// Inversion happens after rescaling against the same min/max — there is one
    /// formula path, not two.
    /// </summary>
    private static double Normalise(double value, double min, double max, MetricDirection direction)
    {
        var range = max - min;
        if (Math.Abs(range) < double.Epsilon)
        {
            return NeutralNormalised;
        }

        var scaled = (value - min) / range;
        scaled = Math.Clamp(scaled, 0d, 1d);

        return direction == MetricDirection.Cost ? 1d - scaled : scaled;
    }

    /// <summary>
    /// Fill gaps from defaults, reject negatives, then rescale so the weights sum
    /// to exactly 1. Rescaling rather than rejecting keeps slider UIs simple: the
    /// user moves one slider without having to rebalance the rest by hand.
    /// </summary>
    private static (Dictionary<string, double> Weights, bool Rescaled, List<string> Warnings)
        ResolveWeights(Dictionary<string, double>? supplied)
    {
        var warnings = new List<string>();
        var weights = MetricCatalog.DefaultWeights();

        if (supplied is not null)
        {
            foreach (var (key, value) in supplied)
            {
                var metric = MetricCatalog.Find(key);
                if (metric is null)
                {
                    warnings.Add($"Ignored unknown metric key '{key}'.");
                    continue;
                }

                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                {
                    warnings.Add($"Weight for '{metric.Label}' must be zero or greater; the default {metric.DefaultWeight:0.##} was used instead.");
                    continue;
                }

                weights[metric.Key] = value;
            }
        }

        var sum = weights.Values.Sum();
        if (sum <= 0)
        {
            warnings.Add("All weights were zero, so the defaults were restored.");
            return (MetricCatalog.DefaultWeights(), true, warnings);
        }

        var rescaled = Math.Abs(sum - 1d) > 1e-6;
        if (rescaled)
        {
            foreach (var key in weights.Keys.ToList())
            {
                weights[key] /= sum;
            }
            warnings.Add($"Weights summed to {sum:0.###} and were rescaled to sum to 1. Relative emphasis is unchanged.");
        }

        return (weights, rescaled, warnings);
    }

    private static string BuildFormula(IReadOnlyDictionary<string, double> weights) =>
        "score = " + string.Join(" + ", MetricCatalog.All.Select(m =>
        {
            var body = m.Direction == MetricDirection.Cost
                ? $"(1 − norm[{m.Key}])"
                : $"norm[{m.Key}]";
            return $"{weights[m.Key]:0.##} × {body}";
        }));

    private static double Round(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    private PriorityScoreResponse Empty(List<string> warnings, string reason)
    {
        warnings.Add(reason);
        return new PriorityScoreResponse
        {
            Method = MethodName,
            Normalisation = NormalisationName,
            Formula = BuildFormula(MetricCatalog.DefaultWeights()),
            Metrics = GetConfig().Metrics,
            Results = Array.Empty<AreaPriorityScore>(),
            AreaCount = 0,
            WeightsWereRescaled = false,
            Warnings = warnings
        };
    }

    // -----------------------------------------------------------------------
    // ADAPTER — the one place that touches your existing analysis service.
    //
    // Replace the body with a call to whatever GET-all-metrics method you
    // already have (e.g. _analysis.GetAllAreaMetricsAsync()), mapping your
    // AccessibilityMetrics projection onto AreaMetrics. Keep the filter:
    // areas without population data cannot produce a density or a per-10k
    // rate, and silently scoring them at the neutral 0.5 would put them
    // mid-table for no defensible reason.
    // -----------------------------------------------------------------------
    private async Task<List<AccessibilityMetrics>> LoadAreaMetricsAsync(CancellationToken ct)
    {
        // GetAllAsync() already restricts to areas with a population row
        // (excludes industrial/reserve areas), which is exactly the scoring set.
        return await _analysis.GetAllAsync();
    }
}