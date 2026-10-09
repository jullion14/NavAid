using System.Text;
using GeoDSS.Api.Models;

namespace GeoDSS.Api.Services;

/// <summary>
/// Deterministic explanation writer. Produces the same sections as the AI path
/// from the same payload, without a language model.
///
/// This exists for two reasons. The practical one is that the Gemini API can be
/// slow, rate-limited or unreachable, and a stiff explanation is better than an
/// error message during an assessed demonstration. The architectural one
/// matters more: it demonstrates that the AI layer is a presentation choice,
/// not a correctness dependency. Every figure in this output and in the model's
/// output comes from the same ledger; only the prose differs.
///
/// By construction its output passes ExplanationVerifier, since it quotes
/// ledger values verbatim and never computes. That is worth a unit test — it
/// checks the verifier as much as the writer.
/// </summary>
public static class TemplateExplanationWriter
{
    public static ExplanationResult Write(ExplanationPayload payload, string? fallbackReason = null)
    {
        var f = new FactLookup(payload);

        var sections = payload.Mode switch
        {
            ExplanationMode.Compare => CompareSections(payload, f),
            ExplanationMode.Rank => new List<ExplanationSection> { Score(payload, f), CaveatSection(payload) },
            ExplanationMode.Sensitivity => new List<ExplanationSection> { Robustness(payload, f), CaveatSection(payload) },
            _ => payload.Subject.IsScored
                ? new List<ExplanationSection> { Overview(payload, f), Access(payload, f), Score(payload, f), Robustness(payload, f), CaveatSection(payload) }
                : new List<ExplanationSection> { Overview(payload, f), Access(payload, f), NotRanked(payload), CaveatSection(payload) },
        };

        return new ExplanationResult
        {
            Source = ExplanationSource.Template,
            Sections = sections.Where(s => !string.IsNullOrWhiteSpace(s.Body)).ToList(),
            Payload = payload,
            Verification = null,
            FallbackReason = fallbackReason
        };
    }
    // =======================================================================
    /// <summary>
    /// Compare mode without a model. Keys are prefixed with the area name, so
    /// the single-subject sections cannot be reused: each lookup has to name
    /// which area it wants.
    /// </summary>
    private static List<ExplanationSection> CompareSections(ExplanationPayload p, FactLookup f)
    {
        var a = p.Subject.Name;
        var b = p.ComparisonSubject?.Name ?? "the other area";

        var intro = new SectionBuilder(f);

        foreach (var name in new[] { a, b })
        {
            intro.IfPresent($"{name}::population", (pop, _) =>
            {
                intro.Sentence($"{name} has a resident population of {pop}");
                intro.IfPresent($"{name}::facilities",
                    (fac, _) => intro.Append($" and {fac} healthcare facilities."),
                    orElse: () => intro.Append("."));
            });
        }

        var diff = new SectionBuilder(f);

        diff.IfPresent("rank_gap", (gap, _) => diff.Sentence($"On the priority ranking they are {gap}."));
        diff.IfPresent("score_gap", (gap, _) => diff.Sentence($"Their scores differ by {gap}."));

        foreach (var key in new[] { "gap::dist_healthcare", "gap::dist_mrt", "gap::pop_density", "gap::facilities_per_10k" })
            diff.IfPresent(key, (text, _) => diff.Sentence($"{text}."));

        var ranks = new SectionBuilder(f);

        foreach (var name in new[] { a, b })
        {
            ranks.IfPresent($"{name}::rank", (rank, _) =>
            {
                ranks.Sentence($"{name} ranks {rank}");
                ranks.IfPresent($"{name}::score",
                    (sc, _) => ranks.Append($", scoring {sc}."),
                    orElse: () => ranks.Append("."));
            });

            ranks.IfPresent($"{name}::top_contributor", (top, _) =>
                ranks.Sentence($"Its largest contribution comes from {top.ToLowerInvariant()}."));
        }

        return new List<ExplanationSection>
        {
            intro.Build("The two areas"),
            diff.Build("Where they differ"),
            ranks.Build("Why they rank as they do"),
            CaveatSection(p),
        };
    }
    // =======================================================================

