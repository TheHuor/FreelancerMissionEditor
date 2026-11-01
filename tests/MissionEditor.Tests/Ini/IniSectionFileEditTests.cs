using MissionEditor.Core.Ini;

namespace MissionEditor.Tests;

public sealed class IniSectionFileEditTests : IDisposable
{
  private readonly string _folder = Path.Combine (Path.GetTempPath (), "definition-edit-" + Guid.NewGuid ());
  private string FilePath => Path.Combine (_folder, "definitions.ini");
  public IniSectionFileEditTests ()
  {
    Directory.CreateDirectory (_folder);
    File.WriteAllText (FilePath, "[Object]\r\nnickname = base\r\nids_name = 123\r\n\r\n[Zone]\r\nnickname = preserved\r\n");
  }
  public void Dispose () => Directory.Delete (_folder, true);

  [Fact]
  public void SavesOnlySelectedSectionAndBacksUpExactOriginal ()
  {
    byte[] original = File.ReadAllBytes (FilePath);
    var edit = IniSectionFileEdit.Open (FilePath, 1, 123);
    string backup = edit.Save ("[Object]\nnickname = base\nids_name = 456\n");
    Assert.Equal (original, File.ReadAllBytes (backup));
    var result = IniDocument.Load (FilePath);
    Assert.Equal ("456", result.Sections[0].Find ("ids_name")!.GetValue (0));
    Assert.Contains ("[Zone]\r\nnickname = preserved\r\n", File.ReadAllText (FilePath));
  }

  [Fact]
  public void ExternalChangeAndExtraSectionsLeaveOriginalUntouched ()
  {
    var edit = IniSectionFileEdit.Open (FilePath, 1, 123);
    Assert.Throws<InvalidDataException> (() => edit.Save ("[Object]\nids_name = 456\n[Zone]\nnickname = injected\n"));
    File.AppendAllText (FilePath, "; external change\n");
    byte[] changed = File.ReadAllBytes (FilePath);
    Assert.Throws<IOException> (() => edit.Save ("[Object]\nids_name = 456\n"));
    Assert.Equal (changed, File.ReadAllBytes (FilePath));
  }

  [Fact]
  public void RefusesStaleDefinitionLocation () =>
      Assert.Throws<InvalidDataException> (() => IniSectionFileEdit.Open (FilePath, 5, 123));
}
