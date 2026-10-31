namespace MissionEditor.Core.Checks;

/// <summary>How serious a finding is.</summary>
public enum FindingSeverity
{
    /// <summary>Worth a look, usually harmless.</summary>
    Info,

    /// <summary>Probably a mistake or without effect in the game.</summary>
    Warning,

    /// <summary>Breaks the mission or crashes the game.</summary>
    Error,
}

/// <summary>One result of a check.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="CheckId">Id of the check that found it.</param>
/// <param name="LineNumber">The line it refers to (1-based).</param>
/// <param name="Subject">The trigger or object nickname it concerns, if any.</param>
/// <param name="Message">The message for the user (German).</param>
/// <param name="Hint">Why it matters and how to fix it (German), if there is advice to give.</param>
public sealed record Finding(FindingSeverity Severity, string CheckId, int LineNumber, string? Subject, string Message, string? Hint = null);

/// <summary>A check over a mission script.</summary>
public interface IMissionCheck
{
    /// <summary>Short id, e.g. "unknown-trigger".</summary>
    string Id { get; }

    /// <summary>Title for the user (German).</summary>
    string Title { get; }

    /// <summary>Runs the check.</summary>
    IEnumerable<Finding> Run(Model.MissionScript script);
}
