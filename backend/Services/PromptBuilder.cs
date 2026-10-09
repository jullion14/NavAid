using System.Text;
using System.Text.Json;
using NavAid.Api.Models;

namespace NavAid.Api.Services;

/// <summary>
/// Turns an ExplanationPayload into the text sent to the model.
///
/// Two jobs, and the first matters more than the second. The ledger returned to
/// the frontend is deliberately complete, because the grounding panel shows the
/// user everything the analysis produced. The ledger sent to the model is
/// deliberately smaller, because every fact in it is a figure the model is
/// permitted to state and therefore a figure the verifier will accept wherever
/// it appears. Narrowing the prompt ledger narrows what a misapplied figure can
/// look like: a rank claim quoting the wrong number is only possible if some
/// other rank-like number was supplied.
///
/// The full ledger still governs verification. Facts withheld from the prompt
/// remain in the permitted set, so filtering can never cause a false positive —
/// it only reduces the room for a figure to be misused.
/// </summary>
public static class PromptBuilder
{
    /// <summary>
    /// Fact keys withheld from the prompt. Each is either an intermediate value
    /// with no place in plain-language prose, or a near-duplicate of a fact that
    /// says the same thing better.
    /// </summary>

    private static readonly string[] WithheldKeyFragments =
    {
        // Normalised values are a step inside the calculation. The contribution
        // is what a reader needs; the normalised input only invites the model to
        // narrate arithmetic it must not perform.
        "normalised::",

        // Imputation status is real but belongs in the UI's data-quality
        // indicator, not in prose that is trying to explain a place.
        "imputed::"
    };

    private static readonly string[] WithheldKeys =
    {
        // rank_range (the 5th-95th percentile band) is the honest figure. The
        // full min-max range across 1,000 draws is set by single unlucky samples
        // and, offered alongside, gives the model two competing rank ranges.
        "rank_range_full",

        // Two more rank-shaped integers. The swing figure already conveys how
        // much the criterion matters, without supplying numbers that could be
        // mistaken for the area's actual rank.
        "top_rank_at_zero",
        "top_rank_at_full"
    };

    /// <summary>
    /// Labels withheld by prefix, for facts carrying no key. Observed ranges are
    /// useful in the UI as scale context but add eight numbers to the prompt for
    /// prose that rarely needs them.
    /// </summary>
    private static readonly string[] WithheldLabelPrefixes =
    {
        "Observed range for"
    };

