namespace AiO.Core.Mouse;

/// <summary>RawAccel acceleration styles (names match RawAccel's settings file).</summary>
public enum AccelMode
{
    NoAccel,
    Classic,
    Linear,
    Natural,
    Jump,
    Power,
    Synchronous,
    /// <summary>Look-up table: kept as-is when editing an existing RawAccel profile, not editable in AiO.</summary>
    Lut,
}

public enum CapMode
{
    /// <summary>Cap defined by an input speed and an output sensitivity.</summary>
    InOut,
    /// <summary>Cap at an input speed.</summary>
    Input,
    /// <summary>Cap at an output sensitivity.</summary>
    Output,
}

/// <summary>One axis of a RawAccel profile. Defaults are RawAccel's own.</summary>
public sealed record AccelParameters
{
    public AccelMode Mode { get; init; } = AccelMode.NoAccel;
    /// <summary>"Gain" mode (true) shapes the change in output, which feels smoother; false is legacy "velocity" mode.</summary>
    public bool Gain { get; init; } = true;
    public double InputOffset { get; init; }
    public double OutputOffset { get; init; }
    public double Acceleration { get; init; } = 0.005;
    public double DecayRate { get; init; } = 0.1;
    public double Gamma { get; init; } = 1;
    public double Motivity { get; init; } = 1.5;
    public double ExponentClassic { get; init; } = 2;
    public double Scale { get; init; } = 1;
    public double ExponentPower { get; init; } = 0.05;
    public double Limit { get; init; } = 1.5;
    public double SyncSpeed { get; init; } = 5;
    public double Smooth { get; init; } = 0.5;
    /// <summary>Cap (classic/power) or jump point (jump): X = input speed, Y = output sensitivity.</summary>
    public double CapX { get; init; } = 15;
    public double CapY { get; init; } = 1.5;
    public CapMode CapMode { get; init; } = CapMode.Output;
}

/// <summary>
/// Sensitivity as a function of input speed (counts/ms), ported from RawAccel's
/// common/accel-*.hpp (MIT, https://github.com/a1xd/rawaccel). Used to draw the preview graph;
/// RawAccel's driver does the real work.
/// </summary>
public static class AccelCurve
{
    /// <summary>Sensitivity multiplier at <paramref name="speed"/>, before the profile's output DPI scaling.</summary>
    public static double Sensitivity(AccelParameters p, double speed)
    {
        if (speed <= 0) speed = 1e-9;
        double result = p.Mode switch
        {
            AccelMode.Classic => Classic(p, speed),
            AccelMode.Linear => Classic(p with { ExponentClassic = 2 }, speed),
            AccelMode.Natural => Natural(p, speed),
            AccelMode.Jump => Jump(p, speed),
            AccelMode.Power => Power(p, speed),
            AccelMode.Synchronous => Synchronous(p, speed),
            _ => 1,
        };
        return double.IsFinite(result) ? result : 1;
    }

    /// <summary>Samples the curve for plotting.</summary>
    public static IReadOnlyList<(double Speed, double Sensitivity)> Sample(AccelParameters p, double maxSpeed, int points = 200)
    {
        var list = new List<(double, double)>(points);
        for (int i = 1; i <= points; i++)
        {
            double x = maxSpeed * i / points;
            list.Add((x, Sensitivity(p, x)));
        }
        return list;
    }

    // ---- classic ---------------------------------------------------------------

    private static double ClassicBase(double x, double accelRaised, AccelParameters p) =>
        accelRaised * Math.Pow(x - p.InputOffset, p.ExponentClassic) / x;

