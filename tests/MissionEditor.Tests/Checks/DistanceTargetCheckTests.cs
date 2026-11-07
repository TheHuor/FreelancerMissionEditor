using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Tests;

// Objects of Cnd_DistShip / Cnd_DistCircle: mission objects by nickname, universe objects of the system INIs, no zones,
// no labels, no [NPC] names.
public sealed class DistanceTargetCheckTests : IDisposable
{
  private readonly string _root = Path.Combine (Path.GetTempPath (), "mission-distancetarget-" + Guid.NewGuid ());
  private readonly GameData _gameData;

  public DistanceTargetCheckTests ()
  {
    Write (@"EXE\freelancer.ini", "[Freelancer]\ndata path = ..\\data\n\n[Data]\nuniverse = universe\\universe.ini\n");
    Write (@"DATA\universe\universe.ini", """
        [System]
        nickname = CF20
        file = systems\CF20\CF20.ini

        [System]
        nickname = CF21
        file = systems\CF21\CF21.ini
        """);
    // CF20 exists only as its single-player variant, as in a Crossfire build folder.
    Write (@"DATA\universe\systems\CF20\CF20sp.ini", """
        [Object]
        nickname = cf20__hazard_1
        pos = 0, 0, 0

        [Zone]
        nickname = Zone_CF20_nebula
        pos = 1000, 0, 0
        """);
    Write (@"DATA\universe\systems\CF21\CF21.ini", "[Object]\nnickname = CF21_planet_1\npos = 0, 0, 0\n");
    _gameData = GameData.Load (_root);
  }

  public void Dispose () => Directory.Delete (_root, true);

  private static readonly string Objects = """
      [NPC]
      nickname = juni_npc

      [MsnShip]
      nickname = juni
      NPC = juni_npc
      label = escort

      [MsnFormation]
      nickname = wing
      ship = juni

      [MsnSolar]
      nickname = Beacon

      [MsnLoot]
      nickname = crate

      """;

  private List<Finding> Run (string triggers) => new DistanceTargetCheck (_gameData)
      .Run (MissionScript.FromDocument (IniDocument.ParseText ((Objects + triggers).ReplaceLineEndings ("\n")))).ToList ();

  private static string Trigger (string condition, string system = "CF20") => $"""
      [Trigger]
      nickname = watch
      system = {system}
      InitState = ACTIVE
      {condition}
      Act_ChangeState = SUCCEED
      """;

  [Theory]
  [InlineData ("Cnd_DistShip = inside, Player, cf20__hazard_1, 50000")]
  [InlineData ("Cnd_DistShip = inside, juni, JUNI, 500")]
  [InlineData ("Cnd_DistShip = inside, wing, Beacon, 500")]
  [InlineData ("Cnd_DistShip = outside, crate, Player, 500")]
  [InlineData ("Cnd_DistCircle = Player, cf20__hazard_1")]
  [InlineData ("Cnd_DistCircle = wing, cf20__hazard_1")]
  public void ResolvableObjectsAreFine (string condition) => Assert.Empty (Run (Trigger (condition)));

  [Fact]
  public void AZoneIsAnErrorWithCndDistVecAsTheFix ()
  {
    Finding finding = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, Zone_CF20_nebula, 5000")));
    Assert.Equal (FindingSeverity.Error, finding.Severity);
    Assert.Equal ("distance-target", finding.CheckId);
    Assert.Contains ("ist eine Zone in CF20", finding.Message);
    Assert.Contains ("Cnd_DistVec", finding.Hint);
  }

  [Fact]
  public void AnUnknownNameIsAnErrorWithSuggestions ()
  {
    Finding finding = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, jnui, 500")));
    Assert.Equal (FindingSeverity.Error, finding.Severity);
    Assert.Contains ("Gemeint vielleicht 'juni'?", finding.Message);
  }

  [Fact]
  public void AnObjectOfAnotherSystemIsAWarning ()
  {
    Finding finding = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, CF21_planet_1, 500")));
    Assert.Equal (FindingSeverity.Warning, finding.Severity);
    Assert.Contains ("liegt in CF21, der Trigger gehört zu CF20", finding.Message);
  }

  [Fact]
  public void LabelsAndNpcNamesAreNotFound ()
  {
    Finding label = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, escort, 500")));
    Assert.Equal (FindingSeverity.Warning, label.Severity);
    Assert.Contains ("ist nur ein label", label.Message);
    Finding npc = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, juni_npc, 500")));
    Assert.Equal (FindingSeverity.Error, npc.Severity);
    Assert.Contains ("ist ein [NPC]", npc.Message);
    Assert.Contains ("'juni'", npc.Message);
  }

  [Fact]
  public void LootAndSolarsNeedTheExactSpelling ()
  {
    Finding finding = Assert.Single (Run (Trigger ("Cnd_DistShip = inside, Player, beacon, 500")));
    Assert.Equal (FindingSeverity.Error, finding.Severity);
    Assert.Contains ("gibt es nur als 'Beacon'", finding.Message);
  }

  [Fact]
  public void CndDistCircleMeasuresOnlyShips ()
  {
    Finding finding = Assert.Single (Run (Trigger ("Cnd_DistCircle = Beacon, cf20__hazard_1")));
    Assert.Equal (FindingSeverity.Error, finding.Severity);
    Assert.Contains ("kein Schiff", finding.Message);
  }

  [Fact]
  public void ADeadTriggerOnlyGetsANote ()
  {
    string text = """
        [Trigger]
        nickname = never
        system = CF20
        Cnd_DistShip = inside, Player, Zone_CF20_nebula, 5000
        Act_ChangeState = SUCCEED
        """;
    Assert.Equal (FindingSeverity.Info, Assert.Single (Run (text)).Severity);
  }

  private void Write (string relative, string text)
  {
    string path = Path.Combine (_root, relative);
    Directory.CreateDirectory (Path.GetDirectoryName (path)!);
    File.WriteAllText (path, text.Replace ("\r\n", "\n"));
  }
}
