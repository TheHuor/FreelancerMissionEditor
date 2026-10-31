using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public class MissionSimulatorTests
{
    private const string Sample = """
        [Trigger]
        nickname = start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = wait_launch
        Act_SetNNObj = obj_launch

        [Trigger]
        nickname = wait_launch
        Cnd_LaunchComplete = Player
        Act_SpawnFormation = Pirates
        Act_ActTrig = pirates_dead
        Act_ActTrig = player_dead

        [Trigger]
        nickname = pirates_dead
        Cnd_Destroyed = Pirates, -1
        Act_DeactTrig = player_dead
        Act_ChangeState = SUCCEED

        [Trigger]
        nickname = player_dead
        Cnd_Destroyed = Player
        Act_ChangeState = FAIL
        """;

    private static MissionScript Load() => MissionScript.FromDocument(IniDocument.ParseText(Sample));

    [Fact]
    public void TrueConditionsFireAtOnceAndManualStepsFollowTheActions()
    {
        MissionScript script = Load();
        var simulator = new MissionSimulator(script);
        // start (Cnd_True) has fired by itself and activated wait_launch.
        Assert.Equal(["wait_launch"], simulator.Active.Select(trigger => trigger.Nickname));
        Assert.True(simulator.Steps[0].Automatic);
        Assert.Contains(simulator.Steps[0].Events, item => item.Text.Contains("Ziel 'obj_launch'"));

        SimulationStep launch = simulator.Fire(script.FindTrigger("wait_launch")!);
        Assert.Contains(launch.Events, item => item.Text == "Spawnt 'Pirates'.");
        Assert.Equal(["pirates_dead", "player_dead"], simulator.Active.Select(trigger => trigger.Nickname));

        simulator.Fire(script.FindTrigger("pirates_dead")!);
        Assert.Equal("SUCCEED", simulator.End);
        Assert.Empty(simulator.Active);
        Assert.Throws<InvalidOperationException>(() => simulator.Fire(script.FindTrigger("player_dead")!));
    }

    [Fact]
    public void UndoAndRestartGoBack()
    {
        MissionScript script = Load();
        var simulator = new MissionSimulator(script, fireTrueConditions: false);
        Assert.Equal(["start"], simulator.Active.Select(trigger => trigger.Nickname));
        simulator.Fire(script.FindTrigger("start")!);
        simulator.Fire(script.FindTrigger("wait_launch")!);
        simulator.Undo();
        Assert.Equal(["wait_launch"], simulator.Active.Select(trigger => trigger.Nickname));
        Assert.Single(simulator.Steps);
        simulator.Restart();
        Assert.Equal(["start"], simulator.Active.Select(trigger => trigger.Nickname));
        Assert.False(simulator.CanUndo);
    }

    [Fact]
    public void ReactivatingADoneTriggerLetsItFireAgain()
    {
        MissionScript script = MissionScript.FromDocument(IniDocument.ParseText("""
            [Trigger]
            nickname = a
            InitState = ACTIVE
            Cnd_Timer = 1
            Act_ActTrig = spawn

            [Trigger]
            nickname = b
            InitState = ACTIVE
            Cnd_Timer = 2
            Act_ActTrig = spawn

            [Trigger]
            nickname = spawn
            Cnd_True = no_params
            Act_SpawnShip = Wing
            """));
        var simulator = new MissionSimulator(script);
        simulator.Fire(script.FindTrigger("a")!);
        SimulationStep second = simulator.Fire(script.FindTrigger("b")!);
        Assert.Contains(second.Events, item => item.Text.Contains("erneut aktiv"));
        Assert.Equal(2, simulator.Steps.Count(step => step.Trigger.Nickname == "spawn"));
    }
}
