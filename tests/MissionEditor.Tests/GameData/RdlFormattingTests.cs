using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public sealed class RdlFormattingTests
{
  [Fact]
  public void FormattingDoesNotLeakAndDoesNotRewriteFontOrOtherSourceInstructions()
  {
    const string raw = "<?xml version=\"1.0\"?><RDL><PUSH/><STYLE id=\"2\"/><TRA data=\"0x112233A8\" mask=\"0xffffffff\" def=\"0\"/><TEXT>AB</TEXT><PARA/><JUST loc=\"C\"/><TEXT>Tail</TEXT><POP/></RDL>";
    var doc = InfocardDocument.Parse(raw);
    string result = doc.WithFormattedSegments([
      new RdlTextRun[] { new("A", Bold: true, Italic: true, Underline: true, Rgb: 0xFF8040), new("B") },
      new RdlTextRun[] { new("Tail") }
    ]);
    Assert.Contains("<STYLE id=\"2\"/>", result);
    Assert.Contains("<JUST loc=\"C\"/>", result);
    Assert.Contains("data=\"0x4080FF07\"", result); // exact COLORREF-packed data, avoiding retail color=# nibble swapping
    var styles = InfocardDocument.Parse(result).TextStyles();
    Assert.Equal(3, styles.Count);
    Assert.Equal(0x4080FFAfu, styles[0].Data);
    Assert.Equal(0x112233A8u, styles[1].Data);
    Assert.Equal(styles[1], styles[2]);
    Assert.Equal("AB\nTail", InfocardDocument.Parse(result).PlainText);
  }

  [Fact]
  public void NoOpPreservesEntitiesAndOriginalSourceByteForByte()
  {
    const string raw = "<RDL>  <TEXT custom=\"keep\">A&#38;B</TEXT><PARA/><TEXT>C</TEXT> </RDL>";
    var doc = InfocardDocument.Parse(raw);
    Assert.Equal(raw, doc.WithFormattedSegments([new RdlTextRun[] { new("A&B") }, new RdlTextRun[] { new("C") }]));
    string changed = doc.WithFormattedSegments([new RdlTextRun[] { new("<&\nNext") }, new RdlTextRun[] { new("C") }]);
    Assert.Contains("<TEXT custom=\"keep\">&lt;&amp;</TEXT><PARA/><TEXT custom=\"keep\">Next</TEXT>", changed);
  }

  [Fact]
  public void DefaultsAndPopFollowRetailSemanticsAndNamedPalette()
  {
    var doc = InfocardDocument.Parse("<RDL><TRA bold=\"true\" color=\"blue\"/><PUSH/><TRA bold=\"false\"/><POP/><TEXT>A</TEXT><TRA bold=\"default\"/><TEXT>B</TEXT></RDL>");
    var styles = doc.TextStyles();
    Assert.Equal(0xE0484800u, styles[0].Data);
    Assert.Equal(1u, styles[0].KnownMask & 1);
    Assert.Equal(0u, styles[1].KnownMask & 1); // outside the push, game default is unknown
    Assert.Equal(0xE0484800u, styles[1].Data & 0xFFFFFF00);
    var hex = InfocardDocument.Parse("<RDL><TRA color=\"#123456\"/><TEXT>x</TEXT></RDL>").TextStyles()[0];
    Assert.Equal(0x65432100u, hex.Data); // retail swaps nibbles in # notation
  }

  [Fact]
  public void ContinuousParagraphsComeFromTheEditorAndNeverFromTextFormattingBoundaries()
  {
    var doc = InfocardDocument.Parse("<RDL><TEXT>AB</TEXT><PARA/><TRA font=\"2\"/><TEXT>CD</TEXT></RDL>");
    string joined = doc.WithContinuousSegments([new RdlTextRun[] { new("A", Bold: true), new("B") }, new RdlTextRun[] { new("CD") }]);
    Assert.Equal("ABCD", InfocardDocument.Parse(joined).PlainText);
    Assert.DoesNotContain("<PARA", joined);
    Assert.Contains("<TRA font=\"2\"/>", joined);
    string split = doc.WithContinuousSegments([new RdlTextRun[] { new("A\nB\n") }, new RdlTextRun[] { new("C\nD\n") }]);
    Assert.Equal("A\nB\nC\nD\n", InfocardDocument.Parse(split).PlainText);
  }
}
