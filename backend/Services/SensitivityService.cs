using NavAid.Api.Models;

namespace NavAid.Api.Services;

public interface ISensitivityService
{
    Task<SensitivityResponse> AnalyseAsync(SensitivityRequest request, CancellationToken ct = default);
}

/// <summary>
/// Sensitivity analysis for the priority score.
///
/// Answers three questions the ranking alone cannot:
///   1. How much does each area's rank move under plausible reweighting?
///      (Monte Carlo over the weight simplex -> stability intervals)
///   2. Which criterion drives a given area's position?
///      (one-at-a-time perturbation -> tornado)
///   3. At what weight does the ordering actually flip?
///      (weight sweep -> crossover points)
///
/// ON DETERMINISM: Monte Carlo introduces randomness into a system whose whole
/// premise is being deterministic and auditable. The seed is fixed by default
/// and always echoed in the response, so the same request returns the same
/// intervals and a reader can reproduce any figure quoted in the report.
/// </summary>
public sealed class SensitivityService : ISensitivityService
{
    private const int DefaultSamples = 1000;
    private const double DefaultConcentration = 40.0;
    private const int DefaultSeed = 20260803;
    private const int SweepSteps = 21;

    private readonly SpatialAnalysisService _analysis;
    private readonly ILogger<SensitivityService> _logger;

    public SensitivityService(SpatialAnalysisService analysis, ILogger<SensitivityService> logger)
    {
        _analysis = analysis;
        _logger = logger;
    }

    public async Task<SensitivityResponse> AnalyseAsync(
        SensitivityRequest request,
        CancellationToken ct = default)
    {
        var areas = (await _analysis.GetAllAsync())
            .Where(a => a.Population > 0)
            .ToList();

        var samples = Math.Clamp(request.Samples ?? DefaultSamples, 100, 5000);
        var concentration = Math.Clamp(request.Concentration ?? DefaultConcentration, 1.0, 200.0);
        var seed = request.Seed ?? DefaultSeed;

        if (areas.Count == 0)
        {
            return Empty(samples, concentration, seed, "No areas with population data were available to analyse.");
        }

        var bounds = ScoringCore.ComputeBounds(areas);
        var normalised = ScoringCore.BuildNormalisedMatrix(areas, bounds);

        var baseWeights = ScoringCore.NormaliseWeights(
            ScoringCore.ToVector(request.Weights ?? MetricCatalog.DefaultWeights()));

        var baseRanks = ScoringCore.Rank(ScoringCore.Score(normalised, baseWeights));

        var stability = MonteCarlo(areas, normalised, baseWeights, baseRanks, samples, concentration, seed);
        var tornado = Tornado(areas, normalised, baseWeights, baseRanks);
        var sweep = request.SweepMetricKey is null
            ? null
            : Sweep(areas, normalised, baseWeights, request.SweepMetricKey);

        _logger.LogInformation(
            "Sensitivity: {Areas} areas, {Samples} samples, concentration {C}, seed {Seed}",
            areas.Count, samples, concentration, seed);

        return new SensitivityResponse
        {
            Method = "Dirichlet Monte Carlo over the weight simplex, plus one-at-a-time perturbation",
            Samples = samples,
            Concentration = concentration,
            Seed = seed,
            Stability = stability,
            Tornado = tornado,
            Sweep = sweep,
            Headline = Headline(stability),
            Notes = new[]
            {
                "Stability intervals show where each area landed across the sampled weightings, not how accurate the underlying data is.",
                "A stable rank means the conclusion survives reasonable disagreement about the weights. It does not mean the ranking is correct.",
                "Sampling is seeded, so the same request always returns the same intervals.",
                "Normalisation bounds are fixed across all samples: only the weights vary, never the data."
            }
        };
    }

    // -- 1. Monte Carlo over the weight simplex ------------------------------

