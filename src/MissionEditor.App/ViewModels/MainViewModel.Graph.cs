using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

// Graph display options and the layout derived from the current trigger selection.
public sealed partial class MainViewModel
{
  /// <summary>The layout of the neighbourhood of the selected trigger.</summary>
  public GraphLayout? Layout
  {
    get => _layout;
    private set
    {
      if (SetProperty (ref _layout, value))
        OnPropertyChanged (nameof (HiddenNodeText));
    }
  }

  /// <summary>Note about nodes left out of the graph, empty if none.</summary>
  public string HiddenNodeText => Layout is { HiddenNodeCount: > 0 } layout
      ? $"{layout.HiddenNodeCount} Knoten ausgelassen (Ebene voll)"
      : string.Empty;

  /// <summary>Steps to follow backwards in the graph.</summary>
  public int DepthBackward
  {
    get => _depthBackward;
    set
    {
      if (SetProperty (ref _depthBackward, Math.Clamp (value, 0, 10)))
        UpdateLayout ();
    }
  }

  /// <summary>Steps to follow forwards in the graph.</summary>
  public int DepthForward
  {
    get => _depthForward;
    set
    {
      if (SetProperty (ref _depthForward, Math.Clamp (value, 0, 10)))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_ActTrig edges.</summary>
  public bool ShowActivate
  {
    get => _showActivate;
    set
    {
      if (SetProperty (ref _showActivate, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_DeactTrig edges.</summary>
  public bool ShowDeactivate
  {
    get => _showDeactivate;
    set
    {
      if (SetProperty (ref _showDeactivate, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Cnd_WatchTrigger edges.</summary>
  public bool ShowWatch
  {
    get => _showWatch;
    set
    {
      if (SetProperty (ref _showWatch, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_Save edges.</summary>
  public bool ShowSave
  {
    get => _showSave;
    set
    {
      if (SetProperty (ref _showSave, value))
        UpdateLayout ();
    }
  }

  /// <summary>Rebuilds the selected trigger's neighborhood using the current depth and edge filters.</summary>
  private void UpdateLayout ()
  {
    if (SelectedTrigger is null)
    {
      Layout = null;
      return;
    }
    var kinds = new HashSet<TriggerReferenceKind> ();
    if (ShowActivate)
      kinds.Add (TriggerReferenceKind.Activate);
    if (ShowDeactivate)
      kinds.Add (TriggerReferenceKind.Deactivate);
    if (ShowWatch)
      kinds.Add (TriggerReferenceKind.Watch);
    if (ShowSave)
      kinds.Add (TriggerReferenceKind.Save);
    Layout = GraphLayout.Build (SelectedTrigger.Node, new GraphLayoutOptions
    {
      NodeHeight = 56,
      NodeWidth = 260,
      DepthBackward = DepthBackward,
      DepthForward = DepthForward,
      Kinds = kinds,
    });
  }
}
