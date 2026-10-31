using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Connects an AI agent to the MCP server: adds the entry to a client's configuration or shows it for copying.</summary>
public partial class AgentConnectionWindow : Window
{
    private readonly bool _hasClaudeCommand;
    private bool _initialized;

    /// <summary>Creates the dialog.</summary>
    /// <param name="missionPath">The open mission file.</param>
    /// <param name="hasUnsavedChanges">True when the open mission has unsaved changes.</param>
    public AgentConnectionWindow(string? missionPath, bool hasUnsavedChanges)
    {
        InitializeComponent();
        (string? rememberedExecutable, string? rememberedWorkspace, bool allowWrites) = AgentConnection.LoadSettings();
        ServerBox.Text = rememberedExecutable is not null && File.Exists(rememberedExecutable)
            ? rememberedExecutable
            : AgentConnection.FindServerExecutable() ?? string.Empty;
        // A folder limit already registered at a client (or remembered) is kept when it lets the server read the
        // open mission; otherwise the suggestion is no limit.
        IEnumerable<string> known = AgentClientConfig.ConfiguredServers().Select(server => server.Workspace)
            .Where(workspace => workspace.Length == 0 || Directory.Exists(workspace));
        if (rememberedWorkspace is not null && (rememberedWorkspace.Length == 0 || Directory.Exists(rememberedWorkspace)))
            known = known.Append(rememberedWorkspace);
        (string? knownWorkspace, bool containsMission) = AgentPrompts.ChooseWorkspace(missionPath, known);
        WorkspaceBox.Text = knownWorkspace is not null && (containsMission || missionPath is null) ? knownWorkspace : string.Empty;
        AllowWritesBox.IsChecked = allowWrites;
        UnsavedHint.Visibility = hasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
        _hasClaudeCommand = AgentConnection.IsClaudeCommandAvailable();
        ClientTabs.SelectedIndex = 0;
        _initialized = true;
        Refresh();
    }

    /// <summary>The current Claude Code command (for tests).</summary>
    public string ClaudeCodeText => AgentConnection.ClaudeCodeCommand(ExecutableOrPlaceholder, Workspace, AllowWrites);

    private string Executable => ServerBox.Text.Trim();

    private string Workspace => WorkspaceBox.Text.Trim();

    private string ExecutableOrPlaceholder => Executable.Length > 0 ? Executable : AgentConnection.ServerExecutable;

    private bool AllowWrites => AllowWritesBox.IsChecked == true;

    private AgentClient SelectedClient => ClientTabs.SelectedItem is TabItem { Tag: string tag } && Enum.TryParse(tag, out AgentClient client)
        ? client
        : AgentClient.ClaudeCode;

    private void OnInputChanged(object sender, RoutedEventArgs args)
    {
        if (_initialized)
            Refresh();
    }

