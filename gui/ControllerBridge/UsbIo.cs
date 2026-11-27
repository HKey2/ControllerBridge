using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControllerBridge;

internal readonly struct UsbDeadline(int milliseconds, CancellationToken cancellation = default)
{
    private readonly long started = Stopwatch.GetTimestamp();
    internal CancellationToken Cancellation => cancellation;
    internal int Remaining
    {
        get
        {
            cancellation.ThrowIfCancellationRequested();
            double remaining = milliseconds - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (remaining <= 0) throw Expired();
            return Math.Max(1, (int)Math.Ceiling(remaining));
        }
    }
    internal static TimeoutException Expired() => new(L.T("USB通信がタイムアウトしました。再接続して設定を読み直してください。", "USB communication timed out. Reconnect and reload the device settings."));
}

internal sealed class UsbTransferPendingException() : IOException(L.T(
    "前回のUSB通信の終了を待っています。少し待って再接続してください。改善しない場合はUSB-Cを抜き差ししてください。",
    "The previous USB transfer is still ending. Wait briefly and reconnect. If this continues, unplug and reconnect USB-C."));

// Every transfer owns its OVERLAPPED, buffers and file reference until Windows
// reports completion, including after cancellation. A retired connection never
// submits another request; the same path stays unavailable until it has drained.
internal sealed class UsbIo : IDisposable
{
    internal const uint HidGetFeature = (11u << 16) | (100u << 2) | 2u;
    internal const uint HidSetFeature = (11u << 16) | (100u << 2) | 1u;
    private const int Pending = 997;
    private static readonly object pathsLock = new();
    private static readonly Dictionary<string, int> pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private readonly SafeFileHandle handle;
    private readonly string path;
    private readonly EventWaitHandle stopped;
    private Operation? active;
    private bool disposed, quarantined;

    internal UsbIo(string path, uint access)
    {
        this.path = path; CheckPath(path);
        handle = CreateFileW(path, access, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try { CheckPath(path); }
        catch { handle.Dispose(); throw; }
        stopped = new(false, EventResetMode.ManualReset);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped { public UIntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(IntPtr handle, uint code, IntPtr input, uint inputLength, IntPtr output, uint outputLength, IntPtr returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOverlappedResult(IntPtr handle, IntPtr overlapped, out uint count, [MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

    private static void CheckPath(string path)
    {
        lock (pathsLock) if (pendingPaths.ContainsKey(path)) throw new UsbTransferPendingException();
    }

    private sealed class Operation
    {
        private readonly SafeFileHandle file;
        private GCHandle inputPin, outputPin;
        private bool fileHeld;
        internal readonly EventWaitHandle Signal = new(false, EventResetMode.ManualReset);
        internal IntPtr Overlapped, Input, Output, File;
        internal bool Pending;
        internal RegisteredWaitHandle? Reaper;

        internal Operation(SafeFileHandle file, byte[] input, byte[] output)
        {
            this.file = file;
            try
            {
                file.DangerousAddRef(ref fileHeld); File = file.DangerousGetHandle();
                if (input.Length != 0) { inputPin = GCHandle.Alloc(input, GCHandleType.Pinned); Input = inputPin.AddrOfPinnedObject(); }
                if (output.Length != 0) { outputPin = GCHandle.Alloc(output, GCHandleType.Pinned); Output = outputPin.AddrOfPinnedObject(); }
                Overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<UsbIo.Overlapped>());
                Marshal.StructureToPtr(new UsbIo.Overlapped { Event = Signal.SafeWaitHandle.DangerousGetHandle() }, Overlapped, false);
            }
            catch { Release(); throw; }
        }

        internal void Release()
        {
            Reaper?.Unregister(null);
            if (Overlapped != IntPtr.Zero) Marshal.FreeHGlobal(Overlapped);
            if (inputPin.IsAllocated) inputPin.Free();
            if (outputPin.IsAllocated) outputPin.Free();
            Signal.Dispose();
            if (fileHeld) file.DangerousRelease();
        }
    }

    internal int Control(uint code, byte[] input, byte[] output, UsbDeadline deadline)
    {
        int timeout = deadline.Remaining;
        Operation operation;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); CheckPath(path);
            if (active is not null) throw new InvalidOperationException("A USB transfer is already pending");
            operation = new(handle, input, output); active = operation;
            try
            {
                if (!DeviceIoControl(operation.File, code, operation.Input, (uint)input.Length,
                        operation.Output, (uint)output.Length, IntPtr.Zero, operation.Overlapped))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != Pending) throw new Win32Exception(error, CommunicationError);
                    operation.Pending = true;
                }
            }
            catch { Finish(operation); throw; }
        }

        bool deferred = false;
        try
        {
            if (operation.Pending)
            {
                WaitHandle[] signals = deadline.Cancellation.CanBeCanceled
                    ? [operation.Signal, stopped, deadline.Cancellation.WaitHandle] : [operation.Signal, stopped];
                int result = WaitHandle.WaitAny(signals, Math.Min(timeout, deadline.Remaining));
                if (result == WaitHandle.WaitTimeout) throw UsbDeadline.Expired();
                deadline.Cancellation.ThrowIfCancellationRequested();
                if (result != 0) throw new ObjectDisposedException(nameof(UsbIo));
            }
            if (!GetOverlappedResult(operation.File, operation.Overlapped, out uint count, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), CommunicationError);
            if (count > output.Length && output.Length != 0) throw new InvalidDataException("USB response length");
            return checked((int)count);
        }
        catch
        {
            Dispose();
            if (operation.Pending && !operation.Signal.WaitOne(250))
            {
                // CancelIoEx requests cancellation; it does not acknowledge it.
                // Keep storage alive without keeping the caller or a worker blocked.
                deferred = true;
                lock (sync)
                    operation.Reaper = ThreadPool.RegisterWaitForSingleObject(operation.Signal,
                        (_, _) => Finish(operation), null, Timeout.Infinite, true);
            }
            throw;
        }
        finally { if (!deferred) Finish(operation); }
    }

    private static string CommunicationError => L.T("デバイスのUSB通信が切れました。USB-Cの接続を確認してください。", "USB communication was interrupted. Check the USB-C connection.");

    private void Finish(Operation operation)
    {
        lock (sync)
        {
            if (!ReferenceEquals(active, operation)) return;
            active = null; operation.Release();
            if (quarantined)
            {
                lock (pathsLock)
                {
                    if (--pendingPaths[path] == 0) pendingPaths.Remove(path);
                }
                quarantined = false;
            }
            if (disposed) stopped.Dispose();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true; stopped.Set();
            if (active is { Pending: true } operation)
            {
                if (!operation.Signal.WaitOne(0))
                {
                    lock (pathsLock) pendingPaths[path] = pendingPaths.GetValueOrDefault(path) + 1;
                    quarantined = true;
                }
                CancelIoEx(operation.File, operation.Overlapped);
            }
            handle.Dispose();
            if (active is null) stopped.Dispose();
        }
    }
}
