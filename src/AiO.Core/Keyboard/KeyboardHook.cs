using System.Runtime.InteropServices;
using System.Text;

namespace AiO.Core.Keyboard;

/// <summary>
/// System-wide key remapping through a low-level keyboard hook (the same approach as PowerToys
/// Keyboard Manager). Works with any keyboard. Limits: it can't see keys typed into apps running as
/// administrator unless AiO is elevated too, and Ctrl+Alt+Del / the lock screen are off-limits.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    // Marks input AiO injects so the hook lets it through instead of remapping it again.
    private static readonly IntPtr InjectedMarker = new(0x41694F21); // "AiO!"

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    private volatile KeyRemapEngine _engine;
    private volatile bool _needsForegroundApp;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private IntPtr _hook;
    private HookProc? _proc; // kept alive while the hook is installed
    private readonly Dictionary<uint, string> _processNames = new();

    /// <summary>Raised (on the hook thread) for mappings whose action isn't a key press.</summary>
    public event Action<KeyMapping>? ActionTriggered;

    public bool IsRunning => _hook != IntPtr.Zero;

    public KeyboardHook(IEnumerable<KeyMapping> mappings)
    {
        var list = mappings.ToList();
        _engine = new KeyRemapEngine(list);
        _needsForegroundApp = list.Any(m => !string.IsNullOrWhiteSpace(m.AppFilter));
        _thread = new Thread(Run) { IsBackground = true, Name = "AiO keyboard hook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    public void UpdateMappings(IEnumerable<KeyMapping> mappings)
    {
        var list = mappings.ToList();
        _engine = new KeyRemapEngine(list);
        _needsForegroundApp = list.Any(m => !string.IsNullOrWhiteSpace(m.AppFilter));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero) return;

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (data.dwExtraInfo == InjectedMarker) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        int message = (int)wParam;
        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = message is WM_KEYUP or WM_SYSKEYUP;
        if (!down && !up) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        KeyDecision decision;
        try
        {
            decision = _engine.Process((int)data.vkCode, down, _needsForegroundApp ? ForegroundProcessName() : null);
        }
        catch
        {
            // Never let an exception swallow the user's typing.
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        if (decision.Outputs.Count > 0) Send(decision.Outputs);
        if (decision.Action is { } action) ThreadPool.QueueUserWorkItem(_ => ActionTriggered?.Invoke(action));
        return decision.Suppress ? (IntPtr)1 : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private string? ForegroundProcessName()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out uint pid);
        if (_processNames.TryGetValue(pid, out var cached)) return cached;

        string? name = null;
        IntPtr process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process != IntPtr.Zero)
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (QueryFullProcessImageName(process, 0, buffer, ref size)) name = Path.GetFileName(buffer.ToString());
            CloseHandle(process);
        }
        if (_processNames.Count > 256) _processNames.Clear();
        if (name is not null) _processNames[pid] = name;
        return name;
    }

    private static void Send(IReadOnlyList<KeyOutput> outputs)
    {
        var inputs = new INPUT[outputs.Count];
        for (int i = 0; i < outputs.Count; i++)
        {
            var o = outputs[i];
            uint flags = o.Down ? 0 : KEYEVENTF_KEYUP;
            if (VirtualKeys.IsExtended(o.Key)) flags |= KEYEVENTF_EXTENDEDKEY;
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)o.Key,
                        wScan = (ushort)MapVirtualKey((uint)o.Key, 0),
                        dwFlags = flags,
                        dwExtraInfo = InjectedMarker,
                    },
                },
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Types text into the focused app (works for any character, independent of layout).</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n')
            {
                inputs.Add(KeyInput(VirtualKeys.Enter, 0));
                inputs.Add(KeyInput(VirtualKeys.Enter, KEYEVENTF_KEYUP));
                continue;
            }
            inputs.Add(Unicode(c, 0));
            inputs.Add(Unicode(c, KEYEVENTF_KEYUP));
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        static INPUT Unicode(char c, uint flags) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | flags, dwExtraInfo = InjectedMarker } },
        };
        static INPUT KeyInput(int vk, uint flags) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags, dwExtraInfo = InjectedMarker } },
        };
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }

    // ---- interop -------------------------------------------------------------------

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
