using AiO.Core.Native;

namespace AiO.Core.Color;

/// <summary>Standard MCCS VCP codes AiO exposes.</summary>
public static class VcpCode
{
    public const byte Brightness = 0x10;
    public const byte Contrast = 0x12;
    public const byte ColorPreset = 0x14;
    public const byte RedGain = 0x16;
    public const byte GreenGain = 0x18;
    public const byte BlueGain = 0x1A;
    public const byte InputSource = 0x60;
    public const byte Volume = 0x62;
    public const byte RestoreFactoryColor = 0x08;
}

public sealed record VcpValue(uint Current, uint Maximum);

/// <summary>
/// A physical monitor reachable over DDC/CI. These settings live in the monitor itself
/// (like pressing its OSD buttons), so they survive switching PCs.
/// DDC/CI is slow (tens of ms per call): don't call from the UI thread.
/// </summary>
public sealed class DdcMonitor : IDisposable
{
    private readonly IntPtr _handle;

    /// <summary>GDI device name of the display this monitor is attached to, e.g. \\.\DISPLAY1.</summary>
    public string DeviceName { get; }
    public string Description { get; }

    private DdcMonitor(IntPtr handle, string deviceName, string description)
    {
        _handle = handle;
        DeviceName = deviceName;
        Description = description;
    }

    public static IReadOnlyList<DdcMonitor> EnumerateAll()
    {
        var monitors = new List<DdcMonitor>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref User32.RECT _, IntPtr _) =>
        {
            var info = User32.MONITORINFOEX.Create();
            if (!User32.GetMonitorInfo(hMonitor, ref info)) return true;
            if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) return true;

            var physical = new Dxva2.PHYSICAL_MONITOR[count];
            if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return true;
            foreach (var p in physical)
                monitors.Add(new DdcMonitor(p.hPhysicalMonitor, info.szDevice, p.szPhysicalMonitorDescription));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public VcpValue? Get(byte code) =>
        Dxva2.GetVCPFeatureAndVCPFeatureReply(_handle, code, IntPtr.Zero, out uint current, out uint max)
            ? new VcpValue(current, max)
            : null;

    public bool Set(byte code, uint value) => Dxva2.SetVCPFeature(_handle, code, value);

    /// <summary>True if the monitor answers DDC/CI at all (many laptop panels and some TVs don't).</summary>
    public bool IsResponsive() => Get(VcpCode.Brightness) is not null;

    public void Dispose() => Dxva2.DestroyPhysicalMonitor(_handle);

    /// <summary>Common MCCS input source values. Monitors vary; these are the standard ones.</summary>
    public static IReadOnlyDictionary<uint, string> InputSources { get; } = new Dictionary<uint, string>
    {
        [0x01] = "VGA 1",
        [0x03] = "DVI 1",
        [0x04] = "DVI 2",
        [0x0F] = "DisplayPort 1",
        [0x10] = "DisplayPort 2",
        [0x11] = "HDMI 1",
        [0x12] = "HDMI 2",
        [0x1B] = "USB-C",
    };
}
