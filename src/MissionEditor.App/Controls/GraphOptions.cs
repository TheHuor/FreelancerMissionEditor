using System.Windows.Media;
using MissionEditor.Core.Model;

namespace MissionEditor.App.Controls;

/// <summary>Static values for the graph toolbar (depth choices and legend colours).</summary>
public static class GraphOptions
{
    /// <summary>Selectable depths.</summary>
    public static IReadOnlyList<int> Depths { get; } = [0, 1, 2, 3, 4, 5, 6, 8, 10];

    /// <summary>Colour of Act_ActTrig edges.</summary>
    public static Brush ActivateBrush { get; } = TriggerGraphCanvas.BrushOf(TriggerReferenceKind.Activate);

    /// <summary>Colour of Act_DeactTrig edges.</summary>
    public static Brush DeactivateBrush { get; } = TriggerGraphCanvas.BrushOf(TriggerReferenceKind.Deactivate);

    /// <summary>Colour of Cnd_WatchTrigger edges.</summary>
    public static Brush WatchBrush { get; } = TriggerGraphCanvas.BrushOf(TriggerReferenceKind.Watch);

    /// <summary>Colour of Act_Save edges.</summary>
    public static Brush SaveBrush { get; } = TriggerGraphCanvas.BrushOf(TriggerReferenceKind.Save);
}
