using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>The state a trigger starts in ("InitState").</summary>
public enum TriggerState
{
  /// <summary>Default; the trigger waits for an Act_ActTrig.</summary>
  Inactive,

  /// <summary>Active from mission start (InitState = ACTIVE).</summary>
  Active,

  /// <summary>InitState = COMPLETE.</summary>
  Complete,
}

/// <summary>Why the engine skips a line of a [Trigger] section.</summary>
public enum IgnoreReason
{
  /// <summary>The line comes before "nickname"; LoadMissionTriggerSection only looks for the nickname there.</summary>
  BeforeNickname,

  /// <summary>A Cnd_/Act_ key the engine does not know (typo).</summary>
  UnknownKey,
}

/// <summary>A line of a [Trigger] section that has no effect in the game.</summary>
/// <param name="Line">The line.</param>
/// <param name="Reason">Why it is skipped.</param>
public sealed record IgnoredEntry (IniLine Line, IgnoreReason Reason);

/// <summary>
/// One [Trigger] section, read the way content.dll reads it (LoadMissionTriggerSection /
/// LoadTrigger): values before "nickname" are ignored, the last Cnd_* wins, Act_* are kept in order.
/// </summary>
public sealed class Trigger
{
  internal Trigger (IniSection section, IniLine nicknameLine)
  {
    Section = section;
    NicknameLine = nicknameLine;
    Nickname = nicknameLine.GetValue (0);
    Id = Hashing.FlHash.CreateId (Nickname);
  }

  /// <summary>The nickname as written in the file.</summary>
  public string Nickname
  {
    get;
  }

  /// <summary>CreateID of the nickname, the id the engine and save games use.</summary>
  public uint Id
  {
    get;
  }

  /// <summary>The [Trigger] section.</summary>
  public IniSection Section
  {
    get;
  }

  /// <summary>The "nickname" line.</summary>
  public IniLine NicknameLine
  {
    get;
  }

  /// <summary>The "system" value (tested but not used by the engine).</summary>
  public string? System
  {
    get; internal set;
  }

  /// <summary>The InitState value.</summary>
  public TriggerState InitialState
  {
    get; internal set;
  }

  /// <summary>"repeatable = true".</summary>
  public bool Repeatable
  {
    get; internal set;
  }

  /// <summary>Every known Cnd_* line after the nickname; only the last one is used.</summary>
  public IReadOnlyList<IniLine> Conditions { get; internal set; } = [];

  /// <summary>The condition that is in effect, or null if the trigger has none.</summary>
  public IniLine? Condition => Conditions.Count > 0 ? Conditions[^1] : null;

  /// <summary>Every known Act_* line after the nickname, in order.</summary>
  public IReadOnlyList<IniLine> Actions { get; internal set; } = [];

  /// <summary>Entry lines the engine skips: keys before the nickname and unknown keys.</summary>
  public IReadOnlyList<IgnoredEntry> IgnoredEntries { get; internal set; } = [];

  /// <summary>True when an earlier trigger has the same id; the engine ignores this definition.</summary>
  public bool IsShadowed
  {
    get; internal set;
  }

  /// <summary>The references to other triggers in this trigger (ActTrig, DeactTrig, WatchTrigger, Save).</summary>
  public IReadOnlyList<TriggerReference> References { get; internal set; } = [];

  /// <summary>First line number of the section.</summary>
  public int LineNumber => Section.FirstLineNumber;

  /// <summary>Short text of the condition, e.g. "Cnd_Timer = 5".</summary>
  public string ConditionText => Condition is null ? "(keine Bedingung)" : $"{Condition.Key} = {Condition.RawValue}";

  /// <inheritdoc/>
  public override string ToString () => Nickname;
}
