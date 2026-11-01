using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MissionEditor.Core.Model;

// Scoped data/image handles: DLL code never executes and handles never survive a read.
internal static class NativeResourceFile
{
  internal sealed record Key (string Type, string Name, ushort Language);
  internal sealed record Blob (Key Key, byte[] Bytes);
  private delegate bool TypeCallback (IntPtr module, IntPtr type, IntPtr state);
  private delegate bool NameCallback (IntPtr module, IntPtr type, IntPtr name, IntPtr state);
  private delegate bool LanguageCallback (IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr state);

  internal static IReadOnlyList<Blob> Read (string path, CancellationToken token = default, int? numericType = null, int? numericName = null)
  {
    ReaderWriterLockSlim gate = ResourceFileAccess.Gate (path);
    gate.EnterReadLock ();
    try { return ReadCore (path, token, numericType, numericName); }
    finally { gate.ExitReadLock (); }
  }

  private static IReadOnlyList<Blob> ReadCore (string path, CancellationToken token, int? numericType, int? numericName)
  {
    if (!OperatingSystem.IsWindows ()) throw new PlatformNotSupportedException ("Windows-Ressourcen sind nur unter Windows verfügbar.");
    token.ThrowIfCancellationRequested ();
    IntPtr module = LoadLibraryExW (Path.GetFullPath (path), IntPtr.Zero, 0x00000002 | 0x00000020);
    if (module == IntPtr.Zero) throw new IOException ($"Ressourcen-DLL kann nicht gelesen werden: {path}", new Win32Exception (Marshal.GetLastWin32Error ()));
    try
    {
      var result = new List<Blob> ();
      Exception? failure = null;
      LanguageCallback languageCallback = (handle, type, name, language, _) =>
      {
        try
        {
          token.ThrowIfCancellationRequested ();
          IntPtr resource = FindResourceExW (handle, type, name, language);
          if (resource == IntPtr.Zero) throw new Win32Exception (Marshal.GetLastWin32Error ());
          uint size = SizeofResource (handle, resource);
          if (size > int.MaxValue) throw new InvalidDataException ("Ressource ist zu groß.");
          IntPtr data = LockResource (LoadResource (handle, resource));
          if (data == IntPtr.Zero && size > 0) throw new Win32Exception (Marshal.GetLastWin32Error ());
          byte[] bytes = new byte[(int)size];
          if (size > 0) Marshal.Copy (data, bytes, 0, bytes.Length);
          result.Add (new (new (Name (type), Name (name), language), bytes));
          return true;
        }
        catch (Exception exception) { failure = exception; return false; }
      };
      NameCallback nameCallback = (handle, type, name, _) =>
      {
        bool success = EnumResourceLanguagesW (handle, type, name, languageCallback, IntPtr.Zero);
        if (!success && failure is null) failure = new Win32Exception (Marshal.GetLastWin32Error ());
        return success;
      };
      TypeCallback typeCallback = (handle, type, _) =>
      {
        bool success = EnumResourceNamesW (handle, type, nameCallback, IntPtr.Zero);
        if (!success && failure is null) failure = new Win32Exception (Marshal.GetLastWin32Error ());
        return success;
      };
      bool enumerated = numericType is { } requestedType && numericName is { } requestedName
          ? EnumResourceLanguagesW (module, (IntPtr)requestedType, (IntPtr)requestedName, languageCallback, IntPtr.Zero)
          : EnumResourceTypesW (module, typeCallback, IntPtr.Zero);
      if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture (failure).Throw ();
      int error = Marshal.GetLastWin32Error ();
      if (!enumerated && error is not 0 and not 1812 and not 1813 and not 1814 and not 1815) throw new IOException ("Ressourcentabelle kann nicht aufgelistet werden.", new Win32Exception (error));
      token.ThrowIfCancellationRequested ();
      return result;
    }
    finally { FreeLibrary (module); }
  }

  internal static void Update (string path, IReadOnlyList<Blob> updates)
  {
    IntPtr handle = BeginUpdateResourceW (path, false);
    if (handle == IntPtr.Zero) throw new IOException ("Arbeitskopie kann nicht aktualisiert werden.", new Win32Exception (Marshal.GetLastWin32Error ()));
    try
    {
      foreach (Blob blob in updates)
      {
        IntPtr type = Pointer (blob.Key.Type, out bool freeType);
        IntPtr name = Pointer (blob.Key.Name, out bool freeName);
        try
        {
          if (!UpdateResourceW (handle, type, name, blob.Key.Language, blob.Bytes, checked ((uint)blob.Bytes.Length)))
            throw new IOException ("Ressourcenupdate fehlgeschlagen.", new Win32Exception (Marshal.GetLastWin32Error ()));
        }
        finally
        {
          if (freeType) Marshal.FreeHGlobal (type);
          if (freeName) Marshal.FreeHGlobal (name);
        }
      }
      IntPtr ending = handle;
      handle = IntPtr.Zero;
      if (!EndUpdateResourceW (ending, false)) throw new IOException ("Arbeitskopie konnte nicht gespeichert werden.", new Win32Exception (Marshal.GetLastWin32Error ()));
    }
    finally { if (handle != IntPtr.Zero) EndUpdateResourceW (handle, true); }
  }

  private static string Name (IntPtr pointer) => (unchecked ((ulong)pointer.ToInt64 ()) >> 16) == 0
      ? "#" + pointer.ToInt64 () : Marshal.PtrToStringUni (pointer)!;
  private static IntPtr Pointer (string name, out bool allocated)
  {
    allocated = !name.StartsWith ('#') || !ushort.TryParse (name.AsSpan (1), out _);
    return allocated ? Marshal.StringToHGlobalUni (name) : (IntPtr)ushort.Parse (name.AsSpan (1));
  }

  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW (string fileName, IntPtr file, uint flags);
  [DllImport ("kernel32.dll", SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool FreeLibrary (IntPtr module);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool EnumResourceTypesW (IntPtr module, TypeCallback callback, IntPtr state);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool EnumResourceNamesW (IntPtr module, IntPtr type, NameCallback callback, IntPtr state);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool EnumResourceLanguagesW (IntPtr module, IntPtr type, IntPtr name, LanguageCallback callback, IntPtr state);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResourceExW (IntPtr module, IntPtr type, IntPtr name, ushort language);
  [DllImport ("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource (IntPtr module, IntPtr resource);
  [DllImport ("kernel32.dll", SetLastError = true)] private static extern IntPtr LockResource (IntPtr resource);
  [DllImport ("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource (IntPtr module, IntPtr resource);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr BeginUpdateResourceW (string path, [MarshalAs (UnmanagedType.Bool)] bool deleteExistingResources);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool UpdateResourceW (IntPtr handle, IntPtr type, IntPtr name, ushort language, byte[] bytes, uint length);
  [DllImport ("kernel32.dll", SetLastError = true)] [return: MarshalAs (UnmanagedType.Bool)] private static extern bool EndUpdateResourceW (IntPtr handle, [MarshalAs (UnmanagedType.Bool)] bool discard);
}
