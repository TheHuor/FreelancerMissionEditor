using System.IO;
using System.Windows;
using System.Windows.Media;

namespace MissionEditor.App;

/// <summary>Collects a mission description and copies a request for the agent to write and check it.</summary>
public partial class NewMissionPromptWindow : Window
{
  private readonly string? _templateReference;
  private readonly bool _exportAllowed;

  /// <summary>Creates the dialog.</summary>
  /// <param name="missionPath">The open mission, offered as template; null when none is open.</param>
  /// <param name="workspace">The server's folder limit (empty for none), or null when the server is not set up yet.</param>
  /// <param name="exportAllowed">True when the server was set up with --allow-writes.</param>
  public NewMissionPromptWindow (string? missionPath, string? workspace, bool exportAllowed)
  {
    InitializeComponent ();
    _exportAllowed = exportAllowed;
    bool reachable = missionPath is not null && workspace is not null && AgentPrompts.IsReachable (missionPath, workspace);
    _templateReference = reachable ? missionPath : null;
    // The new file goes next to the open mission, or into the server's folder.
    string? targetFolder = missionPath is not null ? Path.GetDirectoryName (missionPath) : workspace is { Length: > 0 } ? workspace : null;
    TargetBox.Text = targetFolder is null ? "neue_mission.ini" : Path.Combine (targetFolder, "neue_mission.ini");
    TemplateBox.Content = _templateReference is null
        ? missionPath is null ? "Keine Vorlage (keine Mission geöffnet)" : "Keine Vorlage (der Server darf die geöffnete Mission nicht lesen)"
        : $"Die geöffnete Mission als Vorlage nennen ({Path.GetFileName (_templateReference)})";
    TemplateBox.IsEnabled = _templateReference is not null;
    TemplateBox.IsChecked = _templateReference is not null;
    string limit = workspace is { Length: > 0 } ? $"Server beschränkt auf {workspace}." : "Server ohne Ordnerbeschränkung.";
    if (workspace is null)
      ShowHint (false, "Der MCP-Server ist noch bei keinem Client eingetragen – erst „KI-Agent verbinden…“ verwenden.");
    else
      ShowHint (true, exportAllowed
          ? $"{limit} Export ist freigeschaltet; der Agent fragt vor dem Schreiben nach."
          : $"{limit} Export ist nicht freigeschaltet – der Agent zeigt dir den Text, du speicherst ihn selbst.");
    Loaded += (_, _) => DescriptionBox.Focus ();
  }

  /// <summary>The copied request (for tests), or null when nothing was copied.</summary>
  public string? Prompt
  {
    get; private set;
  }

  /// <summary>Builds the request from the current input (also used by tests).</summary>
  public string BuildPrompt (string description) =>
      AgentPrompts.ForNewMission (description, TemplateBox.IsChecked == true ? _templateReference : null, TargetBox.Text.Trim (), _exportAllowed);

  private void ShowHint (bool good, string text)
  {
    WorkspaceHint.Text = text;
    WorkspaceHint.Foreground = (Brush)FindResource (good ? "MutedTextBrush" : "WarningBrush");
  }

  private void OnCopy (object sender, RoutedEventArgs args)
  {
    string description = DescriptionBox.Text.Trim ();
    if (description.Length == 0)
    {
      ShowHint (false, "Bitte zuerst beschreiben, was die Mission tun soll.");
      DescriptionBox.Focus ();
      return;
    }
    // The server takes absolute paths (relative ones only with a folder limit), so the suggested full path is fine.
    if (!TargetBox.Text.Trim ().EndsWith (".ini", StringComparison.OrdinalIgnoreCase))
    {
      ShowHint (false, "Die Zieldatei muss auf .ini enden, z. B. D:\\Freelancer\\DATA\\MISSIONS\\neue_mission.ini.");
      return;
    }
    Prompt = BuildPrompt (description);
    Clipboard.SetText (Prompt);
    DialogResult = true;
  }
}
