using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DellR730xdFanControlCenter;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\DellR730xdFanControlCenter.SingleInstance";
    private const int DuplicateStartupExitCode = 2;
    private const uint MessageBoxIconWarning = 0x00000030;

    private static Mutex? singleInstanceMutex;

    private Window? _window;

    public static MainWindow? CurrentWindow { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            WriteStartupLog(eventArgs.ExceptionObject.ToString() ?? "未知未处理异常。");
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
            NormalizeProcessWorkingDirectory();
            EnsureSingleInstance();
            CurrentWindow = new MainWindow();
            _window = CurrentWindow;
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupLog(ex.ToString());
            throw;
        }
    }

    private static void NormalizeProcessWorkingDirectory()
    {
        var applicationDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            throw new InvalidOperationException("无法解析应用程序目录。");
        }

        Directory.SetCurrentDirectory(applicationDirectory);
    }

    private static void EnsureSingleInstance()
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            StopDuplicateStartup(
                "Dell R730xd Fan Control Center 已经在当前 Windows 会话中运行。为了避免多个实例同时写入 settings.json 或并发发送 IPMI 风扇命令，本次启动已停止。请切换到已有窗口，或先退出旧实例后再启动。");
        }

        var currentProcess = Process.GetCurrentProcess();
        var currentExecutablePath = currentProcess.MainModule?.FileName
            ?? throw new InvalidOperationException("无法解析当前实例的可执行文件路径，单实例检查已停止启动。");
        var currentVersion = FileVersionInfo.GetVersionInfo(currentExecutablePath).FileVersion
            ?? throw new InvalidOperationException("无法读取当前实例的文件版本，单实例检查已停止启动。");
        var runningProcessIds = new StringBuilder();
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id == currentProcess.Id)
                {
                    continue;
                }

                var processExecutablePath = process.MainModule?.FileName
                    ?? throw new InvalidOperationException($"无法解析进程 {process.Id} 的可执行文件路径，单实例检查已停止启动。");
                var processVersion = FileVersionInfo.GetVersionInfo(processExecutablePath).FileVersion
                    ?? throw new InvalidOperationException($"无法读取进程 {process.Id} 的文件版本，单实例检查已停止启动。");
                if (string.Equals(processVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (runningProcessIds.Length > 0)
                {
                    runningProcessIds.Append(", ");
                }

                runningProcessIds.Append(process.Id);
            }
        }

        if (runningProcessIds.Length > 0)
        {
            StopDuplicateStartup(
                $"Dell R730xd Fan Control Center 已经在运行，进程 ID：{runningProcessIds}。为了避免多个实例同时写入 settings.json 或并发发送 IPMI 风扇命令，本次启动已停止。请切换到已有窗口，或先退出旧实例后再启动。");
        }
    }

    private static void StopDuplicateStartup(string message)
    {
        WriteStartupLog(message);
        MessageBoxW(IntPtr.Zero, message, "Dell R730xd Fan Control Center", MessageBoxIconWarning);
        Environment.Exit(DuplicateStartupExitCode);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
