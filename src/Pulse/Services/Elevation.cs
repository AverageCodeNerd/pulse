using System.Diagnostics;
using System.Security.Principal;

namespace Pulse.Services;

/// <summary>Helpers for checking and requesting administrator elevation.</summary>
public static class Elevation
{
    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>Relaunch Pulse elevated. Returns false if UAC was declined.</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch { return false; }
    }
}
