using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public sealed class ResourceReferenceTests
{
  [Fact]
  public void PopupIdsRetainSeparateRolesAndParameterPositionsWithoutAnInstallation ()
  {
    var document = IniDocument.ParseText ("[Trigger]\nnickname = start\nAct_PopUpDialog = 65537, 65538, CLOSE\nAct_SetTitle = 65537\nAct_Random = 123456\n");
    var references = MissionResourceIndex.Build (document, "test.ini", null);
    Assert.Equal (3, references.Count);
    Assert.Equal (new[] { 65537, 65538, 65537 }, references.Select (reference => reference.Ids));
    Assert.Equal (new[] { 0, 1, 0 }, references.Select (reference => reference.ParameterIndex));
    Assert.NotEqual (references[0].Role, references[1].Role);
    Assert.Equal (3, references[0].LineNumber);
    Assert.Equal ("start", references[0].SectionNickname);
    Assert.Equal (ResourceStatus.NoInstallation, ResourceEntry.Missing (65537).Status);
  }

  [Fact]
  public void ObjectiveParameterPositionsAndMalformedIdsAreExplicit ()
  {
    var document = IniDocument.ParseText ("[NNObjective]\ntype = rep_inst, Li01, 200\n[NNObjective]\ntype = ids, 300\n[Trigger]\nAct_SetOffer = -1\nAct_SetTitle = broken\n");
    var references = MissionResourceIndex.Build (document, null, null);
    Assert.Equal (new[] { 200, 300, -1 }, references.Select (reference => reference.Ids));
    Assert.Equal (2, references[0].ParameterIndex);
    Assert.Equal ("Missionsziel", references[0].Category);
  }

  [Fact]
  public void InfocardSegmentEditsPreserveAttributesFormattingAndParagraphs ()
  {
    const string raw = "<?xml version=\"1.0\"?><RDL><PUSH/><TRA data=\"0x01\"/><TEXT>Hello &amp; welcome</TEXT><PARA/><TEXT>Second</TEXT><POP/></RDL>";
    var card = InfocardDocument.Parse (raw);
    Assert.True (card.CanEditSegments);
    Assert.Equal (new[] { "Hello & welcome", "Second" }, card.TextSegments);
    Assert.Equal ("Hello & welcome\nSecond", card.PlainText);
    string changed = card.WithTextSegments (["Grüße <pilot>", "Next"]);
    Assert.Contains ("<TRA data=\"0x01\"/>", changed);
    Assert.Contains ("<TEXT>Grüße &lt;pilot&gt;</TEXT>", changed);
    Assert.Contains ("<PARA/>", changed);
    Assert.Equal ("Grüße <pilot>\nNext", InfocardDocument.Parse (changed).PlainText);
  }

  [Theory]
  [InlineData ("<!DOCTYPE RDL [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><RDL><TEXT>&x;</TEXT></RDL>")]
  [InlineData ("<RDL><TEXT>broken</RDL>")]
  [InlineData ("<HTML><TEXT>wrong format</TEXT></HTML>")]
  [InlineData ("<RDL><SCRIPT>bad</SCRIPT></RDL>")]
  public void InvalidOrUnsafeRdlCannotBeCommitted (string raw) => Assert.ThrowsAny<Exception> (() => InfocardDocument.Validate (raw));

  [Theory]
  [InlineData ("<RDL><!-- <TEXT>fake</TEXT> --><TEXT/></RDL>")]
  [InlineData ("<RDL><TEXT note=\">\">Real</TEXT></RDL>")]
  [InlineData ("<RDL><?comment <TEXT>fake</TEXT>?><TEXT/></RDL>")]
  public void SimplifiedEditorNeverChangesCommentOrAttributeTextInsteadOfRealSegments (string raw)
  {
    var document = InfocardDocument.Parse (raw);
    Assert.False (document.CanEditSegments);
    Assert.Throws<InvalidOperationException> (() => document.WithTextSegments (["Changed"]));
  }
}
