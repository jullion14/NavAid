using GeoDSS.Api.Models;

namespace GeoDSS.Api.Services;

public interface IExplanationPayloadBuilder
{
    Task<ExplanationPayload?> BuildAsync(
        ExplanationMode mode,
        int planningAreaId,
        int? comparisonAreaId,
        Dictionary<string, double>? weights,
        CancellationToken ct = default);
}

/// <summary>
/// Assembles the closed fact ledger handed to the AI layer.
///
/// All arithmetic in the explanation pipeline happens here, deterministically,
/// from values the deterministic services already produced. Medians, deltas and
/// percentile bands are computed in C# precisely so the model never has a
/// legitimate reason to compute anything itself.
///
/// This class is pure with respect to its inputs and holds no API key, no HTTP
/// client, and no knowledge that a language model exists. It is testable, and
/// its output is what gets printed in the report as "everything the model saw".
/// </summary>
public sealed class ExplanationPayloadBuilder : IExplanationPayloadBuilder
{
    private readonly SpatialAnalysisService _analysis;
    private readonly IPriorityScoringService _scoring;
    private readonly ISensitivityService _sensitivity;
    private readonly ILogger<ExplanationPayloadBuilder> _logger;
    /// <summary>
    /// Fact kinds each mode admits. The ledger is gathered once and filtered
    /// rather than assembled differently per mode, so a figure means the same
    /// thing wherever it appears and the verifier needs no mode awareness.
    /// </summary>
    private static readonly Dictionary<ExplanationMode, string[]> KindsByMode = new()
    {
        [ExplanationMode.Area] = new[] { "metric", "context", "comparative" },
        [ExplanationMode.Rank] = new[] { "score", "comparative", "metric" },
        [ExplanationMode.Compare] = new[] { "metric", "context", "score" },
        [ExplanationMode.Sensitivity] = new[] { "sensitivity" },
    };
    public ExplanationPayloadBuilder(
        SpatialAnalysisService analysis,
        IPriorityScoringService scoring,
        ISensitivityService sensitivity,
        ILogger<ExplanationPayloadBuilder> logger)
    {
        _analysis = analysis;
        _scoring = scoring;
        _sensitivity = sensitivity;
        _logger = logger;
    }

    public async Task<ExplanationPayload?> BuildAsync(
    ExplanationMode mode,
    int planningAreaId,
    int? comparisonAreaId,
    Dictionary<string, double>? weights,
    CancellationToken ct = default)
    {
        var gathered = await GatherAsync(planningAreaId, weights, ct);
        if (gathered is null) return null;

        if (mode == ExplanationMode.Compare)
        {
            if (comparisonAreaId is null || comparisonAreaId == planningAreaId)
                return null;

            var other = await GatherAsync(comparisonAreaId.Value, weights, ct);
            if (other is null) return null;

            return BuildComparison(gathered, other);
        }

        // Sensitivity has nothing to say about an unscored area: no rank was
        // computed, so there is no stability to report. Fall back to the
        // descriptive brief rather than returning an empty explanation.
        var effective = mode == ExplanationMode.Sensitivity && !gathered.IsScored
            ? ExplanationMode.Area
            : mode;

        var kinds = KindsByMode[effective];

        var facts = gathered.Ledger.Facts
            .Where(f => kinds.Contains(f.Kind, StringComparer.Ordinal))
            .ToList();

        return Assemble(effective, gathered, facts, comparison: null);
    }

    // =======================================================================
    // Fact groups
    // =======================================================================

