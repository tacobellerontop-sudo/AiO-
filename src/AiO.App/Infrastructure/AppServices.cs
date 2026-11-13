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

    /// <summary>Set when the driver opened but building the Laptop settings page failed.</summary>
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

    /// <summary>Raised for <see cref="ShowOsd"/>: glyph (Segoe icon) and text.</summary>
    public event Action<string, string>? OsdRequested;

    /// <summary>Shows a short on-screen popup (if enabled) and puts the text in the status line.</summary>
    public void ShowOsd(string glyph, string text)
    {
        Report(text);
        if (Settings.Current.General.ShowOsd) OsdRequested?.Invoke(glyph, text);
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
