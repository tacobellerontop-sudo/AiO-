using System.Net.Sockets;
using AiO.Core.Razer;

namespace AiO.Core.OpenRgb;

/// <summary>
/// Client for the OpenRGB SDK server (OpenRGB → Settings → SDK Server, default port 6742).
/// Lets AiO drive lighting on every device OpenRGB supports, Razer included.
/// </summary>
public sealed class OpenRgbClient : IAsyncDisposable
{
    public const int DefaultPort = 6742;

    private readonly TcpClient _tcp = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NetworkStream? _stream;

    public uint ProtocolVersion { get; private set; }
    public bool IsConnected => _tcp.Connected;

    public async Task ConnectAsync(string host = "127.0.0.1", int port = DefaultPort, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await _tcp.ConnectAsync(host, port, timeout.Token);
        _stream = _tcp.GetStream();

        await SendAsync(0, OpenRgbPacket.SetClientName, OpenRgbProtocol.BuildClientName("AiO"), ct);

        await SendAsync(0, OpenRgbPacket.RequestProtocolVersion, OpenRgbProtocol.BuildUInt32(OpenRgbProtocol.ClientVersion), ct);
        try
        {
            // Protocol-0 servers never answer this request.
            using var versionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            versionTimeout.CancelAfter(TimeSpan.FromSeconds(1));
            var reply = await ReceiveAsync(OpenRgbPacket.RequestProtocolVersion, versionTimeout.Token);
            ProtocolVersion = Math.Min(OpenRgbProtocol.ClientVersion, BitConverter.ToUInt32(reply));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ProtocolVersion = 0;
        }
    }

    public async Task<IReadOnlyList<OpenRgbController>> GetControllersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await SendAsync(0, OpenRgbPacket.RequestControllerCount, [], ct);
            uint count = BitConverter.ToUInt32(await ReceiveAsync(OpenRgbPacket.RequestControllerCount, ct));

            var controllers = new List<OpenRgbController>();
            for (uint i = 0; i < count; i++)
            {
                byte[] request = ProtocolVersion == 0 ? [] : OpenRgbProtocol.BuildUInt32(ProtocolVersion);
                await SendAsync(i, OpenRgbPacket.RequestControllerData, request, ct);
                var data = await ReceiveAsync(OpenRgbPacket.RequestControllerData, ct);
                controllers.Add(OpenRgbProtocol.ParseController(data, ProtocolVersion, (int)i));
            }
            return controllers;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Switches to direct/custom mode and paints every LED with <paramref name="color"/>.</summary>
    public Task SetColorAsync(OpenRgbController controller, RgbColor color, CancellationToken ct = default)
    {
        var colors = Enumerable.Repeat(color, Math.Max(controller.LedNames.Count, controller.Colors.Count)).ToList();
        return Locked(async () =>
        {
            await SendAsync((uint)controller.Index, OpenRgbPacket.SetCustomMode, [], ct);
            await SendAsync((uint)controller.Index, OpenRgbPacket.UpdateLeds, OpenRgbProtocol.BuildUpdateLeds(colors), ct);
        }, ct);
    }

    /// <summary>Activates one of the controller's built-in modes, optionally with a colour.</summary>
    public Task SetModeAsync(OpenRgbController controller, int modeIndex, RgbColor? color = null, CancellationToken ct = default)
    {
        var mode = controller.Modes[modeIndex];
        if (color is RgbColor c && mode.ColorsMax > 0)
        {
            mode.ColorMode = OpenRgbMode.ColorModeModeSpecific;
            mode.Colors = Enumerable.Repeat(c, (int)Math.Max(1, mode.ColorsMin)).ToList();
        }
        return Locked(() => SendAsync((uint)controller.Index, OpenRgbPacket.UpdateMode,
            OpenRgbProtocol.BuildUpdateMode(modeIndex, mode, ProtocolVersion), ct), ct);
    }

    private async Task Locked(Func<Task> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await action(); }
        finally { _gate.Release(); }
    }

    private async Task SendAsync(uint deviceId, uint packetId, byte[] payload, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected to OpenRGB");
        await stream.WriteAsync(OpenRgbProtocol.BuildHeader(deviceId, packetId, payload.Length), ct);
        if (payload.Length > 0) await stream.WriteAsync(payload, ct);
    }

    /// <summary>Reads packets until one with <paramref name="packetId"/> arrives (skipping server notifications).</summary>
    private async Task<byte[]> ReceiveAsync(uint packetId, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected to OpenRGB");
        var header = new byte[OpenRgbProtocol.HeaderSize];
        while (true)
        {
            await stream.ReadExactlyAsync(header, ct);
            var (_, id, size) = OpenRgbProtocol.ParseHeader(header);
            var payload = new byte[size];
            if (size > 0) await stream.ReadExactlyAsync(payload, ct);
            if (id == packetId) return payload;
        }
    }

    public ValueTask DisposeAsync()
    {
        _tcp.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
