using AiO.Core.Hid;

namespace AiO.Core.Razer;

public enum RazerDeviceType
{
    Unknown,
    Mouse,
    Keyboard,
    Headset,
    Accessory,
}

public static partial class RazerDeviceCatalog
{
    public const ushort VendorId = 0x1532;

    public static (RazerDeviceType Type, string Name) Describe(ushort productId, string productString) =>
        Known.TryGetValue(productId, out var known)
            ? known
            : (RazerDeviceType.Unknown, string.IsNullOrWhiteSpace(productString) ? $"Razer device 0x{productId:X4}" : productString);
}

public sealed class RazerCommandException(string message) : Exception(message);

/// <summary>
/// Talks to one Razer device directly over its HID control interface, without Synapse.
/// </summary>
public sealed class RazerDevice : IDisposable
{
    // Different device generations expect different transaction ids; we probe these in order.
    // HyperSpeed receivers mostly answer on 0x1F; 0x10 is used by some Viper dongles for button commands.
    private static readonly byte[] TransactionIdCandidates = [0x1F, 0x3F, 0xFF, 0x08];
    private static readonly byte[] ButtonTransactionIdCandidates = [0x1F, 0x3F, 0xFF, 0x08, 0x10];

    private readonly HidConnection _connection;
    private readonly object _lock = new();
    private byte _transactionId;
    private byte? _buttonTransactionId;

    public ushort ProductId => _connection.Info.ProductId;
    public string Name { get; }
    public RazerDeviceType Type { get; }
    public string FirmwareVersion { get; private set; } = "";

    private RazerDevice(HidConnection connection, string name, RazerDeviceType type)
    {
        _connection = connection;
        Name = name;
        Type = type;
    }

    /// <summary>Finds every Razer device that answers on a control interface.</summary>
    public static IReadOnlyList<RazerDevice> DiscoverAll()
    {
        var devices = new List<RazerDevice>();
        // The control interface is the one with a 91-byte feature report (report id + 90 bytes).
        var candidates = HidEnumerator.Enumerate(RazerDeviceCatalog.VendorId)
            .Where(d => d.FeatureReportLength == RazerReport.Size + 1)
            .GroupBy(d => d.ProductId);

        foreach (var group in candidates)
        {
            foreach (var info in group.OrderBy(d => d.InterfaceNumber))
            {
                var device = TryOpen(info);
                if (device is null) continue;
                devices.Add(device);
                break;
            }
        }
        return devices;
    }

    private static RazerDevice? TryOpen(HidDeviceInfo info)
    {
        HidConnection connection;
        try { connection = HidConnection.Open(info); }
        catch { return null; }

        var (type, name) = RazerDeviceCatalog.Describe(info.ProductId, info.ProductName);
        var device = new RazerDevice(connection, name, type);
        foreach (byte id in TransactionIdCandidates)
        {
            device._transactionId = id;
            try
            {
                var response = device.Send(RazerCommands.GetFirmwareVersion());
                device.FirmwareVersion = $"v{response.Arguments[0]}.{response.Arguments[1]}";
                return device;
            }
            catch
            {
                // Try the next transaction id.
            }
        }
        device.Dispose();
        return null;
    }

    /// <summary>Sends a command and returns the device's response; throws unless it reports success.</summary>
    public RazerReport Send(RazerReport request) => Send(request, _transactionId);

