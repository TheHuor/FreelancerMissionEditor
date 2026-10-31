using System.Text;
using MissionEditor.Core.Hashing;
using MissionEditor.Core.Ini;

namespace MissionEditor.Tests;

public class IniDocumentTests
{
  [Theory]
  [InlineData ("[Mission]\r\nnpc_ship_file = x.ini\r\n")]
  [InlineData ("[Mission]\nnpc_ship_file = x.ini")]
  [InlineData ("; head\n\n[A]\r\nkey=1 ; note\n\r[B]\n  key2  =  a , b  \n")]
  [InlineData ("")]
  public void RoundTripIsByteIdentical (string text)
  {
    byte[] original = IniDocument.Windows1252.GetBytes (text);
    Assert.Equal (original, IniDocument.Parse (original).ToBytes ());
  }

  [Fact]
  public void RoundTripKeepsAnsiAndUndefinedBytes ()
  {
    byte[] original = [.. Encoding.ASCII.GetBytes ("[A]\nname = M"), 0xFC, 0x80, 0x81, (byte)'\n'];
    IniDocument document = IniDocument.Parse (original);
    Assert.Equal (original, document.ToBytes ());
  }

  [Fact]
  public void RoundTripKeepsUtf8Bom ()
  {
    byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes ("[A]\nname = ä\n")];
    IniDocument document = IniDocument.Parse (original);
    Assert.True (document.HasBom);
    Assert.Equal ("ä", document.Sections[0].Find ("name")!.GetValue (0));
    Assert.Equal (original, document.ToBytes ());
  }

  [Fact]
  public void ParsesLikeIniReader ()
  {
    IniDocument document = IniDocument.ParseText ("[Trigger] ; c\nAct_ActTrig = a , b ;comment\nCnd_True\n[;Old]\nx = 1\n");
    Assert.Equal (2, document.Sections.Count);
    IniSection trigger = document.Sections[0];
    Assert.True (trigger.Is ("trigger"));
    IniLine action = trigger.Entries[0];
    Assert.Equal ("Act_ActTrig", action.Key);
    Assert.Equal (["a", "b"], action.Values);
    Assert.Equal (";comment", action.Comment);
    Assert.Equal ("Cnd_True", trigger.Entries[1].Key);
    Assert.Empty (trigger.Entries[1].Values);
    Assert.True (document.Sections[1].IsCommentedOut);
  }

  [Fact]
  public void RemoveSectionKeepsEverythingElse ()
  {
    const string Text = "[A]\r\nx = 1\r\n\r\n[Trigger]\r\nnickname = t\r\n; inner\r\nCnd_True = no_params\r\n\r\n;;;; next part\r\n[B]\r\ny = 2";
    IniDocument document = IniDocument.Parse (IniDocument.Windows1252.GetBytes (Text));
    IniDocument removed = document.RemoveSection (document.Sections[1]);
    Assert.Equal ("[A]\r\nx = 1\r\n\r\n;;;; next part\r\n[B]\r\ny = 2", IniDocument.Windows1252.GetString (removed.ToBytes ()));
    Assert.Equal (2, removed.Sections.Count);
    Assert.Equal (5, removed.Sections[1].FirstLineNumber);
  }

  [Fact]
  public void ReplaceSectionKeepsEverythingElse ()
  {
    const string Text = "[A]\r\nx = 1\r\n\r\n[Trigger]\r\nrepeatable = true\r\nnickname = t\r\n\r\n;;;; next\r\n[B]\r\ny = 2";
    IniDocument document = IniDocument.Parse (IniDocument.Windows1252.GetBytes (Text));
    IniDocument replaced = document.ReplaceSection (document.Sections[1], "[Trigger]\nnickname = t\nrepeatable = true\n");
    Assert.Equal ("[A]\r\nx = 1\r\n\r\n[Trigger]\r\nnickname = t\r\nrepeatable = true\r\n\r\n;;;; next\r\n[B]\r\ny = 2",
        IniDocument.Windows1252.GetString (replaced.ToBytes ()));
  }

  [Fact]
  public void ReplaceLastSectionWithoutFinalNewline ()
  {
    IniDocument document = IniDocument.ParseText ("[A]\nx = 1\n[B]\ny = 2");
    IniDocument replaced = document.ReplaceSection (document.Sections[1], "[B]\ny = 3");
    Assert.Equal ("[A]\nx = 1\n[B]\ny = 3", IniDocument.Windows1252.GetString (replaced.ToBytes ()));
  }

  [Fact]
  public void ComparesSectionEntries ()
  {
    IniDocument document = IniDocument.ParseText ("[T]\nnickname = a\nCnd_Timer = 2\n\n[T]\nnickname  =  a ; c\ncnd_timer = 2\n[T]\nnickname = a\nCnd_Timer = 3\n");
    Assert.True (document.Sections[0].HasSameEntries (document.Sections[1]));
    Assert.False (document.Sections[0].HasSameEntries (document.Sections[2]));
  }

  [Fact]
  public void RejectsBini ()
  {
    Assert.Throws<InvalidDataException> (() => IniDocument.Parse (Encoding.ASCII.GetBytes ("BINI\x01\0\0\0")));
  }

  [Theory]
  [InlineData ("li01_01_base", 2804956685u)]
  [InlineData ("LI01_01_BASE", 2804956685u)]
  public void CreateIdMatchesFreelancer (string nickname, uint expected)
  {
    Assert.Equal (expected, FlHash.CreateId (nickname));
  }
}