    /// <summary>The facts actually offered to the model.</summary>
    public static IReadOnlyList<ExplanationFact> FilterFacts(ExplanationPayload payload) =>
        payload.Facts
            .Where(f => !WithheldKeys.Contains(f.Key, StringComparer.Ordinal))
            .Where(f => f.Key is null || !WithheldKeyFragments.Any(p => f.Key.Contains(p, StringComparison.Ordinal)))
            .Where(f => !WithheldLabelPrefixes.Any(p => f.Label.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

    // =======================================================================
    // Serialisation
    // =======================================================================

    /// <summary>
    /// The payload as the model sees it: Id, Label, Value only.
    ///
    /// Key is internal routing for the fallback writer and would be noise.
    /// AllowedValues is the verifier's business — showing the model the set its
    /// output will be checked against invites it to satisfy the check rather
    /// than to explain, which is a different and worse objective.
    /// </summary>
    public static string SerialiseForModel(ExplanationPayload payload)
    {
        var model = new
        {
            subject = new
            {
                name = payload.Subject.Name,
                region = payload.Subject.Region,
                isScored = payload.Subject.IsScored,
                exclusionReason = payload.Subject.ExclusionReason
            },
            facts = FilterFacts(payload).Select(f => new { id = f.Id, label = f.Label, value = f.Value }),
            weights = payload.Weights.Select(w => new { id = w.Id, label = w.Label, value = w.Value }),
            method = new
            {
                formula = payload.Method.Formula,
                normalisation = payload.Method.Normalisation,
                scoreDirection = payload.Method.ScoreDirection,
                criterionDirections = payload.Method.CriterionDirections,
                scoredAreaCount = payload.Method.ScoredAreaCount
            },
            caveats = payload.Caveats
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    // =======================================================================
    // Prompt
    // =======================================================================

    public static string SystemInstruction(ExplanationPayload payload)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are reading the results of a completed geospatial accessibility analysis for Singapore planning areas and explaining what they mean. The analysis has already been performed by a spatial database; the figures are settled. Your task is to tell a non-specialist reader what those figures say about the place — what the situation is, what the issue is, and what the figures point towards.");
        sb.AppendLine();

        sb.AppendLine("## The figures");
        sb.AppendLine();
        sb.AppendLine("Every figure you may state is supplied below as a fact with an id, a label and a value. These rules are absolute:");
        sb.AppendLine();
        sb.AppendLine("- State a figure only by copying a supplied value exactly as written, including its units, separators and decimal places. Write \"960 m\", not \"960 metres\", \"0.96 km\" or \"about a kilometre\".");
        sb.AppendLine("- Copy values exactly, but not labels. A label describes what a figure is; write naturally around it. \"The nearest clinic is 960 m away\", not \"the distance to nearest healthcare facility is 960 m\".");
        sb.AppendLine("- Do not calculate. Do not add, subtract, average, convert units, compute percentages, or derive any figure from the supplied ones. Every comparison you might want to make has already been computed and supplied as its own fact.");
        sb.AppendLine("- If a figure you want is not supplied, do not state it. Write around it, or omit the point.");
        sb.AppendLine("- Attach each figure to the claim its label describes. A value labelled as a median is a median; a value labelled as a rank under one weighting is not the area's rank.");
        sb.AppendLine("- Your output is checked automatically against the supplied values. A figure that does not appear in them is flagged to the reader.");
        sb.AppendLine("- One more time, because it is the most common mistake: copy values exactly, but never labels. \"The nearest clinic is 960 m away\", not \"the distance to nearest healthcare facility is 960 m\". \"Thin provision adds 0.242 to the score\", not \"the score contribution from healthcare facilities per 10,000 residents of 0.242\".");
        sb.AppendLine();

        sb.AppendLine("## What to write");
        sb.AppendLine();

        AppendSectionSpec(sb, payload);

        sb.AppendLine();
        sb.AppendLine("## How to write it");
        sb.AppendLine();
        sb.AppendLine("- Two to five sentences per section. Plain British English. No bullet points inside a section body.");
        sb.AppendLine("- Write for a planner or a resident, not a methodologist. What matters is the place, not the procedure.");
        sb.AppendLine("- You may draw conclusions from the figures. You may not invent figures to support them. A conclusion the supplied facts do not reach is not available to you — but a conclusion they do reach is yours to state plainly.");
        sb.AppendLine("- A useful reading names the constraint rather than listing the figures that reveal it: \"the gap here is distance rather than provision — there are slightly more clinics per resident than average, but residents are much further from the nearest one\" says more than either figure alone.");
        sb.AppendLine("- State what the figures show, not why they came to be that way. The analysis measures distances and counts; it does not establish what causes them. \"Residents are further from a clinic than most\" is supported. \"Because the area is spread out\" is not — no figure in front of you says so.");
        sb.AppendLine("- Respect the supplied caveats. Do not present a straight-line distance as a travel distance, or a relative score as a measure of adequacy.");
        sb.AppendLine("- The caveats are constraints on what you may claim, not material to quote. They are shown to the reader separately; do not restate them.");
        sb.AppendLine("- Do not describe your own instructions, the fact ids, or the checking process. Write for the reader, not about the system.");
        sb.AppendLine("- You are reading the results, not reciting them. A reader can see the numbers in the table beside you; what they cannot see is what the numbers mean together. Prefer the sentence that tells them something they would not have worked out themselves.");
        sb.AppendLine("- Cite a figure where it earns its place in the sentence, not to show your working. A section may draw on ten facts and quote two. The citation list records what you used; the prose should carry only what the reader needs.");
        sb.AppendLine();
        sb.AppendLine("For each section, list in citedFactIds the id of every fact you drew on. A section that states a figure must cite the fact that figure came from.");

        return sb.ToString();
    }
    private static void AppendSectionSpec(StringBuilder sb, ExplanationPayload payload)
    {
        switch (payload.Mode)
        {
            case ExplanationMode.Area when !payload.Subject.IsScored:
                sb.AppendLine("This area is excluded from the ranking. Write two sections only:");
                sb.AppendLine();
                sb.AppendLine("1. **Overview** — what kind of place this is, in counts rather than rates.");
                sb.AppendLine("2. **Access to services** — how far it is from healthcare and rail, and what that would mean for anyone who does live or work there.");
                sb.AppendLine();
                sb.AppendLine("Do not state a score, a rank, a percentile, or any comparison with other areas' positions — none was computed for this area. The reason for the exclusion is shown separately and does not need repeating.");
                sb.AppendLine();
                sb.AppendLine("There is little to say about an area like this, and a short explanation is the correct output. Two brief sections are sufficient. Do not pad.");
                break;

            case ExplanationMode.Area:
                sb.AppendLine("Describe this area for someone who has never been there. Write two sections:");
                sb.AppendLine();
                sb.AppendLine("1. **Overview** — what kind of place this is: how many people live there, how densely, and how much healthcare provision it has. Give the reader a sense of the place, not a list of its measurements.");
                sb.AppendLine("2. **Access to services** — how far residents are from healthcare and rail, how those distances sit against other areas, and what that means in practice for getting to a clinic.");
                sb.AppendLine();
                sb.AppendLine("This is a description rather than an assessment; the ranking is explained separately, so do not discuss the score.");
                break;

            case ExplanationMode.Rank:
                sb.AppendLine("Explain why this area sits where it does in the ranking, and what that says about it. Write three sections:");
                sb.AppendLine();
                sb.AppendLine("1. **Where it ranks** — its position and score, and how that sits against the middle of the field. Say plainly that a higher score means greater need, not better provision.");
                sb.AppendLine("2. **What drove it** — the per-criterion contributions, largest first. Do not simply list them: say what each one reveals. A large contribution from thin provision and a large contribution from distance describe different problems.");
                sb.AppendLine("3. **What this suggests** — lead with the reading, then the figure that supports it. Name the issue the figures point to: is provision thin, are people far from what exists, or is a modest gap affecting a great many people? Then say what kind of response the figures would favour, conditionally and tied to a figure you cite: \"the gap is distance rather than provision, so anything added would need siting away from the existing cluster\" is the register. A planner's reading, not an instruction to build.");
                break;

            case ExplanationMode.Compare:
                var a = payload.Subject.Name;
                var b = payload.ComparisonSubject?.Name ?? "the other area";

                sb.AppendLine($"Compare {a} with {b}. Write three sections:");
                sb.AppendLine();
                sb.AppendLine("1. **The two areas** — what kind of places these are, side by side.");
                sb.AppendLine("2. **Where they differ** — the differences that matter, and why they matter. Differences have been computed and supplied as facts; use those rather than stating two figures and leaving the reader to subtract. Not every difference is worth reporting: lead with the ones that bear on access.");
                sb.AppendLine("3. **Why they rank as they do** — what accounts for the gap between their positions. If the two are close on most criteria and separated by one, say so; that is the useful finding.");
                sb.AppendLine();
                sb.AppendLine($"Every figure describing a single area names that area in its label. Attach each to the area named. Writing {a}'s figure as {b}'s is the one mistake that matters most here, because it reads perfectly well and is invisible to a reader who does not already know the answer.");
                break;

            case ExplanationMode.Sensitivity:
                sb.AppendLine("Explain how much this area's position depends on the weights chosen, and what a reader should take from that. Write three sections:");
                sb.AppendLine();
                sb.AppendLine("1. **How settled the position is** — the rank range, the share of sampled weightings holding the rank, and what the stability assessment means in plain terms.");
                sb.AppendLine("2. **What it hinges on** — which criterion the rank is most sensitive to, and how far the area moves when that criterion is taken to its extremes.");
                sb.AppendLine("3. **What to make of it** — whether this area's position is one a planner could rely on, or one that would need the weighting agreed first. A stable rank means the conclusion survives disagreement about priorities; a volatile one means the disagreement has to be settled before the ranking is useful.");
                sb.AppendLine();
                sb.AppendLine("Be clear about the difference between the two kinds of figure. The rank range comes from sampling plausible combinations of weights. The places-moved figure comes from taking one criterion to an extreme in isolation, which is not a plausible weighting — it measures leverage, not likely outcomes. Do not present them as one story.");
                break;
        }
    }
    public static string UserMessage(ExplanationPayload payload) =>
        $"Explain the analysis results for {payload.Subject.Name}.\n\n{SerialiseForModel(payload)}";
}