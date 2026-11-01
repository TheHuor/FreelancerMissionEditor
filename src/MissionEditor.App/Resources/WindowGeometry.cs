using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace MissionEditor.App;

/// <summary>Remembered non-modal window bounds and browser dividers.</summary>
public sealed record WindowGeometry(double Left, double Top, double Width, double Height, double LeftRatio = .35, double UsageHeight = 170)
{
  private static string Folder => Environment.GetEnvironmentVariable("MISSION_EDITOR_WINDOW_SETTINGS_DIR")
      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreelancerMissionEditor");

  /// <summary>Restores bounds while keeping a reachable title bar in the desktop work area.</summary>
  public static WindowGeometry? Restore(Window window, string name)
  {
    try
    {
      var value = JsonSerializer.Deserialize<WindowGeometry>(File.ReadAllText(Path.Combine(Folder, name + "-window.json")));
      if (value is null || !double.IsFinite(value.Left + value.Top + value.Width + value.Height)) return null;
      Rect area = MonitorWorkArea(window, value);
      window.Width = Math.Clamp(value.Width, Math.Min(window.MinWidth, area.Width), area.Width);
      window.Height = Math.Clamp(value.Height, Math.Min(window.MinHeight, area.Height), area.Height);
      window.Left = Math.Clamp(value.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
      window.Top = Math.Clamp(value.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
      window.WindowStartupLocation = WindowStartupLocation.Manual;
      return value;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
  }

  private static Rect MonitorWorkArea(Window window, WindowGeometry bounds)
  {
    // MonitorFromRect preserves valid secondary-monitor coordinates and picks the nearest
    // remaining monitor after a display is disconnected. Native rectangles use device pixels.
    var dpi = VisualTreeHelper.GetDpi(window);
    var rectangle = new NativeRect
    {
      Left = (int)(bounds.Left * dpi.DpiScaleX), Top = (int)(bounds.Top * dpi.DpiScaleY),
      Right = (int)((bounds.Left + bounds.Width) * dpi.DpiScaleX), Bottom = (int)((bounds.Top + bounds.Height) * dpi.DpiScaleY),
    };
    IntPtr monitor = MonitorFromRect(ref rectangle, 2);
    var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
    if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
      return new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
          (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    return SystemParameters.WorkArea;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeRect { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)]
  private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
  [DllImport("user32.dll")]
  private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

  /// <summary>Stores normal bounds, including when a window is currently maximized.</summary>
  public static void Save(Window window, string name, double leftRatio = .35, double usageHeight = 170)
  {
    try
    {
      Rect bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
      Directory.CreateDirectory(Folder);
      File.WriteAllText(Path.Combine(Folder, name + "-window.json"), JsonSerializer.Serialize(new WindowGeometry(bounds.Left, bounds.Top, bounds.Width, bounds.Height, leftRatio, usageHeight)));
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
  }
}