    private static void AddAreaFacts(FactLedger f, AccessibilityMetrics a, bool isScored)
    {
        f.Add("Resident population", Fmt.Count(a.Population), "metric", "population");
        f.Add("Land area", Fmt.SqKm(a.AreaSqKm), "metric");

        // Rates are withheld for unscored areas. Their populations are small
        // enough that a per-capita or per-area figure is arithmetically valid
        // and substantively meaningless — 0.0 facilities per 10,000 residents
        // against 80 residents invites a conclusion the data cannot support.
        // Withholding the fact is a stronger constraint than instructing the
        // model not to use it.
        if (isScored)
        {
            f.Add("Population density", Fmt.Density(a.PopulationDensity), "metric", "density");
        }

        f.Add("Healthcare facilities in the area", Fmt.Count(a.TotalFacilities), "metric", "facilities");
        f.Add("GP clinics", Fmt.Count(a.GpCount), "metric");
        f.Add("Polyclinics", Fmt.Count(a.PolyclinicCount), "metric");

        if (isScored)
        {
            f.Add("Facilities per 10,000 residents", Fmt.Rate(a.FacilitiesPer10k), "metric", "facilities_per_10k");
        }

        f.Add("Distance to nearest healthcare facility", Fmt.Metres(a.NearestFacilityMeters), "metric", "dist_healthcare");
        if (!string.IsNullOrWhiteSpace(a.NearestFacilityName))
            f.Add("Nearest healthcare facility", a.NearestFacilityName!, "context", "nearest_facility_name");

        f.Add("Distance to nearest MRT exit", Fmt.Metres(a.NearestMrtMeters), "metric", "dist_mrt");
        if (!string.IsNullOrWhiteSpace(a.NearestMrtStation))
            f.Add("Nearest MRT station", a.NearestMrtStation!, "context", "nearest_mrt_name");
        f.Add("MRT exits in the area", Fmt.Count(a.MrtExitCount), "metric");

        f.Add("Bus stops in the area", Fmt.Count(a.BusStopCount), "context");
        f.Add("Bus stops served by 5 or more services", Fmt.Count(a.WellServedBusStops), "context");
    }

    private static void AddScoreFacts(FactLedger f, AreaPriorityScore mine, PriorityScoreResponse scores)
    {
        f.Add("Priority score", Fmt.Score(mine.TotalScore), "score", "score");
        f.Add("Rank among scored areas", $"{mine.Rank} of {scores.AreaCount}", "score", "rank");

        var byLabel = scores.Metrics.ToDictionary(m => m.Key, m => m.Label);

        foreach (var c in mine.Components.OrderByDescending(c => c.Contribution))
        {
            var label = byLabel.TryGetValue(c.Key, out var l) ? l : c.Key;

            f.Add($"Score contribution from {Lower(label)}",
                Fmt.Score(c.Contribution), "score",
                key: $"contribution::{c.Key}");

            f.Add($"Normalised value for {Lower(label)}",
                Fmt.Score(c.NormalisedValue), "score",
                key: $"normalised::{c.Key}");

            if (c.IsImputed)
                f.Add($"Data status for {Lower(label)}",
                    "missing — imputed at the neutral value 0.500", "score",
                    key: $"imputed::{c.Key}");
        }

        var top = mine.Components.OrderByDescending(c => c.Contribution).First();
        f.Add("Largest single contributor to this area's score",
            byLabel.TryGetValue(top.Key, out var tl) ? tl : top.Key, "score", "top_contributor");
    }

    private static void AddComparativeFacts(
        FactLedger f, AccessibilityMetrics area, AreaPriorityScore mine, PriorityScoreResponse scores)
    {
        // Percentile band, so the model never has to divide a rank by a count.
        var pct = (int)Math.Round(100.0 * mine.Rank / Math.Max(scores.AreaCount, 1));
        f.Add("Position expressed as a percentile",
            pct <= 50
                ? $"in the top {pct}% by priority (higher priority means greater need)"
                : $"in the bottom {100 - pct}% by priority (lower priority means less need)",
            "comparative");

        f.Add("Median priority score across scored areas",
            Fmt.Score(Median(scores.Results.Select(r => r.TotalScore))), "comparative");

        // Per-criterion comparison against the median of the scored set.
        foreach (var metric in scores.Metrics)
        {
            var raws = scores.Results
                .SelectMany(r => r.Components)
                .Where(c => c.Key == metric.Key && c.RawValue.HasValue)
                .Select(c => c.RawValue!.Value)
                .ToList();

            if (raws.Count == 0) continue;

            var mineRaw = mine.Components.FirstOrDefault(c => c.Key == metric.Key)?.RawValue;
            if (mineRaw is null) continue;

            var median = Median(raws);

            // Deltas are computed from the rounded, displayed values rather than
            // the raw ones. Independently rounded figures do not reconcile, and
            // an explanation quoting a value, a median and their difference
            // would appear not to add up even though nothing was miscalculated.
            var shownMine = Fmt.RoundForDisplay(mineRaw.Value, metric.Unit);
            var shownMedian = Fmt.RoundForDisplay(median, metric.Unit);
            var delta = shownMine - shownMedian;
            var direction = Math.Abs(delta) < 1e-9 ? "the same as" : delta > 0 ? "above" : "below";

            f.Add($"Median {Lower(metric.Label)} across scored areas",
                Fmt.ByUnit(shownMedian, metric.Unit), "comparative",
                key: $"median::{metric.Key}");

            f.Add($"This area's {Lower(metric.Label)} compared with the median scored area",
                $"{Fmt.ByUnit(Math.Abs(delta), metric.Unit)} {direction} the median", "comparative",
                key: $"vs_median::{metric.Key}");

            if (metric.ObservedMin.HasValue && metric.ObservedMax.HasValue)
            {
                f.Add($"Observed range for {Lower(metric.Label)} across scored areas",
                    $"{Fmt.ByUnit(metric.ObservedMin.Value, metric.Unit)} to {Fmt.ByUnit(metric.ObservedMax.Value, metric.Unit)}",
                    "comparative");
            }
        }
    }

