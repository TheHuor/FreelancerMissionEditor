using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>Runs all checks over a script.</summary>
public static class MissionChecker
{
  /// <summary>The checks that need nothing but the script; the game data check is added when game data is known.</summary>
  public static IReadOnlyList<IMissionCheck> AllChecks
  {
    get;
  } =
  [
    new UnknownTriggerTargetCheck (),
    new DuplicateNicknameCheck (),
    new TriggerStructureCheck (),
    new UnreachableTriggerCheck (),
    new UnknownObjectCheck (),
    new ImpossibleConditionCheck (),
    new UnusedObjectCheck (),
    new MissionEndCheck (),
  ];

  /// <summary>Runs every check with the game data of the installation the script's file lies in, sorted by line.</summary>
  public static IReadOnlyList<Finding> Run (MissionScript script) => Run (script, GameData.ForMission (script.FilePath));

  /// <summary>Runs every check, the game data check with the given game data (none: skipped), sorted by line.</summary>
  public static IReadOnlyList<Finding> Run (MissionScript script, GameData? gameData)
  {
    IEnumerable<IMissionCheck> checks = gameData is null ? AllChecks : AllChecks.Concat ([new GameDataCheck (gameData), new ResourceRenderingCheck (gameData)]);
    return checks.SelectMany (check => check.Run (script)).OrderBy (finding => finding.LineNumber).ToArray ();
  }
}
