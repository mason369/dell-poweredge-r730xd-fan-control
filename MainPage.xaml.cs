using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DellR730xdFanControlCenter;

public sealed partial class MainPage : Page
{
    private const int SensorHistoryLimit = 120;
    private static readonly JsonSerializerOptions VisualizationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SettingsStore _settingsStore = new();
    private readonly IpmiCommandService _ipmi = new();
    private readonly DispatcherTimer _autoPolicyTimer = new();
    private readonly DispatcherTimer _sensorPollingTimer = new();
    private readonly SemaphoreSlim _ipmiOperationLock = new(1, 1);
    private readonly List<SensorDashboardHistoryPoint> _sensorHistory = [];
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
    private bool _visualizationInitialized;
    private bool _visualizationReady;
    private string? _activePresetId;
    private string _modeSummaryKey = "Mode.Idle";
    private object[] _modeSummaryArgs = Array.Empty<object>();

    public MainPage()
    {
        InitializeComponent();

        Sensors = [];
        Logs = [];
        FanChannels = [];
        Presets = [];
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

    public ObservableCollection<FanPreset> Presets { get; }

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
        RebuildFanChannels();
        RebuildPresets(_settings.Presets);
        ApplyLocalization();
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

    private async void OnVisualizationWebViewLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeVisualizationAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async Task InitializeVisualizationAsync()
    {
        if (_visualizationInitialized)
        {
            return;
        }

        var dashboardPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Charts", "dashboard.html");
        if (!File.Exists(dashboardPath))
        {
            throw new FileNotFoundException("Visualization dashboard asset was not found.", dashboardPath);
        }

        await VisualizationWebView.EnsureCoreWebView2Async();
        VisualizationWebView.Source = new Uri(dashboardPath);
        _visualizationInitialized = true;
        VisualizationStateText.Text = T("Dashboard.VisualizationLoading");
    }

    private void OnVisualizationNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            var message = F("Dashboard.VisualizationLoadFailed", args.WebErrorStatus);
            AddLog(T("Log.Error"), message);
            ShowStatus(message, InfoBarSeverity.Error);
            return;
        }

        _visualizationReady = true;
        VisualizationStateText.Text = T("Dashboard.VisualizationReady");
        SendVisualizationSnapshot();
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

