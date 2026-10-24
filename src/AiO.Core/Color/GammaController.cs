using AiO.Core.Native;
using Microsoft.Win32;

namespace AiO.Core.Color;

public static class GammaController
{
    private const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
    private const string GammaRangeValue = "GdiIcmGammaRange";

    /// <summary>Applies an adjustment to one display (e.g. \\.\DISPLAY1). Returns false if Windows refused the ramp.</summary>
    public static bool Apply(string deviceName, ColorAdjustment adjustment)
    {
        IntPtr hdc = Gdi32.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            return Gdi32.SetDeviceGammaRamp(hdc, GammaRamp.Build(adjustment));
        }
        finally
        {
            Gdi32.DeleteDC(hdc);
        }
    }

    public static bool Reset(string deviceName) => Apply(deviceName, ColorAdjustment.Neutral);

    /// <summary>
    /// By default Windows rejects ramps that stray far from identity (strong warm temperatures,
    /// big brightness changes). Setting GdiIcmGammaRange = 256 lifts that limit.
    /// </summary>
    public static bool IsFullRangeUnlocked()
    {
        using var key = Registry.LocalMachine.OpenSubKey(IcmKey);
        return key?.GetValue(GammaRangeValue) is int v && v == 256;
    }

    /// <summary>Requires administrator rights. Takes effect after signing out.</summary>
    public static void UnlockFullRange()
    {
        using var key = Registry.LocalMachine.CreateSubKey(IcmKey, writable: true);
        key.SetValue(GammaRangeValue, 256, RegistryValueKind.DWord);
    }
}
