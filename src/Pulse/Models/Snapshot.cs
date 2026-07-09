namespace Pulse.Models;

/// <summary>Immutable per-process reading produced on the sampling thread.</summary>
public readonly record struct ProcSnap(int Pid, string Name, double Cpu, double MemMb, int Threads, string Status);

/// <summary>GPU + disk + network readings gathered alongside the process sample.</summary>
public sealed class HwInfo
{
    // GPU
    public bool GpuAvailable { get; set; }
    public bool GpuHasSensors { get; set; }   // temperature/power/clock/fan available (NVML)
    public string GpuName { get; set; } = "GPU";
    public double GpuUtil { get; set; }
    public double GpuMemUsedMb { get; set; }
    public double GpuMemTotalMb { get; set; }
    public double GpuTempC { get; set; }
    public double GpuPowerW { get; set; }
    public double GpuPowerLimitW { get; set; }
    public double GpuCoreClockMhz { get; set; }
    public double GpuFanPct { get; set; }

    // Disk (_Total physical disk)
    public double DiskActivePct { get; set; }
    public double DiskReadMBs { get; set; }
    public double DiskWriteMBs { get; set; }

    // Network (sum of active adapters)
    public double NetSendMbps { get; set; }
    public double NetRecvMbps { get; set; }
}

/// <summary>One full sample of the system, safe to hand across threads.</summary>
public sealed class Snapshot
{
    public List<ProcSnap> Procs { get; init; } = new();
    public double TotalCpu { get; init; }
    public double UsedMemMb { get; init; }
    public double TotalMemMb { get; init; }
    public double[] PerCore { get; init; } = System.Array.Empty<double>();
    public HwInfo Hw { get; init; } = new();
    public int Count => Procs.Count;
}
