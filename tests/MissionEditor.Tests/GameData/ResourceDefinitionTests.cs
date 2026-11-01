using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

public sealed class ResourceDefinitionTests : IDisposable
{
  private readonly string _root = Path.Combine (Path.GetTempPath (), "mission-definition-fixture-" + Guid.NewGuid ());
  public void Dispose () => Directory.Delete (_root, true);

  [Fact]
  public void BasesSystemsAndShipTypesResolveFromKnownPositionsWithRealDefinitionPaths ()
  {
    Write (@"EXE\freelancer.ini", "[Data]\nuniverse = universe\\universe.ini\nships = ships\\shiparch.ini\nloadouts = ships\\loadouts.ini\n");
    Write (@"DATA\universe\universe.ini", "[System]\nnickname = Li01\nstrid_name = 101\nfile = li01\\li01.ini\n[Base]\nnickname = Li01_01_Base\nstrid_name = 102\n");
    Write (@"DATA\universe\li01\li01.ini", "[Object]\nnickname = Planet\nbase = Li01_01_Base\nids_name = 103\nids_info = 104\n");
    Write (@"DATA\ships\shiparch.ini", "[Ship]\nnickname = li_elite\nids_name = 105\nids_info = 106\n");
    Write (@"DATA\ships\loadouts.ini", "[Loadout]\nnickname = fighter_loadout\narchetype = li_elite\n");
    Write (@"DATA\missions\npcships.ini", "[NPCShipArch]\nnickname = fighter\nloadout = fighter_loadout\n");
    var document = IniDocument.ParseText ("[NPC]\nnickname = pilot\nindividual_name = 107\nnpc_ship_arch = fighter\n[MsnShip]\nnickname = my_ship\nNPC = pilot\n[Trigger]\nnickname = start\nCnd_BaseEnter = Li01_01_Base\nCnd_SystemEnter = Li01\nAct_SpawnShip = my_ship\nAct_SetTitle = 108\nAct_DebugMsg = li_elite\n");
    var references = MissionResourceIndex.Build (document, "mission.ini", GameData.Load (_root));
    var baseUses = references.Where (reference => reference.LineNumber == 10).ToArray ();
    Assert.Equal (new[] { 103, 104 }, baseUses.Select (reference => reference.Ids));
    Assert.All (baseUses, reference => Assert.EndsWith (@"universe\li01\li01.ini", reference.DefinitionPath));
    Assert.Contains (references, reference => reference.LineNumber == 11 && reference.Ids == 101);
    var shipUses = references.Where (reference => reference.LineNumber == 12).ToArray ();
    Assert.Contains (shipUses, reference => reference.Ids == 107 && reference.Role == "Objektname");
    Assert.Contains (shipUses, reference => reference.Ids == 105 && reference.Role.StartsWith ("Schiffstyp"));
    Assert.DoesNotContain (references, reference => reference.LineNumber == 14);
    Assert.Null (Assert.Single (references, reference => reference.LineNumber == 13).Provenance);
    Assert.Contains (references, reference => reference.LineNumber == 7 && reference.Ids == 107);
  }

  [Fact]
  public void TriggerSystemNicknameIsAnExplicitContextTarget ()
  {
    Write (@"EXE\freelancer.ini", "[Data]\nuniverse = universe\\universe.ini\n");
    Write (@"DATA\universe\universe.ini", "[System]\nnickname = CF106\nstrid_name = 101\n");
    var document = IniDocument.ParseText ("[Trigger]\nnickname = start\nsystem = CF106\n");
    var reference = Assert.Single (MissionResourceIndex.Build (document, "mission.ini", GameData.Load (_root)));
    Assert.Equal (101, reference.Ids);
    Assert.Equal (0, reference.ParameterIndex);
  }

  [Fact]
  public void MissionObjectNamesResolveThroughNpcWithoutAGameInstallation ()
  {
    Directory.CreateDirectory (_root);
    var document = IniDocument.ParseText ("[NPC]\nnickname = pilot\nindividual_name = 207\n[MsnShip]\nnickname = my_ship\nNPC = pilot\n[Trigger]\nAct_SpawnShip = my_ship\n");
    var references = MissionResourceIndex.Build (document, null, null);
    Assert.Contains (references, reference => reference.LineNumber == 8 && reference.Ids == 207 && reference.Provenance != null);
  }

  private void Write (string relative, string text)
  {
    string path = Path.Combine (_root, relative);
    Directory.CreateDirectory (Path.GetDirectoryName (path)!);
    File.WriteAllText (path, text);
  }
}
