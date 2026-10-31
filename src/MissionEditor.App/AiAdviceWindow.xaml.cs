using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using MissionEditor.App.Ai;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.App;

/// <summary>
/// Asks the configured model about one finding and shows the explanation and the proposed fix. The
/// fix is checked against the mission before it is offered and applied only on "Übernehmen".
/// </summary>
public partial class AiAdviceWindow : Window
{
  private readonly MainViewModel _viewModel;
  private readonly Finding _finding;
  private readonly IniDocument _askedDocument;
  private CancellationTokenSource? _cancellation;
  private AdviceRequest? _request;
  private CheckedFix? _fix;
  private Advice? _advice;

  /// <summary>Creates the window for a finding of the open mission; it starts asking when shown unless <paramref name="askOnOpen"/> is false (tests).</summary>
  public AiAdviceWindow (MainViewModel viewModel, Finding finding, bool askOnOpen = true)
  {
    InitializeComponent ();
    _viewModel = viewModel;
    _finding = finding;
    _askedDocument = viewModel.Script!.Document;
    FindingText.Text = $"Zeile {finding.LineNumber}: {finding.Message}";
    if (askOnOpen)
      Loaded += async (_, _) => await AskAsync ();
    Closed += (_, _) => _cancellation?.Cancel ();
  }

  /// <summary>For tests: shows an answer as if the model had sent it.</summary>
  public void ShowAnswerForTest (string answerJson)
  {
    MissionScript script = _viewModel.Script!;
    _request = AiAdvice.BuildRequest (script, new TriggerGraph (script), _finding, MissionName ());
    ShowAdvice (AiAdvice.Parse (answerJson));
  }

  /// <summary>For tests: the checked fix currently offered, or null.</summary>
  public CheckedFix? OfferedFix => _fix;

  private string MissionName () => _viewModel.FilePath is { } path ? Path.GetFileName (path) : "Mission";

  private async Task AskAsync ()
  {
    AiSettings settings = AiSettings.Load ();
    IAiAdvisor advisor = AiAdvisorFactory.Create (settings);
    MissionScript script = _viewModel.Script!;
    _request = AiAdvice.BuildRequest (script, new TriggerGraph (script), _finding, MissionName ());

    _cancellation?.Cancel ();
    _cancellation = new CancellationTokenSource ();
    SetBusy (true, $"{advisor.DisplayName} wird gefragt …");
    ResultPanel.Visibility = Visibility.Collapsed;
    ApplyButton.IsEnabled = false;
    try
    {
      string answer = await advisor.AskAsync (_request, _cancellation.Token);
      ShowAdvice (AiAdvice.Parse (answer));
      SetBusy (false, $"Antwort von {advisor.DisplayName}.");
    }
    catch (OperationCanceledException)
    {
      SetBusy (false, "Abgebrochen.");
    }
    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or HttpRequestException)
    {
      SetBusy (false, exception.Message, problem: true);
    }
  }

  private void ShowAdvice (Advice advice)
  {
    _advice = advice;
    ResultPanel.Visibility = Visibility.Visible;
    ExplanationText.Text = advice.Explanation;
    AssumptionsText.Text = advice.Assumptions.Count > 0 ? "Annahmen: " + string.Join (" · ", advice.Assumptions) : string.Empty;
    _fix = null;
    if (!advice.HasFix)
    {
      FixSummaryText.Text = "Keine Korrektur vorgeschlagen – die Entscheidung liegt beim Autor (siehe Erklärung).";
      FixPanel.Visibility = Visibility.Collapsed;
      CheckText.Text = string.Empty;
      ApplyButton.IsEnabled = false;
      return;
    }
    FixSummaryText.Text = advice.FixSummary;
    FixPanel.Visibility = Visibility.Visible;
    try
    {
      _fix = AiAdvice.CheckFix (_viewModel.Script!, _request!, _finding, advice);
      BeforeBox.Text = _fix.Before;
      AfterBox.Text = advice.ReplacementText.Replace ("\r\n", "\n").Replace ("\n", Environment.NewLine);
      ShowCheck (_fix);
    }
    catch (InvalidDataException exception)
    {
      BeforeBox.Text = string.Empty;
      AfterBox.Text = advice.ReplacementText;
      ShowStatus (CheckText, false, $"Der Vorschlag lässt sich so nicht übernehmen: {exception.Message}");
      ApplyButton.IsEnabled = false;
    }
  }

  private void ShowCheck (CheckedFix fix)
  {
    string newFindings = fix.NewFindings.Count == 0
        ? "keine neuen Funde"
        : $"{fix.NewFindings.Count} neue Funde: " + string.Join ("; ", fix.NewFindings.Take (3).Select (item => item.Message));
    ShowStatus (CheckText, fix.FindingResolved && fix.NewFindings.Count == 0,
        $"Geprüft: Der Fund {(fix.FindingResolved ? "verschwindet" : "bleibt bestehen")} · Fehler und Warnungen vorher {fix.FindingsBefore}, nachher {fix.FindingsAfter} · {newFindings}.");
    ApplyButton.IsEnabled = true;
  }

  // Re-check the fix when the user edits the proposal.
  private void OnAfterChanged (object sender, System.Windows.Controls.TextChangedEventArgs args)
  {
    if (_advice is null || _request is null || !AfterBox.IsKeyboardFocusWithin)
      return;
    try
    {
      _fix = AiAdvice.CheckFix (_viewModel.Script!, _request, _finding, _advice with
      {
        ReplacementText = AfterBox.Text
      });
      ShowCheck (_fix);
    }
    catch (InvalidDataException exception)
    {
      _fix = null;
      ShowStatus (CheckText, false, exception.Message);
      ApplyButton.IsEnabled = false;
    }
  }

  private void OnApply (object sender, RoutedEventArgs args)
  {
    if (_fix is null)
      return;
    if (_viewModel.Script?.Document != _askedDocument)
    {
      ShowStatus (CheckText, false, "Die Mission wurde seit der Frage geändert. Bitte „Erneut fragen“.");
      ApplyButton.IsEnabled = false;
      return;
    }
    try
    {
      _viewModel.ReplaceSection (_fix.Section, AfterBox.Text);
      _viewModel.ReportStatus ($"KI-Korrektur übernommen ({_fix.Section.Name} ab Zeile {_fix.Section.FirstLineNumber}). Strg+Z macht sie rückgängig, Strg+S speichert.");
      DialogResult = true;
    }
    catch (InvalidDataException exception)
    {
      ShowStatus (CheckText, false, exception.Message);
    }
  }

  private async void OnAskAgain (object sender, RoutedEventArgs args) => await AskAsync ();

  private void OnCancel (object sender, RoutedEventArgs args) => _cancellation?.Cancel ();

  private async void OnSettings (object sender, RoutedEventArgs args)
  {
    if (new AiSettingsWindow { Owner = this }.ShowDialog () == true && Progress.Visibility != Visibility.Visible)
      await AskAsync ();
  }

  private void OnClose (object sender, RoutedEventArgs args) => Close ();

  private void SetBusy (bool busy, string text, bool problem = false)
  {
    Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    AskAgainButton.IsEnabled = !busy;
    ShowStatus (StatusText, !problem, text);
  }

  private void ShowStatus (System.Windows.Controls.TextBlock target, bool good, string text)
  {
    target.Text = text;
    target.Foreground = (Brush)FindResource (good ? "MutedTextBrush" : "WarningBrush");
  }
}
