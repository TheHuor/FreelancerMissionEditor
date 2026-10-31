using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Nicknames of the game (systems, bases, factions, ship and solar archetypes, loadouts, equipment, NPC
/// ship archetypes) that the installation does not define. Runs only when game data is known (the configured
/// game folder or the installation the mission lies in); a wrong nickname there fails silently or crashes the game.
/// </summary>
public sealed class GameDataCheck : IMissionCheck
{
  // Object sections: (section, key, value index, kind).
  private static readonly (string Section, string Key, int Index, GameDataKind Kind)[] SectionFields =
  [
      ("NPC", "affiliation", 0, GameDataKind.Faction),
        ("MsnSolar", "archetype", 0, GameDataKind.SolarArchetype),
        ("MsnSolar", "loadout", 0, GameDataKind.Loadout),
        ("MsnSolar", "faction", 0, GameDataKind.Faction),
        ("MsnSolar", "system", 0, GameDataKind.System),
        ("MsnSolar", "base", 0, GameDataKind.Base),
        ("MsnLoot", "archetype", 0, GameDataKind.Equipment),
    ];

  // Trigger lines: (key, value index, kind).
  private static readonly (string Key, int Index, GameDataKind Kind)[] TriggerFields =
  [
      ("Cnd_BaseEnter", 0, GameDataKind.Base),
        ("Cnd_BaseExit", 0, GameDataKind.Base),
        ("Act_ForceLand", 0, GameDataKind.Base),
        ("Act_SetRep", 1, GameDataKind.Faction),
        ("Act_SetShipAndLoadout", 0, GameDataKind.ShipArchetype),
        ("Act_SetShipAndLoadout", 1, GameDataKind.Loadout),
    ];

  private const string SharedNpcShipFile = @"missions\npcships.ini";

  // Placeholders the scripts use instead of a nickname (e.g. "Cnd_BaseEnter = ANY", "Act_SetShipAndLoadout = none, none").
  private static readonly HashSet<string> Keywords = new (StringComparer.OrdinalIgnoreCase) { "any", "none" };

  private readonly GameData _gameData;

  /// <summary>Creates the check for the game data of an installation (see <see cref="GameData.Locate"/>).</summary>
  public GameDataCheck (GameData gameData) => _gameData = gameData;

  /// <inheritdoc/>
  public string Id => CheckId;

  private const string CheckId = "unknown-game-nickname";

  /// <inheritdoc/>
  public string Title => "Unbekannter Spiel-Nickname";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    GameData gameData = _gameData;

    foreach (ScriptObject item in script.Objects)
    {
      string sectionName = ScriptObject.SectionNameOf (item.Kind);
      foreach ((string _, string key, int index, GameDataKind kind) in SectionFields.Where (field => field.Section.Equals (sectionName, StringComparison.OrdinalIgnoreCase)))
      {
        foreach (IniLine line in item.Section.FindAll (key))
        {
          if (Check (gameData, kind, line, index, item.Nickname) is { } finding)
            yield return finding;
        }
      }
    }

    // NPC ship archetypes: the mission's own npc_ship_file first, then missions\npcships.ini; an unknown one
    // makes the engine skip the ship when spawning.
    string? npcShipFile = script.Document.Sections.FirstOrDefault (section => section.Is ("Mission"))?.Find ("npc_ship_file")?.GetValue (0);
    IReadOnlySet<string>? missionShips = string.IsNullOrEmpty (npcShipFile) ? new HashSet<string> () : gameData.NpcShipArchetypes (npcShipFile);
    IReadOnlySet<string>? sharedShips = gameData.NpcShipArchetypes (SharedNpcShipFile);
    if (missionShips is not null && sharedShips is not null)
    {
      string sources = string.IsNullOrEmpty (npcShipFile) ? SharedNpcShipFile : $"{npcShipFile} noch in {SharedNpcShipFile}";
      foreach (ScriptObject npc in script.Objects.Where (item => item.Kind == ScriptObjectKind.Npc))
      {
        foreach (IniLine line in npc.Section.FindAll ("npc_ship_arch"))
        {
          string value = line.GetValue (0);
          if (value.Length == 0 || missionShips.Contains (value) || sharedShips.Contains (value))
            continue;
          yield return new Finding (FindingSeverity.Error, Id, line.Number, npc.Nickname,
              $"npc_ship_arch '{value}' steht weder in {sources} – Schiffe dieses NPC werden nicht gespawnt.{Suggest (value, missionShips.Concat (sharedShips))}",
              "Die Engine sucht das NPC-Schiff erst in der Datei, die [Mission] mit npc_ship_file nennt, dann in missions\\npcships.ini; "
              + "ohne Treffer überspringt sie das Schiff beim Spawnen ohne Meldung. Abhilfe: Namen korrigieren oder den [NPCShipArch] anlegen.");
        }
      }
    }

    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      foreach (IniLine line in trigger.Actions.Append (trigger.Condition).OfType<IniLine> ())
      {
        foreach ((string key, int index, GameDataKind kind) in TriggerFields.Where (field => line.IsKey (field.Key)))
        {
          if (Check (gameData, kind, line, index, trigger.Nickname) is { } finding)
            yield return finding;
        }
      }
    }
  }

  private static Finding? Check (GameData gameData, GameDataKind kind, IniLine line, int index, string subject)
  {
    string value = line.GetValue (index);
    if (value.Length == 0 || gameData.Contains (kind, value) || Keywords.Contains (value))
      return null;
    return new Finding (FindingSeverity.Error, CheckId, line.Number, subject,
        $"{line.Key} = {line.RawValue}: {NameOf (kind)} '{value}' gibt es im Spiel nicht.{Suggest (value, gameData.Nicknames (kind))}",
        $"Abgeglichen mit {SourceOf (kind)} der Installation {gameData.Root}. Unbekannte Nicknames übergeht das Spiel still oder es stürzt ab "
        + "(z. B. bei Archetypen und Loadouts). Abhilfe: Schreibweise prüfen; Nicknames werden ohne Groß-/Kleinschreibung verglichen.");
  }

  private static string Suggest (string value, IEnumerable<string> names)
  {
    IReadOnlyList<string> suggestions = NameSimilarity.FindClosest (value, names);
    return suggestions.Count > 0 ? $" Gemeint vielleicht {string.Join (" oder ", suggestions.Select (name => $"'{name}'"))}?" : string.Empty;
  }

  private static string NameOf (GameDataKind kind) => kind switch
  {
    GameDataKind.System => "das System",
    GameDataKind.Base => "die Basis",
    GameDataKind.Faction => "die Fraktion",
    GameDataKind.ShipArchetype => "den Schiffstyp",
    GameDataKind.SolarArchetype => "den Stationstyp",
    GameDataKind.Loadout => "das Loadout",
    _ => "die Ausrüstung",
  };

  private static string SourceOf (GameDataKind kind) => kind switch
  {
    GameDataKind.System or GameDataKind.Base => "universe.ini",
    GameDataKind.Faction => "InitialWorld.ini",
    GameDataKind.ShipArchetype => "den Schiffsdateien (shiparch.ini)",
    GameDataKind.SolarArchetype => "solararch.ini",
    GameDataKind.Loadout => "den Loadout-Dateien",
    _ => "den Ausrüstungsdateien",
  };
}
