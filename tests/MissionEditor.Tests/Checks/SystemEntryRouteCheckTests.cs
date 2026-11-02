using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

// The m13sp case: Cnd_SystemEnter = Bw04 hangs behind the approach to Rh03_to_Bw04_hole, while the gate
// Rh03_to_Bw04 at the end of the trade lane leads to Bw04 as well.
public sealed class SystemEntryRouteCheckTests : IDisposable
{
  private readonly string _root = Path.Combine (Path.GetTempPath (), "mission-systementry-" + Guid.NewGuid ());
  private readonly GameData _gameData;

  public SystemEntryRouteCheckTests ()
  {
    Write (@"EXE\freelancer.ini", "[Freelancer]\ndata path = ..\\data\n\n[Data]\nuniverse = universe\\universe.ini\n");
    Write (@"DATA\universe\universe.ini", """
        [System]
        nickname = Rh03
        file = systems\Rh03\Rh03.ini

        [System]
        nickname = Bw04
        file = systems\Bw04\Bw04.ini

        [System]
        nickname = Bw03
        file = systems\Bw03\Bw03.ini
        """);
    Write (@"DATA\universe\systems\Rh03\Rh03.ini", """
        [Object]
        nickname = Rh03_to_Bw04
        pos = -33739, 0, 89085
        archetype = jumpgate
        reputation = br_n_grp
        goto = Bw04, Bw04_to_Rh03, gate_tunnel_bretonia

        [Object]
        nickname = Rh03_to_Bw03
        pos = -91864, 0, -12508
        archetype = jumpgate
        goto = Bw03, Bw03_to_Rh03, gate_tunnel_bretonia

        [Object]
        nickname = Rh03_to_Bw04_hole
        pos = -34280, 0, 59175
        archetype = jumphole_orange
        goto = Bw04, Bw04_to_Rh03_hole, gate_tunnel_bretonia

        [Object]
        nickname = Rh03_Trade_Lane_Ring_38
        pos = -31571, 0, 87688
        archetype = Trade_Lane_Ring
        """);
    Write (@"DATA\universe\systems\Bw04\Bw04.ini", "[Object]\nnickname = Bw04_to_Rh03\npos = 0, 0, 0\narchetype = jumpgate\ngoto = Rh03, Rh03_to_Bw04, gate_tunnel_bretonia\n");
    Write (@"DATA\universe\systems\Bw03\Bw03.ini", "[Object]\nnickname = Bw03_to_Rh03\npos = 0, 0, 0\narchetype = jumpgate\ngoto = Rh03, Rh03_to_Bw03, gate_tunnel_bretonia\n");
    _gameData = GameData.Load (_root);
  }

  public void Dispose () => Directory.Delete (_root, true);

  // The trade lane to the gate, then the approach to the hole, then the system change.
  private static readonly string Route = """
      [Trigger]
      nickname = start
      InitState = ACTIVE
      Cnd_True = no_params
      Act_ActTrig = lane_end
      Act_ActTrig = near_hole

      [Trigger]
      nickname = lane_end
      system = Rh03
      Cnd_TLExited = Player, Rh03_Trade_Lane_Ring_38
      Act_ActTrig = near_hole
      Act_SetNNObj = obj_hole

      [NNObjective]
      nickname = obj_lane
      state = HIDDEN
      type = rep_inst, Rh03, 1, 1, -31571, 0, 87688, Rh03_Trade_Lane_Ring_38

      [NNObjective]
      nickname = obj_hole
      state = HIDDEN
      type = rep_inst, Rh03, 1, 1, -34280, 0, 59175, Rh03_to_Bw04_hole

      [Trigger]
      nickname = near_hole
      system = Rh03
      Cnd_DistVec = inside, Player, -34280, 0, 59175, 3000
      Act_ActTrig = enter_bw04

      [Trigger]
      nickname = enter_bw04
      system = Bw04
      Cnd_SystemEnter = Bw04
      Act_ChangeState = SUCCEED
      """.ReplaceLineEndings ("\n");

  private List<Finding> Run (string text) =>
      new SystemEntryRouteCheck (_gameData).Run (MissionScript.FromDocument (IniDocument.ParseText (text))).ToList ();

