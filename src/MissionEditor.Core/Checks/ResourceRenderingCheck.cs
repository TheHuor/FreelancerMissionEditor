using System.IO;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>Warn about RDL resources used by verified literal mission displays.</summary>
public sealed class ResourceRenderingCheck(GameData gameData) : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "resource-rendering";
  /// <inheritdoc/>
  public string Title => "Unpassendes Textformat für die Spielanzeige";
  /// <inheritdoc/>
  public IEnumerable<Finding> Run(MissionScript script)
  {
    if (gameData.Texts is not { } texts) yield break;
    var references = MissionResourceIndex.Build(script.Document, script.FilePath, gameData);
    foreach (var group in references.Where(reference => reference.Rendering is MissionTextRendering.PlainText or MissionTextRendering.Mixed).GroupBy(reference => reference.Ids))
    {
      // Use the same string-first lookup as FmtStr, separately for each actual language.
      // An unrelated RT_HTML resource with the same numeric ID must not produce a false warning.
      var preferred = texts.Resolve(group.Key);
      if (!preferred.HasRawResource) continue;
      var offending = Variants(texts, group.Key).Where(entry => entry.HasRawResource)
          .GroupBy(entry => entry.Identity!.Language).Select(language => language.OrderBy(entry => entry.Identity!.Kind).First())
          .Where(entry => MissionTextUsage.ContainsRdl(entry.RawText ?? entry.Text)).ToArray();
      if (offending.Length == 0) continue;
      foreach (var reference in group)
      {
        string? warning = MissionTextUsage.Warning(reference, offending[0].RawText ?? offending[0].Text);
        if (warning is null) continue;
        string languages = string.Join(", ", offending.Select(entry => entry.Identity!.Language));
        yield return new(FindingSeverity.Warning, Id, reference.LineNumber, reference.SectionNickname,
            warning + $" Sprachvarianten: {languages}.",
            "Klartext beibehalten oder eine eigene IDS für die formatierte Anzeige verwenden. Alle Fundstellen dieser ID prüfen; nicht automatisch umwandeln.");
      }
    }
  }
  private static IReadOnlyList<ResourceEntry> Variants(GameText texts, int ids)
  {
    try { return texts.Variants(ids); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
        or System.ComponentModel.Win32Exception or System.Text.DecoderFallbackException)
    { return []; } // An unreadable or concurrently replaced DLL cannot prove a rendering conflict.
  }
}
