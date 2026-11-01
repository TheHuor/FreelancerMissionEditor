using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

// The checks that came out of the m13sp.ini flow review (Missions 20, 24, 26, 28 of the Crossfire campaign).
public class FlowReviewCheckTests
{
  private static MissionScript Load (string text) => MissionScript.FromDocument (IniDocument.ParseText (text));

  [Fact]
  public void TargetsSpawnedBeforeTheCountingTriggerIsActiveAreReported ()
  {
    // Mission 26: spawn the relays, wait for the approach, only then start counting.
    MissionScript script = Load ("""
        [MsnShip]
        nickname = relay_a
        label = relay

        [MsnShip]
        nickname = relay_b
        label = relay

        [MsnShip]
        nickname = relay_c
        label = relay

        [Trigger]
        nickname = spawn_relays
        InitState = ACTIVE
        Cnd_True = no_params
        Act_SpawnShip = relay_a
        Act_SpawnShip = relay_b
        Act_SpawnShip = relay_c
        Act_ActTrig = approach

        [Trigger]
        nickname = approach
        Cnd_DistVec = inside, Player, 0, 0, 0, 1000
        Act_ActTrig = count_relays

        [Trigger]
        nickname = count_relays
        Cnd_Destroyed = relay, 3, ALL
        Act_ChangeState = SUCCEED

        [Trigger]
        nickname = too_many
        InitState = ACTIVE
        Cnd_Destroyed = relay, 4
        Act_ChangeState = FAIL
        """);
    List<Finding> findings = new DestroyedCounterCheck ().Run (script).ToList ();
    Finding race = Assert.Single (findings, finding => finding.Subject == "count_relays");
    Assert.Equal (FindingSeverity.Warning, race.Severity);
    Assert.Contains ("spawn_relays → approach → count_relays", race.Message);
    Finding count = Assert.Single (findings, finding => finding.Subject == "too_many");
    Assert.Contains ("höchstens 3", count.Message);
  }

  [Fact]
  public void CountingStartedTogetherWithTheSpawnOrCloakedTargetsAreFine ()
  {
    MissionScript script = Load ("""
        [MsnShip]
        nickname = wing_1
        label = wing

        [MsnShip]
        nickname = wing_2
        label = wing

        [Trigger]
        nickname = spawn_and_count
        InitState = ACTIVE
        Cnd_True = no_params
        Act_SpawnShip = wing_1
        Act_SpawnShip = wing_2
        Act_ActTrig = count_wing

        [Trigger]
        nickname = count_wing
        Cnd_Destroyed = wing, 2
        Act_ChangeState = SUCCEED

        [Trigger]
        nickname = spawn_cloaked
        InitState = ACTIVE
        Cnd_Timer = 5
        Act_SpawnShip = wing_1
        Act_Cloak = wing, true
        Act_ActTrig = reveal

        [Trigger]
        nickname = reveal
        Cnd_Timer = 10
        Act_Cloak = wing, false
        Act_ActTrig = count_later

        [Trigger]
        nickname = count_later
        Cnd_Destroyed = wing, 2
        Act_DebugMsg = done
        """);
    List<Finding> findings = new DestroyedCounterCheck ().Run (script).ToList ();
    Assert.DoesNotContain (findings, finding => finding.Subject == "count_wing");
    Assert.Equal (FindingSeverity.Info, Assert.Single (findings, finding => finding.Subject == "count_later").Severity);
  }

  [Fact]
  public void ProgressWaitingForAnUnprotectedShipIsReported ()
  {
    // Mission 28: wait until Juni reaches a point; an invulnerability from long before her spawn does not count.
    const string Mission = """
        [MsnShip]
        nickname = juni_ship
        label = juni

        [Trigger]
        nickname = old_part
        InitState = ACTIVE
        Cnd_True = no_params
        Act_Invulnerable = juni, true
        Act_ActTrig = spawn_juni

        [Trigger]
        nickname = spawn_juni
        Cnd_Timer = 6
        Act_SpawnShip = juni_ship
        Act_ActTrig = juni_arrives
        {0}

        [Trigger]
        nickname = juni_arrives
        Cnd_DistVec = inside, juni_ship, 0, 0, 0, 1500
        Act_ChangeState = SUCCEED

        [Trigger]
        nickname = juni_dies
        Cnd_Destroyed = juni
        Act_ChangeState = FAIL

        [Trigger]
        nickname = player_close
        InitState = ACTIVE
        Cnd_DistShip = inside, Player, juni_ship, 500
        Act_DebugMsg = close
        """;
    Finding finding = Assert.Single (new VulnerableEscortCheck ().Run (Load (Mission.Replace ("{0}", string.Empty))));
    Assert.Equal ("juni_arrives", finding.Subject);
    Assert.Empty (new VulnerableEscortCheck ().Run (Load (Mission.Replace ("{0}", "Act_Invulnerable = juni_ship, true"))));
    Assert.Empty (new VulnerableEscortCheck ().Run (Load (Mission.Replace ("{0}", "Act_ActTrig = juni_dies"))));
  }

