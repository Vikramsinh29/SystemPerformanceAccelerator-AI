using System.ComponentModel;
using System.Runtime.InteropServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class SystemMonitorService : ISystemMonitorService
{
    private static readonly TimeSpan MinimumSampleDuration = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaximumSampleDuration = TimeSpan.FromSeconds(10);

    public async Task<SystemMonitorSnapshot> CaptureAsync(
        TimeSpan cpuSampleDuration,
        CancellationToken cancellationToken = default)
    {
        if (cpuSampleDuration < MinimumSampleDuration ||
            cpuSampleDuration > MaximumSampleDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cpuSampleDuration),
                $"CPU sample duration must be between {MinimumSampleDuration.TotalMilliseconds:0} ms and {MaximumSampleDuration.TotalSeconds:0} seconds.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var first = ReadProcessorTimes();
        await Task.Delay(cpuSampleDuration, cancellationToken).ConfigureAwait(false);
        var second = ReadProcessorTimes();
        var memory = ReadMemoryStatus();

        return new SystemMonitorSnapshot(
            DateTimeOffset.Now,
            CalculateCpuUsage(first, second),
            checked((long)memory.TotalPhysical),
            checked((long)memory.AvailablePhysical));
    }

    private static ProcessorTimes ReadProcessorTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not provide system processor times.");
        }

        return new ProcessorTimes(
            idle.ToUInt64(),
            kernel.ToUInt64(),
            user.ToUInt64());
    }

    private static MemoryStatus ReadMemoryStatus()
    {
        var nativeStatus = new MemoryStatusEx
        {
            Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>())
        };

        if (!GlobalMemoryStatusEx(ref nativeStatus))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not provide physical-memory information.");
        }

        return new MemoryStatus(
            nativeStatus.TotalPhysical,
            nativeStatus.AvailablePhysical);
    }

    private static double CalculateCpuUsage(
        ProcessorTimes first,
        ProcessorTimes second)
    {
        if (second.Idle < first.Idle ||
            second.Kernel < first.Kernel ||
            second.User < first.User)
        {
            throw new InvalidOperationException(
                "Windows processor counters changed unexpectedly during sampling.");
        }

        var idleDelta = second.Idle - first.Idle;
        var kernelDelta = second.Kernel - first.Kernel;
        var userDelta = second.User - first.User;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta == 0)
        {
            return 0;
        }

        var busyDelta = totalDelta > idleDelta
            ? totalDelta - idleDelta
            : 0;

        return Math.Clamp(busyDelta * 100d / totalDelta, 0, 100);
    }

    private readonly record struct ProcessorTimes(
        ulong Idle,
        ulong Kernel,
        ulong User);

    private readonly record struct MemoryStatus(
        ulong TotalPhysical,
        ulong AvailablePhysical);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() =>
            ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        ref MemoryStatusEx buffer);
}
