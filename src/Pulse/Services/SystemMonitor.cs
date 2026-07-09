using System.Diagnostics;
using Pulse.Models;

namespace Pulse.Services;

/// <summary>
/// Samples running processes and the machine's CPU/RAM. Per-process CPU% is
/// derived from the change in total processor time between two samples,
/// divided by wall-clock time and logical core count — the same method
/// Task Manager uses.
/// </summary>
public sealed class SystemMonitor
{
    private readonly Dictionary<int, ProcessInfo> _map = new();
    private readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private readonly int _cores = Environment.ProcessorCount;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    public double TotalCpu { get; private set; }
    public double UsedMemMb { get; private set; }
    public double TotalMemMb { get; private set; }
    public int ProcessCount { get; private set; }

    /// <summary>Take one sample and return the live process list (instances are reused across calls).</summary>
    public List<ProcessInfo> Sample()
    {
        double elapsedMs = _sw.Elapsed.TotalMilliseconds;
        _sw.Restart();
        if (elapsedMs <= 0) elapsedMs = 1;

        var seen = new HashSet<int>();
        double totalCpuDeltaMs = 0;

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
                    totalCpuDeltaMs += dMs;
                }
                _lastCpu[pid] = cpuNow;

                if (!_map.TryGetValue(pid, out var pi))
                {
                    string name;
                    try { name = p.ProcessName; } catch { name = "(unknown)"; }
                    pi = new ProcessInfo { Pid = pid, Name = name };
                    _map[pid] = pi;
                }

                pi.Cpu = cpuPct > 100 ? 100 : cpuPct;
                try { pi.MemMb = p.WorkingSet64 / 1048576.0; } catch { }
                try { pi.Threads = p.Threads.Count; } catch { }
                try { pi.Status = p.Responding ? "Running" : "Not responding"; } catch { }
            }
            catch { /* process exited mid-enumeration */ }
            finally { p.Dispose(); }
        }

        // Drop processes that have exited.
        foreach (var pid in _map.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _map.Remove(pid);
            _lastCpu.Remove(pid);
        }

        TotalCpu = Math.Min(100, totalCpuDeltaMs / (elapsedMs * _cores) * 100.0);
        ProcessCount = _map.Count;

        Native.GetMemory(out double usedMb, out double totalMb);
        UsedMemMb = usedMb;
        TotalMemMb = totalMb;

        return _map.Values.ToList();
    }

    /// <summary>Terminate a process. Returns false if it can't be killed (e.g. protected/access denied).</summary>
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
}
