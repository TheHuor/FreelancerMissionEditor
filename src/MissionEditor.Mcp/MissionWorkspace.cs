using System.Security.Cryptography;
using System.Text;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Mcp;

/// <summary>A bounded workspace over saved mission files; proposed edits never replace source files.</summary>
public sealed class MissionWorkspace
{
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private readonly string _root;
    private readonly bool _allowWrites;
    private readonly object _draftGate = new();
    private readonly Dictionary<string, StoredDraft> _drafts = [];

    /// <summary>Creates a workspace; exporting drafts requires explicit startup opt-in.</summary>
    public MissionWorkspace(string root, bool allowWrites = false)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("Das Missionsverzeichnis existiert nicht.");
        _allowWrites = allowWrites;
    }

    /// <summary>Returns the saved document's format, revision and counts.</summary>
    public MissionSummary Inspect(string path)
    {
        var loaded = Load(path);
        var findings = MissionChecker.Run(loaded.Script);
        return new(Relative(loaded.Path), loaded.Script.Document.IsBini ? "BINI" : "Text", loaded.Hash,
            loaded.Script.Document.Lines.Count, loaded.Script.Document.Sections.Count,
            loaded.Script.Triggers.Count, loaded.Script.Objects.Count,
            findings.Count(finding => finding.Severity == FindingSeverity.Error), findings.Count(finding => finding.Severity == FindingSeverity.Warning));
    }

    /// <summary>Pages the existing checker's findings, including explanations.</summary>
    public Page<FindingInfo> Check(string path, string minimumSeverity = "Info", int offset = 0, int limit = 100)
    {
        if (!Enum.TryParse<FindingSeverity>(minimumSeverity, true, out var severity) || !Enum.IsDefined(severity))
            throw new ArgumentException("Schwere muss Info, Warning oder Error sein.");
        return Paginate(MissionChecker.Run(Load(path).Script).Where(finding => finding.Severity >= severity).Select(FindingInfo.From).ToArray(), offset, limit);
    }

    /// <summary>Pages named trigger definitions, including shadowed duplicates.</summary>
    public Page<TriggerInfo> ListTriggers(string path, string filter = "", int offset = 0, int limit = 100) =>
        Paginate(Load(path).Script.Triggers.Where(trigger => trigger.Nickname.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(trigger => new TriggerInfo(trigger.Nickname, trigger.Section.Index, trigger.LineNumber, trigger.InitialState.ToString(), trigger.ConditionText, trigger.IsShadowed)).ToArray(), offset, limit);

    /// <summary>Pages all sections with index, name, nickname and line range (the index prepare_section_edit expects).</summary>
    public Page<SectionInfo> ListSections(string path, string filter = "", int offset = 0, int limit = 100) =>
        Paginate(Load(path).Script.Document.Sections
            .Select(section => new SectionInfo(section.Index, section.Name, section.Find("nickname")?.GetValue(0), section.FirstLineNumber, section.LastLineNumber))
            .Where(section => filter.Length == 0
                || section.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (section.Nickname?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray(), offset, limit);

    /// <summary>Reads rendered source lines; BINI line numbers refer to its decoded view.</summary>
    public SourcePage ReadSource(string path, int firstLine = 1, int lineCount = 120)
    {
        var loaded = Load(path);
        return Source(loaded.Script.Document, firstLine, lineCount);
    }

    /// <summary>Returns a bounded graph neighborhood with conditions, actions and references.</summary>
    public TriggerContext Context(string path, string trigger, int depth = 1)
    {
        if (depth < 0 || depth > 3) throw new ArgumentOutOfRangeException(nameof(depth), "Tiefe muss zwischen 0 und 3 liegen.");
        var graph = new TriggerGraph(Load(path).Script);
        var start = graph.FindNode(trigger) ?? throw new KeyNotFoundException("Triggername ist weder definiert noch referenziert.");
        var nodes = new HashSet<TriggerNode> { start };
        var queue = new Queue<(TriggerNode Node, int Depth)>();
        queue.Enqueue((start, 0));
        bool truncated = false;
        while (queue.TryDequeue(out var item))
        {
            if (item.Depth == depth) continue;
            foreach (var edge in item.Node.Incoming.Concat(item.Node.Outgoing))
            {
                var other = edge.From == item.Node ? edge.To : edge.From;
                if (nodes.Contains(other)) continue;
                if (nodes.Count >= 50) { truncated = true; continue; }
                nodes.Add(other); queue.Enqueue((other, item.Depth + 1));
            }
        }
        var contextEdges = graph.Edges.Where(edge => nodes.Contains(edge.From) && nodes.Contains(edge.To)).ToArray();
        return new(nodes.Select(node => new ContextNode(node.Name, node.IsMissing, node.Trigger?.LineNumber,
            node.Trigger?.ConditionText, node.Trigger?.Actions.Take(100).Select(action => action.Text).ToArray() ?? [],
            (node.Trigger?.Actions.Count ?? 0) > 100)).ToArray(),
            contextEdges.Take(500)
                .Select(edge => new ReferenceInfo(edge.From.Name, edge.To.Name, edge.Kind.ToString(), edge.Reference.Line.Number, edge.Reference.IsEffective)).ToArray(), truncated, contextEdges.Length > 500);
    }

    /// <summary>Provides lexical candidates and actual calling references, without asserting intent.</summary>
    public MissingSuggestion SuggestMissing(string path, string name)
    {
        var script = Load(path).Script;
        if (script.EffectiveTriggers.Any(trigger => trigger.Nickname.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Dieser Trigger ist bereits definiert.");
        var references = script.Triggers.SelectMany(trigger => trigger.References).Where(reference => reference.TargetName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (references.Length == 0) throw new KeyNotFoundException("Dieser fehlende Trigger wird nicht referenziert.");
        return new(name, NameSimilarity.FindClosest(name, script.EffectiveTriggers.Select(trigger => trigger.Nickname)),
            references.Take(100).Select(reference => new ReferenceInfo(reference.Source.Nickname, reference.TargetName, reference.Kind.ToString(), reference.Line.Number, reference.IsEffective)).ToArray(),
            references.Length,
            "Nur Namensähnlichkeit, kein Nachweis der beabsichtigten Funktion. Aufrufkontext und Ablauf prüfen; keine Trigger erfinden.");
    }

    /// <summary>Stages a section replacement against an exact saved revision.</summary>
    public DraftPreview PrepareSectionEdit(string path, string expectedSha256, int sectionIndex, string replacementText)
    {
        var loaded = Load(path);
        if (!loaded.Hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Die Datei wurde verändert. Mission erneut lesen.");
        if (sectionIndex < 0 || sectionIndex >= loaded.Script.Document.Sections.Count) throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        LimitText(replacementText);
        var section = loaded.Script.Document.Sections[sectionIndex];
        var edited = loaded.Script.Document.ReplaceSection(section, replacementText);
        return Stage(edited, loaded.Path, loaded.Hash, IniDocument.GetEditableText(section), replacementText);
    }

    /// <summary>Stages agent-supplied text as a new mission; the server does not generate story content.</summary>
    public DraftPreview PrepareNewMission(string text)
    {
        LimitText(text);
        var strict = Encoding.GetEncoding(IniDocument.Windows1252.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var document = IniDocument.Parse(strict.GetBytes(text));
        if (document.Sections.Count == 0 || document.LeadingLines.Any(line => line.Kind == IniLineKind.Entry))
            throw new InvalidDataException("Eine neue Mission braucht Sektionen; Werte vor der ersten Sektion sind nicht zulässig.");
        return Stage(document, null, null, "", text);
    }

    /// <summary>Reads a staged document for review without changing disk contents.</summary>
    public SourcePage ReadDraft(string id, int firstLine = 1, int lineCount = 120)
    {
        lock (_draftGate) return Source(GetDraft(id).Document, firstLine, lineCount);
    }

    /// <summary>Exports a reviewed draft to a new path only; original and existing files are never replaced.</summary>
    public MissionSummary WriteDraft(string id, string outputPath)
    {
        if (!_allowWrites) throw new InvalidOperationException("Schreiben ist deaktiviert. Server mit --allow-writes starten, um Entwürfe als neue Dateien zu exportieren.");
        lock (_draftGate)
        {
            var draft = GetDraft(id);
            string destination = Resolve(outputPath);
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Die Zieldatei existiert bereits; Überschreiben ist nicht erlaubt.");
            if (draft.Source is not null && Load(draft.Source).Hash != draft.SourceHash)
                throw new InvalidOperationException("Die Quelldatei wurde seit dem Entwurf verändert. Entwurf neu erstellen.");
            byte[] bytes = draft.Document.ToBytes();
            string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".mission-mcp-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(true); }
                // No overwrite: a concurrently created destination causes an error too.
                File.Move(temporary, destination, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _drafts.Remove(id);
            return Inspect(destination);
        }
    }

    private DraftPreview Stage(IniDocument document, string? source, string? hash, string before, string after)
    {
        byte[] bytes = document.ToBytes();
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Entwurf ist größer als 8 MiB.");
        var findings = MissionChecker.Run(MissionScript.FromDocument(document));
        string id = Guid.NewGuid().ToString("N");
        lock (_draftGate)
        {
            if (_drafts.Count >= 8) throw new InvalidOperationException("Maximal acht offene Entwürfe pro Sitzung; nicht benötigte Entwürfe verwerfen.");
            _drafts.Add(id, new(document, source, hash));
        }
        return new(id, document.IsBini ? "BINI" : "Text", source is null ? null : Relative(source), hash,
            before[..Math.Min(before.Length, 4000)], after[..Math.Min(after.Length, 4000)], before.Length > 4000 || after.Length > 4000,
            findings.Take(100).Select(FindingInfo.From).ToArray(), findings.Count, _allowWrites);
    }

    /// <summary>Releases a staged draft without exporting it.</summary>
    public bool DiscardDraft(string id) { lock (_draftGate) return _drafts.Remove(id); }

    private StoredDraft GetDraft(string id) => _drafts.TryGetValue(id, out var draft) ? draft : throw new KeyNotFoundException("Entwurf ist unbekannt, bereits exportiert oder nach Serverneustart nicht mehr verfügbar.");

    private (string Path, string Hash, MissionScript Script) Load(string path)
    {
        string resolved = Resolve(path);
        using var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("Mission ist größer als 8 MiB.");
        using var data = new MemoryStream();
        stream.CopyTo(data);
        byte[] bytes = data.ToArray();
        return (resolved, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), MissionScript.FromDocument(IniDocument.Parse(bytes), resolved));
    }

    private string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string candidate = Path.GetFullPath(path, _root);
        string relative = Path.GetRelativePath(_root, candidate);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("Pfad liegt außerhalb des freigegebenen Missionsverzeichnisses.");
        if (!Path.GetExtension(candidate).Equals(".ini", StringComparison.OrdinalIgnoreCase) || candidate.IndexOf(':', Path.GetPathRoot(candidate)!.Length) >= 0)
            throw new ArgumentException("Nur .ini-Dateien sind erlaubt.");
        // Reject junctions/symlinks instead of allowing a path inside the root to escape through them.
        string? current = candidate;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Symbolische Links und Verzeichnisverknüpfungen werden nicht unterstützt.");
            if (current == _root) break;
            current = Path.GetDirectoryName(current);
        }
        return candidate;
    }

    private string Relative(string path) => Path.GetRelativePath(_root, path);
    private static void LimitText(string text) { ArgumentNullException.ThrowIfNull(text); if (text.Length > MaxFileBytes) throw new InvalidDataException("Entwurfstext ist zu groß."); }
    private static SourcePage Source(IniDocument document, int firstLine, int lineCount)
    {
        if (firstLine < 1 || lineCount < 1 || lineCount > 500) throw new ArgumentOutOfRangeException(nameof(firstLine), "Erste Zeile mindestens 1; Zeilenanzahl zwischen 1 und 500.");
        return new(document.Lines.Count, document.IsBini, document.Lines.Skip(firstLine - 1).Take(lineCount).Select(line => new SourceLine(line.Number, line.Text)).ToArray());
    }
    private static Page<T> Paginate<T>(IReadOnlyList<T> items, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(limit), "Offset mindestens 0; Limit zwischen 1 und 200.");
        return new(items.Count, offset, items.Skip(offset).Take(limit).ToArray());
    }
    private sealed record StoredDraft(IniDocument Document, string? Source, string? SourceHash);
}

public sealed record MissionSummary(string Path, string Format, string Sha256, int LineCount, int SectionCount, int TriggerCount, int ObjectCount, int Errors, int Warnings);
public sealed record Page<T>(int Total, int Offset, IReadOnlyList<T> Items);
public sealed record SourcePage(int TotalLines, bool SyntheticBiniLines, IReadOnlyList<SourceLine> Lines);
public sealed record SourceLine(int Number, string Text);
public sealed record SectionInfo(int Index, string Name, string? Nickname, int FirstLine, int LastLine);
public sealed record TriggerInfo(string Name, int SectionIndex, int LineNumber, string InitialState, string Condition, bool Shadowed);
public sealed record FindingInfo(string Severity, string CheckId, int LineNumber, string? Subject, string Message, string? Hint, int? RelatedLineNumber)
{
    internal static FindingInfo From(Finding finding) => new(finding.Severity.ToString(), finding.CheckId, finding.LineNumber, finding.Subject, finding.Message, finding.Hint, finding.RelatedLineNumber);
}
public sealed record ContextNode(string Name, bool Missing, int? LineNumber, string? Condition, IReadOnlyList<string> Actions, bool ActionsTruncated);
public sealed record ReferenceInfo(string Source, string Target, string Kind, int LineNumber, bool Effective);
public sealed record TriggerContext(IReadOnlyList<ContextNode> Nodes, IReadOnlyList<ReferenceInfo> References, bool Truncated, bool ReferencesTruncated);
public sealed record MissingSuggestion(string Name, IReadOnlyList<string> Candidates, IReadOnlyList<ReferenceInfo> References, int TotalReferences, string Caveat);
public sealed record DraftPreview(string Id, string Format, string? Source, string? SourceSha256, string Before, string After, bool PreviewTruncated, IReadOnlyList<FindingInfo> Findings, int TotalFindings, bool ExportEnabled);
