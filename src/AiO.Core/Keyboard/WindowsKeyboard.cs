using AiO.Core.Native;

namespace AiO.Core.Keyboard;

/// <summary>Windows' key repeat settings (Control Panel → Keyboard).</summary>
public static class WindowsKeyboard
{
    private const uint SPI_GETKEYBOARDSPEED = 0x000A;
    private const uint SPI_SETKEYBOARDSPEED = 0x000B;
    private const uint SPI_GETKEYBOARDDELAY = 0x0016;
    private const uint SPI_SETKEYBOARDDELAY = 0x0017;
    private const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x3;

    /// <summary>Delay before a held key repeats: 0 (about 250 ms) to 3 (about 1 s).</summary>
    public static int GetRepeatDelay()
    {
        int delay = 1;
        User32.SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0);
        return Math.Clamp(delay, 0, 3);
    }

    public static bool SetRepeatDelay(int delay) =>
        User32.SystemParametersInfo(SPI_SETKEYBOARDDELAY, (uint)Math.Clamp(delay, 0, 3), IntPtr.Zero, SPIF_UPDATEINIFILE_SENDCHANGE);

    /// <summary>Repeat rate: 0 (about 2.5 per second) to 31 (about 30 per second).</summary>
    public static int GetRepeatRate()
    {
        int rate = 31;
        User32.SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, ref rate, 0);
        return Math.Clamp(rate, 0, 31);
    }

    public static bool SetRepeatRate(int rate) =>
        User32.SystemParametersInfo(SPI_SETKEYBOARDSPEED, (uint)Math.Clamp(rate, 0, 31), IntPtr.Zero, SPIF_UPDATEINIFILE_SENDCHANGE);

    /// <summary>Approximate milliseconds for a repeat-delay step, for display.</summary>
    public static int DelayMilliseconds(int delay) => 250 * (Math.Clamp(delay, 0, 3) + 1);

    /// <summary>Approximate repeats per second for a rate step, for display.</summary>
    public static double RatePerSecond(int rate) => 2.5 + Math.Clamp(rate, 0, 31) * (30.0 - 2.5) / 31;
}
