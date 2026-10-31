using System.Text;
using MissionEditor.Core.Hashing;
using MissionEditor.Core.Ini;

namespace MissionEditor.Tests;

public class IniDocumentTests
{
    [Theory]
    [InlineData("[Mission]\r\nnpc_ship_file = x.ini\r\n")]
    [InlineData("[Mission]\nnpc_ship_file = x.ini")]
    [InlineData("; head\n\n[A]\r\nkey=1 ; note\n\r[B]\n  key2  =  a , b  \n")]
    [InlineData("")]
    public void RoundTripIsByteIdentical(string text)
    {
        byte[] original = IniDocument.Windows1252.GetBytes(text);
        Assert.Equal(original, IniDocument.Parse(original).ToBytes());
    }

    [Fact]
    public void RoundTripKeepsAnsiAndUndefinedBytes()
    {
        byte[] original = [.. Encoding.ASCII.GetBytes("[A]\nname = M"), 0xFC, 0x80, 0x81, (byte)'\n'];
        IniDocument document = IniDocument.Parse(original);
        Assert.Equal(original, document.ToBytes());
    }

    [Fact]
    public void RoundTripKeepsUtf8Bom()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[A]\nname = ä\n")];
        IniDocument document = IniDocument.Parse(original);
        Assert.True(document.HasBom);
        Assert.Equal("ä", document.Sections[0].Find("name")!.GetValue(0));
        Assert.Equal(original, document.ToBytes());
    }

    [Fact]
    public void ParsesLikeIniReader()
    {
        IniDocument document = IniDocument.ParseText("[Trigger] ; c\nAct_ActTrig = a , b ;comment\nCnd_True\n[;Old]\nx = 1\n");
        Assert.Equal(2, document.Sections.Count);
        IniSection trigger = document.Sections[0];
        Assert.True(trigger.Is("trigger"));
        IniLine action = trigger.Entries[0];
        Assert.Equal("Act_ActTrig", action.Key);
        Assert.Equal(["a", "b"], action.Values);
        Assert.Equal(";comment", action.Comment);
        Assert.Equal("Cnd_True", trigger.Entries[1].Key);
        Assert.Empty(trigger.Entries[1].Values);
        Assert.True(document.Sections[1].IsCommentedOut);
    }

    [Fact]
    public void RejectsBini()
    {
        Assert.Throws<InvalidDataException>(() => IniDocument.Parse(Encoding.ASCII.GetBytes("BINI\x01\0\0\0")));
    }

    [Theory]
    [InlineData("li01_01_base", 2804956685u)]
    [InlineData("LI01_01_BASE", 2804956685u)]
    public void CreateIdMatchesFreelancer(string nickname, uint expected)
    {
        Assert.Equal(expected, FlHash.CreateId(nickname));
    }
}
