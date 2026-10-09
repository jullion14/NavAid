using Microsoft.AspNetCore.Mvc;

namespace GeoDSS.Api.Models;

public enum VerificationOutcome
{
    /// <summary>Every figure traced back to a supplied value.</summary>
    Verified,

    /// <summary>At least one figure or claim could not be traced. Show, but mark.</summary>
    Flagged
}

public enum FindingSeverity
{
    /// <summary>Correctness problem. Forces the Flagged outcome.</summary>
    Error,

    /// <summary>Worth logging and reporting, but not a correctness failure.</summary>
    Warning
}

public sealed class VerificationReport
{
    public required VerificationOutcome Outcome { get; init; }
    public required int FiguresChecked { get; init; }
    public required int FiguresMatched { get; init; }
    public required int LedgerSize { get; init; }
    public required IReadOnlyList<VerificationFinding> Findings { get; init; }

    /// <summary>Badge text for the panel, e.g. "9 of 9 figures matched the computed values".</summary>
    public string Summary
    {
        get
        {
            var untraced = FiguresChecked - FiguresMatched;

            if (Outcome == VerificationOutcome.Verified)
                return $"{FiguresMatched} of {FiguresChecked} figures matched the computed values";

            return untraced > 0
                ? $"{untraced} of {FiguresChecked} figures could not be traced to a computed value"
                : $"{FiguresMatched} of {FiguresChecked} figures matched, but a citation could not be resolved";
        }
    }
}

public sealed class VerificationFinding
{
    public required FindingSeverity Severity { get; init; }

    /// <summary>unknown-figure | unparseable-figure | format-drift | unknown-fact-id | forbidden-claim</summary>
    public required string Kind { get; init; }

    public required string Text { get; init; }
    public required string Message { get; init; }

    /// <summary>Character offset into the model output, for highlighting in the panel.</summary>
    public int? Start { get; init; }
    public int? Length { get; init; }
}
