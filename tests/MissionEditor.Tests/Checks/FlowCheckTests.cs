using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public class FlowCheckTests
{
  private const string Sample = """
        [MsnShip]
        nickname = Escort_1
        label = escorts

        [MsnShip]
        nickname = Escort_2
        label = escorts

        [MsnShip]
        nickname = Loner
        init_objectives = ol_missing_init

        [MsnShip]
        nickname = Ghost
        label = ghosts

        [MsnFormation]
        nickname = EscortWing
        ship = Escort_1
        ship = Escort_2

        [MsnSolar]
        nickname = Depot

        [ObjList]
        nickname = ol_follow

        [ObjList]
        nickname = ol_unused

        [Dialog]
        nickname = dx_intro

        [NNObjective]
        nickname = obj_go

        [Trigger]
        nickname = start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_SpawnFormation = EscortWing
        Act_SpawnShip = loner, ol_follow
        Act_SpawnSolar = DepotX
        Act_StartDialog = DX_INTRO
        Act_SetNNObj = obj_go
        Act_SetNNObj = obj_gone
        Act_ActTrig = escorts_dead

        [Trigger]
        nickname = escorts_dead
        Cnd_Destroyed = escorts, -1
        Act_ActTrig = wait_for_dead
        Act_ActTrig = finish

        [Trigger]
        nickname = wait_for_dead
        Cnd_WatchTrigger = orphan, COMPLETE
        Act_DebugMsg = never

        [Trigger]
        nickname = finish
        Cnd_Destroyed = ghosts, 1
        Act_ChangeState = SUCCEED

        [Trigger]
        nickname = orphan
        Cnd_True = no_params
        Act_ActTrig = orphan_child
        Act_GiveObjList = Loner, ol_nowhere

        [Trigger]
        nickname = orphan_child
        Cnd_True = no_params
        Act_SpawnShip = Loner, no_ol
        """;

  private static MissionScript Load (string text = Sample) => MissionScript.FromDocument (IniDocument.ParseText (text));

  [Fact]
  public void FlowFollowsActivationsFromTheStartTriggers ()
  {
    MissionFlow flow = MissionFlow.Of (Load ());
    Assert.Equal (["escorts_dead", "finish", "start", "wait_for_dead"], flow.Reachable.Select (trigger => trigger.Nickname).Order (StringComparer.Ordinal));
    Assert.DoesNotContain (flow.References, reference => reference.Name == "no_ol");
    // Formation members count as spawned with the formation.
    Assert.Contains (flow.SpawnedShipGroups (), item => item.Nickname == "Escort_2");
  }

  [Fact]
  public void UnreachableTriggersAreHintsUnlessARunningTriggerWaitsForThem ()
  {
    List<Finding> findings = new UnreachableTriggerCheck ().Run (Load ()).ToList ();
    Finding orphan = Assert.Single (findings, finding => finding.Subject == "orphan");
    Assert.Equal (FindingSeverity.Warning, orphan.Severity);
    Assert.Contains ("'wait_for_dead' wartet", orphan.Message);
    Finding child = Assert.Single (findings, finding => finding.Subject == "orphan_child");
    Assert.Equal (FindingSeverity.Info, child.Severity);
    Assert.Contains ("'orphan'", child.Message);
    Assert.Equal (2, findings.Count);
  }

  [Fact]
  public void MissingStartTriggerIsReportedOnce ()
  {
    Finding finding = Assert.Single (new UnreachableTriggerCheck ().Run (Load ("[Trigger]\nnickname = a\nCnd_True = no_params\n[Trigger]\nnickname = b\nCnd_True = no_params")));
    Assert.Contains ("InitState = ACTIVE", finding.Message);
  }

  [Fact]
  public void UnknownObjectsFollowTheEngineLookup ()
  {
    List<Finding> findings = new UnknownObjectCheck ().Run (Load ()).ToList ();
    // Spawn* compares exactly: "loner" is not "Loner".
    Finding loner = Assert.Single (findings, finding => finding.Message.Contains ("'loner'"));
    Assert.Equal (FindingSeverity.Error, loner.Severity);
    Assert.Contains ("'Loner'", loner.Message);
    Assert.Single (findings, finding => finding.Message.Contains ("'DepotX'") && finding.Message.Contains ("'Depot'"));
    // Dialogs and objectives compare by CreateID, so the case does not matter.
    Assert.DoesNotContain (findings, finding => finding.Message.Contains ("DX_INTRO"));
    Assert.Single (findings, finding => finding.Message.Contains ("'obj_gone'") && finding.Severity == FindingSeverity.Error);
    Assert.Single (findings, finding => finding.Message.Contains ("'ol_missing_init'") && finding.Severity == FindingSeverity.Warning);
    // In a trigger that never runs the reference is only a hint.
    Assert.Single (findings, finding => finding.Message.Contains ("'ol_nowhere'") && finding.Severity == FindingSeverity.Info);
    Assert.Equal (5, findings.Count);
  }

  [Fact]
  public void DestroyedConditionOnNeverSpawnedObjectCannotBeMet ()
  {
    List<Finding> findings = new ImpossibleConditionCheck ().Run (Load ()).ToList ();
    Finding ghost = Assert.Single (findings);
    Assert.Equal ("finish", ghost.Subject);
    Assert.Equal (FindingSeverity.Warning, ghost.Severity);
    Assert.Contains ("'Ghost'", ghost.Message);
  }

  [Fact]
  public void UnusedObjectsAreHints ()
  {
    string[] unused = new UnusedObjectCheck ().Run (Load ()).Select (finding => finding.Subject!).Order ().ToArray ();
    // Loner is spawned only with the wrong case or from a dead trigger with the right one; the latter still counts as used.
    Assert.Equal (["Depot", "Ghost", "ol_unused"], unused);
  }

  [Fact]
  public void MissionEndMustBeReachable ()
  {
    Assert.Empty (new MissionEndCheck ().Run (Load ()));
    string deadEnd = "[Trigger]\nnickname = start\nInitState = ACTIVE\nCnd_True = no_params\n[Trigger]\nnickname = win\nCnd_True = no_params\nAct_ChangeState = SUCCEED";
    Finding finding = Assert.Single (new MissionEndCheck ().Run (Load (deadEnd)));
    Assert.Equal (FindingSeverity.Warning, finding.Severity);
    Assert.Equal (FindingSeverity.Info, Assert.Single (new MissionEndCheck ().Run (Load ("[Trigger]\nnickname = start\nInitState = ACTIVE\nCnd_True = no_params"))).Severity);
  }
}
