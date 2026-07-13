using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DellR730xdFanControlCenter;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    private readonly IntPtr _windowHandle;
    private readonly TrayIconManager _trayIcon;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnWindowClosed;

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Create the page before registering the native tray callback so every
        // tray failure has a MainPage boundary that can display and log it.
        RootFrame.Navigate(typeof(MainPage));

        _trayIcon = CreateTrayIcon();
        ApplyLocalization();
    }

    private TrayIconManager CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var trayIcon = new TrayIconManager(_windowHandle, iconPath);
        trayIcon.RestoreRequested += (_, _) => RestoreFromTray();
        trayIcon.OverviewRequested += (_, _) =>
        {
            RestoreFromTray();
            RunPageAction(page => page.ShowOverviewView());
        };
        trayIcon.ControlRequested += (_, _) =>
        {
            RestoreFromTray();
            RunPageAction(page => page.ShowControlView());
        };
        trayIcon.SensorsRequested += (_, _) =>
        {
            RestoreFromTray();
            RunPageAction(page => page.ShowSensorsView());
        };
        trayIcon.RefreshSensorsRequested += (_, _) => RunPageCommand(page => page.RefreshSensorsFromTrayAsync());
        trayIcon.OpenIdracRequested += (_, _) => RunPageCommand(page => page.OpenIdracFromTrayAsync());
        trayIcon.OpenLogsRequested += (_, _) => RunPageAction(page => page.OpenLogFolderFromTray());
        trayIcon.FanPercentRequested += (_, percent) => RunPageCommand(page => page.ApplyQuickFanSpeedAsync(percent));
        trayIcon.PresetRequested += (_, preset) => RunPageCommand(page => page.ApplyPresetFromTrayAsync(preset));
        trayIcon.RestoreDefaultRequested += (_, _) => RunPageCommand(page => page.RestoreDellFactoryFanSpeedFromTrayAsync());
        trayIcon.StopAutoRequested += (_, _) => RunPageAction(page => page.StopAutoPolicyFromTray());
        trayIcon.SettingsRequested += (_, _) =>
        {
            RestoreFromTray();
            RunPageAction(page => page.ShowSettingsView());
        };
        trayIcon.ExitRequested += (_, _) => ExitApplication();
        trayIcon.CommandFailed += (_, ex) => ReportTrayFailure(ex);
        return trayIcon;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        if (RootFrame.Content is MainPage page && page.MinimizeToTrayOnClose)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        ShowWindow(_windowHandle, SwHide);
        _trayIcon.ShowBalloon(LocalizationService.T("Tray.BalloonTitle"), LocalizationService.T("Tray.BalloonMessage"));
    }

    public void ApplyLocalization()
    {
        var title = LocalizationService.T("App.Title");
        AppWindow.Title = title;
        AppTitleBar.Title = title;
        _trayIcon.UpdateTip();
    }

    private void RestoreFromTray()
    {
        ShowWindow(_windowHandle, SwShow);
        Activate();
        SetForegroundWindow(_windowHandle);
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Dispose();
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _trayIcon.Dispose();
    }

    private void RunPageAction(Action<MainPage> action)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (RootFrame.Content is MainPage page)
            {
                try
                {
                    action(page);
                }
                catch (Exception ex)
                {
                    page.ReportTrayCommandFailure(ex);
                }
            }
        }))
        {
            throw new InvalidOperationException("无法将托盘操作加入界面线程队列，应用可能正在关闭。");
        }
    }

    private void RunPageCommand(Func<MainPage, System.Threading.Tasks.Task> command)
    {
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            if (RootFrame.Content is MainPage page)
            {
                try
                {
                    await command(page);
                }
                catch (Exception ex)
                {
                    page.ReportTrayCommandFailure(ex);
                }
            }
        }))
        {
            throw new InvalidOperationException("无法将托盘命令加入界面线程队列，应用可能正在关闭。");
        }
    }

    private void ReportTrayFailure(Exception ex)
    {
        if (RootFrame.Content is MainPage page)
        {
            page.ReportTrayCommandFailure(ex);
            return;
        }

        throw new InvalidOperationException("托盘命令失败，但主页面尚未就绪，无法显示失败详情。", ex);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
