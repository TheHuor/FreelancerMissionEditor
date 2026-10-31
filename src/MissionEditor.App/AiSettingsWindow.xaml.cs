using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MissionEditor.App.Ai;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Chooses how "Frag KI" reaches a model and stores the API key encrypted.</summary>
public partial class AiSettingsWindow : Window
{
  private readonly AiSettings _settings;
  private bool _forgetKey;

  /// <summary>Creates the dialog for the current settings.</summary>
  public AiSettingsWindow ()
  {
    InitializeComponent ();
    _settings = AiSettings.Load ();
    ModelBox.Text = _settings.Model;
    CliBox.Text = _settings.CliPath;
    (_settings.Provider switch
    {
      AiProvider.ClaudeCli => ClaudeCliOption,
      AiProvider.CodexCli => CodexCliOption,
      _ => ApiOption,
    }).IsChecked = true;
    RefreshKeyStatus ();
    RefreshPanels ();
  }

  private AiProvider SelectedProvider => ClaudeCliOption.IsChecked == true ? AiProvider.ClaudeCli
      : CodexCliOption.IsChecked == true ? AiProvider.CodexCli
      : AiProvider.AnthropicApi;

  private void OnProviderChanged (object sender, RoutedEventArgs args)
  {
    if (IsInitialized && ApiPanel is not null)
      RefreshPanels ();
  }

  private void OnCliChanged (object sender, TextChangedEventArgs args)
  {
    if (IsInitialized && CliPanel is not null)
      RefreshPanels ();
  }

  private void RefreshPanels ()
  {
    bool api = SelectedProvider == AiProvider.AnthropicApi;
    ApiPanel.Visibility = api ? Visibility.Visible : Visibility.Collapsed;
    CliPanel.Visibility = api ? Visibility.Collapsed : Visibility.Visible;
    if (api)
      return;
    var probe = new AiSettings { Provider = SelectedProvider, CliPath = CliBox.Text.Trim () };
    string? program = new CliAdvisor (probe, SelectedProvider).FindProgram ();
    string name = SelectedProvider == AiProvider.ClaudeCli ? "claude" : "codex";
    ShowStatus (CliStatus, program is not null, program is not null
        ? $"Gefunden: {program}"
        : CliBox.Text.Trim ().Length > 0
            ? "Datei nicht gefunden."
            : $"„{name}“ ist nicht im Suchpfad. Programm installieren oder den Pfad zur {name}.exe/.cmd angeben.");
  }

  private void RefreshKeyStatus ()
  {
    bool stored = _settings.ApiKey.Length > 0 && !_forgetKey;
    bool fromEnvironment = !stored && Environment.GetEnvironmentVariable ("ANTHROPIC_API_KEY") is { Length: > 0 };
    ForgetKeyButton.IsEnabled = stored;
    ShowStatus (KeyStatus, stored || fromEnvironment, stored
        ? "Ein Schlüssel ist gespeichert (verschlüsselt für deinen Windows-Benutzer). Leer lassen, um ihn zu behalten."
        : fromEnvironment
            ? "Kein Schlüssel gespeichert; verwendet wird die Umgebungsvariable ANTHROPIC_API_KEY."
            : "Noch kein Schlüssel. Einen API-Schlüssel erzeugst du in der Anthropic Console (console.anthropic.com).");
  }

  private void ShowStatus (TextBlock target, bool good, string text)
  {
    target.Text = text;
    target.Foreground = (Brush)FindResource (good ? "MutedTextBrush" : "WarningBrush");
  }

  private void OnForgetKey (object sender, RoutedEventArgs args)
  {
    _forgetKey = true;
    KeyBox.Clear ();
    RefreshKeyStatus ();
  }

  private void OnBrowseCli (object sender, RoutedEventArgs args)
  {
    var dialog = new OpenFileDialog { Filter = "Programme (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat" };
    if (dialog.ShowDialog (this) == true)
      CliBox.Text = dialog.FileName;
  }

  private void OnSave (object sender, RoutedEventArgs args)
  {
    _settings.Provider = SelectedProvider;
    _settings.Model = ModelBox.Text.Trim ().Length > 0 ? ModelBox.Text.Trim () : AiSettings.DefaultModel;
    _settings.CliPath = CliBox.Text.Trim ();
    if (KeyBox.Password.Trim ().Length > 0)
      _settings.ApiKey = KeyBox.Password.Trim ();
    else if (_forgetKey)
      _settings.ApiKey = string.Empty;
    try
    {
      _settings.Save ();
      DialogResult = true;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
    {
      MessageBox.Show (this, exception.Message, "Einstellungen nicht gespeichert", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }
}
