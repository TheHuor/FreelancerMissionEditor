using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// References to trigger nicknames that no [Trigger] defines. The engine ignores such an
/// Act_ActTrig/Act_DeactTrig silently; a Cnd_WatchTrigger never fires and an Act_Save resumes
/// into nothing. A similar existing nickname is suggested.
/// </summary>
public sealed class UnknownTriggerTargetCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "unknown-trigger";

  /// <inheritdoc/>
  public string Title => "Unbekanntes Trigger-Ziel";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    string[] names = script.EffectiveTriggers.Select (trigger => trigger.Nickname).ToArray ();
    foreach (Trigger trigger in script.Triggers)
    {
      foreach (TriggerReference reference in trigger.References)
      {
        if (reference.Target is not null)
          continue;
        FindingSeverity severity = trigger.IsShadowed ? FindingSeverity.Info
            : reference.Kind == TriggerReferenceKind.Deactivate ? FindingSeverity.Warning
            : FindingSeverity.Error;
        string effect = reference.Kind switch
        {
          TriggerReferenceKind.Activate => "die Aktivierung passiert nie",
          TriggerReferenceKind.Deactivate => "die Deaktivierung hat keine Wirkung",
          TriggerReferenceKind.Watch => "die Bedingung wird nie erfüllt",
          _ => "der Spielstand setzt an keinem Trigger fort",
        };
        string message = $"{TriggerReference.KeyOf (reference.Kind)} auf unbekannten Trigger '{reference.TargetName}' – {effect}.";
        // A name of another section type: the line means something the trigger actions cannot do. Then that section was
        // meant, and similar trigger names would only mislead.
        ScriptObject? other = script.Objects.FirstOrDefault (item => item.Nickname.Equals (reference.TargetName, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<string> suggestions = other is null ? NameSimilarity.FindClosest (reference.TargetName, names) : [];
        // For a switch-off, a similar name the same trigger already switches off was not meant (a, b, "bc", d, e → c).
        if (reference.Kind == TriggerReferenceKind.Deactivate)
        {
          string[] open = suggestions.Where (name => !trigger.References.Any (other => other.Kind == TriggerReferenceKind.Deactivate
              && other.TargetName.Equals (name, StringComparison.OrdinalIgnoreCase))).ToArray ();
          if (open.Length > 0)
            suggestions = open;
        }
        if (suggestions.Count > 0)
          message += $" Gemeint vielleicht {string.Join (" oder ", suggestions.Select (name => $"'{name}'"))}?";
        if (other is not null)
          message += $" '{reference.TargetName}' ist eine [{ScriptObject.SectionNameOf (other.Kind)}] (Zeile {other.LineNumber}), kein Trigger.";
        // Act_DeactTrig on a random encounter that the same trigger also switches off properly (Act_DisableEnc, as in the
        // original m13 watch_endgame_object) is only a superfluous line.
        IniLine? disabled = reference.Kind == TriggerReferenceKind.Deactivate && other?.Kind == ScriptObjectKind.RandomEncounter
            ? trigger.Actions.FirstOrDefault (action => action.IsKey ("Act_DisableEnc") && action.GetValue (0).Equals (reference.TargetName, StringComparison.OrdinalIgnoreCase))
            : null;
        if (disabled is not null)
        {
          severity = FindingSeverity.Info;
          message += $" Derselbe Trigger schaltet den Zufallskampf in Zeile {disabled.Number} mit Act_DisableEnc ab – die Zeile ist nur überflüssig.";
        }
        // Likewise on an object list when the same trigger gives every ship that uses it other orders or destroys it
        // (the cleanup trigger cfmission5-38 of the Crossfire campaign).
        IniLine? replaced = reference.Kind == TriggerReferenceKind.Deactivate && other?.Kind == ScriptObjectKind.ObjList
            ? OrdersEnded (script, trigger, reference.TargetName)
            : null;
        if (replaced is not null)
        {
          severity = FindingSeverity.Info;
          message += $" Derselbe Trigger beendet die Befehle der Schiffe mit dieser Liste ohnehin ({replaced.Key} in Zeile {replaced.Number}) – die Zeile ist nur überflüssig.";
        }
        // A misspelt Act_DeactTrig is not harmless when the trigger it meant keeps running and spawns or repeats. Which
        // of several similar names was meant is the author's call, so all running ones are named. It stays a warning:
        // the mission goes on, only the leftover spawns or repeats are unwanted.
        string[] keepRunning = reference.Kind != TriggerReferenceKind.Deactivate || disabled is not null ? []
            : suggestions.Select (name => script.FindTrigger (name)).OfType<Trigger> ()
                .Where (candidate => candidate.Repeatable || candidate.Actions.Any (action => action.Key!.StartsWith ("Act_Spawn", StringComparison.OrdinalIgnoreCase)))
                .Select (candidate => $"'{candidate.Nickname}'").ToArray ();
        bool meantKeepsRunning = keepRunning.Length > 0;
        if (meantKeepsRunning && !trigger.IsShadowed)
        {
          message += keepRunning.Length == 1
              ? $" Ist {keepRunning[0]} gemeint, bleibt er dadurch aktiv und spawnt bzw. wiederholt weiter."
              : $" Welcher auch gemeint ist ({string.Join (", ", keepRunning)}), er bleibt dadurch aktiv und spawnt bzw. wiederholt weiter.";
        }
        if (trigger.IsShadowed)
          message += " (Der Trigger ist doppelt definiert und wird ohnehin ignoriert.)";
        string hint = reference.Kind switch
        {
          TriggerReferenceKind.Activate => "Act_ActTrig auf einen Namen ohne [Trigger] ist im Spiel ein stiller No-op – es gibt keine Fehlermeldung, der Ablauf geht dort einfach nicht weiter.",
          TriggerReferenceKind.Deactivate when replaced is not null =>
            "Act_DeactTrig schaltet nur Trigger ab; die Befehlsliste beendet hier schon das Act_GiveObjList bzw. Act_Destroy desselben Triggers.",
          TriggerReferenceKind.Deactivate when other?.Kind == ScriptObjectKind.ObjList =>
            "Act_DeactTrig schaltet nur Trigger ab; eine laufende Befehlsliste beendet es nicht. Dafür dem Schiff per Act_GiveObjList eine andere [ObjList] geben.",
          TriggerReferenceKind.Deactivate when disabled is not null =>
            "Act_DeactTrig schaltet nur Trigger ab; den Zufallskampf schaltet hier schon das Act_DisableEnc desselben Triggers ab.",
          TriggerReferenceKind.Deactivate when other?.Kind == ScriptObjectKind.RandomEncounter =>
            "Act_DeactTrig schaltet nur Trigger ab; einen Zufallskampf ([MsnRandEnc]) schaltet Act_DisableEnc ab.",
          TriggerReferenceKind.Deactivate when meantKeepsRunning =>
            "Act_DeactTrig auf einen Namen ohne [Trigger] tut nichts – der vermutlich gemeinte Trigger läuft weiter, mit allen Spawns bzw. Wiederholungen.",
          TriggerReferenceKind.Deactivate => "Act_DeactTrig auf einen Namen ohne [Trigger] ist harmlos, aber meist ein Tippfehler oder ein Überbleibsel eines gelöschten Triggers.",
          TriggerReferenceKind.Watch => "Cnd_WatchTrigger wartet dann für immer; der Trigger löst nie aus.",
          _ => "Beim Laden dieses Spielstands wird kein Trigger aktiviert – die Mission steht still.",
        };
        // The remedy follows from what the name is: a superfluous line goes, another section needs the matching action.
        hint += disabled is not null || replaced is not null
            ? " Abhilfe: die Zeile entfernen – am Spielverhalten ändert sich nichts."
            : other is not null
              ? " Abhilfe: die passende Aktion verwenden oder die Zeile entfernen."
              : " Trigger-Namen vergleicht die Engine ohne Groß-/Kleinschreibung. Abhilfe: Namen korrigieren oder den fehlenden [Trigger] anlegen"
                + (reference.Kind == TriggerReferenceKind.Deactivate ? " bzw. die Zeile entfernen." : ".");
        yield return new Finding (severity, Id, reference.Line.Number, trigger.Nickname, message, hint);
      }
    }
  }

  // The line in the trigger that ends the orders of every ship using the object list (another Act_GiveObjList or
  // Act_Destroy on it), or null when a ship keeps them or no ship gets the list. Only ships that got the list before –
  // in a trigger on the way to this one – count; ships that get it later (e.g. in the next mission) do not follow it yet.
  private static IniLine? OrdersEnded (MissionScript script, Trigger trigger, string objList)
  {
    // The way there may also run over a save point; when no ship gets the list on the Act_ActTrig way, all ships count.
    HashSet<string> ships = ShipsWithList (script, Upstream (script, trigger), objList);
    if (ships.Count == 0)
      ships = ShipsWithList (script, script.EffectiveTriggers, objList);
    if (ships.Count == 0)
      return null;
    IniLine? first = null;
    foreach (string ship in ships)
    {
      IniLine? ending = trigger.Actions.FirstOrDefault (action => action.GetValue (0).Equals (ship, StringComparison.OrdinalIgnoreCase)
          && (action.IsKey ("Act_Destroy") || action.IsKey ("Act_GiveObjList") && !action.GetValue (1).Equals (objList, StringComparison.OrdinalIgnoreCase)));
      if (ending is null)
        return null;
      first ??= ending;
    }
    return first;
  }

  // The ships the given triggers hand the object list to (Act_GiveObjList, spawn with the list, init_objectives of a spawned ship).
  private static HashSet<string> ShipsWithList (MissionScript script, IEnumerable<Trigger> givers, string objList)
  {
    var ships = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
    var spawned = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
    foreach (Trigger giver in givers)
    {
      foreach (IniLine action in giver.Actions)
      {
        if (action.IsKey ("Act_GiveObjList") && action.GetValue (1).Equals (objList, StringComparison.OrdinalIgnoreCase)
            || (action.IsKey ("Act_SpawnShip") || action.IsKey ("Act_SpawnShipRel")) && action.GetValue (1).Equals (objList, StringComparison.OrdinalIgnoreCase))
          ships.Add (action.GetValue (0));
        if (action.IsKey ("Act_SpawnShip") || action.IsKey ("Act_SpawnShipRel"))
          spawned.Add (action.GetValue (0));
      }
    }
    foreach (ScriptObject ship in script.Objects.Where (item => item.Kind == ScriptObjectKind.Ship && spawned.Contains (item.Nickname)))
    {
      if (ship.Section.FindAll ("init_objectives").Any (line => line.GetValue (0).Equals (objList, StringComparison.OrdinalIgnoreCase)))
        ships.Add (ship.Nickname);
    }
    return ships;
  }

  // The trigger itself and every effective trigger that leads to it over Act_ActTrig.
  private static HashSet<Trigger> Upstream (MissionScript script, Trigger trigger)
  {
    var activators = script.EffectiveTriggers
        .SelectMany (source => source.References.Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is not null)
            .Select (reference => (Source: source, Target: reference.Target!)))
        .ToLookup (edge => edge.Target, edge => edge.Source);
    var upstream = new HashSet<Trigger> { trigger };
    var queue = new Queue<Trigger> ([trigger]);
    while (queue.TryDequeue (out Trigger? current))
    {
      foreach (Trigger source in activators[current])
      {
        if (upstream.Add (source))
          queue.Enqueue (source);
      }
    }
    return upstream;
  }
}
