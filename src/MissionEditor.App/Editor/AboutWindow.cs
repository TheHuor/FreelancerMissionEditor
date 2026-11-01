using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MissionEditor.Core.Ini;

namespace MissionEditor.App;

/// <summary>Version and reference overview with copyable build information.</summary>
public partial class AboutWindow : Window
{
  /// <summary>The GUI informational version, including its source revision.</summary>
  public static string BuildVersion => VersionOf (typeof (AboutWindow).Assembly);

  /// <summary>The semantic version used in the main caption.</summary>
  public static string DisplayVersion => BuildVersion.Split ('+')[0];

  /// <summary>The prominent version label.</summary>
  public string VersionLabel => DisplayVersion;

  /// <summary>The complete build identifier, available as a tooltip and clipboard text.</summary>
  public string FullBuildVersion => BuildVersion;

  /// <summary>A short source revision, keeping hashes out of the main information hierarchy.</summary>
  public string RevisionLabel => BuildVersion.Contains ('+') ? $"Build {BuildVersion.Split ('+')[1][..Math.Min (8, BuildVersion.Split ('+')[1].Length)]}" : "Release";

  /// <summary>The actual running platform.</summary>
  public string RuntimeLabel => $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";

  /// <summary>The loaded WPF assembly version.</summary>
  public string WpfLabel => $"WPF {typeof (Window).Assembly.GetName ().Version}";

  /// <summary>Versioned repository components; CLI ships separately with the shared build version.</summary>
  public IReadOnlyList<ModuleInfo> Modules
  {
    get;
  } =
  [
      new("GUI", "WPF-Oberfläche", BuildVersion),
        new("Core", "INI, BINI und Prüfungen", VersionOf(typeof(IniDocument).Assembly)),
        new("CLI", "Kommandozeile · separat", BuildVersion),
        new("MCP", "Agent-Schnittstelle · separat", BuildVersion),
    ];

  /// <summary>Creates the themed information dialog.</summary>
  public AboutWindow ()
  {
    InitializeComponent ();
    DataContext = this;
  }

  private void OnCopy (object sender, RoutedEventArgs args) => Clipboard.SetText (
      string.Join (Environment.NewLine, Modules.Select (module => $"{module.Name}: {module.FullVersion}"))
      + Environment.NewLine + RuntimeLabel + Environment.NewLine + WpfLabel);

  private void OnClose (object sender, RoutedEventArgs args) => Close ();

  private void OnOpenReference (object sender, RoutedEventArgs args)
  {
    if (sender is not Button { Tag: string url })
      return;
    try
    {
      Process.Start (new ProcessStartInfo (url) { UseShellExecute = true });
    }
    catch (Win32Exception exception)
    {
      ThemedMessageBox.Show (this, exception.Message, "Link kann nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Information);
    }
  }

  private static string VersionOf (Assembly assembly) =>
      assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute> ()?.InformationalVersion
      ?? assembly.GetName ().Version?.ToString () ?? "unbekannt";
}

/// <summary>A repository component and its informational version.</summary>
public sealed record ModuleInfo (string Name, string Description, string FullVersion)
{
  /// <summary>The compact semantic version shown in the module list.</summary>
  public string Version => FullVersion.Split ('+')[0];
}