    /// <summary>
    /// Samples weight vectors from a Dirichlet distribution centred on the
    /// user's current weights. Unlike one-at-a-time perturbation, this explores
    /// combinations of changes — which is where rankings usually break down.
    /// </summary>
    private static List<RankStability> MonteCarlo(
        IReadOnlyList<AccessibilityMetrics> areas,
        double[,] normalised,
        double[] baseWeights,
        int[] baseRanks,
        int samples,
        double concentration,
        int seed)
    {
        var rng = new Random(seed);
        var metricCount = MetricCatalog.All.Count;
        var observed = new int[areas.Count][];
        for (var a = 0; a < areas.Count; a++) observed[a] = new int[samples];

        var held = new int[areas.Count];
        var alpha = baseWeights.Select(w => Math.Max(w, 1e-4) * concentration).ToArray();
        var draw = new double[metricCount];

        for (var s = 0; s < samples; s++)
        {
            // Dirichlet via normalised Gamma draws.
            double sum = 0;
            for (var m = 0; m < metricCount; m++)
            {
                draw[m] = SampleGamma(rng, alpha[m]);
                sum += draw[m];
            }
            if (sum <= 0) { s--; continue; }
            for (var m = 0; m < metricCount; m++) draw[m] /= sum;

            var ranks = ScoringCore.Rank(ScoringCore.Score(normalised, draw));
            for (var a = 0; a < areas.Count; a++)
            {
                observed[a][s] = ranks[a];
                if (ranks[a] == baseRanks[a]) held[a]++;
            }
        }

        var results = new List<RankStability>(areas.Count);

        for (var a = 0; a < areas.Count; a++)
        {
            var sorted = observed[a].OrderBy(r => r).ToArray();
            var p05 = Percentile(sorted, 0.05);
            var p95 = Percentile(sorted, 0.95);
            var band = p95 - p05;

            results.Add(new RankStability
            {
                PlanningAreaId = areas[a].PlanningAreaId,
                Name = areas[a].Name,
                BaseRank = baseRanks[a],
                BestRank = sorted[0],
                WorstRank = sorted[^1],
                MedianRank = Median(sorted),
                P05Rank = p05,
                P95Rank = p95,
                RankHeldShare = Math.Round((double)held[a] / samples, 4),
                Stability = Classify(band, areas.Count)
            });
        }

        return results.OrderBy(r => r.BaseRank).ToList();
    }

    /// <summary>
    /// Thresholds are proportions of the field, so they hold if the area count
    /// ever changes. Under 10% of positions is tight; over 25% is wide enough
    /// that the displayed rank should not be read as a finding.
    /// </summary>
    private static string Classify(int band, int areaCount)
    {
        var share = (double)band / Math.Max(areaCount - 1, 1);
        if (share <= 0.10) return "stable";
        if (share <= 0.25) return "moderate";
        return "volatile";
    }

    // -- 2. One-at-a-time tornado --------------------------------------------

    /// <summary>
    /// For each criterion, drive its weight to 0 and to 1 while renormalising
    /// the others proportionally, and record where each area lands. This is the
    /// classic tornado input: it isolates one criterion at a time, which is
    /// easy to explain but cannot see interactions — hence the Monte Carlo too.
    /// </summary>
    private static List<AreaTornado> Tornado(
        IReadOnlyList<AccessibilityMetrics> areas,
        double[,] normalised,
        double[] baseWeights,
        int[] baseRanks)
    {
        var metricCount = MetricCatalog.All.Count;
        var atZero = new int[metricCount][];
        var atFull = new int[metricCount][];

        for (var m = 0; m < metricCount; m++)
        {
            atZero[m] = ScoringCore.Rank(ScoringCore.Score(normalised, WithWeight(baseWeights, m, 0.0)));
            atFull[m] = ScoringCore.Rank(ScoringCore.Score(normalised, WithWeight(baseWeights, m, 1.0)));
        }

        var results = new List<AreaTornado>(areas.Count);

        for (var a = 0; a < areas.Count; a++)
        {
            var effects = new List<TornadoEffect>(metricCount);

            for (var m = 0; m < metricCount; m++)
            {
                var zero = atZero[m][a];
                var full = atFull[m][a];
                effects.Add(new TornadoEffect
                {
                    MetricKey = MetricCatalog.All[m].Key,
                    Label = MetricCatalog.All[m].Label,
                    RankAtZero = zero,
                    RankAtFull = full,
                    BestRank = Math.Min(zero, full),
                    WorstRank = Math.Max(zero, full),
                    Swing = Math.Abs(full - zero)
                });
            }

            effects = effects.OrderByDescending(e => e.Swing).ToList();

            results.Add(new AreaTornado
            {
                PlanningAreaId = areas[a].PlanningAreaId,
                Name = areas[a].Name,
                BaseRank = baseRanks[a],
                Effects = effects,
                Summary = Summarise(areas[a].Name, baseRanks[a], effects)
            });
        }

        return results;
    }

    private static string Summarise(string name, int baseRank, IReadOnlyList<TornadoEffect> effects)
    {
        var top = effects[0];
        if (top.Swing == 0)
        {
            return $"{name} holds rank {baseRank} no matter how the weights are set.";
        }

        var second = effects.Count > 1 ? effects[1] : null;
        var dominant = second is null || top.Swing >= second.Swing * 2;

        return dominant
            ? $"{name}'s position depends mostly on how '{top.Label.ToLowerInvariant()}' is weighted — it moves {top.Swing} places across that criterion's range."
            : $"{name}'s position responds to several criteria, most of all '{top.Label.ToLowerInvariant()}' ({top.Swing} places).";
    }

