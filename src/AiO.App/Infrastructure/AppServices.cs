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

    private readonly Lazy<(AsusLaptop? Laptop, AsusDetection Detection)> _asus = new(AsusLaptop.Detect);
    private readonly Lazy<IReadOnlyList<IGpuColorControl>> _gpu = new(GpuColorControls.LoadAll);

    /// <summary>Null on machines without the ASUS ATKACPI driver.</summary>
    public AsusLaptop? Asus => _asus.Value.Laptop;

    /// <summary>What was found when looking for the ASUS driver (shown when <see cref="Asus"/> is null).</summary>
    public AsusDetection AsusDetection => _asus.Value.Detection;

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
        if (_asus.IsValueCreated) _asus.Value.Laptop?.Dispose();
        if (_gpu.IsValueCreated)
            foreach (var g in _gpu.Value) g.Dispose();
    }
}
