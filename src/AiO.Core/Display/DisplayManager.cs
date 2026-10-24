using System.Runtime.InteropServices;
using AiO.Core.Native;
using static AiO.Core.Native.DisplayConfigNative;

namespace AiO.Core.Display;

public sealed record DisplayChangeResult(bool Success, bool RestartRequired, string Message);

/// <summary>Reads and changes resolution, refresh rate, orientation, HDR and scaling.</summary>
public static class DisplayManager
{
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var paths = GetActivePaths();
        var displays = new List<DisplayInfo>();

        var adapter = User32.DISPLAY_DEVICE.Create();
        for (uint i = 0; User32.EnumDisplayDevices(null, i, ref adapter, 0); i++, adapter = User32.DISPLAY_DEVICE.Create())
        {
            if ((adapter.StateFlags & User32.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var current = User32.DEVMODE.Create();
            if (!User32.EnumDisplaySettingsEx(adapter.DeviceName, User32.ENUM_CURRENT_SETTINGS, ref current, 0)) continue;

            paths.TryGetValue(adapter.DeviceName, out var path);
            displays.Add(new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                FriendlyName = path.HasValue ? TargetName(path.Value) ?? adapter.DeviceString : adapter.DeviceString,
                AdapterName = adapter.DeviceString,
                IsPrimary = (adapter.StateFlags & User32.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                PositionX = current.dmPositionX,
                PositionY = current.dmPositionY,
                CurrentMode = ToLandscapeMode(current, (DisplayOrientation)current.dmDisplayOrientation),
                Orientation = (DisplayOrientation)current.dmDisplayOrientation,
                Modes = EnumerateModes(adapter.DeviceName, (DisplayOrientation)current.dmDisplayOrientation),
                Hdr = path.HasValue ? ReadHdr(path.Value) : null,
                Scale = path.HasValue ? ReadScale(path.Value) : null,
            });
        }
        return displays;
    }

    /// <summary>AiO always describes modes in landscape terms; rotation is tracked separately.</summary>
    private static DisplayMode ToLandscapeMode(User32.DEVMODE dm, DisplayOrientation orientation)
    {
        var (width, height) = DisplayModeMath.SizeForOrientation((int)dm.dmPelsWidth, (int)dm.dmPelsHeight,
            orientation, DisplayOrientation.Landscape);
        return new(width, height, (int)dm.dmDisplayFrequency, (int)dm.dmBitsPerPel);
    }

    private static IReadOnlyList<DisplayMode> EnumerateModes(string deviceName, DisplayOrientation orientation)
    {
        var modes = new List<DisplayMode>();
        var dm = User32.DEVMODE.Create();
        for (int i = 0; User32.EnumDisplaySettingsEx(deviceName, i, ref dm, 0); i++)
        {
            // Without EDS_ROTATEDMODE, Windows lists modes in the display's current orientation.
            modes.Add(ToLandscapeMode(dm, orientation));
            dm = User32.DEVMODE.Create();
        }
        return DisplayModeMath.Normalize(modes);
    }

    /// <summary>Changes resolution/refresh rate/orientation. The change is tested first and persisted on success.</summary>
    public static DisplayChangeResult SetMode(DisplayInfo display, DisplayMode mode, DisplayOrientation? orientation = null)
    {
        var dm = User32.DEVMODE.Create();
        if (!User32.EnumDisplaySettingsEx(display.DeviceName, User32.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return new(false, false, "Could not read the current display settings.");

        var targetOrientation = orientation ?? display.Orientation;
        // The mode list is landscape; a portrait desktop needs width and height swapped.
        var (width, height) = DisplayModeMath.SizeForOrientation(mode.Width, mode.Height,
            DisplayOrientation.Landscape, targetOrientation);

        dm.dmPelsWidth = (uint)width;
        dm.dmPelsHeight = (uint)height;
        dm.dmDisplayFrequency = (uint)mode.RefreshRate;
        dm.dmBitsPerPel = (uint)mode.BitsPerPixel;
        dm.dmDisplayOrientation = (uint)targetOrientation;
        dm.dmFields = User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT | User32.DM_DISPLAYFREQUENCY |
                      User32.DM_BITSPERPEL | User32.DM_DISPLAYORIENTATION;

        int test = User32.ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, User32.CDS_TEST, IntPtr.Zero);
        if (test != User32.DISP_CHANGE_SUCCESSFUL)
            return new(false, false, $"Windows rejected this mode ({DescribeResult(test)}).");

        int result = User32.ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
        return result switch
        {
            User32.DISP_CHANGE_SUCCESSFUL => new(true, false, "Display settings applied."),
            User32.DISP_CHANGE_RESTART => new(true, true, "Restart Windows to finish applying this change."),
            _ => new(false, false, $"Could not apply this mode ({DescribeResult(result)})."),
        };
    }

    private static string DescribeResult(int code) => code switch
    {
        -1 => "the driver failed the change",
        -2 => "mode not supported",
        -3 => "registry write failed",
        -4 => "invalid flags",
        -5 => "invalid parameter",
        -6 => "the display is in a dual-view configuration",
        _ => $"code {code}",
    };

    public static bool SetHdr(DisplayInfo display, bool enabled)
    {
        if (!GetActivePaths().TryGetValue(display.DeviceName, out var path) || path is null) return false;
        var packet = new SET_ADVANCED_COLOR_STATE_DATA
        {
            header = Header<SET_ADVANCED_COLOR_STATE_DATA>(SET_ADVANCED_COLOR_STATE, path.Value.targetInfo.adapterId, path.Value.targetInfo.id),
            enableAdvancedColor = enabled ? 1u : 0u,
        };
        return DisplayConfigSetDeviceInfo(ref packet) == 0;
    }

    public static bool SetScale(DisplayInfo display, int percent)
    {
        if (display.Scale is null) return false;
        if (!GetActivePaths().TryGetValue(display.DeviceName, out var path) || path is null) return false;
        if (DisplayModeMath.EncodeScale(percent, display.Scale.RecommendedPercent) is not int rel) return false;

        var packet = new SET_DPI_SCALE_DATA
        {
            header = Header<SET_DPI_SCALE_DATA>(SET_DPI_SCALE, path.Value.sourceInfo.adapterId, path.Value.sourceInfo.id),
            scaleRel = rel,
        };
        return DisplayConfigSetDeviceInfo(ref packet) == 0;
    }

    // ---- CCD helpers -------------------------------------------------------

    private static DEVICE_INFO_HEADER Header<T>(int type, LUID adapter, uint id) where T : struct =>
        new() { type = type, size = Marshal.SizeOf<T>(), adapterId = adapter, id = id };

    /// <summary>Active CCD paths keyed by GDI device name.</summary>
    private static Dictionary<string, PATH_INFO?> GetActivePaths()
    {
        var result = new Dictionary<string, PATH_INFO?>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return result;

        var paths = new PATH_INFO[pathCount];
        var modes = new MODE_INFO[modeCount];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new SOURCE_DEVICE_NAME
            {
                header = Header<SOURCE_DEVICE_NAME>(GET_SOURCE_NAME, path.sourceInfo.adapterId, path.sourceInfo.id),
            };
            if (DisplayConfigGetDeviceInfo(ref source) == 0 && !result.ContainsKey(source.viewGdiDeviceName))
                result[source.viewGdiDeviceName] = path;
        }
        return result;
    }

    private static string? TargetName(PATH_INFO path)
    {
        var target = new TARGET_DEVICE_NAME
        {
            header = Header<TARGET_DEVICE_NAME>(GET_TARGET_NAME, path.targetInfo.adapterId, path.targetInfo.id),
        };
        if (DisplayConfigGetDeviceInfo(ref target) != 0) return null;
        return string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName) ? null : target.monitorFriendlyDeviceName;
    }

    private static HdrState? ReadHdr(PATH_INFO path)
    {
        var info = new GET_ADVANCED_COLOR_INFO_DATA
        {
            header = Header<GET_ADVANCED_COLOR_INFO_DATA>(GET_ADVANCED_COLOR_INFO, path.targetInfo.adapterId, path.targetInfo.id),
        };
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return null;
        return new HdrState((info.value & 0x1) != 0, (info.value & 0x2) != 0, (int)info.bitsPerColorChannel);
    }

    private static DisplayScale? ReadScale(PATH_INFO path)
    {
        var info = new GET_DPI_SCALE_DATA
        {
            header = Header<GET_DPI_SCALE_DATA>(GET_DPI_SCALE, path.sourceInfo.adapterId, path.sourceInfo.id),
        };
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return null;
        return DisplayModeMath.DecodeScale(info.minScaleRel, info.curScaleRel, info.maxScaleRel);
    }
}
