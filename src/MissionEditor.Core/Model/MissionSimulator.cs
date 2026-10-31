using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>One thing that happened when a trigger fired.</summary>
/// <param name="Line">The action line.</param>
/// <param name="Text">What happened, for the user (German).</param>
public sealed record SimulationEvent (IniLine Line, string Text);

/// <summary>One step of a simulation: the trigger that fired and what its actions did.</summary>
/// <param name="Trigger">The trigger whose condition was taken as met.</param>
/// <param name="Automatic">True when it fired on its own (Cnd_True).</param>
/// <param name="Events">What the actions did, in order.</param>
public sealed record SimulationStep (Trigger Trigger, bool Automatic, IReadOnlyList<SimulationEvent> Events);

/// <summary>
/// Steps through a mission the way the engine runs its triggers, without the game: the user says which active
/// trigger's condition is met, the simulator applies the actions (Act_ActTrig activates, Act_DeactTrig
/// deactivates, a fired trigger is done unless repeatable – and fires again when re-activated) and reports
/// spawns, objectives, saves and the mission end. Conditions themselves are not evaluated, except that
/// Cnd_True fires at once when automatic firing is on.
/// </summary>
public sealed class MissionSimulator
{
  private readonly MissionScript _script;
  private readonly List<Trigger> _active = [];
  private readonly HashSet<Trigger> _done = [];
  private readonly List<SimulationStep> _steps = [];
  private readonly Stack<(Trigger[] Active, Trigger[] Done, int Steps, string? End)> _history = new ();

  /// <summary>Starts a simulation: the triggers with InitState = ACTIVE are active.</summary>
  public MissionSimulator (MissionScript script, bool fireTrueConditions = true)
  {
    _script = script;
    FireTrueConditions = fireTrueConditions;
    Restart ();
  }

  /// <summary>When true, active triggers with Cnd_True fire on their own, as in the game.</summary>
  public bool FireTrueConditions
  {
    get; set;
  }

  /// <summary>The active triggers in the order they became active.</summary>
  public IReadOnlyList<Trigger> Active => _active;

  /// <summary>The triggers that fired and are done.</summary>
  public IReadOnlySet<Trigger> Done => _done;

  /// <summary>All steps so far.</summary>
  public IReadOnlyList<SimulationStep> Steps => _steps;

  /// <summary>"SUCCEED" or "FAIL" once an Act_ChangeState ended the mission, else null.</summary>
  public string? End
  {
    get; private set;
  }

  /// <summary>True when a step can be undone.</summary>
  public bool CanUndo => _history.Count > 0;

  /// <summary>Back to the start.</summary>
  public void Restart ()
  {
    _active.Clear ();
    _done.Clear ();
    _steps.Clear ();
    _history.Clear ();
    End = null;
    _active.AddRange (_script.EffectiveTriggers.Where (trigger => trigger.InitialState == TriggerState.Active));
    _done.UnionWith (_script.EffectiveTriggers.Where (trigger => trigger.InitialState == TriggerState.Complete));
    RunAutomatic ();
  }

  /// <summary>Takes the condition of an active trigger as met and runs its actions (then any Cnd_True that follows).</summary>
  /// <exception cref="InvalidOperationException">The trigger is not active or the mission has ended.</exception>
  public SimulationStep Fire (Trigger trigger)
  {
    if (End is not null)
      throw new InvalidOperationException ("Die Mission ist beendet.");
    if (!_active.Contains (trigger))
      throw new InvalidOperationException ($"Trigger '{trigger.Nickname}' ist nicht aktiv.");
    _history.Push ((_active.ToArray (), _done.ToArray (), _steps.Count, End));
    SimulationStep step = Execute (trigger, automatic: false);
    RunAutomatic ();
    return step;
  }

  /// <summary>Undoes the last manual step (with the automatic ones after it).</summary>
  public void Undo ()
  {
    if (!_history.TryPop (out var previous))
      return;
    _active.Clear ();
    _active.AddRange (previous.Active);
    _done.Clear ();
    _done.UnionWith (previous.Done);
    _steps.RemoveRange (previous.Steps, _steps.Count - previous.Steps);
    End = previous.End;
  }

  // Cnd_True triggers fire as soon as they are active. A repeatable one would fire every frame in the game;
  // here it fires once per round, and a bound keeps activation loops finite.
  private void RunAutomatic ()
  {
    var firedRepeatable = new HashSet<Trigger> ();
    for (int guard = 0; FireTrueConditions && End is null && guard < 500; guard++)
    {
      Trigger? next = _active.FirstOrDefault (trigger => trigger.Condition?.IsKey ("Cnd_True") == true && !firedRepeatable.Contains (trigger));
      if (next is null)
        return;
      if (next.Repeatable)
        firedRepeatable.Add (next);
      Execute (next, automatic: true);
    }
  }

  private SimulationStep Execute (Trigger trigger, bool automatic)
  {
    if (!trigger.Repeatable)
    {
      _active.Remove (trigger);
      _done.Add (trigger);
    }
    var events = new List<SimulationEvent> ();
    foreach (IniLine action in trigger.Actions)
    {
      if (Describe (action) is { } text)
        events.Add (new SimulationEvent (action, text));
      if (End is not null)
        break;
    }
    var step = new SimulationStep (trigger, automatic, events);
    _steps.Add (step);
    return step;
  }

  private string? Describe (IniLine action)
  {
    string first = action.GetValue (0);
    switch (action.Key!.ToLowerInvariant ())
    {
      case "act_acttrig":
        if (_script.FindTrigger (first) is not { } target)
          return $"Act_ActTrig '{first}' – diesen Trigger gibt es nicht, nichts passiert.";
        bool again = _done.Remove (target);
        if (_active.Contains (target))
          return $"'{target.Nickname}' ist schon aktiv.";
        _active.Add (target);
        return again ? $"'{target.Nickname}' wird erneut aktiv (war schon erledigt – löst noch einmal aus)." : $"'{target.Nickname}' wird aktiv.";
      case "act_deacttrig":
        if (_script.FindTrigger (first) is { } stopped && _active.Remove (stopped))
          return $"'{stopped.Nickname}' wird abgeschaltet.";
        return null;
      case "act_changestate":
        End = first.ToUpperInvariant ();
        return End == "SUCCEED" ? "Mission erfolgreich beendet (SUCCEED)." : $"Mission beendet ({End}).";
      case "act_spawnship":
      case "act_spawnshiprel":
      case "act_spawnformation":
      case "act_spawnsolar":
      case "act_spawnloot":
        return $"Spawnt '{first}'.";
      case "act_setnnobj":
        return $"Ziel '{first}' wird gesetzt.";
      case "act_startdialog":
        return $"Dialog '{first}' startet.";
      case "act_save":
        return $"Speichert; beim Laden geht es mit '{first}' weiter.";
      case "act_destroy":
        return $"Zerstört '{first}'.";
      default:
        return null;
    }
  }
}
