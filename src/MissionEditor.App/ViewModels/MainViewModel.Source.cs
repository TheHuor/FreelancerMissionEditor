using System.Globalization;
using System.IO;
using MissionEditor.Core.Ini;

namespace MissionEditor.App.ViewModels;

public sealed partial class MainViewModel
{
  private bool _isGraphView;
  private SectionLineItem? _selectedSourceLine;
  private ResourceLinkItem? _selectedResourceLink;
  private IReadOnlyList<SectionLineItem> _sourceLines = [];

  /// <summary>Physical source rows only, without synthesized headings or metadata rows.</summary>
  public IReadOnlyList<SectionLineItem> SourceLines => _sourceLines;
  /// <summary>The optional graph shares the middle workspace with source.</summary>
  public bool IsGraphView
  {
    get => _isGraphView;
    set { if (SetProperty (ref _isGraphView, value)) OnPropertyChanged (nameof (IsSourceView)); }
  }
  /// <summary>Source is the default workspace.</summary>
  public bool IsSourceView => !IsGraphView;

  /// <summary>The physical line inspected in either source view.</summary>
  public SectionLineItem? SelectedSourceLine
  {
    get => _selectedSourceLine;
    set
    {
      if (!SetProperty (ref _selectedSourceLine, value)) return;
      var previous = _selectedResourceLink;
      OnPropertyChanged (nameof (SourceContextLinks));
      OnPropertyChanged (nameof (SourceDefinitions));
      OnPropertyChanged (nameof (SourceContextErrors));
      OnPropertyChanged (nameof (SelectedSourceSection));
      SelectedResourceLink = SourceContextLinks.FirstOrDefault (link => link.Ids == previous?.Ids && link.Reference.Role == previous.Reference.Role)
          ?? SourceContextLinks.FirstOrDefault ();
      NotifySourceContext ();
    }
  }
  /// <summary>All distinct roles on the selected line, including indirect name/type references.</summary>
  public IReadOnlyList<ResourceLinkItem> SourceContextLinks => SelectedSourceLine?.ResourceLinks ?? [];
  /// <summary>One explicit edit action for each origin definition.</summary>
  public IReadOnlyList<ResourceLinkItem> SourceDefinitions => SourceContextLinks.Where (link => link.Reference.DefinitionPath is not null)
      .DistinctBy (link => (link.Reference.DefinitionPath!.ToUpperInvariant (), link.Reference.DefinitionLineNumber)).ToArray ();
  /// <summary>Unavailable nickname and invalid IDS explanations belong in the inspector.</summary>
  public string SourceContextErrors => string.Join (Environment.NewLine, SelectedSourceLine?.ResourceErrors ?? []);
  /// <summary>Section edited by the source toolbar.</summary>
  public IniSection? SelectedSourceSection => SelectedSourceLine is { } line ? Script?.Document.FindSectionAt (line.Number) : null;

  /// <summary>The individually selected resource role; selecting it does not open another window.</summary>
  public ResourceLinkItem? SelectedResourceLink
  {
    get => _selectedResourceLink;
    set { if (SetProperty (ref _selectedResourceLink, value)) NotifySourceContext (); }
  }
  /// <summary>Inspector heading.</summary>
  public string SourceContextHeading => SelectedSourceLine is { } line
      ? $"Zeile {line.Number}" + (SelectedResourceLink is { } link ? $" · {link.Reference.Role}" : "")
      : "Zeile oder Wert auswählen";
  /// <summary>Wrapped selectable source text or a precise resolution status.</summary>
  public string SourceContextText => SelectedResourceLink is { } link
      ? link.Resource?.HasRawResource == true ? link.Resource.Text : link.Resource?.StatusText ?? "Spielressourcen nicht geladen"
      : SelectedSourceLine is null ? "Eine IDS oder einen Nickname im Quelltext auswählen, um Zusatzinfos zu lesen."
      : "Für diese Zeile sind keine Ressourcentexte bekannt.";
  /// <summary>Actual DLL, actual language and definition provenance.</summary>
  public string SourceContextOrigin
  {
    get
    {
      if (SelectedResourceLink is not { } link) return string.Empty;
      string origin = link.Resource?.Identity is { } id ? Path.GetFileName (id.DllPath) : "Keine Ressourcen-DLL aufgelöst";
      if (link.Resource?.HasRawResource == true && link.Resource.Identity is { } actual)
      {
        string language;
        try { language = actual.Language == 0 ? "Sprachneutral" : CultureInfo.GetCultureInfo (actual.Language).DisplayName; }
        catch (CultureNotFoundException) { language = "Sprache"; }
        origin += $" · {language} ({actual.Language})";
      }
      return origin + (link.Reference.Provenance is { } provenance ? "\n" + provenance : "");
    }
  }
  private void NotifySourceContext ()
  {
    OnPropertyChanged (nameof (SelectedResourceLink));
    OnPropertyChanged (nameof (SourceContextHeading)); OnPropertyChanged (nameof (SourceContextText)); OnPropertyChanged (nameof (SourceContextOrigin));
  }
  private void RefreshSourceRows ()
  {
    int? previous = SelectedSourceLine?.Number;
    ResourceLinkItem? previousRole = SelectedResourceLink;
    _sourceLines = SelectedSectionLines.Where (line => line.Number > 0 && !line.IsCaption).ToArray ();
    OnPropertyChanged (nameof (SourceLines));
    SelectedSourceLine = SourceLines.FirstOrDefault (line => line.Number == previous) ?? SourceLines.FirstOrDefault ();
    if (SelectedSourceLine?.Number == previous && previousRole is not null)
      SelectedResourceLink = SourceContextLinks.FirstOrDefault (link => link.Reference.Role == previousRole.Reference.Role
          && link.Reference.ParameterIndex == previousRole.Reference.ParameterIndex) ?? SelectedResourceLink;
  }
  /// <summary>Select an inline resource without automatically opening its editor.</summary>
  public void InspectResource (ResourceLinkItem link)
  {
    SelectedSourceLine = SourceLines.FirstOrDefault (line => line.Number == link.Reference.LineNumber);
    SelectedResourceLink = SourceContextLinks.FirstOrDefault (candidate => candidate == link) ?? link;
  }
}
