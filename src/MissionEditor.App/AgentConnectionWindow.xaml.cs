using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Shows ready-to-paste configuration for connecting an AI agent to the MCP server.</summary>
public partial class AgentConnectionWindow : Window
{
    private bool _initialized;

    /// <summary>Creates the dialog.</summary>
    /// <param name="missionPath">The open mission file; its folder is the suggested workspace.</param>
    /// <param name="hasUnsavedChanges">True when the open mission has unsaved changes.</param>
    public AgentConnectionWindow(string? missionPath, bool hasUnsavedChanges)
    {
        InitializeComponent();
        (string? rememberedExecutable, bool allowWrites) = AgentConnection.LoadSettings();
        ServerBox.Text = rememberedExecutable is not null && File.Exists(rememberedExecutable)
            ? rememberedExecutable
            : AgentConnection.FindServerExecutable() ?? string.Empty;
        WorkspaceBox.Text = missionPath is null ? string.Empty : Path.GetDirectoryName(missionPath) ?? string.Empty;
        AllowWritesBox.IsChecked = allowWrites;
        UnsavedHint.Visibility = hasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
        bool hasClaudeCommand = AgentConnection.IsClaudeCommandAvailable();
        ClaudeCommandStatus.Text = hasClaudeCommand
            ? "Die Kommandozeile claude ist auf diesem Rechner vorhanden."
            : "Die Kommandozeile claude wurde auf diesem Rechner nicht gefunden – der Befehl meldet dann „claude wurde nicht erkannt“. Weg 2 verwenden.";
        ClaudeCommandStatus.Foreground = (System.Windows.Media.Brush)FindResource(hasClaudeCommand ? "MutedTextBrush" : "WarningBrush");
        _initialized = true;
        Refresh();
    }

    /// <summary>The current Claude Code command (for tests).</summary>
    public string ClaudeCodeText => ClaudeCodeBox.Text;

    private string Executable => ServerBox.Text.Trim();

    private string Workspace => WorkspaceBox.Text.Trim();

    private bool AllowWrites => AllowWritesBox.IsChecked == true;

    private void OnInputChanged(object sender, RoutedEventArgs args)
    {
        if (_initialized)
            Refresh();
    }

    private void Refresh()
    {
        string executable = Executable.Length > 0 ? Executable : AgentConnection.ServerExecutable;
        string workspace = Workspace.Length > 0 ? Workspace : "<Missionsverzeichnis>";
        ClaudeCodeBox.Text = AgentConnection.ClaudeCodeCommand(executable, workspace, AllowWrites);
        ClaudeCodeJsonBox.Text = AgentConnection.ClaudeCodeJson(executable, workspace, AllowWrites);
        ClaudeDesktopBox.Text = AgentConnection.JsonConfiguration(executable, workspace, AllowWrites);
        CodexBox.Text = AgentConnection.CodexConfiguration(executable, workspace, AllowWrites);

        if (Executable.Length == 0)
            ShowServerStatus(false, "Kein MCP-Server gefunden. Das Release-ZIP „MissionEditorMcp“ entpacken und hier die MissionEditor.Mcp.exe wählen.");
        else if (!File.Exists(Executable))
            ShowServerStatus(false, "Datei nicht gefunden.");
        else
            ShowServerStatus(true, "Gefunden. „Prüfen“ startet den Server testweise.");
        if (Workspace.Length > 0 && !Directory.Exists(Workspace))
            ShowServerStatus(false, "Das Missionsverzeichnis existiert nicht.");
        CopyStatus.Text = string.Empty;
    }

    private void ShowServerStatus(bool good, string text)
    {
        ServerStatus.Text = text;
        ServerStatus.Foreground = (System.Windows.Media.Brush)FindResource(good ? "MutedTextBrush" : "WarningBrush");
    }

    private void OnProbe(object sender, RoutedEventArgs args)
    {
        (bool success, string text) = AgentConnection.ProbeServer(Executable);
        ShowServerStatus(success, success ? $"Server läuft: Version {text}." : $"Server-Test fehlgeschlagen: {text}");
    }

    private void OnBrowseServer(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"MCP-Server ({AgentConnection.ServerExecutable})|{AgentConnection.ServerExecutable}|Programme (*.exe)|*.exe",
            FileName = AgentConnection.ServerExecutable,
        };
        if (File.Exists(Executable))
            dialog.InitialDirectory = Path.GetDirectoryName(Executable);
        if (dialog.ShowDialog(this) == true)
            ServerBox.Text = dialog.FileName;
    }

    private void OnBrowseWorkspace(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog { Title = "Missionsverzeichnis wählen" };
        if (Directory.Exists(Workspace))
            dialog.InitialDirectory = Workspace;
        if (dialog.ShowDialog(this) == true)
            WorkspaceBox.Text = dialog.FolderName;
    }

    private void OnCopy(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string client })
            return;
        TextBox source = client switch
        {
            "ClaudeCode" => ClaudeCodeBox,
            "ClaudeCodeJson" => ClaudeCodeJsonBox,
            "ClaudeDesktop" => ClaudeDesktopBox,
            _ => CodexBox,
        };
        Clipboard.SetText(source.Text);
        CopyStatus.Text = Executable.Length == 0 || Workspace.Length == 0
            ? "Kopiert – Platzhalter vor dem Einfügen noch ersetzen."
            : "In die Zwischenablage kopiert.";
        if (File.Exists(Executable))
            AgentConnection.SaveSettings(Executable, AllowWrites);
    }

    private void OnClose(object sender, RoutedEventArgs args) => Close();
}
