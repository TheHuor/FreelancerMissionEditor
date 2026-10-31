using System.IO;

namespace MissionEditor.App;

/// <summary>
/// Finds command-line programs such as <c>claude</c> or <c>codex</c>. Installers add their folder to the
/// user's PATH in the registry, which a running process (e.g. one started from Visual Studio before
/// the installation) does not see; so the search also reads the current PATH from the registry and
/// looks in the usual installation folders.
/// </summary>
public static class ProgramLocator
{
  private static readonly string[] Extensions = [".exe", ".cmd", ".bat"];

  /// <summary>
  /// The PATH a freshly started program would get: the process PATH, then the user and machine PATH
  /// from the registry and the usual installation folders, without duplicates.
  /// </summary>
  public static string CurrentPath ()
  {
    string userProfile = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
    IEnumerable<string?> sources =
    [
        Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Path.Combine(userProfile, ".local", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"),
        ];
    var directories = new List<string> ();
    var seen = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
    foreach (string? source in sources)
    {
      foreach (string entry in (source ?? string.Empty).Split (Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
      {
        string directory = Environment.ExpandEnvironmentVariables (entry.Trim ().Trim ('"'));
        if (directory.Length > 0 && seen.Add (directory.TrimEnd ('\\')))
          directories.Add (directory);
      }
    }
    return string.Join (Path.PathSeparator, directories);
  }

  /// <summary>The full path of a program (e.g. "claude"), or null when it is not installed.</summary>
  public static string? Find (string name)
  {
    foreach (string directory in CurrentPath ().Split (Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      foreach (string extension in Extensions)
      {
        try
        {
          string candidate = Path.Combine (directory, name + extension);
          if (File.Exists (candidate))
            return candidate;
        }
        catch (ArgumentException)
        {
          // A malformed PATH entry is skipped.
        }
      }
    }
    return null;
  }
}