    // -----------------------------------------------------------------------
    private static void AddSensitivityFacts(FactLedger f, int planningAreaId, SensitivityResponse sens)
    {
        var stability = sens.Stability.FirstOrDefault(s => s.PlanningAreaId == planningAreaId);
        if (stability is not null)
        {
            f.Add("Percentile band used for the rank range",
                "the middle 90% of sampled weightings", "sensitivity", "rank_band");

            f.Add("Rank range across the middle 90% of sampled weightings",
                $"{stability.P05Rank} to {stability.P95Rank}", "sensitivity", "rank_range");

            f.Add("Median rank", Fmt.Rank(stability.MedianRank), "sensitivity", "median_rank");

            f.Add("Share of weightings holding this rank",
                Fmt.Percent(stability.RankHeldShare), "sensitivity", "rank_held_share");

            f.Add("Stability assessment", stability.Stability.ToString(), "sensitivity", "stability_label");

            f.Add("Full rank range including extremes",
                $"{stability.BestRank} to {stability.WorstRank}", "sensitivity", "rank_range_full");

            f.Add("Number of weight vectors sampled", Fmt.Count(sens.Samples), "sensitivity", "samples");
        }

        var tornado = sens.Tornado.FirstOrDefault(t => t.PlanningAreaId == planningAreaId);
        if (tornado is null) return;

        var top = tornado.Effects.First();   // already ordered by swing, descending

        f.Add("Criterion this area's rank is most sensitive to", top.Label, "sensitivity", "most_sensitive_criterion");

        f.Add($"Places moved when '{Lower(top.Label)}' is taken to each extreme",
            Fmt.Count(top.Swing), "sensitivity", "top_swing");

        f.Add($"Rank if '{Lower(top.Label)}' is given zero weight",
            Fmt.Rank(top.RankAtZero), "sensitivity", "top_rank_at_zero");

        f.Add($"Rank if '{Lower(top.Label)}' is given all the weight",
            Fmt.Rank(top.RankAtFull), "sensitivity", "top_rank_at_full");
    }

    // =======================================================================

    private static ExplanationPayload Assemble(
    ExplanationMode mode,
    Gathered g,
    IReadOnlyList<ExplanationFact> facts,
    ExplanationSubject? comparison) => new()
    {
        Mode = mode,
        Subject = new ExplanationSubject
        {
            Name = g.Area.Name,
            Region = g.Area.Region ?? "unstated",
            IsScored = g.IsScored,
            ExclusionReason = g.ExclusionReason
        },
        ComparisonSubject = comparison,
        Facts = facts,
        Weights = g.IsScored
            ? g.Scores.Metrics
                .Select((m, i) => new ExplanationFact
                {
                    Id = $"w{i + 1}",
                    Label = $"Weight on {Lower(m.Label)}",
                    Value = Fmt.Score(m.Weight),
                    Kind = "weight"
                })
                .ToList()
            : Array.Empty<ExplanationFact>(),
        Method = new ExplanationMethod
        {
            Formula = g.Scores.Formula,
            Normalisation = g.Scores.Normalisation,
            ScoreDirection = "A higher score means higher priority for intervention, not better accessibility.",
            CriterionDirections = g.Scores.Metrics
                    .Select(m => $"{m.Label}: {(string.Equals(m.Direction, "cost", StringComparison.OrdinalIgnoreCase) ? "more lowers priority" : "more raises priority")}")
                    .ToArray(),
            ScoredAreaCount = g.Scores.AreaCount,
            SensitivitySamples = g.Sensitivity?.Samples,
            SensitivitySeed = g.Sensitivity?.Seed
        },
        Caveats = Caveats(g.IsScored)
    };

