using MissionEditor.Core.Checks;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public sealed class GameDataTests : IDisposable
{
  private readonly string _root = Path.Combine (Path.GetTempPath (), "mission-gamedata-" + Guid.NewGuid ());

  public GameDataTests ()
  {
    Write (@"EXE\freelancer.ini", "[Freelancer]\ndata path = ..\\data\n\n[Data]\nuniverse = universe\\universe.ini\ngroups = InitialWorld.ini ; factions\nships = ships\\shiparch.ini\nsolar = solar\\solararch.ini\nloadouts = ships\\loadouts.ini\nequipment = equipment\\misc_equip.ini\n");
    Write (@"DATA\universe\universe.ini", "[System]\nnickname = Li01\n\n[Base]\nnickname = Li01_01_Base\n");
    Write (@"DATA\InitialWorld.ini", "[Group]\nnickname = li_n_grp\n");
    Write (@"DATA\ships\shiparch.ini", "[Ship]\nnickname = li_elite\n");
    Write (@"DATA\solar\solararch.ini", "[Solar]\nnickname = depot\n");
    Write (@"DATA\ships\loadouts.ini", "[Loadout]\nnickname = li_elite_loadout\n");
    Write (@"DATA\equipment\misc_equip.ini", "[Commodity]\nnickname = commodity_gold\n");
    Write (@"DATA\missions\npcships.ini", "[NPCShipArch]\nnickname = shared_fighter\n");
    Write (@"DATA\missions\m99\npcships.ini", "[NPCShipArch]\nnickname = own_fighter\n");
    Write (@"DATA\missions\m99\m99.ini", """
            [Mission]
            npc_ship_file = missions\m99\npcships.ini

            [NPC]
            nickname = pilot_a
            npc_ship_arch = own_fighter
            affiliation = li_n_grp

            [NPC]
            nickname = pilot_b
            npc_ship_arch = shared_fighter
            affiliation = li_x_grp

            [NPC]
            nickname = pilot_c
            npc_ship_arch = lost_fighter

            [MsnSolar]
            nickname = Station
            archetype = depot
            loadout = li_elite_loadout
            system = LI01
            base = Li01_02_Base

            [MsnLoot]
            nickname = gold
            archetype = commodity_gold

            [Trigger]
            nickname = start
            InitState = ACTIVE
            Cnd_BaseEnter = ANY
            Act_SetShipAndLoadout = li_eltie, li_elite_loadout
            Act_ForceLand = li01_01_base
            """);
  }

  public void Dispose () => Directory.Delete (_root, true);

  [Fact]
  public void GameDataIsFoundFromTheMissionAndReadFromTheDataList ()
  {
    GameData data = Assert.IsType<GameData> (GameData.ForMission (Path.Combine (_root, @"DATA\missions\m99\m99.ini")));
    Assert.True (data.Contains (GameDataKind.System, "li01"));
    Assert.True (data.Contains (GameDataKind.Faction, "LI_N_GRP"));
    Assert.True (data.Contains (GameDataKind.Equipment, "commodity_gold"));
    Assert.Contains ("own_fighter", data.NpcShipArchetypes (@"missions\m99\npcships.ini")!);
    Assert.Null (GameData.ForMission (Path.Combine (Path.GetTempPath (), "nowhere", "x.ini")));
  }

  [Fact]
  public void UnknownGameNicknamesAreErrorsWithSuggestions ()
  {
    MissionScript script = MissionScript.Load (Path.Combine (_root, @"DATA\missions\m99\m99.ini"));
    List<Finding> findings = new GameDataCheck ().Run (script).ToList ();
    Assert.Single (findings, finding => finding.Message.Contains ("'li_x_grp'"));
    Assert.Single (findings, finding => finding.Message.Contains ("'lost_fighter'") && finding.Message.Contains (@"missions\m99\npcships.ini"));
    Assert.Single (findings, finding => finding.Message.Contains ("'Li01_02_Base'") && finding.Message.Contains ("'Li01_01_Base'"));
    Assert.Single (findings, finding => finding.Message.Contains ("'li_eltie'") && finding.Message.Contains ("'li_elite'"));
    // Case does not matter, keywords are not names, and shared NPC ships count.
    Assert.Equal (4, findings.Count);
    Assert.All (findings, finding => Assert.Equal (FindingSeverity.Error, finding.Severity));
  }

  private void Write (string relative, string text)
  {
    string path = Path.Combine (_root, relative);
    Directory.CreateDirectory (Path.GetDirectoryName (path)!);
    File.WriteAllText (path, text.Replace ("\r\n", "\n"));
  }
}