    private static ExplanationSection Overview(ExplanationPayload p, FactLookup f)
    {
        var s = new SectionBuilder(f);
        var region = p.Subject.Region.EndsWith(" Region", StringComparison.OrdinalIgnoreCase)
            ? p.Subject.Region
            : $"{p.Subject.Region} region";

        s.Sentence($"{p.Subject.Name} is in the {region}.");

        s.IfPresent("population", (v, _) =>
            s.Sentence($"It has a resident population of {v}"))
         .AndIfPresent("density", (v, _) =>
            s.Append($", at a density of {v}."), orElse: () => s.Append("."));

        s.IfPresent("facilities", (v, _) =>
        {
            s.Sentence($"There are {v} healthcare facilities within its boundary");
            s.IfPresent("facilities_per_10k", (r, _) => s.Append($", or {r}."), orElse: () => s.Append("."));
        });

        return s.Build("Overview");
    }

    private static ExplanationSection Access(ExplanationPayload p, FactLookup f)
    {
        var s = new SectionBuilder(f);

        s.IfPresent("dist_healthcare", (v, _) =>
        {
            s.Sentence($"The nearest healthcare facility is {v} away");
            s.IfPresent("nearest_facility_name", (n, _) => s.Append($" ({n})."), orElse: () => s.Append("."));

            s.IfPresent("vs_median::dist_healthcare", (d, _) =>
                s.Sentence($"That is {d} for the areas being scored."));
        });

        s.IfPresent("dist_mrt", (v, _) =>
        {
            s.Sentence($"The nearest MRT exit is {v} away");
            s.IfPresent("nearest_mrt_name", (n, _) => s.Append($", at {n}."), orElse: () => s.Append("."));

            s.IfPresent("vs_median::dist_mrt", (d, _) =>
                s.Sentence($"That is {d}."));
        });

        return s.Build("Access to services");
    }

    private static ExplanationSection Score(ExplanationPayload p, FactLookup f)
    {
        var s = new SectionBuilder(f);
        // Score facts are absent in modes that do not discuss the ranking.
        // Without them the direction note and the weight list stand alone,
        // explaining a score the section never states.
        if (f.Get("rank") is null) return s.Build("Priority score");

        s.IfPresent("rank", (v, _) =>
        {
            s.Sentence($"{p.Subject.Name} ranks {v} on the priority score");
            s.IfPresent("score", (sc, _) => s.Append($", with a score of {sc}."), orElse: () => s.Append("."));
        });

        s.Sentence(p.Method.ScoreDirection);

        s.IfPresent("top_contributor", (v, _) =>
            s.Sentence($"The criterion contributing most to that score is {v.ToLowerInvariant()}."));

        // Contributions, largest first, quoted directly from the ledger.
        var contributions = p.Facts
            .Where(x => x.Kind == "score" && x.Label.StartsWith("Score contribution from", StringComparison.Ordinal))
            .ToList();

        if (contributions.Count > 0)
        {
            var parts = contributions.Select(c =>
            {
                s.Cite(c.Id);
                var criterion = c.Label["Score contribution from ".Length..];
                return $"{criterion} {c.Value}";
            });

            s.Sentence($"The full breakdown is: {string.Join("; ", parts)}.");
        }

        var weights = string.Join("; ", p.Weights.Select(w =>
        {
            s.Cite(w.Id);
            return $"{w.Label["Weight on ".Length..]} {w.Value}";
        }));

        if (!string.IsNullOrEmpty(weights))
            s.Sentence($"These follow from the weights in force: {weights}.");

        return s.Build("Priority score");
    }

