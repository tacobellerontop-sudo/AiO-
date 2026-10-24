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
    private static readonly byte[] TransactionIdCandidates = [0x1F, 0x3F, 0xFF, 0x08];

    private readonly HidConnection _connection;
    private readonly object _lock = new();
    private byte _transactionId;

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
    public RazerReport Send(RazerReport request)
    {
        lock (_lock)
        {
            request.TransactionId = _transactionId;
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

    public int? GetPollingRate() => TryGet(() => RazerCommands.ParsePollingRate(Send(RazerCommands.GetPollingRate())));

    public void SetPollingRate(int hz) => Send(RazerCommands.SetPollingRate(hz));

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

    public void Dispose() => _connection.Dispose();
}
