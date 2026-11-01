using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

public sealed partial class GameData
{
  private readonly Dictionary<(GameDataKind Kind, string Nickname), GameObjectDefinition> _definitions = [];
  private readonly List<string> _systemFiles = [];
  private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<IniSection>> _npcDefinitions = new (StringComparer.OrdinalIgnoreCase);

  /// <summary>A definition and its source, or null when this nickname is unknown for the requested kind.</summary>
  public GameObjectDefinition? Definition (GameDataKind kind, string nickname) =>
      _definitions.GetValueOrDefault ((kind, nickname.ToLowerInvariant ()));

  private static int? Id (IniSection section, string key) => int.TryParse (section.Find (key)?.GetValue (0), out int id) ? id : null;

  // Base names often live on [Object] in a system INI, not in universe.ini's [Base] declaration.
  private void ReadUniverseResourceDefinitions ()
  {
    foreach (string file in _systemFiles)
    {
      if (!File.Exists (file)) continue;
      foreach (IniSection section in IniDocument.Load (file).Sections.Where (section => section.Is ("Object")))
      {
        string? baseName = section.Find ("base")?.GetValue (0);
        if (baseName is null || !_nicknames[GameDataKind.Base].Contains (baseName)) continue;
        GameObjectDefinition? existing = Definition (GameDataKind.Base, baseName);
        _definitions[(GameDataKind.Base, baseName.ToLowerInvariant ())] = new (GameDataKind.Base, baseName, Path.GetFullPath (file),
            section.FirstLineNumber, Id (section, "ids_name") ?? existing?.NameIds, Id (section, "ids_info") ?? existing?.InfoIds);
      }
    }
  }

  /// <summary>Follow a mission ship's NPC, NPCShipArch and loadout to the ship type. Never treat its object nickname as a global type.</summary>
  public GameObjectDefinition? ResolveMissionShipType (IniDocument document, IniSection ship)
  {
    string? npcName = ship.Find ("NPC")?.GetValue (0);
    IniSection? npc = document.Sections.FirstOrDefault (section => section.Is ("NPC")
        && string.Equals (section.Find ("nickname")?.GetValue (0), npcName, StringComparison.OrdinalIgnoreCase));
    string? archName = npc?.Find ("npc_ship_arch")?.GetValue (0);
    string? loadoutName = ship.Find ("loadout")?.GetValue (0);
    if (!string.IsNullOrEmpty (archName))
    {
      string? own = document.Sections.FirstOrDefault (section => section.Is ("Mission"))?.Find ("npc_ship_file")?.GetValue (0);
      foreach (string relative in new[] { own, @"missions\npcships.ini" }.OfType<string> ())
      {
        string file = Path.Combine (DataFolder, relative);
        IReadOnlyList<IniSection> definitions = _npcDefinitions.GetOrAdd (file, path =>
        {
          try { return File.Exists (path) ? IniDocument.Load (path).Sections : []; }
          catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { return []; }
        });
        IniSection? arch = definitions.FirstOrDefault (section => section.Is ("NPCShipArch")
            && string.Equals (section.Find ("nickname")?.GetValue (0), archName, StringComparison.OrdinalIgnoreCase));
        if (arch is null) continue;
        loadoutName ??= arch.Find ("loadout")?.GetValue (0);
        string? direct = arch.Find ("ship_archetype")?.GetValue (0);
        if (direct is not null) return Definition (GameDataKind.ShipArchetype, direct);
        break;
      }
    }
    GameObjectDefinition? loadout = loadoutName is null ? null : Definition (GameDataKind.Loadout, loadoutName);
    return loadout?.ShipArchetype is { } archetype ? Definition (GameDataKind.ShipArchetype, archetype) : null;
  }

  /// <summary>Explicitly invalidate installation metadata and text caches after external resource changes.</summary>
  public static void Invalidate (string root)
  {
    string full = Path.TrimEndingDirectorySeparator (Path.GetFullPath (root));
    if (Cache.TryRemove (full, out var entry)) entry.Data.Texts?.Reload ();
  }
}
