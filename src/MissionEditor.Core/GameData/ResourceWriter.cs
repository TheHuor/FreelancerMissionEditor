using System.Security.Cryptography;

namespace MissionEditor.Core.Model;

/// <summary>An immutable, validated change against an actual resource language and complete source-file fingerprint.</summary>
public sealed record ResourceDraft (ResourceEntry Original, string EditedText, string OriginalFileHash)
{
  /// <summary>Validate a draft and capture its source state. Original bytes remain a second direct-save conflict guard, permitting copy recovery from stale selections.</summary>
  public static ResourceDraft Create (ResourceEntry original, string editedText)
  {
    Validate (original, editedText);
    return new (original, editedText, CaptureSourceHash (original));
  }

  /// <summary>Capture the source independently of malformed editor content; original resource bytes are checked on direct save as well.</summary>
  public static string CaptureSourceHash (ResourceEntry original) => original.HasRawResource
      ? ResourceWriter.Fingerprint (original.Identity!.DllPath)
      : throw new InvalidDataException ("Kein vorhandener Ressourceneintrag ausgewählt.");

  /// <summary>Validate content without recapturing a draft's file fingerprint; suitable for editing an existing draft.</summary>
  public static void Validate (ResourceEntry original, string editedText) => ResourceWriter.Validate (original, editedText);
}

/// <summary>Updates only existing resources via a verified full working copy. Caller retains drafts until success.</summary>
public static class ResourceWriter
{
  /// <summary>Write drafts for exactly one DLL, or export a complete modified copy. A copy never changes the active source.</summary>
  public static ResourceSaveResult Save (IReadOnlyList<ResourceDraft> drafts, string? destinationPath = null)
  {
    if (drafts.Count == 0) throw new ArgumentException ("Keine Ressourcenentwürfe ausgewählt.", nameof (drafts));
    foreach (ResourceDraft draft in drafts) Validate (draft.Original, draft.EditedText);
    string source = Path.GetFullPath (drafts[0].Original.Identity!.DllPath);
    if (drafts.Any (draft => !string.Equals (Path.GetFullPath (draft.Original.Identity!.DllPath), source, StringComparison.OrdinalIgnoreCase)))
      throw new ArgumentException ("Ressourcen müssen pro DLL gespeichert werden.", nameof (drafts));
    string target = destinationPath is null ? source : Path.GetFullPath (destinationPath);
    bool copy = !string.Equals (target, source, StringComparison.OrdinalIgnoreCase);
    ReaderWriterLockSlim gate = ResourceFileAccess.Gate (source);
    gate.EnterWriteLock ();
    try { return SaveFile (drafts, source, target, copy); }
    finally { gate.ExitWriteLock (); }
  }

