using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace DellR730xdFanControlCenter;

public sealed partial class MainPage : Page
{
    private const int SensorHistoryLimit = 120;
    private const string DefaultCurvePointsText = "45 = 18%" + "\r\n" + "68 = 28%" + "\r\n" + "78 = 42%";
    private static readonly JsonSerializerOptions VisualizationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SettingsStore _settingsStore = new();
    private readonly AppLogService _appLog = new();
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
    private bool _isConnecting;
    private bool _hasDisconnected;
    private DateTime? _lastPollTime;
    private TimeSpan? _lastPollDuration;
    private DateTimeOffset _lastPollingWarningAt = DateTimeOffset.MinValue;
    private bool _pollingWasDegraded;
    private bool _visualizationInitialized;
    private bool _visualizationReady;
    private string? _activePresetId;
    private FanPreset? _activeCurvePreset;
    private string _heroRequestMessage = string.Empty;
    private string _heroRequestSummary = string.Empty;
    private DateTime? _heroRequestUpdatedAt;
    private string _modeSummaryKey = "Mode.Idle";
    private object[] _modeSummaryArgs = Array.Empty<object>();
    private string? _editingCurvePresetId;

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
        LocalizedSensors = [];
        NewCurvePoints = [];

        _ipmi.CommandCompleted += OnCommandCompleted;
        _autoPolicyTimer.Tick += OnAutoPolicyTimerTick;
        _sensorPollingTimer.Tick += OnSensorPollingTimerTick;
    }

    public ObservableCollection<SensorReading> Sensors { get; }

    public ObservableCollection<SensorReading> LocalizedSensors { get; }

    public ObservableCollection<LogEntry> Logs { get; }

    public ObservableCollection<FanChannelViewModel> FanChannels { get; }

    public ObservableCollection<FanPreset> Presets { get; }

    public ObservableCollection<DashboardTileViewModel> TemperatureTiles { get; }

    public ObservableCollection<DashboardTileViewModel> FanTiles { get; }

    public ObservableCollection<DashboardTileViewModel> PowerTiles { get; }

    public ObservableCollection<DashboardTileViewModel> StatusTiles { get; }

    public ObservableCollection<FanCurvePoint> NewCurvePoints { get; }

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
        ResetNewCurveEditor();
        AddLog(T("Log.Info"), T("Status.Loaded"));
        AddLog(T("Log.Info"), F("Status.LogFileReady", _appLog.CurrentLogPath), "Application", "LogFileReady");
        shouldShowSettingsOnStart = shouldShowSettingsOnStart || string.IsNullOrWhiteSpace(PasswordBox.Password);
        var hasUnsafePollingInterval = _settings.SensorRefreshSeconds < AppSettings.MinimumSensorRefreshSeconds;
        if (hasUnsafePollingInterval)
        {
            SelectView("Settings");
            ShowStatus(
                F("Status.SensorRefreshSecondsTooLow", _settings.SensorRefreshSeconds, AppSettings.MinimumSensorRefreshSeconds),
                InfoBarSeverity.Warning);
        }
        else if (shouldShowSettingsOnStart)
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

        var dashboardPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Charts", "dashboard.html");
        if (!File.Exists(dashboardPath))
        {
            throw new FileNotFoundException("Visualization dashboard asset was not found.", dashboardPath);
        }

        await VisualizationWebView.EnsureCoreWebView2Async();
        VisualizationWebView.CoreWebView2.WebMessageReceived -= OnVisualizationWebMessageReceived;
        VisualizationWebView.CoreWebView2.WebMessageReceived += OnVisualizationWebMessageReceived;
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

    private void OnVisualizationWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var message = JsonDocument.Parse(args.WebMessageAsJson);
        var root = message.RootElement;
        if (!root.TryGetProperty("type", out var typeProperty) ||
            !string.Equals(typeProperty.GetString(), "wheel", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!root.TryGetProperty("deltaY", out var deltaYProperty) ||
            deltaYProperty.ValueKind != JsonValueKind.Number ||
            !deltaYProperty.TryGetDouble(out var deltaY) ||
            double.IsNaN(deltaY) ||
            double.IsInfinity(deltaY))
        {
            return;
        }

        var nextOffset = Math.Clamp(
            ContentScrollViewer.VerticalOffset + deltaY,
            0,
            ContentScrollViewer.ScrollableHeight);
        ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: false);
    }

    public Task ApplyQuickFanSpeedAsync(int percent)
    {
        AllFanSlider.Value = percent;
        AllFanPercentBox.Value = percent;
        return ApplyAllFansAsync(percent);
    }

    public Task RestoreDellFactoryFanSpeedFromTrayAsync()
    {
        return ResetDellAutomaticModeAsync();
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
        _settings.SensorRefreshSeconds = ReadSensorRefreshSeconds();
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
        SetConnectingState();
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
        var intervalSeconds = _settings.SensorRefreshSeconds;
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingTimer.Start();
        _isConnecting = false;
        _hasDisconnected = false;
        _pollingWasDegraded = false;
        _lastPollingWarningAt = DateTimeOffset.MinValue;
        UpdatePollingStatusTexts();
        AddLog(T("Log.Info"), F("Status.PollingStarted", intervalSeconds));
    }

    private void StopSensorPolling(string reason)
    {
        _sensorPollingTimer.Stop();
        _isConnecting = false;
        _hasDisconnected = true;
        UpdatePollingStatusTexts();
        AddLog(T("Log.Warn"), F("Status.PollingStopped", reason));
    }

    private async void OnSensorPollingTimerTick(object? sender, object e)
    {
        var intervalSeconds = _settings.SensorRefreshSeconds;
        if (_sensorPollingTickRunning)
        {
            ReportPollingWarning(BuildPollingSkippedWarning("Status.PollingSkippedPreviousRunning", "Status.PollingSkippedPreviousRunningNoSample", intervalSeconds));
            return;
        }

        _sensorPollingTickRunning = true;
        if (!await _ipmiOperationLock.WaitAsync(0))
        {
            _sensorPollingTickRunning = false;
            ReportPollingWarning(BuildPollingSkippedWarning("Status.PollingSkippedIpmiBusy", "Status.PollingSkippedIpmiBusyNoSample", intervalSeconds));
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
        SetHeroRequestStatus(T("Status.RefreshingSensors"));
        var operation = _appLog.StartOperation(
            "SensorRefresh",
            T("Status.RefreshingSensors"),
            new Dictionary<string, string>
            {
                ["host"] = profile.Host,
                ["refreshSeconds"] = _settings.SensorRefreshSeconds.ToString(CultureInfo.InvariantCulture),
            });

        try
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
            SetHeroRequestStatus(F("Hero.SensorRefreshSucceeded", Sensors.Count, stopwatch.Elapsed.TotalSeconds));
            operation.Succeed(
                T("Status.SensorsRefreshed"),
                new Dictionary<string, string>
                {
                    ["sensorCount"] = Sensors.Count.ToString(CultureInfo.InvariantCulture),
                    ["elapsedMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                });
            return stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            try
            {
                operation.Fail(ex);
            }
            catch (Exception logException)
            {
                throw new InvalidOperationException(
                    $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                    new AggregateException(ex, logException));
            }

            throw;
        }
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
            _activeCurvePreset = null;
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
            _activeCurvePreset = null;
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
            if (_activeCurvePreset?.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activeCurvePreset = _settings.Presets.FirstOrDefault(item => item.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase));
            }

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

            if (_activeCurvePreset?.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activeCurvePreset = null;
                _autoPolicyTimer.Stop();
                StartAutoButton.IsEnabled = true;
                StopAutoButton.IsEnabled = false;
                SetAutoPolicySummary(false);
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

    private void OnAddCurvePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NewCurvePresetNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(T("Validation.PresetNameRequired"));
            }

            var preset = new FanPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = FanPreset.CurveKind,
                Name = name,
                DescriptionKey = "Preset.CurveDetail",
                CurvePoints = ReadNewCurvePoints(),
                SmoothCurve = NewCurveSmoothSwitch.IsOn,
            };
            preset.ValidateCurvePoints();

            var existing = string.IsNullOrWhiteSpace(_editingCurvePresetId)
                ? null
                : Presets.FirstOrDefault(item => item.Id.Equals(_editingCurvePresetId, StringComparison.OrdinalIgnoreCase));
            var statusKey = "Status.CurvePresetAdded";
            if (existing is null)
            {
                Presets.Add(preset);
            }
            else
            {
                existing.EditableName = name;
                existing.CurvePoints = preset.CurvePoints.Select(point => point.Clone()).ToList();
                existing.SmoothCurve = preset.SmoothCurve;
                statusKey = "Status.CurvePresetSaved";
            }

            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            if (existing is not null &&
                _activeCurvePreset?.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activeCurvePreset = ValidateAndClonePreset(existing);
            }

            _settingsStore.Save(_settings);
            ResetNewCurveEditor();
            RefreshPresetRows();
            ShowStatus(F(statusKey, name), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F(statusKey, name));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnAddNewCurvePointClick(object sender, RoutedEventArgs e)
    {
        try
        {
            NewCurvePoints.Add(new FanCurvePoint
            {
                TemperatureCelsius = ReadDouble(NewCurveTemperatureBox, T("Field.CurveTemperature")),
                FanPercent = CheckedPercent(NewCurvePercentBox.Value, T("Field.CurveFanPercent")),
            });
            NormalizeNewCurveEditorPoints();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnDeleteNewCurvePointClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FanCurvePoint point })
        {
            NewCurvePoints.Remove(point);
            UpdateNewCurvePreview();
        }
    }

    private void OnNewCurvePointValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.DataContext is FanCurvePoint point && !double.IsNaN(sender.Value))
        {
            if (string.Equals(sender.Tag as string, "Temperature", StringComparison.OrdinalIgnoreCase))
            {
                point.TemperatureCelsius = sender.Value;
            }
            else
            {
                point.FanPercent = sender.Value;
            }
        }

        UpdateNewCurvePreview();
    }

    private void OnNewCurveCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (NewCurveCanvas.ActualWidth <= 0 || NewCurveCanvas.ActualHeight <= 0)
        {
            return;
        }

        var position = e.GetCurrentPoint(NewCurveCanvas).Position;
        var temperature = 30 + (Math.Clamp(position.X / NewCurveCanvas.ActualWidth, 0, 1) * 65);
        var percent = 100 - (Math.Clamp(position.Y / NewCurveCanvas.ActualHeight, 0, 1) * 100);
        NewCurvePoints.Add(new FanCurvePoint
        {
            TemperatureCelsius = Math.Round(temperature, 1),
            FanPercent = Math.Round(percent, 0),
        });
        NormalizeNewCurveEditorPoints();
    }

    private void OnNewCurveCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNewCurvePreview();
    }

    private void OnNewCurveSmoothToggled(object sender, RoutedEventArgs e)
    {
        UpdateNewCurvePreview();
    }

    private void OnResetNewCurveEditorClick(object sender, RoutedEventArgs e)
    {
        ResetNewCurveEditor();
    }

    private void OnEditCurvePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);
            if (!preset.IsCurvePreset)
            {
                return;
            }

            _editingCurvePresetId = preset.Id;
            NewCurvePresetNameBox.Text = preset.DisplayName;
            NewCurveSmoothSwitch.IsOn = preset.SmoothCurve;
            ReplaceNewCurvePoints(preset.CurvePoints);
            UpdateNewCurveEditorModeText();
            UpdateNewCurvePreview();
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
        await ResetDellAutomaticModeAsync();
    }

    private Task ApplyPresetAsync(FanPreset preset)
    {
        if (string.Equals(preset.Kind, FanPreset.ManualKind, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyManualPresetAsync(preset);
        }

        if (string.Equals(preset.Kind, FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreDefaultManualAsync(preset.Id, CheckedPercent(preset.Percent, T("Field.AllFanPercent")));
        }

        if (string.Equals(preset.Kind, FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
        {
            return ResetDellAutomaticModeAsync(preset.Id);
        }

        if (string.Equals(preset.Kind, FanPreset.CurveKind, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyCurvePresetAsync(preset);
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
            _activeCurvePreset = null;
            SetModeSummary("Mode.PresetManual", preset.DisplayName, percent);
            MarkActivePreset(preset.Id);
            ShowStatus(F("Status.PresetApplied", preset.DisplayName), InfoBarSeverity.Success);
        });
    }

    private async Task RestoreDefaultManualAsync(string? activePresetId = "restore-manual", int? percentOverride = null)
    {
        var percent = percentOverride ?? AppSettings.LocalDefaultManualFanPercent;
        await RunUiCommandAsync(F("Status.RestoringDefault", percent), async token =>
        {
            PersistSettingsFromControls();
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            _activeCurvePreset = null;
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
            _activeCurvePreset = null;
            SetModeSummary("Mode.DellAuto");
            MarkActivePreset(activePresetId);
            ShowStatus(T("Status.DellAutoRestored"), InfoBarSeverity.Success);
        });
    }

    private async Task ApplyCurvePresetAsync(FanPreset preset)
    {
        var curvePreset = ValidateAndClonePreset(preset);
        await RunUiCommandAsync(F("Status.CurvePresetStarted", curvePreset.DisplayName), async token =>
        {
            PersistSettingsFromControls();
            _activeCurvePreset = curvePreset;
            _autoPolicyTimer.Interval = TimeSpan.FromSeconds(_settings.SensorRefreshSeconds);
            _autoPolicyTimer.Start();
            StartAutoButton.IsEnabled = false;
            StopAutoButton.IsEnabled = true;
            SetAutoPolicySummary(true);
            SetModeSummary("Mode.CurveAuto", curvePreset.DisplayName);
            MarkActivePreset(curvePreset.Id);
            AddLog(T("Log.Info"), F("Status.CurvePresetStarted", curvePreset.DisplayName));
            try
            {
                await RunAutoPolicyOnceAsync(token);
            }
            catch
            {
                _autoPolicyTimer.Stop();
                _activeCurvePreset = null;
                StartAutoButton.IsEnabled = true;
                StopAutoButton.IsEnabled = false;
                SetAutoPolicySummary(false);
                throw;
            }

            ShowStatus(F("Status.CurvePresetStarted", curvePreset.DisplayName), InfoBarSeverity.Success);
        });
    }

    private async void OnStartAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistSettingsFromControls();
            _activeCurvePreset = null;
            _autoPolicyTimer.Interval = TimeSpan.FromSeconds(_settings.SensorRefreshSeconds);
            _autoPolicyTimer.Start();
            StartAutoButton.IsEnabled = false;
            StopAutoButton.IsEnabled = true;
            SetAutoPolicySummary(true);
            SetModeSummary("Mode.SmartAuto");
            AddLog(T("Log.Info"), T("Status.AutoStarted"));
            try
            {
                await RunAutoPolicyOnceAsync(CancellationToken.None);
            }
            catch
            {
                _autoPolicyTimer.Stop();
                StartAutoButton.IsEnabled = true;
                StopAutoButton.IsEnabled = false;
                SetAutoPolicySummary(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void OnStopAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        _autoPolicyTimer.Stop();
        _activeCurvePreset = null;
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
        var activeCurvePreset = _activeCurvePreset?.Clone();
        SetHeroRequestStatus(activeCurvePreset is null ? T("Status.AutoStarted") : F("Status.CurvePresetStarted", activeCurvePreset.DisplayName));
        var operationProperties = new Dictionary<string, string>
        {
            ["targetTemperatureCelsius"] = _settings.TargetCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
            ["highTemperatureCelsius"] = _settings.HighCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
            ["emergencyTemperatureCelsius"] = _settings.EmergencyCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
        };
        if (activeCurvePreset is not null)
        {
            operationProperties["curvePresetId"] = activeCurvePreset.Id;
            operationProperties["curvePresetName"] = activeCurvePreset.DisplayName;
            operationProperties["curvePoints"] = activeCurvePreset.CurvePointsText;
        }

        var operation = _appLog.StartOperation(
            "SmartAutoPolicyTick",
            T("Status.AutoStarted"),
            operationProperties);

        try
        {
            var profile = ReadProfile();
            var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken);
            ReplaceSensors(readings);
            UpdateMetricSummaries();
            RecordVisualizationHistoryPoint();
            SendVisualizationSnapshot();

            var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(readings);
            var percent = activeCurvePreset is null
                ? CalculateAutoFanPercent(cpuTemp)
                : activeCurvePreset.CalculateFanPercent(cpuTemp);

            if (cpuTemp >= _settings.EmergencyCpuTemperatureCelsius)
            {
                await _ipmi.SetDellAutomaticModeAsync(profile, cancellationToken);
                _activeCurvePreset = null;
                AddLog(T("Log.Warn"), F("Status.EmergencyAuto", cpuTemp));
                SetModeSummary("Mode.DellAuto");
                SetHeroRequestStatus(F("Status.EmergencyAuto", cpuTemp));
                operation.Succeed(
                    F("Status.EmergencyAuto", cpuTemp),
                    new Dictionary<string, string>
                    {
                        ["cpuTemperatureCelsius"] = cpuTemp.ToString("0.0", CultureInfo.InvariantCulture),
                        ["action"] = "RestoreDellAutomaticMode",
                    });
                return;
            }

            await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);
            var message = activeCurvePreset is null
                ? F("Status.SmartFanApplied", cpuTemp, percent)
                : F("Status.CurveFanApplied", activeCurvePreset.DisplayName, cpuTemp, percent);
            if (activeCurvePreset is null)
            {
                SetModeSummary("Mode.SmartPercent", percent);
            }
            else
            {
                SetModeSummary("Mode.CurvePercent", activeCurvePreset.DisplayName, percent);
            }

            AddLog(T("Log.Info"), message);
            SetHeroRequestStatus(message);
            operation.Succeed(
                message,
                new Dictionary<string, string>
                {
                    ["cpuTemperatureCelsius"] = cpuTemp.ToString("0.0", CultureInfo.InvariantCulture),
                    ["fanPercent"] = percent.ToString(CultureInfo.InvariantCulture),
                    ["action"] = "SetAllFansManualSpeed",
                });
        }
        catch (Exception ex)
        {
            try
            {
                operation.Fail(ex);
            }
            catch (Exception logException)
            {
                throw new InvalidOperationException(
                    $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                    new AggregateException(ex, logException));
            }

            throw;
        }
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

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_appLog.LogDirectory);
            Process.Start(new ProcessStartInfo(_appLog.LogDirectory) { UseShellExecute = true });
            AddLog(
                T("Log.Info"),
                F("Status.LogFolderOpened", _appLog.LogDirectory),
                "Application",
                "LogFolderOpened",
                new Dictionary<string, string>
                {
                    ["logDirectory"] = _appLog.LogDirectory,
                });
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
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
        AppLogOperation? operation = null;
        var runningMessage = F("Hero.RequestRunning", description);
        try
        {
            operation = _appLog.StartOperation(
                "UiCommand",
                description,
                new Dictionary<string, string>
                {
                    ["description"] = description,
                });
            SetHeroRequestStatus(runningMessage);
            AddVolatileLog(T("Log.Info"), description);
            using var cancellation = new CancellationTokenSource();
            await _ipmiOperationLock.WaitAsync(cancellation.Token);
            lockTaken = true;
            await command(cancellation.Token);
            if (string.Equals(_heroRequestMessage, runningMessage, StringComparison.Ordinal))
            {
                SetHeroRequestStatus(F("Hero.RequestSucceeded", description));
            }

            operation.Succeed(description);
        }
        catch (Exception ex)
        {
            if (_isConnecting)
            {
                _isConnecting = false;
                _hasDisconnected = true;
                UpdatePollingStatusTexts();
            }

            if (operation is not null)
            {
                try
                {
                    operation.Fail(ex);
                }
                catch (Exception logException)
                {
                    ShowFailure(new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        ex));
                    return;
                }
            }

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

        RefreshLocalizedSensorRows();
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
            temperatureReadings.Select(BuildDashboardTile));

        var fanReadings = Sensors
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        FanSummaryText.Text = F("Overview.FansCount", fanReadings.Count);
        FanRpmSummaryText.Text = fanReadings.Count == 0
            ? T("Overview.NoFanRpm")
            : $"{fanReadings.Min(f => f.NumericValue):0} - {fanReadings.Max(f => f.NumericValue):0} {T("SensorUnit.Rpm")}";
        ReplaceTiles(
            FanTiles,
            fanReadings.Select(BuildDashboardTile));

        var metrics = HeroRealtimeMetrics.FromSensors(Sensors);
        PowerSummaryText.Text = FormatOverviewSummaryMetric(metrics.PowerWatts, "0.#", T("SensorUnit.Watts"));
        PowerSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.PowerItems, "0.#", "Overview.NoPowerReading");
        VoltageSummaryText.Text = FormatOverviewSummaryMetric(metrics.AverageVoltage, "0.#", T("SensorUnit.Volts"));
        VoltageSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.VoltageItems, "0.#", "Overview.NoVoltageReading");
        CurrentSummaryText.Text = FormatOverviewSummaryMetric(metrics.TotalCurrent, "0.#", T("SensorUnit.Amps"));
        CurrentSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.CurrentItems, "0.#", "Overview.NoCurrentReading");

        var powerAndHealth = Sensors
            .Where(sensor => IsPowerSensor(sensor) || IsHealthSensor(sensor))
            .Take(14)
            .Select(BuildDashboardTile);
        ReplaceTiles(PowerTiles, powerAndHealth);
        UpdateHeroRealtimeMetrics();
    }

    private DashboardTileViewModel BuildDashboardTile(SensorReading sensor)
    {
        var style = GetDashboardTileStyle(sensor);
        return new DashboardTileViewModel
        {
            Title = BuildVisualizationSensorName(sensor),
            Value = BuildDashboardTileValue(sensor),
            Unit = sensor.NumericValue.HasValue ? BuildLocalizedSensorUnit(sensor) : string.Empty,
            Subtitle = BuildSensorSubtitle(sensor),
            Status = BuildLocalizedSensorStatus(sensor.Status),
            AccentHex = style.ForegroundHex,
            ValueHex = style.ForegroundHex,
            TemperatureIconOpacity = IsTemperatureSensor(sensor) ? 1 : 0,
            FanIconOpacity = IsFanSensor(sensor) ? 1 : 0,
            PowerIconOpacity = IsPowerWattsSensor(sensor) || IsPerformanceSensor(sensor) ? 1 : 0,
            VoltageIconOpacity = IsVoltageSensor(sensor) ? 1 : 0,
            CurrentIconOpacity = IsCurrentSensor(sensor) ? 1 : 0,
            HealthIconOpacity = IsHealthSensor(sensor) || !sensor.NumericValue.HasValue ? 1 : 0,
            IsFanAnimated = IsFanSensor(sensor),
            FanRotationSeconds = CalculateFanRotationSeconds(sensor.NumericValue),
            IsElectricalAnimated = IsPowerSensor(sensor) && sensor.NumericValue.HasValue && !IsFanSensor(sensor),
            ElectricalPulseSeconds = CalculateElectricalPulseSeconds(sensor.NumericValue),
        };
    }

    private string BuildDashboardTileValue(SensorReading sensor)
    {
        if (!sensor.NumericValue.HasValue)
        {
            return BuildLocalizedSensorReading(sensor);
        }

        var format = IsFanSensor(sensor) ? "0" : "0.#";
        return sensor.NumericValue.Value.ToString(format, CultureInfo.InvariantCulture);
    }

    private HeroMetricSeverityStyle GetDashboardTileStyle(SensorReading sensor)
    {
        if (IsTemperatureSensor(sensor))
        {
            return HeroMetricSeverityStyle.ForTemperature(sensor.NumericValue);
        }

        if (IsFanSensor(sensor))
        {
            return HeroMetricSeverityStyle.ForFanRpm(sensor.NumericValue);
        }

        if (IsPowerWattsSensor(sensor))
        {
            return HeroMetricSeverityStyle.ForPowerWatts(sensor.NumericValue);
        }

        if (IsVoltageSensor(sensor))
        {
            return HeroMetricSeverityStyle.ForVoltage(sensor.NumericValue);
        }

        if (IsCurrentSensor(sensor))
        {
            return HeroMetricSeverityStyle.ForCurrentAmps(sensor.NumericValue);
        }

        if (IsHealthSensor(sensor) || !sensor.NumericValue.HasValue)
        {
            return IsOkStatus(sensor)
                ? new HeroMetricSeverityStyle("Normal", "#FF22C55E")
                : new HeroMetricSeverityStyle("Caution", "#FFF97316");
        }

        return new HeroMetricSeverityStyle("Info", "#FF2563EB");
    }

    private static double CalculateFanRotationSeconds(double? rpm)
    {
        if (!rpm.HasValue || rpm.Value <= 0)
        {
            return 5.2;
        }

        var normalized = Math.Clamp((rpm.Value - 1000) / 17000, 0, 1);
        return Math.Round(3.2 - (normalized * 2.4), 2);
    }

    private static double CalculateElectricalPulseSeconds(double? value)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            return 1.35;
        }

        return Math.Clamp(1.1 - Math.Log10(value.Value + 1) * 0.15, 0.62, 1.15);
    }

    private string FormatOverviewSummaryMetric(double? value, string numberFormat, string unit)
    {
        return value.HasValue
            ? $"{value.Value.ToString(numberFormat, CultureInfo.InvariantCulture)} {unit}"
            : T("Hero.LiveWaiting");
    }

    private string FormatOverviewSummaryItems(IReadOnlyList<HeroRealtimeMetricItem> items, string numberFormat, string emptyKey)
    {
        if (items.Count == 0)
        {
            return T(emptyKey);
        }

        return FormatHeroRealtimeItems(items.Take(2).ToList(), numberFormat);
    }

    private void OnDashboardTileFanIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            StartDashboardTileFanAnimation(element);
        }
    }

    private void OnDashboardTileFanIconDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        StartDashboardTileFanAnimation(sender);
    }

    private void StartDashboardTileFanAnimation(FrameworkElement element)
    {
        if (element.Tag is Storyboard previousStoryboard)
        {
            previousStoryboard.Stop();
            element.Tag = null;
        }

        if (element.DataContext is not DashboardTileViewModel tile ||
            !tile.IsFanAnimated ||
            tile.FanIconOpacity <= 0)
        {
            element.RenderTransform = null;
            return;
        }

        var currentAngle = element.RenderTransform is RotateTransform currentTransform
            ? currentTransform.Angle % 360
            : 0;
        var rotateTransform = new RotateTransform { Angle = currentAngle };
        element.RenderTransform = rotateTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var rotation = new DoubleAnimation
        {
            From = currentAngle,
            To = currentAngle + 360,
            Duration = new Duration(TimeSpan.FromSeconds(tile.FanRotationSeconds)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(rotation, rotateTransform);
        Storyboard.SetTargetProperty(rotation, "Angle");

        var storyboard = new Storyboard();
        storyboard.Children.Add(rotation);
        element.Tag = storyboard;
        storyboard.Begin();
    }

    private void OnDashboardTileElectricalIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            StartDashboardTileElectricalAnimation(element);
        }
    }

    private void OnDashboardTileElectricalIconDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        StartDashboardTileElectricalAnimation(sender);
    }

    private void StartDashboardTileElectricalAnimation(FrameworkElement element)
    {
        if (element.Tag is Storyboard previousStoryboard)
        {
            previousStoryboard.Stop();
            element.Tag = null;
        }

        if (element.DataContext is not DashboardTileViewModel tile ||
            !tile.IsElectricalAnimated ||
            element.Opacity <= 0)
        {
            element.RenderTransform = null;
            return;
        }

        var scaleTransform = new ScaleTransform { ScaleX = 0.94, ScaleY = 0.94 };
        element.RenderTransform = scaleTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleX = new DoubleAnimation
        {
            From = 0.94,
            To = 1.14,
            AutoReverse = true,
            Duration = new Duration(TimeSpan.FromSeconds(tile.ElectricalPulseSeconds)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        var scaleY = new DoubleAnimation
        {
            From = 0.94,
            To = 1.14,
            AutoReverse = true,
            Duration = new Duration(TimeSpan.FromSeconds(tile.ElectricalPulseSeconds)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(scaleX, scaleTransform);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, scaleTransform);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        element.Tag = storyboard;
        storyboard.Begin();
    }

    private void UpdateHeroRealtimeMetrics()
    {
        if (HeroLiveTemperatureText is null ||
            HeroLiveFanRpmText is null ||
            HeroLivePowerText is null ||
            HeroLiveVoltageText is null ||
            HeroLiveCurrentText is null ||
            HeroLiveTemperatureItemsText is null ||
            HeroLiveFanItemsText is null ||
            HeroLivePowerItemsText is null ||
            HeroLiveVoltageItemsText is null ||
            HeroLiveCurrentItemsText is null)
        {
            return;
        }

        var metrics = HeroRealtimeMetrics.FromSensors(Sensors);
        HeroLiveTemperatureText.Text = FormatHeroRealtimeMetric(metrics.CurrentTemperatureCelsius, "0.0", T("SensorUnit.Celsius"));
        HeroLiveFanRpmText.Text = FormatHeroRealtimeMetric(metrics.AverageFanRpm, "0", T("SensorUnit.Rpm"));
        HeroLivePowerText.Text = FormatHeroRealtimeMetric(metrics.PowerWatts, "0.#", T("SensorUnit.Watts"));
        HeroLiveVoltageText.Text = FormatHeroRealtimeMetric(metrics.AverageVoltage, "0.#", T("SensorUnit.Volts"));
        HeroLiveCurrentText.Text = FormatHeroRealtimeMetric(metrics.TotalCurrent, "0.#", T("SensorUnit.Amps"));
        HeroLiveTemperatureItemsText.Text = FormatHeroRealtimeItems(metrics.TemperatureItems, "0.#");
        HeroLiveFanItemsText.Text = FormatHeroRealtimeItems(metrics.FanItems, "0");
        HeroLivePowerItemsText.Text = FormatHeroRealtimeItems(metrics.PowerItems, "0.#");
        HeroLiveVoltageItemsText.Text = FormatHeroRealtimeItems(metrics.VoltageItems, "0.#");
        HeroLiveCurrentItemsText.Text = FormatHeroRealtimeItems(metrics.CurrentItems, "0.#");
        ApplyHeroMetricStyle(
            HeroLiveTemperatureText,
            HeroLiveTemperatureItemsText,
            HeroMetricSeverityStyle.ForTemperature(metrics.CurrentTemperatureCelsius));
        ApplyHeroMetricStyle(
            HeroLiveFanRpmText,
            HeroLiveFanItemsText,
            HeroMetricSeverityStyle.ForFanRpm(metrics.AverageFanRpm));
        ApplyHeroMetricStyle(
            HeroLivePowerText,
            HeroLivePowerItemsText,
            HeroMetricSeverityStyle.ForPowerWatts(metrics.PowerWatts));
        ApplyHeroMetricStyle(
            HeroLiveVoltageText,
            HeroLiveVoltageItemsText,
            HeroMetricSeverityStyle.ForVoltage(metrics.AverageVoltage));
        ApplyHeroMetricStyle(
            HeroLiveCurrentText,
            HeroLiveCurrentItemsText,
            HeroMetricSeverityStyle.ForCurrentAmps(metrics.TotalCurrent));
    }

    private string FormatHeroRealtimeMetric(double? value, string numberFormat, string unit)
    {
        return value.HasValue
            ? $"{value.Value.ToString(numberFormat, CultureInfo.InvariantCulture)} {unit}"
            : T("Hero.LiveWaiting");
    }

    private string FormatHeroRealtimeItems(IReadOnlyList<HeroRealtimeMetricItem> items, string numberFormat)
    {
        if (items.Count == 0)
        {
            return T("Hero.LiveWaiting");
        }

        var fragments = items
            .Select(item =>
            {
                var sensor = new SensorReading { Key = item.Key, Unit = item.Unit };
                var name = BuildVisualizationSensorName(sensor);
                var unit = BuildLocalizedSensorUnit(sensor);
                return $"{name} {item.Value.ToString(numberFormat, CultureInfo.InvariantCulture)} {unit}";
            })
            .ToList();

        return string.Join(Environment.NewLine, fragments);
    }

    private static void ApplyHeroMetricStyle(
        TextBlock primaryValue,
        TextBlock detailValue,
        HeroMetricSeverityStyle style)
    {
        var brush = ToBrush(style.ForegroundHex);
        primaryValue.Foreground = brush;
        detailValue.Foreground = brush;
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException($"Invalid metric color value: {hex}");
        }

        var alpha = Convert.ToByte(hex.Substring(1, 2), 16);
        var red = Convert.ToByte(hex.Substring(3, 2), 16);
        var green = Convert.ToByte(hex.Substring(5, 2), 16);
        var blue = Convert.ToByte(hex.Substring(7, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
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
            FanUnit = T("SensorUnit.Rpm"),
            PowerUnit = T("SensorUnit.Watts"),
            VoltageUnit = T("SensorUnit.Volts"),
            CurrentUnit = T("SensorUnit.Amps"),
            TrendTitle = T("Dashboard.TrendTitle"),
            TrendSubtitle = T("Dashboard.TrendSubtitle"),
            TempUnit = "°C",
            CpuUsage = T("Dashboard.CpuUsage"),
            MemoryUsage = T("Dashboard.MemoryUsage"),
            IoUsage = T("Dashboard.IoUsage"),
            SystemUsage = T("Dashboard.SystemUsage"),
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
            Name = BuildVisualizationSensorName(sensor),
            Type = type,
            Value = sensor.NumericValue.HasValue ? Math.Round(sensor.NumericValue.Value, 1) : (double?)null,
            Unit = BuildLocalizedSensorUnit(sensor),
            Status = BuildLocalizedSensorStatus(sensor.Status),
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
                    .ThenBy(BuildVisualizationSensorName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(sensor => new
                    {
                        Name = BuildVisualizationSensorName(sensor),
                        Value = 1,
                        Status = BuildLocalizedSensorStatus(sensor.Status),
                        Reading = BuildLocalizedSensorReading(sensor),
                        Unit = BuildLocalizedSensorUnit(sensor),
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

    private void RefreshLocalizedSensorRows()
    {
        LocalizedSensors.Clear();
        foreach (var sensor in Sensors)
        {
            LocalizedSensors.Add(new SensorReading
            {
                Key = BuildVisualizationSensorName(sensor),
                SensorId = sensor.SensorId,
                Entity = sensor.Entity,
                Value = BuildLocalizedSensorReading(sensor),
                Unit = BuildLocalizedSensorUnit(sensor),
                Status = BuildLocalizedSensorStatus(sensor.Status),
                NumericValue = sensor.NumericValue,
            });
        }
    }

    private string BuildLocalizedSensorReading(SensorReading sensor)
    {
        if (string.IsNullOrWhiteSpace(sensor.Value))
        {
            return string.IsNullOrWhiteSpace(sensor.Status) ? "--" : BuildLocalizedSensorStatus(sensor.Status);
        }

        return sensor.NumericValue.HasValue
            ? sensor.Value
            : TranslateSensorValue(sensor.Value);
    }

    private string BuildLocalizedSensorUnit(SensorReading sensor)
    {
        if (sensor.Unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Celsius");
        }

        if (sensor.Unit.Contains("RPM", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Rpm");
        }

        if (sensor.Unit.Contains("Watts", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Watts");
        }

        if (sensor.Unit.Contains("Volts", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Volts");
        }

        if (sensor.Unit.Contains("Amps", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Amps");
        }

        if (sensor.Unit.Contains("percent", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorUnit.Percent");
        }

        return sensor.Unit;
    }

    private string BuildLocalizedSensorStatus(string status)
    {
        var key = NormalizeSensorToken(status) switch
        {
            "ok" => "SensorStatus.Ok",
            "ns" => "SensorStatus.NotSpecified",
            "na" => "SensorStatus.NotAvailable",
            "nc" => "SensorStatus.NonCritical",
            "cr" => "SensorStatus.Critical",
            "nr" => "SensorStatus.NonRecoverable",
            "lnc" => "SensorStatus.LowerNonCritical",
            "lc" or "lcr" => "SensorStatus.LowerCritical",
            "lnr" => "SensorStatus.LowerNonRecoverable",
            "unc" => "SensorStatus.UpperNonCritical",
            "uc" or "ucr" => "SensorStatus.UpperCritical",
            "unr" => "SensorStatus.UpperNonRecoverable",
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(key) ? status : T(key);
    }

    private string TranslateSensorValue(string value)
    {
        var key = NormalizeSensorToken(value) switch
        {
            "no reading" => "SensorValue.NoReading",
            "general chassis intrusion" => "SensorValue.GeneralChassisIntrusion",
            "fully redundant" => "SensorValue.FullyRedundant",
            "not redundant" => "SensorValue.NotRedundant",
            "redundancy lost" => "SensorValue.RedundancyLost",
            "redundancy degraded" => "SensorValue.RedundancyDegraded",
            "oem specific" => "SensorValue.OemSpecific",
            "presence detected" => "SensorValue.PresenceDetected",
            "present" => "SensorValue.Present",
            "not present" => "SensorValue.NotPresent",
            "absent" => "SensorValue.Absent",
            "enabled" => "SensorValue.Enabled",
            "disabled" => "SensorValue.Disabled",
            "active" => "SensorValue.Active",
            "inactive" => "SensorValue.Inactive",
            "good" => "SensorValue.Good",
            "ok" => "SensorStatus.Ok",
            "fault" => "SensorValue.Fault",
            "open" => "SensorValue.Open",
            "closed" => "SensorValue.Closed",
            "connected" => "SensorValue.Connected",
            "disconnected" => "SensorValue.Disconnected",
            "state asserted" => "SensorValue.StateAsserted",
            "state deasserted" => "SensorValue.StateDeasserted",
            "asserted" => "SensorValue.Asserted",
            "deasserted" => "SensorValue.Deasserted",
            "detected" => "SensorValue.Detected",
            "not detected" => "SensorValue.NotDetected",
            "device present" => "SensorValue.Present",
            "device absent" => "SensorValue.Absent",
            "unknown" => "SensorValue.Unknown",
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(key) ? value : T(key);
    }

    private static string NormalizeSensorToken(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
    }

    private static string BuildSensorStableId(SensorReading sensor)
    {
        return $"{sensor.SensorId}|{sensor.Key}|{sensor.Entity}";
    }

    private string BuildVisualizationSensorName(SensorReading sensor)
    {
        var key = BuildSensorTitle(sensor).Trim();

        if (key.Equals("CPU Usage", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.CpuUsage");
        }

        if (key.Equals("MEM Usage", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.MemoryUsage");
        }

        if (key.Equals("IO Usage", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.IoUsage");
        }

        if (key.Equals("SYS Usage", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.SystemUsage");
        }

        if (key.Equals("Pwr Consumption", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Power Consumption", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.PowerConsumption");
        }

        if (key.Equals("SEL", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("System Event Log", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.SystemEventLog");
        }

        if (key.Equals("Inlet Temp", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.InletTemperature");
        }

        if (key.Equals("Exhaust Temp", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.ExhaustTemperature");
        }

        if (key.Equals("Intrusion", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.Intrusion");
        }

        if (key.Equals("Fan Redundancy", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.FanRedundancy");
        }

        if (key.Equals("PS Redundancy", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Power Supply Redundancy", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.PowerSupplyRedundancy");
        }

        if (key.Equals("Power Optimized", StringComparison.OrdinalIgnoreCase))
        {
            return T("SensorDisplay.PowerOptimized");
        }

        var fanMatch = Regex.Match(key, @"^Fan\s*([0-9]+)\s*RPM$", RegexOptions.IgnoreCase);
        if (fanMatch.Success)
        {
            return F("SensorDisplay.FanRpmIndexed", fanMatch.Groups[1].Value);
        }

        var voltageMatch = Regex.Match(key, @"^Voltage\s*([0-9]+)$", RegexOptions.IgnoreCase);
        if (voltageMatch.Success)
        {
            return F("SensorDisplay.VoltageIndexed", voltageMatch.Groups[1].Value);
        }

        var currentMatch = Regex.Match(key, @"^Current\s*([0-9]+)$", RegexOptions.IgnoreCase);
        if (currentMatch.Success)
        {
            return F("SensorDisplay.CurrentIndexed", currentMatch.Groups[1].Value);
        }

        var tempMatch = Regex.Match(key, @"^Temp\s*([0-9.]+)$", RegexOptions.IgnoreCase);
        if (tempMatch.Success)
        {
            return F("SensorDisplay.TemperatureIndexed", tempMatch.Groups[1].Value);
        }

        return key;
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

    private void ResetNewCurveEditor()
    {
        _editingCurvePresetId = null;
        NewCurvePresetNameBox.Text = string.Empty;
        NewCurveSmoothSwitch.IsOn = false;
        ReplaceNewCurvePoints(FanPreset.ParseCurvePoints(DefaultCurvePointsText));
        UpdateNewCurveEditorModeText();
        AddCurvePresetButtonText.Text = T("Control.AddCurvePreset");
        UpdateNewCurvePreview();
    }

    private void UpdateNewCurvePreview()
    {
        if (NewCurvePreviewText is null)
        {
            return;
        }

        List<FanCurvePoint>? validPoints = null;
        try
        {
            validPoints = ReadNewCurvePoints();
            NewCurvePreviewText.Text = FanPreset.BuildCurveChartText(validPoints, NewCurveSmoothSwitch.IsOn);
        }
        catch (Exception ex)
        {
            NewCurvePreviewText.Text = F("Status.CurvePreviewInvalid", ex.Message);
        }

        DrawNewCurveCanvas(validPoints);
    }

    private List<FanCurvePoint> ReadNewCurvePoints()
    {
        var preset = new FanPreset
        {
            Kind = FanPreset.CurveKind,
            Name = NewCurvePresetNameBox.Text.Trim(),
            CurvePoints = NewCurvePoints.Select(point => point.Clone()).ToList(),
            SmoothCurve = NewCurveSmoothSwitch.IsOn,
        };
        preset.ValidateCurvePoints();
        return preset.CurvePoints.Select(point => point.Clone()).ToList();
    }

    private void NormalizeNewCurveEditorPoints()
    {
        ReplaceNewCurvePoints(NewCurvePoints.OrderBy(point => point.TemperatureCelsius).Select(point => point.Clone()));
        UpdateNewCurvePreview();
    }

    private void ReplaceNewCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        NewCurvePoints.Clear();
        foreach (var point in points.OrderBy(point => point.TemperatureCelsius))
        {
            NewCurvePoints.Add(point.Clone());
        }
    }

    private void UpdateNewCurveEditorModeText()
    {
        if (NewCurveEditorModeText is null || AddCurvePresetButtonText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_editingCurvePresetId))
        {
            NewCurveEditorModeText.Text = T("Control.NewCurveEditor");
            AddCurvePresetButtonText.Text = T("Control.AddCurvePreset");
            return;
        }

        var preset = Presets.FirstOrDefault(item => item.Id.Equals(_editingCurvePresetId, StringComparison.OrdinalIgnoreCase));
        var presetName = preset?.DisplayName ?? NewCurvePresetNameBox.Text.Trim();
        NewCurveEditorModeText.Text = F("Control.EditingCurvePreset", presetName);
        AddCurvePresetButtonText.Text = T("Control.SaveCurvePreset");
    }

    private void DrawNewCurveCanvas(List<FanCurvePoint>? validPoints = null)
    {
        if (NewCurveCanvas is null)
        {
            return;
        }

        var width = NewCurveCanvas.ActualWidth;
        var height = NewCurveCanvas.ActualHeight;
        NewCurveCanvas.Children.Clear();
        if (width <= 8 || height <= 8)
        {
            return;
        }

        const double minTemperature = 30;
        const double maxTemperature = 95;
        const double minPercent = 0;
        const double maxPercent = 100;

        double ToX(double temperature)
        {
            var ratio = Math.Clamp((temperature - minTemperature) / (maxTemperature - minTemperature), 0, 1);
            return 8 + ((width - 16) * ratio);
        }

        double ToY(double percent)
        {
            var ratio = Math.Clamp((percent - minPercent) / (maxPercent - minPercent), 0, 1);
            return 8 + ((height - 16) * (1 - ratio));
        }

        var gridBrush = ToBrush("#220F766E");
        var axisBrush = ToBrush("#660F766E");
        for (var index = 0; index <= 4; index++)
        {
            var x = 8 + ((width - 16) * index / 4);
            var y = 8 + ((height - 16) * index / 4);
            NewCurveCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 8,
                Y2 = height - 8,
                Stroke = gridBrush,
                StrokeThickness = 1,
            });
            NewCurveCanvas.Children.Add(new Line
            {
                X1 = 8,
                X2 = width - 8,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
            });
        }

        NewCurveCanvas.Children.Add(new Line
        {
            X1 = 8,
            X2 = width - 8,
            Y1 = height - 8,
            Y2 = height - 8,
            Stroke = axisBrush,
            StrokeThickness = 1.4,
        });
        NewCurveCanvas.Children.Add(new Line
        {
            X1 = 8,
            X2 = 8,
            Y1 = 8,
            Y2 = height - 8,
            Stroke = axisBrush,
            StrokeThickness = 1.4,
        });

        var points = NewCurvePoints
            .Select(point => point.Clone())
            .OrderBy(point => point.TemperatureCelsius)
            .ToList();
        if (points.Count == 0)
        {
            return;
        }

        if (validPoints is not null && validPoints.Count >= 2)
        {
            var linePoints = new PointCollection();
            if (NewCurveSmoothSwitch.IsOn)
            {
                var previewPreset = new FanPreset
                {
                    Kind = FanPreset.CurveKind,
                    CurvePoints = validPoints.Select(point => point.Clone()).ToList(),
                    SmoothCurve = true,
                };

                var first = validPoints[0].TemperatureCelsius;
                var last = validPoints[^1].TemperatureCelsius;
                const int samples = 48;
                for (var index = 0; index < samples; index++)
                {
                    var ratio = index / (double)(samples - 1);
                    var temperature = first + ((last - first) * ratio);
                    linePoints.Add(new Point(ToX(temperature), ToY(previewPreset.CalculateFanPercent(temperature))));
                }
            }
            else
            {
                foreach (var point in validPoints)
                {
                    linePoints.Add(new Point(ToX(point.TemperatureCelsius), ToY(point.FanPercent)));
                }
            }

            NewCurveCanvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = ToBrush("#FF0F766E"),
                StrokeThickness = 3,
            });
        }

        foreach (var point in points)
        {
            var marker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = ToBrush("#FF14B8A6"),
                Stroke = ToBrush("#FFFFFFFF"),
                StrokeThickness = 2,
            };
            Canvas.SetLeft(marker, ToX(point.TemperatureCelsius) - 6);
            Canvas.SetTop(marker, ToY(point.FanPercent) - 6);
            NewCurveCanvas.Children.Add(marker);
        }
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
            clone.CurvePoints.Clear();
        }
        else if (clone.Kind.Equals(FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.RestoreManualKind;
            clone.Percent = CheckedPercent(clone.Percent, T("Field.AllFanPercent"));
            clone.CurvePoints.Clear();
        }
        else if (clone.Kind.Equals(FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.DellAutoKind;
            clone.Percent = 0;
            clone.CurvePoints.Clear();
        }
        else if (clone.Kind.Equals(FanPreset.CurveKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.CurveKind;
            clone.Percent = 0;
            clone.ApplyCurvePointsText();
            clone.ValidateCurvePoints();
        }
        else
        {
            throw new InvalidOperationException($"Unsupported fan preset kind: {clone.Kind}");
        }

        if (string.IsNullOrWhiteSpace(clone.NameKey) && string.IsNullOrWhiteSpace(clone.Name))
        {
            throw new InvalidOperationException(T("Validation.PresetNameRequired"));
        }

        clone.Name = (clone.Name ?? string.Empty).Trim();
        clone.Description = (clone.Description ?? string.Empty).Trim();
        return clone;
    }

    private void OnCommandCompleted(object? sender, CommandTraceEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                AddCommandLog(e);
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
        });
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
        SetHeroRequestStatus(message, severity);
    }

    private void SetHeroRequestStatus(string message, InfoBarSeverity? severity = null)
    {
        _heroRequestMessage = string.IsNullOrWhiteSpace(message)
            ? T("Hero.RequestIdle")
            : message.Trim();
        _heroRequestSummary = BuildHeroRequestSummary(_heroRequestMessage, severity);
        _heroRequestUpdatedAt = DateTime.Now;
        UpdateHeroRequestStatusTexts();
    }

    private void UpdateHeroRequestStatusTexts()
    {
        if (HeroRequestStateText is null || HeroRequestUpdatedText is null)
        {
            return;
        }

        HeroRequestStateText.Text = string.IsNullOrWhiteSpace(_heroRequestMessage)
            ? T("Hero.RequestIdle")
            : _heroRequestSummary;
        HeroRequestUpdatedText.Text = _heroRequestUpdatedAt.HasValue
            ? F("Hero.LastUpdateValue", _heroRequestUpdatedAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            : T("Hero.LastUpdateEmpty");
    }

    private string BuildHeroRequestSummary(string message, InfoBarSeverity? severity)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            message.Equals(T("Hero.RequestIdle"), StringComparison.OrdinalIgnoreCase))
        {
            return T("Hero.RequestShortIdle");
        }

        if (severity == InfoBarSeverity.Error)
        {
            return T("Hero.RequestShortFailed");
        }

        if (severity == InfoBarSeverity.Warning)
        {
            return IsPollingSkipMessage(message)
                ? T("Hero.RequestShortSkipped")
                : T("Hero.RequestShortWarning");
        }

        if (severity == InfoBarSeverity.Success)
        {
            return T("Hero.RequestShortOk");
        }

        if (message.Contains(T("Status.RefreshingSensors"), StringComparison.OrdinalIgnoreCase) ||
            message.Contains(T("Status.Connecting"), StringComparison.OrdinalIgnoreCase) ||
            message.Contains(T("Hero.RequestRunning").Split('{')[0], StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Requesting", StringComparison.OrdinalIgnoreCase))
        {
            return T("Hero.RequestShortRunning");
        }

        if (IsPollingSkipMessage(message))
        {
            return T("Hero.RequestShortSkipped");
        }

        if (message.Contains(T("Status.AutoStarted"), StringComparison.OrdinalIgnoreCase) ||
            message.Contains(T("Status.CurvePresetStarted").Split('{')[0], StringComparison.OrdinalIgnoreCase))
        {
            return T("Hero.RequestShortRunning");
        }

        if (message.Contains(T("Status.DellAutoRestored"), StringComparison.OrdinalIgnoreCase) ||
            message.Contains(T("Status.EmergencyAuto").Split('{')[0], StringComparison.OrdinalIgnoreCase))
        {
            return T("Hero.RequestShortAuto");
        }

        if (message.Contains(T("Status.SmartFanApplied").Split('{')[0], StringComparison.OrdinalIgnoreCase) ||
            message.Contains(T("Status.CurveFanApplied").Split('{')[0], StringComparison.OrdinalIgnoreCase))
        {
            return T("Hero.RequestShortApplied");
        }

        return T("Hero.RequestShortUpdated");
    }

    private bool IsPollingSkipMessage(string message)
    {
        return message.Contains("跳过", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("skipped", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowFailure(Exception ex)
    {
        var statusMessage = ex.Message;
        AddVolatileLog(T("Log.Error"), ex.Message);
        try
        {
            _appLog.Write(new AppLogRecord
            {
                Level = "Error",
                Category = "Application",
                EventName = "Failure",
                Message = ex.Message,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message,
            });
        }
        catch (Exception logException)
        {
            var logFailureMessage = F("Status.LogWriteFailed", logException.Message);
            AddVolatileLog(T("Log.Error"), logFailureMessage);
            statusMessage = $"{statusMessage}{Environment.NewLine}{logFailureMessage}";
        }

        ShowStatus(statusMessage, InfoBarSeverity.Error);
    }

    private void AddCommandLog(CommandTraceEventArgs e)
    {
        var level = e.Succeeded ? T("Log.Ok") : T("Log.Fail");
        var message = $"{e.CommandLine} [{e.ExitCode}] {e.Elapsed.TotalSeconds:0.0}s";
        AddVolatileLog(level, message);
        _appLog.Write(new AppLogRecord
        {
            Level = e.Succeeded ? "Info" : "Error",
            Category = "IpmiCommand",
            EventName = "CommandCompleted",
            Message = message,
            CommandLine = e.CommandLine,
            ExitCode = e.ExitCode,
            DurationMilliseconds = e.Elapsed.TotalMilliseconds,
            Succeeded = e.Succeeded,
            Properties = new Dictionary<string, string>
            {
                ["displayLevel"] = level,
                ["elapsedSeconds"] = e.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            },
        });
    }

    private void AddLog(
        string level,
        string message,
        string category = "Application",
        string eventName = "UiLog",
        IReadOnlyDictionary<string, string>? properties = null)
    {
        AddVolatileLog(level, message);
        _appLog.Write(new AppLogRecord
        {
            Level = NormalizeLogLevel(level),
            Category = category,
            EventName = eventName,
            Message = message,
            Properties = MergeLogProperties(level, properties),
        });
    }

    private void AddVolatileLog(string level, string message)
    {
        Logs.Insert(0, new LogEntry { Level = level, Message = message });
        while (Logs.Count > 80)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private IReadOnlyDictionary<string, string> MergeLogProperties(
        string displayLevel,
        IReadOnlyDictionary<string, string>? properties)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayLevel"] = displayLevel,
        };

        if (properties is not null)
        {
            foreach (var property in properties)
            {
                merged[property.Key] = property.Value;
            }
        }

        return merged;
    }

    private static string NormalizeLogLevel(string level)
    {
        if (level.Equals(T("Log.Error"), StringComparison.OrdinalIgnoreCase) ||
            level.Equals(T("Log.Fail"), StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (level.Equals(T("Log.Warn"), StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return "Info";
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
        if (percent is < 0 or > 100)
        {
            throw new InvalidOperationException(F("Validation.PercentRange", fieldName));
        }

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

    private int ReadSensorRefreshSeconds()
    {
        var seconds = ReadInt(SensorRefreshSecondsBox, T("Field.SensorRefreshSeconds"));
        if (seconds < AppSettings.MinimumSensorRefreshSeconds)
        {
            throw new InvalidOperationException(F("Validation.SensorRefreshSecondsMinimum", AppSettings.MinimumSensorRefreshSeconds));
        }

        return seconds;
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
        NewCurvePresetNameBox.PlaceholderText = T("Preset.CurveNewNamePlaceholder");
        UpdateNewCurveEditorModeText();
        UpdateNewCurvePreview();
        VisualizationStateText.Text = _visualizationReady
            ? T("Dashboard.VisualizationReady")
            : T("Dashboard.VisualizationLoading");
        FanSummaryText.Text = F("Overview.FansCount", _settings.FanCount);
        if (Sensors.Count == 0)
        {
            FanRpmSummaryText.Text = T("State.WaitingRefresh");
            PowerSummaryText.Text = T("Hero.LiveWaiting");
            PowerSummaryDetailText.Text = T("Overview.NoPowerReading");
            VoltageSummaryText.Text = T("Hero.LiveWaiting");
            VoltageSummaryDetailText.Text = T("Overview.NoVoltageReading");
            CurrentSummaryText.Text = T("Hero.LiveWaiting");
            CurrentSummaryDetailText.Text = T("Overview.NoCurrentReading");
        }
        else
        {
            UpdateMetricSummaries();
        }

        UpdateHeroRealtimeMetrics();
        SetModeSummary(_modeSummaryKey, _modeSummaryArgs);
        SetAutoPolicySummary(_autoPolicyRunning);
        UpdateHeroRequestStatusTexts();
        UpdatePollingStatusTexts();
        UpdateIndividualFanWarning();
        RefreshLocalizedSensorRows();
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
        HeroModeStateText.Text = modeText;
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

        if (_isConnecting)
        {
            ConnectionStateText.Text = T("State.Connecting");
            return;
        }

        if (_sensorPollingTimer.IsEnabled)
        {
            var intervalSeconds = _settings.SensorRefreshSeconds;
            ConnectionStateText.Text = _lastPollTime.HasValue
                ? F("State.ConnectedPollingTime", intervalSeconds, _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                : F("State.ConnectedPolling", intervalSeconds);
            return;
        }

        ConnectionStateText.Text = _hasDisconnected ? T("State.Disconnected") : T("State.NotConnected");
    }

    private void SetConnectingState()
    {
        _isConnecting = true;
        _hasDisconnected = false;
        ConnectionStateText.Text = T("State.Connecting");
        ShowStatus(T("Status.Connecting"), InfoBarSeverity.Informational);
    }

    private void CheckSensorPollingLatency(TimeSpan elapsed)
    {
        var interval = TimeSpan.FromSeconds(_settings.SensorRefreshSeconds);
        if (elapsed > interval)
        {
            var recommendedSeconds = GetRecommendedPollingSeconds(elapsed);
            ReportPollingWarning(F("Status.PollingLatencyExceeded", elapsed.TotalSeconds, interval.TotalSeconds, recommendedSeconds));
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

    private string BuildPollingSkippedWarning(string sampledKey, string noSampleKey, int intervalSeconds)
    {
        if (_lastPollDuration is not { } lastDuration)
        {
            return F(noSampleKey, intervalSeconds);
        }

        var recommendedSeconds = GetRecommendedPollingSeconds(lastDuration);
        return F(sampledKey, intervalSeconds, lastDuration.TotalSeconds, recommendedSeconds);
    }

    private static int GetRecommendedPollingSeconds(TimeSpan observedDuration)
    {
        return Math.Max(1, (int)Math.Ceiling(observedDuration.TotalSeconds + 1));
    }

    private void ReportPollingWarning(string message)
    {
        _pollingWasDegraded = true;
        var now = DateTimeOffset.Now;
        var throttleSeconds = Math.Max(5, _settings.SensorRefreshSeconds * 2);
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
