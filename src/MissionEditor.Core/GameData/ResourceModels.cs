namespace MissionEditor.Core.Model;

/// <summary>The two editable Freelancer text resource types.</summary>
public enum ResourceKind
{
  /// <summary>A UTF-16 string table entry.</summary>
  String,
  /// <summary>A Freelancer RT_HTML RDL infocard.</summary>
  Infocard,
}

/// <summary>Resolution result; missing data is distinct from an unreadable source.</summary>
public enum ResourceStatus
{
  /// <summary>Successfully loaded.</summary>
  Resolved,
  /// <summary>IDS is not positive.</summary>
  InvalidId,
  /// <summary>No active game installation.</summary>
  NoInstallation,
  /// <summary>The requested DLL slot isn't registered.</summary>
  SlotMissing,
  /// <summary>The registered DLL file is missing.</summary>
  DllMissing,
  /// <summary>The DLL or its resource table cannot be read.</summary>
  DllUnreadable,
  /// <summary>The readable DLL has no entry for this ID/type/language.</summary>
  EntryMissing,
  /// <summary>The actual resource exists, but its encoded text is malformed. Original bytes remain available.</summary>
  DecodeError,
}

/// <summary>Identity of an actual language variant in its registered source DLL.</summary>
public sealed record ResourceIdentity (string DllPath, int Slot, int LocalId, ResourceKind Kind, ushort Language);

/// <summary>A resolved resource and its lossless source. Text is only a reading preview for infocards.</summary>
public sealed record ResourceEntry (int Ids, ResourceIdentity? Identity, ResourceStatus Status, string StatusText,
    string Text, string? RawText, byte[]? RawBytes)
{
  /// <summary>Whether the entry exists and was read successfully.</summary>
  public bool IsResolved => Status == ResourceStatus.Resolved;
  /// <summary>An actual resource was read, including one needing text repair.</summary>
  public bool HasRawResource => Identity is not null && RawBytes is not null;
  /// <summary>Existing valid resources and malformed infocards with repair text can be edited.</summary>
  public bool CanEdit => IsResolved || (Status == ResourceStatus.DecodeError && HasRawResource
      && Identity!.Kind == ResourceKind.Infocard && RawText is not null);

  /// <summary>A reference remains navigable even without loaded resources.</summary>
  public static ResourceEntry Missing (int ids, ResourceStatus status = ResourceStatus.NoInstallation,
      string message = "Spielressourcen nicht geladen") => new (ids, null, status, message, string.Empty, null, null);
}

/// <summary>One actual use in the current in-memory mission; indirect uses retain their resolution path.</summary>
public sealed record IdsReference (int Ids, string Role, string Category, string? FilePath, int SectionIndex,
    string SectionName, string SectionNickname, int LineNumber, int ParameterIndex, string SourceText, string? Provenance = null)
{
  /// <summary>The separate source INI defining an indirectly resolved nickname.</summary>
  public string? DefinitionPath { get; init; }
  /// <summary>The definition section's header line.</summary>
  public int? DefinitionLineNumber { get; init; }
}

/// <summary>A game nickname definition, including its source INI and name/infocard IDs.</summary>
public sealed record GameObjectDefinition (GameDataKind Kind, string Nickname, string FilePath, int LineNumber,
    int? NameIds, int? InfoIds, string? ShipArchetype = null);

/// <summary>A verified save's destination and optional backup of the replaced original.</summary>
public sealed record ResourceSaveResult (string Path, string? BackupPath, bool IsCopy);
