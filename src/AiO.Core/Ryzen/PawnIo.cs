using System.Runtime.InteropServices;
using System.Text;
using AiO.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace AiO.Core.Ryzen;

public enum PawnIoStatus { Ok, NotInstalled, AccessDenied, Error }

/// <summary>
/// Client for the PawnIO driver (https://pawnio.eu): a signed kernel driver that runs small, verified
/// modules instead of exposing raw hardware access. AiO loads the RyzenSMU module into it.
/// </summary>
public sealed class PawnIo : IDisposable
{
    private const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    private const uint DeviceType = 41394u << 16;
    private const uint IoctlLoad = DeviceType | (0x821 << 2);
    private const uint IoctlExecute = DeviceType | (0x841 << 2);
    private const int FunctionNameLength = 32;

    private readonly SafeFileHandle _handle;

    private PawnIo(SafeFileHandle handle) => _handle = handle;

    public static PawnIo? Open(out PawnIoStatus status)
    {
        var handle = Kernel32.CreateFile(DevicePath, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            status = error switch
            {
                2 or 3 => PawnIoStatus.NotInstalled,
                5 => PawnIoStatus.AccessDenied,
                _ => PawnIoStatus.Error,
            };
            return null;
        }
        status = PawnIoStatus.Ok;
        return new PawnIo(handle);
    }

    public bool LoadModule(byte[] module) =>
        Kernel32.DeviceIoControl(_handle, IoctlLoad, module, (uint)module.Length, [], 0, out _, IntPtr.Zero);

    /// <summary>Calls a module function with 64-bit arguments; returns false when the driver reports failure.</summary>
    public bool Execute(string function, ReadOnlySpan<ulong> input, Span<ulong> output)
    {
        var inBuffer = BuildExecuteBuffer(function, input);
        var outBuffer = new byte[output.Length * 8];
        if (!Kernel32.DeviceIoControl(_handle, IoctlExecute, inBuffer, (uint)inBuffer.Length, outBuffer,
                (uint)outBuffer.Length, out uint returned, IntPtr.Zero))
            return false;
        MemoryMarshal.Cast<byte, ulong>(outBuffer.AsSpan(0, (int)Math.Min(returned, (uint)outBuffer.Length) / 8 * 8))
            .CopyTo(output);
        return true;
    }

    /// <summary>32-byte zero-padded ASCII function name followed by the arguments.</summary>
    internal static byte[] BuildExecuteBuffer(string function, ReadOnlySpan<ulong> input)
    {
        var buffer = new byte[FunctionNameLength + input.Length * 8];
        var name = Encoding.ASCII.GetBytes(function);
        name.AsSpan(0, Math.Min(name.Length, FunctionNameLength - 1)).CopyTo(buffer);
        MemoryMarshal.AsBytes(input).CopyTo(buffer.AsSpan(FunctionNameLength));
        return buffer;
    }

    public void Dispose() => _handle.Dispose();
}