    private static string ExclusionReason(AccessibilityMetrics a) =>
        a.Population <= 0
            ? "This area has no resident population on record, so per-capita rates are undefined and it is excluded from the ranking."
            : "This area has fewer than 1,000 residents. Per-capita rates are unstable at that scale and would distort the shared normalisation range, so it is excluded from the ranking.";

    private static string[] Caveats(bool isScored)
    {
        var common = new List<string>
        {
            "Distances are straight-line, measured from a single representative interior point, and are shorter than real travel distances.",
            "Bus figures are descriptive context and carry no weight in the score.",
            "Scores are relative to the scored set and say nothing about whether provision is adequate in absolute terms.",
            "Weights are set by judgement, not derived from a formal elicitation method."
        };

        if (!isScored)
        {
            common.Insert(0, "This area is excluded from the ranking, so no score, rank or comparison with other areas' positions applies to it.");
            common.Insert(1, "Per-capita and per-area rates are not reported for this area because its resident population is too small for them to be meaningful.");
        }
        return common.ToArray();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    } 

    /// Lowercases a metric label for mid-sentence use while preserving acronyms,
    /// which ToLowerInvariant alone mangles ("nearest mrt exit").
    private static string Lower(string label) =>
        label.ToLowerInvariant().Replace("mrt", "MRT", StringComparison.Ordinal);

    /// <summary>Assigns sequential fact IDs so uniqueness is structural, not clerical.</summary>
    private sealed class FactLedger
    {
        private readonly List<ExplanationFact> _facts = new();
        public IReadOnlyList<ExplanationFact> Facts => _facts;

        public void Add(string label, string value, string kind, string? key = null, string? owner = null) =>
            _facts.Add(new ExplanationFact
            {
                Id = $"f{_facts.Count + 1}",
                Label = label,
                Value = value,
                Kind = kind,
                Key = key,
                Owner = owner
            });
    }
    /// <summary>
    /// Everything computed for one area, before any mode filtering. Held
    /// together so Compare mode can gather twice and diff the results.
    /// </summary>
    private sealed record Gathered(
        AccessibilityMetrics Area,
        FactLedger Ledger,
        PriorityScoreResponse Scores,
        AreaPriorityScore? Mine,
        SensitivityResponse? Sensitivity,
        bool IsScored,
        string? ExclusionReason);

    private async Task<Gathered?> GatherAsync(
        int planningAreaId,
        Dictionary<string, double>? weights,
        CancellationToken ct)
    {
        var area = await _analysis.GetForAreaAsync(planningAreaId);
        if (area is null) return null;

        var ledger = new FactLedger();

        var scores = await _scoring.ScoreAsync(
            new PriorityScoreRequest { Weights = weights }, ct);

        var mine = scores.Results.FirstOrDefault(r => r.PlanningAreaId == planningAreaId);

        if (mine is null)
        {
            _logger.LogInformation(
                "Explanation payload for unscored area {Id} ({Name})", planningAreaId, area.Name);

            AddAreaFacts(ledger, area, isScored: false);

            return new Gathered(area, ledger, scores, null, null, false, ExclusionReason(area));
        }

        AddAreaFacts(ledger, area, isScored: true);
        AddScoreFacts(ledger, mine, scores);
        AddComparativeFacts(ledger, area, mine, scores);

        var sens = await _sensitivity.AnalyseAsync(
            new SensitivityRequest { Weights = weights }, ct);

        AddSensitivityFacts(ledger, planningAreaId, sens);

        return new Gathered(area, ledger, scores, mine, sens, true, null);
    }
    /// <summary>
    /// Two areas in one ledger, plus the differences between them.
    ///
    /// Every fact carries an Owner naming the area it describes, and its label
    /// is prefixed with that name. In a single-area ledger a figure can only
    /// belong to one place; here it can belong to either, and a figure attached
    /// to the wrong area is both the most likely failure and the hardest to
    /// notice, since it reads perfectly well.
    ///
    /// Differences are computed here from the rounded display values, so the
    /// two figures and the gap between them reconcile when all three appear in
    /// the same sentence.
    /// </summary>
    private static ExplanationPayload BuildComparison(Gathered subject, Gathered other)
    {
        var ledger = new FactLedger();

        var kinds = KindsByMode[ExplanationMode.Compare];

        foreach (var g in new[] { subject, other })
        {
            foreach (var fact in g.Ledger.Facts.Where(f => kinds.Contains(f.Kind, StringComparer.Ordinal)))
            {
                // Comparatives against the median are dropped: with two named
                // areas in play, "440 m above the median" invites the model to
                // read the median as the other area.
                if (fact.Key?.StartsWith("vs_median::", StringComparison.Ordinal) == true) continue;
                if (fact.Key?.StartsWith("median::", StringComparison.Ordinal) == true) continue;

                ledger.Add(
                    $"{g.Area.Name}: {LowerFirst(fact.Label)}",
                    fact.Value,
                    fact.Kind,
                    key: fact.Key is null ? null : $"{g.Area.Name}::{fact.Key}",
                    owner: g.Area.Name);
            }
        }

        AddDifferenceFacts(ledger, subject, other);

        return new ExplanationPayload
        {
            Mode = ExplanationMode.Compare,
            Subject = SubjectOf(subject),
            ComparisonSubject = SubjectOf(other),
            Facts = ledger.Facts,
            Weights = subject.Scores.Metrics
                .Select((m, i) => new ExplanationFact
                {
                    Id = $"w{i + 1}",
                    Label = $"Weight on {Lower(m.Label)}",
                    Value = Fmt.Score(m.Weight),
                    Kind = "weight"
                })
                .ToList(),
            Method = new ExplanationMethod
            {
                Formula = subject.Scores.Formula,
                Normalisation = subject.Scores.Normalisation,
                ScoreDirection = "A higher score means higher priority for intervention, not better accessibility.",
                CriterionDirections = subject.Scores.Metrics
                    .Select(m => $"{m.Label}: {(string.Equals(m.Direction, "cost", StringComparison.OrdinalIgnoreCase) ? "more lowers priority" : "more raises priority")}")
                    .ToArray(),
                ScoredAreaCount = subject.Scores.AreaCount,
                SensitivitySamples = null,
                SensitivitySeed = null
            },
            Caveats = CompareCaveats(subject, other)
        };
    }

