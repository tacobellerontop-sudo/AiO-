using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AiO.App.Infrastructure;
using AiO.Core.Asus;
using AiO.Core.Display;
using AiO.Core.Settings;
using Microsoft.Win32;

namespace AiO.App.ViewModels;

public sealed record Option<T>(T? Value, string Label) where T : struct
{
    public override string ToString() => Label;
}

/// <summary>
/// G-Helper style automation: switches mode, GPU and screen refresh rate when the charger is plugged
/// in or pulled, turns the keyboard backlight off when idle, and re-applies settings after sleep.
/// </summary>
public sealed class AsusAutomationViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Func<AsusViewModel?> _asus;
    private readonly Action? _afterResume;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool? _lastOnAc;
    private bool _backlightDimmed;
    private string _refreshText = "";

    public AsusAutomationViewModel(Func<AsusViewModel?> asus, Action? afterResume = null)
    {
        _asus = asus;
        _afterResume = afterResume;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        AcProfile = new PowerProfileViewModel(Saved.OnAc, Changed);
        BatteryProfile = new PowerProfileViewModel(Saved.OnBattery, Changed);
        SetRefreshCommand = new RelayCommand(p => { if (p is string s && Enum.TryParse(s, out RefreshChoice c)) SetRefresh(c, showOsd: false); });

        _lastOnAc = IsOnAc();
        if (Saved.AutoSwitch) Dispatcher.CurrentDispatcher.BeginInvoke(() => ApplyProfile(force: true), DispatcherPriority.Background);
        UpdateRefreshText();
    }

    private AsusSettings Saved => _services.Settings.Current.Asus;

    // ---- power source ------------------------------------------------------------------

    public bool AutoSwitch
    {
        get => Saved.AutoSwitch;
        set
        {
            Saved.AutoSwitch = value;
            _services.SaveSettings();
            OnPropertyChanged();
            if (value) ApplyProfile(force: true);
        }
    }

    public PowerProfileViewModel AcProfile { get; }
    public PowerProfileViewModel BatteryProfile { get; }

    public string PowerSourceText => IsOnAc() switch
    {
        true => "Plugged in now.",
        false => "On battery now.",
        null => "Power source unknown.",
    };

    public bool ShowOsd
    {
        get => _services.Settings.Current.General.ShowOsd;
        set { _services.Settings.Current.General.ShowOsd = value; _services.SaveSettings(); OnPropertyChanged(); }
    }

    private void Changed()
    {
        _services.SaveSettings();
        if (Saved.AutoSwitch) ApplyProfile(force: true, showOsd: false);
    }

    private static bool? IsOnAc() => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus switch
    {
        System.Windows.Forms.PowerLineStatus.Online => true,
        System.Windows.Forms.PowerLineStatus.Offline => false,
        _ => null,
    };

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (e.Mode == PowerModes.Resume) Resume();
            OnPropertyChanged(nameof(PowerSourceText));
            if (e.Mode == PowerModes.StatusChange && Saved.AutoSwitch) ApplyProfile(force: false);
        });
    }

    /// <summary>Firmware forgets fan curves and charge limits over sleep on some models; put them back.</summary>
    private void Resume()
    {
        if (_services.Asus is { } laptop)
        {
            try { StartupApplier.ApplyAsus(laptop, Saved); }
            catch (Exception ex) { _services.Report($"Could not restore ASUS settings after sleep: {ex.Message}"); }
        }
        _backlightDimmed = false;
        _afterResume?.Invoke();
        // The CPU forgets Curve Optimizer and temperature limits over sleep.
        if (Saved.DangerZoneUnlocked && Saved.Ryzen.ApplyAutomatically && (Saved.Ryzen.CurveOffset != 0 || Saved.Ryzen.TempLimit is not null))
            Task.Run(() => _services.Report(RyzenTuning.Apply(Saved.Ryzen)));
        if (Saved.AutoSwitch) ApplyProfile(force: true, showOsd: false);
    }

    /// <summary>Applies the profile for the current power source (only when it changed, unless forced).</summary>
    private void ApplyProfile(bool force, bool showOsd = true)
    {
        bool? onAc = IsOnAc();
        if (onAc is null) return;
        if (!force && onAc == _lastOnAc) return;
        _lastOnAc = onAc;

        var profile = onAc.Value ? Saved.OnAc : Saved.OnBattery;
        var done = new List<string>();

        if (_asus() is { } asus)
        {
            if (profile.Mode is { } mode && asus.PerformanceMode != mode)
            {
                asus.PerformanceMode = mode;
                done.Add(mode.ToString());
            }
            // Ultimate (MUX) needs a restart, so automation never switches into or out of it.
            if (profile.Gpu is AsusGpuMode.Eco or AsusGpuMode.Standard && asus.SupportsGpuModes
                && asus.GpuMode is { } current && current != AsusGpuMode.Ultimate && current != profile.Gpu)
            {
                asus.GpuMode = profile.Gpu;
                done.Add($"GPU {profile.Gpu}");
            }
        }
        if (profile.Refresh is { } refresh && SetRefresh(refresh, showOsd: false) is { } hz) done.Add($"{hz} Hz");

        if (showOsd && done.Count > 0)
            _services.ShowOsd(onAc.Value ? "" : "", $"{(onAc.Value ? "Plugged in" : "On battery")}: {string.Join(" · ", done)}");
    }

    // ---- screen refresh ------------------------------------------------------------------

    public System.Windows.Input.ICommand SetRefreshCommand { get; }

    public string RefreshText { get => _refreshText; private set => Set(ref _refreshText, value); }

    private void UpdateRefreshText()
    {
        try
        {
            RefreshText = DisplayManager.GetInternalDisplay() is { } panel
                ? $"Built-in screen: {panel.CurrentMode.RefreshRate} Hz (up to {panel.Modes.Where(m => m.Width == panel.CurrentMode.Width && m.Height == panel.CurrentMode.Height).Max(m => m.RefreshRate)} Hz)"
                : "The built-in screen is off or wasn't found.";
        }
        catch (Exception ex)
        {
            RefreshText = $"Couldn't read the screen: {ex.Message}";
        }
    }

    /// <summary>Returns the new rate when it changed.</summary>
    private int? SetRefresh(RefreshChoice choice, bool showOsd)
    {
        int? result = null;
        try
        {
            var change = DisplayManager.SetInternalRefresh(choice);
            if (change is { Success: false }) _services.Report(change.Message);
            else if (change is { Success: true } && DisplayManager.GetInternalDisplay() is { } panel)
            {
                result = panel.CurrentMode.RefreshRate;
                if (showOsd) _services.ShowOsd("", $"{result} Hz");
                else _services.Report($"Built-in screen set to {result} Hz.");
            }
        }
        catch (Exception ex)
        {
            _services.Report($"Couldn't change the refresh rate: {ex.Message}");
        }
        UpdateRefreshText();
        return result;
    }

    // ---- keyboard backlight timeout --------------------------------------------------------

    // Instance property so XAML can bind to it by path.
    public IReadOnlyList<Option<int>> TimeoutOptions => AllTimeoutOptions;
    private static readonly IReadOnlyList<Option<int>> AllTimeoutOptions =
    [
        new(0, "Never"), new(15, "15 seconds"), new(30, "30 seconds"), new(60, "1 minute"),
        new(120, "2 minutes"), new(300, "5 minutes"),
    ];

    public Option<int> BacklightTimeoutAc
    {
        get => TimeoutOptions.FirstOrDefault(o => o.Value == Saved.BacklightTimeoutAc) ?? TimeoutOptions[0];
        set { Saved.BacklightTimeoutAc = value?.Value ?? 0; _services.SaveSettings(); OnPropertyChanged(); }
    }

    public Option<int> BacklightTimeoutBattery
    {
        get => TimeoutOptions.FirstOrDefault(o => o.Value == Saved.BacklightTimeoutBattery) ?? TimeoutOptions[0];
        set { Saved.BacklightTimeoutBattery = value?.Value ?? 0; _services.SaveSettings(); OnPropertyChanged(); }
    }

    private void CheckIdle()
    {
        int timeout = IsOnAc() == false ? Saved.BacklightTimeoutBattery : Saved.BacklightTimeoutAc;
        if (timeout <= 0 && !_backlightDimmed) return;
        if (Saved.KeyboardBrightness <= 0) { _backlightDimmed = false; return; }

        double idle = IdleSeconds();
        bool shouldBeOff = timeout > 0 && idle >= timeout;
        if (shouldBeOff == _backlightDimmed) return;

        try
        {
            AsusAura.SetBrightness(shouldBeOff ? 0 : Saved.KeyboardBrightness, _services.Asus?.Acpi);
            _backlightDimmed = shouldBeOff;
        }
        catch
        {
            // Keyboard busy or gone (sleep); try again on the next tick.
        }
    }

    private static double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _idleTimer.Stop();
    }
}

