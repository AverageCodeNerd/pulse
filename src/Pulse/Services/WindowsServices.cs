using System.Diagnostics;
using System.ServiceProcess;

namespace Pulse.Services;

public enum SvcAction { Start, Stop, Restart }

public sealed record ServiceEntry(
    string Name, string DisplayName, string Status, string StartType,
    bool CanStop, bool IsRunning, bool IsStopped);

/// <summary>
/// Lists Windows services and starts/stops/restarts them. Listing is read-only
/// and works unelevated; changing a service's state needs admin, so those go
/// through an elevated relaunch (one UAC prompt), like the default-Task-Manager
/// toggle.
/// </summary>
public static class WindowsServices
{
    public const string StartFlag = "--svc-start";
    public const string StopFlag = "--svc-stop";
    public const string RestartFlag = "--svc-restart";

    public static List<ServiceEntry> List()
    {
        var result = new List<ServiceEntry>();
        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                string start; try { start = sc.StartType.ToString(); } catch { start = "—"; }
                result.Add(new ServiceEntry(
                    sc.ServiceName,
                    string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.ServiceName : sc.DisplayName,
                    sc.Status.ToString(),
                    start,
                    Safe(() => sc.CanStop),
                    sc.Status == ServiceControllerStatus.Running,
                    sc.Status == ServiceControllerStatus.Stopped));
            }
            catch { }
            finally { sc.Dispose(); }
        }
        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }

    /// <summary>Relaunch elevated to perform the action. Returns false if UAC was declined.</summary>
    public static bool Apply(SvcAction action, string name)
    {
        if (Elevation.IsAdmin)
        {
            try { ApplyElevated(action, name); return true; } catch { return false; }
        }
        string flag = action switch { SvcAction.Start => StartFlag, SvcAction.Stop => StopFlag, _ => RestartFlag };
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{flag} \"{name}\"",
                UseShellExecute = true,
                Verb = "runas",
            };
            var p = Process.Start(psi);
            p?.WaitForExit(30000);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Runs in the elevated instance: actually changes the service state.</summary>
    public static void ApplyElevated(SvcAction action, string name)
    {
        var timeout = TimeSpan.FromSeconds(25);
        using var sc = new ServiceController(name);
        switch (action)
        {
            case SvcAction.Start:
                if (sc.Status != ServiceControllerStatus.Running)
                { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, timeout); }
                break;
            case SvcAction.Stop:
                if (sc.CanStop && sc.Status != ServiceControllerStatus.Stopped)
                { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout); }
                break;
            case SvcAction.Restart:
                if (sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout); }
                sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
                break;
        }
    }
}
