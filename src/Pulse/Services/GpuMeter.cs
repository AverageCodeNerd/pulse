using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse.Services;

/// <summary>
/// GPU metrics. For NVIDIA cards it uses NVML (nvml.dll, shipped with the
/// driver) for the full set — utilization, memory, temperature, power draw,
/// clocks and fan. For anything else it falls back to the Windows "GPU Engine"
/// performance counters for utilization only.
/// </summary>
public sealed class GpuMeter : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtil { public uint Gpu; public uint Mem; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMem { public ulong Total; public ulong Free; public ulong Used; }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] private static extern int NvmlInit();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] private static extern int NvmlShutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int NvmlHandle(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)] private static extern int NvmlName(IntPtr device, StringBuilder name, uint length);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] private static extern int NvmlUtilRates(IntPtr device, out NvmlUtil util);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] private static extern int NvmlMemInfo(IntPtr device, out NvmlMem mem);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] private static extern int NvmlTemp(IntPtr device, int sensor, out uint temp);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] private static extern int NvmlPower(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] private static extern int NvmlPowerLimit(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] private static extern int NvmlClock(IntPtr device, int type, out uint mhz);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed")] private static extern int NvmlFan(IntPtr device, out uint pct);

    private bool _nvml;
    private IntPtr _dev;
    private PerformanceCounter[]? _pdhEngine;

    public bool Available { get; private set; }
    public bool HasSensors { get; private set; }
    public string Name { get; private set; } = "GPU";
    public double Util, MemUsedMb, MemTotalMb, TempC, PowerW, PowerLimitW, CoreClockMhz, FanPct;

    public GpuMeter()
    {
        try
        {
            if (NvmlInit() == 0 && NvmlHandle(0, out _dev) == 0)
            {
                _nvml = true;
                Available = true;
                HasSensors = true;
                var sb = new StringBuilder(96);
                if (NvmlName(_dev, sb, 96) == 0 && sb.Length > 0) Name = sb.ToString();
            }
        }
        catch { _nvml = false; }

        if (!_nvml) TryInitPdh();
    }

    private void TryInitPdh()
    {
        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            var inst = cat.GetInstanceNames().Where(n => n.Contains("engtype_3D")).ToArray();
            if (inst.Length > 0)
            {
                _pdhEngine = inst.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true)).ToArray();
                foreach (var c in _pdhEngine) { try { c.NextValue(); } catch { } }
                Available = true;
                Name = "GPU";
            }
        }
        catch { }
    }

    public void Sample()
    {
        if (_nvml)
        {
            try { if (NvmlUtilRates(_dev, out var u) == 0) Util = u.Gpu; } catch { }
            try { if (NvmlMemInfo(_dev, out var m) == 0) { MemUsedMb = m.Used / 1048576.0; MemTotalMb = m.Total / 1048576.0; } } catch { }
            try { if (NvmlTemp(_dev, 0, out var t) == 0) TempC = t; } catch { }
            try { if (NvmlPower(_dev, out var p) == 0) PowerW = p / 1000.0; } catch { }
            try { if (NvmlPowerLimit(_dev, out var pl) == 0) PowerLimitW = pl / 1000.0; } catch { }
            try { if (NvmlClock(_dev, 0, out var c) == 0) CoreClockMhz = c; } catch { }
            try { if (NvmlFan(_dev, out var f) == 0) FanPct = f; } catch { }
        }
        else if (_pdhEngine != null)
        {
            double sum = 0;
            foreach (var c in _pdhEngine) { try { sum += c.NextValue(); } catch { } }
            Util = Math.Min(100, sum);
        }
    }

    public void Dispose()
    {
        if (_nvml) { try { NvmlShutdown(); } catch { } }
        if (_pdhEngine != null) foreach (var c in _pdhEngine) c.Dispose();
    }
}
