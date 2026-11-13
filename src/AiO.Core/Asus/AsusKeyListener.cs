using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using AiO.Core.Hid;
using AiO.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace AiO.Core.Asus;

/// <summary>A raw key event from the ASUS firmware.</summary>
public readonly record struct AsusKeyEvent(int Code, string Source)
{
    public AsusHotkey? Key => AsusHotkeyCodes.FromEventCode(Code);
}

/// <summary>
/// Listens for ASUS special keys (M3, M4/ROG, Fn+F4, Fn+F5...) on the two channels the firmware uses:
/// <list type="bullet">
/// <item>the AsusAtkWmiEvent WMI event, raised through the ASUS ACPI driver;</item>
/// <item>input report 0x5A on the keyboard's vendor HID interface, which is how the keys arrive when
///   the ASUS Optimization service isn't installed to turn them into WMI events.</item>
/// </list>
/// Both are opened; <see cref="HotkeyDebouncer"/> drops the duplicate when a press arrives on both.
/// </summary>
public sealed class AsusKeyListener : IDisposable
{
    private readonly HotkeyDebouncer _debouncer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<SafeFileHandle> _hidHandles = [];
    private ManagementEventWatcher? _wmi;
    private Thread? _hidThread;

    /// <summary>Raised on a background thread for each key press.</summary>
    public event Action<AsusKeyEvent>? KeyPressed;

    /// <summary>Which channels opened, for the diagnostics line.</summary>
    public bool WmiListening { get; private set; }
    public int HidInterfaces { get; private set; }

    /// <summary>Starts both channels. <paramref name="acpi"/> (if any) gets the INIT call that enables events.</summary>
    public void Start(AsusAcpi? acpi)
    {
        try { acpi?.InitHotkeys(); }
        catch { /* Older firmware has no INIT; events may still arrive. */ }

        StartWmi();
        _hidThread = new Thread(HidLoop) { IsBackground = true, Name = "AiO ASUS keys" };
        _hidThread.Start();
    }

    private void StartWmi()
    {
        try
        {
            _wmi = new ManagementEventWatcher(new ManagementScope(@"root\wmi"),
                new WqlEventQuery("SELECT * FROM AsusAtkWmiEvent"));
            _wmi.EventArrived += (_, e) =>
            {
                try
                {
                    if (e.NewEvent?["EventID"] is { } id) Raise(Convert.ToInt32(id), "WMI");
                }
                catch (Exception ex) { Debug.WriteLine($"ASUS WMI event: {ex.Message}"); }
            };
            _wmi.Start();
            WmiListening = true;
        }
        catch (Exception ex)
        {
            // The class only exists with the ASUS ACPI/WMI driver installed.
            Debug.WriteLine($"ASUS WMI events unavailable: {ex.Message}");
            _wmi?.Dispose();
            _wmi = null;
        }
    }

    private void Raise(int code, string source)
    {
        if (AsusHotkeyCodes.IsNoise(code)) return;
        if (!_debouncer.ShouldHandle(code, DateTime.UtcNow)) return;
        KeyPressed?.Invoke(new AsusKeyEvent(code, source));
    }

    // ---- HID ---------------------------------------------------------------------------

    /// <summary>The keyboard's vendor collections that can carry report 0x5A.</summary>
    internal static IEnumerable<HidDeviceInfo> InputCandidates(IEnumerable<HidDeviceInfo> devices) =>
        devices.Where(d => d.VendorId == AsusAura.VendorId
                           && d.InputReportLength > 1
                           && (d.UsagePage == AsusAura.VendorUsagePage
                               || (AsusAura.AuraProductIds.Contains(d.ProductId) && d.UsagePage >= 0xFF00)));

    /// <summary>Extracts the key code from an input report, or null if it isn't a key press.</summary>
    internal static int? ParseInputReport(ReadOnlySpan<byte> report, int length)
    {
        if (length < 2 || report[0] != AsusAura.InputReportId) return null;
        int code = report[1];
        return code == 0 ? null : code;
    }

    private void HidLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var readers = OpenHid();
            HidInterfaces = readers.Count;
            if (readers.Count == 0)
            {
                // Not found yet (e.g. right after resume); look again later.
                if (_stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) return;
                continue;
            }

            // One reader thread per interface; wait for all to end (device gone, sleep, or Dispose).
            var threads = readers.Select(r => new Thread(() => ReadLoop(r)) { IsBackground = true, Name = "AiO ASUS keys (HID)" }).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            lock (_hidHandles)
            {
                foreach (var h in readers) h.Handle.Dispose();
                _hidHandles.Clear();
            }
            if (_stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2))) return;
        }
    }

    private sealed record HidReader(SafeFileHandle Handle, int InputLength);

    private List<HidReader> OpenHid()
    {
        var result = new List<HidReader>();
        IEnumerable<HidDeviceInfo> candidates;
        try { candidates = InputCandidates(HidEnumerator.Enumerate(AsusAura.VendorId)).ToList(); }
        catch { return result; }

        foreach (var info in candidates)
        {
            var handle = Kernel32.CreateFile(info.Path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
                Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            // The handshake the keyboard expects before it reports keys to an app.
            if (info.FeatureReportLength > 0)
            {
                var init = AsusAura.BuildInitMessage(AsusAura.InputReportId);
                var report = new byte[Math.Max(info.FeatureReportLength, init.Length)];
                init.CopyTo(report, 0);
                HidNative.HidD_SetFeature(handle, report, report.Length);
            }

            lock (_hidHandles) _hidHandles.Add(handle);
            result.Add(new HidReader(handle, info.InputReportLength));
        }
        return result;
    }

    private void ReadLoop(HidReader reader)
    {
        var buffer = new byte[reader.InputLength];
        while (!_stop.IsCancellationRequested)
        {
            if (!Kernel32.ReadFile(reader.Handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
            {
                Debug.WriteLine($"ASUS HID read ended: {Marshal.GetLastWin32Error()}");
                return;
            }
            if (ParseInputReport(buffer, (int)read) is int code) Raise(code, "HID");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _wmi?.Stop(); } catch { }
        _wmi?.Dispose();
        lock (_hidHandles)
            foreach (var h in _hidHandles)
                if (!h.IsClosed) Kernel32.CancelIoEx(h, IntPtr.Zero);
        _hidThread?.Join(TimeSpan.FromSeconds(2));
    }
}
