using System.Diagnostics;
using Microsoft.Win32;

namespace Pulse.Services;

/// <summary>
/// Makes Pulse the default Task Manager using the Image File Execution Options
/// "Debugger" hook on taskmgr.exe. When set, any attempt to open Task Manager
/// (Ctrl+Shift+Esc, the taskbar menu, Run → taskmgr, …) launches Pulse instead.
///
/// The IFEO key lives under HKLM and needs administrator rights, so writes are
/// done by relaunching Pulse elevated with a flag (a single UAC prompt). It is
/// fully reversible — turning it off deletes the value and restores the
/// built-in Task Manager.
/// </summary>
public static class TaskManagerDefault
{
    private const string Ifeo =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe";

    public const string SetFlag = "--set-default-taskmgr";
    public const string UnsetFlag = "--unset-default-taskmgr";

    /// <summary>True if the Debugger hook currently points at Pulse.</summary>
    public static bool IsDefault()
    {
        var v = CurrentDebugger();
        return !string.IsNullOrWhiteSpace(v) && v.Contains("Pulse", StringComparison.OrdinalIgnoreCase);
    }

    public static string? CurrentDebugger()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Ifeo);
            return k?.GetValue("Debugger") as string;
        }
        catch { return null; }
    }

    /// <summary>Relaunch elevated to set/clear the hook. Returns the resulting state's success.</summary>
    public static bool Apply(bool enable)
    {
        if (Elevation.IsAdmin)
        {
            try { ApplyElevated(enable); return IsDefault() == enable; } catch { return false; }
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = enable ? SetFlag : UnsetFlag,
                UseShellExecute = true,
                Verb = "runas", // triggers UAC
            };
            var p = Process.Start(psi);
            p?.WaitForExit(10000);
            return IsDefault() == enable;
        }
        catch
        {
            return false; // UAC declined or failed
        }
    }

    /// <summary>Runs in the elevated instance: performs the actual registry write.</summary>
    public static void ApplyElevated(bool enable)
    {
        using var k = Registry.LocalMachine.CreateSubKey(Ifeo, writable: true);
        if (k is null) return;
        if (enable)
            k.SetValue("Debugger", $"\"{Environment.ProcessPath}\"", RegistryValueKind.String);
        else
            try { k.DeleteValue("Debugger", throwOnMissingValue: false); } catch { }
    }
}