/// <summary>Edits one <see cref="AsusPowerProfile"/>.</summary>
public sealed class PowerProfileViewModel(AsusPowerProfile profile, Action changed) : ObservableObject
{
    // Instance property so XAML can bind to it by path.
    public IReadOnlyList<Option<AsusPerformanceMode>> ModeOptions => AllModeOptions;
    private static readonly IReadOnlyList<Option<AsusPerformanceMode>> AllModeOptions =
    [
        new(null, "Leave as is"),
        new(AsusPerformanceMode.Silent, "Silent"),
        new(AsusPerformanceMode.Balanced, "Balanced"),
        new(AsusPerformanceMode.Turbo, "Turbo"),
    ];

    // Instance property so XAML can bind to it by path.
    public IReadOnlyList<Option<AsusGpuMode>> GpuOptions => AllGpuOptions;
    private static readonly IReadOnlyList<Option<AsusGpuMode>> AllGpuOptions =
    [
        new(null, "Leave as is"),
        new(AsusGpuMode.Eco, "Eco (dGPU off)"),
        new(AsusGpuMode.Standard, "Standard"),
    ];

    // Instance property so XAML can bind to it by path.
    public IReadOnlyList<Option<RefreshChoice>> RefreshOptions => AllRefreshOptions;
    private static readonly IReadOnlyList<Option<RefreshChoice>> AllRefreshOptions =
    [
        new(null, "Leave as is"),
        new(RefreshChoice.Lowest, "60 Hz"),
        new(RefreshChoice.Highest, "Highest"),
    ];

    public Option<AsusPerformanceMode> Mode
    {
        get => ModeOptions.First(o => o.Value == profile.Mode);
        set { profile.Mode = value?.Value; OnPropertyChanged(); changed(); }
    }

    public Option<AsusGpuMode> Gpu
    {
        get => GpuOptions.FirstOrDefault(o => o.Value == profile.Gpu) ?? GpuOptions[0];
        set { profile.Gpu = value?.Value; OnPropertyChanged(); changed(); }
    }

    public Option<RefreshChoice> Refresh
    {
        get => RefreshOptions.First(o => o.Value == profile.Refresh);
        set { profile.Refresh = value?.Value; OnPropertyChanged(); changed(); }
    }
}
