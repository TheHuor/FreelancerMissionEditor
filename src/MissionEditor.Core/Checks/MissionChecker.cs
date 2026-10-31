using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>Runs all checks over a script.</summary>
public static class MissionChecker
{
    /// <summary>All available checks.</summary>
    public static IReadOnlyList<IMissionCheck> AllChecks { get; } =
    [
        new UnknownTriggerTargetCheck(),
        new DuplicateNicknameCheck(),
        new TriggerStructureCheck(),
        new UnreachableTriggerCheck(),
        new UnknownObjectCheck(),
        new ImpossibleConditionCheck(),
        new UnusedObjectCheck(),
        new MissionEndCheck(),
    ];

    /// <summary>Runs every check and returns the findings sorted by line.</summary>
    public static IReadOnlyList<Finding> Run(MissionScript script) =>
        AllChecks.SelectMany(check => check.Run(script)).OrderBy(finding => finding.LineNumber).ToArray();
}
