using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>The named, non-trigger sections of a mission script.</summary>
public enum ScriptObjectKind
{
    /// <summary>[NPC]</summary>
    Npc,

    /// <summary>[MsnShip]</summary>
    Ship,

    /// <summary>[MsnFormation]</summary>
    Formation,

    /// <summary>[MsnSolar]</summary>
    Solar,

    /// <summary>[MsnLoot]</summary>
    Loot,

    /// <summary>[MsnRandEnc]</summary>
    RandomEncounter,

    /// <summary>[ObjList]</summary>
    ObjList,

    /// <summary>[Dialog]</summary>
    Dialog,

    /// <summary>[NNObjective]</summary>
    Objective,
}

/// <summary>A named section such as an [MsnShip] or an [ObjList].</summary>
public sealed class ScriptObject
{
    internal ScriptObject(ScriptObjectKind kind, IniSection section, IniLine? nicknameLine)
    {
        Kind = kind;
        Section = section;
        NicknameLine = nicknameLine;
        Nickname = nicknameLine?.GetValue(0) ?? string.Empty;
    }

    /// <summary>The section type.</summary>
    public ScriptObjectKind Kind { get; }

    /// <summary>The nickname, empty if the section has none.</summary>
    public string Nickname { get; }

    /// <summary>The section.</summary>
    public IniSection Section { get; }

    /// <summary>The "nickname" line, or null.</summary>
    public IniLine? NicknameLine { get; }

    /// <summary>The line to point at in messages.</summary>
    public int LineNumber => NicknameLine?.Number ?? Section.FirstLineNumber;

    /// <summary>Maps a section name to its kind, or null for other sections.</summary>
    public static ScriptObjectKind? KindOf(string sectionName) => sectionName.ToLowerInvariant() switch
    {
        "npc" => ScriptObjectKind.Npc,
        "msnship" => ScriptObjectKind.Ship,
        "msnformation" => ScriptObjectKind.Formation,
        "msnsolar" => ScriptObjectKind.Solar,
        "msnloot" => ScriptObjectKind.Loot,
        "msnrandenc" => ScriptObjectKind.RandomEncounter,
        "objlist" => ScriptObjectKind.ObjList,
        "dialog" => ScriptObjectKind.Dialog,
        "nnobjective" => ScriptObjectKind.Objective,
        _ => null,
    };

    /// <summary>The section name of a kind, for messages.</summary>
    public static string SectionNameOf(ScriptObjectKind kind) => kind switch
    {
        ScriptObjectKind.Npc => "NPC",
        ScriptObjectKind.Ship => "MsnShip",
        ScriptObjectKind.Formation => "MsnFormation",
        ScriptObjectKind.Solar => "MsnSolar",
        ScriptObjectKind.Loot => "MsnLoot",
        ScriptObjectKind.RandomEncounter => "MsnRandEnc",
        ScriptObjectKind.ObjList => "ObjList",
        ScriptObjectKind.Dialog => "Dialog",
        _ => "NNObjective",
    };

    /// <inheritdoc/>
    public override string ToString() => $"[{SectionNameOf(Kind)}] {Nickname}";
}