    // -- 3. Weight sweep ------------------------------------------------------

    /// <summary>
    /// Sweeps one weight from 0 to 1 in fixed steps, renormalising the others,
    /// and records every area's rank at each step. Crossover points are where
    /// the decision genuinely changes.
    /// </summary>
    private static WeightSweep? Sweep(
        IReadOnlyList<AccessibilityMetrics> areas,
        double[,] normalised,
        double[] baseWeights,
        string metricKey)
    {
        var index = MetricCatalog.All.ToList().FindIndex(m =>
            string.Equals(m.Key, metricKey, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;

        var steps = new List<SweepStep>(SweepSteps);
        var leadChanges = new List<double>();
        int? previousLeader = null;

        for (var i = 0; i < SweepSteps; i++)
        {
            var w = (double)i / (SweepSteps - 1);
            var ranks = ScoringCore.Rank(ScoringCore.Score(normalised, WithWeight(baseWeights, index, w)));

            var map = new Dictionary<int, int>(areas.Count);
            var leader = -1;
            for (var a = 0; a < areas.Count; a++)
            {
                map[areas[a].PlanningAreaId] = ranks[a];
                if (ranks[a] == 1) leader = areas[a].PlanningAreaId;
            }

            if (previousLeader is not null && leader != previousLeader) leadChanges.Add(Math.Round(w, 3));
            previousLeader = leader;

            steps.Add(new SweepStep { Weight = Math.Round(w, 3), Ranks = map });
        }

        return new WeightSweep
        {
            MetricKey = MetricCatalog.All[index].Key,
            Label = MetricCatalog.All[index].Label,
            CurrentWeight = Math.Round(baseWeights[index], 3),
            Steps = steps,
            LeadChanges = leadChanges
        };
    }

    // -- helpers --------------------------------------------------------------

    /// <summary>
    /// Sets one weight and rescales the rest to fill the remainder, preserving
    /// their relative proportions. Keeps every sampled vector on the simplex.
    /// </summary>
    private static double[] WithWeight(double[] baseWeights, int index, double value)
    {
        var result = new double[baseWeights.Length];
        var othersTotal = baseWeights.Where((_, i) => i != index).Sum();
        var remaining = 1.0 - value;

        for (var i = 0; i < baseWeights.Length; i++)
        {
            if (i == index) { result[i] = value; continue; }
            result[i] = othersTotal <= 0
                ? remaining / (baseWeights.Length - 1)
                : baseWeights[i] / othersTotal * remaining;
        }

        return result;
    }

    /// <summary>Marsaglia–Tsang gamma sampler, the standard route to Dirichlet draws.</summary>
    private static double SampleGamma(Random rng, double shape)
    {
        if (shape < 1)
        {
            var u = rng.NextDouble();
            return SampleGamma(rng, shape + 1) * Math.Pow(u, 1.0 / shape);
        }

        var d = shape - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9 * d);

        while (true)
        {
            double x, v;
            do
            {
                x = Normal(rng);
                v = 1 + c * x;
            } while (v <= 0);

            v = v * v * v;
            var u2 = rng.NextDouble();

            if (u2 < 1 - 0.0331 * x * x * x * x) return d * v;
            if (Math.Log(u2) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v;
        }
    }

    private static double Normal(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static int Percentile(int[] sorted, double p)
    {
        var idx = (int)Math.Round(p * (sorted.Length - 1), MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private static double Median(int[] sorted)
    {
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static string Headline(IReadOnlyList<RankStability> stability)
    {
        if (stability.Count == 0) return "No areas were analysed.";

        var volatile_ = stability.Count(s => s.Stability == "volatile");
        var top = stability.FirstOrDefault(s => s.BaseRank == 1);

        var lead = top is null
            ? ""
            : top.Stability == "stable"
                ? $"{top.Name} stays at or near the top across almost every weighting. "
                : $"{top.Name} leads under the current weights but does not hold that position consistently. ";

        var tail = volatile_ == 0
            ? "Every area's rank held up well under reweighting."
            : $"{volatile_} of {stability.Count} areas move enough under reweighting that their exact rank should not be read as a finding.";

        return lead + tail;
    }

    private static SensitivityResponse Empty(int samples, double concentration, int seed, string reason) => new()
    {
        Method = "Dirichlet Monte Carlo over the weight simplex, plus one-at-a-time perturbation",
        Samples = samples,
        Concentration = concentration,
        Seed = seed,
        Stability = Array.Empty<RankStability>(),
        Tornado = Array.Empty<AreaTornado>(),
        Sweep = null,
        Headline = reason,
        Notes = Array.Empty<string>()
    };
}