    private RazerReport Send(RazerReport request, byte transactionId)
    {
        lock (_lock)
        {
            request.TransactionId = transactionId;
            _connection.SetFeature(request.ToFeatureReport());

            var buffer = new byte[RazerReport.Size + 1];
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 2 : 10);
                buffer[0] = 0;
                _connection.GetFeature(buffer);
                var response = RazerReport.ParseFeatureReport(buffer);

                if (response.Status == RazerStatus.Busy) continue;
                if (!response.IsResponseTo(request))
                    throw new RazerCommandException("Device answered a different command");
                return response.Status switch
                {
                    RazerStatus.Successful => response,
                    RazerStatus.NotSupported => throw new RazerCommandException("Command not supported by this device"),
                    _ => throw new RazerCommandException($"Device returned {response.Status}"),
                };
            }
            throw new RazerCommandException("Device stayed busy");
        }
    }

    private T? TryGet<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch { return null; }
    }

    public (int X, int Y)? GetDpi() => TryGet(() => RazerCommands.ParseDpi(Send(RazerCommands.GetDpi())));

    public void SetDpi(int x, int y) => Send(RazerCommands.SetDpi(x, y));

    /// <summary>True for newer mice that use the "hyperpolling" command (rates above 1000 Hz).</summary>
    public bool SupportsHyperPolling { get; private set; }

    public IReadOnlyList<int> PollingRates => SupportsHyperPolling ? [125, 250, 500, 1000, 2000, 4000, 8000] : [125, 500, 1000];

    public int? GetPollingRate()
    {
        if (TryGet(() => RazerCommands.ParsePollingRate2(Send(RazerCommands.GetPollingRate2()))) is int hyper and > 0)
        {
            SupportsHyperPolling = true;
            return hyper;
        }
        return TryGet(() => RazerCommands.ParsePollingRate(Send(RazerCommands.GetPollingRate())));
    }

    public void SetPollingRate(int hz)
    {
        if (!SupportsHyperPolling)
        {
            Send(RazerCommands.SetPollingRate(hz));
            return;
        }
        // Razer's own software sends the hyperpolling command twice, with argument 0 and 1.
        Send(RazerCommands.SetPollingRate2(hz, 0x00));
        try { Send(RazerCommands.SetPollingRate2(hz, 0x01)); }
        catch (RazerCommandException) { }
    }

    public (int Active, IReadOnlyList<int> Stages)? GetDpiStages()
    {
        try
        {
            var stages = RazerCommands.ParseDpiStages(Send(RazerCommands.GetDpiStages()));
            return stages.Stages.Count > 0 ? stages : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetDpiStages(IReadOnlyList<int> stages, int activeStage) => Send(RazerCommands.SetDpiStages(stages, activeStage));

    public int? GetIdleSeconds() => TryGet(() => RazerCommands.ParseIdleTime(Send(RazerCommands.GetIdleTime())));

    public void SetIdleSeconds(int seconds) => Send(RazerCommands.SetIdleTime(seconds));

    public int? GetLowBatteryPercent() => TryGet(() => RazerCommands.ParseLowBatteryPercent(Send(RazerCommands.GetLowBatteryThreshold())));

    public void SetLowBatteryPercent(int percent) => Send(RazerCommands.SetLowBatteryThreshold(percent));

    private static readonly RazerLed[] CandidateLeds =
        [RazerLed.Zero, RazerLed.Logo, RazerLed.ScrollWheel, RazerLed.Backlight, RazerLed.Underglow, RazerLed.LeftSide, RazerLed.RightSide];

    /// <summary>
    /// Lighting zones this device actually has, found by asking each zone for its brightness
    /// (a zone that doesn't exist answers "not supported"). Falls back to typical zones.
    /// </summary>
    public IReadOnlyList<RazerLed> DetectLeds()
    {
        var found = CandidateLeds.Where(led => GetBrightness(led) is not null).ToList();
        return found.Count > 0 ? found : SuggestedLeds;
    }

    public int? GetBatteryPercent() => TryGet(() => RazerCommands.ParseBatteryPercent(Send(RazerCommands.GetBatteryLevel())));

    public bool? IsCharging() => TryGet(() => Send(RazerCommands.GetChargingStatus()).Arguments[1] == 1);

    public int? GetBrightness(RazerLed led) => TryGet(() => (int)Send(RazerCommands.GetBrightness(led)).Arguments[2]);

    public void SetBrightness(RazerLed led, byte value) => Send(RazerCommands.SetBrightness(led, value));

    public void SetEffect(RazerLed led, RazerEffect effect, IReadOnlyList<RgbColor>? colors = null) =>
        Send(RazerCommands.SetEffect(led, effect, colors));

    /// <summary>LED ids worth offering for this device type, in display order.</summary>
    public IReadOnlyList<RazerLed> SuggestedLeds => Type switch
    {
        RazerDeviceType.Keyboard => [RazerLed.Backlight, RazerLed.Logo],
        RazerDeviceType.Mouse => [RazerLed.Zero, RazerLed.Logo, RazerLed.ScrollWheel, RazerLed.Backlight],
        _ => [RazerLed.Zero, RazerLed.Backlight, RazerLed.Logo],
    };

    // ---- Button assignment (onboard) ----------------------------------------------------

    /// <summary>
    /// Sends a button-assignment command. A HyperSpeed receiver can want a different transaction id for
    /// this command class than the one that answered the firmware probe, so on the first failure each
    /// candidate is tried and the one that works is remembered.
    /// </summary>
    private RazerReport SendButtonCommand(RazerReport request)
    {
        if (_buttonTransactionId is byte known) return Send(request, known);

        RazerCommandException? last = null;
        foreach (byte id in ButtonTransactionIdCandidates.Prepend(_transactionId).Distinct())
        {
            try
            {
                var response = Send(request, id);
                _buttonTransactionId = id;
                return response;
            }
            catch (RazerCommandException ex) { last = ex; }
        }
        throw last ?? new RazerCommandException("No answer to the button command");
    }

    /// <summary>The function a button has now, or null if the device has no such button.</summary>
    public RazerButtonAction? GetButton(byte button, RazerButtonLayer layer)
    {
        try { return RazerButtonCommands.Parse(SendButtonCommand(RazerButtonCommands.Get(button, layer)), button); }
        catch (RazerCommandException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>
    /// Buttons this mouse answers for, with their current function on <paramref name="layer"/>.
    /// Known ids come first; then every other id up to <see cref="RazerButton.ScanLimit"/> is asked, so
    /// buttons AiO has no name for (thumb/clutch buttons on some models) still show up.
    /// </summary>
    public IReadOnlyList<(RazerButton Button, RazerButtonAction Action)> ReadButtons(RazerButtonLayer layer)
    {
        if (Type is not (RazerDeviceType.Mouse or RazerDeviceType.Unknown)) return [];
        // Ask about left click first: if it doesn't answer, the mouse doesn't support the command at all.
        if (GetButton(RazerButton.LeftId, layer) is not { } left) return [];

        var found = new List<(RazerButton, RazerButtonAction)> { (RazerButton.Candidates[0], left) };
        foreach (var button in RazerButton.Candidates.Skip(1))
            if (GetButton(button.Id, layer) is { } action) found.Add((button, action));

        var known = RazerButton.Candidates.Select(b => b.Id).ToHashSet();
        for (int id = 1; id <= RazerButton.ScanLimit; id++)
        {
            byte scanId = (byte)id;
            if (known.Contains(scanId) || RazerButton.NeverTouch.Contains(scanId)) continue;
            if (GetButton(scanId, layer) is { IsEmpty: false } scanned)
                found.Add((new RazerButton(scanId, $"Button 0x{scanId:X2}"), scanned));
        }
        return found;
    }

    private bool _modeChecked;

    /// <summary>
    /// Onboard button functions only work in normal mode. Synapse leaves mice in driver mode, where the
    /// mouse sends raw buttons and Synapse does the remapping, so AiO switches it back once.
    /// </summary>
    private void EnsureNormalMode()
    {
        if (_modeChecked) return;
        _modeChecked = true;
        try
        {
            var mode = Send(RazerCommands.GetDeviceMode()).Arguments[0];
            if (mode != RazerCommands.ModeNormal) Send(RazerCommands.SetDeviceMode(RazerCommands.ModeNormal));
        }
        catch (RazerCommandException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// Writes a button's function to the mouse's onboard memory and reads it back. If the read-back
    /// doesn't match, the previous function is written back and an exception is thrown.
    /// </summary>
    public void SetButton(byte button, RazerButtonLayer layer, RazerButtonAction action)
    {
        EnsureNormalMode();
        var before = GetButton(button, layer);
        SendButtonCommand(RazerButtonCommands.Set(button, layer, action));
        var after = GetButton(button, layer);
        if (after is not null && after.SameAs(action)) return;

        if (before is not null)
        {
            try { SendButtonCommand(RazerButtonCommands.Set(button, layer, before)); }
            catch (RazerCommandException) { }
        }
        throw new RazerCommandException($"{RazerButton.NameOf(button)} didn't keep the new function, so it was put back");
    }

    public void Dispose() => _connection.Dispose();
}
