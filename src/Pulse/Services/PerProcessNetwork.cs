using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Pulse.Services;

/// <summary>
/// Per-process network throughput. Windows has no per-process network
/// performance counter, so — like Task Manager — this consumes a kernel ETW
/// session (TCP/UDP send+receive) and accumulates bytes per PID. ETW kernel
/// sessions require administrator rights, so this only runs when Pulse is
/// elevated; otherwise it stays inert and every process reports 0.
/// </summary>
public sealed class PerProcessNetwork : IDisposable
{
    private readonly ConcurrentDictionary<int, long> _bytes = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private volatile bool _active;

    public bool Active => _active;

    public PerProcessNetwork()
    {
        if (!Elevation.IsAdmin) return; // kernel session needs admin
        try
        {
            _session = new TraceEventSession("PulseNetworkSession")
            {
                StopOnDispose = true,
            };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var k = _session.Source.Kernel;
            k.TcpIpRecv += d => Add(d.ProcessID, d.size);
            k.TcpIpSend += d => Add(d.ProcessID, d.size);
            k.UdpIpRecv += d => Add(d.ProcessID, d.size);
            k.UdpIpSend += d => Add(d.ProcessID, d.size);

            _thread = new Thread(() => { try { _session.Source.Process(); } catch { } })
            {
                IsBackground = true,
                Name = "PulseNetworkETW",
            };
            _thread.Start();
            _active = true;
        }
        catch
        {
            _active = false;
            try { _session?.Dispose(); } catch { }
            _session = null;
        }
    }

    private void Add(int pid, int size)
    {
        if (pid <= 0 || size <= 0) return;
        _bytes.AddOrUpdate(pid, size, (_, cur) => cur + size);
    }

    /// <summary>Bytes transferred per PID since the previous call; resets the counters.</summary>
    public Dictionary<int, long> DrainBytes()
    {
        var result = new Dictionary<int, long>(_bytes.Count);
        foreach (var pid in _bytes.Keys.ToArray())
            if (_bytes.TryRemove(pid, out var v) && v > 0)
                result[pid] = v;
        return result;
    }

    public void Dispose()
    {
        _active = false;
        try { _session?.Dispose(); } catch { }
        _session = null;
    }
}
