using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

namespace MissionEditor.Tests;

public sealed class ResourceDllTests : IDisposable
{
  private readonly ResourceDllFixture _fixture = new ();
  private string _root => _fixture.Root;
  private string DllPath => _fixture.DllPath;
  private static string LongText => ResourceDllFixture.LongText;
  private const string Card = ResourceDllFixture.Card;
  public void Dispose () => _fixture.Dispose ();
  private GameText Texts () => GameData.Load (_root).Texts!;

  [Fact]
  public void FullStringAndActualLanguageAreReadWithoutWhitespaceCleanupOrTruncation ()
  {
    GameText texts = Texts ();
    ResourceEntry entry = texts.Resolve (1, 1033);
    Assert.True (entry.IsResolved, entry.StatusText);
    Assert.Equal (LongText, entry.Text);
    Assert.Equal ((ushort)1033, entry.Identity!.Language);
    Assert.Equal ("Deutsch", texts.Resolve (1, 1031).Text);
    Assert.Equal (ResourceStatus.EntryMissing, texts.Resolve (999).Status);
    Assert.Equal (ResourceStatus.SlotMissing, texts.Resolve (65537).Status);
  }

  [Fact]
  public void EnumerationVisitsActualStringBlocksAndInfocardsInAllLanguages ()
  {
    var entries = Texts ().Enumerate ();
    Assert.Equal (6, entries.Count);
    Assert.Equal (new[] { 1, 1, 2, 2, 3, 50 }, entries.OrderBy (entry => entry.Ids).Select (entry => entry.Ids));
    ResourceEntry card = Assert.Single (entries, entry => entry.Ids == 50);
    Assert.Equal ("First\nSecond", card.Text);
    Assert.Equal (Card, card.RawText);
    using var cancel = new CancellationTokenSource ();
    cancel.Cancel ();
    Assert.Throws<OperationCanceledException> (() => Texts ().Enumerate (cancel.Token));
  }

  [Fact]
  public void DirectSavePreservesBlockNeighborsOtherLanguagesAndMakesExactBackup ()
  {
    var texts = Texts ();
    byte[] before = File.ReadAllBytes (DllPath);
    ResourceDraft first = ResourceDraft.Create (texts.Resolve (1, 1033), "Updated Ω\n\nparagraph");
    ResourceDraft third = ResourceDraft.Create (texts.Resolve (3, 1033), "Third changed");
    ResourceSaveResult result = ResourceWriter.Save ([first, third]);
    Assert.False (result.IsCopy);
    Assert.Equal (before, File.ReadAllBytes (result.BackupPath!));
    texts.Reload ();
    Assert.Equal ("Updated Ω\n\nparagraph", texts.Resolve (1, 1033).Text);
    Assert.Equal ("Neighbor", texts.Resolve (2, 1033).Text);
    Assert.Equal ("Third changed", texts.Resolve (3, 1033).Text);
    Assert.Equal ("Deutsch", texts.Resolve (1, 1031).Text);
    Assert.Equal (Card, texts.Resolve (50, 1033).RawText);
  }

  [Fact]
  public void SaveAsCopiesCompleteDllAndLeavesOriginalUnchanged ()
  {
    var texts = Texts ();
    byte[] original = File.ReadAllBytes (DllPath);
    var card = texts.Resolve (50, 1033);
    string edited = InfocardDocument.Parse (card.RawText!).WithTextSegments (["New first", "New second"]);
    string copy = Path.Combine (_root, "copy.dll");
    ResourceSaveResult result = ResourceWriter.Save ([ResourceDraft.Create (card, edited)], copy);
    Assert.True (result.IsCopy);
    Assert.Null (result.BackupPath);
    Assert.Equal (original, File.ReadAllBytes (DllPath));
    // Register a second temporary fixture path; IDS slot remains zero.
    File.Copy (copy, DllPath, true);
    texts.Reload ();
    Assert.Equal ("New first\nNew second", texts.Resolve (50, 1033).Text);
    Assert.Equal (LongText, texts.Resolve (1, 1033).Text);
    Assert.Contains ("<TRA data=\"1\"/>", texts.Resolve (50, 1033).RawText!);
  }

  [Fact]
  public void ExternalChangesAndLocksFailWithoutReplacingOriginalOrMutatingDraft ()
  {
    var texts = Texts ();
    var draft = ResourceDraft.Create (texts.Resolve (1, 1033), "Draft retained");
    File.SetLastWriteTimeUtc (DllPath, DateTime.UtcNow.AddSeconds (-10));
    // Fingerprints compare bytes, so a timestamp-only change is harmless; modify actual file bytes.
    using (var stream = new FileStream (DllPath, FileMode.Append)) stream.WriteByte (123);
    byte[] external = File.ReadAllBytes (DllPath);
    Assert.Throws<IOException> (() => ResourceWriter.Save ([draft]));
    Assert.Equal (external, File.ReadAllBytes (DllPath));
    Assert.Equal ("Draft retained", draft.EditedText);
    // Export remains available after a source conflict.
    ResourceWriter.Save ([draft], Path.Combine (_root, "conflict-copy.dll"));
    var fresh = ResourceDraft.Create (Texts ().Resolve (1, 1033), "Locked");
    using (var locked = new FileStream (DllPath, FileMode.Open, FileAccess.Read, FileShare.Read))
      Assert.ThrowsAny<IOException> (() => ResourceWriter.Save ([fresh]));
    Assert.Equal (external, File.ReadAllBytes (DllPath));
  }

  [Theory]
  [InlineData ("abc\0def")]
  [InlineData ("")]
  public void InvalidStringCannotCreateADraft (string value) =>
      Assert.Throws<InvalidDataException> (() => ResourceDraft.Create (Texts ().Resolve (1, 1033), value));

  [Fact]
  public void StaleSelectionBeforeDraftCreationCanStillBeExportedWithoutOverwritingExternalChanges ()
  {
    var texts = Texts ();
    var selected = texts.Resolve (1, 1033);
    ResourceWriter.Save ([ResourceDraft.Create (selected, "External edit")]);
    byte[] externallyChanged = File.ReadAllBytes (DllPath);
    var unsaved = ResourceDraft.Create (selected, "User content retained");
    Assert.Throws<IOException> (() => ResourceWriter.Save ([unsaved]));
    string exported = Path.Combine (_root, "stale-export.dll");
    Assert.True (ResourceWriter.Save ([unsaved], exported).IsCopy);
    Assert.Equal (externallyChanged, File.ReadAllBytes (DllPath));
    File.Copy (exported, DllPath, true);
    texts.Reload ();
    Assert.Equal ("User content retained", texts.Resolve (1, 1033).Text);
  }

}
