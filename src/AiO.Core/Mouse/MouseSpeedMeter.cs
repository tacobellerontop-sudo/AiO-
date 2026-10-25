namespace AiO.Core.Mouse;

/// <summary>
/// Turns raw mouse movement into a smoothed input speed in counts per millisecond
/// (the unit RawAccel's curves use). Thread-safe: samples arrive on the input thread,
/// readings happen on the UI thread.
/// </summary>
public sealed class MouseSpeedMeter
{
    private readonly object _lock = new();
    private readonly double _windowMs;
    private readonly Queue<(double TimeMs, double Distance)> _samples = new();
    private double _distance;
    private double _peak;

    /// <param name="windowMs">Speed is averaged over this much recent movement.</param>
    public MouseSpeedMeter(double windowMs = 25) => _windowMs = windowMs;

    public void Add(int dx, int dy, double timeMs)
    {
        double d = Math.Sqrt((double)dx * dx + (double)dy * dy);
        lock (_lock)
        {
            _samples.Enqueue((timeMs, d));
            _distance += d;
            Trim(timeMs);
        }
    }

    /// <summary>Current speed in counts/ms; falls to 0 once the mouse stops.</summary>
    public double Speed(double nowMs)
    {
        lock (_lock)
        {
            Trim(nowMs);
            double speed = _distance / _windowMs;
            if (speed > _peak) _peak = speed;
            return speed;
        }
    }

    /// <summary>Highest speed seen since the last <see cref="ResetPeak"/>.</summary>
    public double Peak
    {
        get { lock (_lock) return _peak; }
    }

    public void ResetPeak()
    {
        lock (_lock) _peak = 0;
    }

    private void Trim(double nowMs)
    {
        while (_samples.Count > 0 && _samples.Peek().TimeMs < nowMs - _windowMs)
            _distance -= _samples.Dequeue().Distance;
        if (_samples.Count == 0) _distance = 0; // avoid floating-point drift
    }
}
