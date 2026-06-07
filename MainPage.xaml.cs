using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DellR730xdFanControlCenter;

public sealed partial class MainPage : Page
{
    private readonly SettingsStore _settingsStore = new();
    private readonly IpmiCommandService _ipmi = new();
    private readonly DispatcherTimer _autoPolicyTimer = new();
    private readonly DispatcherTimer _sensorPollingTimer = new();
    private readonly SemaphoreSlim _ipmiOperationLock = new(1, 1);
    private AppSettings _settings = new();
    private bool _syncingAllFanControls;
    private bool _autoPolicyTickRunning;
    private bool _sensorPollingTickRunning;

    public MainPage()
    {
        InitializeComponent();

        Sensors = [];
        Logs = [];
        FanChannels = [];
        TemperatureTiles = [];
        FanTiles = [];
        PowerTiles = [];
        StatusTiles = [];

        _ipmi.CommandCompleted += OnCommandCompleted;
        _autoPolicyTimer.Tick += OnAutoPolicyTimerTick;
        _sensorPollingTimer.Tick += OnSensorPollingTimerTick;
    }

    public ObservableCollection<SensorReading> Sensors { get; }

    public ObservableCollection<LogEntry> Logs { get; }

    public ObservableCollection<FanChannelViewModel> FanChannels { get; }

    public ObservableCollection<DashboardTileViewModel> TemperatureTiles { get; }

    public ObservableCollection<DashboardTileViewModel> FanTiles { get; }

    public ObservableCollection<DashboardTileViewModel> PowerTiles { get; }

    public ObservableCollection<DashboardTileViewModel> StatusTiles { get; }

