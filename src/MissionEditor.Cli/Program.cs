using System.Text;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Hashing;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Cli;

/// <summary>Command line front end: checks, round trip test, graph export and CreateID.</summary>
internal static class Program
{
    private const string Usage = """
        Freelancer Mission Editor – Kommandozeile

          check <datei> [--min info|warning|error] [--hints]
                                                     Prüfungen ausgeben (Exitcode 1 bei Fehlern),
                                                     --hints mit Erklärung und Abhilfe
          roundtrip <datei>                          Laden + Speichern im Speicher, Bytes vergleichen
          stats <datei>                              Anzahl Trigger, Objekte, Verweise
          graph <datei> <trigger> [--back n] [--forward n] [--deact]
                                                     Umgebung eines Triggers als Graphviz-DOT
          hash <nickname>                            CreateID eines Nicknames
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "check" => Check(args[1], GetOption(args, "--min"), args.Contains("--hints", StringComparer.OrdinalIgnoreCase)),
                "roundtrip" => RoundTrip(args[1]),
                "stats" => Stats(args[1]),
                "graph" when args.Length >= 3 => Graph(args[1], args[2], args),
                "hash" => Hash(args[1]),
                _ => ShowUsage(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Fehler: {exception.Message}");
            return 2;
        }
    }

    private static int ShowUsage()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.FindIndex(args, argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Check(string path, string? minimum, bool showHints)
    {
        FindingSeverity threshold = minimum?.ToLowerInvariant() switch
        {
            "warning" => FindingSeverity.Warning,
            "error" => FindingSeverity.Error,
            _ => FindingSeverity.Info,
        };
        MissionScript script = MissionScript.Load(path);
        IReadOnlyList<Finding> findings = MissionChecker.Run(script);
        foreach (Finding finding in findings.Where(item => item.Severity >= threshold))
        {
            Console.WriteLine($"{path}:{finding.LineNumber}: {SeverityText(finding.Severity)} [{finding.CheckId}] {finding.Message}");
            if (showHints && finding.Hint is not null)
                Console.WriteLine($"    -> {finding.Hint}");
        }
        int errors = findings.Count(item => item.Severity == FindingSeverity.Error);
        int warnings = findings.Count(item => item.Severity == FindingSeverity.Warning);
        int infos = findings.Count(item => item.Severity == FindingSeverity.Info);
        Console.WriteLine($"{errors} Fehler, {warnings} Warnungen, {infos} Hinweise.");
        return errors > 0 ? 1 : 0;
    }

    private static string SeverityText(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Error => "Fehler",
        FindingSeverity.Warning => "Warnung",
        _ => "Hinweis",
    };

    private static int RoundTrip(string path)
    {
        byte[] original = File.ReadAllBytes(path);
        IniDocument document = IniDocument.Parse(original);
        byte[] written = document.ToBytes();
        if (written.AsSpan().SequenceEqual(original))
        {
            Console.WriteLine($"OK: {document.Lines.Count} Zeilen, {document.Sections.Count} Sektionen, {original.Length} Bytes, {document.Encoding.WebName}, identisch.");
            return 0;
        }
        int firstDifference = 0;
        while (firstDifference < Math.Min(written.Length, original.Length) && written[firstDifference] == original[firstDifference])
            firstDifference++;
        Console.WriteLine($"UNTERSCHIED ab Byte {firstDifference} (Original {original.Length} Bytes, geschrieben {written.Length} Bytes).");
        return 1;
    }

    private static int Stats(string path)
    {
        MissionScript script = MissionScript.Load(path);
        var graph = new TriggerGraph(script);
        Console.WriteLine($"Zeilen:    {script.Document.Lines.Count}");
        Console.WriteLine($"Sektionen: {script.Document.Sections.Count}");
        Console.WriteLine($"Trigger:   {script.Triggers.Count} ({script.Triggers.Count(trigger => trigger.IsShadowed)} doppelt, {script.Triggers.Count(trigger => trigger.InitialState == TriggerState.Active)} InitState = ACTIVE)");
        foreach (IGrouping<TriggerReferenceKind, TriggerEdge> group in graph.Edges.GroupBy(edge => edge.Kind).OrderBy(group => group.Key))
            Console.WriteLine($"  {TriggerReference.KeyOf(group.Key),-17} {group.Count()}");
        foreach (IGrouping<ScriptObjectKind, ScriptObject> group in script.Objects.GroupBy(item => item.Kind).OrderBy(group => group.Key))
            Console.WriteLine($"[{ScriptObject.SectionNameOf(group.Key)}] {group.Count()}");
        return 0;
    }

    private static int Graph(string path, string triggerName, string[] args)
    {
        MissionScript script = MissionScript.Load(path);
        var graph = new TriggerGraph(script);
        TriggerNode? center = graph.FindNode(triggerName);
        if (center is null)
        {
            Console.Error.WriteLine($"Trigger '{triggerName}' nicht gefunden.");
            return 1;
        }
        var kinds = new HashSet<TriggerReferenceKind> { TriggerReferenceKind.Activate, TriggerReferenceKind.Watch, TriggerReferenceKind.Save };
        if (args.Contains("--deact", StringComparer.OrdinalIgnoreCase))
            kinds.Add(TriggerReferenceKind.Deactivate);
        var options = new GraphLayoutOptions
        {
            DepthBackward = int.TryParse(GetOption(args, "--back"), out int back) ? back : 2,
            DepthForward = int.TryParse(GetOption(args, "--forward"), out int forward) ? forward : 2,
            Kinds = kinds,
        };
        GraphLayout layout = GraphLayout.Build(center, options);

        var output = new StringBuilder();
        output.AppendLine("digraph triggers {");
        output.AppendLine("  rankdir=LR; node [shape=box, fontname=\"Consolas\", fontsize=10];");
        foreach (LayoutNode node in layout.Nodes)
        {
            string label = node.Node!.Trigger is { } trigger ? $"{trigger.Nickname}\\n{Escape(trigger.ConditionText)}" : $"? {node.Node!.Name}";
            string style = node.Node!.IsMissing ? ", color=red, style=dashed" : node == layout.Center ? ", style=bold" : string.Empty;
            output.AppendLine($"  \"{Escape(node.Node!.Name)}\" [label=\"{label}\"{style}];");
        }
        foreach (LayoutEdge edge in layout.Edges)
        {
            string attributes = edge.Edge.Kind switch
            {
                TriggerReferenceKind.Deactivate => "color=gray, style=dotted",
                TriggerReferenceKind.Watch => "color=blue, style=dashed",
                TriggerReferenceKind.Save => "color=orange, label=\"save\"",
                _ => "color=darkgreen",
            };
            output.AppendLine($"  \"{Escape(edge.From.Node!.Name)}\" -> \"{Escape(edge.To.Node!.Name)}\" [{attributes}];");
        }
        output.AppendLine("}");
        Console.Write(output);
        if (layout.HiddenNodeCount > 0)
            Console.Error.WriteLine($"{layout.HiddenNodeCount} Knoten ausgelassen (Ebene voll).");
        return 0;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static int Hash(string nickname)
    {
        uint id = FlHash.CreateId(nickname);
        Console.WriteLine($"{id} (0x{id:X8})");
        return 0;
    }
}
