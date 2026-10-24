using System.Security.Principal;
using AiO.Core.Asus;
using AiO.Core.Gpu;
using AiO.Core.Settings;

namespace AiO.App.Infrastructure;

/// <summary>Process-wide services: settings, hardware backends, and the status line.</summary>
public sealed class AppServices : IDisposable
{
    public static AppServices Instance { get; } = new();

    public SettingsStore Settings { get; } = new();

    private (AsusLaptop? Laptop, AsusDetection Detection)? _asus;
    private readonly Lazy<IReadOnlyList<IGpuColorControl>> _gpu = new(GpuColorControls.LoadAll);

    private (AsusLaptop? Laptop, AsusDetection Detection) AsusState => _asus ??= AsusLaptop.Detect();

    /// <summary>Null on machines without the ASUS ATKACPI driver.</summary>
    public AsusLaptop? Asus => AsusState.Laptop;

    /// <summary>What was found when looking for the ASUS driver (shown when <see cref="Asus"/> is null).</summary>
    public AsusDetection AsusDetection => AsusState.Detection;

    /// <summary>Set when the driver opened but building the ASUS page failed.</summary>
    public string? AsusLoadError { get; set; }

    /// <summary>Closes and re-opens the ASUS driver.</summary>
    public void ReloadAsus()
    {
        _asus?.Laptop?.Dispose();
        _asus = null;
        AsusLoadError = null;
    }

    public IReadOnlyList<IGpuColorControl> GpuColor => _gpu.Value;

    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public event Action<string>? StatusReported;

    /// <summary>The latest message, so the window can show anything reported before it existed.</summary>
    public string LastStatus { get; private set; } = "";

    public void Report(string message)
    {
        LastStatus = message;
        StatusReported?.Invoke(message);
    }

    public void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) { Report($"Could not save settings: {ex.Message}"); }
    }

    public void Dispose()
    {
        _asus?.Laptop?.Dispose();
        if (_gpu.IsValueCreated)
            foreach (var g in _gpu.Value) g.Dispose();
    }
}
