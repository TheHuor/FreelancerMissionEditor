using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Model;
using Trigger = MissionEditor.Core.Model.Trigger;

namespace MissionEditor.App;

/// <summary>Steps through the mission's triggers without the game (see <see cref="MissionSimulator"/>).</summary>
public partial class SimulationWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly MissionSimulator _simulator;

    /// <summary>Creates the window for the mission as it is in the editor now.</summary>
    public SimulationWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _simulator = new MissionSimulator(viewModel.Script!, fireTrueConditions: true);
        Refresh();
    }

    /// <summary>The simulator (for tests).</summary>
    public MissionSimulator Simulator => _simulator;

    /// <summary>Fires an active trigger by name (for tests).</summary>
    public void FireForTest(string nickname)
    {
        _simulator.Fire(_simulator.Active.First(trigger => trigger.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)));
        Refresh();
    }

    private void Refresh()
    {
        ActiveList.ItemsSource = _simulator.Active.Select(trigger => new ActiveItem(trigger)).ToList();
        ActiveCaption.Text = $"AKTIV ({_simulator.Active.Count})";
        var log = new List<LogItem>();
        foreach (SimulationStep step in _simulator.Steps)
        {
            log.Add(new LogItem(step.Trigger, $"{(step.Automatic ? "↻" : "▶")} {step.Trigger.Nickname} löst aus ({step.Trigger.ConditionText})",
                new Thickness(0, 6, 0, 0), FontWeights.SemiBold, (Brush)FindResource("TextBrush")));
            foreach (SimulationEvent item in step.Events)
                log.Add(new LogItem(step.Trigger, $"Z.{item.Line.Number}  {item.Text}", new Thickness(18, 1, 0, 0), FontWeights.Normal, (Brush)FindResource("MutedTextBrush")));
        }
        LogList.ItemsSource = log;
        if (log.Count > 0)
            LogList.ScrollIntoView(log[^1]);
        UndoButton.IsEnabled = _simulator.CanUndo;
        if (_simulator.End is { } end)
        {
            EndText.Text = end == "SUCCEED" ? "Mission erfolgreich beendet." : $"Mission beendet ({end}).";
            EndText.Foreground = (Brush)FindResource(end == "SUCCEED" ? "ActivateEdgeBrush" : "WarningBrush");
            EndText.Visibility = Visibility.Visible;
        }
        else if (_simulator.Active.Count == 0)
        {
            EndText.Text = "Kein Trigger mehr aktiv und kein Missionsende – hier bleibt die Mission stehen.";
            EndText.Foreground = (Brush)FindResource("WarningBrush");
            EndText.Visibility = Visibility.Visible;
        }
        else
            EndText.Visibility = Visibility.Collapsed;
    }

    private void OnFire(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ActiveItem item } && _simulator.End is null)
        {
            _simulator.Fire(item.Trigger);
            Refresh();
        }
    }

    private void OnActiveSelected(object sender, SelectionChangedEventArgs args)
    {
        if (ActiveList.SelectedItem is ActiveItem item)
            _viewModel.SelectByName(item.Trigger.Nickname);
    }

    private void OnLogSelected(object sender, SelectionChangedEventArgs args)
    {
        if (LogList.SelectedItem is LogItem item)
            _viewModel.SelectByName(item.Trigger.Nickname);
    }

    private void OnAutoTrueChanged(object sender, RoutedEventArgs args)
    {
        if (_simulator is null)
            return;
        _simulator.FireTrueConditions = AutoTrueBox.IsChecked == true;
        _simulator.Restart();
        Refresh();
    }

    private void OnUndo(object sender, RoutedEventArgs args)
    {
        _simulator.Undo();
        Refresh();
    }

    private void OnRestart(object sender, RoutedEventArgs args)
    {
        _simulator.Restart();
        Refresh();
    }

    private void OnClose(object sender, RoutedEventArgs args) => Close();

    /// <summary>A row of the active list.</summary>
    public sealed record ActiveItem(Trigger Trigger)
    {
        /// <summary>Name, marked when repeatable.</summary>
        public string Title => $"{Trigger.Nickname}{(Trigger.Repeatable ? "  (repeatable)" : string.Empty)}";

        /// <summary>Line and condition.</summary>
        public string Detail => $"Z.{Trigger.LineNumber}  {Trigger.ConditionText}";
    }

    /// <summary>A row of the log.</summary>
    public sealed record LogItem(Trigger Trigger, string Text, Thickness Indent, FontWeight Weight, Brush Brush);
}
