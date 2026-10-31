using System.IO;
using System.Windows;
using System.Windows.Media;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.App;

/// <summary>
/// Compares the open mission with another file (e.g. a draft the AI agent exported) section by section and
/// takes over the chosen sections, checked before they are applied.
/// </summary>
public partial class CompareWindow : Window
{
  private readonly MainViewModel _viewModel;
  private readonly IniDocument _baseDocument;
  private readonly IniDocument _other;
  private readonly List<ChangeItem> _items;
  private readonly (int Errors, int Warnings) _before;

  /// <summary>Creates the window for the open mission and another file.</summary>
  public CompareWindow (MainViewModel viewModel, string otherPath, IniDocument other)
  {
    InitializeComponent ();
    _viewModel = viewModel;
    _baseDocument = viewModel.Script!.Document;
    _other = other;
    _items = SectionDiff.Compare (_baseDocument, other).Select (change => new ChangeItem (change, this)).ToList ();
    _before = Count (_baseDocument);
    HeaderText.Text = _items.Count == 0
        ? $"{Path.GetFileName (otherPath)} hat dieselben Abschnitte wie die geöffnete Mission."
        : $"{(_items.Count == 1 ? "1 Abschnitt unterscheidet" : $"{_items.Count} Abschnitte unterscheiden")} sich von {otherPath}. Angehakte Abschnitte werden übernommen; "
          + "neue Abschnitte kommen hinter den Abschnitt, hinter dem sie in der anderen Datei stehen.";
    ChangeList.ItemsSource = _items;
    if (_items.Count > 0)
      ChangeList.SelectedIndex = 0;
    UpdateOutcome ();
  }

  /// <summary>The differing sections (for tests).</summary>
  public IReadOnlyList<SectionChange> Changes => _items.Select (item => item.Change).ToList ();

  /// <summary>Chooses only the given sections and applies them (for tests).</summary>
  public void ApplyForTest (Func<SectionChange, bool> choose)
  {
    foreach (ChangeItem item in _items)
      item.IsChosen = choose (item.Change);
    OnApply (this, new RoutedEventArgs ());
  }

  private (int Errors, int Warnings) Count (IniDocument document)
  {
    IReadOnlyList<Finding> findings = MissionChecker.Run (MissionScript.FromDocument (document, _viewModel.FilePath), _viewModel.GameData);
    return (findings.Count (finding => finding.Severity == FindingSeverity.Error), findings.Count (finding => finding.Severity == FindingSeverity.Warning));
  }

  private void OnChangeSelected (object sender, RoutedEventArgs args)
  {
    if (ChangeList.SelectedItem is not ChangeItem item)
      return;
    BeforeBox.Text = item.Change.Kind == SectionChangeKind.Added ? "(gibt es in der Mission noch nicht)" : item.Change.Before;
    AfterBox.Text = item.Change.Kind == SectionChangeKind.Removed ? "(gibt es in der anderen Datei nicht – Übernehmen löscht den Abschnitt)" : item.Change.After;
    BeforeCaption.Text = item.Change.CurrentLine is int current ? $"Jetzt in der Mission (ab Zeile {current})" : "Jetzt in der Mission";
    AfterCaption.Text = item.Change.OtherLine is int other ? $"In der anderen Datei (ab Zeile {other})" : "In der anderen Datei";
  }

  private void OnChoiceChanged (object sender, RoutedEventArgs args) => UpdateOutcome ();

  private void OnChooseAll (object sender, RoutedEventArgs args) => ChooseAll (true);

  private void OnChooseNone (object sender, RoutedEventArgs args) => ChooseAll (false);

  private void ChooseAll (bool chosen)
  {
    foreach (ChangeItem item in _items)
      item.IsChosen = chosen;
    ChangeList.Items.Refresh ();
    UpdateOutcome ();
  }

  // Shows what the checker says about the mission after the chosen sections were taken over.
  private void UpdateOutcome ()
  {
    List<SectionChange> chosen = _items.Where (item => item.IsChosen).Select (item => item.Change).ToList ();
    ApplyButton.IsEnabled = chosen.Count > 0;
    if (chosen.Count == 0)
    {
      OutcomeText.Text = _items.Count == 0 ? string.Empty : "Nichts angehakt.";
      OutcomeText.Foreground = (Brush)FindResource ("MutedTextBrush");
      return;
    }
    try
    {
      (int errors, int warnings) = Count (SectionDiff.Apply (_baseDocument, _other, chosen));
      OutcomeText.Text = $"Danach: {errors} Fehler, {warnings} Warnungen (jetzt {_before.Errors} Fehler, {_before.Warnings} Warnungen).";
      OutcomeText.Foreground = (Brush)FindResource (errors > _before.Errors || warnings > _before.Warnings ? "WarningBrush" : "MutedTextBrush");
    }
    catch (InvalidDataException exception)
    {
      OutcomeText.Text = exception.Message;
      OutcomeText.Foreground = (Brush)FindResource ("ErrorBrush");
      ApplyButton.IsEnabled = false;
    }
  }

  private void OnApply (object sender, RoutedEventArgs args)
  {
    if (_viewModel.Script?.Document != _baseDocument)
    {
      MessageBox.Show (this, "Die Mission wurde inzwischen geändert. Bitte den Vergleich neu öffnen.", "Übernehmen", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }
    List<SectionChange> chosen = _items.Where (item => item.IsChosen).Select (item => item.Change).ToList ();
    try
    {
      _viewModel.ApplyDocument (SectionDiff.Apply (_baseDocument, _other, chosen), chosen.Count == 1 ? "1 Abschnitt aus dem Vergleich übernommen" : $"{chosen.Count} Abschnitte aus dem Vergleich übernommen");
      Close ();
    }
    catch (InvalidDataException exception)
    {
      MessageBox.Show (this, exception.Message, "Übernehmen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OnClose (object sender, RoutedEventArgs args) => Close ();

  /// <summary>A row of the list: one differing section and whether to take it over.</summary>
  public sealed class ChangeItem (SectionChange change, FrameworkElement owner)
  {
    /// <summary>The difference.</summary>
    public SectionChange Change { get; } = change;

    /// <summary>True when the section is taken over; changed and added ones are chosen at first, removals not.</summary>
    public bool IsChosen { get; set; } = change.Kind != SectionChangeKind.Removed;

    /// <summary>The section title.</summary>
    public string Title => Change.Title;

    /// <summary>"geändert", "neu" or "entfernt".</summary>
    public string KindText => Change.Kind switch
    {
      SectionChangeKind.Changed => "geändert",
      SectionChangeKind.Added => "neu",
      _ => "entfernt",
    };

    /// <summary>The colour of the kind.</summary>
    public Brush KindBrush => (Brush)owner.FindResource (Change.Kind switch
    {
      SectionChangeKind.Changed => "AccentBrush",
      SectionChangeKind.Added => "ActivateEdgeBrush",
      _ => "WarningBrush",
    });
  }
}
