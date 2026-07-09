using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Pulse.Services;

public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

public sealed class StartupEntry
{
    public required string Key { get; init; }          // registry value name, or .lnk file name
    public required string DisplayName { get; init; }
    public required string Command { get; init; }
    public string Publisher { get; set; } = "";
    public bool Enabled { get; set; }
    public StartupSource Source { get; init; }

    /// <summary>Only user-scoped entries can be toggled without admin rights.</summary>
    public bool CanToggle => Source is StartupSource.UserRun or StartupSource.UserFolder;

    public string SourceLabel => Source switch
    {
        StartupSource.UserRun => "Registry · current user",
        StartupSource.MachineRun => "Registry · all users",
        StartupSource.MachineRun32 => "Registry · all users (32-bit)",
        StartupSource.UserFolder => "Startup folder · current user",
        StartupSource.CommonFolder => "Startup folder · all users",
        _ => "",
    };
}

/// <summary>
/// Reads the same startup locations Task Manager shows — the Run registry keys
/// and the Startup folders — plus each entry's enabled/disabled state from the
/// StartupApproved keys. User-scoped entries can be toggled in place.
/// </summary>
public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRun(list, Registry.CurrentUser, RunPath, StartupSource.UserRun);
        AddRun(list, Registry.LocalMachine, RunPath, StartupSource.MachineRun);
        AddRun(list, Registry.LocalMachine, Run32Path, StartupSource.MachineRun32);
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder);
        return list.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddRun(List<StartupEntry> list, RegistryKey hive, string path, StartupSource source)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            var approvedHive = source == StartupSource.UserRun ? Registry.CurrentUser : Registry.LocalMachine;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                string cmd = key.GetValue(name)?.ToString() ?? "";
                list.Add(new StartupEntry
                {
                    Key = name,
                    DisplayName = name,
                    Command = cmd,
                    Publisher = ReadPublisher(cmd),
                    Enabled = IsEnabled(approvedHive, ApprovedRun, name),
                    Source = source,
                });
            }
        }
        catch { /* missing key / access denied */ }
    }

    private void AddFolder(List<StartupEntry> list, string folder, StartupSource source)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var approvedHive = source == StartupSource.UserFolder ? Registry.CurrentUser : Registry.LocalMachine;
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                string fileName = Path.GetFileName(file);
                list.Add(new StartupEntry
                {
                    Key = fileName,
                    DisplayName = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Publisher = "",
                    Enabled = IsEnabled(approvedHive, ApprovedFolder, fileName),
                    Source = source,
                });
            }
        }
        catch { }
    }

    private static bool IsEnabled(RegistryKey hive, string approvedPath, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(approvedPath);
            if (key?.GetValue(name) is byte[] v && v.Length > 0)
                return (v[0] & 1) == 0; // even first byte = enabled, odd = disabled
        }
        catch { }
        return true; // absent = enabled
    }

    /// <summary>Enable/disable a user-scoped entry by writing its StartupApproved value.</summary>
    public bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (!entry.CanToggle) return false;
        try
        {
            string approvedPath = entry.Source == StartupSource.UserFolder ? ApprovedFolder : ApprovedRun;
            using var key = Registry.CurrentUser.CreateSubKey(approvedPath, writable: true);
            if (key is null) return false;
            var bytes = new byte[12];
            bytes[0] = (byte)(enabled ? 2 : 3);
            key.SetValue(entry.Key, bytes, RegistryValueKind.Binary);
            entry.Enabled = enabled;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadPublisher(string command)
    {
        try
        {
            string path = ExePath(command);
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return info.CompanyName ?? "";
            }
        }
        catch { }
        return "";
    }

    private static string ExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command.Substring(1, end - 1) : command.Trim('"');
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command.Substring(0, space) : command;
    }
}