    private static ExplanationSection Robustness(ExplanationPayload p, FactLookup f)
    {
        var s = new SectionBuilder(f);

        if (f.Get("rank_held_share") is null && f.Get("rank_range") is null)
            return s.Build("How robust this is");

        s.IfPresent("rank_held_share", (share, _) =>
        {
            s.IfPresent("rank", (rank, _) =>
                s.Sentence($"Across the sampled weightings, {p.Subject.Name} held rank {rank.Split(' ')[0]} in {share} of cases."),
                orElse: () => s.Sentence($"Its rank was unchanged in {share} of the sampled weightings."));
        });

        s.IfPresent("rank_range", (range, _) =>
            s.Sentence($"Its rank fell in the range {range} across the middle nine-tenths of samples."));

        s.IfPresent("stability_label", (label, _) =>
            s.Sentence($"On that basis its position is assessed as {label.ToLowerInvariant()}."));

        s.IfPresent("most_sensitive_criterion", (crit, _) =>
        {
            s.IfPresent("top_swing", (swing, _) =>
                s.Sentence($"Its position depends most on how {crit.ToLowerInvariant()} is weighted, moving {swing} places across that criterion's full range."),
                orElse: () => s.Sentence($"Its position depends most on how {crit.ToLowerInvariant()} is weighted."));
        });

        if (p.Method.SensitivitySamples is { } n && p.Method.SensitivitySeed is { } seed)
            s.Sentence($"This comes from {n:N0} sampled weight vectors using seed {seed}, so the same request returns the same figures.");

        s.Sentence("A stable rank means the conclusion survives reasonable disagreement about the weights. It does not mean the ranking is correct.");

        return s.Build("How robust this is");
    }

    private static ExplanationSection NotRanked(ExplanationPayload p) => new()
    {
        Heading = "Not included in the ranking",
        Body = p.Subject.ExclusionReason
               ?? "This area is excluded from the ranking. Its metrics are shown for reference only.",
        CitedFactIds = Array.Empty<string>(),
        IsVerbatim = true
    };

    private static ExplanationSection CaveatSection(ExplanationPayload p) => new()
    {
        Heading = "Worth bearing in mind",
        Body = string.Join(" ", p.Caveats),
        CitedFactIds = Array.Empty<string>(),
        IsVerbatim = true
    };

    // =======================================================================

    /// <summary>Semantic access to the ledger by stable key.</summary>
    private sealed class FactLookup
    {
        private readonly Dictionary<string, ExplanationFact> _byKey;

        public FactLookup(ExplanationPayload payload) =>
            _byKey = payload.Facts
                .Concat(payload.Weights)
                .Where(f => f.Key is not null)
                .GroupBy(f => f.Key!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        public ExplanationFact? Get(string key) =>
            _byKey.TryGetValue(key, out var fact) ? fact : null;
    }

    /// <summary>
    /// Accumulates prose and the fact IDs it drew on, so the grounding list in
    /// the panel is populated for the template path exactly as for the model
    /// path. Values marked "not available" are skipped rather than stated.
    /// </summary>
    private sealed class SectionBuilder
    {
        private readonly FactLookup _facts;
        private readonly StringBuilder _text = new();
        private readonly List<string> _cited = new();

        public SectionBuilder(FactLookup facts) => _facts = facts;

        public SectionBuilder Sentence(string text)
        {
            if (_text.Length > 0) _text.Append(' ');
            _text.Append(text);
            return this;
        }

        public SectionBuilder Append(string text)
        {
            _text.Append(text);
            return this;
        }

        public void Cite(string id)
        {
            if (!_cited.Contains(id)) _cited.Add(id);
        }

        public SectionBuilder IfPresent(string key, Action<string, string> then, Action? orElse = null)
        {
            var fact = _facts.Get(key);

            if (fact is null || IsUnavailable(fact.Value))
            {
                orElse?.Invoke();
                return this;
            }

            Cite(fact.Id);
            then(fact.Value, fact.Id);
            return this;
        }

        public SectionBuilder AndIfPresent(string key, Action<string, string> then, Action? orElse = null)
            => IfPresent(key, then, orElse);

        private static bool IsUnavailable(string value) =>
            value.Equals("not available", StringComparison.OrdinalIgnoreCase);

        public ExplanationSection Build(string heading) => new()
        {
            Heading = heading,
            Body = _text.ToString(),
            CitedFactIds = _cited
        };
    }
}