    public Task ApplyPresetFromTrayAsync(FanPreset preset)
    {
        return ApplyPresetAsync(preset);
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
            NewPresetPercentBox.Value = AppSettings.LocalDefaultManualFanPercent;
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
        _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();

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
        RecordVisualizationHistoryPoint();
        SendVisualizationSnapshot();
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
            MarkActivePreset(null);
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
            MarkActivePreset(null);
            ShowStatus(F("Status.FanSet", fanIndex, percent), InfoBarSeverity.Success);
        });
    }

    private async void OnApplyPresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ApplyPresetAsync(ReadPresetFromSender(sender));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);
            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            _settingsStore.Save(_settings);
            RefreshPresetRows();
            ShowStatus(F("Status.PresetSaved", preset.DisplayName), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F("Status.PresetSaved", preset.DisplayName));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);
            if (!preset.CanDelete)
            {
                throw new InvalidOperationException(T("Validation.CannotDeleteBuiltInPreset"));
            }

            Presets.Remove(preset);
            if (_activePresetId?.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activePresetId = null;
            }

            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            _settingsStore.Save(_settings);
            RefreshPresetRows();
            ShowStatus(F("Status.PresetDeleted", preset.DisplayName), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F("Status.PresetDeleted", preset.DisplayName));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NewPresetNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(T("Validation.PresetNameRequired"));
            }

            var percent = CheckedPercent(NewPresetPercentBox.Value, T("Field.AllFanPercent"));
            var preset = new FanPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = FanPreset.ManualKind,
                Name = name,
                Percent = percent,
            };
            Presets.Add(preset);
            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            _settingsStore.Save(_settings);
            NewPresetNameBox.Text = string.Empty;
            NewPresetPercentBox.Value = AppSettings.LocalDefaultManualFanPercent;
            RefreshPresetRows();
            ShowStatus(F("Status.PresetAdded", preset.DisplayName), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F("Status.PresetAdded", preset.DisplayName));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
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

    private Task ApplyPresetAsync(FanPreset preset)
    {
        if (string.Equals(preset.Kind, FanPreset.ManualKind, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyManualPresetAsync(preset);
        }

        if (string.Equals(preset.Kind, FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreDefaultManualAsync(preset.Id);
        }

        if (string.Equals(preset.Kind, FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
        {
            return ResetDellAutomaticModeAsync(preset.Id);
        }

        throw new InvalidOperationException($"Unsupported fan preset kind: {preset.Kind}");
    }

    private async Task ApplyManualPresetAsync(FanPreset preset)
    {
        var percent = CheckedPercent(preset.Percent, T("Field.AllFanPercent"));
        await RunUiCommandAsync(F("Status.SetAllFans", percent), async token =>
        {
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            SetModeSummary("Mode.PresetManual", preset.DisplayName, percent);
            MarkActivePreset(preset.Id);
            ShowStatus(F("Status.PresetApplied", preset.DisplayName), InfoBarSeverity.Success);
        });
    }

    private async Task RestoreDefaultManualAsync(string? activePresetId = "restore-manual")
    {
        await RunUiCommandAsync(F("Status.RestoringDefault", AppSettings.LocalDefaultManualFanPercent), async token =>
        {
            PersistSettingsFromControls();
            var percent = AppSettings.LocalDefaultManualFanPercent;
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            SetModeSummary("Mode.ManualPercent", percent);
            MarkActivePreset(activePresetId);
            ShowStatus(F("Status.RestoredDefault", percent), InfoBarSeverity.Success);
        });
    }

    private async Task ResetDellAutomaticModeAsync(string? activePresetId = "dell-auto")
    {
        await RunUiCommandAsync(T("Status.ResettingDellAuto"), async token =>
        {
            await _ipmi.SetDellAutomaticModeAsync(ReadProfile(), token);
            SetModeSummary("Mode.DellAuto");
            MarkActivePreset(activePresetId);
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
        RecordVisualizationHistoryPoint();
        SendVisualizationSnapshot();

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
            RebuildPresets(_settings.Presets);
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
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
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

    private void RecordVisualizationHistoryPoint()
    {
        var temperatures = Sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();
        var fans = Sensors
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        _sensorHistory.Add(new SensorDashboardHistoryPoint
        {
            Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            MaxTemperature = temperatures.Count == 0 ? null : Math.Round(temperatures.Max(sensor => sensor.NumericValue!.Value), 1),
            AverageFanRpm = fans.Count == 0 ? null : Math.Round(fans.Average(sensor => sensor.NumericValue!.Value), 0),
            CpuUsage = FindNumericSensorValue("CPU Usage"),
            MemUsage = FindNumericSensorValue("MEM Usage"),
            IoUsage = FindNumericSensorValue("IO Usage"),
            SysUsage = FindNumericSensorValue("SYS Usage"),
            PowerWatts = Sensors.FirstOrDefault(IsPowerWattsSensor)?.NumericValue,
        });

        while (_sensorHistory.Count > SensorHistoryLimit)
        {
            _sensorHistory.RemoveAt(0);
        }
    }

    private void SendVisualizationSnapshot()
    {
        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var payload = BuildVisualizationPayload();
            var json = JsonSerializer.Serialize(payload, VisualizationJsonOptions);
            VisualizationWebView.CoreWebView2.PostWebMessageAsJson(json);
            VisualizationStateText.Text = _lastPollTime.HasValue
                ? F("Dashboard.VisualizationUpdated", _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                : T("Dashboard.VisualizationReady");
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private object BuildVisualizationPayload()
    {
        var temperatures = Sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeTemperature")))
            .ToList();
        var fans = Sensors
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeFan")))
            .ToList();
        var power = Sensors
            .Where(IsPowerSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypePower")))
            .ToList();
        var performance = Sensors
            .Where(IsPerformanceSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypePerformance")))
            .ToList();
        var electrical = Sensors
            .Where(sensor => IsPowerWattsSensor(sensor) || IsVoltageSensor(sensor) || IsCurrentSensor(sensor))
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
            .ToList();
        var allNumeric = Sensors
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
            .ToList();
        var statusSensors = Sensors
            .Where(sensor => !sensor.NumericValue.HasValue)
            .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
            .ToList();
        var health = Sensors
            .Where(IsHealthSensor)
            .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeStatus")))
            .ToList();

        return new
        {
            MessageType = "sensorDashboard",
            Language = _settings.Language,
            Theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light",
            Labels = BuildVisualizationLabels(),
            Summary = BuildVisualizationSummary(),
            Current = new
            {
                Temperatures = temperatures,
                Fans = fans,
                Power = power,
                Performance = performance,
                Electrical = electrical,
                AllNumeric = allNumeric,
                StatusSensors = statusSensors,
                Health = health,
            },
            History = _sensorHistory,
            TypeCounts = BuildTypeCounts(),
            SensorTree = BuildSensorTree(),
        };
    }

    private object BuildVisualizationLabels()
    {
        return new
        {
            Title = T("Dashboard.Title"),
            Subtitle = T("Dashboard.Subtitle"),
            ViewAll = T("Dashboard.ViewAll"),
            ViewTemperature = T("Dashboard.ViewTemperature"),
            ViewFans = T("Dashboard.ViewFans"),
            ViewPower = T("Dashboard.ViewPower"),
            ViewStatus = T("Dashboard.ViewStatus"),
            MaxCpuTemp = T("Overview.MaxCpuTemp"),
            AverageFanRpm = T("Dashboard.AverageFanRpm"),
            HardwareTypes = T("Dashboard.HardwareTypes"),
            LastUpdated = T("Dashboard.LastUpdated"),
            HealthState = T("Dashboard.HealthState"),
            PerformancePeak = T("Dashboard.PerformancePeak"),
            ElectricalReadings = T("Dashboard.ElectricalReadings"),
            WarningCount = T("Dashboard.WarningCount"),
            FanRange = T("Dashboard.FanRange"),
            TemperatureSensors = T("Dashboard.TemperatureSensors"),
            TrendTitle = T("Dashboard.TrendTitle"),
            TrendSubtitle = T("Dashboard.TrendSubtitle"),
            TempUnit = "°C",
            CpuUsage = T("Dashboard.CpuUsage"),
            CurrentSnapshot = T("Dashboard.CurrentSnapshot"),
            OverallTitle = T("Dashboard.OverallTitle"),
            OverallSubtitle = T("Dashboard.OverallSubtitle"),
            TemperatureTitle = T("Dashboard.TemperatureTitle"),
            TemperatureSubtitle = T("Dashboard.TemperatureSubtitle"),
            FanTitle = T("Dashboard.FanTitle"),
            FanSubtitle = T("Dashboard.FanSubtitle"),
            PowerTitle = T("Dashboard.PowerTitle"),
            PowerSubtitle = T("Dashboard.PowerSubtitle"),
            TypeTitle = T("Dashboard.TypeTitle"),
            TypeSubtitle = T("Dashboard.TypeSubtitle"),
            SensorTree = T("Dashboard.SensorTree"),
            StatusByType = T("Dashboard.StatusByType"),
            TypePerformance = T("Dashboard.TypePerformance"),
            TypePower = T("Dashboard.TypePower"),
            TypeVoltage = T("Dashboard.TypeVoltage"),
            TypeCurrent = T("Dashboard.TypeCurrent"),
            Value = T("Dashboard.Value"),
            Status = T("Dashboard.Status"),
            Unit = T("Dashboard.Unit"),
        };
    }

    private object BuildVisualizationSummary()
    {
        var temperatureValues = Sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var fanValues = Sensors
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var performanceValues = Sensors
            .Where(IsPerformanceSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var electricalValues = Sensors
            .Where(sensor => IsPowerWattsSensor(sensor) || IsVoltageSensor(sensor) || IsCurrentSensor(sensor))
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        var okCount = Sensors.Count(IsOkStatus);
        var warningCount = Sensors.Count - okCount;
        var powerWatts = Sensors.FirstOrDefault(IsPowerWattsSensor)?.NumericValue;
        var voltageValues = Sensors.Where(IsVoltageSensor).Where(sensor => sensor.NumericValue.HasValue).Select(sensor => sensor.NumericValue!.Value).ToList();
        var currentValues = Sensors.Where(IsCurrentSensor).Where(sensor => sensor.NumericValue.HasValue).Select(sensor => sensor.NumericValue!.Value).ToList();
        return new
        {
            SensorCount = Sensors.Count,
            TemperatureCount = temperatureValues.Count,
            FanCount = fanValues.Count,
            PerformanceCount = performanceValues.Count,
            ElectricalCount = electricalValues.Count,
            MaxTemperature = temperatureValues.Count == 0 ? (double?)null : Math.Round(temperatureValues.Max(), 1),
            AverageFanRpm = fanValues.Count == 0 ? (double?)null : Math.Round(fanValues.Average(), 0),
            MinFanRpm = fanValues.Count == 0 ? (double?)null : Math.Round(fanValues.Min(), 0),
            MaxFanRpm = fanValues.Count == 0 ? (double?)null : Math.Round(fanValues.Max(), 0),
            MaxPerformance = performanceValues.Count == 0 ? (double?)null : Math.Round(performanceValues.Max(), 1),
            AveragePerformance = performanceValues.Count == 0 ? (double?)null : Math.Round(performanceValues.Average(), 1),
            PowerWatts = powerWatts.HasValue ? Math.Round(powerWatts.Value, 1) : (double?)null,
            VoltageCount = voltageValues.Count,
            AverageVoltage = voltageValues.Count == 0 ? (double?)null : Math.Round(voltageValues.Average(), 1),
            CurrentCount = currentValues.Count,
            TotalCurrent = currentValues.Count == 0 ? (double?)null : Math.Round(currentValues.Sum(), 2),
            CpuUsage = FindNumericSensorValue("CPU Usage"),
            MemUsage = FindNumericSensorValue("MEM Usage"),
            IoUsage = FindNumericSensorValue("IO Usage"),
            SysUsage = FindNumericSensorValue("SYS Usage"),
            OkCount = okCount,
            WarningCount = warningCount,
            LastUpdated = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private object BuildVisualizationPoint(SensorReading sensor, string type)
    {
        return new
        {
            Id = BuildSensorStableId(sensor),
            Name = BuildSensorTitle(sensor),
            Type = type,
            Value = sensor.NumericValue.HasValue ? Math.Round(sensor.NumericValue.Value, 1) : (double?)null,
            Unit = NormalizeVisualizationUnit(sensor),
            Status = sensor.Status,
            Subtitle = BuildSensorSubtitle(sensor),
        };
    }

    private object[] BuildTypeCounts()
    {
        return Sensors
            .GroupBy(GetHardwareTypeName)
            .OrderByDescending(group => group.Count())
            .Select(group => new { Name = group.Key, Value = group.Count() })
            .Cast<object>()
            .ToArray();
    }

    private object[] BuildSensorTree()
    {
        return Sensors
            .GroupBy(GetHardwareTypeName)
            .OrderByDescending(group => group.Count())
            .Select(group => new
            {
                Name = group.Key,
                Value = group.Count(),
                Children = group
                    .OrderBy(sensor => IsOkStatus(sensor) ? 0 : 1)
                    .ThenBy(sensor => BuildSensorTitle(sensor), StringComparer.CurrentCultureIgnoreCase)
                    .Select(sensor => new
                    {
                        Name = BuildSensorTitle(sensor),
                        Value = 1,
                        Status = sensor.Status,
                        Reading = string.IsNullOrWhiteSpace(sensor.Value) ? sensor.Status : sensor.Value,
                        Unit = NormalizeVisualizationUnit(sensor),
                        Subtitle = BuildSensorSubtitle(sensor),
                    })
                    .Cast<object>()
                    .ToArray(),
            })
            .Cast<object>()
            .ToArray();
    }

    private double? FindNumericSensorValue(string key)
    {
        return Sensors.FirstOrDefault(sensor => sensor.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.NumericValue;
    }

    private string GetHardwareTypeName(SensorReading sensor)
    {
        if (IsTemperatureSensor(sensor))
        {
            return T("Dashboard.TypeTemperature");
        }

        if (IsFanSensor(sensor))
        {
            return T("Dashboard.TypeFan");
        }

        if (IsPerformanceSensor(sensor))
        {
            return T("Dashboard.TypePerformance");
        }

        if (IsPowerWattsSensor(sensor))
        {
            return T("Dashboard.TypePower");
        }

        if (IsVoltageSensor(sensor))
        {
            return T("Dashboard.TypeVoltage");
        }

        if (IsCurrentSensor(sensor))
        {
            return T("Dashboard.TypeCurrent");
        }

        if (IsHealthSensor(sensor))
        {
            return T("Dashboard.TypeStatus");
        }

        if (!sensor.NumericValue.HasValue)
        {
            return T("Dashboard.TypeStatus");
        }

        if (sensor.NumericValue.HasValue)
        {
            return T("Dashboard.TypeNumeric");
        }

        return T("Dashboard.TypeOther");
    }

    private static bool IsOkStatus(SensorReading sensor)
    {
        return sensor.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
               sensor.Status.Equals("ns", StringComparison.OrdinalIgnoreCase) ||
               sensor.Status.Equals("na", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVisualizationUnit(SensorReading sensor)
    {
        if (sensor.Unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase))
        {
            return "°C";
        }

        if (sensor.Unit.Contains("RPM", StringComparison.OrdinalIgnoreCase))
        {
            return "RPM";
        }

        if (sensor.Unit.Contains("Watts", StringComparison.OrdinalIgnoreCase))
        {
            return "W";
        }

        if (sensor.Unit.Contains("Volts", StringComparison.OrdinalIgnoreCase))
        {
            return "V";
        }

        if (sensor.Unit.Contains("Amps", StringComparison.OrdinalIgnoreCase))
        {
            return "A";
        }

        if (sensor.Unit.Contains("percent", StringComparison.OrdinalIgnoreCase))
        {
            return "%";
        }

        return sensor.Unit;
    }

    private static string BuildSensorStableId(SensorReading sensor)
    {
        return $"{sensor.SensorId}|{sensor.Key}|{sensor.Entity}";
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

    private static bool IsFanSensor(SensorReading sensor)
    {
        return sensor.Key.StartsWith("Fan", StringComparison.OrdinalIgnoreCase) &&
               sensor.Unit.Contains("RPM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPerformanceSensor(SensorReading sensor)
    {
        return sensor.Key.Contains("Usage", StringComparison.OrdinalIgnoreCase) ||
               sensor.Unit.Contains("percent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerWattsSensor(SensorReading sensor)
    {
        return sensor.Unit.Contains("Watts", StringComparison.OrdinalIgnoreCase) ||
               sensor.Key.Contains("Pwr Consumption", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVoltageSensor(SensorReading sensor)
    {
        return sensor.Unit.Contains("Volts", StringComparison.OrdinalIgnoreCase) && sensor.NumericValue.HasValue;
    }

    private static bool IsCurrentSensor(SensorReading sensor)
    {
        return sensor.Unit.Contains("Amps", StringComparison.OrdinalIgnoreCase) && sensor.NumericValue.HasValue;
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

    private void RebuildPresets(IEnumerable<FanPreset> presets)
    {
        Presets.Clear();
        foreach (var preset in presets)
        {
            var clone = preset.Clone();
            clone.SetActive(_activePresetId?.Equals(clone.Id, StringComparison.OrdinalIgnoreCase) == true);
            Presets.Add(clone);
        }
    }

    private void RefreshPresetRows()
    {
        RebuildPresets(Presets.Select(preset => preset.Clone()).ToList());
    }

    private void MarkActivePreset(string? presetId)
    {
        _activePresetId = presetId;
        RefreshPresetRows();
    }

    private FanPreset ReadPresetFromSender(object sender)
    {
        if (sender is not Button { Tag: string presetId })
        {
            throw new InvalidOperationException(T("Validation.PresetNotFound"));
        }

        return Presets.FirstOrDefault(preset => preset.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(F("Validation.PresetNotFound", presetId));
    }

    private FanPreset ValidateAndClonePreset(FanPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Kind))
        {
            throw new InvalidOperationException("Fan preset kind is empty.");
        }

        var clone = preset.Clone();
        if (clone.Kind.Equals(FanPreset.ManualKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.ManualKind;
            clone.Percent = CheckedPercent(clone.Percent, T("Field.AllFanPercent"));
        }
        else if (clone.Kind.Equals(FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.RestoreManualKind;
            clone.Percent = AppSettings.LocalDefaultManualFanPercent;
        }
        else if (clone.Kind.Equals(FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.DellAutoKind;
            clone.Percent = 0;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported fan preset kind: {clone.Kind}");
        }

        if (string.IsNullOrWhiteSpace(clone.NameKey) && string.IsNullOrWhiteSpace(clone.Name))
        {
            throw new InvalidOperationException(T("Validation.PresetNameRequired"));
        }

        clone.Name = clone.Name.Trim();
        return clone;
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
        NewPresetNameBox.PlaceholderText = T("Preset.NewNamePlaceholder");
        VisualizationStateText.Text = _visualizationReady
            ? T("Dashboard.VisualizationReady")
            : T("Dashboard.VisualizationLoading");
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
        RefreshPresetRows();
        SendVisualizationSnapshot();

        foreach (var fanChannel in FanChannels)
        {
            fanChannel.RefreshLocalization();
        }
    }

    private void SetModeSummary(string key, params object[] args)
    {
        _modeSummaryKey = key;
        _modeSummaryArgs = args;
        var modeText = args.Length == 0 ? T(key) : F(key, args);
        ModeSummaryText.Text = modeText;
        ControlCurrentModeText.Text = F("Control.CurrentMode", modeText);
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

    private sealed class SensorDashboardHistoryPoint
    {
        public string Time { get; set; } = string.Empty;

        public double? MaxTemperature { get; set; }

        public double? AverageFanRpm { get; set; }

        public double? CpuUsage { get; set; }

        public double? MemUsage { get; set; }

        public double? IoUsage { get; set; }

        public double? SysUsage { get; set; }

        public double? PowerWatts { get; set; }
    }
}
