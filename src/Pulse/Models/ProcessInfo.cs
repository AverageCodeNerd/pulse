using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pulse.Models;

/// <summary>
/// A single running process. Implements INotifyPropertyChanged so the ListView
/// updates in place each refresh without losing the user's selection.
/// </summary>
public sealed class ProcessInfo : INotifyPropertyChanged
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";

    private double _cpu;
    public double Cpu
    {
        get => _cpu;
        set { if (Set(ref _cpu, value)) Raise(nameof(CpuText)); }
    }

    private double _memMb;
    public double MemMb
    {
        get => _memMb;
        set { if (Set(ref _memMb, value)) Raise(nameof(MemText)); }
    }

    private double _diskMbs;
    public double DiskMBs
    {
        get => _diskMbs;
        set { if (Set(ref _diskMbs, value)) Raise(nameof(DiskText)); }
    }

    private double _netMbps;
    public double NetMbps
    {
        get => _netMbps;
        set { if (Set(ref _netMbps, value)) Raise(nameof(NetText)); }
    }

    private double _gpuPct;
    public double GpuPct
    {
        get => _gpuPct;
        set { if (Set(ref _gpuPct, value)) Raise(nameof(GpuText)); }
    }

    private int _threads;
    public int Threads
    {
        get => _threads;
        set => Set(ref _threads, value);
    }

    private string _status = "Running";
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string CpuText => _cpu < 0.05 ? "0%" : _cpu.ToString("0.0") + "%";

    public string MemText => _memMb >= 1024
        ? (_memMb / 1024).ToString("0.0") + " GB"
        : _memMb.ToString("0") + " MB";

    public string DiskText => _diskMbs < 0.05 ? "0 MB/s" : _diskMbs.ToString("0.0") + " MB/s";

    /// <summary>App-wide flag: per-process network needs an elevated ETW session.</summary>
    public static bool NetAvailable { get; set; } = true;

    public string NetText => !NetAvailable ? "—"
        : _netMbps < 0.05 ? "0 Mbps"
        : (_netMbps < 10 ? _netMbps.ToString("0.0") : _netMbps.ToString("0")) + " Mbps";

    public string GpuText => _gpuPct < 0.5 ? "0%" : _gpuPct.ToString("0") + "%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