  [Fact]
  public void SystemChangeBehindTheApproachToOneOfTwoConnectionsIsAWarningWithTheFix ()
  {
    string text = Route.Replace ("Act_ActTrig = lane_end", "Act_ActTrig = lane_end\nAct_SetNNObj = obj_lane");
    Finding finding = Assert.Single (Run (text));
    Assert.Equal (FindingSeverity.Warning, finding.Severity);
    Assert.Equal ("system-entry-route", finding.CheckId);
    Assert.Equal ("enter_bw04", finding.Subject);
    Assert.Contains ("dem Sprungloch 'Rh03_to_Bw04_hole' ('near_hole'", finding.Message);
    // The other gate to Bw04 is named with its position and the nearby waypoint, the gate to Bw03 is not.
    Assert.Contains ("das Sprungtor 'Rh03_to_Bw04' (-33739, 0, 89085; nur 2,6 km vom Wegpunkt 'obj_lane' bei 'Rh03_Trade_Lane_Ring_38' entfernt)", finding.Message);
    Assert.DoesNotContain ("Rh03_to_Bw03", finding.Message);
    // Switch the trigger on where the section begins and the waypoint triggers off once in Bw04.
    Assert.Contains ("'Act_ActTrig = enter_bw04' schon in 'start'", finding.Hint);
    Assert.Contains ("in 'enter_bw04' die Rh03-Wegpunkttrigger abschalten ('Act_DeactTrig = lane_end', 'Act_DeactTrig = near_hole')", finding.Hint);
    Assert.Equal (MissionScript.FromDocument (IniDocument.ParseText (text)).Triggers.Single (trigger => trigger.Nickname == "near_hole").Condition!.Number,
        finding.RelatedLineNumber);
  }

  [Fact]
  public void ApproachByNicknameCountsAsWell ()
  {
    Finding finding = Assert.Single (Run (Route.Replace ("Cnd_DistVec = inside, Player, -34280, 0, 59175, 3000", "Cnd_DistShip = inside, Player, Rh03_to_Bw04_hole, 2000")));
    Assert.Contains ("'Rh03_to_Bw04'", finding.Message);
  }

  [Fact]
  public void OtherWaysToTheTriggerOrToTheSystemAreNotReported ()
  {
    // Another activator that does not wait at the hole.
    Assert.Empty (Run (Route.Replace ("Act_SetNNObj = obj_hole", "Act_SetNNObj = obj_hole\nAct_ActTrig = enter_bw04")));
    // Switched on early enough: the fix itself.
    Assert.Empty (Run (Route.Replace ("Act_ActTrig = lane_end\nAct_ActTrig = near_hole", "Act_ActTrig = lane_end\nAct_ActTrig = near_hole\nAct_ActTrig = enter_bw04")));
    // Waiting at a point that is no connection to Bw04 (here the gate to Bw03).
    Assert.Empty (Run (Route.Replace ("Cnd_SystemEnter = Bw04", "Cnd_SystemEnter = Bw03")));
    // A second approach trigger covers the gate.
    Assert.Empty (Run (Route.Replace ("Act_ActTrig = near_hole\nAct_SetNNObj = obj_hole", "Act_ActTrig = near_hole\nAct_ActTrig = near_gate\nAct_SetNNObj = obj_hole")
        + "\n\n[Trigger]\nnickname = near_gate\nsystem = Rh03\nCnd_DistShip = inside, Player, Rh03_to_Bw04, 2000\nAct_ActTrig = enter_bw04\n"));
  }

  [Fact]
  public void ConnectionsThePlayerCannotTakeAreLeftOut ()
  {
    Assert.Empty (Run (Route.Replace ("Cnd_True = no_params", "Cnd_True = no_params\nAct_LockDock = Player, Rh03_to_Bw04, lock")));
    // A gate refuses a player its faction is hostile to.
    Assert.Empty (Run (Route.Replace ("Cnd_True = no_params", "Cnd_True = no_params\nAct_SetRep = Player, br_n_grp, -0.65")));
    Assert.Single (Run (Route.Replace ("Cnd_True = no_params", "Cnd_True = no_params\nAct_SetRep = Player, br_n_grp, REP_FRIEND_THRESHOLD")));
  }

  [Fact]
  public void TheCheckRunsOnlyWithGameData ()
  {
    MissionScript script = MissionScript.FromDocument (IniDocument.ParseText (Route));
    Assert.DoesNotContain (MissionChecker.Run (script, null), finding => finding.CheckId == "system-entry-route");
    Assert.Contains (MissionChecker.Run (script, _gameData), finding => finding.CheckId == "system-entry-route");
  }

  private void Write (string relative, string text)
  {
    string path = Path.Combine (_root, relative);
    Directory.CreateDirectory (Path.GetDirectoryName (path)!);
    File.WriteAllText (path, text.Replace ("\r\n", "\n"));
  }
}
