using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AiO.App.Infrastructure;

/// <summary>
/// Receives raw mouse movement (WM_INPUT) through a hidden message-only window, even while AiO
/// isn't in front. This is the same signal games and RawAccel's own graph read.
/// </summary>
public sealed class RawMouseMonitor : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

    private readonly HwndSource _window;
    private readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    private byte[] _buffer = new byte[64];

    /// <summary>Raised for each relative movement: dx, dy and a timestamp in milliseconds.</summary>
    public event Action<int, int, double>? Moved;

    public RawMouseMonitor()
    {
        _window = new HwndSource(new HwndSourceParameters("AiO raw mouse")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: invisible, message-only
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
        if (!Register(RIDEV_INPUTSINK, _window.Handle))
        {
            int error = Marshal.GetLastWin32Error();
            _window.Dispose();
            throw new System.ComponentModel.Win32Exception(error, "Windows refused raw mouse input");
        }
    }

    private static bool Register(uint flags, IntPtr target)
    {
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = flags, hwndTarget = target };
        return RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;

        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, (uint)_headerSize);
        if (size > _buffer.Length) _buffer = new byte[size];
        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, (uint)_headerSize) == unchecked((uint)-1)) return IntPtr.Zero;

        // RAWINPUTHEADER, then RAWMOUSE: usFlags (2) + pad (2) + buttons (4) + raw buttons (4) + lLastX (4) + lLastY (4).
        if (BitConverter.ToUInt32(_buffer, 0) != RIM_TYPEMOUSE) return IntPtr.Zero;
        ushort flags = BitConverter.ToUInt16(_buffer, _headerSize);
        if ((flags & MOUSE_MOVE_ABSOLUTE) != 0) return IntPtr.Zero; // tablets, remote desktop
        int dx = BitConverter.ToInt32(_buffer, _headerSize + 12);
        int dy = BitConverter.ToInt32(_buffer, _headerSize + 16);
        if (dx != 0 || dy != 0) Moved?.Invoke(dx, dy, Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Register(RIDEV_REMOVE, IntPtr.Zero);
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, byte[]? data, ref uint size, uint headerSize);
}
