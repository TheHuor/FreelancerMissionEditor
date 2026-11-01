using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Story save points (Act_Save = target): loading the save activates only the target trigger. Reported: a save made
/// in space whose target never sets the start position (Act_SetInitialPlayerPos – the original missions do this for
/// nearly every save in space), and Act_NNPath or Act_PopUpDialog in a Cnd_True trigger that runs right at loading,
/// before the player has a system: the waypoint or popup is lost.
/// </summary>
public sealed class SavePointCheck : IMissionCheck
{
  private static readonly string[] BaseConditions = ["Cnd_BaseEnter", "Cnd_LocEnter", "Cnd_LocExit"];

  /// <inheritdoc/>
  public string Id => "save-point";

  /// <inheritdoc/>
  public string Title => "Speicherpunkt";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    var reported = new HashSet<Trigger> ();
    foreach (Trigger saver in script.EffectiveTriggers.Where (flow.Reachable.Contains))
    {
      foreach (TriggerReference save in saver.References.Where (reference => reference.Kind == TriggerReferenceKind.Save && reference.Target is not null))
      {
        Trigger target = save.Target!;
        // What runs at once when the save is loaded: the target if it has Cnd_True, and Cnd_True triggers it activates.
        List<Trigger> atLoad = [target, .. ImmediateFollowers (target)];

        if (!OnBase (flow, saver, 3) && !atLoad.Any (step => step.Actions.Any (action => action.IsKey ("Act_SetInitialPlayerPos"))))
        {
          yield return new Finding (FindingSeverity.Warning, Id, save.Line.Number, saver.Nickname,
              $"'{saver.Nickname}' speichert im Weltraum, aber das Speicherziel '{target.Nickname}' setzt keine Startposition (Act_SetInitialPlayerPos).",
              "Beim Laden aktiviert die Engine nur das Speicherziel; wo der Spieler steht, legt Act_SetInitialPlayerPos fest. Die Originalmissionen "
              + "setzen es in fast jedem Speicherziel im Weltraum (z. B. m02 key_save_first_save_game). Abhilfe: im Speicherziel "
              + "'Act_SetInitialPlayerPos = x, y, z, w, x, y, z' (Position und Drehung) eintragen.",
              target.LineNumber);
        }

        foreach (Trigger step in atLoad.Where (step => step.Condition?.IsKey ("Cnd_True") == true && reported.Add (step)))
        {
          if (step.Actions.FirstOrDefault (action => action.IsKey ("Act_NNPath") || action.IsKey ("Act_PopUpDialog")) is not { } lost)
            continue;
          yield return new Finding (FindingSeverity.Warning, Id, lost.Number, step.Nickname,
              $"{lost.Key} in '{step.Nickname}' läuft beim Laden des Speicherpunkts von '{saver.Nickname}' sofort (Cnd_True) – der Spieler ist da noch "
              + "in keinem System, der Wegpunkt bzw. das Popup geht verloren.",
              "Ein aktivierter Trigger wird sofort geprüft; beim Laden geschieht das, bevor der Spieler ein System hat (StoryChain-Spieltest "
              + "2026-10-30). Abhilfe: statt Cnd_True 'Cnd_Timer = 1, 1, RUN_ON_BASE' oder eine Bedingung wie Cnd_SpaceEnter/Cnd_PlayerLaunch verwenden.",
              save.Line.Number);
        }
      }
    }
  }

  // Cnd_True triggers the target activates; they fire in the same moment.
  private static IEnumerable<Trigger> ImmediateFollowers (Trigger target) =>
      target.Condition?.IsKey ("Cnd_True") == true
        ? target.References.Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target?.Condition?.IsKey ("Cnd_True") == true)
            .Select (reference => reference.Target!)
        : [];

  // True when the trigger fires while the player is on a base: a base condition, or Cnd_True/Cnd_Timer reached only from such triggers.
  private static bool OnBase (MissionFlow flow, Trigger trigger, int depth)
  {
    if (trigger.Condition is not { } condition)
      return false;
    if (BaseConditions.Any (condition.IsKey))
      return true;
    if (depth == 0 || !(condition.IsKey ("Cnd_True") || condition.IsKey ("Cnd_Timer")))
      return false;
    Trigger[] activators = flow.ActivatorsOf (trigger).ToArray ();
    return activators.Length > 0 && activators.All (activator => OnBase (flow, activator, depth - 1));
  }
}
