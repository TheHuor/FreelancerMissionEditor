using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>A jump gate or jump hole of a system: an [Object] with "goto = &lt;system&gt;, &lt;object&gt;, &lt;tunnel&gt;".</summary>
/// <param name="Nickname">The object's nickname, e.g. "Rh03_to_Bw04_hole".</param>
/// <param name="System">The system the object lies in.</param>
/// <param name="TargetSystem">The system it leads to.</param>
/// <param name="Position">The "pos" value.</param>
/// <param name="Archetype">The "archetype" value, e.g. "jumpgate" or "jumphole_orange".</param>
/// <param name="Reputation">The "reputation" value: the faction a jump gate belongs to, or empty.</param>
/// <param name="FilePath">The system INI.</param>
/// <param name="LineNumber">The first line of the [Object] section.</param>
public sealed record SystemConnection (string Nickname, string System, string TargetSystem, Vector3 Position, string Archetype, string Reputation, string FilePath, int LineNumber)
{
  /// <summary>True for a jump gate, false for a jump hole (the archetype names "jumpgate", "jumphole_…" and the like).</summary>
  public bool IsGate => Archetype.Contains ("gate", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A nickname of a system INI: an [Object] (station, planet, jump gate, buoy …) or a [Zone].</summary>
/// <param name="Nickname">The nickname as written there.</param>
/// <param name="System">The system the INI belongs to.</param>
/// <param name="IsZone">True for a [Zone], false for an [Object].</param>
/// <param name="FilePath">The system INI.</param>
/// <param name="LineNumber">The first line of the section.</param>
public sealed record SpaceNickname (string Nickname, string System, bool IsZone, string FilePath, int LineNumber);

public sealed partial class GameData
{
  private readonly Dictionary<string, string> _systemFileOf = new (StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, IReadOnlyList<SystemConnection>> _connections = new (StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// The jump gates and jump holes of a system (objects of its INI with "goto"), or an empty list when the system
  /// or its file is unknown.
  /// </summary>
  public IReadOnlyList<SystemConnection> Connections (string system) => _connections.GetOrAdd (system, name =>
  {
    if (SystemFile (name) is not { } file)
      return [];
    try
    {
      var connections = new List<SystemConnection> ();
      foreach (IniSection section in IniDocument.Load (file).Sections.Where (section => section.Is ("Object")))
      {
        string? nickname = section.Find ("nickname")?.GetValue (0);
        string? target = section.Find ("goto")?.GetValue (0);
        if (string.IsNullOrEmpty (nickname) || string.IsNullOrEmpty (target) || section.Find ("pos") is not { } position
            || !TryReadVector (position, 0, out Vector3 point))
          continue;
        connections.Add (new SystemConnection (nickname, name, target, point, section.Find ("archetype")?.GetValue (0) ?? string.Empty, section.Find ("reputation")?.GetValue (0) ?? string.Empty,
            Path.GetFullPath (file), section.FirstLineNumber));
      }
      return connections;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      return [];
    }
  });

  /// <summary>
  /// The INI of a system as universe.ini names it, or null. Crossfire keeps several variants per system and lets its
  /// launcher copy one of them over the named file when the game starts ("cf14sp.ini", "cf14mp.ini" → "cf14.ini"); in a
  /// build folder only the variants exist. Then the single-player variant is taken, else the multiplayer one.
  /// </summary>
  public string? SystemFile (string system)
  {
    if (!_systemFileOf.TryGetValue (system, out string? file))
      return null;
    if (File.Exists (file))
      return file;
    string stem = Path.Combine (Path.GetDirectoryName (file)!, Path.GetFileNameWithoutExtension (file));
    return new[] { stem + "sp.ini", stem + "mp.ini" }.FirstOrDefault (File.Exists);
  }

  /// <summary>
  /// The [Object] and [Zone] sections of all system INIs with this nickname (any case). Empty when no system
  /// defines it.
  /// </summary>
  public IReadOnlyList<SpaceNickname> FindSpaceNickname (string nickname) =>
      SpaceNicknames.TryGetValue (nickname, out List<SpaceNickname>? found) ? found : [];

  /// <summary>
  /// True when every system of universe.ini has a readable INI. Otherwise (e.g. a build folder that holds only changed
  /// files) a nickname missing from <see cref="FindSpaceNickname"/> may still exist in the game.
  /// </summary>
  public bool HasAllSystemFiles => _systemFileOf.Keys.All (system => SystemFile (system) is not null);

  private Dictionary<string, List<SpaceNickname>>? _spaceNicknames;

  private Dictionary<string, List<SpaceNickname>> SpaceNicknames
  {
    get
    {
      lock (_systemFileOf)
        return _spaceNicknames ??= LoadSpaceNicknames ();
    }
  }

  private Dictionary<string, List<SpaceNickname>> LoadSpaceNicknames ()
  {
    var index = new Dictionary<string, List<SpaceNickname>> (StringComparer.OrdinalIgnoreCase);
    foreach (string system in _systemFileOf.Keys)
    {
      if (SystemFile (system) is not { } file)
        continue;
      try
      {
        foreach (IniSection section in IniDocument.Load (file).Sections.Where (section => section.Is ("Object") || section.Is ("Zone")))
        {
          if (section.Find ("nickname")?.GetValue (0) is not { Length: > 0 } nickname)
            continue;
          if (!index.TryGetValue (nickname, out List<SpaceNickname>? entries))
            index[nickname] = entries = [];
          entries.Add (new SpaceNickname (nickname, system, section.Is ("Zone"), Path.GetFullPath (file), section.FirstLineNumber));
        }
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
      {
      }
    }
    return index;
  }

  /// <summary>Reads three numbers of a line from the given value index on (e.g. "pos = x, y, z").</summary>
  public static bool TryReadVector (IniLine line, int index, out Vector3 vector)
  {
    vector = default;
    if (!TryReadNumber (line, index, out float x) || !TryReadNumber (line, index + 1, out float y) || !TryReadNumber (line, index + 2, out float z))
      return false;
    vector = new Vector3 (x, y, z);
    return true;
  }

  /// <summary>Reads a number of a line (invariant culture).</summary>
  public static bool TryReadNumber (IniLine line, int index, out float number) =>
      float.TryParse (line.GetValue (index), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
}
