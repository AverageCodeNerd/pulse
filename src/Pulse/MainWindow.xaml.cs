using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pulse.Models;
using Pulse.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Pulse;

public sealed partial class MainWindow : Window
{
    private readonly SystemMonitor _mon = new();
    private readonly StartupService _startup = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _loadingSettings;
    private readonly Dictionary<int, ProcessInfo> _map = new();
    private readonly ObservableCollection<ProcessInfo> _processes = new();
    private DispatcherQueueTimer? _timer;
    private bool _busy;
    private MenuFlyout _procMenu = null!;
    private MenuFlyout _svcMenu = null!;

    private string _sortKey = "cpu";
    private bool _sortDesc = true;
    private bool _perfVisible;
    private string _filter = "";
    private Snapshot? _lastSnap;
    private readonly ObservableCollection<ServiceEntry> _services = new();
    private List<ServiceEntry> _allServices = new();
    private string _svcFilter = "";

    private const int HistN = 60;   // seconds of history for the big graphs
    private const int CoreN = 40;   // samples per core tile
    private const double CoreGraphW = 152, CoreGraphH = 44;

    private readonly List<double> _cpuHist = new();
    private readonly List<double> _memHist = new();
    private readonly List<double> _gpuHist = new();
    private readonly List<double> _diskHist = new();
    private readonly List<double> _netHist = new();
    private string _cpuName = "CPU";
    private string _osName = "Windows";

    private sealed class CoreTile
    {
        public Polyline Line = null!;
        public Polygon Fill = null!;
        public TextBlock Pct = null!;
        public readonly List<double> Data = new();
    }
    private readonly List<CoreTile> _coreTiles = new();

