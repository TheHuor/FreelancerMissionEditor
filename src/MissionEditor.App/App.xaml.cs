using System.Windows;

namespace MissionEditor.App;

/// <summary>The WPF application.</summary>
public partial class App : Application
{
  /// <summary>Installs caption command routing for all editor windows.</summary>
  public App () => WindowCommands.Register ();
}
