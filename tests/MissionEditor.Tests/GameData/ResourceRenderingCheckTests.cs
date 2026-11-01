using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

namespace MissionEditor.Tests;

public sealed class ResourceRenderingCheckTests
{
  private const string Rdl = "<?xml version=\"1.0\"?><RDL><TEXT>Rich text</TEXT></RDL>";
  private static MissionScript Script(string text) => MissionScript.FromDocument(IniDocument.ParseText(text));

  [Fact]
  public void ClassifiesActualParametersAndKeepsUntracedUsesUnknown()
  {
    var script = Script("[Trigger]\nnickname = display\nAct_PopUpDialog = 3, 3, CLOSE\nAct_SetOffer = 3\nAct_SetTitle = 3\nAct_NNIds = 3, OBJECTIVE\n[NNObjective]\nnickname = target\ntype = ids, 3\n");
    var uses = MissionResourceIndex.Build(script.Document, null, null);
    Assert.Equal(MissionTextRendering.PlainText, uses.Single(use => use.Role == "Dialogtitel").Rendering);
    Assert.Equal(MissionTextRendering.Rdl, uses.Single(use => use.Role == "Dialogtext").Rendering);
    Assert.Equal(MissionTextRendering.Rdl, uses.Single(use => use.Role == "Missionsangebot").Rendering);
    Assert.Equal(MissionTextRendering.Unknown, uses.Single(use => use.Role == "Missionstitel").Rendering);
    Assert.Equal(MissionTextRendering.Mixed, uses.Single(use => use.SectionName == "NNObjective").Rendering);
    Assert.Contains("Gemischt", MissionTextUsage.Summary(uses));
    Assert.Contains("ungeprüft", MissionTextUsage.Summary(uses));
    Assert.Equal("Ungeprüft", MissionTextUsage.Summary([]));
    Assert.Null(MissionTextUsage.Warning(uses[0], "Literal text"));
    Assert.False(MissionTextUsage.ContainsRdl("<RDLexample>ordinary literal</RDLexample>"));
  }

  [Fact]
  public void CommonCheckerWarnsAtPlainAndMixedSitesOnly()
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(3, 1033), Rdl)]); game.Texts.Reload();
    var script = Script("[Trigger]\nnickname = display\nAct_PopUpDialog = 3, 3, CLOSE\nAct_SetOffer = 3\nAct_SetTitle = 3\n[NNObjective]\nnickname = target\ntype = ids, 3\n");
    var warnings = MissionChecker.Run(script, game).Where(finding => finding.CheckId == "resource-rendering").ToArray();
    Assert.Equal(2, warnings.Length);
    Assert.All(warnings, finding => Assert.Equal(FindingSeverity.Warning, finding.Severity));
    Assert.Contains(warnings, finding => finding.LineNumber == 3 && finding.Message.Contains("Dialogtitel"));
    Assert.Contains(warnings, finding => finding.LineNumber == 8 && finding.Message.Contains("HUD"));
    Assert.DoesNotContain(MissionChecker.Run(script, null), finding => finding.CheckId == "resource-rendering");
  }

  [Fact]
  public void ChecksNonDefaultLanguagesButDoesNotConfuseShadowedInfocards()
  {
    using var fixture = new ResourceDllFixture();
    fixture.AddInfocard(3, Rdl); // String 3 is plain and takes precedence in FmtStr.
    var game = GameData.Load(fixture.Root);
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(2, 1031), Rdl)]); game.Texts.Reload();
    var warnings = new ResourceRenderingCheck(game).Run(Script("[Trigger]\nnickname = languages\nAct_PopUpDialog = 3, 3\nAct_PopUpDialog = 2, 2\nAct_PopUpDialog = 60000, 60001\n")).ToArray();
    var warning = Assert.Single(warnings);
    Assert.Equal(4, warning.LineNumber);
    Assert.Contains("1031", warning.Message);
    Assert.DoesNotContain("1033", warning.Message);
  }
}
