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
    /// <param name="workspace">The workspace shared with the agent, or null when not set up yet.</param>
    /// <param name="exportAllowed">True when the server was set up with --allow-writes.</param>
    public NewMissionPromptWindow(string? missionPath, string? workspace, bool exportAllowed)
    {
        InitializeComponent();
        _exportAllowed = exportAllowed;
        _templateReference = missionPath is not null && workspace is not null ? AgentPrompts.RelativeToWorkspace(missionPath, workspace) : null;
        TargetBox.Text = "neue_mission.ini";
        TemplateBox.Content = _templateReference is null
            ? "Keine Vorlage (die geöffnete Mission liegt nicht im Workspace)"
            : $"Die geöffnete Mission als Vorlage nennen ({_templateReference})";
        TemplateBox.IsEnabled = _templateReference is not null;
        TemplateBox.IsChecked = _templateReference is not null;
        if (workspace is null)
            ShowHint(false, "Noch kein Workspace eingerichtet – erst „KI-Agent verbinden…“ verwenden, sonst kann der Agent den Server nicht nutzen.");
        else
            ShowHint(true, exportAllowed
                ? $"Workspace: {workspace}. Export ist freigeschaltet; der Agent fragt vor dem Schreiben nach."
                : $"Workspace: {workspace}. Export ist nicht freigeschaltet – der Agent zeigt dir den Text, du speicherst ihn selbst.");
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    /// <summary>The copied request (for tests), or null when nothing was copied.</summary>
    public string? Prompt { get; private set; }

    /// <summary>Builds the request from the current input (also used by tests).</summary>
    public string BuildPrompt(string description) =>
        AgentPrompts.ForNewMission(description, TemplateBox.IsChecked == true ? _templateReference : null, TargetBox.Text.Trim(), _exportAllowed);

    private void ShowHint(bool good, string text)
    {
        WorkspaceHint.Text = text;
        WorkspaceHint.Foreground = (Brush)FindResource(good ? "MutedTextBrush" : "WarningBrush");
    }

    private void OnCopy(object sender, RoutedEventArgs args)
    {
        string description = DescriptionBox.Text.Trim();
        if (description.Length == 0)
        {
            ShowHint(false, "Bitte zuerst beschreiben, was die Mission tun soll.");
            DescriptionBox.Focus();
            return;
        }
        if (!TargetBox.Text.Trim().EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(TargetBox.Text.Trim()))
        {
            ShowHint(false, "Die Zieldatei muss ein relativer Pfad auf .ini sein, z. B. test\\neue_mission.ini.");
            return;
        }
        Prompt = BuildPrompt(description);
        Clipboard.SetText(Prompt);
        DialogResult = true;
    }
}
