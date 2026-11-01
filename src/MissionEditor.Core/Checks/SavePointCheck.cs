using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Story save points (Act_Save = target). Loading the save puts the target into the start set and sends MissionStart
/// while the save file is still being read (fl_decomp Story.cpp, PlayerLoad); ActivateMissionTrigger probes the
/// condition at once. Reported: a save made in space whose target never sets the start position
/// (Act_SetInitialPlayerPos – the original missions do this for nearly every save in space) as a note only: the save
/// writes the live ship position (fl_decomp server_pub.cpp, "pos"), and loading copies it to the start position
/// (server_savegame.cpp, PlayerData_ApplySavedCharacter) before PlayerLoad runs the target – without the action the
/// player starts where he saved. Also Act_NNPath or
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
          yield return new Finding (FindingSeverity.Info, Id, save.Line.Number, saver.Nickname,
              $"'{saver.Nickname}' speichert im Weltraum, das Speicherziel '{target.Nickname}' setzt keine Startposition (Act_SetInitialPlayerPos) – "
              + "nach dem Laden beginnt der Spieler dort, wo er beim Speichern war.",
              "Der Spielstand enthält die Position des Schiffs beim Speichern; dort beginnt der Spieler nach dem Laden. Act_SetInitialPlayerPos im "
              + "Speicherziel ersetzt sie durch eine feste Stelle. Die Originalmissionen setzen es in fast jedem Speicherziel im Weltraum "
              + "(z. B. m02 key_save_first_save_game), damit der Spieler an einer bekannten Stelle beginnt, etwa nicht mitten in Schiffen, die "
              + "das Speicherziel neu spawnt. Nötig ist es nicht. Für eine feste Stelle im Speicherziel "
              + "'Act_SetInitialPlayerPos = x, y, z, w, x, y, z' (Position und Drehung) eintragen.",
              target.LineNumber);
        }

        if (target.Condition?.IsKey ("Cnd_True") != true || !reported.Add (target))
          continue;
        // A lost waypoint leaves the player without direction after loading; a lost popup is only a message – in the
        // Crossfire missionNsave targets it repeats the "mission completed" popup the player already saw.
        foreach (IniLine lost in new[] { "Act_NNPath", "Act_PopUpDialog" }
                     .Select (key => target.Actions.FirstOrDefault (action => action.IsKey (key))).OfType<IniLine> ())
        {
          bool waypoint = lost.IsKey ("Act_NNPath");
          bool shownWhenSaving = !waypoint && saver.Actions.Any (action => action.IsKey ("Act_PopUpDialog") && action.RawValue == lost.RawValue);
          yield return new Finding (waypoint ? FindingSeverity.Warning : FindingSeverity.Info, Id, lost.Number, target.Nickname,
              $"{lost.Key} im Speicherziel '{target.Nickname}' (Cnd_True) läuft beim Laden des Speicherpunkts von '{saver.Nickname}' sofort, noch während "
              + "der Spielstand gelesen wird – "
              + (waypoint
                  ? "der Wegpunkt fehlt dann nach dem Laden."
                  : "das Popup erscheint dann nicht." + (shownWhenSaving ? " Beim Speichern hat der Spieler es schon gesehen; es fehlt nur die Wiederholung." : string.Empty)),
              "Beim Laden schickt die Engine MissionStart, noch während sie den Spielstand liest, und prüft das Speicherziel dabei sofort. "
              + "Abhilfe: " + (waypoint ? "den Wegpunkt" : "das Popup") + " in einen Trigger verschieben, den das Speicherziel per Act_ActTrig "
              + "aktiviert (der läuft erst im Spiel), oder statt Cnd_True 'Cnd_Timer = 1, 1, RUN_ON_BASE' verwenden.",
              save.Line.Number);
        }
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