  [Fact]
  public void SavePointsInSpaceNeedAStartPositionAndLoseImmediatePopups ()
  {
    // Mission 20: saved in space, the target neither places the player nor delays its popup.
    MissionScript script = Load ("""
        [Trigger]
        nickname = reach_point
        InitState = ACTIVE
        Cnd_DistVec = inside, Player, 0, 0, 0, 500
        Act_Save = mission20save, 1

        [Trigger]
        nickname = mission20save
        Cnd_True = no_params
        Act_Popupdialog = 500484, 500485, CLOSE
        Act_ActTrig = next_waypoint

        [Trigger]
        nickname = next_waypoint
        Cnd_True = no_params
        Act_NNPath = 12345, 0, 0, 0, sys

        [Trigger]
        nickname = docked
        InitState = ACTIVE
        Cnd_BaseEnter = li01_01_base
        Act_Save = base_save

        [Trigger]
        nickname = base_save
        Cnd_Timer = 1, 1, RUN_ON_BASE
        Act_DebugMsg = welcome
        """);
    List<Finding> findings = new SavePointCheck ().Run (script).ToList ();
    Assert.Single (findings, finding => finding.Subject == "reach_point" && finding.Message.Contains ("Startposition"));
    Assert.Single (findings, finding => finding.Subject == "mission20save" && finding.Message.Contains ("Act_Popupdialog"));
    // What the target activates is only queued and runs in the game: its waypoint is not lost.
    Assert.DoesNotContain (findings, finding => finding.Subject == "next_waypoint");
    Assert.DoesNotContain (findings, finding => finding.Subject == "docked");
    Assert.Equal (2, findings.Count);
  }

  [Fact]
  public void ASaveAfterACutsceneOnABaseNeedsNoStartPosition ()
  {
    MissionScript script = Load ("""
        [Trigger]
        nickname = docked
        InitState = ACTIVE
        Cnd_BaseEnter = cf01_01_base
        Act_AddRTC = missions\m13\scene.ini
        Act_ActTrig = scene_done

        [Trigger]
        nickname = scene_done
        Cnd_RTCDone = missions\m13\scene.ini
        Act_Save = after_scene

        [Trigger]
        nickname = after_scene
        Cnd_BaseEnter = cf01_01_base
        Act_DebugMsg = loaded
        """);
    Assert.Empty (new SavePointCheck ().Run (script));
  }

  [Fact]
  public void AnImmediateStepBetweenSpawnAndCountingIsNoRace ()
  {
    // Cnd_True fires with the next event, the count starts practically together with the spawn.
    MissionScript script = Load ("""
        [MsnShip]
        nickname = nomad_1
        label = nomad

        [MsnShip]
        nickname = nomad_2
        label = nomad

        [Trigger]
        nickname = spawn_nomads
        InitState = ACTIVE
        Cnd_True = no_params
        Act_SpawnShip = nomad_1
        Act_SpawnShip = nomad_2
        Act_ActTrig = go_on

        [Trigger]
        nickname = go_on
        Cnd_True = no_params
        Act_ActTrig = count_nomads

        [Trigger]
        nickname = count_nomads
        Cnd_Destroyed = nomad, 2, ALL
        Act_ChangeState = SUCCEED
        """);
    Assert.Empty (new DestroyedCounterCheck ().Run (script));
  }

  [Fact]
  public void AShipThatOnlyGetsNewOrdersOnArrivalBlocksNothing ()
  {
    MissionScript script = Load ("""
        [MsnShip]
        nickname = wing_b

        [Trigger]
        nickname = spawn_wing
        InitState = ACTIVE
        Cnd_True = no_params
        Act_SpawnShip = wing_b
        Act_ActTrig = wing_arrives

        [Trigger]
        nickname = wing_arrives
        Cnd_DistVec = inside, wing_b, 0, 0, 0, 1000
        Act_GiveObjList = wing_b, break_formation
        """);
    Assert.Empty (new VulnerableEscortCheck ().Run (script));
  }

  [Fact]
  public void ActivationFollowedByDeactivationOfTheSameTriggerIsReported ()
  {
    MissionScript script = Load ("""
        [Trigger]
        nickname = cancels
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = target
        Act_DeactTrig = target

        [Trigger]
        nickname = restarts
        InitState = ACTIVE
        Cnd_True = no_params
        Act_DeactTrig = target
        Act_ActTrig = target

        [Trigger]
        nickname = target
        Cnd_Timer = 5
        Act_DebugMsg = x
        """);
    Finding finding = Assert.Single (new CancelledActivationCheck ().Run (script));
    Assert.Equal ("cancels", finding.Subject);
  }

  [Fact]
  public void DeactivatingAnObjListOrAMisspeltSpawningTriggerIsExplained ()
  {
    MissionScript script = Load ("""
        [ObjList]
        nickname = follow_trent
        Follow = trent, 100

        [Trigger]
        nickname = stop_things
        InitState = ACTIVE
        Cnd_True = no_params
        Act_DeactTrig = follow_trent
        Act_DeactTrig = respawn_wing1bc

        [Trigger]
        nickname = respawn_wing1c
        repeatable = true
        Cnd_Timer = 30
        Act_SpawnShip = wing
        """);
    List<Finding> findings = new UnknownTriggerTargetCheck ().Run (script).ToList ();
    Finding objList = Assert.Single (findings, finding => finding.Message.Contains ("'follow_trent'"));
    Assert.Contains ("ist eine [ObjList]", objList.Message);
    Assert.Equal (FindingSeverity.Warning, objList.Severity);
    Finding typo = Assert.Single (findings, finding => finding.Message.Contains ("'respawn_wing1bc'"));
    Assert.Equal (FindingSeverity.Error, typo.Severity);
    Assert.Contains ("'respawn_wing1c'", typo.Message);
    Assert.Contains ("bleibt er dadurch aktiv", typo.Message);
  }
}
