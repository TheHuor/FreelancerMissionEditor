using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Objects of distance conditions that the engine cannot find. Cnd_DistShip resolves both objects by name on every
/// check (fl_decomp MissionObject.cpp, FMissionObject::GetLabelObjectId): "Player", a mission ship or formation
/// (stricmp), a mission loot or solar (strcmp), and last a universe object of that nickname in the player's current
/// system (pub::GetNicknameId + IsSpaceObjTypeSet). The "label" entries of ships are not looked up by name, and a
/// [Zone] is no space object. Cnd_DistCircle takes its ship only from "Player", mission ships and formations
/// (GetLabelShipId) and its circle from a universe object (pub::GetNicknameId). An object that resolves to nothing
/// leaves the condition false for good – there is no fallback. Runs only with game data (the system INIs).
/// </summary>
public sealed class DistanceTargetCheck : IMissionCheck
{
  private readonly GameData _gameData;

  /// <summary>Creates the check for the game data of an installation (see <see cref="GameData.Locate"/>).</summary>
  public DistanceTargetCheck (GameData gameData) => _gameData = gameData;

  /// <inheritdoc/>
  public string Id => "distance-target";

  /// <inheritdoc/>
  public string Title => "Ziel einer Entfernungsbedingung";

  // What a name has to be at one argument position.
  private enum TargetRole
  {
    // Cnd_DistShip: Player, mission ship, formation, loot or solar, or a universe object in the current system.
    AnyObject,
    // Cnd_DistCircle, first value: Player, mission ship or formation.
    Ship,
    // Cnd_DistCircle, second value: a universe object.
    UniverseObject,
  }

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      foreach (IniLine condition in trigger.Conditions)
      {
        IEnumerable<(string Name, TargetRole Role)> targets = condition.Key!.ToLowerInvariant () switch
        {
          "cnd_distship" => [(condition.GetValue (1), TargetRole.AnyObject), (condition.GetValue (2), TargetRole.AnyObject)],
          "cnd_distcircle" => [(condition.GetValue (0), TargetRole.Ship), (condition.GetValue (1), TargetRole.UniverseObject)],
          _ => [],
        };
        foreach ((string name, TargetRole role) in targets)
        {
          if (name.Length == 0 || name.Equals ("Player", StringComparison.OrdinalIgnoreCase))
            continue;
          if (Assess (script, trigger, condition, name, role) is not { } problem)
            continue;
          // In a trigger that never becomes active the condition does nothing anyway.
          bool dead = !flow.Reachable.Contains (trigger);
          string message = $"{condition.Key} in '{trigger.Nickname}': '{name}' {problem.Message}"
              + (dead ? $" (Der Trigger '{trigger.Nickname}' wird ohnehin nie aktiviert.)" : string.Empty);
          yield return new Finding (dead ? FindingSeverity.Info : problem.Severity, Id, condition.Number, trigger.Nickname, message, problem.Hint, problem.RelatedLine);
        }
      }
    }
  }

  private sealed record Problem (FindingSeverity Severity, string Message, string Hint, int? RelatedLine = null);

  private Problem? Assess (MissionScript script, Trigger trigger, IniLine condition, string name, TargetRole role)
  {
    string never = role == TargetRole.UniverseObject ? "die Bedingung löst nie aus" : "die Entfernung wird nie gemessen, die Bedingung löst nie aus";
    if (role != TargetRole.UniverseObject)
    {
      // Ships and formations by name in any case; loot and solars only with the exact spelling.
      ScriptObject[] ships = script.Objects.Where (item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation or ScriptObjectKind.RandomEncounter
          && item.Nickname.Equals (name, StringComparison.OrdinalIgnoreCase)).ToArray ();
      if (ships.Length > 0)
        return null;
      ScriptObject[] placed = script.Objects.Where (item => item.Kind is ScriptObjectKind.Loot or ScriptObjectKind.Solar
          && item.Nickname.Equals (name, StringComparison.OrdinalIgnoreCase)).ToArray ();
      if (role == TargetRole.AnyObject && placed.Any (item => item.Nickname == name))
        return null;
      if (placed.FirstOrDefault () is { } other)
      {
        return role == TargetRole.Ship
            ? new Problem (FindingSeverity.Error, $"ist ein [{ScriptObject.SectionNameOf (other.Kind)}] (Zeile {other.LineNumber}), kein Schiff – {never}.",
                "Cnd_DistCircle misst nur den Spieler, ein [MsnShip] oder eine [MsnFormation]. Abhilfe: das Schiff angeben oder für feste Objekte "
                + "Cnd_DistShip bzw. Cnd_DistVec verwenden.", other.LineNumber)
            : new Problem (FindingSeverity.Error, $"gibt es nur als '{other.Nickname}' (Zeile {other.LineNumber}) – bei [{ScriptObject.SectionNameOf (other.Kind)}] muss die Schreibweise exakt stimmen, {never}.",
                $"Loot und Solare der Mission werden mit Groß-/Kleinschreibung gesucht. Abhilfe: '{other.Nickname}' schreiben.", other.LineNumber);
      }
      // An [NPC] only describes pilots; the ship that uses it has its own nickname.
      if (script.Objects.FirstOrDefault (item => item.Kind == ScriptObjectKind.Npc && item.Nickname.Equals (name, StringComparison.OrdinalIgnoreCase)) is { } npc)
      {
        string[] users = script.Objects.Where (item => item.Kind == ScriptObjectKind.Ship
                && item.Section.Find ("NPC")?.GetValue (0).Equals (npc.Nickname, StringComparison.OrdinalIgnoreCase) == true)
            .Select (item => $"'{item.Nickname}'").ToArray ();
        return new Problem (FindingSeverity.Error, $"ist ein [NPC] (Zeile {npc.LineNumber}), kein Schiff – {never}."
                + (users.Length > 0 ? $" Schiffe mit diesem NPC: {string.Join (", ", users)}." : " Kein [MsnShip] dieser Mission benutzt diesen NPC."),
            "Entfernungsbedingungen finden Schiffe über den Nickname des [MsnShip] oder der [MsnFormation], nicht über den [NPC]. "
            + "Abhilfe: den Nickname des Schiffs angeben" + (users.Length == 0 ? " und das Schiff in dieser Mission anlegen." : "."), npc.LineNumber);
      }
      // A "label" of mission objects is no name the lookup knows.
      ScriptObject? labelled = script.Objects.FirstOrDefault (item => item.Section.Entries.Any (entry => entry.IsKey ("label")
          && entry.GetValue (0).Equals (name, StringComparison.OrdinalIgnoreCase)));
      if (labelled is not null)
      {
        return new Problem (FindingSeverity.Warning, $"ist nur ein label (z. B. von '{labelled.Nickname}', Zeile {labelled.LineNumber}) – über ein label wird kein Objekt gefunden, {never}.",
            "Entfernungsbedingungen suchen das Objekt über seinen Nickname (Schiff, Formation, Loot, Solar oder ein Objekt im System), nicht über "
            + "label-Einträge. Abhilfe: den Nickname eines Schiffs oder der Formation angeben.", labelled.LineNumber);
      }
      if (role == TargetRole.Ship)
        return Unknown (script, name, "[MsnShip] oder [MsnFormation]", never, onlyShips: true);
    }

    // A universe object: [Object] of a system INI; a [Zone] is no object.
    IReadOnlyList<SpaceNickname> found = _gameData.FindSpaceNickname (name);
    SpaceNickname[] objects = found.Where (item => !item.IsZone).ToArray ();
    if (objects.Length == 0 && found.FirstOrDefault (item => item.IsZone) is { } zone)
    {
      return new Problem (FindingSeverity.Error, $"ist eine Zone in {zone.System} ({Path.GetFileName (zone.FilePath)}, Zeile {zone.LineNumber}), kein Objekt – {never}.",
          "Zonen sind keine Objekte im Raum; eine Entfernung zu ihnen gibt es nicht. Abhilfe: Cnd_DistVec mit der Position der Zone verwenden "
          + "(pos aus der System-INI) oder ein Objekt in der Zone angeben.");
    }
    // With system INIs missing from the installation an unknown name may still be a universe object.
    if (objects.Length == 0 && !_gameData.HasAllSystemFiles)
      return null;
    if (objects.Length == 0)
      return Unknown (script, name, role == TargetRole.UniverseObject ? "[Object] einer System-INI" : "Schiff, Formation, Loot, Solar der Mission oder [Object] einer System-INI", never, onlyShips: false);
    // The universe object counts only in the system the player is in.
    string? system = trigger.System;
    if (system is { Length: > 0 } && !objects.Any (item => item.System.Equals (system, StringComparison.OrdinalIgnoreCase)))
    {
      string where = string.Join (", ", objects.Select (item => item.System).Distinct (StringComparer.OrdinalIgnoreCase));
      return new Problem (FindingSeverity.Warning, $"liegt in {where}, der Trigger gehört zu {system} – gemessen wird nur, solange der Spieler im System des Objekts ist.",
          $"Objekte aus den System-INIs findet die Bedingung nur im System, in dem der Spieler gerade ist. Abhilfe: 'system = ' des Triggers "
          + "und das Objekt angleichen oder ein Objekt aus diesem System angeben.");
    }
    return null;
  }

  private Problem Unknown (MissionScript script, string name, string expected, string never, bool onlyShips)
  {
    IEnumerable<string> candidates = script.Objects
        .Where (item => onlyShips ? item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation
            : item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation or ScriptObjectKind.Loot or ScriptObjectKind.Solar)
        .Select (item => item.Nickname).Distinct (StringComparer.OrdinalIgnoreCase);
    IReadOnlyList<string> suggestions = NameSimilarity.FindClosest (name, candidates);
    return new Problem (FindingSeverity.Error,
        $"ist kein {expected} – {never}."
        + (suggestions.Count > 0 ? $" Gemeint vielleicht {string.Join (" oder ", suggestions.Select (suggestion => $"'{suggestion}'"))}?" : string.Empty),
        "Findet die Bedingung kein Objekt mit diesem Namen, bleibt sie falsch; es gibt keine Fehlermeldung und keinen Ersatz durch den Spieler. "
        + "Abhilfe: Namen korrigieren.");
  }
}
