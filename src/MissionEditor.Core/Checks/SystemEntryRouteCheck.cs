using System.Globalization;
using System.Numerics;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// A trigger with Cnd_SystemEnter = X that only becomes active once the player has come close to one particular
/// jump gate or jump hole (Cnd_DistVec at its position, Cnd_DistShip on its nickname), while the system the
/// player is in has a further connection to X. Taking the other one, the player enters X before the trigger is
/// active; the activation then checks the current system but the approach trigger behind it never fires in X, so the
/// mission hangs. Runs only with game data (universe.ini and the system INIs).
/// </summary>
public sealed class SystemEntryRouteCheck : IMissionCheck
{
  // A waypoint this close to the other connection makes it likely that the player flies through it (in m13sp the
  // gate is 2.6 km from the end of the trade lane, the hole that counts 30 km further).
  private const float NearbyDistance = 10000;

  private const int MaximumSteps = 8;

  private static readonly CultureInfo German = CultureInfo.GetCultureInfo ("de-DE");

  private readonly GameData _gameData;

  /// <summary>Creates the check for the game data of an installation (see <see cref="GameData.Locate"/>).</summary>
  public SystemEntryRouteCheck (GameData gameData) => _gameData = gameData;

  /// <inheritdoc/>
  public string Id => "system-entry-route";

  /// <inheritdoc/>
  public string Title => "Systemwechsel nur über eine Verbindung";

