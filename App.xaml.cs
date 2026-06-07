using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DellR730xdFanControlCenter;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            WriteStartupLog(eventArgs.ExceptionObject.ToString() ?? "Unknown unhandled exception.");
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            WriteStartupLog(eventArgs.Exception.ToString());
        };

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupLog(ex.ToString());
            throw;
        }
    }

    private static void WriteStartupLog(string message)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DellR730xdFanControlCenter");
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, "startup-error.log");
        File.AppendAllText(
            logPath,
            $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}",
            Encoding.UTF8);
    }
}