    public bool MinimizeToTrayOnClose => MinimizeToTraySwitch?.IsOn ?? true;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        LoadSettingsToControls(_settings);
        RebuildFanChannels();
        ApplyTheme(_settings.Theme);
        AddLog("Info", "Application loaded. Settings are ready.");
        if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            _ = ConnectAndStartPollingAsync();
        }
    }

    public void ShowSettingsView()
    {
        SelectView("Settings");
    }

    public Task ApplyQuickFanSpeedAsync(int percent)
    {
        AllFanSlider.Value = percent;
        AllFanPercentBox.Value = percent;
        return ApplyAllFansAsync(percent);
    }

    public Task RestoreDefaultManualFromTrayAsync()
    {
        return RestoreDefaultManualAsync();
    }

    private void LoadSettingsToControls(AppSettings settings)
    {
        HostBox.Text = settings.Host;
        UserNameBox.Text = settings.UserName;
        PasswordBox.Password = settings.RememberPassword ? _settingsStore.UnprotectPassword(settings.ProtectedPassword) : string.Empty;
        RememberPasswordSwitch.IsOn = settings.RememberPassword;
        IpmiToolPathBox.Text = IpmiCommandService.ResolveToolPath(settings.IpmiToolPath);
        FanCountBox.Value = settings.FanCount;
        CommandTimeoutBox.Value = settings.CommandTimeoutSeconds;
        SensorRefreshSecondsBox.Value = settings.SensorRefreshSeconds;
        MinimizeToTraySwitch.IsOn = settings.MinimizeToTrayOnClose;
        IndividualFanSwitch.IsOn = settings.EnableIndividualFanTargets;
        TargetTempBox.Value = settings.TargetCpuTemperatureCelsius;
        HighTempBox.Value = settings.HighCpuTemperatureCelsius;
        EmergencyTempBox.Value = settings.EmergencyCpuTemperatureCelsius;
        AutoMinFanBox.Value = settings.AutoMinimumFanPercent;
        AutoMaxFanBox.Value = settings.AutoMaximumFanPercent;
        AllFanSlider.Value = settings.DefaultAllFanPercent;
        AllFanPercentBox.Value = settings.DefaultAllFanPercent;
        CurrentTargetText.Text = settings.Host;

        ThemeComboBox.SelectedIndex = settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
    }

    private void CaptureSettingsFromControls()
    {
        _settings.Host = HostBox.Text.Trim();
        _settings.UserName = UserNameBox.Text.Trim();
        _settings.RememberPassword = RememberPasswordSwitch.IsOn;
        _settings.ProtectedPassword = RememberPasswordSwitch.IsOn
            ? _settingsStore.ProtectPassword(PasswordBox.Password)
            : string.Empty;
        _settings.IpmiToolPath = AppSettings.BundledIpmiToolRelativePath;
        _settings.FanCount = ReadInt(FanCountBox, "Fan count");
        _settings.CommandTimeoutSeconds = ReadInt(CommandTimeoutBox, "Command timeout");
        _settings.SensorRefreshSeconds = Math.Max(1, ReadInt(SensorRefreshSecondsBox, "Sensor refresh seconds"));
        _settings.MinimizeToTrayOnClose = MinimizeToTraySwitch.IsOn;
        _settings.EnableIndividualFanTargets = IndividualFanSwitch.IsOn;
        _settings.TargetCpuTemperatureCelsius = ReadDouble(TargetTempBox, "Target temperature");
        _settings.HighCpuTemperatureCelsius = ReadDouble(HighTempBox, "High temperature");
        _settings.EmergencyCpuTemperatureCelsius = ReadDouble(EmergencyTempBox, "Emergency temperature");
        _settings.AutoMinimumFanPercent = ReadInt(AutoMinFanBox, "Auto minimum fan percent");
        _settings.AutoMaximumFanPercent = ReadInt(AutoMaxFanBox, "Auto maximum fan percent");
        _settings.Theme = GetSelectedTheme();

        if (_settings.AutoMinimumFanPercent > _settings.AutoMaximumFanPercent)
        {
            throw new InvalidOperationException("Auto minimum fan percent must be less than or equal to auto maximum fan percent.");
        }

        if (_settings.TargetCpuTemperatureCelsius >= _settings.HighCpuTemperatureCelsius ||
            _settings.HighCpuTemperatureCelsius >= _settings.EmergencyCpuTemperatureCelsius)
        {
            throw new InvalidOperationException("Temperature thresholds must be ordered as Target < High < Emergency.");
        }

        CurrentTargetText.Text = _settings.Host;
        ApplyTheme(_settings.Theme);
        IpmiToolPathBox.Text = IpmiCommandService.ResolveToolPath(_settings.IpmiToolPath);
    }

    private void PersistSettingsFromControls()
    {
        CaptureSettingsFromControls();
        _settingsStore.Save(_settings);
    }

    private IdracProfile ReadProfile(bool saveSettings = true)
    {
        if (saveSettings)
        {
            PersistSettingsFromControls();
        }
        else
        {
            CaptureSettingsFromControls();
        }

        return new IdracProfile
        {
            Host = _settings.Host,
            UserName = _settings.UserName,
            Password = PasswordBox.Password,
            IpmiToolPath = _settings.IpmiToolPath,
            CommandTimeoutSeconds = _settings.CommandTimeoutSeconds,
        };
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        await ConnectAndStartPollingAsync();
    }

    private async void OnRefreshSensorsClick(object sender, RoutedEventArgs e)
    {
        await RefreshSensorsAsync();
    }

    private async Task RefreshSensorsAsync()
    {
        await RunUiCommandAsync("Refreshing sensors", async token =>
        {
            await RefreshSensorsCoreAsync(ReadProfile(), token);
            ShowStatus("传感器已刷新 / Sensors refreshed", InfoBarSeverity.Success);
        });
    }

    private async Task ConnectAndStartPollingAsync()
    {
        await RunUiCommandAsync("Connecting to iDRAC and starting polling", async token =>
        {
            var profile = ReadProfile();
            await _ipmi.TestConnectionAsync(profile, token);
            StartSensorPolling();
            await RefreshSensorsCoreAsync(profile, token);
            ShowStatus("已连接并开始自动轮询 / Connected and polling", InfoBarSeverity.Success);
        });
    }

    private void StartSensorPolling()
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingTimer.Start();
        ConnectionStateText.Text = $"已连接，{intervalSeconds}s 轮询 / Connected";
        AddLog("Info", $"Sensor polling started at {intervalSeconds}s interval.");
    }

    private void StopSensorPolling(string reason)
    {
        _sensorPollingTimer.Stop();
        ConnectionStateText.Text = "已断开 / Disconnected";
        AddLog("Warn", $"Sensor polling stopped: {reason}");
    }

    private async void OnSensorPollingTimerTick(object? sender, object e)
    {
        if (_sensorPollingTickRunning)
        {
            return;
        }

        _sensorPollingTickRunning = true;
        if (!await _ipmiOperationLock.WaitAsync(0))
        {
            _sensorPollingTickRunning = false;
            return;
        }

        try
        {
            await RefreshSensorsCoreAsync(ReadProfile(saveSettings: false), CancellationToken.None);
        }
        catch (Exception ex)
        {
            StopSensorPolling(ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            _ipmiOperationLock.Release();
            _sensorPollingTickRunning = false;
        }
    }

    private async Task RefreshSensorsCoreAsync(IdracProfile profile, CancellationToken token)
    {
        var readings = await _ipmi.ReadSensorsAsync(profile, token);
        ReplaceSensors(readings);
        UpdateMetricSummaries();
        LastPollText.Text = $"最后轮询 / Last poll {DateTime.Now:HH:mm:ss}";
        ConnectionStateText.Text = $"已连接，{Math.Max(1, _settings.SensorRefreshSeconds)}s 轮询 · {DateTime.Now:HH:mm:ss}";
    }

    private async void OnSetAllFansClick(object sender, RoutedEventArgs e)
    {
        await ApplyAllFansAsync(ReadInt(AllFanPercentBox, "All fan percent"));
    }

    private async Task ApplyAllFansAsync(int percent)
    {
        await RunUiCommandAsync($"Setting all fans to {percent}%", async token =>
        {
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            ModeSummaryText.Text = "Manual";
            ShowStatus($"全部风扇已设置为 {percent}% / All fans set to {percent}%", InfoBarSeverity.Success);
        });
    }

    private async void OnSetSingleFanClick(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableIndividualFanTargets)
        {
            ShowStatus("单风扇 raw target 未启用，请先在设置中开启。", InfoBarSeverity.Warning);
            return;
        }

        if (sender is not Button button || button.Tag is not int fanIndex)
        {
            ShowStatus("无法识别风扇编号。", InfoBarSeverity.Error);
            return;
        }

        var channel = FanChannels.First(fan => fan.Index == fanIndex);
        var percent = CheckedPercent(channel.Percent, $"Fan {fanIndex} percent");

        await RunUiCommandAsync($"Setting Fan {fanIndex} to {percent}%", async token =>
        {
            await _ipmi.SetSingleFanManualSpeedAsync(ReadProfile(), fanIndex, percent, token);
            ModeSummaryText.Text = "Manual";
            ShowStatus($"Fan {fanIndex} 已设置为 {percent}%", InfoBarSeverity.Success);
        });
    }

    private async void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            await ApplyQuickFanSpeedAsync(percent);
        }
    }

    private async void OnResetAutoClick(object sender, RoutedEventArgs e)
    {
        await ResetDellAutomaticModeAsync();
    }

    private async void OnRestoreDefaultClick(object sender, RoutedEventArgs e)
    {
        await RestoreDefaultManualAsync();
    }

    private async Task RestoreDefaultManualAsync()
    {
        await RunUiCommandAsync($"Restoring manual {AppSettings.LocalDefaultManualFanPercent}% default", async token =>
        {
            PersistSettingsFromControls();
            var percent = AppSettings.LocalDefaultManualFanPercent;
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            ModeSummaryText.Text = $"Manual {percent}%";
            ShowStatus($"已还原为手动 {percent}% / Restored manual {percent}%", InfoBarSeverity.Success);
        });
    }

    private async Task ResetDellAutomaticModeAsync()
    {
        await RunUiCommandAsync("Resetting Dell automatic fan mode", async token =>
        {
            await _ipmi.SetDellAutomaticModeAsync(ReadProfile(), token);
            ModeSummaryText.Text = "Dell Auto";
            ShowStatus("已切回 Dell 自动风扇模式 / Dell automatic mode restored", InfoBarSeverity.Success);
        });
    }

    private async void OnStartAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistSettingsFromControls();
            _autoPolicyTimer.Interval = TimeSpan.FromSeconds(_settings.SensorRefreshSeconds);
            _autoPolicyTimer.Start();
            StartAutoButton.IsEnabled = false;
            StopAutoButton.IsEnabled = true;
            AutoPolicySummaryText.Text = "软件自动策略运行中";
            ModeSummaryText.Text = "Smart Auto";
            AddLog("Info", "Smart auto policy started.");
            await RunAutoPolicyOnceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnStopAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        _autoPolicyTimer.Stop();
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        AutoPolicySummaryText.Text = "软件自动策略未运行";
        AddLog("Info", "Smart auto policy stopped.");
    }

    private async void OnAutoPolicyTimerTick(object? sender, object e)
    {
        if (_autoPolicyTickRunning)
        {
            AddLog("Warn", "Skipped auto policy tick because the previous tick is still running.");
            return;
        }

        _autoPolicyTickRunning = true;
        try
        {
            await RunAutoPolicyOnceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _autoPolicyTickRunning = false;
        }
    }

    private async Task RunAutoPolicyOnceAsync(CancellationToken cancellationToken)
    {
        var profile = ReadProfile();
        var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken);
        ReplaceSensors(readings);
        UpdateMetricSummaries();

        var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(readings);
        var percent = CalculateAutoFanPercent(cpuTemp);

        if (cpuTemp >= _settings.EmergencyCpuTemperatureCelsius)
        {
            await _ipmi.SetDellAutomaticModeAsync(profile, cancellationToken);
            AddLog("Warn", $"CPU {cpuTemp:0.0} °C reached emergency threshold; Dell auto mode restored.");
            ModeSummaryText.Text = "Dell Auto";
            return;
        }

        await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);
        ModeSummaryText.Text = $"Smart {percent}%";
        AddLog("Info", $"CPU {cpuTemp:0.0} °C -> all fans {percent}%.");
    }

    private int CalculateAutoFanPercent(double cpuTemp)
    {
        if (cpuTemp <= _settings.TargetCpuTemperatureCelsius)
        {
            return _settings.AutoMinimumFanPercent;
        }

        if (cpuTemp >= _settings.HighCpuTemperatureCelsius)
        {
            return _settings.AutoMaximumFanPercent;
        }

        var span = _settings.HighCpuTemperatureCelsius - _settings.TargetCpuTemperatureCelsius;
        var progress = (cpuTemp - _settings.TargetCpuTemperatureCelsius) / span;
        var fanSpan = _settings.AutoMaximumFanPercent - _settings.AutoMinimumFanPercent;
        return (int)Math.Round(_settings.AutoMinimumFanPercent + fanSpan * progress, MidpointRounding.AwayFromZero);
    }

    private async void OnVisitIdracClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistSettingsFromControls();
            var url = $"https://{_settings.Host}/";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            AddLog("Info", $"Opened iDRAC web console: {url}");
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }

        await Task.CompletedTask;
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistSettingsFromControls();
            RebuildFanChannels();
            ShowStatus("设置已保存 / Settings saved", InfoBarSeverity.Success);
            AddLog("Info", "Settings saved.");

            if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                await ConnectAndStartPollingAsync();
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnAllFanSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingAllFanControls)
        {
            return;
        }

        _syncingAllFanControls = true;
        AllFanPercentBox.Value = Math.Round(e.NewValue);
        _syncingAllFanControls = false;
    }

    private void OnAllFanNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingAllFanControls || double.IsNaN(args.NewValue))
        {
            return;
        }

        _syncingAllFanControls = true;
        AllFanSlider.Value = args.NewValue;
        _syncingAllFanControls = false;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            SelectView(tag);
        }
    }

    private void SelectView(string tag)
    {
        OverviewView.Visibility = tag == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ControlView.Visibility = tag == "Control" ? Visibility.Visible : Visibility.Collapsed;
        SensorsView.Visibility = tag == "Sensors" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in ShellNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            item.IsSelected = item.Tag is string itemTag && itemTag == tag;
        }
    }

    private async Task RunUiCommandAsync(string description, Func<CancellationToken, Task> command)
    {
        var lockTaken = false;
        try
        {
            AddLog("Info", description);
            using var cancellation = new CancellationTokenSource();
            await _ipmiOperationLock.WaitAsync(cancellation.Token);
            lockTaken = true;
            await command(cancellation.Token);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            if (lockTaken)
            {
                _ipmiOperationLock.Release();
            }
        }
    }

    private void ReplaceSensors(System.Collections.Generic.IEnumerable<SensorReading> readings)
    {
        Sensors.Clear();
        foreach (var reading in readings)
        {
            Sensors.Add(reading);
        }
    }

    private void UpdateMetricSummaries()
    {
        var temperatureReadings = Sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(temperatureReadings);
        CpuTemperatureText.Text = $"{cpuTemp:0.0} °C";
        ReplaceTiles(
            TemperatureTiles,
            temperatureReadings.Select(sensor => new DashboardTileViewModel
            {
                Title = BuildSensorTitle(sensor),
                Value = sensor.NumericValue!.Value.ToString("0.#", CultureInfo.InvariantCulture),
                Unit = "°C",
                Subtitle = BuildSensorSubtitle(sensor),
                Status = sensor.Status,
            }));

        var fanReadings = Sensors
            .Where(sensor => sensor.Key.StartsWith("Fan", StringComparison.OrdinalIgnoreCase) &&
                             sensor.Unit.Contains("RPM", StringComparison.OrdinalIgnoreCase) &&
                             sensor.NumericValue.HasValue)
            .ToList();

        FanSummaryText.Text = $"{fanReadings.Count} fans";
        FanRpmSummaryText.Text = fanReadings.Count == 0
            ? "未读取到风扇 RPM"
            : $"{fanReadings.Min(f => f.NumericValue):0} - {fanReadings.Max(f => f.NumericValue):0} RPM";
        ReplaceTiles(
            FanTiles,
            fanReadings.Select(sensor => new DashboardTileViewModel
            {
                Title = sensor.Key,
                Value = sensor.NumericValue!.Value.ToString("0", CultureInfo.InvariantCulture),
                Unit = "RPM",
                Subtitle = BuildSensorSubtitle(sensor),
                Status = sensor.Status,
            }));

        var powerAndHealth = Sensors
            .Where(sensor => IsPowerSensor(sensor) || IsHealthSensor(sensor))
            .Take(14)
            .Select(sensor => new DashboardTileViewModel
            {
                Title = BuildSensorTitle(sensor),
                Value = string.IsNullOrWhiteSpace(sensor.Value) ? "--" : sensor.Value,
                Unit = sensor.Unit,
                Subtitle = BuildSensorSubtitle(sensor),
                Status = sensor.Status,
            });
        ReplaceTiles(PowerTiles, powerAndHealth);
    }

    private static void ReplaceTiles(
        ObservableCollection<DashboardTileViewModel> target,
        System.Collections.Generic.IEnumerable<DashboardTileViewModel> tiles)
    {
        target.Clear();
        foreach (var tile in tiles)
        {
            target.Add(tile);
        }
    }

    private static bool IsTemperatureSensor(SensorReading sensor)
    {
        return sensor.Unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Temp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerSensor(SensorReading sensor)
    {
        return sensor.Unit.Contains("Watts", StringComparison.OrdinalIgnoreCase) ||
               sensor.Unit.Contains("Volts", StringComparison.OrdinalIgnoreCase) ||
               sensor.Unit.Contains("Amps", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Usage", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Current", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Voltage", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Pwr", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHealthSensor(SensorReading sensor)
    {
        return sensor.Key.Contains("Redundancy", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Battery", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Intrusion", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Power Optimized", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSensorTitle(SensorReading sensor)
    {
        if (string.IsNullOrWhiteSpace(sensor.Entity) ||
            !sensor.Key.Equals("Temp", StringComparison.OrdinalIgnoreCase))
        {
            return sensor.Key;
        }

        return $"{sensor.Key} {sensor.Entity}";
    }

    private static string BuildSensorSubtitle(SensorReading sensor)
    {
        var parts = new[] { sensor.SensorId, sensor.Entity }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var subtitle = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(subtitle) ? "SDR" : subtitle;
    }

    private void RebuildFanChannels()
    {
        FanChannels.Clear();
        var fanCount = ReadInt(FanCountBox, "Fan count");
        var defaultPercent = ReadInt(AllFanPercentBox, "Default all fan percent");
        for (var fan = 1; fan <= fanCount; fan++)
        {
            FanChannels.Add(new FanChannelViewModel(fan, defaultPercent));
        }
    }

    private void OnCommandCompleted(object? sender, CommandTraceEventArgs e)
    {
        var level = e.Succeeded ? "OK" : "Fail";
        DispatcherQueue.TryEnqueue(() => AddLog(level, $"{e.CommandLine} [{e.ExitCode}] {e.Elapsed.TotalSeconds:0.0}s"));
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void ShowFailure(Exception ex)
    {
        AddLog("Error", ex.Message);
        ShowStatus(ex.Message, InfoBarSeverity.Error);
    }

    private void AddLog(string level, string message)
    {
        Logs.Insert(0, new LogEntry { Level = level, Message = message });
        while (Logs.Count > 80)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private static int ReadInt(NumberBox numberBox, string fieldName)
    {
        if (double.IsNaN(numberBox.Value))
        {
            throw new InvalidOperationException($"{fieldName} is empty.");
        }

        return CheckedPercentLikeInteger(numberBox.Value, fieldName);
    }

    private static int CheckedPercent(double value, string fieldName)
    {
        var percent = CheckedPercentLikeInteger(value, fieldName);
        AppSettings.ValidatePercent(percent, fieldName);
        return percent;
    }

    private static int CheckedPercentLikeInteger(double value, string fieldName)
    {
        if (double.IsNaN(value))
        {
            throw new InvalidOperationException($"{fieldName} is empty.");
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static double ReadDouble(NumberBox numberBox, string fieldName)
    {
        if (double.IsNaN(numberBox.Value))
        {
            throw new InvalidOperationException($"{fieldName} is empty.");
        }

        return numberBox.Value;
    }

    private string GetSelectedTheme()
    {
        return ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "Default";
    }

    private void ApplyTheme(string theme)
    {
        RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
