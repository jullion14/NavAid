using System.Globalization;
using System.Text.RegularExpressions;
using NavAid.Api.Models;

namespace NavAid.Api.Services;

/// <summary>
/// Post-generation check that every figure in the model's output traces back to
/// a value supplied in the payload.
///
/// This is the module's guarantee. Prompt constraints and low temperature
/// reduce the rate at which the model invents figures; they cannot eliminate
/// it. This class ensures an invented figure cannot reach the user unflagged,
/// which is a weaker claim than "the model cannot hallucinate" and the only one
/// that is actually true.
///
/// Pure and static by design: no HTTP, no configuration, no API key. It can be
/// unit-tested against handcrafted bad outputs, which is how the failure-mode
/// evidence for the report is produced.
/// </summary>
public static class ExplanationVerifier
{
    /// <summary>
    /// Numbers permitted regardless of the ledger. Deliberately empty.
    ///
    /// Every entry added here is a figure the model may state unchecked, so the
    /// set should only ever grow in response to a demonstrated false positive,
    /// and each addition should be justified in the report. Resist the urge to
    /// allowlist small integers: rank claims are small integers.
    /// </summary>
    private static readonly HashSet<double> AlwaysAllowed = new();

    /// <summary>
    /// Matches numbers with optional thousands separators and decimals.
    /// Lookarounds keep it from splitting identifiers or version strings.
    /// </summary>
    private static readonly Regex NumberPattern = new(
        @"(?<![\w.])\d{1,3}(?:,\d{3})+(?:\.\d+)?(?![\w])|(?<![\w.])\d+(?:\.\d+)?(?![\w])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FactIdPattern = new(
        @"\bf\d+\b|\bw\d+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Terms the model must not use when the subject is unscored.</summary>
    private static readonly string[] ScoringTerms =
        { "rank", "ranked", "ranking", "priority score", "scored", "percentile" };

    public static VerificationReport Verify(
        ExplanationPayload payload,
        string modelOutput,
        IReadOnlyList<string>? citedFactIds = null)
    {
        var findings = new List<VerificationFinding>();

        // -- Build the permitted sets from the ledger -----------------------
        // Numeric: every number appearing anywhere in any supplied value.
        // Canonical: the exact textual forms, used to detect format drift.
        var permittedNumbers = new HashSet<double>(AlwaysAllowed);
        var canonicalForms = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in payload.Facts.Concat(payload.Weights).Select(f => f.Value))
        {
            foreach (Match m in NumberPattern.Matches(value))
            {
                canonicalForms.Add(m.Value);
                if (TryParse(m.Value, out var parsed)) permittedNumbers.Add(parsed);
            }
        }

        // The method block carries figures the model may legitimately restate.
        foreach (var value in MethodStrings(payload))
        {
            foreach (Match m in NumberPattern.Matches(value))
            {
                canonicalForms.Add(m.Value);
                if (TryParse(m.Value, out var parsed)) permittedNumbers.Add(parsed);
            }
        }

        // -- Check every numeric token in the output ------------------------
        var checkedCount = 0;
        var matchedCount = 0;

        foreach (Match m in NumberPattern.Matches(modelOutput))
        {
            checkedCount++;

            if (!TryParse(m.Value, out var parsed))
            {
                findings.Add(new VerificationFinding
                {
                    Severity = FindingSeverity.Error,
                    Kind = "unparseable-figure",
                    Text = m.Value,
                    Start = m.Index,
                    Length = m.Length,
                    Message = $"The figure '{m.Value}' could not be parsed and cannot be checked."
                });
                continue;
            }

            if (!permittedNumbers.Contains(parsed))
            {
                findings.Add(new VerificationFinding
                {
                    Severity = FindingSeverity.Error,
                    Kind = "unknown-figure",
                    Text = m.Value,
                    Start = m.Index,
                    Length = m.Length,
                    Message = $"The figure '{m.Value}' does not appear in the computed values supplied to the model."
                });
                continue;
            }

            matchedCount++;

            // Numerically correct but textually re-rendered — the model has
            // reformatted a value rather than quoting it. Not a correctness
            // failure, but it indicates the model is manipulating figures.
            if (!canonicalForms.Contains(m.Value))
            {
                findings.Add(new VerificationFinding
                {
                    Severity = FindingSeverity.Warning,
                    Kind = "format-drift",
                    Text = m.Value,
                    Start = m.Index,
                    Length = m.Length,
                    Message = $"The figure '{m.Value}' matches a supplied value numerically but was re-formatted rather than quoted."
                });
            }
        }

        // -- Check cited fact IDs resolve -----------------------------------
        var knownIds = payload.Facts.Concat(payload.Weights)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.Ordinal);

        var idsToCheck = citedFactIds ?? FactIdPattern.Matches(modelOutput)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var id in idsToCheck.Where(id => !knownIds.Contains(id)))
        {
            findings.Add(new VerificationFinding
            {
                Severity = FindingSeverity.Error,
                Kind = "unknown-fact-id",
                Text = id,
                Message = $"The model cited fact '{id}', which was not supplied."
            });
        }

        // -- Check scoring claims are not made about unscored areas ---------
        if (!payload.Subject.IsScored)
        {
            foreach (var term in ScoringTerms)
            {
                var idx = modelOutput.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                findings.Add(new VerificationFinding
                {
                    Severity = FindingSeverity.Error,
                    Kind = "forbidden-claim",
                    Text = term,
                    Start = idx,
                    Length = term.Length,
                    Message = $"The subject is excluded from the ranking, but the output refers to '{term}'."
                });
            }
        }

        var hasErrors = findings.Any(f => f.Severity == FindingSeverity.Error);

        return new VerificationReport
        {
            Outcome = hasErrors ? VerificationOutcome.Flagged : VerificationOutcome.Verified,
            FiguresChecked = checkedCount,
            FiguresMatched = matchedCount,
            Findings = findings,
            LedgerSize = payload.Facts.Count + payload.Weights.Count
        };
    }

    private static IEnumerable<string> MethodStrings(ExplanationPayload p)
    {
        yield return p.Method.Formula;
        yield return p.Method.ScoredAreaCount.ToString(CultureInfo.InvariantCulture);
        if (p.Method.SensitivitySamples is { } s) yield return s.ToString(CultureInfo.InvariantCulture);
        if (p.Method.SensitivitySeed is { } seed) yield return seed.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParse(string token, out double value) =>
        double.TryParse(
            token.Replace(",", string.Empty),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}