    private static double Classic(AccelParameters p, double x)
    {
        double power = p.ExponentClassic;
        if (power <= 1) return 1;

        if (!p.Gain)
        {
            double cap = double.MaxValue, sign = 1, accelRaised;
            switch (p.CapMode)
            {
                case CapMode.InOut:
                    cap = p.CapY - 1;
                    if (cap < 0) { cap = -cap; sign = -1; }
                    double a = Math.Pow(p.CapX * cap * Math.Pow(p.CapX - p.InputOffset, -power), 1 / (power - 1));
                    accelRaised = Math.Pow(a, power - 1);
                    break;
                case CapMode.Input:
                    accelRaised = Math.Pow(p.Acceleration, power - 1);
                    if (p.CapX > 0) cap = ClassicBase(p.CapX, accelRaised, p);
                    break;
                default:
                    accelRaised = Math.Pow(p.Acceleration, power - 1);
                    if (p.CapY > 0)
                    {
                        cap = p.CapY - 1;
                        if (cap < 0) { cap = -cap; sign = -1; }
                    }
                    break;
            }
            if (x <= p.InputOffset) return 1;
            return sign * Math.Min(ClassicBase(x, accelRaised, p), cap) + 1;
        }
        else
        {
            double capX = double.MaxValue, capY = double.MaxValue, constant = 0, sign = 1, accelRaised;
            switch (p.CapMode)
            {
                case CapMode.InOut:
                    capX = p.CapX;
                    capY = p.CapY - 1;
                    if (capY < 0) { capY = -capY; sign = -1; }
                    double a = -Math.Pow(capY / power, 1 / (power - 1)) / (p.InputOffset - capX);
                    accelRaised = Math.Pow(a, power - 1);
                    constant = (ClassicBase(capX, accelRaised, p) - capY) * capX;
                    break;
                case CapMode.Input:
                    accelRaised = Math.Pow(p.Acceleration, power - 1);
                    if (p.CapX > 0)
                    {
                        capX = p.CapX;
                        capY = power * Math.Pow(p.Acceleration * (capX - p.InputOffset), power - 1);
                        constant = (ClassicBase(capX, accelRaised, p) - capY) * capX;
                    }
                    break;
                default:
                    accelRaised = Math.Pow(p.Acceleration, power - 1);
                    if (p.CapY > 0)
                    {
                        capY = p.CapY - 1;
                        if (capY == 0) capX = 0;
                        else
                        {
                            if (capY < 0) { capY = -capY; sign = -1; }
                            capX = (p.Acceleration * p.InputOffset + Math.Pow(capY / power, 1 / (power - 1))) / p.Acceleration;
                            constant = (ClassicBase(capX, accelRaised, p) - capY) * capX;
                        }
                    }
                    break;
            }
            if (x <= p.InputOffset) return 1;
            double output = x < capX ? ClassicBase(x, accelRaised, p) : constant / x + capY;
            return sign * output + 1;
        }
    }

    // ---- natural ---------------------------------------------------------------

    private static double Natural(AccelParameters p, double x)
    {
        double offset = p.InputOffset;
        double limit = p.Limit - 1;
        if (limit == 0) return 1;
        double accel = p.DecayRate / Math.Abs(limit);
        if (x <= offset) return 1;

        double offsetX = offset - x;
        double decay = Math.Exp(accel * offsetX);
        if (!p.Gain) return limit * (1 - (offset - decay * offsetX) / x) + 1;

        double constant = -limit / accel;
        double output = limit * (decay / accel - offsetX) + constant;
        return output / x + 1;
    }

    // ---- jump --------------------------------------------------------------------

    private static double Jump(AccelParameters p, double x)
    {
        double stepX = p.CapX, stepY = p.CapY - 1;
        double rateInverse = p.Smooth * stepX;
        double smoothRate = rateInverse < 1 ? 0 : 2 * Math.PI / rateInverse;
        bool smooth = smoothRate != 0;

        double Decay(double v) => Math.Exp(smoothRate * (stepX - v));
        double Antideriv(double v) => stepY * (v + Math.Log(1 + Decay(v)) / smoothRate);

        if (!p.Gain)
        {
            if (smooth) return stepY / (1 + Decay(x)) + 1;
            return x < stepX ? 1 : 1 + stepY;
        }

        if (smooth) return 1 + (Antideriv(x) - Antideriv(0)) / x;
        return x < stepX ? 1 : 1 + stepY * (x - stepX) / x;
    }

