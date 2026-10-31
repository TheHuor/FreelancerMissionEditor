using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>
/// The texts behind IDS numbers, read from the resource DLLs of an installation: index ids &gt;&gt; 16 in the list
/// resources.dll + [Resources] DLL = ... of freelancer.ini, local id ids &amp; 0xFFFF. Strings come from the
/// string tables, infocards (RT_HTML, Freelancer's XML) are reduced to plain text. Windows only.
/// </summary>
public sealed partial class GameText
{
    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;
    private const int ResourceTypeHtml = 23;

    private readonly string[] _dllPaths;
    private readonly IntPtr?[] _modules;
    private readonly Dictionary<int, string?> _cache = [];
    private readonly object _gate = new();

    private GameText(string[] dllPaths)
    {
        _dllPaths = dllPaths;
        _modules = new IntPtr?[dllPaths.Length];
    }

    /// <summary>The resource DLLs of an installation from its freelancer.ini (EXE folder).</summary>
    internal static GameText FromFreelancerIni(IniDocument freelancer, string exeFolder)
    {
        var paths = new List<string> { Path.Combine(exeFolder, "resources.dll") };
        paths.AddRange(freelancer.Sections.Where(section => section.Is("Resources"))
            .SelectMany(section => section.FindAll("DLL"))
            .Select(entry => Path.Combine(exeFolder, entry.GetValue(0))));
        return new GameText([.. paths]);
    }

    /// <summary>The text of an IDS number, or null when there is none (or not on Windows).</summary>
    public string? Get(int ids)
    {
        if (ids <= 0 || !OperatingSystem.IsWindows())
            return null;
        lock (_gate)
        {
            if (_cache.TryGetValue(ids, out string? cached))
                return cached;
            string? text = Read(ids);
            _cache[ids] = text;
            return text;
        }
    }

    private string? Read(int ids)
    {
        int index = ids >> 16;
        if (index >= _dllPaths.Length || Module(index) is not { } module)
            return null;
        uint local = (uint)(ids & 0xFFFF);
        var buffer = new char[4096];
        int length = LoadStringW(module, local, buffer, buffer.Length);
        if (length > 0)
            return Clean(new string(buffer, 0, length));

        IntPtr resource = FindResourceW(module, (IntPtr)local, (IntPtr)ResourceTypeHtml);
        if (resource == IntPtr.Zero)
            return null;
        int size = (int)SizeofResource(module, resource);
        IntPtr data = LockResource(LoadResource(module, resource));
        if (data == IntPtr.Zero || size < 2)
            return null;
        var bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, size);
        string xml = Encoding.Unicode.GetString(bytes).TrimStart('﻿').TrimEnd('\0');
        return Clean(InfocardText(xml));
    }

    private IntPtr? Module(int index)
    {
        if (_modules[index] is { } loaded)
            return loaded == IntPtr.Zero ? null : loaded;
        IntPtr module = File.Exists(_dllPaths[index])
            ? LoadLibraryExW(_dllPaths[index], IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource)
            : IntPtr.Zero;
        _modules[index] = module;
        return module == IntPtr.Zero ? null : module;
    }

    // Infocards are XML (<RDL><TEXT>…</TEXT><PARA/>…); paragraphs become line breaks.
    private static string InfocardText(string xml)
    {
        string text = ParagraphTag().Replace(xml, "\n");
        text = AnyTag().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    private static string? Clean(string text)
    {
        string cleaned = string.Join("\n", text.Replace("\r", string.Empty).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex("<PARA\\s*/>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphTag();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int LoadStringW(IntPtr instance, uint id, [Out] char[] buffer, int bufferMax);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
}
