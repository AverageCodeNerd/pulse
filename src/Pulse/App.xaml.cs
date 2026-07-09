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

        _window = new MainWindow();
        _window.Activate();
    }
}
