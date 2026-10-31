using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public class MissionScriptTests
{
    private const string Sample = """
        [MsnShip]
        nickname = Wing

        [MsnFormation]
        nickname = Wing

        [MsnShip]
        nickname = wing

        [ObjList]
        nickname = follow

        [ObjList]
        nickname = FOLLOW

        [Trigger]
        nickname = start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = second
        Act_ActTrig = secnod
        Act_Save = second, 1234

        [Trigger]
        repeatable = true
        nickname = second
        Cnd_Timer = 5
        Cnd_WatchTrigger = start, COMPLETE
        Act_DeacTrig = start

        [Trigger]
        nickname = Second
        Cnd_True = no_params

        [Trigger]
        nickname = third
        Act_DeactTrig = gone
        Act_DeactTrig = start

        [Trigger]
        system = Li01
        """;

    private static MissionScript Load() => MissionScript.FromDocument(IniDocument.ParseText(Sample));

    [Fact]
    public void ReadsTriggersLikeTheEngine()
    {
        MissionScript script = Load();
        Assert.Equal(4, script.Triggers.Count);
        Trigger start = script.FindTrigger("START")!;
        Assert.Equal(TriggerState.Active, start.InitialState);
        Assert.Equal(3, start.References.Count);

        Trigger second = script.FindTrigger("second")!;
        Assert.Equal("second", second.Nickname);
        Assert.False(second.Repeatable);
        Assert.Equal("Cnd_WatchTrigger", second.Condition!.Key);
        Assert.Contains(second.IgnoredEntries, ignored => ignored.Reason == IgnoreReason.BeforeNickname);
        Assert.Contains(second.IgnoredEntries, ignored => ignored.Reason == IgnoreReason.UnknownKey);

        Assert.True(script.Triggers.Single(trigger => trigger.Nickname == "Second").IsShadowed);
        Assert.Single(script.TriggerSectionsWithoutNickname);
    }

    [Fact]
    public void ReportsUnknownTargetsWithSuggestion()
    {
        List<Finding> findings = new UnknownTriggerTargetCheck().Run(Load()).ToList();
        Finding typo = Assert.Single(findings, finding => finding.Severity == FindingSeverity.Error);
        Assert.Contains("'secnod'", typo.Message);
        Assert.Contains("'second'", typo.Message);
        Finding deactivate = Assert.Single(findings, finding => finding.Severity == FindingSeverity.Warning);
        Assert.Contains("'gone'", deactivate.Message);
    }

    [Fact]
    public void ReportsDuplicateNicknames()
    {
        List<Finding> findings = new DuplicateNicknameCheck().Run(Load()).ToList();
        Assert.Contains(findings, finding => finding.Subject == "Second" && finding.Severity == FindingSeverity.Warning);
        Assert.Contains(findings, finding => finding.Subject == "Wing" && finding.Message.Contains("MsnShip/MsnFormation"));
        Assert.Contains(findings, finding => finding.Subject == "wing" && finding.Severity == FindingSeverity.Info);
        Assert.Contains(findings, finding => finding.Subject == "FOLLOW" && finding.Message.Contains("ObjList"));
    }

    [Fact]
    public void ReportsTriggerStructure()
    {
        List<Finding> findings = new TriggerStructureCheck().Run(Load()).ToList();
        Assert.Contains(findings, finding => finding.Subject == "third" && finding.Severity == FindingSeverity.Error);
        Assert.Contains(findings, finding => finding.Subject == "second" && finding.Message.Contains("2 Bedingungen"));
        Assert.Contains(findings, finding => finding.Subject is null && finding.Message.Contains("ohne nickname"));
        Assert.DoesNotContain(findings, finding => finding.Subject == "Second");
    }

    [Fact]
    public void GraphAndLayoutFollowFlowDirection()
    {
        var graph = new TriggerGraph(Load());
        TriggerNode start = graph.FindNode("start")!;
        Assert.Contains(start.Outgoing, edge => edge.To.Name == "second" && edge.Kind == TriggerReferenceKind.Activate);
        Assert.Contains(start.Outgoing, edge => edge.To.Name == "second" && edge.Kind == TriggerReferenceKind.Watch);
        Assert.True(graph.FindNode("secnod")!.IsMissing);

        GraphLayout layout = GraphLayout.Build(graph.FindNode("second")!, new GraphLayoutOptions());
        LayoutNode startBox = layout.Nodes.Single(node => node.Node!.Name == "start");
        Assert.True(startBox.Layer < layout.Center.Layer);
        Assert.DoesNotContain(layout.Nodes, node => node.Node!.Name == "third");
    }

    private const string LayeredSample = """
        [Trigger]
        nickname = a
        Cnd_True = no_params
        Act_ActTrig = b
        Act_ActTrig = c

        [Trigger]
        nickname = b
        Cnd_True = no_params
        Act_ActTrig = c

        [Trigger]
        nickname = c
        Cnd_True = no_params
        Act_ActTrig = a
        Act_ActTrig = c
        """;

    [Fact]
    public void LayoutRoutesLongEdgesAndBreaksCycles()
    {
        var graph = new TriggerGraph(MissionScript.FromDocument(IniDocument.ParseText(LayeredSample)));
        GraphLayout layout = GraphLayout.Build(graph.FindNode("a")!, new GraphLayoutOptions { DepthBackward = 3, DepthForward = 3 });

        int LayerOf(string name) => layout.Nodes.Single(node => node.Node!.Name == name).Layer;
        Assert.True(LayerOf("a") < LayerOf("b"));
        Assert.True(LayerOf("b") < LayerOf("c"));

        // a -> c and the backward c -> a span two columns and pass b's column through a dummy each.
        Assert.Equal(2, layout.Dummies.Count);
        LayoutEdge longEdge = layout.Edges.Single(edge => edge.From.Node!.Name == "a" && edge.To.Node!.Name == "c");
        Assert.Equal(4, longEdge.Points.Count);

        // c -> a closes the cycle and is drawn backwards; c -> c is a self loop.
        LayoutEdge cycle = layout.Edges.Single(edge => edge.From.Node!.Name == "c" && edge.To.Node!.Name == "a");
        Assert.True(cycle.IsBackward);
        Assert.True(cycle.Points[^1].X < cycle.Points[0].X);
        Assert.Contains(layout.Edges, edge => edge.IsSelfLoop);

        // Forward edges end at the left side of their target.
        foreach (LayoutEdge edge in layout.Edges.Where(edge => !edge.IsBackward && !edge.IsSelfLoop))
            Assert.Equal(edge.To.X, edge.Points[^1].X);

        // No two boxes of a column overlap.
        foreach (IGrouping<int, LayoutNode> column in layout.Nodes.Concat(layout.Dummies).GroupBy(node => node.Layer))
        {
            var sorted = column.OrderBy(node => node.Y).ToList();
            for (int i = 1; i < sorted.Count; i++)
                Assert.True(sorted[i].Y >= sorted[i - 1].Y + sorted[i - 1].Height);
        }
    }
}
