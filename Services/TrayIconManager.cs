using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DellR730xdFanControlCenter;

public sealed class TrayIconManager : IDisposable
{
    private const uint IconId = 1;
    private const uint TrayCallbackMessage = 0x8000 + 731;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmContextMenu = 0x007B;
    private const uint WmNull = 0x0000;
    private const uint TpmReturNcmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint MfPopup = 0x0010;
    private const int RestoreCommand = 100;
    private const int Fans20Command = 120;
    private const int Fans35Command = 135;
    private const int Fans50Command = 150;
    private const int RestoreDefaultCommand = 200;
    private const int SettingsCommand = 300;
    private const int ExitCommand = 900;
    private const int PresetCommandBase = 4000;

    private readonly IntPtr _windowHandle;
    private readonly SubclassProc _subclassProc;
    private readonly IntPtr _iconHandle;
    private readonly Dictionary<int, FanPreset> _presetCommands = [];
    private bool _disposed;

    public TrayIconManager(IntPtr windowHandle, string iconPath)
    {
        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("Tray icon file was not found.", iconPath);
        }

        _windowHandle = windowHandle;
        _subclassProc = WndProc;
        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (_iconHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to load tray icon: {iconPath}");
        }

        if (!SetWindowSubclass(_windowHandle, _subclassProc, new UIntPtr(1), IntPtr.Zero))
        {
            throw new InvalidOperationException("Unable to subclass the main window for tray messages.");
        }

        var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip);
        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException("Unable to add the tray icon.");
        }
    }

    public event EventHandler? RestoreRequested;

    public event EventHandler<int>? FanPercentRequested;

    public event EventHandler<FanPreset>? PresetRequested;

    public event EventHandler? RestoreDefaultRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void ShowBalloon(string title, string message)
    {
        var data = CreateNotifyIconData(NifInfo);
        data.InfoTitle = title;
        data.Info = message;
        data.InfoFlags = 0x00000001;
        ShellNotifyIcon(NimModify, ref data);
    }

    public void UpdateTip()
    {
        var data = CreateNotifyIconData(NifTip);
        ShellNotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateNotifyIconData(0);
        ShellNotifyIcon(NimDelete, ref data);
        RemoveWindowSubclass(_windowHandle, _subclassProc, new UIntPtr(1));
        DestroyIcon(_iconHandle);
        _disposed = true;
    }

    private NotifyIconData CreateNotifyIconData(uint flags)
    {
        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = TrayCallbackMessage,
            IconHandle = _iconHandle,
            Tip = LocalizationService.T("App.TrayTip"),
        };
    }

    private IntPtr WndProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData)
    {
        if (message == TrayCallbackMessage)
        {
            var trayMessage = unchecked((uint)lParam.ToInt64());
            if (trayMessage is WmLButtonDblClk)
            {
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            if (trayMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (!GetCursorPos(out var point))
        {
            throw new InvalidOperationException("Unable to read cursor position for tray menu.");
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create tray context menu.");
        }

        try
        {
            AppendCommand(menu, RestoreCommand, LocalizationService.T("Tray.Restore"));
            AppendSeparator(menu);
            AppendPopup(menu, BuildFanControlMenu(), LocalizationService.T("Tray.FanControlGroup"));
            AppendSeparator(menu);
            AppendCommand(menu, SettingsCommand, LocalizationService.T("Tray.Settings"));
            AppendCommand(menu, ExitCommand, LocalizationService.T("Tray.Exit"));

            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(menu, TpmReturNcmd | TpmRightButton, point.X, point.Y, _windowHandle, IntPtr.Zero);
            PostMessage(_windowHandle, WmNull, UIntPtr.Zero, IntPtr.Zero);
            HandleCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private IntPtr BuildFanControlMenu()
    {
        var fanControlMenu = CreatePopupMenu();
        if (fanControlMenu == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create fan control tray submenu.");
        }

        AppendPopup(fanControlMenu, BuildPresetMenu(), LocalizationService.T("Tray.Presets"));
        AppendPopup(fanControlMenu, BuildAllFanSpeedMenu(), LocalizationService.T("Tray.AllFanSpeeds"));
        AppendCommand(fanControlMenu, RestoreDefaultCommand, LocalizationService.T("Tray.RestoreDefault"));
        return fanControlMenu;
    }

    private IntPtr BuildPresetMenu()
    {
        var presetMenu = CreatePopupMenu();
        if (presetMenu == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create preset tray submenu.");
        }

        _presetCommands.Clear();
        var presets = new SettingsStore().Load().Presets;
        for (var index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            var commandId = PresetCommandBase + index;
            _presetCommands[commandId] = preset.Clone();
            AppendCommand(presetMenu, commandId, BuildPresetMenuLabel(preset));
        }

        return presetMenu;
    }

    private static IntPtr BuildAllFanSpeedMenu()
    {
        var speedMenu = CreatePopupMenu();
        if (speedMenu == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create all-fan speed tray submenu.");
        }

        AppendCommand(speedMenu, Fans20Command, LocalizationService.T("Tray.AllFans20"));
        AppendCommand(speedMenu, Fans35Command, LocalizationService.T("Tray.AllFans35"));
        AppendCommand(speedMenu, Fans50Command, LocalizationService.T("Tray.AllFans50"));
        return speedMenu;
    }

    private static string BuildPresetMenuLabel(FanPreset preset)
    {
        return preset.IsManual
            ? $"{preset.DisplayName} - {preset.Percent:0}%"
            : preset.DisplayName;
    }

    private void HandleCommand(int command)
    {
        if (_presetCommands.TryGetValue(command, out var preset))
        {
            PresetRequested?.Invoke(this, preset);
            return;
        }

        switch (command)
        {
            case RestoreCommand:
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Fans20Command:
                FanPercentRequested?.Invoke(this, 20);
                break;
            case Fans35Command:
                FanPercentRequested?.Invoke(this, 35);
                break;
            case Fans50Command:
                FanPercentRequested?.Invoke(this, 50);
                break;
            case RestoreDefaultCommand:
                RestoreDefaultRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SettingsCommand:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case ExitCommand:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static void AppendCommand(IntPtr menu, int command, string text)
    {
        if (!AppendMenu(menu, MfString, new UIntPtr((uint)command), text))
        {
            throw new InvalidOperationException($"Unable to append tray menu command: {text}");
        }
    }

    private static void AppendSeparator(IntPtr menu)
    {
        if (!AppendMenu(menu, MfSeparator, UIntPtr.Zero, string.Empty))
        {
            throw new InvalidOperationException("Unable to append tray menu separator.");
        }
    }

    private static void AppendPopup(IntPtr menu, IntPtr submenu, string text)
    {
        var submenuHandle = new UIntPtr(unchecked((ulong)submenu.ToInt64()));
        if (!AppendMenu(menu, MfPopup, submenuHandle, text))
        {
            throw new InvalidOperationException($"Unable to append tray menu group: {text}");
        }
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr subclassId, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr subclassId);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr idNewItem, string newItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr refData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
