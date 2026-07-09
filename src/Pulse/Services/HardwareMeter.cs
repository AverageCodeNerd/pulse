using System.Diagnostics;
using Pulse.Models;

namespace Pulse.Services;

/// <summary>
/// Gathers GPU, disk and network metrics each sample. Disk and network come
/// from Windows performance counters (_Total physical disk; summed active
/// network adapters); GPU comes from <see cref="GpuMeter"/>.
/// </summary>
public sealed class HardwareMeter : IDisposable
{
    private readonly GpuMeter _gpu = new();
    private PerformanceCounter? _diskTime, _diskRead, _diskWrite;
    private PerformanceCounter[]? _netSent, _netRecv;

    public HardwareMeter()
    {
        try
        {
            _diskTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
            _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
            _diskTime.NextValue(); _diskRead.NextValue(); _diskWrite.NextValue();
        }
        catch { }

        try
        {
            var cat = new PerformanceCounterCategory("Network Interface");
            var inst = cat.GetInstanceNames()
                .Where(n => !n.Contains("Loopback") && !n.Contains("isatap") && !n.Contains("Teredo"))
                .ToArray();
            _netSent = inst.Select(n => new PerformanceCounter("Network Interface", "Bytes Sent/sec", n, true)).ToArray();
            _netRecv = inst.Select(n => new PerformanceCounter("Network Interface", "Bytes Received/sec", n, true)).ToArray();
            foreach (var c in _netSent) { try { c.NextValue(); } catch { } }
            foreach (var c in _netRecv) { try { c.NextValue(); } catch { } }
        }
        catch { }
    }

    public void Fill(HwInfo hw)
    {
        _gpu.Sample();
        hw.GpuAvailable = _gpu.Available;
        hw.GpuHasSensors = _gpu.HasSensors;
        hw.GpuName = _gpu.Name;
        hw.GpuUtil = _gpu.Util;
        hw.GpuMemUsedMb = _gpu.MemUsedMb;
        hw.GpuMemTotalMb = _gpu.MemTotalMb;
        hw.GpuTempC = _gpu.TempC;
        hw.GpuPowerW = _gpu.PowerW;
        hw.GpuPowerLimitW = _gpu.PowerLimitW;
        hw.GpuCoreClockMhz = _gpu.CoreClockMhz;
        hw.GpuFanPct = _gpu.FanPct;

        try { if (_diskTime != null) hw.DiskActivePct = Math.Min(100, _diskTime.NextValue()); } catch { }
        try { if (_diskRead != null) hw.DiskReadMBs = _diskRead.NextValue() / 1048576.0; } catch { }
        try { if (_diskWrite != null) hw.DiskWriteMBs = _diskWrite.NextValue() / 1048576.0; } catch { }

        double s = 0, r = 0;
        if (_netSent != null) foreach (var c in _netSent) { try { s += c.NextValue(); } catch { } }
        if (_netRecv != null) foreach (var c in _netRecv) { try { r += c.NextValue(); } catch { } }
        hw.NetSendMbps = s * 8 / 1_000_000.0;
        hw.NetRecvMbps = r * 8 / 1_000_000.0;
    }

    public void Dispose()
    {
        _gpu.Dispose();
        _diskTime?.Dispose(); _diskRead?.Dispose(); _diskWrite?.Dispose();
        if (_netSent != null) foreach (var c in _netSent) c.Dispose();
        if (_netRecv != null) foreach (var c in _netRecv) c.Dispose();
    }
}