    // ---- power -------------------------------------------------------------------

    private static double Power(AccelParameters p, double x)
    {
        double n = p.ExponentPower;
        if (n <= 0) return 1;

        double scale, offsetX = 0, offsetY = 0, constant = 0;
        bool legacyInOut = !p.Gain && p.CapMode == CapMode.InOut;

        if (p.CapMode != CapMode.InOut) scale = p.Scale;
        else if (p.Gain) scale = Math.Pow(p.CapY / (n + 1), 1 / n) / p.CapX;
        else scale = Math.Pow(p.CapY, 1 / n) / p.CapX;

        if (!legacyInOut)
        {
            offsetX = Math.Pow(p.OutputOffset / (n + 1), 1 / n) / scale;
            offsetY = p.OutputOffset;
            constant = offsetX * offsetY * n / (n + 1);
        }

        double Base(double v) => v <= offsetX ? offsetY : Math.Pow(scale * v, n) + constant / v;

        if (!p.Gain)
        {
            double cap = double.MaxValue;
            switch (p.CapMode)
            {
                case CapMode.InOut: cap = p.CapY; break;
                case CapMode.Input: if (p.CapX > 0) cap = Base(p.CapX); break;
                default: if (p.CapY > 0) cap = p.CapY; break;
            }
            return Math.Min(Base(x), cap);
        }

        double capX = double.MaxValue, capY = double.MaxValue;
        switch (p.CapMode)
        {
            case CapMode.InOut:
                capX = p.CapX;
                capY = p.CapY;
                break;
            case CapMode.Input when p.CapX > 0:
                if (p.CapX <= offsetX) return offsetY; // cap below the offset: flat curve
                capX = p.CapX;
                capY = (n + 1) * Math.Pow(capX * scale, n);
                break;
            case CapMode.Output when p.CapY > 0:
                capX = Math.Pow(p.CapY / (n + 1), 1 / n) / scale;
                capY = p.CapY;
                break;
        }
        if (capX == double.MaxValue) return Base(x);
        double constantB = (Base(capX) - capY) * capX;
        return x < capX ? Base(x) : capY + constantB / x;
    }

    // ---- synchronous ---------------------------------------------------------------

    private static double SyncActivation(AccelParameters p, double x)
    {
        double logMotivity = Math.Log(p.Motivity);
        if (logMotivity == 0) return 1;
        double gammaConst = p.Gamma / logMotivity;
        double logSync = Math.Log(p.SyncSpeed);
        double sharpness = p.Smooth == 0 ? 16 : 0.5 / p.Smooth;

        if (x <= 0) return 1 / p.Motivity;
        if (sharpness >= 16)
        {
            double logSpace = gammaConst * (Math.Log(x) - logSync);
            if (logSpace < -1) return 1 / p.Motivity;
            if (logSpace > 1) return p.Motivity;
            return Math.Exp(logSpace * logMotivity);
        }
        if (x == p.SyncSpeed) return 1;
        double logDiff = Math.Log(x) - logSync;
        double space = gammaConst * Math.Abs(logDiff);
        double exponent = Math.Pow(Math.Tanh(Math.Pow(space, sharpness)), 1 / sharpness);
        return Math.Exp((logDiff > 0 ? exponent : -exponent) * logMotivity);
    }

    private static double Synchronous(AccelParameters p, double x)
    {
        if (p.Motivity <= 1 || p.SyncSpeed <= 0) return 1;
        if (!p.Gain) return SyncActivation(p, x);

        // Gain mode: sensitivity is the average of the activation curve from 0 to x.
        const int steps = 256;
        double sum = 0, h = x / steps;
        for (int i = 0; i < steps; i++) sum += SyncActivation(p, (i + 0.5) * h) * h;
        return sum / x;
    }
}
