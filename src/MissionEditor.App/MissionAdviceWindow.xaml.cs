using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using MissionEditor.App.Ai;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App;

/// <summary>"Frag KI" about the whole mission: explanation or gaps and breaks, answered as text inside the editor.</summary>
public partial class MissionAdviceWindow : Window
{
  private readonly MainViewModel _viewModel;
  private readonly MissionQuestion _question;
  private CancellationTokenSource? _cancellation;

  /// <summary>Creates the window; with <paramref name="askOnOpen"/> the model is asked right away.</summary>
  public MissionAdviceWindow (MainViewModel viewModel, MissionQuestion question, bool askOnOpen = true)
  {
    InitializeComponent ();
    _viewModel = viewModel;
    _question = question;
    TitleText.Text = question == MissionQuestion.Explain ? "Mission erklären" : "Lücken und Brüche";
    IntroText.Text = (question == MissionQuestion.Explain
            ? "Das Sprachmodell bekommt einen Auszug der Mission (alle Trigger, Ziele, Namen und die Funde) und erklärt den Ablauf."
            : "Das Sprachmodell bekommt einen Auszug der Mission (alle Trigger, Ziele, Namen und die Funde) und sucht nach Stellen, an denen der Ablauf hängen bleibt.")
        + " Es ändert nichts an der Datei." + (viewModel.IsModified ? " Gefragt wird der aktuelle Stand im Editor, auch ungespeichert." : string.Empty);
    if (askOnOpen)
      Loaded += async (_, _) => await AskAsync ();
    Closed += (_, _) => _cancellation?.Cancel ();
  }

  /// <summary>Shows an answer without asking (for tests and previews).</summary>
  public void ShowAnswerForTest (string answer) => ShowAnswer (answer, "Beispielantwort.");

  /// <summary>The request that would be sent (for tests).</summary>
  public AdviceRequest BuildRequest () => MissionAdvice.Build (_viewModel.Script!, _question, MissionName ());

  private string MissionName () => _viewModel.FilePath is { } path ? Path.GetFileName (path) : "Mission";

  private async Task AskAsync ()
  {
    if (_viewModel.Script is null)
      return;
    AiSettings settings = AiSettings.Load ();
    IAiAdvisor advisor = AiAdvisorFactory.Create (settings);
    AdviceRequest request = BuildRequest ();

    _cancellation?.Cancel ();
    _cancellation = new CancellationTokenSource ();
    SetBusy (true, $"{advisor.DisplayName} wird gefragt … (bei großen Missionen kann das eine Minute dauern)");
    try
    {
      string answer = await advisor.AskAsync (request, _cancellation.Token);
      ShowAnswer (answer, $"Antwort von {advisor.DisplayName}.");
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

  private void ShowAnswer (string answer, string status)
  {
    AnswerBox.Text = answer.Trim ();
    CopyButton.IsEnabled = AnswerBox.Text.Length > 0;
    SetBusy (false, status);
  }

  private void SetBusy (bool busy, string text, bool problem = false)
  {
    Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    AskAgainButton.IsEnabled = !busy;
    StatusText.Text = text;
    StatusText.Foreground = (Brush)FindResource (problem ? "WarningBrush" : "MutedTextBrush");
  }

  private async void OnAskAgain (object sender, RoutedEventArgs args) => await AskAsync ();

  private void OnCancel (object sender, RoutedEventArgs args) => _cancellation?.Cancel ();

  private void OnCopy (object sender, RoutedEventArgs args)
  {
    Clipboard.SetText (AnswerBox.Text);
    StatusText.Text = "Antwort kopiert.";
  }

  private async void OnSettings (object sender, RoutedEventArgs args)
  {
    if (new AiSettingsWindow { Owner = this }.ShowDialog () == true && Progress.Visibility != Visibility.Visible)
      await AskAsync ();
  }

  private void OnClose (object sender, RoutedEventArgs args) => Close ();
}
