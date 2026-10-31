using System.Windows;
using System.Windows.Input;

namespace MissionEditor.App;

/// <summary>Routes custom caption buttons through native window commands and normal close handling.</summary>
internal static class WindowCommands
{
  private static bool _registered;

  internal static void Register ()
  {
    if (_registered)
      return;
    _registered = true;
    CommandManager.RegisterClassCommandBinding (typeof (Window), new CommandBinding (
        SystemCommands.CloseWindowCommand, (sender, _) => SystemCommands.CloseWindow ((Window)sender)));
    CommandManager.RegisterClassCommandBinding (typeof (Window), new CommandBinding (
        SystemCommands.MinimizeWindowCommand, (sender, _) => SystemCommands.MinimizeWindow ((Window)sender)));
    CommandManager.RegisterClassCommandBinding (typeof (Window), new CommandBinding (
        SystemCommands.MaximizeWindowCommand, (sender, _) =>
        {
          var window = (Window)sender;
          if (window.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow (window);
          else
            SystemCommands.MaximizeWindow (window);
        }, (sender, args) => args.CanExecute = ((Window)sender).ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip));
  }
}