    private void OnClientTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_initialized && ReferenceEquals(args.OriginalSource, ClientTabs))
            Refresh();
    }

    private void Refresh()
    {
        RefreshServerStatus();
        AgentClient client = SelectedClient;
        string configPath = AgentClientConfig.ConfigPath(client);
        string clientName = ClientName(client);
        bool configured = AgentClientConfig.IsConfigured(client, configPath);

        IReadOnlyList<string> configPaths = AgentClientConfig.ConfigPaths(client);
        TargetText.Text = $"Trägt den Server direkt in {string.Join(" und ", configPaths)} ein.";
        bool ready = File.Exists(Executable) && (Workspace.Length == 0 || Directory.Exists(Workspace));
        AddButton.IsEnabled = ready;
        AddButton.Content = configured ? "Aktualisieren" : "Hinzufügen";
        if (!ready)
            ShowStatus(TargetStatus, false, "Erst einen vorhandenen MCP-Server wählen; der freigegebene Ordner muss existieren oder leer sein.");
        else if (configured)
            ShowStatus(TargetStatus, true, $"Ist schon eingetragen. „Aktualisieren“ ersetzt den Eintrag mit den Angaben oben. Danach {RestartHint(client)}");
        else
            ShowStatus(TargetStatus, true, $"Noch nicht eingetragen. Nach dem Hinzufügen {RestartHint(client)}");

        CommandPanel.Visibility = client == AgentClient.ClaudeCode ? Visibility.Visible : Visibility.Collapsed;
        ClaudeCodeBox.Text = ClaudeCodeText;
        ShowStatus(ClaudeCommandStatus, _hasClaudeCommand, _hasClaudeCommand
            ? "Die Kommandozeile claude ist vorhanden: den Befehl in einem Terminal ausführen."
            : "Die Kommandozeile claude ist auf diesem Rechner nicht installiert (der Befehl meldet dann „nicht erkannt“). „Hinzufügen“ oben funktioniert trotzdem.");

        (string instruction, string snippet) = AgentClientConfig.ManualSnippet(client, configPath, ExecutableOrPlaceholder, Workspace, AllowWrites);
        string preparation = File.Exists(configPath) ? $"Vorher eine Kopie der Datei anlegen und {clientName} schließen." : $"{clientName} vorher schließen.";
        ManualInstruction.Text = $"{instruction} {preparation} Danach {RestartHint(client)}";
        ManualBox.Text = snippet;
        CopyStatus.Text = string.Empty;
    }

    private static string ClientName(AgentClient client) => client switch
    {
        AgentClient.ClaudeCode => "Claude Code",
        AgentClient.ClaudeDesktop => "Claude Desktop",
        _ => "Codex",
    };

    private static string RestartHint(AgentClient client) => client switch
    {
        AgentClient.ClaudeCode => "eine neue Claude-Code-Sitzung starten; /mcp zeigt den Status.",
        AgentClient.ClaudeDesktop => "Claude Desktop ganz beenden (auch im Infobereich) und neu starten.",
        _ => "Codex neu starten.",
    };

    private void RefreshServerStatus()
    {
        if (Executable.Length == 0)
            ShowStatus(ServerStatus, false, "Kein MCP-Server gefunden. Das Release-ZIP „MissionEditorMcp“ entpacken und hier die MissionEditor.Mcp.exe wählen.");
        else if (!File.Exists(Executable))
            ShowStatus(ServerStatus, false, "Datei nicht gefunden.");
        else if (Workspace.Length > 0 && !Directory.Exists(Workspace))
            ShowStatus(ServerStatus, false, "Der freigegebene Ordner existiert nicht.");
        else
            ShowStatus(ServerStatus, true, "Gefunden. „Prüfen“ startet den Server testweise.");
    }

    private void ShowStatus(TextBlock target, bool good, string text)
    {
        target.Text = text;
        target.Foreground = (Brush)FindResource(good ? "MutedTextBrush" : "WarningBrush");
    }

    private void OnAdd(object sender, RoutedEventArgs args)
    {
        AgentClient client = SelectedClient;
        IReadOnlyList<string> configPaths = AgentClientConfig.ConfigPaths(client);
        string clientName = ClientName(client);
        string question = $"Den MCP-Server „{AgentConnection.ServerName}“ in die Konfiguration von {clientName} eintragen?\n\n"
            + string.Concat(configPaths.Select(configPath => $"Datei: {configPath}\n"
                + (File.Exists(configPath) ? $"Sicherung: {configPath}{AgentClientConfig.BackupSuffix}\n" : "Die Datei wird neu angelegt.\n")))
            + $"Freigegebener Ordner: {(Workspace.Length > 0 ? Workspace : "keine Beschränkung (Missionen per vollständigem Pfad)")}\n"
            + $"Export erlaubt: {(AllowWrites ? "ja" : "nein")}\n\n"
            + $"Nur der Eintrag „{AgentConnection.ServerName}“ wird angelegt oder ersetzt, alles andere bleibt. "
            + $"Am sichersten ist es, {clientName} vorher zu schließen, damit es die Datei nicht mit einem älteren Stand überschreibt.";
        if (MessageBox.Show(this, question, "KI-Agent verbinden", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            List<string> backups = [];
            foreach (string configPath in configPaths)
            {
                if (AgentClientConfig.AddServer(client, configPath, Executable, Workspace, AllowWrites) is { } backup)
                    backups.Add(Path.GetFileName(backup));
            }
            AgentConnection.SaveSettings(Executable, Workspace, AllowWrites);
            Refresh();
            ShowStatus(TargetStatus, true, $"Eingetragen{(backups.Count == 0 ? string.Empty : $" (Sicherung: {string.Join(", ", backups)})")}. Jetzt {RestartHint(client)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Eintragen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnProbe(object sender, RoutedEventArgs args)
    {
        (bool success, string text) = AgentConnection.ProbeServer(Executable);
        ShowStatus(ServerStatus, success, success ? $"Server läuft: Version {text}." : $"Server-Test fehlgeschlagen: {text}");
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
        var dialog = new OpenFolderDialog { Title = "Freigegebenen Ordner wählen" };
        if (Directory.Exists(Workspace))
            dialog.InitialDirectory = Workspace;
        if (dialog.ShowDialog(this) == true)
            WorkspaceBox.Text = dialog.FolderName;
    }

    private void OnCopy(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string source })
            return;
        Clipboard.SetText(source == "Command" ? ClaudeCodeBox.Text : ManualBox.Text);
        CopyStatus.Text = Executable.Length == 0
            ? "Kopiert – Platzhalter vor dem Einfügen noch ersetzen."
            : "In die Zwischenablage kopiert.";
        if (File.Exists(Executable))
            AgentConnection.SaveSettings(Executable, Workspace, AllowWrites);
    }

    private void OnClose(object sender, RoutedEventArgs args) => Close();
}