    private SolidColorBrush _cpuLineBrush = null!, _cpuFillBrush = null!;
    private SolidColorBrush _memLineBrush = null!, _memFillBrush = null!;
    private SolidColorBrush _gpuLineBrush = null!, _gpuFillBrush = null!;
    private SolidColorBrush _diskLineBrush = null!, _diskFillBrush = null!;
    private SolidColorBrush _netLineBrush = null!, _netFillBrush = null!;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Pulse";
        try { this.AppWindow?.Resize(new SizeInt32(1220, 800)); } catch { }
        try { this.AppWindow?.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "pulse.ico")); } catch { }

        ProcList.ItemsSource = _processes;
        SvcList.ItemsSource = _services;
        for (int i = 0; i < HistN; i++) { _cpuHist.Add(0); _memHist.Add(0); _gpuHist.Add(0); _diskHist.Add(0); _netHist.Add(0); }

        BuildBrushes();
        StyleGraph(CpuLine, CpuFill, _cpuLineBrush, _cpuFillBrush);
        StyleGraph(MemLine, MemFill, _memLineBrush, _memFillBrush);
        StyleGraph(GpuLine, GpuFill, _gpuLineBrush, _gpuFillBrush);
        StyleGraph(DiskLine, DiskFill, _diskLineBrush, _diskFillBrush);
        StyleGraph(NetLine, NetFill, _netLineBrush, _netFillBrush);
        BuildCoreTiles();
        BuildProcMenu();
        BuildSvcMenu();
        StCores.Text = _mon.Cores.ToString();
        _cpuName = ReadCpuName();
        _osName = ReadOsName();

        // Apply persisted preferences.
        ApplyTheme(_settings.Theme);
        ApplyAlwaysOnTop(_settings.AlwaysOnTop);
        _loadingSettings = true;
        SelectComboByTag(ThemeCombo, _settings.Theme);
        SelectComboByTag(SpeedCombo, _settings.UpdateMs.ToString());
        AlwaysOnTopToggle.IsOn = _settings.AlwaysOnTop;
        _loadingSettings = false;

        _timer = this.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(_settings.UpdateMs);
        _timer.Tick += async (_, _) => await RefreshAsync();
        _ = RefreshAsync();
        _timer.Start();
    }

    private void ApplyTheme(string theme)
    {
        if (this.Content is FrameworkElement root)
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem cbi && cbi.Tag is string t && t == tag) { combo.SelectedItem = cbi; return; }
    }

    // ---------- sampling / refresh ----------

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var snap = await Task.Run(_mon.SampleRaw); // heavy enumeration off the UI thread

            _lastSnap = snap;
            ApplyProcesses(snap);

            CpuValue.Text = snap.TotalCpu.ToString("0") + "%";
            CpuBar.Value = snap.TotalCpu;
            double usedGb = snap.UsedMemMb / 1024.0, totalGb = snap.TotalMemMb / 1024.0;
            string memText = usedGb.ToString("0.0") + " / " + totalGb.ToString("0") + " GB";
            MemValue.Text = memText;
            PerfMemValue.Text = memText;
            double memPct = snap.TotalMemMb > 0 ? snap.UsedMemMb / snap.TotalMemMb * 100.0 : 0;
            MemBar.Value = memPct;
            CountText.Text = snap.Count + " processes";

            Push(_cpuHist, snap.TotalCpu);
            Push(_memHist, memPct);
            Push(_gpuHist, snap.Hw.GpuAvailable ? snap.Hw.GpuUtil : 0);
            Push(_diskHist, snap.Hw.DiskActivePct);
            Push(_netHist, snap.Hw.NetSendMbps + snap.Hw.NetRecvMbps);
            for (int i = 0; i < _coreTiles.Count && i < snap.PerCore.Length; i++)
                Push(_coreTiles[i].Data, snap.PerCore[i]);

            if (_perfVisible) DrawPerf(snap);
        }
        finally { _busy = false; }
    }

    private void ApplyProcesses(Snapshot snap)
    {
        var seen = new HashSet<int>(snap.Procs.Count);
        var list = new List<ProcessInfo>(snap.Procs.Count);
        foreach (var s in snap.Procs)
        {
            seen.Add(s.Pid);
            if (!_map.TryGetValue(s.Pid, out var pi))
            {
                pi = new ProcessInfo { Pid = s.Pid, Name = s.Name };
                _map[s.Pid] = pi;
            }
            pi.Cpu = s.Cpu;
            pi.MemMb = s.MemMb;
            pi.Threads = s.Threads;
            pi.Status = s.Status;
            list.Add(pi);
        }
        foreach (var pid in _map.Keys.Where(k => !seen.Contains(k)).ToList())
            _map.Remove(pid);

        if (_filter.Length > 0)
            list = list.Where(p => p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

        Sync(SortList(list));
    }

    private List<ProcessInfo> SortList(List<ProcessInfo> list)
    {
        IOrderedEnumerable<ProcessInfo> o = _sortKey switch
        {
            "name" => _sortDesc ? list.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                                : list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "mem" => _sortDesc ? list.OrderByDescending(p => p.MemMb) : list.OrderBy(p => p.MemMb),
            "threads" => _sortDesc ? list.OrderByDescending(p => p.Threads) : list.OrderBy(p => p.Threads),
            "pid" => _sortDesc ? list.OrderByDescending(p => p.Pid) : list.OrderBy(p => p.Pid),
            _ => _sortDesc ? list.OrderByDescending(p => p.Cpu) : list.OrderBy(p => p.Cpu),
        };
        return o.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reconcile the collection with the sorted snapshot, reusing instances
    /// so selection and scroll position survive each refresh.</summary>
    private void Sync(List<ProcessInfo> sorted)
    {
        var target = new HashSet<ProcessInfo>(sorted);
        for (int i = _processes.Count - 1; i >= 0; i--)
            if (!target.Contains(_processes[i]))
                _processes.RemoveAt(i);

        for (int i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            if (i >= _processes.Count) { _processes.Add(item); }
            else if (!ReferenceEquals(_processes[i], item))
            {
                int cur = _processes.IndexOf(item);
                if (cur >= 0) _processes.Move(cur, i);
                else _processes.Insert(i, item);
            }
        }
    }

    private static void Push(List<double> buf, double v)
    {
        buf.Add(v);
        while (buf.Count > HistN) buf.RemoveAt(0);
    }

    // ---------- performance graphs ----------

    private void BuildBrushes()
    {
        Color accent;
        try { accent = new UISettings().GetColorValue(UIColorType.Accent); }
        catch { accent = Color.FromArgb(255, 0x4C, 0xC2, 0xFF); }
        Color mem = Color.FromArgb(255, 0xB1, 0x8C, 0xFF);
        Color gpu = Color.FromArgb(255, 0xE0, 0x6C, 0xB0);
        Color disk = Color.FromArgb(255, 0x3A, 0xC0, 0x7A);
        Color net = Color.FromArgb(255, 0xE0, 0x9B, 0x2E);

        _cpuLineBrush = new SolidColorBrush(accent);
        _cpuFillBrush = new SolidColorBrush(accent) { Opacity = 0.22 };
        _memLineBrush = new SolidColorBrush(mem);
        _memFillBrush = new SolidColorBrush(mem) { Opacity = 0.20 };
        _gpuLineBrush = new SolidColorBrush(gpu);
        _gpuFillBrush = new SolidColorBrush(gpu) { Opacity = 0.20 };
        _diskLineBrush = new SolidColorBrush(disk);
        _diskFillBrush = new SolidColorBrush(disk) { Opacity = 0.20 };
        _netLineBrush = new SolidColorBrush(net);
        _netFillBrush = new SolidColorBrush(net) { Opacity = 0.20 };
    }

    private static void StyleGraph(Polyline line, Polygon fill, Brush stroke, Brush fillBrush)
    {
        line.Stroke = stroke;
        line.StrokeThickness = 1.6;
        line.StrokeLineJoin = PenLineJoin.Round;
        fill.Fill = fillBrush;
    }

    private void BuildCoreTiles()
    {
        var card = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        var stroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        for (int i = 0; i < _mon.Cores; i++)
        {
            var tile = new CoreTile();
            for (int k = 0; k < CoreN; k++) tile.Data.Add(0);

            tile.Fill = new Polygon { Fill = _cpuFillBrush };
            tile.Line = new Polyline { Stroke = _cpuLineBrush, StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round };
            var graph = new Grid { Width = CoreGraphW, Height = CoreGraphH };
            graph.Children.Add(tile.Fill);
            graph.Children.Add(tile.Line);

            var label = new TextBlock { Text = "CPU " + i, FontSize = 10, Foreground = secondary };
            tile.Pct = new TextBlock { Text = "0%", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tile.Pct, 1);
            top.Children.Add(label);
            top.Children.Add(tile.Pct);

            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(top);
            stack.Children.Add(graph);

            var border = new Border
            {
                Background = card,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Width = 156,
                Height = 82,
                Child = stack,
            };
            CoreGrid.Children.Add(border);
            _coreTiles.Add(tile);
        }
    }

    private void DrawPerf(Snapshot snap)
    {
        DrawGraph(CpuLine, CpuFill, _cpuHist, CpuGraphHost.ActualWidth, CpuGraphHost.ActualHeight, 100);
        DrawGraph(MemLine, MemFill, _memHist, MemGraphHost.ActualWidth, MemGraphHost.ActualHeight, 100);
        for (int i = 0; i < _coreTiles.Count && i < snap.PerCore.Length; i++)
        {
            DrawGraph(_coreTiles[i].Line, _coreTiles[i].Fill, _coreTiles[i].Data, CoreGraphW, CoreGraphH, 100);
            _coreTiles[i].Pct.Text = snap.PerCore[i].ToString("0") + "%";
        }
        PerfCpuValue.Text = snap.TotalCpu.ToString("0") + "%";
        StUtil.Text = snap.TotalCpu.ToString("0") + "%";
        StProcs.Text = snap.Count.ToString();
        StThreads.Text = snap.Procs.Sum(p => p.Threads).ToString();

        DrawGraph(GpuLine, GpuFill, _gpuHist, GpuGraphHost.ActualWidth, GpuGraphHost.ActualHeight, 100);
        DrawGraph(DiskLine, DiskFill, _diskHist, DiskGraphHost.ActualWidth, DiskGraphHost.ActualHeight, 100);
        DrawGraph(NetLine, NetFill, _netHist, NetGraphHost.ActualWidth, NetGraphHost.ActualHeight, NetMax());
        UpdateHwStats(snap);
        UpdateSysInfo(snap);
    }

    private double NetMax() => Math.Max(10, _netHist.Count > 0 ? _netHist.Max() * 1.25 : 10);

    private void UpdateHwStats(Snapshot snap)
    {
        var hw = snap.Hw;
        GpuTitle.Text = hw.GpuAvailable ? "GPU · " + hw.GpuName : "GPU";
        if (hw.GpuAvailable)
        {
            GpuSub.Text = "% Utilization over 60s";
            StGpuValue.Text = hw.GpuUtil.ToString("0") + "%";
            StGpuUtil.Text = hw.GpuUtil.ToString("0") + "%";
            StGpuMem.Text = hw.GpuMemTotalMb > 0
                ? (hw.GpuMemUsedMb / 1024).ToString("0.0") + " / " + (hw.GpuMemTotalMb / 1024).ToString("0") + " GB" : "—";
            StGpuTemp.Text = hw.GpuHasSensors ? hw.GpuTempC.ToString("0") + " °C" : "—";
            StGpuPower.Text = hw.GpuHasSensors && hw.GpuPowerW > 0
                ? hw.GpuPowerW.ToString("0") + " W" + (hw.GpuPowerLimitW > 0 ? " / " + hw.GpuPowerLimitW.ToString("0") : "") : "—";
            StGpuClock.Text = hw.GpuHasSensors && hw.GpuCoreClockMhz > 0 ? hw.GpuCoreClockMhz.ToString("0") + " MHz" : "—";
            StGpuFan.Text = hw.GpuHasSensors ? hw.GpuFanPct.ToString("0") + "%" : "—";
        }
        else
        {
            GpuSub.Text = "Not available on this system";
            StGpuValue.Text = "—";
            StGpuUtil.Text = StGpuMem.Text = StGpuTemp.Text = StGpuPower.Text = StGpuClock.Text = StGpuFan.Text = "—";
        }

        StDiskValue.Text = hw.DiskActivePct.ToString("0") + "%";
        StDiskActive.Text = hw.DiskActivePct.ToString("0") + "%";
        StDiskRead.Text = FmtRate(hw.DiskReadMBs);
        StDiskWrite.Text = FmtRate(hw.DiskWriteMBs);

        double tot = hw.NetSendMbps + hw.NetRecvMbps;
        StNetValue.Text = FmtMbps(tot);
        StNetSend.Text = FmtMbps(hw.NetSendMbps);
        StNetRecv.Text = FmtMbps(hw.NetRecvMbps);
    }

    private void UpdateSysInfo(Snapshot snap)
    {
        long up = Environment.TickCount64 / 1000;
        string uptime = $"{up / 3600}:{(up % 3600) / 60:00}:{up % 60:00}";
        string gpu = snap.Hw.GpuAvailable ? snap.Hw.GpuName : "no GPU sensor";
        SysInfoText.Text = $"{_cpuName}   ·   {_mon.Cores} logical cores   ·   {snap.TotalMemMb / 1024:0} GB RAM   ·   {gpu}   ·   {_osName}   ·   up {uptime}";
    }

    private static string FmtRate(double mbps) => mbps < 0.05 ? "0 MB/s" : mbps.ToString("0.0") + " MB/s";
    private static string FmtMbps(double m) => m < 0.05 ? "0 Mbps" : (m < 10 ? m.ToString("0.0") : m.ToString("0")) + " Mbps";

    private static string ReadCpuName()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return k?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "CPU";
        }
        catch { return "CPU"; }
    }

    private static string ReadOsName()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? product = k?.GetValue("ProductName")?.ToString();
            string? build = k?.GetValue("CurrentBuildNumber")?.ToString();
            int b = int.TryParse(build, out var bb) ? bb : 0;
            string name = b >= 22000 ? "Windows 11" : (product ?? "Windows");
            return build is not null ? $"{name} (build {build})" : name;
        }
        catch { return "Windows"; }
    }

    private void RedrawAll()
    {
        DrawGraph(CpuLine, CpuFill, _cpuHist, CpuGraphHost.ActualWidth, CpuGraphHost.ActualHeight, 100);
        DrawGraph(MemLine, MemFill, _memHist, MemGraphHost.ActualWidth, MemGraphHost.ActualHeight, 100);
        DrawGraph(GpuLine, GpuFill, _gpuHist, GpuGraphHost.ActualWidth, GpuGraphHost.ActualHeight, 100);
        DrawGraph(DiskLine, DiskFill, _diskHist, DiskGraphHost.ActualWidth, DiskGraphHost.ActualHeight, 100);
        DrawGraph(NetLine, NetFill, _netHist, NetGraphHost.ActualWidth, NetGraphHost.ActualHeight, NetMax());
        foreach (var t in _coreTiles)
            DrawGraph(t.Line, t.Fill, t.Data, CoreGraphW, CoreGraphH, 100);
    }

    private static void DrawGraph(Polyline line, Polygon fill, List<double> data, double w, double h, double max)
    {
        if (w <= 0 || h <= 0) return;
        int n = data.Count;
        if (n < 2) { line.Points = new PointCollection(); fill.Points = new PointCollection(); return; }

        var lp = new PointCollection();
        var fp = new PointCollection();
        double step = w / (n - 1);
        fp.Add(new Point(0, h));
        for (int i = 0; i < n; i++)
        {
            double v = data[i];
            if (v < 0) v = 0; else if (v > max) v = max;
            double y = h - (v / max) * (h - 2) - 1;
            var pt = new Point(i * step, y);
            lp.Add(pt);
            fp.Add(pt);
        }
        fp.Add(new Point(w, h));
        line.Points = lp;
        fill.Points = fp;
    }

    private void CpuGraphHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawGraph(CpuLine, CpuFill, _cpuHist, CpuGraphHost.ActualWidth, CpuGraphHost.ActualHeight, 100);

    private void MemGraphHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawGraph(MemLine, MemFill, _memHist, MemGraphHost.ActualWidth, MemGraphHost.ActualHeight, 100);

    private void GpuGraphHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawGraph(GpuLine, GpuFill, _gpuHist, GpuGraphHost.ActualWidth, GpuGraphHost.ActualHeight, 100);

    private void DiskGraphHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawGraph(DiskLine, DiskFill, _diskHist, DiskGraphHost.ActualWidth, DiskGraphHost.ActualHeight, 100);

    private void NetGraphHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawGraph(NetLine, NetFill, _netHist, NetGraphHost.ActualWidth, NetGraphHost.ActualHeight, NetMax());

    // ---------- navigation ----------

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            bool perf = tag == "performance";
            bool startup = tag == "startup";
            bool services = tag == "services";
            bool settings = tag == "settings";
            bool proc = !perf && !startup && !services && !settings;
            _perfVisible = perf;
            if (ProcessesPanel is not null) ProcessesPanel.Visibility = proc ? Visibility.Visible : Visibility.Collapsed;
            if (PerformancePanel is not null) PerformancePanel.Visibility = perf ? Visibility.Visible : Visibility.Collapsed;
            if (StartupPanel is not null) StartupPanel.Visibility = startup ? Visibility.Visible : Visibility.Collapsed;
            if (ServicesPanel is not null) ServicesPanel.Visibility = services ? Visibility.Visible : Visibility.Collapsed;
            if (SettingsPanel is not null) SettingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
            if (perf) DispatcherQueue.TryEnqueue(RedrawAll);
            if (startup) BuildStartupList();
            if (services) _ = LoadServices();
            if (settings) RefreshSettings();
        }
    }

    // ---------- services ----------

    private async Task LoadServices()
    {
        SvcCount.Text = "loading…";
        _allServices = await Task.Run(WindowsServices.List);
        ApplyServiceFilter();
    }

    private void ApplyServiceFilter()
    {
        IEnumerable<ServiceEntry> q = _allServices;
        if (_svcFilter.Length > 0)
            q = q.Where(s => s.DisplayName.Contains(_svcFilter, StringComparison.OrdinalIgnoreCase)
                          || s.Name.Contains(_svcFilter, StringComparison.OrdinalIgnoreCase));
        _services.Clear();
        foreach (var s in q) _services.Add(s);
        SvcCount.Text = $"{_services.Count} services";
    }

    private void SvcFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _svcFilter = SvcFilterBox.Text?.Trim() ?? "";
        ApplyServiceFilter();
    }

    private void SvcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var s = SvcList.SelectedItem as ServiceEntry;
        SvcStartBtn.IsEnabled = s is { IsStopped: true };
        SvcStopBtn.IsEnabled = s is { IsRunning: true, CanStop: true };
        SvcRestartBtn.IsEnabled = s is { IsRunning: true };
    }

    private async void SvcStart_Click(object sender, RoutedEventArgs e) => await DoService(SvcAction.Start);
    private async void SvcStop_Click(object sender, RoutedEventArgs e) => await DoService(SvcAction.Stop);
    private async void SvcRestart_Click(object sender, RoutedEventArgs e) => await DoService(SvcAction.Restart);
    private async void SvcRefresh_Click(object sender, RoutedEventArgs e) => await LoadServices();

    private async Task DoService(SvcAction action)
    {
        if (SvcList.SelectedItem is not ServiceEntry s) return;
        await Task.Run(() => WindowsServices.Apply(action, s.Name));
        await LoadServices();
    }

    private void BuildSvcMenu()
    {
        _svcMenu = new MenuFlyout();
        void Add(string text, Action act)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => act();
            _svcMenu.Items.Add(mi);
        }
        Add("Start", async () => await DoService(SvcAction.Start));
        Add("Stop", async () => await DoService(SvcAction.Stop));
        Add("Restart", async () => await DoService(SvcAction.Restart));
        _svcMenu.Items.Add(new MenuFlyoutSeparator());
        Add("Copy name", () => { if (SvcList.SelectedItem is ServiceEntry s) CopyText(s.Name); });
        Add("Open services.msc", () => { try { Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); } catch { } });
    }

    private void SvcRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ServiceEntry s)
        {
            SvcList.SelectedItem = s;
            _svcMenu.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
            e.Handled = true;
        }
    }

    private void FindAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ServicesPanel.Visibility == Visibility.Visible) SvcFilterBox.Focus(FocusState.Programmatic);
        else if (ProcessesPanel.Visibility == Visibility.Visible) FilterBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    // ---------- settings ----------

    private bool _tmGuard;

    private void RefreshSettings()
    {
        bool isDefault = TaskManagerDefault.IsDefault();
        _tmGuard = true;
        DefaultTmToggle.IsOn = isDefault;
        _tmGuard = false;
        DefaultTmStatus.Text = isDefault
            ? "Pulse is currently the default Task Manager."
            : "Windows Task Manager is currently the default.";
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string ver = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        AboutText.Text = $"Pulse {ver} — a fast, native Windows Task Manager replacement.";
    }

    private void DefaultTmToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_tmGuard) return;
        bool want = DefaultTmToggle.IsOn;
        DefaultTmStatus.Text = "Waiting for administrator approval…";
        bool ok = TaskManagerDefault.Apply(want);
        if (!ok)
        {
            _tmGuard = true;
            DefaultTmToggle.IsOn = TaskManagerDefault.IsDefault();
            _tmGuard = false;
            DefaultTmStatus.Text = "Change cancelled or blocked — administrator approval is required.";
            return;
        }
        DefaultTmStatus.Text = want
            ? "Done. Task Manager shortcuts now open Pulse."
            : "Done. The built-in Task Manager is restored.";
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string ms && int.TryParse(ms, out int millis))
        {
            if (_timer is not null) _timer.Interval = TimeSpan.FromMilliseconds(millis);
            if (!_loadingSettings) { _settings.UpdateMs = millis; _settings.Save(); }
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string theme)
        {
            ApplyTheme(theme);
            if (!_loadingSettings) { _settings.Theme = theme; _settings.Save(); }
        }
    }

    private void AlwaysOnTopToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ApplyAlwaysOnTop(AlwaysOnTopToggle.IsOn);
        if (!_loadingSettings) { _settings.AlwaysOnTop = AlwaysOnTopToggle.IsOn; _settings.Save(); }
    }

    private void ApplyAlwaysOnTop(bool on)
    {
        if (this.AppWindow?.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = on;
    }

    // ---------- startup apps ----------

    private void BuildStartupList()
    {
        StartupList.Children.Clear();
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        foreach (var entry in _startup.List())
        {
            var row = new Grid { Padding = new Thickness(12, 9, 12, 9) };
            row.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var name = new StackPanel { Spacing = 1 };
            name.Children.Add(new TextBlock { Text = entry.DisplayName, FontSize = 13.5 });
            var sub = string.IsNullOrWhiteSpace(entry.Publisher) ? entry.Command : entry.Publisher;
            name.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(name, 0);

            var source = new TextBlock { Text = entry.SourceLabel, FontSize = 12, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(source, 1);

            var toggle = new ToggleSwitch
            {
                IsOn = entry.Enabled,
                IsEnabled = entry.CanToggle,
                OnContent = "",
                OffContent = "",
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var captured = entry;
            bool reverting = false;
            toggle.Toggled += (_, _) =>
            {
                if (reverting) return;
                if (!_startup.SetEnabled(captured, toggle.IsOn))
                {
                    reverting = true;
                    toggle.IsOn = captured.Enabled;
                    reverting = false;
                }
            };
            Grid.SetColumn(toggle, 2);

            row.Children.Add(name);
            row.Children.Add(source);
            row.Children.Add(toggle);
            StartupList.Children.Add(row);
        }
    }

    // ---------- process actions ----------

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key)
        {
            if (_sortKey == key) _sortDesc = !_sortDesc;
            else { _sortKey = key; _sortDesc = key != "name"; }
            _ = RefreshAsync();
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text?.Trim() ?? "";
        if (_lastSnap is not null) ApplyProcesses(_lastSnap);
    }

    private async void RunNewTask_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = "e.g. notepad, cmd, or C:\\path\\to\\app.exe" };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Type a program, folder, document or website to open.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(input);
        var dlg = new ContentDialog
        {
            Title = "Run new task",
            Content = body,
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            string cmd = input.Text?.Trim() ?? "";
            if (cmd.Length == 0) return;
            try { Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true }); }
            catch (Exception ex) { await ShowInfo("Couldn't run that", $"“{cmd}” could not be started.\n\n{ex.Message}"); }
        }
    }

    private async Task ShowInfo(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private void ProcList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EndBtn.IsEnabled = ProcList.SelectedItem is ProcessInfo;

    private void ProcList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ProcList.SelectedItem is ProcessInfo pi) _ = ShowDetails(pi);
    }

    private async Task ShowDetails(ProcessInfo pi)
    {
        var d = _mon.GetDetails(pi.Pid);
        var grid = new Grid { RowSpacing = 7, ColumnSpacing = 16, MinWidth = 420 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = key, Foreground = secondary, FontSize = 12.5 };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, IsTextSelectionEnabled = true };
            Grid.SetRow(k, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            grid.Children.Add(k); grid.Children.Add(v);
            row++;
        }
        Add("Name", pi.Name);
        Add("Description", d.Description);
        Add("Publisher", d.Company);
        Add("Status", pi.Status);
        Add("PID", pi.Pid.ToString());
        Add("Threads", pi.Threads.ToString());
        Add("Memory", pi.MemText);
        Add("Started", d.Started?.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("Path", d.Path ?? "(unavailable — process may be protected)");

        var dlg = new ContentDialog
        {
            Title = "Process details",
            Content = grid,
            CloseButtonText = "Close",
            XamlRoot = this.Content.XamlRoot,
        };
        if (d.Path is not null)
        {
            dlg.PrimaryButtonText = "Open file location";
            dlg.PrimaryButtonClick += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{d.Path}\"") { UseShellExecute = true }); } catch { }
            };
        }
        await dlg.ShowAsync();
    }

    private void ProcList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete) { EndSelected(); e.Handled = true; }
    }

    private void EndTask_Click(object sender, RoutedEventArgs e) => EndSelected();

    private void BuildProcMenu()
    {
        _procMenu = new MenuFlyout();
        void Add(string text, Action act)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => act();
            _procMenu.Items.Add(mi);
        }
        Add("Details", () => { if (ProcList.SelectedItem is ProcessInfo p) _ = ShowDetails(p); });
        Add("End task", EndSelected);
        Add("Restart", RestartSelected);
        Add("Open file location", OpenFileLocation);
        _procMenu.Items.Add(new MenuFlyoutSeparator());
        Add("Copy PID", () => { if (ProcList.SelectedItem is ProcessInfo p) CopyText(p.Pid.ToString()); });
        Add("Copy name", () => { if (ProcList.SelectedItem is ProcessInfo p) CopyText(p.Name); });
    }

    private void ProcRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProcessInfo pi)
        {
            ProcList.SelectedItem = pi;
            _procMenu.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
            e.Handled = true;
        }
    }

    private void EndSelected()
    {
        if (ProcList.SelectedItem is ProcessInfo pi)
        {
            _mon.EndTask(pi.Pid);
            _ = RefreshAsync();
        }
    }

    private void RestartSelected()
    {
        if (ProcList.SelectedItem is ProcessInfo pi)
        {
            if (!_mon.Restart(pi.Pid))
                _ = ShowInfo("Couldn't restart", "This process can't be restarted — its path is unavailable or access was denied.");
            _ = RefreshAsync();
        }
    }

    private void OpenFileLocation()
    {
        if (ProcList.SelectedItem is not ProcessInfo pi) return;
        string? path = _mon.GetPath(pi.Pid);
        if (path is null)
        {
            _ = ShowInfo("Location unavailable", "Couldn't get the file path for this process — it may be protected.");
            return;
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static void CopyText(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }
}
