using System.Linq;
using Microsoft.UI.Xaml;
using Pulse.Services;

namespace Pulse;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Elevated relaunch to toggle the default-Task-Manager registry hook, then exit.
        var argv = Environment.GetCommandLineArgs();
        if (argv.Any(a => a is TaskManagerDefault.SetFlag or TaskManagerDefault.UnsetFlag))
        {
            try { TaskManagerDefault.ApplyElevated(argv.Contains(TaskManagerDefault.SetFlag)); } catch { }
            this.Exit();
            return;
        }

        // Elevated relaunch to start/stop/restart a service, then exit.
        int svcIdx = Array.FindIndex(argv, a => a is WindowsServices.StartFlag or WindowsServices.StopFlag or WindowsServices.RestartFlag);
        if (svcIdx >= 0 && svcIdx + 1 < argv.Length)
        {
            var action = argv[svcIdx] switch
            {
                WindowsServices.StartFlag => SvcAction.Start,
                WindowsServices.StopFlag => SvcAction.Stop,
                _ => SvcAction.Restart,
            };
            try { WindowsServices.ApplyElevated(action, argv[svcIdx + 1]); } catch { }
            this.Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
