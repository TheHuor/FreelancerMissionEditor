using System.Globalization;
using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>Lossless resources from registered DLL slots; no code executes and no persistent native handles remain open.</summary>
public sealed class GameText
{
  private readonly string[] _dllPaths;
  private readonly Dictionary<int, IReadOnlyList<ResourceEntry>> _cache = [];
  private readonly Dictionary<int, IReadOnlyList<ResourceEntry>> _idCache = [];
  private readonly object _gate = new ();
  private int _generation;

  private GameText (string[] paths) => _dllPaths = paths;

  /// <summary>Registered DLL paths in their actual IDS slot order, including resources.dll at slot zero.</summary>
  public IReadOnlyList<string> DllPaths => Array.AsReadOnly (_dllPaths);

  internal static GameText FromFreelancerIni (IniDocument freelancer, string exeFolder)
  {
    var paths = new List<string> { Path.GetFullPath (Path.Combine (exeFolder, "resources.dll")) };
    paths.AddRange (freelancer.Sections.Where (section => section.Is ("Resources"))
        .SelectMany (section => section.FindAll ("DLL"))
        .Select (entry => Path.GetFullPath (Path.Combine (exeFolder, entry.GetValue (0)))));
    return new GameText ([.. paths]);
  }

  /// <summary>Compatibility wrapper returning readable text, or null when unavailable.</summary>
  public string? Get (int ids) => Resolve (ids) is { IsResolved: true } entry ? entry.Text : null;

  /// <summary>Resolve an IDS, reporting actual language, original bytes and a specific failure cause.</summary>
  public ResourceEntry Resolve (int ids, ushort? language = null, ResourceKind? kind = null)
  {
    if (ids <= 0) return ResourceEntry.Missing (ids, ResourceStatus.InvalidId, "IDS muss eine positive Zahl sein.");
    int slot = ids >> 16;
    if (slot >= _dllPaths.Length) return ResourceEntry.Missing (ids, ResourceStatus.SlotMissing, $"DLL-Slot {slot} ist nicht registriert.");
    if (!OperatingSystem.IsWindows ()) return ResourceEntry.Missing (ids, ResourceStatus.DllUnreadable, "Windows-Ressourcen sind auf diesem System nicht verfügbar.");
    if (!File.Exists (_dllPaths[slot])) return ResourceEntry.Missing (ids, ResourceStatus.DllMissing, $"Ressourcen-DLL fehlt: {_dllPaths[slot]}");
    try
    {
      ResourceEntry? found = Variants (ids).Where (entry => (language is null || entry.Identity!.Language == language)
          && (kind is null || entry.Identity!.Kind == kind)).OrderBy (entry => entry.Identity!.Kind)
          .ThenBy (entry => LanguageOrder (entry.Identity!.Language)).FirstOrDefault ();
      return found ?? new ResourceEntry (ids, new (_dllPaths[slot], slot, ids & 0xFFFF, kind ?? ResourceKind.String, language ?? 0),
          ResourceStatus.EntryMissing, "Kein Ressourceneintrag gefunden", string.Empty, null, null);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception or System.Text.DecoderFallbackException)
    {
      return new (ids, new (_dllPaths[slot], slot, ids & 0xFFFF, kind ?? ResourceKind.String, language ?? 0), ResourceStatus.DllUnreadable,
          $"Ressourcen-DLL nicht lesbar: {_dllPaths[slot]} · {exception.Message}", string.Empty, null, null);
    }
  }

  /// <summary>All existing resource type/language variants of one IDS, without scanning other DLLs.</summary>
  public IReadOnlyList<ResourceEntry> Variants (int ids)
  {
    if (ids <= 0 || (ids >> 16) >= _dllPaths.Length) return [];
    int generation;
    lock (_gate)
    {
      if (_idCache.TryGetValue (ids, out var found)) return found;
      if (_cache.TryGetValue (ids >> 16, out var slot)) return slot.Where (entry => entry.Ids == ids).ToArray ();
      generation = _generation;
    }
    string path = _dllPaths[ids >> 16];
    var blobs = NativeResourceFile.Read (path, numericType: 6, numericName: ((ids & 0xFFFF) >> 4) + 1)
        .Concat (NativeResourceFile.Read (path, numericType: 23, numericName: ids & 0xFFFF));
    var entries = Decode (ids >> 16, blobs, default).Where (entry => entry.Ids == ids).ToArray ();
    lock (_gate) { if (_generation == generation) _idCache[ids] = entries; }
    return entries;
  }