  private static ResourceSaveResult SaveFile (IReadOnlyList<ResourceDraft> drafts, string source, string target, bool copy)
  {
    // Read locks disallow in-place external writes while retaining our ability to atomically replace the source.
    using var sourceLock = new FileStream (source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    string hash = Hash (sourceLock);
    if (!copy && drafts.Any (draft => draft.OriginalFileHash != hash))
      throw new IOException ("Die Ressourcen-DLL wurde extern geändert. Das Original wurde nicht überschrieben. Speichern unter… bleibt möglich.");
    var original = NativeResourceFile.Read (source).ToDictionary (blob => blob.Key);
    var expected = original.ToDictionary (pair => pair.Key, pair => pair.Value.Bytes);
    foreach (var group in drafts.GroupBy (draft => Key (draft.Original.Identity!)))
    {
      if (!original.TryGetValue (group.Key, out var blob))
        throw new IOException ("Der ursprüngliche Ressourceneintrag existiert nicht mehr. Es werden keine neuen IDs angelegt.");
      if (!copy && group.Any (draft => !blob.Bytes.AsSpan ().SequenceEqual (draft.Original.RawBytes)))
        throw new IOException ("Die Ressource wurde seit ihrer Auswahl geändert. Ressourcen neu laden oder als Kopie speichern.");
      if (group.First ().Original.Identity!.Kind == ResourceKind.String)
      {
        string[] strings = ResourceEncoding.ReadStringBlock (blob.Bytes);
        foreach (ResourceDraft draft in group) strings[draft.Original.Identity!.LocalId % 16] = draft.EditedText;
        expected[group.Key] = ResourceEncoding.StringBlock (strings);
      }
      else
      {
        if (group.Count () > 1) throw new ArgumentException ("Mehrere Entwürfe für dieselbe Infocard-Sprachvariante.");
        expected[group.Key] = ResourceEncoding.InfocardBytes (blob.Bytes, group.First ().EditedText);
      }
    }
    string parent = Path.GetDirectoryName (target)!;
    string working = Path.Combine (parent, "." + Path.GetFileName (target) + "." + Guid.NewGuid ().ToString ("N") + ".tmp.dll");
    string? backup = null;
    try
    {
      // Copy the entire file, including code and all resources, without changing the source.
      sourceLock.Position = 0;
      using (var output = new FileStream (working, FileMode.CreateNew, FileAccess.Write, FileShare.None)) sourceLock.CopyTo (output);
      var updates = expected.Where (pair => !pair.Value.AsSpan ().SequenceEqual (original[pair.Key].Bytes))
          .Select (pair => new NativeResourceFile.Blob (pair.Key, pair.Value)).ToArray ();
      NativeResourceFile.Update (working, updates);
      var actual = NativeResourceFile.Read (working).ToDictionary (blob => blob.Key, blob => blob.Bytes);
      if (actual.Count != expected.Count || expected.Any (pair => !actual.TryGetValue (pair.Key, out byte[]? bytes) || !pair.Value.AsSpan ().SequenceEqual (bytes)))
        throw new IOException ("Die Prüfung der Arbeitskopie ist fehlgeschlagen. Das Original bleibt unverändert.");
      if (!copy)
      {
        // Recheck the named path as well as the open handle in case another process replaced it.
        if (Fingerprint (source) != hash || Hash (sourceLock) != hash)
          throw new IOException ("Die Ressourcen-DLL wurde während des Speicherns extern geändert. Entwürfe bleiben erhalten.");
        backup = source + "." + DateTime.UtcNow.ToString ("yyyyMMdd-HHmmssfff") + "." + Guid.NewGuid ().ToString ("N")[..8] + ".bak";
        File.Replace (working, source, backup, ignoreMetadataErrors: false);
      }
      else if (File.Exists (target))
        File.Replace (working, target, null, ignoreMetadataErrors: false);
      else
        File.Move (working, target);
      return new (target, backup, copy);
    }
    finally
    {
      if (File.Exists (working))
      {
        try { File.Delete (working); }
        catch (IOException) { /* Don't obscure the write/verification error. */ }
        catch (UnauthorizedAccessException) { }
      }
    }
  }

  internal static NativeResourceFile.Key Key (ResourceIdentity identity) => new (
      identity.Kind == ResourceKind.String ? "#6" : "#23",
      "#" + (identity.Kind == ResourceKind.String ? identity.LocalId / 16 + 1 : identity.LocalId), identity.Language);

  internal static void Validate (ResourceEntry original, string text)
  {
    if (!original.CanEdit || original.Identity is null || original.RawBytes is null)
      throw new InvalidDataException ("Nur vorhandene, erfolgreich geladene Ressourcen können bearbeitet werden.");
    if (original.Identity.LocalId is < 0 or > 65535)
      throw new InvalidDataException ("Ungültige lokale Ressourcen-ID.");
    if (original.Identity.Kind == ResourceKind.String)
    {
      if (text.Length == 0) throw new InvalidDataException ("Ein leerer String würde den Eintrag entfernen. Das Löschen von Ressourcen wird nicht unterstützt.");
      ResourceEncoding.ValidateString (text);
    }
    else
    {
      if (original.Status == ResourceStatus.DecodeError && text.Contains ('\uFFFD'))
        throw new InvalidDataException ("Ersetzungszeichen � aus fehlerhaften Bytes müssen im RDL/XML-Rohtext ausdrücklich korrigiert werden.");
      InfocardDocument.Validate (text);
    }
  }

  internal static string Fingerprint (string path)
  {
    using var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    return Hash (file);
  }

  private static string Hash (FileStream file)
  {
    file.Position = 0;
    return Convert.ToHexString (SHA256.HashData (file));
  }
}
