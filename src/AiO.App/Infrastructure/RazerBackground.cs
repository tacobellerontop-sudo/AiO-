using System.Windows.Threading;
using AiO.Core.Razer;
using AiO.Core.Settings;

namespace AiO.App.Infrastructure;

/// <summary>Razer jobs that run without the Razer page open: start-up restore and low-battery alerts.</summary>
public static class RazerBackground
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private static readonly HashSet<ushort> Warned = [];
    private static int _checking;

    public static void Start()
    {
        var settings = AppServices.Instance.Settings.Current;
        if (settings.Razer.Values.Any(r => r.RestoreOnStartup))
            Task.Run(() => Try(RestoreAll));
        Timer.Tick += (_, _) => CheckBatteries();
        Timer.Start();
    }

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { AppServices.Instance.Report($"Razer: {ex.Message}"); }
    }

    private static void RestoreAll()
    {
        var all = AppServices.Instance.Settings.Current.Razer;
        var devices = RazerDevice.DiscoverAll();
        try
        {
            foreach (var device in devices)
            {
                if (!all.TryGetValue(device.ProductId.ToString("X4"), out var saved) || !saved.RestoreOnStartup) continue;
                Restore(device, saved);
            }
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }

    /// <summary>Sends every saved setting; one the device refuses doesn't stop the rest.</summary>
    private static void Restore(RazerDevice device, RazerDeviceSettings saved)
    {
        void Step(Action a) { try { a(); } catch (RazerCommandException) { } }

        if (saved.Dpi is int dpi) Step(() => device.SetDpi(dpi, dpi));
        if (saved.PollingRate is int hz) Step(() => device.SetPollingRate(hz));
        if (!saved.LightingSet) return;
        var effect = Enum.TryParse<RazerEffect>(saved.Effect, out var e) ? e : RazerEffect.Static;
        var colors = new List<RgbColor> { RgbColor.FromHex(saved.Color), RgbColor.FromHex(saved.Color2) };
        foreach (var led in device.DetectLeds())
        {
            Step(() => device.SetEffect(led, effect, colors));
            Step(() => device.SetBrightness(led, (byte)Math.Clamp(saved.Brightness, 0, 255)));
        }
    }

    /// <summary>Warns once per discharge when a wireless device drops to the alert level.</summary>
    public static void CheckBatteries()
    {
        var options = AppServices.Instance.Settings.Current.RazerOptions;
        if (!options.BatteryAlerts || Interlocked.Exchange(ref _checking, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var devices = RazerDevice.DiscoverAll();
                try
                {
                    foreach (var device in devices)
                    {
                        if (device.GetBatteryPercent() is not int pct || pct <= 0) continue;
                        bool charging = device.IsCharging() == true;
                        if (charging || pct > options.AlertPercent + 5) { lock (Warned) Warned.Remove(device.ProductId); continue; }
                        if (pct > options.AlertPercent) continue;
                        bool first;
                        lock (Warned) first = Warned.Add(device.ProductId);
                        if (first) AppServices.Instance.ShowOsd("", $"{device.Name} battery low: {pct}%");
                    }
                }
                finally
                {
                    foreach (var d in devices) d.Dispose();
                }
            }
            catch
            {
                // A device unplugged mid-check; try again next time.
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        });
    }
}