  /// <summary>Enumerate actual resource tables in registered DLLs. Missing DLLs don't hide usable DLLs.</summary>
  public IReadOnlyList<ResourceEntry> Enumerate (CancellationToken cancellationToken = default, IProgress<string>? progress = null)
  {
    var entries = new List<ResourceEntry> ();
    for (int slot = 0; slot < _dllPaths.Length; slot++)
    {
      cancellationToken.ThrowIfCancellationRequested ();
      progress?.Report ($"{slot + 1}/{_dllPaths.Length} · {Path.GetFileName (_dllPaths[slot])}");
      try
      {
        entries.AddRange (Slot (slot, cancellationToken));
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception or System.Text.DecoderFallbackException)
      {
        progress?.Report ($"{Path.GetFileName (_dllPaths[slot])}: {exception.Message}");
      }
    }
    return entries.OrderBy (entry => entry.Ids).ThenBy (entry => entry.Identity!.Language).ToArray ();
  }

  /// <summary>Forget snapshots after saving or explicitly reloading. Native handles are already released.</summary>
  public void Reload () { lock (_gate) { _cache.Clear (); _idCache.Clear (); _generation++; } }

  private IReadOnlyList<ResourceEntry> Slot (int slot, CancellationToken token)
  {
    int generation;
    lock (_gate)
    {
      if (_cache.TryGetValue (slot, out var cached)) return cached;
      generation = _generation;
    }
    var entries = Decode (slot, NativeResourceFile.Read (_dllPaths[slot], token), token);
    lock (_gate) { if (_generation == generation) _cache[slot] = entries; }
    return entries;
  }

  private IReadOnlyList<ResourceEntry> Decode (int slot, IEnumerable<NativeResourceFile.Blob> blobs, CancellationToken token)
  {
    var entries = new List<ResourceEntry> ();
    foreach (var blob in blobs)
    {
      token.ThrowIfCancellationRequested ();
      if (!blob.Key.Name.StartsWith ('#') || !int.TryParse (blob.Key.Name.AsSpan (1), out int resourceId)) continue;
      if (blob.Key.Type == "#6")
      {
        if (resourceId is < 1 or > 4096) continue;
        string[] strings = ResourceEncoding.ReadStringBlock (blob.Bytes);
        for (int offset = 0; offset < 16; offset++)
        {
          if (strings[offset].Length == 0) continue;
          int local = (resourceId - 1) * 16 + offset;
          int ids = checked ((slot << 16) | local);
          entries.Add (new (ids, new (_dllPaths[slot], slot, local, ResourceKind.String, blob.Key.Language), ResourceStatus.Resolved,
              "String geladen", strings[offset], strings[offset], (byte[])blob.Bytes.Clone ()));
        }
      }
      else if (blob.Key.Type == "#23" && resourceId is >= 0 and <= 65535)
      {
        string raw = ResourceEncoding.ReadInfocard (blob.Bytes);
        string text;
        try { text = InfocardDocument.Parse (raw).PlainText; }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidDataException) { text = raw; }
        entries.Add (new ((slot << 16) | resourceId, new (_dllPaths[slot], slot, resourceId, ResourceKind.Infocard, blob.Key.Language),
            ResourceStatus.Resolved, "Klartext aus Infocard", text, raw, (byte[])blob.Bytes.Clone ()));
      }
    }
    return entries;
  }

  private static int LanguageOrder (ushort language)
  {
    int preferred = CultureInfo.CurrentUICulture.LCID & 0xFFFF;
    return language == preferred ? -3 : language == 0 ? -2 : (language & 0x3FF) == (preferred & 0x3FF) ? -1 : language;
  }
}
