using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Story save points (Act_Save = target). Loading the save puts the target into the start set and sends MissionStart
/// while the save file is still being read (fl_decomp Story.cpp, PlayerLoad); ActivateMissionTrigger probes the
/// condition at once. Reported: a save made in space whose target never sets the start position
/// (Act_SetInitialPlayerPos – the original missions do this for nearly every save in space), and Act_NNPath or
/// Act_PopUpDialog in a target with Cnd_True, which therefore runs before the player is in the game: the waypoint or
/// popup is lost. Triggers the target activates are only queued and run with the next event, in the game – they are fine.
/// </summary>
public sealed class SavePointCheck : IMissionCheck
{
  // Conditions that only occur on a base: docking, rooms, cutscenes after docking, mission offers, bar.
  private static readonly string[] BaseConditions = ["Cnd_BaseEnter", "Cnd_LocEnter", "Cnd_LocExit", "Cnd_RTCDone", "Cnd_MsnResponse", "Cnd_CharSelect", "Cnd_RumorHeard"];

  // Conditions that keep the place of the trigger that activated them.
  private static readonly string[] PassThroughConditions = ["Cnd_True", "Cnd_Timer", "Cnd_PopUpDialog"];

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
        // The start position may also be set by what the target switches on right after loading.
        IEnumerable<Trigger> afterLoad = target.References
            .Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is not null)
            .Select (reference => reference.Target!)
            .Prepend (target);
        if (!OnBase (flow, saver, 4) && !afterLoad.Any (step => step.Actions.Any (action => action.IsKey ("Act_SetInitialPlayerPos"))))
        {
          yield return new Finding (FindingSeverity.Warning, Id, save.Line.Number, saver.Nickname,
              $"'{saver.Nickname}' speichert im Weltraum, aber das Speicherziel '{target.Nickname}' setzt keine Startposition (Act_SetInitialPlayerPos).",
              "Beim Laden aktiviert die Engine nur das Speicherziel; wo der Spieler steht, legt Act_SetInitialPlayerPos fest. Die Originalmissionen "
              + "setzen es in fast jedem Speicherziel im Weltraum (z. B. m02 key_save_first_save_game). Abhilfe: im Speicherziel "
              + "'Act_SetInitialPlayerPos = x, y, z, w, x, y, z' (Position und Drehung) eintragen.",
              target.LineNumber);
        }

        if (target.Condition?.IsKey ("Cnd_True") != true || !reported.Add (target)
            || target.Actions.FirstOrDefault (action => action.IsKey ("Act_NNPath") || action.IsKey ("Act_PopUpDialog")) is not { } lost)
          continue;
        yield return new Finding (FindingSeverity.Warning, Id, lost.Number, target.Nickname,
            $"{lost.Key} im Speicherziel '{target.Nickname}' (Cnd_True) läuft beim Laden des Speicherpunkts von '{saver.Nickname}' sofort, noch während "
            + "der Spielstand gelesen wird – der Wegpunkt bzw. das Popup geht verloren.",
            "Beim Laden schickt die Engine MissionStart, noch während sie den Spielstand liest, und prüft das Speicherziel dabei sofort. "
            + "Abhilfe: Wegpunkt und Popup in "
            + "einen Trigger verschieben, den das Speicherziel per Act_ActTrig aktiviert (der läuft erst im Spiel), oder statt Cnd_True "
            + "'Cnd_Timer = 1, 1, RUN_ON_BASE' verwenden.",
            save.Line.Number);
      }
    }
  }

  // True when the trigger fires while the player is on a base: a base condition, or a pass-through condition reached only from such triggers.
  private static bool OnBase (MissionFlow flow, Trigger trigger, int depth)
  {
    if (trigger.Condition is not { } condition)
      return false;
    if (BaseConditions.Any (condition.IsKey))
      return true;
    if (depth == 0 || !PassThroughConditions.Any (condition.IsKey))
      return false;
    Trigger[] activators = flow.ActivatorsOf (trigger).ToArray ();
    return activators.Length > 0 && activators.All (activator => OnBase (flow, activator, depth - 1));
  }
}
