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
    if (!_systemFileOf.TryGetValue (name, out string? file) || !File.Exists (file))
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
