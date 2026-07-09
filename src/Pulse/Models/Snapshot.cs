namespace Pulse.Models;

/// <summary>Immutable per-process reading produced on the sampling thread.</summary>
public readonly record struct ProcSnap(int Pid, string Name, double Cpu, double MemMb, int Threads, string Status);

/// <summary>One full sample of the system, safe to hand across threads.</summary>
public sealed class Snapshot
{
    public List<ProcSnap> Procs { get; init; } = new();
    public double TotalCpu { get; init; }
    public double UsedMemMb { get; init; }
    public double TotalMemMb { get; init; }
    public double[] PerCore { get; init; } = System.Array.Empty<double>();
    public int Count => Procs.Count;
}
