using System.Text;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

namespace MissionEditor.Tests;

public sealed class ResourceHealthTests
{
  private const string Rdl = "<?xml version=\"1.0\"?><RDL><TEXT>Rich text</TEXT></RDL>";

  private static IReadOnlyList<ResourceProblem> Assess(GameData game, int ids, IEnumerable<IdsReference>? references = null)
      => ResourceHealth.Assess(game.Texts!.Resolve(ids), game.Texts.Variants(ids), references ?? []);

  private static IReadOnlyList<IdsReference> Uses(string mission)
      => MissionResourceIndex.Build(MissionScript.FromDocument(IniDocument.ParseText(mission)).Document, null, null);

  [Fact]
  public void ReadableStringsAndInfocardsAreFine()
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    Assert.Empty(Assess(game, 1));
    Assert.Empty(Assess(game, 50, Uses("[Trigger]\nnickname = offer\nAct_SetOffer = 50\n")));
  }

  [Fact]
  public void MissingEntriesAreFlaggedButNotWithoutInstallation()
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    var missing = Assert.Single(Assess(game, 999));
    Assert.Equal(ResourceProblemKind.EntryMissing, missing.Kind);
    var slot = Assert.Single(Assess(game, 9 << 16));
    Assert.Equal(ResourceProblemKind.DllUnavailable, slot.Kind);
    Assert.Empty(ResourceHealth.Assess(ResourceEntry.Missing(5), [], []));
  }

  [Fact]
  public void MalformedEncodingIsADecodeErrorOnly()
  {
    using var fixture = new ResourceDllFixture();
    // An odd trailing byte that is not padding; a single 0x00 would be.
    fixture.AddInfocardBytes(60, [.. Encoding.Unicode.GetBytes("﻿" + ResourceDllFixture.Card + "  "), 0x41]);
    fixture.AddInfocardBytes(60, Encoding.Unicode.GetBytes("<RDL><TEXT>Deutsch</TEXT></RDL>\0"), 1031);
    var game = GameData.Load(fixture.Root);
    Assert.Equal(ResourceStatus.DecodeError, game.Texts!.Resolve(60, 1033).Status);
    var problem = Assert.Single(Assess(game, 60));
    Assert.Equal(ResourceProblemKind.DecodeError, problem.Kind);
    Assert.Contains("Sprache 1033", problem.Reason);
    Assert.Contains("unvollständiges Byte", problem.Reason);
    Assert.Contains("RDL/XML-Reiter", problem.Reason);
  }

  [Fact]
  public void PaddingByteIsNotAProblem()
  {
    using var fixture = new ResourceDllFixture();
    fixture.AddInfocardBytes(60, [.. Encoding.Unicode.GetBytes("﻿" + ResourceDllFixture.Card), 0]);
    var game = GameData.Load(fixture.Root);
    Assert.Equal(ResourceStatus.Resolved, game.Texts!.Resolve(60, 1033).Status);
    Assert.Empty(Assess(game, 60));
  }

  [Fact]
  public void UnreadableRdlIsFlaggedForInfocardsAndRdlStrings()
  {
    using var fixture = new ResourceDllFixture();
    fixture.AddInfocard(61, "<RDL><TEXT>Unclosed</RDL>");
    fixture.AddInfocard(62, "<RDL><SCRIPT>Unsupported</SCRIPT></RDL>");
    var game = GameData.Load(fixture.Root);
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(3, 1033), "<RDL><TEXT>Unclosed</RDL>")]);
    game.Texts.Reload();
    Assert.Equal(ResourceProblemKind.InvalidRdl, Assert.Single(Assess(game, 61)).Kind);
    Assert.Contains("RDL-Struktur", Assert.Single(Assess(game, 62)).Reason);
    Assert.Equal(ResourceProblemKind.InvalidRdl, Assert.Single(Assess(game, 3)).Kind);
    Assert.Null(ResourceHealth.RdlError(game.Texts.Resolve(2, 1033)));
  }

  [Fact]
  public void RdlInALiteralDisplayIsARenderingConflictOnlyWhereItIsLoaded()
  {
    using var fixture = new ResourceDllFixture();
    fixture.AddInfocard(3, Rdl); // String 3 is plain and takes precedence; the infocard is never shown.
    var game = GameData.Load(fixture.Root);
    var title = Uses("[Trigger]\nnickname = popup\nAct_PopUpDialog = 3, 3\n");
    Assert.Empty(Assess(game, 3, title));
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(2, 1031), Rdl)]);
    game.Texts.Reload();
    var conflict = Assert.Single(Assess(game, 2, Uses("[Trigger]\nnickname = popup\nAct_PopUpDialog = 2, 2\n")));
    Assert.Equal(ResourceProblemKind.RenderingConflict, conflict.Kind);
    Assert.Contains("Dialogtitel", conflict.Reason);
    Assert.Contains("1031", conflict.Reason);
    Assert.Empty(Assess(game, 2, Uses("[Trigger]\nnickname = offer\nAct_SetOffer = 2\n")));
  }

  [Fact]
  public void EveryKindHasADistinctLabel()
  {
    var labels = Enum.GetValues<ResourceProblemKind>().Select(ResourceHealth.Label).ToArray();
    Assert.Equal(labels.Length, labels.Distinct().Count());
  }
}
