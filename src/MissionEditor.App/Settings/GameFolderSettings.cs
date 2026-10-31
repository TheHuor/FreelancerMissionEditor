using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MissionEditor.App;

/// <summary>
/// Which Freelancer installation provides the game data (nicknames, texts): automatic (the installation the
/// mission lies in) or a chosen folder, for several versions side by side. Stored per Windows user.
/// </summary>
public sealed class GameFolderSettings
{
  private const int MaxRecent = 8;

  private static readonly string DefaultPath = Path.Combine (
      Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "FreelancerMissionEditor", "game-folder.json");

  /// <summary>The chosen installation folder; empty means automatic.</summary>
  public string GameFolder { get; set; } = string.Empty;

  /// <summary>Folders chosen before, most recent first.</summary>
  public List<string> RecentFolders { get; set; } = [];

  /// <summary>Loads the settings; missing or unreadable settings mean automatic.</summary>
  public static GameFolderSettings Load (string? path = null)
  {
    var settings = new GameFolderSettings ();
    try
    {
      string file = path ?? DefaultPath;
      if (!File.Exists (file))
        return settings;
      JsonNode? node = JsonNode.Parse (File.ReadAllText (file));
      settings.GameFolder = node?["gameFolder"]?.GetValue<string> () ?? string.Empty;
      settings.RecentFolders = node?["recentFolders"]?.AsArray ().Select (item => item?.GetValue<string> ()).OfType<string> ().ToList () ?? [];
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
    {
      // Unreadable settings: automatic.
    }
    return settings;
  }

  /// <summary>Remembers a chosen folder at the front of the recent list.</summary>
  public void Remember (string folder)
  {
    if (folder.Length == 0)
      return;
    RecentFolders.RemoveAll (item => string.Equals (item, folder, StringComparison.OrdinalIgnoreCase));
    RecentFolders.Insert (0, folder);
    if (RecentFolders.Count > MaxRecent)
      RecentFolders.RemoveRange (MaxRecent, RecentFolders.Count - MaxRecent);
  }

  /// <summary>Saves the settings; failures are ignored (the choice then lasts for this session only).</summary>
  public void Save (string? path = null)
  {
    try
    {
      string file = path ?? DefaultPath;
      Directory.CreateDirectory (Path.GetDirectoryName (file)!);
      var node = new JsonObject
      {
        ["gameFolder"] = GameFolder,
        ["recentFolders"] = new JsonArray (RecentFolders.Select (folder => (JsonNode?)JsonValue.Create (folder)).ToArray ()),
      };
      File.WriteAllText (file, node.ToJsonString (new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      // Not remembering the choice is harmless.
    }
  }
}
