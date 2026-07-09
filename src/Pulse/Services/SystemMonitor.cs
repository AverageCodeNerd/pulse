using System.Diagnostics;
using Pulse.Models;

namespace Pulse.Services;

/// <summary>
/// Samples all processes plus machine CPU/RAM. Per-process CPU% comes from the
/// change in processor time between samples; total/per-core CPU comes from
/// <see cref="CpuMeter"/>. Returns a plain <see cref="Snapshot"/> so the heavy
/// enumeration can run on a background thread — the UI thread only applies the
/// result, which keeps refreshes from hitching.
/// </summary>
public sealed class SystemMonitor
{
    private readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private readonly int _cores = Environment.ProcessorCount;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly CpuMeter _cpu = new();
    private HardwareMeter? _hw; // lazily created on the sampling thread

    public int Cores => _cores;

    public Snapshot SampleRaw()
    {
        double elapsedMs = _sw.Elapsed.TotalMilliseconds;
        _sw.Restart();
        if (elapsedMs <= 0) elapsedMs = 1;

        _cpu.Sample();

        var procs = new List<ProcSnap>(256);
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                int pid = p.Id;
                if (pid == 0) continue; // System Idle
                seen.Add(pid);

                TimeSpan cpuNow;
                try { cpuNow = p.TotalProcessorTime; }
                catch { cpuNow = _lastCpu.TryGetValue(pid, out var t) ? t : TimeSpan.Zero; }

                double cpuPct = 0;
                if (_lastCpu.TryGetValue(pid, out var prev))
                {
                    double dMs = (cpuNow - prev).TotalMilliseconds;
                    if (dMs < 0) dMs = 0;
                    cpuPct = dMs / (elapsedMs * _cores) * 100.0;
                }
                _lastCpu[pid] = cpuNow;

                string name; try { name = p.ProcessName; } catch { name = "(unknown)"; }
                double mem = 0; try { mem = p.WorkingSet64 / 1048576.0; } catch { }
                int threads = 0; try { threads = p.Threads.Count; } catch { }
                string status = "Running"; try { status = p.Responding ? "Running" : "Not responding"; } catch { }

                procs.Add(new ProcSnap(pid, name, Math.Min(cpuPct, 100), mem, threads, status));
            }
            catch { /* exited mid-enumeration */ }
            finally { p.Dispose(); }
        }

        foreach (var pid in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastCpu.Remove(pid);

        Native.GetMemory(out double usedMb, out double totalMb);

        var hw = new HwInfo();
        try { (_hw ??= new HardwareMeter()).Fill(hw); } catch { }

        return new Snapshot
        {
            Procs = procs,
            TotalCpu = _cpu.Overall,
            UsedMemMb = usedMb,
            TotalMemMb = totalMb,
            PerCore = (double[])_cpu.PerCore.Clone(),
            Hw = hw,
        };
    }

    public bool EndTask(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Full path to a process's executable, or null if inaccessible.</summary>
    public string? GetPath(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch { return null; }
    }

    /// <summary>Kill a process and relaunch it from its executable path.</summary>
    public bool Restart(int pid)
    {
        string? path = GetPath(pid);
        if (string.IsNullOrEmpty(path)) return false;
        try { using var p = Process.GetProcessById(pid); p.Kill(); p.WaitForExit(3000); } catch { }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return true; }
        catch { return false; }
    }
}
