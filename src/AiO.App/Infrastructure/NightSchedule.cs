using System.Windows.Threading;
using AiO.Core.Color;
using AiO.Core.Display;

namespace AiO.App.Infrastructure;

/// <summary>
/// Warms every display's colour temperature between the scheduled times (like Night Light, but on
/// top of AiO's own colour settings). Outside the window each display gets its saved colours back.
/// </summary>
public sealed class NightSchedule
{
    public static NightSchedule Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _active;

    private NightSchedule() => _timer.Tick += (_, _) => Update();

    public bool IsActive => _active;

    /// <summary>Raised when night mode starts or ends.</summary>
    public event Action? Changed;

    public void Start()
    {
        _timer.Start();
        Update();
    }

    /// <summary>Re-checks the schedule now (after its settings change).</summary>
    public void Update()
    {
        var night = AppServices.Instance.Settings.Current.Night;
        var now = DateTime.Now;
        bool want = night.Enabled && night.IsNight(now.Hour * 60 + now.Minute);
        if (!want && !_active) return;
        try
        {
            foreach (var display in DisplayManager.GetDisplays())
            {
                var saved = AppServices.Instance.Settings.Current.Displays.TryGetValue(display.DeviceName, out var s)
                    ? s.Gamma : ColorAdjustment.Neutral;
                GammaController.Apply(display.DeviceName, want ? Warm(saved, night.Temperature) : saved);
            }
        }
        catch (Exception ex)
        {
            AppServices.Instance.Report($"Night colours: {ex.Message}");
        }
        if (want != _active)
        {
            _active = want;
            Changed?.Invoke();
        }
    }

    /// <summary>Never makes a display cooler than the user set it.</summary>
    public static ColorAdjustment Warm(ColorAdjustment saved, int temperature) =>
        saved with { Temperature = Math.Min(saved.Temperature, temperature) };
}
