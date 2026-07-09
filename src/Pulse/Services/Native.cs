using System.Runtime.InteropServices;

namespace Pulse.Services;

/// <summary>Thin P/Invoke layer for system-wide physical memory figures.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static void GetMemory(out double usedMb, out double totalMb)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            totalMb = m.ullTotalPhys / 1048576.0;
            usedMb = (m.ullTotalPhys - m.ullAvailPhys) / 1048576.0;
        }
        else
        {
            totalMb = 0;
            usedMb = 0;
        }
    }
}
