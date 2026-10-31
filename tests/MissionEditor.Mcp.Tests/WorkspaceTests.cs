using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;

namespace MissionEditor.Mcp.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mission-mcp-" + Guid.NewGuid());

    public WorkspaceTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "sample.ini"),
            "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = secnod\n\n[Trigger]\nnickname = second\nCnd_Timer = 5\n");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void InspectionAndSuggestionsExposeEvidenceWithoutInventingTrigger()
    {
        var workspace = new MissionWorkspace(_root);
        Assert.Equal(2, workspace.Inspect("sample.ini").TriggerCount);
        Assert.Contains(workspace.Check("sample.ini").Items, finding => finding.CheckId == "unknown-trigger");
        MissingSuggestion suggestion = workspace.SuggestMissing("sample.ini", "secnod");
        Assert.Contains("second", suggestion.Candidates);
        Assert.Equal("start", suggestion.References[0].Source);
        Assert.NotEmpty(workspace.Context("sample.ini", "start").Nodes);
    }

    [Theory]
    [InlineData("../outside.ini")]
    [InlineData("sub/../../outside.ini")]
    public void RejectsPathsOutsideWorkspace(string path) =>
        Assert.Throws<UnauthorizedAccessException>(() => new MissionWorkspace(_root).Inspect(path));

    [Fact]
    public void WithoutRootsAbsolutePathsWorkAndRelativePathsAreExplained()
    {
        var workspace = new MissionWorkspace([], allowWrites: true);
        string sample = Path.Combine(_root, "sample.ini");
        MissionSummary summary = workspace.Inspect(sample);
        Assert.Equal(sample, summary.Path);
        Assert.Contains(workspace.Check(sample).Items, finding => finding.CheckId == "unknown-trigger");

        DraftPreview draft = workspace.PrepareSectionEdit(sample, summary.Sha256, 0, "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = second");
        Assert.Equal(sample, draft.Source);
        string copy = Path.Combine(_root, "fixed.ini");
        Assert.Equal(copy, workspace.WriteDraft(draft.Id, copy).Path);
        Assert.Throws<DirectoryNotFoundException>(() => workspace.WriteDraft(workspace.PrepareNewMission("[Trigger]\nnickname = x\nCnd_True = no_params").Id,
            Path.Combine(_root, "missing-folder", "new.ini")));

        ArgumentException relative = Assert.Throws<ArgumentException>(() => workspace.Inspect("sample.ini"));
        Assert.Contains("kein absoluter Pfad", relative.Message);
        Assert.Throws<ArgumentException>(() => workspace.Inspect(Path.Combine(_root, "notes.txt")));
    }

    [Fact]
    public void SeveralRootsAreAllowedAndTheErrorNamesThem()
    {
        string first = Path.Combine(_root, "first");
        string second = Path.Combine(_root, "second");
        string outside = Path.Combine(_root, "outside");
        foreach (string folder in new[] { first, second, outside })
            Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(_root, "sample.ini"), Path.Combine(first, "main.ini"));
        File.Copy(Path.Combine(_root, "sample.ini"), Path.Combine(second, "other.ini"));
        File.Copy(Path.Combine(_root, "sample.ini"), Path.Combine(outside, "blocked.ini"));
        // The same folder twice (once with "..") counts once.
        var workspace = new MissionWorkspace([first, second, Path.Combine(outside, "..", "second")]);
        Assert.Equal(2, workspace.Roots.Count);

        Assert.Equal("main.ini", workspace.Inspect("main.ini").Path);
        string other = Path.Combine(second, "other.ini");
        Assert.Equal(other, workspace.Inspect(other).Path);
        UnauthorizedAccessException blocked = Assert.Throws<UnauthorizedAccessException>(() => workspace.Inspect(Path.Combine(outside, "blocked.ini")));
        Assert.Contains(first, blocked.Message);
        Assert.Contains(second, blocked.Message);
        Assert.Contains("blocked.ini", blocked.Message);
        Assert.Throws<DirectoryNotFoundException>(() => new MissionWorkspace([Path.Combine(_root, "does-not-exist")]));
    }

    [Fact]
    public void DraftsDoNotChangeSourceAndWritesRequireExplicitMode()
    {
        var workspace = new MissionWorkspace(_root);
        byte[] original = File.ReadAllBytes(Path.Combine(_root, "sample.ini"));
        MissionSummary summary = workspace.Inspect("sample.ini");
        DraftPreview draft = workspace.PrepareSectionEdit("sample.ini", summary.Sha256, 0,
            "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = second");
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_root, "sample.ini")));
        Assert.DoesNotContain(draft.Findings, finding => finding.CheckId == "unknown-trigger");
        Assert.Throws<InvalidOperationException>(() => workspace.WriteDraft(draft.Id, "fixed.ini"));
        Assert.False(File.Exists(Path.Combine(_root, "fixed.ini")));
    }

    [Fact]
    public void EnabledWriteCreatesNewMissionButNeverOverwritesExistingFile()
    {
        var workspace = new MissionWorkspace(_root, allowWrites: true);
        DraftPreview draft = workspace.PrepareNewMission(
            "[Mission]\nnpc_ship_file = missions\\npcships.ini\n[Trigger]\nnickname = entry\nInitState = ACTIVE\nCnd_True = no_params");
        Assert.Throws<IOException>(() => workspace.WriteDraft(draft.Id, "sample.ini"));
        workspace.WriteDraft(draft.Id, "created.ini");
        IniDocument created = IniDocument.Load(Path.Combine(_root, "created.ini"));
        Assert.Equal("entry", created.GetSections("Trigger").Single().Find("nickname")!.GetValue(0));
        Assert.Throws<KeyNotFoundException>(() => workspace.ReadDraft(draft.Id));
    }

    [Fact]
    public void StaleSourceIsRejectedAtPreparationAndBeforeWritingCopy()
    {
        var workspace = new MissionWorkspace(_root, allowWrites: true);
        MissionSummary summary = workspace.Inspect("sample.ini");
        Assert.Throws<InvalidOperationException>(() => workspace.PrepareSectionEdit("sample.ini", "wrong", 0, "[Trigger]\nnickname = x"));
        DraftPreview draft = workspace.PrepareSectionEdit("sample.ini", summary.Sha256, 0, "[Trigger]\nnickname = x\nCnd_True = no_params");
        File.AppendAllText(Path.Combine(_root, "sample.ini"), "\n; externally changed");
        Assert.Throws<InvalidOperationException>(() => workspace.WriteDraft(draft.Id, "stale.ini"));
        Assert.False(File.Exists(Path.Combine(_root, "stale.ini")));
    }

    [Fact]
    public void BinarySectionDraftKeepsBiniAndOriginalFile()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "MissionEditor.slnx")))
            repository = repository.Parent;
        File.Copy(Path.Combine(repository!.FullName, "tests/Fixtures/Missions/bini-sample.ini"), Path.Combine(_root, "binary.ini"));
        var workspace = new MissionWorkspace(_root, true);
        MissionSummary summary = workspace.Inspect("binary.ini");
        IniDocument source = IniDocument.Load(Path.Combine(_root, "binary.ini"));
        string edited = IniDocument.GetEditableText(source.Sections[0]).Replace("Cnd_Timer = 5", "Cnd_Timer = 7");
        DraftPreview draft = workspace.PrepareSectionEdit("binary.ini", summary.Sha256, 0, edited);
        workspace.WriteDraft(draft.Id, "binary-copy.ini");
        Assert.True(IniDocument.Load(Path.Combine(_root, "binary-copy.ini")).IsBini);
        Assert.Equal("5", IniDocument.Load(Path.Combine(_root, "binary.ini")).Sections[0].Find("Cnd_Timer")!.GetValue(0));
    }

    [Fact]
    public void PaginationAndBoundsAreExplicit()
    {
        var workspace = new MissionWorkspace(_root);
        Page<TriggerInfo> page = workspace.ListTriggers("sample.ini", limit: 1);
        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.ReadSource("sample.ini", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.Context("sample.ini", "start", 4));
    }

    [Fact]
    public void ListSectionsGivesTheIndexForSectionEdits()
    {
        File.WriteAllText(Path.Combine(_root, "objects.ini"),
            "[ObjList]\nnickname = follow\nFollow = Player, 100\n\n[Trigger]\nnickname = go\nCnd_True = no_params\n");
        var workspace = new MissionWorkspace(_root);
        SectionInfo objList = Assert.Single(workspace.ListSections("objects.ini", "objlist").Items);
        Assert.Equal((0, "ObjList", "follow", 1), (objList.Index, objList.Name, objList.Nickname, objList.FirstLine));
        Assert.Equal(2, workspace.ListSections("objects.ini").Total);
        DraftPreview draft = workspace.PrepareSectionEdit("objects.ini", workspace.Inspect("objects.ini").Sha256, objList.Index,
            "[ObjList]\nnickname = follow\nFollow = Player, 200");
        Assert.Contains("Follow = Player, 200", draft.After);
    }

    [Fact]
    public void SectionDraftRejectsCharactersUnrepresentableInOriginalEncoding()
    {
        var workspace = new MissionWorkspace(_root, true);
        MissionSummary summary = workspace.Inspect("sample.ini");
        Assert.Throws<InvalidDataException>(() => workspace.PrepareSectionEdit("sample.ini", summary.Sha256, 0,
            "[Trigger]\nnickname = 入口\nCnd_True = no_params"));
    }

    [Fact]
    public void RejectsCompactBiniWithExcessiveDecodedExpansion()
    {
        // 40 entries that all point at one 100 000-character string: ~100 KB on disk, ~4 MB decoded.
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        const int Entries = 40;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("BINI"));
        writer.Write(1);
        writer.Write(12 + 4 + 8 * Entries);
        writer.Write((ushort)0);
        writer.Write((ushort)Entries);
        for (int i = 0; i < Entries; i++)
        {
            writer.Write((ushort)8);
            writer.Write((byte)1);
            writer.Write((byte)3);
            writer.Write(17);
        }
        writer.Write(System.Text.Encoding.ASCII.GetBytes("Trigger\0nickname\0" + new string('x', 100000) + "\0"));
        File.WriteAllBytes(Path.Combine(_root, "expanded.ini"), data.ToArray());
        Assert.Throws<InvalidDataException>(() => new MissionWorkspace(_root).Inspect("expanded.ini"));
    }

    [Fact]
    public async Task LongSimilarNamesUseBoundedDistanceWork()
    {
        string name = new string('x', 100000) + "a";
        string candidate = new string('x', 100000) + "b";
        IReadOnlyList<string> closest = await Task.Run(() => NameSimilarity.FindClosest(name, [candidate]))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(candidate, closest.Single());
    }
}
