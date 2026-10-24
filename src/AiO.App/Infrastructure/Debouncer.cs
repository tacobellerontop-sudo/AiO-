using System.Windows.Threading;

namespace AiO.App.Infrastructure;

/// <summary>Runs an action once input has been quiet for a moment (e.g. while a slider is dragged).</summary>
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private Action? _pending;

    public Debouncer(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var action = _pending;
            _pending = null;
            action?.Invoke();
        };
    }

    public void Run(Action action)
    {
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }
}
