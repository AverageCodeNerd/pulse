using System.Runtime.InteropServices;

namespace Pulse.Services;

/// <summary>
/// Accurate total and per-logical-core CPU usage, read from
/// NtQuerySystemInformation(SystemProcessorPerformanceInformation) and
/// differenced between samples. This is the same data the kernel exposes to
/// Task Manager, so the numbers line up.
/// </summary>
public sealed class CpuMeter
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;   // 100ns units; note: included within KernelTime
        public long KernelTime;
        public long UserTime;
        public long Reserved0;
        public long Reserved1;
        public uint Reserved2;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

    private const int SystemProcessorPerformanceInformation = 8;

    private readonly int _n = Environment.ProcessorCount;
    private readonly long[] _prevIdle;
    private readonly long[] _prevTotal;

    public double[] PerCore { get; }
    public double Overall { get; private set; }
    public int Cores => _n;

    public CpuMeter()
    {
        _prevIdle = new long[_n];
        _prevTotal = new long[_n];
        PerCore = new double[_n];
    }

    public void Sample()
    {
        int size = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size * _n);
        try
        {
            if (NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, size * _n, out _) != 0)
                return;

            long busySum = 0, totalSum = 0;
            for (int i = 0; i < _n; i++)
            {
                var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(IntPtr.Add(buffer, i * size));
                long idle = info.IdleTime;
                long total = info.KernelTime + info.UserTime; // kernel already includes idle
                long dIdle = idle - _prevIdle[i];
                long dTotal = total - _prevTotal[i];
                _prevIdle[i] = idle;
                _prevTotal[i] = total;

                double busy = dTotal > 0 ? (1.0 - (double)dIdle / dTotal) * 100.0 : 0;
                PerCore[i] = Math.Clamp(busy, 0, 100);

                busySum += dTotal - dIdle;
                totalSum += dTotal;
            }
            Overall = totalSum > 0 ? Math.Clamp((double)busySum / totalSum * 100.0, 0, 100) : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
