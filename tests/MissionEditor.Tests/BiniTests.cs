using System.Text;
using MissionEditor.Core.Ini;

namespace MissionEditor.Tests;

public class BiniTests
{
    // Independent fixture writer follows the packed structures in common_ini.h.
    private static byte[] Fixture(params (byte Type, object Value)[] values)
    {
        using var strings = new MemoryStream();
        int Add(string value) { int offset = (int)strings.Position; strings.Write(Encoding.Latin1.GetBytes(value)); strings.WriteByte(0); return offset; }
        int section = Add("Trigger"), key = Add("nickname");
        using var records = new MemoryStream();
        using var writer = new BinaryWriter(records);
        writer.Write((ushort)section); writer.Write((ushort)1);
        writer.Write((ushort)key); writer.Write((byte)values.Length);
        foreach (var (type, value) in values)
        {
            writer.Write(type);
            switch (type)
            {
                case 0: writer.Write((uint)(byte)value); break;
                case 1: writer.Write((int)value); break;
                case 2: writer.Write((float)value); break;
                case 3: writer.Write(Add((string)value)); break;
            }
        }
        using var result = new MemoryStream();
        using var output = new BinaryWriter(result);
        output.Write(Encoding.ASCII.GetBytes("BINI")); output.Write(1u); output.Write(12 + (int)records.Length);
        output.Write(records.ToArray()); output.Write(strings.ToArray());
        return result.ToArray();
    }

    [Fact]
    public void LoadsTypedParametersAndPreservesOriginalBytes()
    {
        byte[] bytes = Fixture((0, (byte)1), (1, -12), (2, 1.25f), (3, "Müller, test; value"));
        IniDocument doc = IniDocument.Parse(bytes);
        Assert.Equal(new[] { "1", "-12", "1.25", "Müller, test; value" }, doc.Sections[0].Entries[0].Values);
        Assert.Equal(bytes, doc.ToBytes());
        bytes[0] = 0;
        Assert.Equal((byte)'B', doc.ToBytes()[0]);
    }

    [Fact]
    public void EditAndRemoveKeepBinaryFormatAndUntouchedTypedValues()
    {
        IniDocument doc = IniDocument.Parse(Fixture((3, "old")));
        IniDocument edited = doc.ReplaceSection(doc.Sections[0], "[Trigger]\nnickname = newer\nCnd_Timer = 2.5\n");
        Assert.Equal("BINI", Encoding.ASCII.GetString(edited.ToBytes(), 0, 4));
        IniDocument loaded = IniDocument.Parse(edited.ToBytes());
        Assert.Equal("newer", loaded.Sections[0].Find("nickname")!.GetValue(0));
        Assert.Equal("2.5", loaded.Sections[0].Find("Cnd_Timer")!.GetValue(0));
        Assert.Empty(IniDocument.Parse(loaded.RemoveSection(loaded.Sections[0]).ToBytes()).Sections);
    }

    [Fact]
    public void EditableTextKeepsStringsWithDelimitersAndNumericLookingStrings()
    {
        IniDocument doc = IniDocument.Parse(Fixture((3, "007"), (3, "a,b;c\nquote\"\\"), (2, 1f)));
        string text = IniDocument.GetEditableText(doc.Sections[0]);
        IniDocument edited = doc.ReplaceSection(doc.Sections[0], text + "\nnew_value = 10");
        IniDocument loaded = IniDocument.Parse(edited.ToBytes());
        Assert.Equal(doc.Sections[0].Entries[0].Values, loaded.Sections[0].Entries[0].Values);
        // Original types survive rewriting a section and adding another entry.
        byte[] encoded = edited.ToBytes();
        Assert.Equal(3, encoded[19]); Assert.Equal(3, encoded[24]); Assert.Equal(2, encoded[29]);
    }

    [Fact]
    public void SaveAndSaveAsRetainBinarySignature()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, Fixture((3, "old")));
            IniDocument doc = IniDocument.Load(file);
            doc.ReplaceSection(doc.Sections[0], "[Trigger]\nnickname = changed").Save(file);
            Assert.Equal("changed", IniDocument.Load(file).Sections[0].Find("nickname")!.GetValue(0));
            Assert.Equal("BINI", Encoding.ASCII.GetString(File.ReadAllBytes(file), 0, 4));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void RejectsMalformedHeadersOffsetsTypesAndTruncation()
    {
        byte[] valid = Fixture((3, "hello"));
        for (int size = 4; size < valid.Length; size++)
            Assert.Throws<InvalidDataException>(() => IniDocument.Parse(valid[..size]));
        byte[] bad = (byte[])valid.Clone(); bad[19] = 99;
        Assert.Throws<InvalidDataException>(() => IniDocument.Parse(bad));
        bad = (byte[])valid.Clone(); Array.Fill(bad, (byte)255, 20, 4);
        Assert.Throws<InvalidDataException>(() => IniDocument.Parse(bad));
    }
    [Fact]
    public void RemovingDuplicateSectionPreservesTypesOfRemainingSection()
    {
        byte[] first = Fixture((0, (byte)7)), second = Fixture((1, 7));
        byte[] bytes = [.. first[..12], .. first[12..24], .. second[12..24], .. first[24..]];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 36);
        IniDocument doc = IniDocument.Parse(bytes);
        byte[] saved = doc.RemoveSection(doc.Sections[0]).ToBytes();
        Assert.Equal(1, saved[19]);
    }

    [Fact]
    public void FailedBinarySaveDoesNotOverwriteDestination()
    {
        string file = Path.GetTempFileName();
        try
        {
            byte[] original = Fixture((3, "original"));
            File.WriteAllBytes(file, original);
            IniDocument doc = IniDocument.Parse(original);
            IniDocument edited = doc.ReplaceSection(doc.Sections[0], "[Trigger]\nnickname = changed\n; comment not representable");
            Assert.Throws<InvalidDataException>(() => edited.Save(file));
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void LargeIntegerTextIsNotRoundedToSinglePrecision()
    {
        IniDocument doc = IniDocument.Parse(Fixture((3, "old")));
        IniDocument edited = doc.ReplaceSection(doc.Sections[0], "[Trigger]\nnickname = old\nids = 4294967295");
        Assert.Equal("4294967295", IniDocument.Parse(edited.ToBytes()).Sections[0].Find("ids")!.GetValue(0));
    }
    [Fact]
    public void EditingOneParameterKeepsUnchangedSiblingTypesAndPayloads()
    {
        IniDocument doc = IniDocument.Parse(Fixture((0, (byte)1), (2, 1f), (2, float.PositiveInfinity), (3, "old")));
        string text = IniDocument.GetEditableText(doc.Sections[0]).Replace("old", "new");
        byte[] encoded = doc.ReplaceSection(doc.Sections[0], text).ToBytes();
        Assert.Equal(0, encoded[19]); Assert.Equal(2, encoded[24]); Assert.Equal(2, encoded[29]);
        Assert.Equal("Infinity", IniDocument.Parse(encoded).Sections[0].Entries[0].GetValue(2));
    }
    [Fact]
    public void BinaryFloatBooleanIsInterpretedLikeEngine()
    {
        byte[] original = Fixture((3, "t"));
        int table = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(8));
        byte[] entry = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)(original.Length - table));
        entry[2] = 1; entry[3] = 2;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(4), 0.5f);
        byte[] bytes = [.. original[..table], .. entry, .. original[table..], .. Encoding.ASCII.GetBytes("repeatable\0")];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), table + entry.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 2);
        Assert.True(MissionEditor.Core.Model.MissionScript.FromDocument(IniDocument.Parse(bytes)).Triggers[0].Repeatable);
    }
}