    /// <summary>
    /// The gaps between the two areas, precomputed. Without these the model has
    /// two numbers and an obvious subtraction in front of it, which is exactly
    /// the temptation the architecture exists to remove.
    /// </summary>
    private static void AddDifferenceFacts(FactLedger f, Gathered a, Gathered b)
    {
        if (a.Mine is null || b.Mine is null)
        {
            f.Add("Comparison of ranks",
                "not available — one of these areas is excluded from the ranking",
                "comparative");
            return;
        }

        var rankGap = Math.Abs(a.Mine.Rank - b.Mine.Rank);
        var ahead = a.Mine.Rank < b.Mine.Rank ? a.Area.Name : b.Area.Name;

        f.Add("Difference in rank",
            rankGap == 0
                ? "the same rank"
                : rankGap == 1
                    ? $"1 place apart, with {ahead} the higher priority"
                    : $"{Fmt.Count(rankGap)} places apart, with {ahead} the higher priority",
            "comparative", "rank_gap");

        var shownA = Fmt.RoundForDisplay(a.Mine.TotalScore, null);
        var shownB = Fmt.RoundForDisplay(b.Mine.TotalScore, null);

        f.Add("Difference in priority score",
            Fmt.Score(Math.Abs(shownA - shownB)),
            "comparative", "score_gap");

        foreach (var metric in a.Scores.Metrics)
        {
            var rawA = a.Mine.Components.FirstOrDefault(c => c.Key == metric.Key)?.RawValue;
            var rawB = b.Mine.Components.FirstOrDefault(c => c.Key == metric.Key)?.RawValue;
            if (rawA is null || rawB is null) continue;

            var displayA = Fmt.RoundForDisplay(rawA.Value, metric.Unit);
            var displayB = Fmt.RoundForDisplay(rawB.Value, metric.Unit);
            var gap = displayA - displayB;

            var direction = Math.Abs(gap) < 1e-9
                ? "the same as"
                : gap > 0 ? $"higher than {b.Area.Name}" : $"lower than {b.Area.Name}";

            f.Add($"Difference in {Lower(metric.Label)}",
                Math.Abs(gap) < 1e-9
                    ? "the same in both areas"
                    : $"{a.Area.Name} is {Fmt.ByUnit(Math.Abs(gap), metric.Unit)} " +
                      $"{Comparative(metric.Key, gap > 0)} {b.Area.Name}",
                "comparative", $"gap::{metric.Key}");
        }
    }

    private static ExplanationSubject SubjectOf(Gathered g) => new()
    {
        Name = g.Area.Name,
        Region = g.Area.Region ?? "unstated",
        IsScored = g.IsScored,
        ExclusionReason = g.ExclusionReason
    };

