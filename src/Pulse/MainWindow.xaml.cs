using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pulse.Models;
using Pulse.Services;
using Windows.Graphics;

namespace Pulse;

public sealed partial class MainWindow : Window
{
    private readonly SystemMonitor _mon = new();
    private readonly ObservableCollection<ProcessInfo> _processes = new();
    private DispatcherQueueTimer? _timer;

    private string _sortKey = "cpu";
    private bool _sortDesc = true;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Pulse";
        try { this.AppWindow?.Resize(new SizeInt32(1160, 760)); } catch { }

        ProcList.ItemsSource = _processes;

        _timer = this.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh(); // first paint (CPU shows on the second tick once deltas exist)
    }

    private void Refresh()
    {
        var sorted = Sort(_mon.Sample());
        Sync(sorted);

        CpuValue.Text = _mon.TotalCpu.ToString("0") + "%";
        CpuBar.Value = _mon.TotalCpu;

        double usedGb = _mon.UsedMemMb / 1024.0;
        double totalGb = _mon.TotalMemMb / 1024.0;
        MemValue.Text = usedGb.ToString("0.0") + " / " + totalGb.ToString("0") + " GB";
        MemBar.Value = _mon.TotalMemMb > 0 ? _mon.UsedMemMb / _mon.TotalMemMb * 100.0 : 0;

        CountText.Text = _mon.ProcessCount + " processes";
    }

    private List<ProcessInfo> Sort(List<ProcessInfo> list)
    {
        IOrderedEnumerable<ProcessInfo> o = _sortKey switch
        {
            "name" => _sortDesc
                ? list.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "mem" => _sortDesc ? list.OrderByDescending(p => p.MemMb) : list.OrderBy(p => p.MemMb),
            "threads" => _sortDesc ? list.OrderByDescending(p => p.Threads) : list.OrderBy(p => p.Threads),
            "pid" => _sortDesc ? list.OrderByDescending(p => p.Pid) : list.OrderBy(p => p.Pid),
            _ => _sortDesc ? list.OrderByDescending(p => p.Cpu) : list.OrderBy(p => p.Cpu),
        };
        return o.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reconcile the ObservableCollection with the sorted snapshot, reusing
    /// item instances so selection and scroll position survive each refresh.</summary>
    private void Sync(List<ProcessInfo> sorted)
    {
        var target = new HashSet<ProcessInfo>(sorted);
        for (int i = _processes.Count - 1; i >= 0; i--)
            if (!target.Contains(_processes[i]))
                _processes.RemoveAt(i);

        for (int i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            if (i >= _processes.Count)
            {
                _processes.Add(item);
            }
            else if (!ReferenceEquals(_processes[i], item))
            {
                int cur = _processes.IndexOf(item);
                if (cur >= 0) _processes.Move(cur, i);
                else _processes.Insert(i, item);
            }
        }
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key)
        {
            if (_sortKey == key) _sortDesc = !_sortDesc;
            else { _sortKey = key; _sortDesc = key != "name"; } // text asc, numbers desc
            Refresh();
        }
    }

    private void ProcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EndBtn.IsEnabled = ProcList.SelectedItem is ProcessInfo;
    }

    private void ProcList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => EndSelected();

    private void EndTask_Click(object sender, RoutedEventArgs e) => EndSelected();

    private void EndSelected()
    {
        if (ProcList.SelectedItem is ProcessInfo pi)
        {
            _mon.EndTask(pi.Pid);
            Refresh();
        }
    }
}
