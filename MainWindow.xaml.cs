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
        _trayIcon = CreateTrayIcon();
        ApplyLocalization();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private TrayIconManager CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var trayIcon = new TrayIconManager(_windowHandle, iconPath);
        trayIcon.RestoreRequested += (_, _) => RestoreFromTray();
        trayIcon.FanPercentRequested += (_, percent) => RunPageCommand(page => page.ApplyQuickFanSpeedAsync(percent));
        trayIcon.RestoreDefaultRequested += (_, _) => RunPageCommand(page => page.RestoreDefaultManualFromTrayAsync());
        trayIcon.SettingsRequested += (_, _) =>
        {
            RestoreFromTray();
            RunPageAction(page => page.ShowSettingsView());
        };
        trayIcon.ExitRequested += (_, _) => ExitApplication();
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
        DispatcherQueue.TryEnqueue(() =>
        {
            if (RootFrame.Content is MainPage page)
            {
                action(page);
            }
        });
    }

    private void RunPageCommand(Func<MainPage, System.Threading.Tasks.Task> command)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (RootFrame.Content is MainPage page)
            {
                await command(page);
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
