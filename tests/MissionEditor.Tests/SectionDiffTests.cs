using MissionEditor.Core.Ini;

namespace MissionEditor.Tests;

public class SectionDiffTests
{
    private const string Current = """
        [Mission]
        npc_ship_file = missions\m99\npcships.ini

        [Trigger]
        nickname = start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = secnod

        ; old leftover
        [Trigger]
        nickname = old
        Cnd_True = no_params
        """;

    private const string Draft = """
        [Mission]
        npc_ship_file = missions\m99\npcships.ini

        [Trigger]
        nickname = start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = second

        [Trigger]
        nickname = second
        Cnd_Timer = 5
        """;

    [Fact]
    public void SectionsAreMatchedByNameAndNickname()
    {
        IReadOnlyList<SectionChange> changes = SectionDiff.Compare(IniDocument.ParseText(Current), IniDocument.ParseText(Draft));
        Assert.Equal(3, changes.Count);
        SectionChange changed = Assert.Single(changes, change => change.Kind == SectionChangeKind.Changed);
        Assert.Equal("[Trigger] start", changed.Title);
        Assert.Contains("secnod", changed.Before);
        Assert.Contains("Act_ActTrig = second", changed.After);
        Assert.Equal("[Trigger] second", Assert.Single(changes, change => change.Kind == SectionChangeKind.Added).Title);
        Assert.Equal("[Trigger] old", Assert.Single(changes, change => change.Kind == SectionChangeKind.Removed).Title);
    }

    [Fact]
    public void ChosenChangesAreAppliedAndTheRestStays()
    {
        IniDocument current = IniDocument.ParseText(Current);
        IniDocument draft = IniDocument.ParseText(Draft);
        IReadOnlyList<SectionChange> changes = SectionDiff.Compare(current, draft);

        IniDocument partly = SectionDiff.Apply(current, draft, changes.Where(change => change.Kind != SectionChangeKind.Removed));
        string text = string.Join("\n", partly.Lines.Select(line => line.Text));
        Assert.Contains("Act_ActTrig = second", text);
        Assert.Contains("nickname = old", text);
        // The added trigger follows the one it follows in the draft.
        Assert.True(text.IndexOf("nickname = second", StringComparison.Ordinal) > text.IndexOf("nickname = start", StringComparison.Ordinal));
        Assert.True(text.IndexOf("nickname = second", StringComparison.Ordinal) < text.IndexOf("nickname = old", StringComparison.Ordinal));

        IniDocument all = SectionDiff.Apply(current, draft, changes);
        Assert.Empty(SectionDiff.Compare(all, draft));
    }
}
