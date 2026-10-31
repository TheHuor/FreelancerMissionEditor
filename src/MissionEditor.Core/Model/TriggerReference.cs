using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>How one trigger refers to another.</summary>
public enum TriggerReferenceKind
{
  /// <summary>Act_ActTrig: activates the target.</summary>
  Activate,

  /// <summary>Act_DeactTrig: deactivates the target.</summary>
  Deactivate,

  /// <summary>Cnd_WatchTrigger: the condition waits for the target's state.</summary>
  Watch,

  /// <summary>Act_Save: the save game resumes by activating the target.</summary>
  Save,
}

/// <summary>A reference from a trigger line to another trigger nickname.</summary>
public sealed class TriggerReference
{
  internal TriggerReference (Trigger source, IniLine line, TriggerReferenceKind kind, string targetName, bool isEffective)
  {
    Source = source;
    Line = line;
    Kind = kind;
    TargetName = targetName;
    TargetId = Hashing.FlHash.CreateId (targetName);
    IsEffective = isEffective;
  }

  /// <summary>The trigger that contains the line.</summary>
  public Trigger Source
  {
    get;
  }

  /// <summary>The Act_/Cnd_ line.</summary>
  public IniLine Line
  {
    get;
  }

  /// <summary>What the line does with the target.</summary>
  public TriggerReferenceKind Kind
  {
    get;
  }

  /// <summary>The target nickname as written.</summary>
  public string TargetName
  {
    get;
  }

  /// <summary>CreateID of <see cref="TargetName"/>.</summary>
  public uint TargetId
  {
    get;
  }

  /// <summary>The target trigger, or null when no trigger has that nickname.</summary>
  public Trigger? Target
  {
    get; internal set;
  }

  /// <summary>False for a Cnd_WatchTrigger that a later Cnd_* in the same trigger replaces.</summary>
  public bool IsEffective
  {
    get;
  }

  /// <summary>The key as it would be written, for messages.</summary>
  public static string KeyOf (TriggerReferenceKind kind) => kind switch
  {
    TriggerReferenceKind.Activate => "Act_ActTrig",
    TriggerReferenceKind.Deactivate => "Act_DeactTrig",
    TriggerReferenceKind.Watch => "Cnd_WatchTrigger",
    _ => "Act_Save",
  };

  /// <inheritdoc/>
  public override string ToString () => $"{Source.Nickname} -{Kind}-> {TargetName}";
}
