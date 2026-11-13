using System.Management;
using System.Runtime.InteropServices;
using AiO.Core.Native;

namespace AiO.Core.Display;

/// <summary>The Win+P choices.</summary>
public enum ProjectionMode
{
    PcScreenOnly,
    Duplicate,
    Extend,
    SecondScreenOnly,
}

/// <summary>Projection mode (Win+P), primary display, and built-in panel brightness.</summary>
public static class DisplayExtras
{
    private const uint SDC_TOPOLOGY_INTERNAL = 0x1;
    private const uint SDC_TOPOLOGY_CLONE = 0x2;
    private const uint SDC_TOPOLOGY_EXTEND = 0x4;
    private const uint SDC_TOPOLOGY_EXTERNAL = 0x8;
    private const uint SDC_APPLY = 0x80;

    private const uint CDS_SET_PRIMARY = 0x10;
    private const uint CDS_NORESET = 0x10000000;

    /// <summary>Same as picking an option in Win+P. Returns false when Windows refuses (e.g. only one screen).</summary>
    public static bool SetProjection(ProjectionMode mode)
    {
        uint topology = mode switch
        {
            ProjectionMode.PcScreenOnly => SDC_TOPOLOGY_INTERNAL,
            ProjectionMode.Duplicate => SDC_TOPOLOGY_CLONE,
            ProjectionMode.Extend => SDC_TOPOLOGY_EXTEND,
            _ => SDC_TOPOLOGY_EXTERNAL,
        };
        return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | topology) == 0;
    }

    /// <summary>
    /// Makes <paramref name="display"/> the main display. Windows puts the primary display at (0, 0),
    /// so every display is moved by the same offset to keep the layout.
    /// </summary>
    public static bool SetPrimary(DisplayInfo display, IReadOnlyList<DisplayInfo> all)
    {
        if (display.IsPrimary) return true;
        int dx = display.PositionX, dy = display.PositionY;
        bool ok = true;
        foreach (var d in all)
        {
            var dm = User32.DEVMODE.Create();
            if (!User32.EnumDisplaySettingsEx(d.DeviceName, User32.ENUM_CURRENT_SETTINGS, ref dm, 0)) { ok = false; continue; }
            dm.dmPositionX -= dx;
            dm.dmPositionY -= dy;
            dm.dmFields = User32.DM_POSITION;
            uint flags = User32.CDS_UPDATEREGISTRY | CDS_NORESET;
            if (d.DeviceName == display.DeviceName) flags |= CDS_SET_PRIMARY;
            if (User32.ChangeDisplaySettingsEx(d.DeviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero) != User32.DISP_CHANGE_SUCCESSFUL)
                ok = false;
        }
        // Applies everything queued with CDS_NORESET in one go.
        int commit = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return ok && commit == User32.DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Built-in panel brightness in percent (WMI), or null when the screen doesn't expose it.</summary>
    public static int? GetPanelBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    public static bool SetPanelBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
                using (o)
                {
                    o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)Math.Clamp(percent, 0, 100)]);
                    return true;
                }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements,
        IntPtr modeInfoArray, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
}
