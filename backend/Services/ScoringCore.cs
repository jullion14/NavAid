using NavAid.Api.Models;

namespace NavAid.Api.Services;

/// <summary>
/// The scoring maths, as pure functions with no I/O.
///
/// Extracted so the sensitivity analysis runs the *same* code as the main
/// scoring endpoint. If these diverged, a stability interval would describe a
/// model the user never actually sees, which is worse than having no interval.
/// </summary>
public static class ScoringCore
{
    public const double NeutralNormalised = 0.5;

    public readonly record struct Bounds(double Min, double Max);

    /// <summary>
    /// Observed range per criterion. Depends only on the data, not the weights,
    /// so this is computed once and reused across every sensitivity run.
    /// </summary>
    public static Dictionary<string, Bounds> ComputeBounds(IReadOnlyList<AccessibilityMetrics> areas)
    {
        var bounds = new Dictionary<string, Bounds>();

        foreach (var metric in MetricCatalog.All)
        {
            var present = areas
                .Select(a => metric.Select(a))
                .Where(IsUsable)
                .Select(v => v!.Value)
                .ToList();

            bounds[metric.Key] = present.Count == 0
                ? new Bounds(0, 0)
                : new Bounds(present.Min(), present.Max());
        }

        return bounds;
    }

    public static bool IsUsable(double? v) =>
        v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value);

    /// <summary>
    /// Rescale to [0,1], then invert cost criteria so 1 always means "highest
    /// priority" regardless of the criterion's natural direction.
    /// </summary>
    public static double Normalise(double value, Bounds b, MetricDirection direction)
    {
        var range = b.Max - b.Min;
        if (Math.Abs(range) < double.Epsilon) return NeutralNormalised;

        var scaled = Math.Clamp((value - b.Min) / range, 0d, 1d);
        return direction == MetricDirection.Cost ? 1d - scaled : scaled;
    }

    /// <summary>Normalised value for one area and one criterion, imputing when missing.</summary>
    public static double NormalisedFor(
        AccessibilityMetrics area,
        MetricDefinition metric,
        IReadOnlyDictionary<string, Bounds> bounds)
    {
        var raw = metric.Select(area);
        return IsUsable(raw)
            ? Normalise(raw!.Value, bounds[metric.Key], metric.Direction)
            : NeutralNormalised;
    }

    /// <summary>
    /// Precompute the normalised matrix once. Normalisation does not depend on
    /// weights, so a 1,000-run Monte Carlo only repeats the weighted sum — this
    /// turns the whole analysis into a few million multiply-adds.
    /// </summary>
    public static double[,] BuildNormalisedMatrix(
        IReadOnlyList<AccessibilityMetrics> areas,
        IReadOnlyDictionary<string, Bounds> bounds)
    {
        var matrix = new double[areas.Count, MetricCatalog.All.Count];

        for (var a = 0; a < areas.Count; a++)
        {
            for (var m = 0; m < MetricCatalog.All.Count; m++)
            {
                matrix[a, m] = NormalisedFor(areas[a], MetricCatalog.All[m], bounds);
            }
        }

        return matrix;
    }

    /// <summary>Weighted sum for every area, given a normalised matrix and a weight vector.</summary>
    public static double[] Score(double[,] normalised, double[] weights)
    {
        var areaCount = normalised.GetLength(0);
        var metricCount = normalised.GetLength(1);
        var scores = new double[areaCount];

        for (var a = 0; a < areaCount; a++)
        {
            double total = 0;
            for (var m = 0; m < metricCount; m++)
            {
                total += weights[m] * normalised[a, m];
            }
            scores[a] = total;
        }

        return scores;
    }

    /// <summary>
    /// Ranks by descending score, 1-based. Ties share the lower rank number,
    /// matching the main scoring endpoint.
    /// </summary>
    public static int[] Rank(double[] scores)
    {
        var order = Enumerable.Range(0, scores.Length)
            .OrderByDescending(i => scores[i])
            .ToArray();

        var ranks = new int[scores.Length];
        var currentRank = 0;
        double? previous = null;

        for (var position = 0; position < order.Length; position++)
        {
            var idx = order[position];
            if (previous is null || Math.Abs(scores[idx] - previous.Value) > 1e-9)
            {
                currentRank = position + 1;
                previous = scores[idx];
            }
            ranks[idx] = currentRank;
        }

        return ranks;
    }

    /// <summary>Weight vector in MetricCatalog order, for the array-based routines above.</summary>
    public static double[] ToVector(IReadOnlyDictionary<string, double> weights) =>
        MetricCatalog.All.Select(m => weights.TryGetValue(m.Key, out var w) ? w : m.DefaultWeight).ToArray();

    /// <summary>Rescale a vector to sum to 1. Returns defaults if the total is non-positive.</summary>
    public static double[] NormaliseWeights(double[] weights)
    {
        var sum = weights.Sum();
        if (sum <= 0) return MetricCatalog.All.Select(m => m.DefaultWeight).ToArray();
        return weights.Select(w => w / sum).ToArray();
    }
}