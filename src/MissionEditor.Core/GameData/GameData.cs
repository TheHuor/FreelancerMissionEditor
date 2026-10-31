using System.Collections.Concurrent;
using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>The kinds of game nicknames a mission script can refer to.</summary>
public enum GameDataKind
{
  /// <summary>[System] in universe.ini.</summary>
  System,

  /// <summary>[Base] in universe.ini.</summary>
  Base,

  /// <summary>[Group] in the "groups" file (InitialWorld.ini): factions.</summary>
  Faction,

  /// <summary>[Ship] in the "ships" files (shiparch.ini).</summary>
  ShipArchetype,

  /// <summary>[Solar] in the "solar" file (solararch.ini).</summary>
  SolarArchetype,

  /// <summary>[Loadout] in the "loadouts" files.</summary>
  Loadout,

  /// <summary>Any nickname in the "equipment" files.</summary>
  Equipment,
}

/// <summary>
/// The nicknames of a Freelancer installation, read from the files listed under [Data] in
/// EXE\freelancer.ini. Located from a mission file by walking up to the folder that holds EXE\freelancer.ini.
/// </summary>
public sealed class GameData
{
  private static readonly ConcurrentDictionary<string, (DateTime Stamp, GameData Data)> Cache = new (StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<GameDataKind, HashSet<string>> _nicknames = [];

  private GameData (string root, string dataFolder)
  {
    Root = root;
    DataFolder = dataFolder;
    foreach (GameDataKind kind in Enum.GetValues<GameDataKind> ())
      _nicknames[kind] = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>The installation folder (the one with EXE and DATA).</summary>
  public string Root
  {
    get;
  }

  /// <summary>The DATA folder.</summary>
  public string DataFolder
  {
    get;
  }

  /// <summary>The texts behind IDS numbers (resource DLLs of the installation).</summary>
  public GameText? Texts
  {
    get; private init;
  }

  /// <summary>
  /// The texts of the IDS numbers in a mission line, e.g. "„Fliegen Sie nach Manhattan“", or null when the line
  /// has none or they cannot be read. Known places: string_id, individual_name, ids_name, ids_info, the
  /// [NNObjective] type (ids / rep_inst), Act_SetTitle, Act_SetOffer, Act_NNIds, Act_EtherComm (name) and Act_PopUpDialog.
  /// </summary>
  public string? DescribeIds (IniLine line, string sectionName)
  {
    if (Texts is null || line.Key is not { } key)
      return null;
    int[] indexes = key.ToLowerInvariant () switch
    {
      "string_id" or "individual_name" or "ids_name" or "ids_info" => [0],
      "type" when sectionName.Equals ("NNObjective", StringComparison.OrdinalIgnoreCase) =>
          line.GetValue (0).Equals ("ids", StringComparison.OrdinalIgnoreCase) ? [1]
          : line.GetValue (0).Equals ("rep_inst", StringComparison.OrdinalIgnoreCase) ? [2]
          : [],
      "act_settitle" or "act_setoffer" or "act_nnids" => [0],
      "act_ethercomm" => [1],
      "act_popupdialog" => [0, 1],
      _ => [],
    };
    string[] texts = indexes
        .Select (index => int.TryParse (line.GetValue (index), out int ids) ? Texts.Get (ids) : null)
        .OfType<string> ()
        .Select (text => $"„{text.Replace ("\n", " ¶ ")}“")
        .ToArray ();
    return texts.Length == 0 ? null : string.Join (" – ", texts);
  }

  /// <summary>All nicknames of a kind.</summary>
  public IReadOnlySet<string> Nicknames (GameDataKind kind) => _nicknames[kind];

  /// <summary>True when the installation defines the nickname (case-insensitive, like CreateID).</summary>
  public bool Contains (GameDataKind kind, string nickname) => _nicknames[kind].Contains (nickname);

  /// <summary>
  /// The game data for a mission file, or null when the file does not lie inside a Freelancer installation.
  /// Results are cached per installation until freelancer.ini changes.
  /// </summary>
  public static GameData? ForMission (string? missionPath) => Locate (missionPath, null);

  /// <summary>
  /// The game data to use for a mission: the configured installation folder when one is given (several versions
  /// of the game can live side by side), otherwise the installation the mission file lies in. Null when neither
  /// yields a readable installation.
  /// </summary>
  public static GameData? Locate (string? missionPath, string? gameFolder)
  {
    if (!string.IsNullOrWhiteSpace (gameFolder))
      return ForRoot (gameFolder);
    if (missionPath is null)
      return null;
    try
    {
      for (DirectoryInfo? folder = new FileInfo (Path.GetFullPath (missionPath)).Directory; folder is not null; folder = folder.Parent)
      {
        if (IsInstallation (folder.FullName))
          return ForRoot (folder.FullName);
      }
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
    {
      // Without readable game data the checks that need it are skipped.
    }
    return null;
  }

  /// <summary>True when the folder is a Freelancer installation (it contains EXE\freelancer.ini).</summary>
  public static bool IsInstallation (string folder)
  {
    try
    {
      return File.Exists (Path.Combine (folder, "EXE", "freelancer.ini"));
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  /// <summary>The game data of an installation folder (cached until freelancer.ini changes), or null when it is none or unreadable.</summary>
  public static GameData? ForRoot (string root)
  {
    try
    {
      string full = Path.TrimEndingDirectorySeparator (Path.GetFullPath (root));
      string freelancerIni = Path.Combine (full, "EXE", "freelancer.ini");
      if (!File.Exists (freelancerIni))
        return null;
      DateTime stamp = File.GetLastWriteTimeUtc (freelancerIni);
      if (Cache.TryGetValue (full, out var cached) && cached.Stamp == stamp)
        return cached.Data;
      GameData data = Load (full);
      Cache[full] = (stamp, data);
      return data;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
    {
      return null;
    }
  }

  /// <summary>A short description for the user, e.g. "D:\Freelancer (48 Systeme, 312 Basen, 61 Fraktionen)".</summary>
  public string Summary =>
    $"{Root} ({Nicknames (GameDataKind.System).Count} Systeme, {Nicknames (GameDataKind.Base).Count} Basen, {Nicknames (GameDataKind.Faction).Count} Fraktionen)";

  /// <summary>Reads the game data of an installation folder.</summary>
  public static GameData Load (string root)
  {
    string exe = Path.Combine (root, "EXE");
    IniDocument freelancer = IniDocument.Load (Path.Combine (exe, "freelancer.ini"));
    string dataPath = freelancer.Sections.FirstOrDefault (section => section.Is ("Freelancer"))?.Find ("data path")?.GetValue (0) ?? @"..\data";
    string dataFolder = Path.GetFullPath (Path.Combine (exe, dataPath));
    var data = new GameData (root, dataFolder) { Texts = GameText.FromFreelancerIni (freelancer, exe) };
    foreach (IniLine entry in freelancer.Sections.Where (section => section.Is ("Data")).SelectMany (section => section.Entries))
    {
      string file = Path.Combine (dataFolder, entry.GetValue (0));
      switch (entry.Key?.ToLowerInvariant ())
      {
        case "universe":
          data.Read (file, (section, nickname) =>
              section.Is ("System") ? GameDataKind.System : section.Is ("Base") ? GameDataKind.Base : null);
          break;
        case "groups":
          data.Read (file, (section, _) => section.Is ("Group") ? GameDataKind.Faction : null);
          break;
        case "ships":
          data.Read (file, (section, _) => section.Is ("Ship") ? GameDataKind.ShipArchetype : null);
          break;
        case "solar":
          data.Read (file, (section, _) => section.Is ("Solar") ? GameDataKind.SolarArchetype : null);
          break;
        case "loadouts":
          data.Read (file, (section, _) => section.Is ("Loadout") ? GameDataKind.Loadout : null);
          break;
        case "equipment":
          data.Read (file, (_, _) => GameDataKind.Equipment);
          break;
      }
    }
    return data;
  }

  private readonly ConcurrentDictionary<string, IReadOnlySet<string>?> _npcShipFiles = new (StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// The [NPCShipArch] nicknames of an npc_ship_file (relative to DATA, as in [Mission]), or null when the
  /// file does not exist.
  /// </summary>
  public IReadOnlySet<string>? NpcShipArchetypes (string npcShipFile) => _npcShipFiles.GetOrAdd (npcShipFile, relative =>
  {
    string file = Path.Combine (DataFolder, relative);
    if (!File.Exists (file))
      return null;
    return IniDocument.Load (file).Sections
          .Where (section => section.Is ("NPCShipArch"))
          .Select (section => section.Find ("nickname")?.GetValue (0))
          .OfType<string> ()
          .ToHashSet (StringComparer.OrdinalIgnoreCase);
  });

  private void Read (string file, Func<IniSection, string, GameDataKind?> kindOf)
  {
    if (!File.Exists (file))
      return;
    foreach (IniSection section in IniDocument.Load (file).Sections)
    {
      string? nickname = section.Find ("nickname")?.GetValue (0);
      if (string.IsNullOrEmpty (nickname))
        continue;
      if (kindOf (section, nickname) is { } kind)
        _nicknames[kind].Add (nickname);
    }
  }
}
