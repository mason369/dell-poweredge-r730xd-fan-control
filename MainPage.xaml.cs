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
    private bool _loadingSettings;
    private bool _autoPolicyRunning;
    private bool _hasDisconnected;
    private DateTime? _lastPollTime;
    private TimeSpan? _lastPollDuration;
    private DateTimeOffset _lastPollingWarningAt = DateTimeOffset.MinValue;
    private bool _pollingWasDegraded;
    private string _modeSummaryKey = "Mode.Idle";
    private object[] _modeSummaryArgs = Array.Empty<object>();

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
        var shouldShowSettingsOnStart = !_settingsStore.SettingsFileExists;
        _settings = _settingsStore.Load();
        LocalizationService.SetLanguage(_settings.Language);
        LoadSettingsToControls(_settings);
        ApplyTheme(_settings.Theme);
        ApplyLocalization();
        RebuildFanChannels();
        AddLog(T("Log.Info"), T("Status.Loaded"));
        shouldShowSettingsOnStart = shouldShowSettingsOnStart || string.IsNullOrWhiteSpace(PasswordBox.Password);
        if (shouldShowSettingsOnStart)
        {
            SelectView("Settings");
            ShowStatus(T("Status.FirstRunSettingsRequired"), InfoBarSeverity.Informational);
        }
        else
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
        _loadingSettings = true;
        try
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

            LanguageComboBox.SelectedIndex = settings.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        finally
        {
            _loadingSettings = false;
        }
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
        _settings.Language = GetSelectedLanguage();
        LocalizationService.SetLanguage(_settings.Language);
        _settings.FanCount = ReadInt(FanCountBox, T("Field.FanCount"));
        _settings.CommandTimeoutSeconds = ReadInt(CommandTimeoutBox, T("Field.CommandTimeout"));
        _settings.SensorRefreshSeconds = Math.Max(1, ReadInt(SensorRefreshSecondsBox, T("Field.SensorRefreshSeconds")));
        _settings.MinimizeToTrayOnClose = MinimizeToTraySwitch.IsOn;
        _settings.EnableIndividualFanTargets = IndividualFanSwitch.IsOn;
        _settings.TargetCpuTemperatureCelsius = ReadDouble(TargetTempBox, T("Field.TargetTemperature"));
        _settings.HighCpuTemperatureCelsius = ReadDouble(HighTempBox, T("Field.HighTemperature"));
        _settings.EmergencyCpuTemperatureCelsius = ReadDouble(EmergencyTempBox, T("Field.EmergencyTemperature"));
        _settings.AutoMinimumFanPercent = ReadInt(AutoMinFanBox, T("Field.AutoMinimumFanPercent"));
        _settings.AutoMaximumFanPercent = ReadInt(AutoMaxFanBox, T("Field.AutoMaximumFanPercent"));
        _settings.Theme = GetSelectedTheme();

        if (_settings.AutoMinimumFanPercent > _settings.AutoMaximumFanPercent)
        {
            throw new InvalidOperationException(T("Validation.AutoFanRange"));
        }

        if (_settings.TargetCpuTemperatureCelsius >= _settings.HighCpuTemperatureCelsius ||
            _settings.HighCpuTemperatureCelsius >= _settings.EmergencyCpuTemperatureCelsius)
        {
            throw new InvalidOperationException(T("Validation.TemperatureOrder"));
        }

        CurrentTargetText.Text = _settings.Host;
        ApplyTheme(_settings.Theme);
        ApplyLocalization();
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

        return BuildProfileFromSettings();
    }

    private IdracProfile BuildProfileFromSettings()
    {
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
        await RunUiCommandAsync(T("Status.RefreshingSensors"), async token =>
        {
            await RefreshSensorsCoreAsync(ReadProfile(), token);
            ShowStatus(T("Status.SensorsRefreshed"), InfoBarSeverity.Success);
        });
    }

    private async Task ConnectAndStartPollingAsync()
    {
        await RunUiCommandAsync(T("Status.Connecting"), async token =>
        {
            var profile = ReadProfile();
            await _ipmi.TestConnectionAsync(profile, token);
            var elapsed = await RefreshSensorsCoreAsync(profile, token);
            StartSensorPolling();
            ShowStatus(T("Status.ConnectedPolling"), InfoBarSeverity.Success);
            CheckSensorPollingLatency(elapsed);
        });
    }

    private void StartSensorPolling()
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingTimer.Start();
        _hasDisconnected = false;
        _pollingWasDegraded = false;
        _lastPollingWarningAt = DateTimeOffset.MinValue;
        UpdatePollingStatusTexts();
        AddLog(T("Log.Info"), F("Status.PollingStarted", intervalSeconds));
    }

    private void StopSensorPolling(string reason)
    {
        _sensorPollingTimer.Stop();
        _hasDisconnected = true;
        UpdatePollingStatusTexts();
        AddLog(T("Log.Warn"), F("Status.PollingStopped", reason));
    }

    private async void OnSensorPollingTimerTick(object? sender, object e)
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        if (_sensorPollingTickRunning)
        {
            ReportPollingWarning(F("Status.PollingSkippedPreviousRunning", intervalSeconds));
            return;
        }

        _sensorPollingTickRunning = true;
        if (!await _ipmiOperationLock.WaitAsync(0))
        {
            _sensorPollingTickRunning = false;
            ReportPollingWarning(F("Status.PollingSkippedIpmiBusy", intervalSeconds));
            return;
        }

        try
        {
            var elapsed = await RefreshSensorsCoreAsync(BuildProfileFromSettings(), CancellationToken.None);
            CheckSensorPollingLatency(elapsed);
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

    private async Task<TimeSpan> RefreshSensorsCoreAsync(IdracProfile profile, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var readings = await _ipmi.ReadSensorsAsync(profile, token);
        stopwatch.Stop();
        ReplaceSensors(readings);
        UpdateMetricSummaries();
        _lastPollTime = DateTime.Now;
        _lastPollDuration = stopwatch.Elapsed;
        UpdatePollingStatusTexts();
        return stopwatch.Elapsed;
    }

    private async void OnSetAllFansClick(object sender, RoutedEventArgs e)
    {
        await ApplyAllFansAsync(ReadInt(AllFanPercentBox, T("Field.AllFanPercent")));
    }

    private async Task ApplyAllFansAsync(int percent)
    {
        await RunUiCommandAsync(F("Status.SetAllFans", percent), async token =>
        {
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            SetModeSummary("Mode.Manual");
            ShowStatus(F("Status.AllFansSet", percent), InfoBarSeverity.Success);
        });
    }

    private async void OnSetSingleFanClick(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableIndividualFanTargets)
        {
            ShowStatus(T("Status.SingleFanDisabled"), InfoBarSeverity.Warning);
            return;
        }

        if (sender is not Button button || button.Tag is not int fanIndex)
        {
            ShowStatus(T("Status.UnknownFan"), InfoBarSeverity.Error);
            return;
        }

        var channel = FanChannels.First(fan => fan.Index == fanIndex);
        var percent = CheckedPercent(channel.Percent, F("Field.SingleFanPercent", fanIndex));

        await RunUiCommandAsync(F("Status.FanSet", fanIndex, percent), async token =>
        {
            await _ipmi.SetSingleFanManualSpeedAsync(ReadProfile(), fanIndex, percent, token);
            SetModeSummary("Mode.Manual");
            ShowStatus(F("Status.FanSet", fanIndex, percent), InfoBarSeverity.Success);
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
        await RunUiCommandAsync(F("Status.RestoringDefault", AppSettings.LocalDefaultManualFanPercent), async token =>
        {
            PersistSettingsFromControls();
            var percent = AppSettings.LocalDefaultManualFanPercent;
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            SetModeSummary("Mode.ManualPercent", percent);
            ShowStatus(F("Status.RestoredDefault", percent), InfoBarSeverity.Success);
        });
    }

    private async Task ResetDellAutomaticModeAsync()
    {
        await RunUiCommandAsync(T("Status.ResettingDellAuto"), async token =>
        {
            await _ipmi.SetDellAutomaticModeAsync(ReadProfile(), token);
            SetModeSummary("Mode.DellAuto");
            ShowStatus(T("Status.DellAutoRestored"), InfoBarSeverity.Success);
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
            SetAutoPolicySummary(true);
            SetModeSummary("Mode.SmartAuto");
            AddLog(T("Log.Info"), T("Status.AutoStarted"));
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
        SetAutoPolicySummary(false);
        AddLog(T("Log.Info"), T("Status.AutoStopped"));
    }

    private async void OnAutoPolicyTimerTick(object? sender, object e)
    {
        if (_autoPolicyTickRunning)
        {
            AddLog(T("Log.Warn"), T("Status.AutoTickSkipped"));
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
            AddLog(T("Log.Warn"), F("Status.EmergencyAuto", cpuTemp));
            SetModeSummary("Mode.DellAuto");
            return;
        }

        await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);
        SetModeSummary("Mode.SmartPercent", percent);
        AddLog(T("Log.Info"), F("Status.SmartFanApplied", cpuTemp, percent));
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
            AddLog(T("Log.Info"), F("Status.OpenedIdrac", url));
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
            ApplyLocalization();
            ShowStatus(T("Status.SettingsSaved"), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), T("Status.SettingsSaved"));

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

    private void OnIndividualFanSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _settings.EnableIndividualFanTargets = IndividualFanSwitch.IsOn;
        UpdateIndividualFanWarning();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            SelectView(tag);
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        try
        {
            var language = GetSelectedLanguage();
            LocalizationService.SetLanguage(language);
            _settings.Language = language;
            _settingsStore.Save(_settings);
            ApplyLocalization();
            AddLog(T("Log.Info"), F("Status.LanguageChanged", GetSelectedLanguageDisplayName(language)));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
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
            AddLog(T("Log.Info"), description);
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

        FanSummaryText.Text = F("Overview.FansCount", fanReadings.Count);
        FanRpmSummaryText.Text = fanReadings.Count == 0
            ? T("Overview.NoFanRpm")
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
        var fanCount = ReadInt(FanCountBox, T("Field.FanCount"));
        var defaultPercent = ReadInt(AllFanPercentBox, T("Field.DefaultAllFanPercent"));
        for (var fan = 1; fan <= fanCount; fan++)
        {
            FanChannels.Add(new FanChannelViewModel(fan, defaultPercent));
        }
    }

    private void OnCommandCompleted(object? sender, CommandTraceEventArgs e)
    {
        var level = e.Succeeded ? T("Log.Ok") : T("Log.Fail");
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
        AddLog(T("Log.Error"), ex.Message);
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

    private int ReadInt(NumberBox numberBox, string fieldName)
    {
        if (double.IsNaN(numberBox.Value))
        {
            throw new InvalidOperationException(F("Validation.Empty", fieldName));
        }

        return CheckedPercentLikeInteger(numberBox.Value, fieldName);
    }

    private int CheckedPercent(double value, string fieldName)
    {
        var percent = CheckedPercentLikeInteger(value, fieldName);
        AppSettings.ValidatePercent(percent, fieldName);
        return percent;
    }

    private int CheckedPercentLikeInteger(double value, string fieldName)
    {
        if (double.IsNaN(value))
        {
            throw new InvalidOperationException(F("Validation.Empty", fieldName));
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private double ReadDouble(NumberBox numberBox, string fieldName)
    {
        if (double.IsNaN(numberBox.Value))
        {
            throw new InvalidOperationException(F("Validation.Empty", fieldName));
        }

        return numberBox.Value;
    }

    private string GetSelectedTheme()
    {
        return ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "Default";
    }

    private string GetSelectedLanguage()
    {
        return LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : LocalizationService.DefaultLanguage;
    }

    private static string GetSelectedLanguageDisplayName(string language)
    {
        return LocalizationService.SupportedLanguages.First(option => option.Code == language).DisplayName;
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

    private void ApplyLocalization()
    {
        Localization.Apply(this);
        App.CurrentWindow?.ApplyLocalization();
        CurrentTargetText.Text = _settings.Host;
        FanSummaryText.Text = F("Overview.FansCount", _settings.FanCount);
        if (Sensors.Count == 0)
        {
            FanRpmSummaryText.Text = T("State.WaitingRefresh");
        }
        else
        {
            UpdateMetricSummaries();
        }

        SetModeSummary(_modeSummaryKey, _modeSummaryArgs);
        SetAutoPolicySummary(_autoPolicyRunning);
        UpdatePollingStatusTexts();
        UpdateIndividualFanWarning();

        foreach (var fanChannel in FanChannels)
        {
            fanChannel.RefreshLocalization();
        }
    }

    private void SetModeSummary(string key, params object[] args)
    {
        _modeSummaryKey = key;
        _modeSummaryArgs = args;
        ModeSummaryText.Text = args.Length == 0 ? T(key) : F(key, args);
    }

    private void SetAutoPolicySummary(bool running)
    {
        _autoPolicyRunning = running;
        AutoPolicySummaryText.Text = T(running ? "Status.AutoStarted" : "Status.AutoStopped");
    }

    private void UpdatePollingStatusTexts()
    {
        if (_lastPollTime.HasValue)
        {
            var lastPollTime = _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            LastPollText.Text = _lastPollDuration.HasValue
                ? F("Overview.LastPollWithDuration", lastPollTime, _lastPollDuration.Value.TotalSeconds)
                : F("Overview.LastPoll", lastPollTime);
        }
        else
        {
            LastPollText.Text = T("Overview.WaitingPoll");
        }

        if (_sensorPollingTimer.IsEnabled)
        {
            var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
            ConnectionStateText.Text = _lastPollTime.HasValue
                ? F("State.ConnectedPollingTime", intervalSeconds, _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                : F("State.ConnectedPolling", intervalSeconds);
            return;
        }

        ConnectionStateText.Text = _hasDisconnected ? T("State.Disconnected") : T("State.NotConnected");
    }

    private void CheckSensorPollingLatency(TimeSpan elapsed)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SensorRefreshSeconds));
        if (elapsed > interval)
        {
            ReportPollingWarning(F("Status.PollingLatencyExceeded", elapsed.TotalSeconds, interval.TotalSeconds));
            return;
        }

        if (_pollingWasDegraded)
        {
            _pollingWasDegraded = false;
            var message = F("Status.PollingRecovered", elapsed.TotalSeconds);
            ShowStatus(message, InfoBarSeverity.Success);
            AddLog(T("Log.Info"), message);
        }
    }

    private void ReportPollingWarning(string message)
    {
        _pollingWasDegraded = true;
        var now = DateTimeOffset.Now;
        var throttleSeconds = Math.Max(5, Math.Max(1, _settings.SensorRefreshSeconds) * 2);
        if ((now - _lastPollingWarningAt).TotalSeconds < throttleSeconds)
        {
            return;
        }

        _lastPollingWarningAt = now;
        ShowStatus(message, InfoBarSeverity.Warning);
        AddLog(T("Log.Warn"), message);
    }

    private void UpdateIndividualFanWarning()
    {
        if (IndividualFanInfoBar is null)
        {
            return;
        }

        IndividualFanInfoBar.Severity = _settings.EnableIndividualFanTargets
            ? InfoBarSeverity.Informational
            : InfoBarSeverity.Warning;
        IndividualFanInfoBar.Message = T(_settings.EnableIndividualFanTargets
            ? "Control.IndividualEnabledWarning"
            : "Control.IndividualDisabledWarning");
    }

    private static string T(string key)
    {
        return LocalizationService.T(key);
    }

    private static string F(string key, params object[] args)
    {
        return LocalizationService.Format(key, args);
    }
}
