using System.Windows;
using System.Windows.Controls;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App.Controls;

/// <summary>Details follow explicit source selection and never interrupt physical source rows.</summary>
public partial class SourceContextView : UserControl
{
  /// <summary>Create the shared source inspector.</summary>
  public SourceContextView () => InitializeComponent ();
  private void OnEditResource (object sender, RoutedEventArgs args)
  {
    if (DataContext is MainViewModel { SelectedResourceLink: { } link })
      RaiseEvent (new RoutedEventArgs (SourceLineView.ResourceRequestedEvent, link));
  }
  private void OnEditDefinition (object sender, RoutedEventArgs args)
  {
    if (sender is FrameworkElement { DataContext: ResourceLinkItem link })
      RaiseEvent (new RoutedEventArgs (SourceLineView.DefinitionRequestedEvent, link));
  }
}
