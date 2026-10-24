using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Mouse;

namespace AiO.App.ViewModels;

public sealed class MouseViewModel : ObservableObject
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Debouncer _speedDebounce = new(TimeSpan.FromMilliseconds(150));
    private RawAccelProfile _profile = new();
    private double _pointerSpeed;
    private bool _epp;
    private bool _driverInstalled;
    private IReadOnlyList<(double Speed, double Sensitivity)> _curve = [];

    public MouseViewModel()
    {
        _pointerSpeed = Try(WindowsMouse.GetSpeed, 10);
        _epp = Try(WindowsMouse.GetEnhancePointerPrecision, false);

        RecommendedWindowsCommand = new RelayCommand(() =>
        {
            PointerSpeed = 10;
            EnhancePointerPrecision = false;
            _services.Report("Windows pointer set to 10/20 with Enhance Pointer Precision off, as RawAccel recommends.");
        });
        GetPackageCommand = new AsyncCommand(GetPackageAsync);
        InstallCommand = new AsyncCommand(() => RunInstallerAsync(uninstall: false));
        UninstallCommand = new AsyncCommand(() => RunInstallerAsync(uninstall: true));
        ApplyCommand = new AsyncCommand(ApplyAsync);
        ReloadCommand = new RelayCommand(LoadProfile);
        ResetCommand = new RelayCommand(() => Profile = new RawAccelProfile { Accel = new AccelParameters { Mode = Mode } });

        RefreshStatus();
        LoadProfile();
    }

    private static T Try<T>(Func<T> f, T fallback)
    {
        try { return f(); } catch { return fallback; }
    }

    // ---- Windows pointer -------------------------------------------------------

    public double PointerSpeed
    {
        get => _pointerSpeed;
        set
        {
            if (!Set(ref _pointerSpeed, value)) return;
            _speedDebounce.Run(() => WindowsMouse.SetSpeed((int)value));
            OnPropertyChanged(nameof(WindowsAdvice));
        }
    }

    public bool EnhancePointerPrecision
    {
        get => _epp;
        set
        {
            if (!Set(ref _epp, value)) return;
            WindowsMouse.SetEnhancePointerPrecision(value);
            OnPropertyChanged(nameof(WindowsAdvice));
        }
    }

    public string WindowsAdvice => (_epp, (int)_pointerSpeed) switch
    {
        (true, _) => "Enhance Pointer Precision is Windows' own acceleration. Turn it off when using RawAccel, or the two stack.",
        (false, not 10) => "Speeds other than 10 skip or repeat pixels. Leave this at 10 and set sensitivity in RawAccel instead.",
        _ => "Good: no Windows scaling or acceleration in the way.",
    };

    public ICommand RecommendedWindowsCommand { get; }

    // ---- Driver -------------------------------------------------------------------

    private Core.Settings.MouseSettings Saved => _services.Settings.Current.Mouse;
    private bool _busy;

    public bool PackageReady => RawAccel.PackageFolder is not null;
    public bool DriverInstalled { get => _driverInstalled; private set => Set(ref _driverInstalled, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public bool ShowGetPackage => !PackageReady;
    public bool ShowInstall => PackageReady && !DriverInstalled;

    public string Status => (PackageReady, DriverInstalled) switch
    {
        (_, true) => "Acceleration driver running. Changes below apply to every app and game, including raw-input games.",
        (true, false) => "One-time setup: install the acceleration driver, then restart Windows.",
        _ => "This AiO build doesn't include the acceleration driver files. Get them with one click (about 1 MB).",
    };

    public bool ReapplyOnStartup
    {
        get => Saved.ReapplyRawAccelOnStartup;
        set { Saved.ReapplyRawAccelOnStartup = value; _services.SaveSettings(); OnPropertyChanged(); }
    }

    public ICommand GetPackageCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }

    private void RefreshStatus()
    {
        DriverInstalled = Try(RawAccel.IsDriverInstalled, false);
        foreach (var name in new[] { nameof(PackageReady), nameof(ShowGetPackage), nameof(ShowInstall), nameof(Status) })
            OnPropertyChanged(name);
    }

    private async Task GetPackageAsync()
    {
        Busy = true;
        try
        {
            await RawAccel.DownloadPackageAsync();
            _services.Report("Driver files ready. Next: install the driver.");
        }
        catch (Exception ex)
        {
            _services.Report($"Couldn't get the driver files: {ex.Message}");
        }
        finally
        {
            Busy = false;
            RefreshStatus();
        }
    }

    private async Task RunInstallerAsync(bool uninstall)
    {
        Busy = true;
        try
        {
            var result = await Task.Run(() => RawAccel.RunInstaller(uninstall));
            _services.Report(result.Message);
            if (result.Success && Views.Dialogs.Confirm($"{result.Message}\n\nRestart now?")) SystemIntegration.RestartWindows();
        }
        finally
        {
            Busy = false;
            RefreshStatus();
        }
    }

    // ---- Profile -----------------------------------------------------------------

    public ICommand ApplyCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ResetCommand { get; }

    private RawAccelProfile Profile
    {
        get => _profile;
        set
        {
            _profile = value;
            foreach (var name in AllProfileProperties) OnPropertyChanged(name);
            UpdateCurve();
        }
    }

    private void LoadProfile()
    {
        Profile = Saved.Profile ?? _profile;
    }

    private async Task ApplyAsync()
    {
        var profile = _profile;
        Saved.Profile = profile;
        _services.SaveSettings();
        var result = await Task.Run(() => RawAccel.Apply(profile));
        _services.Report(result.Message);
    }

    private AccelParameters A => _profile.Accel;

    private void Edit(AccelParameters accel, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _profile = _profile with { Accel = accel };
        OnPropertyChanged(name);
        UpdateCurve();
    }

    private void EditProfile(RawAccelProfile profile, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _profile = profile;
        OnPropertyChanged(name);
        UpdateCurve();
    }

    public IReadOnlyList<AccelMode> Modes { get; } =
        [AccelMode.NoAccel, AccelMode.Linear, AccelMode.Classic, AccelMode.Natural, AccelMode.Synchronous, AccelMode.Power, AccelMode.Jump];

    public IReadOnlyList<CapMode> CapModes { get; } = Enum.GetValues<CapMode>();

    public AccelMode Mode
    {
        get => A.Mode;
        set
        {
            Edit(A with { Mode = value });
            foreach (var name in VisibilityProperties) OnPropertyChanged(name);
        }
    }

    public bool IsLut => A.Mode == AccelMode.Lut;
    public bool Gain { get => A.Gain; set => Edit(A with { Gain = value }); }
    public double Acceleration { get => A.Acceleration; set => Edit(A with { Acceleration = value }); }
    public double ExponentClassic { get => A.ExponentClassic; set => Edit(A with { ExponentClassic = value }); }
    public double InputOffset { get => A.InputOffset; set => Edit(A with { InputOffset = value }); }
    public double OutputOffset { get => A.OutputOffset; set => Edit(A with { OutputOffset = value }); }
    public double CapX { get => A.CapX; set => Edit(A with { CapX = value }); }
    public double CapY { get => A.CapY; set => Edit(A with { CapY = value }); }
    public CapMode CapMode
    {
        get => A.CapMode;
        set { Edit(A with { CapMode = value }); OnPropertyChanged(nameof(ShowCapX)); OnPropertyChanged(nameof(ShowCapY)); OnPropertyChanged(nameof(ShowAcceleration)); }
    }
    public double DecayRate { get => A.DecayRate; set => Edit(A with { DecayRate = value }); }
    public double Limit { get => A.Limit; set => Edit(A with { Limit = value }); }
    public double Scale { get => A.Scale; set => Edit(A with { Scale = value }); }
    public double ExponentPower { get => A.ExponentPower; set => Edit(A with { ExponentPower = value }); }
    public double SyncSpeed { get => A.SyncSpeed; set => Edit(A with { SyncSpeed = value }); }
    public double Motivity { get => A.Motivity; set => Edit(A with { Motivity = value }); }
    public double Gamma { get => A.Gamma; set => Edit(A with { Gamma = value }); }
    public double Smooth { get => A.Smooth; set => Edit(A with { Smooth = value }); }

    public double Sensitivity
    {
        get => _profile.SensitivityMultiplier;
        set => EditProfile(_profile with { OutputDpi = Math.Round(value * 1000, 1) });
    }
    public double YxRatio { get => _profile.YxRatio; set => EditProfile(_profile with { YxRatio = value }); }
    public double Rotation { get => _profile.RotationDegrees; set => EditProfile(_profile with { RotationDegrees = value }); }
    public double AngleSnap { get => _profile.AngleSnapDegrees; set => EditProfile(_profile with { AngleSnapDegrees = value }); }

    // Which parameters each style uses (as in RawAccel's GUI).
    private bool Is(params AccelMode[] modes) => modes.Contains(A.Mode);
    public bool HasCurve => !Is(AccelMode.NoAccel, AccelMode.Lut);
    public bool ShowGain => HasCurve;
    public bool ShowAcceleration => Is(AccelMode.Classic, AccelMode.Linear) && A.CapMode != CapMode.InOut;
    public bool ShowExponentClassic => Is(AccelMode.Classic);
    public bool ShowInputOffset => Is(AccelMode.Classic, AccelMode.Linear, AccelMode.Natural);
    public bool ShowOutputOffset => Is(AccelMode.Power);
    public bool ShowCapMode => Is(AccelMode.Classic, AccelMode.Linear, AccelMode.Power);
    public bool ShowCapX => (ShowCapMode && A.CapMode != CapMode.Output) || Is(AccelMode.Jump);
    public bool ShowCapY => (ShowCapMode && A.CapMode != CapMode.Input) || Is(AccelMode.Jump);
    public bool ShowNatural => Is(AccelMode.Natural);
    public bool ShowPower => Is(AccelMode.Power) && A.CapMode != CapMode.InOut;
    public bool ShowPowerExponent => Is(AccelMode.Power);
    public bool ShowSync => Is(AccelMode.Synchronous);
    public bool ShowSmooth => Is(AccelMode.Synchronous, AccelMode.Jump);
    public string CapXLabel => Is(AccelMode.Jump) ? "Jump at speed" : "Cap at speed";
    public string CapYLabel => Is(AccelMode.Jump) ? "Jump to sensitivity" : "Cap sensitivity";

    private static readonly string[] VisibilityProperties =
    [
        nameof(HasCurve), nameof(ShowGain), nameof(ShowAcceleration), nameof(ShowExponentClassic), nameof(ShowInputOffset),
        nameof(ShowOutputOffset), nameof(ShowCapMode), nameof(ShowCapX), nameof(ShowCapY), nameof(ShowNatural), nameof(ShowPower),
        nameof(ShowPowerExponent), nameof(ShowSync), nameof(ShowSmooth), nameof(CapXLabel), nameof(CapYLabel), nameof(IsLut),
    ];

    private static readonly string[] AllProfileProperties =
    [
        nameof(Mode), nameof(Gain), nameof(Acceleration), nameof(ExponentClassic), nameof(InputOffset), nameof(OutputOffset),
        nameof(CapX), nameof(CapY), nameof(CapMode), nameof(DecayRate), nameof(Limit), nameof(Scale), nameof(ExponentPower),
        nameof(SyncSpeed), nameof(Motivity), nameof(Gamma), nameof(Smooth), nameof(Sensitivity), nameof(YxRatio), nameof(Rotation),
        nameof(AngleSnap), .. VisibilityProperties,
    ];

    // ---- Graph ---------------------------------------------------------------------

    public const double GraphMaxSpeed = 100;

    /// <summary>Sensitivity (including the multiplier) against input speed in counts/ms.</summary>
    public IReadOnlyList<(double Speed, double Sensitivity)> Curve { get => _curve; private set => Set(ref _curve, value); }

    private void UpdateCurve()
    {
        double multiplier = _profile.SensitivityMultiplier;
        Curve = AccelCurve.Sample(A, GraphMaxSpeed, 160).Select(p => (p.Speed, p.Sensitivity * multiplier)).ToList();
    }
}
