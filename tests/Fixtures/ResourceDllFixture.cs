using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MissionEditor.Core.Model;

namespace MissionEditor.TestFixtures;

/// <summary>A disposable standalone installation made exclusively from a temporary copy of our own Core DLL.</summary>
internal sealed class ResourceDllFixture : IDisposable
{
  public string Root { get; } = Path.Combine (Path.GetTempPath (), "mission-resource-fixture-" + Guid.NewGuid ());
  public string DllPath => Path.Combine (Root, "EXE", "resources.dll");
  public static string LongText => "  Grüße Ω\n\n" + new string ('x', 6000) + "  ";
  public const string Card = "<RDL><PUSH/><TRA data=\"1\"/><TEXT>First</TEXT><PARA/><TEXT>Second</TEXT><POP/></RDL>";

  public ResourceDllFixture ()
  {
    Directory.CreateDirectory (Path.Combine (Root, "EXE"));
    File.WriteAllText (Path.Combine (Root, "EXE", "freelancer.ini"), "[Freelancer]\ndata path = ..\\DATA\n");
    File.Copy (typeof (GameData).Assembly.Location, DllPath);
    IntPtr update = BeginUpdateResourceW (DllPath, false);
    if (update == IntPtr.Zero) throw new Win32Exception ();
    try
    {
      Put (6, 1, 1033, Block ((1, LongText), (2, "Neighbor"), (3, "Third")));
      Put (6, 1, 1031, Block ((1, "Deutsch"), (2, "Nachbar")));
      Put (23, 50, 1033, Encoding.Unicode.GetBytes ("\uFEFF" + Card + "\0"));
      Put (10, 70, 1033, [1, 2, 3, 4, 5]);
      if (!EndUpdateResourceW (update, false)) throw new Win32Exception ();
      update = IntPtr.Zero;
    }
    finally { if (update != IntPtr.Zero) EndUpdateResourceW (update, true); }

    void Put (int type, int name, ushort lang, byte[] bytes)
    {
      if (!UpdateResourceW (update, (IntPtr)type, (IntPtr)name, lang, bytes, (uint)bytes.Length)) throw new Win32Exception ();
    }
  }

  public void Dispose () => Directory.Delete (Root, true);

  public void AddInfocard (int localId, string rdl)
      => AddInfocardBytes (localId, Encoding.Unicode.GetBytes (rdl + "\0"));

  public void AddInfocardBytes (int localId, byte[] bytes, ushort language = 1033)
  {
    IntPtr update = BeginUpdateResourceW (DllPath, false);
    if (update == IntPtr.Zero) throw new Win32Exception ();
    try
    {
      if (!UpdateResourceW (update, (IntPtr)23, (IntPtr)localId, language, bytes, (uint)bytes.Length)) throw new Win32Exception ();
      if (!EndUpdateResourceW (update, false)) throw new Win32Exception ();
      update = IntPtr.Zero;
    }
    finally { if (update != IntPtr.Zero) EndUpdateResourceW (update, true); }
  }

  private static byte[] Block (params (int Index, string Value)[] values)
  {
    using var stream = new MemoryStream ();
    using var writer = new BinaryWriter (stream, Encoding.Unicode);
    for (int index = 0; index < 16; index++)
    {
      string value = values.FirstOrDefault (pair => pair.Index == index).Value ?? string.Empty;
      writer.Write ((ushort)value.Length);
      writer.Write (Encoding.Unicode.GetBytes (value));
    }
    return stream.ToArray ();
  }

  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr BeginUpdateResourceW (string path, [MarshalAs (UnmanagedType.Bool)] bool delete);
  [DllImport ("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs (UnmanagedType.Bool)]
  private static extern bool UpdateResourceW (IntPtr handle, IntPtr type, IntPtr name, ushort language, byte[] bytes, uint length);
  [DllImport ("kernel32.dll", SetLastError = true)]
  [return: MarshalAs (UnmanagedType.Bool)]
  private static extern bool EndUpdateResourceW (IntPtr handle, [MarshalAs (UnmanagedType.Bool)] bool discard);
}
