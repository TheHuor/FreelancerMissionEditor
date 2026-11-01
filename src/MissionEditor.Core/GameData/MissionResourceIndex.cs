using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>Builds source-positioned uses from the current document, without parsing presentation strings.</summary>
public static class MissionResourceIndex
{
  /// <summary>Known numeric IDS parameter positions, including distinct popup roles.</summary>
  public static IReadOnlyList<(int Index, string Role, string Category)> Parameters (IniLine line, string sectionName) => line.Key?.ToLowerInvariant () switch
  {
    "string_id" => [(0, "Text", "Weitere Texte")],
    "individual_name" => [(0, "Objektname", "Name")],
    "ids_name" => [(0, "Name", "Name")],
    "ids_info" => [(0, "Infocard", "Infocard")],
    "type" when sectionName.Equals ("NNObjective", StringComparison.OrdinalIgnoreCase) => line.GetValue (0).ToLowerInvariant () switch
    {
      "ids" => [(1, "Missionsziel", "Missionsziel")],
      "rep_inst" => [(2, "Missionsziel", "Missionsziel")],
      _ => [],
    },
    "act_settitle" => [(0, "Missionstitel", "Missionstitel")],
    "act_setoffer" => [(0, "Missionsangebot", "Missionsangebot")],
    "act_nnids" => [(0, "Missionsziel", "Missionsziel")],
    "act_ethercomm" => [(1, "Sprechername", "Name")],
    "act_popupdialog" => [(0, "Dialogtitel", "Dialog"), (1, "Dialogtext", "Dialog")],
    _ => [],
  };

  /// <summary>All direct and traceable indirect IDS uses in this mission snapshot.</summary>
  public static IReadOnlyList<IdsReference> Build (IniDocument document, string? filePath, GameData? gameData)
  {
    var result = new List<IdsReference> ();
    foreach (IniSection section in document.Sections.Where (section => !section.IsCommentedOut))
    {
      string nickname = section.Find ("nickname")?.GetValue (0) ?? string.Empty;
      foreach (IniLine line in section.Entries)
      {
        foreach (var parameter in Parameters (line, section.Name))
        {
          if (int.TryParse (line.GetValue (parameter.Index), out int ids))
            result.Add (new (ids, parameter.Role, parameter.Category, filePath, section.Index, section.Name, nickname, line.Number, parameter.Index, line.Text));
        }
        foreach (var target in ObjectTargets (line, section))
        {
          GameObjectDefinition? definition = gameData?.Definition (target.Kind, line.GetValue (target.Index));
          if (definition is not null)
            AddDefinition (definition, target.Index, target.Label, line, section, nickname);
        }
        foreach (int index in MissionObjectParameters (line, section))
        {
          string name = section.Is ("MsnShip") ? nickname : line.GetValue (index);
          IniSection? ship = section.Is ("MsnShip") ? section : document.Sections.FirstOrDefault (candidate => candidate.Is ("MsnShip")
              && string.Equals (candidate.Find ("nickname")?.GetValue (0), name, StringComparison.OrdinalIgnoreCase));
          if (ship is null)
            continue;
          string? npcName = ship.Find ("NPC")?.GetValue (0);
          IniSection? npc = document.Sections.FirstOrDefault (candidate => candidate.Is ("NPC")
              && string.Equals (candidate.Find ("nickname")?.GetValue (0), npcName, StringComparison.OrdinalIgnoreCase));
          IniSection nameDefinition = ship.Find ("individual_name") is not null ? ship : npc ?? ship;
          if (int.TryParse (nameDefinition.Find ("individual_name")?.GetValue (0), out int nameIds))
            result.Add (new (nameIds, "Objektname", "Name", filePath, section.Index, section.Name, nickname, line.Number, index, line.Text,
                $"MsnShip {name} → {nameDefinition.Name} {nameDefinition.Find ("nickname")?.GetValue (0)} → individual_name (Zeile {nameDefinition.FirstLineNumber})")
                { DefinitionPath = filePath, DefinitionLineNumber = nameDefinition.FirstLineNumber });
          GameObjectDefinition? type = gameData?.ResolveMissionShipType (document, ship);
          if (type is not null)
            AddDefinition (type, index, "Schiffstyp", line, section, nickname, $"MsnShip {name} → ");
        }
      }
    }
    return result;

    void AddDefinition (GameObjectDefinition definition, int parameterIndex, string label, IniLine line, IniSection section, string nickname, string prefix = "")
    {
      string provenance = $"{prefix}{definition.Nickname} → {definition.FilePath}:{definition.LineNumber}";
      if (definition.NameIds is { } name)
        result.Add (new (name, $"{label} · Name", "Name", filePath, section.Index, section.Name, nickname, line.Number, parameterIndex, line.Text, provenance + " → ids_name")
            { DefinitionPath = definition.FilePath, DefinitionLineNumber = definition.LineNumber });
      if (definition.InfoIds is { } info)
        result.Add (new (info, $"{label} · Infocard", "Infocard", filePath, section.Index, section.Name, nickname, line.Number, parameterIndex, line.Text, provenance + " → ids_info")
            { DefinitionPath = definition.FilePath, DefinitionLineNumber = definition.LineNumber });
    }
  }

  /// <summary>Known nickname parameter positions. Exposed for unavailable-definition hints without guessing arbitrary values.</summary>
  public static IEnumerable<(int Index, GameDataKind Kind, string Label)> ObjectTargets (IniLine line, IniSection section)
  {
    string key = line.Key?.ToLowerInvariant () ?? string.Empty;
    if (section.Is ("MsnSolar") || section.Is ("MsnShip") || section.Is ("Mission"))
    {
      if (key == "system") yield return (0, GameDataKind.System, "System");
      if (key == "base") yield return (0, GameDataKind.Base, "Basis");
    }
    if (section.Is ("Trigger"))
    {
      switch (key)
      {
        case "cnd_baseenter": case "cnd_baseexit": case "act_forceland": yield return (0, GameDataKind.Base, "Basis"); break;
        case "act_setshipandloadout": yield return (0, GameDataKind.ShipArchetype, "Schiffstyp"); break;
        case "system": case "cnd_systementer": case "cnd_systemexit": yield return (0, GameDataKind.System, "System"); break;
      }
    }
  }

  private static IEnumerable<int> MissionObjectParameters (IniLine line, IniSection section)
  {
    if (section.Is ("MsnShip") && line.IsKey ("NPC")) { yield return 0; yield break; }
    if (!section.Is ("Trigger")) yield break;
    switch (line.Key?.ToLowerInvariant ())
    {
      case "act_spawnship": case "act_spawnshiprel": case "act_relocateship": case "act_destroy": case "act_markobj":
      case "act_setlifetime": case "act_invulnerable": case "act_giveobjlist":
      case "cnd_destroyed": case "cnd_healthdec": yield return 0; break;
      case "cnd_distship": yield return 1; yield return 2; break;
      case "act_ethercomm": yield return 0; break;
    }
  }
}