    /// How a gap in each metric should be described. "Higher" and "lower" are
    /// ambiguous for distances — a distance that is higher reads as altitude —
    /// and uninformative for rates, so each metric gets wording that says what
    /// the difference means for residents.
    private static string Comparative(string metricKey, bool subjectIsGreater) => metricKey switch
    {
        "dist_healthcare" => subjectIsGreater ? "further from its nearest clinic than" : "closer to its nearest clinic than",
        "dist_mrt" => subjectIsGreater ? "further from its nearest MRT exit than" : "closer to its nearest MRT exit than",
        "pop_density" => subjectIsGreater ? "more crowded than" : "less crowded than",
        "facilities_per_10k" => subjectIsGreater ? "better served than" : "less well served than",
        _ => subjectIsGreater ? "higher than" : "lower than"
    };

    private static string[] CompareCaveats(Gathered a, Gathered b)
    {
        var common = new List<string>
        {
            "Distances are straight-line, measured from a single representative interior point, and are shorter than real travel distances.",
            "Bus figures are descriptive context and carry no weight in the score.",
            "Scores are relative to the scored set and say nothing about whether provision is adequate in absolute terms.",
            "Weights are set by judgement, not derived from a formal elicitation method."
        };

        foreach (var g in new[] { a, b }.Where(x => !x.IsScored))
            common.Insert(0, $"{g.Area.Name} is excluded from the ranking, so it has no score or rank to compare.");

        return common.ToArray();
    }

    /// Lowercases a label's first word so it reads after a name prefix, unless
    /// that word is an acronym ("GP clinics", "MRT exits"), which lowercasing
    /// would corrupt.
    private static string LowerFirst(string label)
    {
        if (label.Length == 0) return label;

        var firstWord = label.Split(' ')[0];
        if (firstWord.Length > 1 && firstWord.All(char.IsUpper)) return label;

        return char.ToLowerInvariant(label[0]) + label[1..];
    }
    /// The single place where numbers become text. Every figure the model can
    /// quote passes through here, so the verifier's exact-match test is only as
    /// sound as this class is consistent.
    ///
    /// The unit strings in ByUnit and RoundForDisplay must match MetricCatalog
    /// exactly. A miss falls through to the default and produces an unformatted
    /// figure, which is hard to spot by eye and breaks nothing until it does.
    private static class Fmt
    {
        public static string Count(int v) => v.ToString("N0");
        public static string Count(int? v) => v.HasValue ? v.Value.ToString("N0") : "not available";
        public static string Count(double v) => v.ToString("N0");

        public static string Rank(int v) => v.ToString();
        public static string Rank(double v) => v.ToString("0");

        public static string Metres(double? v) =>
            v is null ? "not available"
            : v.Value >= 1000 ? $"{v.Value / 1000:0.0} km"
            : $"{Math.Round(v.Value / 10) * 10:N0} m";

        public static string SqKm(double? v) => v is null ? "not available" : $"{v.Value:0.0} km²";
        public static string Density(double? v) => v is null ? "not available" : $"{v.Value:N0} residents per km²";
        public static string Rate(double? v) => v is null ? "not available" : $"{v.Value:0.0} per 10,000 residents";
        public static string Score(double v) => v.ToString("0.000");
        public static string Score(double? v) => v.HasValue ? Score(v.Value) : "not available";

        /// <summary>Share expressed 0–1, rendered as a whole percentage.</summary>
        public static string Percent(double share) => $"{Math.Round(share * 100)}%";

        /// <summary>Unit strings come from MetricCatalog. Keep these cases in step with it.</summary>
        public static string ByUnit(double v, string? unit) => unit switch
        {
            "m" => Metres(v),
            "people/km²" => Density(v),
            "per 10k" => Rate(v),
            _ => v.ToString("0.##")
        };

        /// <summary>
        /// The value as it will be displayed, still numeric. Deltas are computed
        /// from these rather than from raw values so that a median, a value and
        /// the gap between them reconcile when all three are quoted together.
        /// </summary>
        public static double RoundForDisplay(double v, string? unit) => unit switch
        {
            "m" => v >= 1000 ? Math.Round(v / 100) * 100 : Math.Round(v / 10) * 10,
            "people/km²" => Math.Round(v),
            "per 10k" => Math.Round(v, 1),
            null => Math.Round(v, 3),
            _ => v
        };
    }
}