  private sealed record Approach (Trigger Trigger, string System, IReadOnlyList<SystemConnection> Connections);

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    var saveTargets = script.EffectiveTriggers.SelectMany (trigger => trigger.References)
        .Where (reference => reference.Kind == TriggerReferenceKind.Save && reference.Target is not null)
        .Select (reference => reference.Target!)
        .ToHashSet ();
    Trigger[] running = script.EffectiveTriggers.Where (flow.Reachable.Contains).ToArray ();
    // Connections the player cannot take: locked by the mission (Act_LockDock = Player, <object>, lock), or a jump gate
    // whose faction the mission makes hostile to the player (a gate refuses hostile ships). Both are left out at any
    // point of the mission, to keep false alarms low.
    var locked = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
    var hostileFactions = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
    foreach (IniLine action in running.SelectMany (trigger => trigger.Actions))
    {
      if (action.IsKey ("Act_LockDock") && IsPlayer (action.GetValue (0)) && action.GetValue (2).Equals ("lock", StringComparison.OrdinalIgnoreCase))
        locked.Add (action.GetValue (1));
      else if (action.IsKey ("Act_SetRep") && IsPlayer (action.GetValue (0)) && IsHostile (action.GetValue (2)))
        hostileFactions.Add (action.GetValue (1));
    }
    foreach (Trigger entry in running)
    {
      // Active from the start or as the target of a save it does not wait for anything.
      if (entry.Condition is not { } condition || !condition.IsKey ("Cnd_SystemEnter") || entry.InitialState == TriggerState.Active
          || saveTargets.Contains (entry))
        continue;
      string targetSystem = condition.GetValue (0);
      string fromSystem = condition.GetValue (1);
      if (targetSystem.Length == 0 || IsAny (targetSystem))
        continue;
      Trigger[] activators = flow.ActivatorsOf (entry).Where (flow.Reachable.Contains).Where (trigger => trigger != entry).ToArray ();
      if (activators.Length == 0)
        continue;
      // Only when every way to the trigger leads past one particular connection: one other activator (a timer, a
      // landing, an approach somewhere else) and the trigger does not hang on the route.
      var approaches = new List<Approach> ();
      foreach (Trigger activator in activators)
      {
        if (FindApproach (flow, activator, targetSystem) is not { } approach)
        {
          approaches.Clear ();
          break;
        }
        approaches.Add (approach);
      }
      foreach (var group in approaches.GroupBy (approach => approach.System, StringComparer.OrdinalIgnoreCase))
      {
        string system = group.Key;
        if (fromSystem.Length > 0 && !IsAny (fromSystem) && !fromSystem.Equals (system, StringComparison.OrdinalIgnoreCase))
          continue;
        var covered = group.SelectMany (approach => approach.Connections).ToHashSet ();
        SystemConnection[] others = _gameData.Connections (system)
            .Where (connection => connection.TargetSystem.Equals (targetSystem, StringComparison.OrdinalIgnoreCase) && !covered.Contains (connection)
                && !locked.Contains (connection.Nickname) && !(connection.IsGate && hostileFactions.Contains (connection.Reputation)))
            .ToArray ();
        if (others.Length == 0)
          continue;
        yield return Report (flow, entry, condition, group.ToArray (), others);
      }
    }
  }

  private Finding Report (MissionFlow flow, Trigger entry, IniLine condition, Approach[] approaches, SystemConnection[] others)
  {
    Approach first = approaches[0];
    string system = first.System;
    string targetSystem = condition.GetValue (0);
    string approached = string.Join (" bzw. ", approaches.Select (approach =>
        $"{string.Join (" oder ", approach.Connections.Select (connection => $"{KindOf (connection, true)} '{connection.Nickname}'"))} "
        + $"('{approach.Trigger.Nickname}', Zeile {approach.Trigger.Condition!.Number})"));

    // Where to switch the trigger on instead: the earliest trigger that leads to the approach, and the waypoint
    // triggers in between that the entry trigger should then switch off.
    Trigger[] starts = approaches.SelectMany (approach => EarliestActivators (flow, approach.Trigger)).Distinct ().ToArray ();
    var waypoints = new List<Trigger> ();
    foreach (Approach approach in approaches)
    {
      IEnumerable<Trigger> between = starts.Length == 0 ? [approach.Trigger] : starts.SelectMany (start => Between (flow, start, approach.Trigger));
      waypoints.AddRange (between.Where (trigger => trigger != entry && !starts.Contains (trigger)));
    }
    Trigger[] waypointTriggers = waypoints.Distinct ().OrderBy (trigger => trigger.LineNumber).ToArray ();
    var objectivePositions = Objectives (flow, starts.Concat (waypointTriggers), system).ToArray ();

    string otherText = string.Join (" und ", others.Select (other =>
    {
      string text = $"{KindOf (other)} '{other.Nickname}' ({Format (other.Position)}";
      var nearest = objectivePositions.Select (objective => (objective.Name, objective.Target, Distance: Vector3.Distance (objective.Position, other.Position)))
          .Where (objective => objective.Distance <= NearbyDistance)
          .OrderBy (objective => objective.Distance)
          .FirstOrDefault ();
      if (nearest.Name is not null)
        text += $"; nur {(nearest.Distance / 1000).ToString ("0.0", German)} km vom Wegpunkt '{nearest.Name}'{(nearest.Target.Length > 0 ? $" bei '{nearest.Target}'" : string.Empty)} entfernt";
      return text + ")";
    }));

    string fix = starts.Length > 0
        ? $"'Act_ActTrig = {entry.Nickname}' schon in {string.Join (" bzw. ", starts.Select (start => $"'{start.Nickname}' (Zeile {start.LineNumber})"))} eintragen"
        : $"'{entry.Nickname}' mit 'InitState = ACTIVE' starten";
    if (waypointTriggers.Length > 0)
      fix += $" und in '{entry.Nickname}' die {system}-Wegpunkttrigger abschalten ({string.Join (", ", waypointTriggers.Select (trigger => $"'Act_DeactTrig = {trigger.Nickname}'"))})";

    return new Finding (FindingSeverity.Warning, Id, condition.Number, entry.Nickname,
        $"'{entry.Nickname}' (Cnd_SystemEnter = {targetSystem}) wird erst aktiv, wenn sich der Spieler {approached} nähert. "
        + $"Von {system} {(others.Length > 1 ? "führen" : "führt")} aber auch {otherText} nach {targetSystem} – fliegt der Spieler dort hindurch, ist '{entry.Nickname}' noch nicht aktiv "
        + "und die Mission geht nicht weiter.",
        $"Cnd_SystemEnter tritt beim Aktivieren sofort ein, wenn der Spieler schon im System ist – aber nur, wenn ihn jemand aktiviert. Der "
        + $"Annäherungstrigger in {system} löst in {targetSystem} nie aus, der Wegpunkt geht verloren. Abhilfe: {fix}, damit jede Verbindung "
        + $"nach {targetSystem} zählt und die alten Ziele nicht nachträglich wieder auftauchen. Ist die andere Verbindung im Spiel für den Spieler "
        + "gesperrt, kann der Fund bleiben.",
        first.Trigger.Condition!.Number);
  }

  // The connection to the target system that the trigger waits for the player to approach, or null when it waits for
  // something else.
  private Approach? FindApproach (MissionFlow flow, Trigger trigger, string targetSystem)
  {
    if (trigger.Condition is not { } condition || !condition.GetValue (0).Equals ("inside", StringComparison.OrdinalIgnoreCase))
      return null;
    IEnumerable<string> systems = trigger.System is { Length: > 0 } system && _gameData.Contains (GameDataKind.System, system)
        ? [system]
        : _gameData.Nicknames (GameDataKind.System);
    if (condition.IsKey ("Cnd_DistVec"))
    {
      if (!IsPlayer (condition.GetValue (1)) || !GameData.TryReadVector (condition, 2, out Vector3 point) || !GameData.TryReadNumber (condition, 5, out float radius))
        return null;
      return Find (systems, targetSystem, connection => Vector3.Distance (connection.Position, point) <= radius, trigger);
    }
    if (condition.IsKey ("Cnd_DistShip"))
    {
      string first = condition.GetValue (1);
      string second = condition.GetValue (2);
      string objectName = IsPlayer (first) ? second : IsPlayer (second) ? first : string.Empty;
      // A mission ship or solar of that name wins over a universe object (GetLabelObjectId → GetSolarId).
      if (objectName.Length == 0 || flow.ObjectsCalled (objectName).Count > 0)
        return null;
      return Find (systems, targetSystem, connection => connection.Nickname.Equals (objectName, StringComparison.OrdinalIgnoreCase), trigger);
    }
    return null;
  }

  private Approach? Find (IEnumerable<string> systems, string targetSystem, Func<SystemConnection, bool> matches, Trigger trigger)
  {
    foreach (string system in systems)
    {
      SystemConnection[] connections = _gameData.Connections (system)
          .Where (connection => connection.TargetSystem.Equals (targetSystem, StringComparison.OrdinalIgnoreCase) && matches (connection))
          .ToArray ();
      if (connections.Length > 0)
        return new Approach (trigger, connections[0].System, connections);
    }
    return null;
  }

  // The reachable activators of the approach trigger that no other of them leads to: where the section begins.
  private static IEnumerable<Trigger> EarliestActivators (MissionFlow flow, Trigger approach)
  {
    Trigger[] activators = flow.ActivatorsOf (approach).Where (flow.Reachable.Contains).Where (trigger => trigger != approach).ToArray ();
    return activators.Where (candidate => !activators.Any (other => other != candidate && flow.ActivationPath (other, candidate, MaximumSteps) is not null));
  }

  // The triggers on activation chains from one trigger to another, the end included.
  private static IEnumerable<Trigger> Between (MissionFlow flow, Trigger from, Trigger to)
  {
    HashSet<Trigger> forward = Spread (from, trigger => trigger.References
        .Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is not null)
        .Select (reference => reference.Target!));
    HashSet<Trigger> backward = Spread (to, trigger => flow.ActivatorsOf (trigger));
    return forward.Intersect (backward).Append (to).Where (trigger => trigger != from).Distinct ();
  }

  private static HashSet<Trigger> Spread (Trigger start, Func<Trigger, IEnumerable<Trigger>> next)
  {
    var seen = new HashSet<Trigger> ();
    var frontier = new List<Trigger> { start };
    for (int step = 0; step < MaximumSteps && frontier.Count > 0; step++)
      frontier = frontier.SelectMany (next).Where (seen.Add).ToList ();
    return seen;
  }

  // The positions of the waypoints the triggers set in the system: [NNObjective] "type = rep_inst|navmarker, system, ids, ids, x, y, z[, object]".
  private static IEnumerable<(string Name, string Target, Vector3 Position)> Objectives (MissionFlow flow, IEnumerable<Trigger> triggers, string system)
  {
    foreach (IniLine action in triggers.SelectMany (trigger => trigger.Actions).Where (action => action.IsKey ("Act_SetNNObj")))
    {
      string name = action.GetValue (0);
      ScriptObject? objective = flow.Script.Objects.FirstOrDefault (item => item.Kind == ScriptObjectKind.Objective
          && item.Nickname.Equals (name, StringComparison.OrdinalIgnoreCase));
      if (objective?.Section.Find ("type") is not { } type || !type.GetValue (1).Equals (system, StringComparison.OrdinalIgnoreCase)
          || !(type.GetValue (0).Equals ("rep_inst", StringComparison.OrdinalIgnoreCase) || type.GetValue (0).Equals ("navmarker", StringComparison.OrdinalIgnoreCase))
          || !GameData.TryReadVector (type, 4, out Vector3 position))
        continue;
      yield return (objective.Nickname, type.GetValue (7), position);
    }
  }

  private static string KindOf (SystemConnection connection, bool dative = false) => (dative ? "dem " : "das ") + (connection.IsGate ? "Sprungtor" : "Sprungloch");

  private static string Format (Vector3 position) =>
      string.Join (", ", new[] { position.X, position.Y, position.Z }.Select (value => Math.Round (value).ToString (CultureInfo.InvariantCulture)));

  // REP_HOSTILE_THRESHOLD / REP_HOSTILE_MAXIMUM or a number at or below the hostile threshold of -0.6.
  private static bool IsHostile (string value) =>
      value.StartsWith ("REP_HOSTILE", StringComparison.OrdinalIgnoreCase)
      || float.TryParse (value, NumberStyles.Float, CultureInfo.InvariantCulture, out float reputation) && reputation <= -0.6f;

  private static bool IsAny (string value) => value.Equals ("any", StringComparison.OrdinalIgnoreCase);

  private static bool IsPlayer (string name) => name.Equals ("Player", StringComparison.OrdinalIgnoreCase);
}
