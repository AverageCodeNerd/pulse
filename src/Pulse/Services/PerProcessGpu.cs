using System.Runtime.InteropServices;

namespace Pulse.Services;

/// <summary>
/// Per-process GPU utilization, read from the Windows "GPU Engine" performance
/// counters via a PDH wildcard query. Each counter instance name embeds the
/// owning PID (e.g. "pid_1234_luid_..._engtype_3D"); utilization is summed per
/// PID across engines. Works for any GPU vendor, no elevation required.
/// </summary>
public sealed class PerProcessGpu
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint ERROR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_DOUBLE
    {
        public IntPtr szName;
        public uint CStatus;
        private uint _pad;
        public double doubleValue;
    }

    [DllImport("pdh.dll")]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    private IntPtr _query;
    private IntPtr _counter;
    private bool _ready;

    public PerProcessGpu()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) == ERROR_SUCCESS &&
                PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) == ERROR_SUCCESS)
            {
                PdhCollectQueryData(_query); // prime (rate counters need two samples)
                _ready = true;
            }
        }
        catch { _ready = false; }
    }

    /// <summary>Utilization percentage per PID (0–100), summed across engines.</summary>
    public Dictionary<int, double> Sample()
    {
        var result = new Dictionary<int, double>();
        if (!_ready) return result;
        try
        {
            if (PdhCollectQueryData(_query) != ERROR_SUCCESS) return result;

            uint size = 0, count = 0;
            uint status = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return result;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out count, buffer) != ERROR_SUCCESS)
                    return result;

                int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>(IntPtr.Add(buffer, i * itemSize));
                    string? name = Marshal.PtrToStringUni(item.szName);
                    int pid = ExtractPid(name);
                    if (pid <= 0) continue;
                    double v = item.doubleValue;
                    if (double.IsNaN(v) || v < 0) v = 0;
                    result[pid] = result.TryGetValue(pid, out var cur) ? cur + v : v;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { }
        return result;
    }

    private static int ExtractPid(string? instance)
    {
        // instance form: "pid_1234_luid_0x..._engtype_3D"
        if (string.IsNullOrEmpty(instance)) return 0;
        const string tag = "pid_";
        int i = instance.IndexOf(tag, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += tag.Length;
        int j = i;
        while (j < instance.Length && char.IsDigit(instance[j])) j++;
        return j > i && int.TryParse(instance.AsSpan(i, j - i), out int pid) ? pid : 0;
    }
}
