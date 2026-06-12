using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
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
    private const int VisualizationHistoryRetentionDays = 7;
    private const string DefaultCurvePointsText = "45 = 18%" + "\r\n" + "68 = 28%" + "\r\n" + "78 = 42%";
    private const string DefaultPowerCurvePointsText = "280W = 18%" + "\r\n" + "500W = 28%" + "\r\n" + "750W = 42%";
    private const double TemperatureCurveCanvasMinCelsius = 30;
    private const double TemperatureCurveCanvasMaxCelsius = 95;
    private const double PowerCurveCanvasMinWatts = 0;
    private const double PowerCurveCanvasMaxWatts = 1200;
    private const double CurveCanvasPadding = 8;
    private const double CurvePointHitRadius = 18;
    private const string HeroThermalTemperatureCurveAutoChinese = "温度曲线自动";
    private const string HeroThermalPowerCurveAutoChinese = "功耗曲线自动";
    private const string HeroThermalSmartPolicyChinese = "软件恒温策略";
    private const string HeroThermalDellAutoChinese = "Dell 自动温控";
    private const string CurveHoverFanSpeedChineseLabel = "风扇速度";
    private static readonly JsonSerializerOptions VisualizationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly string[] PollingSkipStatusKeys =
    [
        "Status.PollingSkippedPreviousRunning",
        "Status.PollingSkippedPreviousRunningNoSample",
        "Status.PollingSkippedIpmiBusy",
        "Status.PollingSkippedIpmiBusyNoSample",
        "Status.AutoTickSkipped",
        "Status.AutoTickSkippedIpmiBusy",
    ];

    private readonly SettingsStore _settingsStore = new();
    private readonly AppLogService _appLog = new();
    private readonly IpmiCommandService _ipmi = new();
    private readonly DispatcherTimer _autoPolicyTimer = new();
    private readonly DispatcherTimer _sensorPollingTimer = new();
    private readonly SemaphoreSlim _ipmiOperationLock = new(1, 1);
    private readonly PollingSkipLogGate _pollingSkipLogGate = new();
    private readonly List<SensorDashboardHistoryPoint> _sensorHistory = [];
    private long _sensorHistorySequence;
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
    private bool _localizedSensorsDirty = true;
    private DateTimeOffset _lastPollingWarningAt = DateTimeOffset.MinValue;
    private bool _pollingWasDegraded;
    private bool _visualizationInitialized;
    private bool _visualizationReady;
    private bool _visualizationSnapshotUpdateScheduled;
    private double _pendingVisualizationWheelDeltaY;
    private bool _visualizationWheelScrollScheduled;
    private VisualizationSnapshot? _latestVisualizationSnapshot;
    private DateTime? _latestVisualizationSnapshotTime;
    private string? _activePresetId;
    private FanPreset? _activeCurvePreset;
    private string _heroRequestMessage = string.Empty;
    private string _heroRequestSummary = string.Empty;
    private DateTime? _heroRequestUpdatedAt;
    private string _modeSummaryKey = "Mode.Idle";
    private object[] _modeSummaryArgs = Array.Empty<object>();
    private string? _editingCurvePresetId;
    private string? _editingPowerCurvePresetId;
    private FanCurvePoint? _draggingTemperatureCurvePoint;
    private FanCurvePoint? _draggingPowerCurvePoint;
    private bool _syncingTemperatureCurveInputsFromCanvas;
    private bool _syncingPowerCurveInputsFromCanvas;
    private Point? _temperatureCurveHoverPosition;
    private Point? _powerCurveHoverPosition;
    private ResponsiveLayoutSize? _activeResponsiveLayoutSize;

    private enum ResponsiveLayoutSize
    {
        Small,
        Medium,
        Large,
    }

    private sealed class DashboardTileFanAnimationState
    {
        public Storyboard? Storyboard { get; set; }

        public DashboardTileViewModel? Tile { get; set; }

        public PropertyChangedEventHandler? TilePropertyChangedHandler { get; set; }
    }

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
        NewPowerCurvePoints = [];

        LanguageComboBox.ItemsSource = LocalizationService.SupportedLanguages;
        LanguageComboBox.DisplayMemberPath = nameof(LanguageOption.DisplayName);

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

    public ObservableCollection<FanCurvePoint> NewPowerCurvePoints { get; }

    public bool MinimizeToTrayOnClose => MinimizeToTraySwitch?.IsOn ?? true;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        var shouldShowSettingsOnStart = !_settingsStore.SettingsFileExists;
        _settings = _settingsStore.Load();
        LocalizationService.SetLanguage(_settings.Language);
        TryLoadVisualizationHistory();
        LoadSettingsToControls(_settings);
        ApplyTheme(_settings.Theme);
        RebuildFanChannels();
        RebuildPresets(_settings.Presets);
        ApplyLocalization();
        ResetNewCurveEditor();
        ResetNewPowerCurveEditor();
        AddLog(T("Log.Info"), T("Status.Loaded"));
        AddLog(T("Log.Info"), F("Status.LogFileReady", _appLog.CurrentLogPath), "Application", "LogFileReady");
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

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    public void ShowSettingsView()
    {
        SelectView("Settings");
    }

    public void ShowOverviewView()
    {
        SelectView("Overview");
    }

    public void ShowControlView()
    {
        SelectView("Control");
    }

    public void ShowSensorsView()
    {
        SelectView("Sensors");
    }

    private void ApplyResponsiveLayout(double pageWidth)
    {
        if (!double.IsFinite(pageWidth) || pageWidth <= 0)
        {
            return;
        }

        var layoutSize = pageWidth < 641
            ? ResponsiveLayoutSize.Small
            : pageWidth < 1008
                ? ResponsiveLayoutSize.Medium
                : ResponsiveLayoutSize.Large;

        if (_activeResponsiveLayoutSize == layoutSize)
        {
            ApplyResponsiveVisualizationHeight(layoutSize);
            return;
        }

        _activeResponsiveLayoutSize = layoutSize;
        var isSmall = layoutSize == ResponsiveLayoutSize.Small;
        var isLarge = layoutSize == ResponsiveLayoutSize.Large;
        var pagePadding = isSmall ? 12 : layoutSize == ResponsiveLayoutSize.Medium ? 18 : 24;

        ShellNavigation.PaneDisplayMode = isSmall
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.LeftCompact;
        ContentPanel.Padding = new Thickness(pagePadding);
        StatusInfoBar.Margin = new Thickness(pagePadding, 12, pagePadding, 0);
        HeroBannerCard.Padding = new Thickness(isSmall ? 16 : 24);
        HeroBannerGlow.Visibility = isSmall ? Visibility.Collapsed : Visibility.Visible;

        ApplyResponsiveHeroLayout(layoutSize);
        ApplyResponsiveOverviewLayout(layoutSize);
        ApplyResponsiveControlLayout(layoutSize);
        ApplyResponsiveSettingsLayout(layoutSize);
        ApplyResponsiveVisualizationHeight(layoutSize);
    }

    private void ApplyResponsiveHeroLayout(ResponsiveLayoutSize layoutSize)
    {
        var isLarge = layoutSize == ResponsiveLayoutSize.Large;
        ReflowGridChildren(HeroBannerContent, isLarge ? 2 : 1, isLarge ? [new GridLength(1, GridUnitType.Star), GridLength.Auto] : [new GridLength(1, GridUnitType.Star)]);
        HeroStatusCard.Width = isLarge ? 340 : double.NaN;
        HeroStatusCard.MinWidth = isLarge ? 320 : 0;
        HeroStatusCard.HorizontalAlignment = isLarge ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        HeroStatusCard.Margin = isLarge ? new Thickness(18, 0, 0, 0) : new Thickness(0, 12, 0, 0);
        HeroRealtimeMetricsPanel.MaxWidth = isLarge ? 900 : double.PositiveInfinity;
        ReflowGridChildren(
            HeroRealtimeMetricsPanel,
            layoutSize == ResponsiveLayoutSize.Small ? 1 : layoutSize == ResponsiveLayoutSize.Medium ? 2 : 5);
    }

    private void ApplyResponsiveOverviewLayout(ResponsiveLayoutSize layoutSize)
    {
        ReflowGridChildren(
            OverviewSummaryGrid,
            layoutSize == ResponsiveLayoutSize.Small ? 1 : layoutSize == ResponsiveLayoutSize.Medium ? 3 : 6);
        ReflowGridChildren(OverviewQuickActionsGrid, layoutSize == ResponsiveLayoutSize.Large ? 2 : 1);
        QuickActionsCommandBar.DefaultLabelPosition = layoutSize == ResponsiveLayoutSize.Small
            ? CommandBarDefaultLabelPosition.Collapsed
            : CommandBarDefaultLabelPosition.Right;

        if (layoutSize == ResponsiveLayoutSize.Large)
        {
            ConfigureGridColumns(AllFanPercentGrid, new GridLength(1, GridUnitType.Star), new GridLength(120), GridLength.Auto);
            EnsureGridRows(AllFanPercentGrid, 1);
            Grid.SetColumn(AllFanPercentBox, 1);
            Grid.SetRow(AllFanPercentBox, 0);
            Grid.SetColumn(SetAllFansButton, 2);
            Grid.SetRow(SetAllFansButton, 0);
        }
        else
        {
            ConfigureGridColumns(AllFanPercentGrid, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            EnsureGridRows(AllFanPercentGrid, 1);
            Grid.SetColumn(AllFanPercentBox, 0);
            Grid.SetRow(AllFanPercentBox, 0);
            Grid.SetColumn(SetAllFansButton, 1);
            Grid.SetRow(SetAllFansButton, 0);
        }

        TemperatureGridView.MaxHeight = layoutSize == ResponsiveLayoutSize.Large ? 300 : double.PositiveInfinity;
        FanGridView.MaxHeight = layoutSize == ResponsiveLayoutSize.Large ? 260 : double.PositiveInfinity;
        PowerHealthGridView.MaxHeight = double.PositiveInfinity;
    }

    private void ApplyResponsiveControlLayout(ResponsiveLayoutSize layoutSize)
    {
        ReflowGridChildren(NewPresetGrid, layoutSize == ResponsiveLayoutSize.Large ? 3 : 1);
        ReflowGridChildren(CurveEditorGrid, layoutSize == ResponsiveLayoutSize.Large ? 3 : 1);
        ReflowGridChildren(PowerCurveEditorGrid, layoutSize == ResponsiveLayoutSize.Large ? 3 : 1);
        ReflowGridChildren(SmartAutoGrid, layoutSize == ResponsiveLayoutSize.Large ? 2 : 1);
        ReflowGridChildren(SmartAutoButtonsGrid, layoutSize == ResponsiveLayoutSize.Small ? 1 : 2);
    }

    private void ApplyResponsiveSettingsLayout(ResponsiveLayoutSize layoutSize)
    {
        var isLarge = layoutSize == ResponsiveLayoutSize.Large;
        ConfigureGridColumns(SettingsView, isLarge ? [new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star)] : [new GridLength(1, GridUnitType.Star)]);
        EnsureGridRows(SettingsView, isLarge ? 2 : 3);

        Grid.SetRow(SettingsCommandBar, 0);
        Grid.SetColumn(SettingsCommandBar, 0);
        Grid.SetColumnSpan(SettingsCommandBar, isLarge ? 2 : 1);
        SettingsCommandBar.DefaultLabelPosition = layoutSize == ResponsiveLayoutSize.Small
            ? CommandBarDefaultLabelPosition.Collapsed
            : CommandBarDefaultLabelPosition.Right;

        Grid.SetRow(ConnectionSettingsCard, 1);
        Grid.SetColumn(ConnectionSettingsCard, 0);
        Grid.SetColumnSpan(ConnectionSettingsCard, 1);
        ConnectionSettingsCard.Margin = isLarge ? new Thickness(0, 0, 9, 0) : new Thickness(0);

        Grid.SetRow(ApplicationSettingsCard, isLarge ? 1 : 2);
        Grid.SetColumn(ApplicationSettingsCard, isLarge ? 1 : 0);
        Grid.SetColumnSpan(ApplicationSettingsCard, 1);
        ApplicationSettingsCard.Margin = isLarge ? new Thickness(9, 0, 0, 0) : new Thickness(0, 12, 0, 0);
    }

    private void ApplyResponsiveVisualizationHeight(ResponsiveLayoutSize layoutSize)
    {
        VisualizationWebView.MinHeight = layoutSize == ResponsiveLayoutSize.Large ? 1520 : 3200;
    }

    private static void ReflowGridChildren(Grid grid, int columns, params GridLength[] columnWidths)
    {
        var safeColumns = Math.Max(1, columns);
        if (columnWidths.Length == 0)
        {
            columnWidths = Enumerable.Repeat(new GridLength(1, GridUnitType.Star), safeColumns).ToArray();
        }

        ConfigureGridColumns(grid, columnWidths);
        EnsureGridRows(grid, (int)Math.Ceiling(grid.Children.Count / (double)safeColumns));

        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is not FrameworkElement child)
            {
                continue;
            }

            Grid.SetColumn(child, index % safeColumns);
            Grid.SetRow(child, index / safeColumns);
            Grid.SetColumnSpan(child, 1);
            Grid.SetRowSpan(child, 1);
        }
    }

    private static void ConfigureGridColumns(Grid grid, params GridLength[] columnWidths)
    {
        grid.ColumnDefinitions.Clear();
        foreach (var width in columnWidths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }
    }

    private static void EnsureGridRows(Grid grid, int rowCount)
    {
        var safeRows = Math.Max(1, rowCount);
        grid.RowDefinitions.Clear();
        for (var i = 0; i < safeRows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
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
            throw new FileNotFoundException(T("Dashboard.VisualizationAssetMissing"), dashboardPath);
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
        ScheduleVisualizationSnapshot();
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

        ScheduleVisualizationWheelScroll(deltaY);
    }

    private void ScheduleVisualizationWheelScroll(double deltaY)
    {
        _pendingVisualizationWheelDeltaY += deltaY;
        if (_visualizationWheelScrollScheduled)
        {
            return;
        }

        _visualizationWheelScrollScheduled = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, ApplyPendingVisualizationWheelScroll))
        {
            _visualizationWheelScrollScheduled = false;
            ShowFailure(new InvalidOperationException("Unable to schedule forwarded chart scrolling on the UI dispatcher."));
        }
    }

    private void ApplyPendingVisualizationWheelScroll()
    {
        _visualizationWheelScrollScheduled = false;
        var deltaY = _pendingVisualizationWheelDeltaY;
        _pendingVisualizationWheelDeltaY = 0;
        if (Math.Abs(deltaY) < 0.01)
        {
            return;
        }

        var nextOffset = Math.Clamp(
            ContentScrollViewer.VerticalOffset + deltaY,
            0,
            ContentScrollViewer.ScrollableHeight);
        ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);
    }

    public Task ApplyQuickFanSpeedAsync(int percent)
    {
        AllFanSlider.Value = percent;
        AllFanPercentBox.Value = percent;
        return ApplyAllFansAsync(percent);
    }

    public Task RefreshSensorsFromTrayAsync()
    {
        return RefreshSensorsAsync();
    }

    public Task RestoreDellFactoryFanSpeedFromTrayAsync()
    {
        return ResetDellAutomaticModeAsync();
    }

    public Task ApplyPresetFromTrayAsync(FanPreset preset)
    {
        return ApplyPresetAsync(preset);
    }

    public void StopAutoPolicyFromTray()
    {
        try
        {
            StopAutoPolicy();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    public Task OpenIdracFromTrayAsync()
    {
        OpenIdrac();
        return Task.CompletedTask;
    }

    public void OpenLogFolderFromTray()
    {
        OpenLogFolder();
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

            SelectLanguage(settings.Language);
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
        if (_sensorPollingTimer.IsEnabled)
        {
            CancelSensorPollingFromUser();
            return;
        }

        await ConnectAndStartPollingAsync();
    }

    private void CancelSensorPollingFromUser()
    {
        var reason = T("Action.CancelPolling");
        StopSensorPolling(reason);
        ShowStatus(F("Status.PollingStopped", reason), InfoBarSeverity.Informational);
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

    private async Task RefreshSensorsAfterFanCommandAsync()
    {
        await RunUiCommandAsync(T("Status.RefreshingSensorsAfterFanCommand"), async token =>
        {
            var elapsed = await RefreshSensorsCoreAsync(ReadProfile(), token);
            CheckSensorPollingLatency(elapsed);
            RestartSensorPollingAfterImmediateRefresh();
            ShowStatus(F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds), InfoBarSeverity.Success);
        });
    }

    private void RestartSensorPollingAfterImmediateRefresh()
    {
        if (!_sensorPollingTimer.IsEnabled)
        {
            return;
        }

        _sensorPollingTimer.Stop();
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SensorRefreshSeconds));
        _sensorPollingTimer.Start();
        UpdatePollingStatusTexts();
    }

    private async Task ConnectAndStartPollingAsync()
    {
        SetConnectingState();
        var connected = await RunUiCommandAsync(T("Status.Connecting"), async token =>
        {
            var profile = ReadProfile();
            await _ipmi.TestConnectionAsync(profile, token);
            var elapsed = await RefreshSensorsCoreAsync(profile, token);
            StartSensorPolling();
            ShowStatus(T("Status.ConnectedPolling"), InfoBarSeverity.Success);
            CheckSensorPollingLatency(elapsed);
        });
        if (connected)
        {
            try
            {
                await RestoreLastRunningStateAsync();
            }
            catch (Exception ex)
            {
                StopSensorPolling(ex.Message);
                ShowFailure(ex);
            }
        }
    }

    private void StartSensorPolling()
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingTimer.Start();
        _isConnecting = false;
        _hasDisconnected = false;
        _pollingWasDegraded = false;
        _lastPollingWarningAt = DateTimeOffset.MinValue;
        _pollingSkipLogGate.ResetAll();
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

    private async Task RestoreLastRunningStateAsync()
    {
        if (_settings.LastSmartAutoPolicyRunning)
        {
            await StartSmartAutoPolicyAsync(persistControls: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.LastRunningPresetId))
        {
            return;
        }

        var presetId = _settings.LastRunningPresetId.Trim();
        var preset = Presets.FirstOrDefault(item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(F("Validation.PresetNotFound", presetId));
        await ApplyPresetAsync(preset);
    }

    private async void OnSensorPollingTimerTick(object? sender, object e)
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        if (_autoPolicyRunning)
        {
            return;
        }

        if (_sensorPollingTickRunning)
        {
            LogPollingSkip(
                PollingSkipKind.PreviousPollRunning,
                BuildPollingSkippedWarning("Status.PollingSkippedPreviousRunning", "Status.PollingSkippedPreviousRunningNoSample", intervalSeconds));
            return;
        }

        _sensorPollingTickRunning = true;
        _pollingSkipLogGate.Reset(PollingSkipKind.PreviousPollRunning);
        if (!await _ipmiOperationLock.WaitAsync(0))
        {
            _sensorPollingTickRunning = false;
            LogPollingSkip(
                PollingSkipKind.IpmiCommandBusy,
                BuildPollingSkippedWarning("Status.PollingSkippedIpmiBusy", "Status.PollingSkippedIpmiBusyNoSample", intervalSeconds));
            return;
        }

        _pollingSkipLogGate.Reset(PollingSkipKind.IpmiCommandBusy);
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
            var snapshotTime = DateTime.Now;
            _lastPollTime = snapshotTime;
            RecordVisualizationHistoryPoint(snapshotTime);
            ScheduleVisualizationSnapshot();
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
        var succeeded = await RunUiCommandAsync(F("Status.SetAllFans", percent), async token =>
        {
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            _activeCurvePreset = null;
            SetModeSummary("Mode.Manual");
            MarkActivePreset(null, persistRunningState: true);
            ShowStatus(F("Status.AllFansSet", percent), InfoBarSeverity.Success);
        });

        if (succeeded)
        {
            await RefreshSensorsAfterFanCommandAsync();
        }
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

        var succeeded = await RunUiCommandAsync(F("Status.FanSet", fanIndex, percent), async token =>
        {
            await _ipmi.SetSingleFanManualSpeedAsync(ReadProfile(), fanIndex, percent, token);
            _activeCurvePreset = null;
            SetModeSummary("Mode.Manual");
            MarkActivePreset(null, persistRunningState: true);
            ShowStatus(F("Status.FanSet", fanIndex, percent), InfoBarSeverity.Success);
        });

        if (succeeded)
        {
            await RefreshSensorsAfterFanCommandAsync();
        }
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

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);
            var shouldReapply = _activePresetId?.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true;
            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            if (_activeCurvePreset?.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activeCurvePreset = _settings.Presets.FirstOrDefault(item => item.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase));
            }

            _settingsStore.Save(_settings);
            RefreshPresetRows();
            ShowStatus(F("Status.PresetSaved", preset.DisplayName), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F("Status.PresetSaved", preset.DisplayName));
            if (shouldReapply)
            {
                var savedPreset = Presets.FirstOrDefault(item => item.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(F("Validation.PresetNotFound", preset.Id));
                await ApplyPresetAsync(savedPreset);
            }
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
                ClearPersistedRunningState();
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

    private async void OnAddCurvePresetClick(object sender, RoutedEventArgs e)
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
            var savedPresetId = existing?.Id ?? preset.Id;
            var statusKey = "Status.CurvePresetAdded";
            var shouldReapply = existing is not null &&
                _activePresetId?.Equals(existing.Id, StringComparison.OrdinalIgnoreCase) == true;
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
            ScrollPresetIntoView(savedPresetId);
            ShowStatus(F(statusKey, name), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F(statusKey, name));
            if (shouldReapply)
            {
                var savedPreset = Presets.FirstOrDefault(item => item.Id.Equals(savedPresetId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(F("Validation.PresetNotFound", savedPresetId));
                await ApplyPresetAsync(savedPreset);
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void OnAddPowerCurvePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NewPowerCurvePresetNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(T("Validation.PresetNameRequired"));
            }

            var preset = new FanPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = FanPreset.PowerCurveKind,
                Name = name,
                CurvePoints = ReadNewPowerCurvePoints(),
                SmoothCurve = NewPowerCurveSmoothSwitch.IsOn,
            };
            preset.ValidateCurvePoints();

            var existing = string.IsNullOrWhiteSpace(_editingPowerCurvePresetId)
                ? null
                : Presets.FirstOrDefault(item => item.Id.Equals(_editingPowerCurvePresetId, StringComparison.OrdinalIgnoreCase));
            var savedPresetId = existing?.Id ?? preset.Id;
            var statusKey = "Status.CurvePresetAdded";
            var shouldReapply = existing is not null &&
                _activePresetId?.Equals(existing.Id, StringComparison.OrdinalIgnoreCase) == true;
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
            ResetNewPowerCurveEditor();
            RefreshPresetRows();
            ScrollPresetIntoView(savedPresetId);
            ShowStatus(F(statusKey, name), InfoBarSeverity.Success);
            AddLog(T("Log.Info"), F(statusKey, name));
            if (shouldReapply)
            {
                var savedPreset = Presets.FirstOrDefault(item => item.Id.Equals(savedPresetId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(F("Validation.PresetNotFound", savedPresetId));
                await ApplyPresetAsync(savedPreset);
            }
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

    private void OnAddNewPowerCurvePointClick(object sender, RoutedEventArgs e)
    {
        try
        {
            NewPowerCurvePoints.Add(new FanCurvePoint
            {
                PowerWatts = ReadDouble(NewPowerCurveWattsBox, T("SensorDisplay.PowerConsumption")),
                FanPercent = CheckedPercent(NewPowerCurvePercentBox.Value, T("Field.CurveFanPercent")),
            });
            NormalizeNewPowerCurveEditorPoints();
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

    private void OnDeleteNewPowerCurvePointClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FanCurvePoint point })
        {
            NewPowerCurvePoints.Remove(point);
            UpdateNewPowerCurvePreview();
        }
    }

    private void OnNewCurvePointValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingTemperatureCurveInputsFromCanvas || _draggingTemperatureCurvePoint is not null)
        {
            return;
        }

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

    private void OnNewPowerCurvePointValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingPowerCurveInputsFromCanvas || _draggingPowerCurvePoint is not null)
        {
            return;
        }

        if (sender.DataContext is FanCurvePoint point && !double.IsNaN(sender.Value))
        {
            if (string.Equals(sender.Tag as string, "Power", StringComparison.OrdinalIgnoreCase))
            {
                point.PowerWatts = sender.Value;
            }
            else
            {
                point.FanPercent = sender.Value;
            }
        }

        UpdateNewPowerCurvePreview();
    }

    private void OnNewCurveCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (NewCurveCanvas.ActualWidth <= 0 || NewCurveCanvas.ActualHeight <= 0)
        {
            return;
        }

        var position = e.GetCurrentPoint(NewCurveCanvas).Position;
        _temperatureCurveHoverPosition = position;
        var point = FindNearestTemperatureCurvePoint(position);
        if (point is null)
        {
            point = new FanCurvePoint();
            NewCurvePoints.Add(point);
        }

        _draggingTemperatureCurvePoint = point;
        NewCurveCanvas.CapturePointer(e.Pointer);
        UpdateTemperatureCurvePointFromCanvasInput(point, position);
        DrawNewCurveCanvas(BuildTemperatureCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewCurveCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(NewCurveCanvas);
        _temperatureCurveHoverPosition = currentPoint.Position;
        if (_draggingTemperatureCurvePoint is null)
        {
            DrawNewCurveCanvas(BuildTemperatureCurveCanvasPreviewPoints(), useSmoothPreview: false);
            e.Handled = true;
            return;
        }

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            FinishTemperatureCurveDrag();
            return;
        }

        UpdateTemperatureCurvePointFromCanvasInput(_draggingTemperatureCurvePoint, currentPoint.Position);
        DrawNewCurveCanvas(BuildTemperatureCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewCurveCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _temperatureCurveHoverPosition = e.GetCurrentPoint(NewCurveCanvas).Position;
        FinishTemperatureCurveDrag();
        e.Handled = true;
    }

    private void OnNewCurveCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        FinishTemperatureCurveDrag();
        e.Handled = true;
    }

    private void OnNewCurveCanvasPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _temperatureCurveHoverPosition = e.GetCurrentPoint(NewCurveCanvas).Position;
        DrawNewCurveCanvas(BuildTemperatureCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewCurveCanvasPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _temperatureCurveHoverPosition = null;
        UpdateNewCurvePreview();
        e.Handled = true;
    }

    private void OnNewCurveCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNewCurvePreview();
    }

    private void OnNewCurveSmoothToggled(object sender, RoutedEventArgs e)
    {
        UpdateNewCurvePreview();
    }

    private void OnNewPowerCurveCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (NewPowerCurveCanvas.ActualWidth <= 0 || NewPowerCurveCanvas.ActualHeight <= 0)
        {
            return;
        }

        var position = e.GetCurrentPoint(NewPowerCurveCanvas).Position;
        _powerCurveHoverPosition = position;
        var point = FindNearestPowerCurvePoint(position);
        if (point is null)
        {
            point = new FanCurvePoint();
            NewPowerCurvePoints.Add(point);
        }

        _draggingPowerCurvePoint = point;
        NewPowerCurveCanvas.CapturePointer(e.Pointer);
        UpdatePowerCurvePointFromCanvasInput(point, position);
        DrawNewPowerCurveCanvas(BuildPowerCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(NewPowerCurveCanvas);
        _powerCurveHoverPosition = currentPoint.Position;
        if (_draggingPowerCurvePoint is null)
        {
            DrawNewPowerCurveCanvas(BuildPowerCurveCanvasPreviewPoints(), useSmoothPreview: false);
            e.Handled = true;
            return;
        }

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            FinishPowerCurveDrag();
            return;
        }

        UpdatePowerCurvePointFromCanvasInput(_draggingPowerCurvePoint, currentPoint.Position);
        DrawNewPowerCurveCanvas(BuildPowerCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _powerCurveHoverPosition = e.GetCurrentPoint(NewPowerCurveCanvas).Position;
        FinishPowerCurveDrag();
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        FinishPowerCurveDrag();
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _powerCurveHoverPosition = e.GetCurrentPoint(NewPowerCurveCanvas).Position;
        DrawNewPowerCurveCanvas(BuildPowerCurveCanvasPreviewPoints(), useSmoothPreview: false);
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _powerCurveHoverPosition = null;
        UpdateNewPowerCurvePreview();
        e.Handled = true;
    }

    private void OnNewPowerCurveCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNewPowerCurvePreview();
    }

    private void OnNewPowerCurveSmoothToggled(object sender, RoutedEventArgs e)
    {
        UpdateNewPowerCurvePreview();
    }

    private void OnResetNewCurveEditorClick(object sender, RoutedEventArgs e)
    {
        ResetNewCurveEditor();
    }

    private void OnResetNewPowerCurveEditorClick(object sender, RoutedEventArgs e)
    {
        ResetNewPowerCurveEditor();
    }

    private void OnEditCurvePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);
            if (preset.IsPowerCurvePreset)
            {
                _editingPowerCurvePresetId = preset.Id;
                NewPowerCurvePresetNameBox.Text = preset.DisplayName;
                NewPowerCurveSmoothSwitch.IsOn = preset.SmoothCurve;
                ReplaceNewPowerCurvePoints(preset.CurvePoints);
                UpdateNewPowerCurveEditorModeText();
                UpdateNewPowerCurvePreview();
                ScrollEditorIntoView(PowerCurveEditorGrid);
                return;
            }

            if (!preset.IsTemperatureCurvePreset)
            {
                return;
            }

            _editingCurvePresetId = preset.Id;
            NewCurvePresetNameBox.Text = preset.DisplayName;
            NewCurveSmoothSwitch.IsOn = preset.SmoothCurve;
            ReplaceNewCurvePoints(preset.CurvePoints);
            UpdateNewCurveEditorModeText();
            UpdateNewCurvePreview();
            ScrollEditorIntoView(CurveEditorGrid);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void ScrollEditorIntoView(FrameworkElement editorElement)
    {
        editorElement.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.08,
            VerticalOffset = -12,
        });
    }

    private void ScrollPresetIntoView(string presetId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var preset = Presets.FirstOrDefault(item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(F("Validation.PresetNotFound", presetId));
                PresetGridView.ScrollIntoView(preset, ScrollIntoViewAlignment.Leading);
                PresetGridView.UpdateLayout();
                if (PresetGridView.ContainerFromItem(preset) is FrameworkElement presetContainer)
                {
                    presetContainer.StartBringIntoView(new BringIntoViewOptions
                    {
                        AnimationDesired = true,
                        VerticalAlignmentRatio = 0.08,
                        VerticalOffset = -12,
                    });
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (PresetGridView.ContainerFromItem(preset) is FrameworkElement delayedPresetContainer)
                        {
                            delayedPresetContainer.StartBringIntoView(new BringIntoViewOptions
                            {
                                AnimationDesired = true,
                                VerticalAlignmentRatio = 0.08,
                                VerticalOffset = -12,
                            });
                            return;
                        }

                        throw new InvalidOperationException(F("Validation.PresetNotFound", preset.DisplayName));
                    }
                    catch (Exception ex)
                    {
                        ShowFailure(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
        });
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

        if (preset.IsCurvePreset)
        {
            return ApplyCurvePresetAsync(preset);
        }

        throw new InvalidOperationException(F("Validation.UnsupportedPresetKind", preset.Kind));
    }

    private async Task ApplyManualPresetAsync(FanPreset preset)
    {
        var percent = CheckedPercent(preset.Percent, T("Field.AllFanPercent"));
        var succeeded = await RunUiCommandAsync(F("Status.SetAllFans", percent), async token =>
        {
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            _activeCurvePreset = null;
            SetModeSummary("Mode.PresetManual", preset.DisplayName, percent);
            MarkActivePreset(preset.Id, persistRunningState: true);
            ShowStatus(F("Status.PresetApplied", preset.DisplayName), InfoBarSeverity.Success);
        });

        if (succeeded)
        {
            await RefreshSensorsAfterFanCommandAsync();
        }
    }

    private async Task RestoreDefaultManualAsync(string? activePresetId = "restore-manual", int? percentOverride = null)
    {
        var percent = percentOverride ?? AppSettings.LocalDefaultManualFanPercent;
        var succeeded = await RunUiCommandAsync(F("Status.RestoringDefault", percent), async token =>
        {
            PersistSettingsFromControls();
            AllFanSlider.Value = percent;
            AllFanPercentBox.Value = percent;
            await _ipmi.SetAllFansManualSpeedAsync(ReadProfile(), percent, token);
            _activeCurvePreset = null;
            SetModeSummary("Mode.ManualPercent", percent);
            MarkActivePreset(activePresetId, persistRunningState: true);
            ShowStatus(F("Status.RestoredDefault", percent), InfoBarSeverity.Success);
        });

        if (succeeded)
        {
            await RefreshSensorsAfterFanCommandAsync();
        }
    }

    private async Task ResetDellAutomaticModeAsync(string? activePresetId = "dell-auto")
    {
        var succeeded = await RunUiCommandAsync(T("Status.ResettingDellAuto"), async token =>
        {
            await _ipmi.SetDellAutomaticModeAsync(ReadProfile(), token);
            _activeCurvePreset = null;
            SetModeSummary("Mode.DellAuto");
            MarkActivePreset(activePresetId, persistRunningState: true);
            ShowStatus(T("Status.DellAutoRestored"), InfoBarSeverity.Success);
        });

        if (succeeded)
        {
            await RefreshSensorsAfterFanCommandAsync();
        }
    }

    private async Task ApplyCurvePresetAsync(FanPreset preset)
    {
        var curvePreset = ValidateAndClonePreset(preset);
        var started = await RunUiCommandAsync(
            F("Status.CurvePresetStarted", curvePreset.DisplayName),
            async token =>
            {
                PersistSettingsFromControls();
                _activeCurvePreset = curvePreset;
                PrepareAutoPolicyRunningState();
                SetModeSummary("Mode.CurveAuto", curvePreset.DisplayName);
                MarkActivePreset(curvePreset.Id);
                AddLog(T("Log.Info"), F("Status.CurvePresetStarted", curvePreset.DisplayName));
                try
                {
                    await RunAutoPolicyOnceCoreAsync(token);
                    PersistRunningPresetState(curvePreset.Id);
                }
                catch
                {
                    StopAutoPolicyAfterFailure();
                    throw;
                }
            });

        if (started)
        {
            StartAutoPolicyTimer();
            ShowStatus(F("Status.CurvePresetStarted", curvePreset.DisplayName), InfoBarSeverity.Success);
        }
        else
        {
            StopAutoPolicyAfterFailure();
        }
    }

    private async void OnStartAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartSmartAutoPolicyAsync(persistControls: true);
        }
        catch (Exception ex)
        {
            StopAutoPolicyAfterFailure();
            ShowFailure(ex);
        }
    }

    private async Task StartSmartAutoPolicyAsync(bool persistControls)
    {
        if (persistControls)
        {
            PersistSettingsFromControls();
        }

        _activeCurvePreset = null;
        PrepareAutoPolicyRunningState();
        SetModeSummary("Mode.SmartAuto");
        AddLog(T("Log.Info"), T("Status.AutoStarted"));

        var started = await RunUiCommandAsync(
            T("Status.AutoStarted"),
            async token =>
            {
                await RunAutoPolicyOnceCoreAsync(token);
                PersistSmartAutoRunningState();
            });

        if (started)
        {
            StartAutoPolicyTimer();
        }
        else
        {
            StopAutoPolicyAfterFailure();
        }
    }

    private void OnStopAutoPolicyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StopAutoPolicy();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void StopAutoPolicy()
    {
        _autoPolicyTimer.Stop();
        _activeCurvePreset = null;
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        MarkActivePreset(null);
        ClearPersistedRunningState();
        AddLog(T("Log.Info"), T("Status.AutoStopped"));
    }

    private void PrepareAutoPolicyRunningState()
    {
        StartAutoButton.IsEnabled = false;
        StopAutoButton.IsEnabled = true;
        SetAutoPolicySummary(true);
    }

    private void StartAutoPolicyTimer()
    {
        _autoPolicyTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SensorRefreshSeconds));
        _autoPolicyTimer.Start();
    }

    private void StopAutoPolicyAfterFailure()
    {
        _autoPolicyTimer.Stop();
        _activeCurvePreset = null;
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        MarkActivePreset(null);
        ClearPersistedRunningState();
    }

    private async void OnAutoPolicyTimerTick(object? sender, object e)
    {
        if (_autoPolicyTickRunning)
        {
            AddLog(T("Log.Warn"), T("Status.AutoTickSkipped"));
            return;
        }

        _autoPolicyTickRunning = true;
        var lockTaken = false;
        try
        {
            if (!await _ipmiOperationLock.WaitAsync(0))
            {
                var message = T("Status.AutoTickSkippedIpmiBusy");
                SetHeroRequestStatus(message, InfoBarSeverity.Warning);
                AddLog(T("Log.Warn"), message);
                return;
            }

            lockTaken = true;
            await RunAutoPolicyOnceCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StopAutoPolicyAfterFailure();
            ShowFailure(ex);
        }
        finally
        {
            if (lockTaken)
            {
                _ipmiOperationLock.Release();
            }

            _autoPolicyTickRunning = false;
        }
    }

    private async Task RunAutoPolicyOnceCoreAsync(CancellationToken cancellationToken)
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
            operationProperties["curvePresetKind"] = activeCurvePreset.Kind;
            operationProperties["curvePoints"] = activeCurvePreset.CurvePointsText;
        }

        var operation = _appLog.StartOperation(
            "SmartAutoPolicyTick",
            T("Status.AutoStarted"),
            operationProperties);

        try
        {
            var profile = BuildProfileFromSettings();
            var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken);
            ReplaceSensors(readings);
            UpdateMetricSummaries();
            var snapshotTime = DateTime.Now;
            _lastPollTime = snapshotTime;
            RecordVisualizationHistoryPoint(snapshotTime);
            ScheduleVisualizationSnapshot();

            var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(readings);
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

            double? powerWatts = null;
            var percent = CalculateFanPercentForAutoTick(activeCurvePreset, cpuTemp, readings, out powerWatts);

            await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);
            var message = activeCurvePreset is null
                ? F("Status.SmartFanApplied", cpuTemp, percent)
                : activeCurvePreset.IsPowerCurvePreset
                    ? FormatPowerCurveFanApplied(activeCurvePreset.DisplayName, powerWatts!.Value, percent)
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
            var successProperties = new Dictionary<string, string>
            {
                ["cpuTemperatureCelsius"] = cpuTemp.ToString("0.0", CultureInfo.InvariantCulture),
                ["fanPercent"] = percent.ToString(CultureInfo.InvariantCulture),
                ["action"] = "SetAllFansManualSpeed",
            };
            if (powerWatts.HasValue)
            {
                successProperties["powerWatts"] = powerWatts.Value.ToString("0.0", CultureInfo.InvariantCulture);
            }

            operation.Succeed(message, successProperties);
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

    private int CalculateFanPercentForAutoTick(
        FanPreset? activeCurvePreset,
        double cpuTemp,
        IReadOnlyList<SensorReading> readings,
        out double? powerWatts)
    {
        powerWatts = null;
        if (activeCurvePreset is null)
        {
            return CalculateAutoFanPercent(cpuTemp);
        }

        if (activeCurvePreset.IsPowerCurvePreset)
        {
            powerWatts = FindPowerWatts(readings);
            return activeCurvePreset.CalculateFanPercentForPower(powerWatts.Value);
        }

        return activeCurvePreset.CalculateFanPercent(cpuTemp);
    }

    private static double FindPowerWatts(IEnumerable<SensorReading> readings)
    {
        var powerSensor = readings.FirstOrDefault(IsPowerWattsSensor);
        if (powerSensor?.NumericValue is { } watts)
        {
            return Math.Round(watts, 1);
        }

        throw new InvalidOperationException(T("Status.PowerCurveNoPowerReading"));
    }

    private string FormatPowerCurveFanApplied(string presetName, double powerWatts, int percent)
    {
        return F("Status.PowerCurveFanApplied", presetName, powerWatts, T("SensorUnit.Watts"), percent);
    }

    private async void OnVisitIdracClick(object sender, RoutedEventArgs e)
    {
        OpenIdrac();
        await Task.CompletedTask;
    }

    private void OpenIdrac()
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
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        OpenLogFolder();
    }

    private void OpenLogFolder()
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

        if (tag == "Sensors")
        {
            RefreshLocalizedSensorRowsIfVisible();
        }
    }

    private async Task<bool> RunUiCommandAsync(
        string description,
        Func<CancellationToken, Task> command,
        bool waitForIpmiLock = true)
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
            if (waitForIpmiLock)
            {
                await _ipmiOperationLock.WaitAsync(cancellation.Token);
            }
            else if (!await _ipmiOperationLock.WaitAsync(0, cancellation.Token))
            {
                throw new InvalidOperationException(T("Status.IpmiCommandBusy"));
            }

            lockTaken = true;
            await command(cancellation.Token);
            if (string.Equals(_heroRequestMessage, runningMessage, StringComparison.Ordinal))
            {
                SetHeroRequestStatus(F("Hero.RequestSucceeded", description));
            }

            operation.Succeed(description);
            return true;
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
                    return false;
                }
            }

            ShowFailure(ex);
            return false;
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

        MarkLocalizedSensorRowsDirty();
    }

    private void UpdateMetricSummaries()
    {
        var temperatureReadings = Sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        var cpuTemp = IpmiCommandService.TryFindCpuTemperatureCelsius(temperatureReadings);
        CpuTemperatureText.Text = cpuTemp.HasValue
            ? $"{cpuTemp.Value:0.0} {T("SensorUnit.Celsius")}"
            : T("State.WaitingRefresh");
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
            .Select(BuildDashboardTile);
        ReplaceTiles(PowerTiles, powerAndHealth);
        UpdateHeroRealtimeMetrics();
    }

    private DashboardTileViewModel BuildDashboardTile(SensorReading sensor)
    {
        var style = GetDashboardTileStyle(sensor);
        return new DashboardTileViewModel
        {
            Id = BuildSensorStableId(sensor),
            Title = BuildVisualizationSensorName(sensor),
            Value = BuildDashboardTileValue(sensor),
            ValueFontSize = sensor.NumericValue.HasValue ? 26 : 22,
            ValueMaxLines = sensor.NumericValue.HasValue ? 1 : 2,
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
        const double MaximumFanAnimationRpm = 18000;
        const double SlowestFanRotationSeconds = 5.2;
        const double FastestFanRotationSeconds = 0.11;

        if (!rpm.HasValue || rpm.Value <= 0)
        {
            return SlowestFanRotationSeconds;
        }

        var normalized = Math.Clamp(rpm.Value / MaximumFanAnimationRpm, 0, 1);
        var slowestRotationsPerSecond = 1 / SlowestFanRotationSeconds;
        var fastestRotationsPerSecond = 1 / FastestFanRotationSeconds;
        var rotationsPerSecond = slowestRotationsPerSecond + (normalized * (fastestRotationsPerSecond - slowestRotationsPerSecond));
        return Math.Round(1 / rotationsPerSecond, 2);
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

        return FormatHeroRealtimeItems(items, numberFormat);
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

    private void OnDashboardTileFanIconUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            StopDashboardTileFanAnimation(element, detachTile: true, clearTransform: true);
        }
    }

    private void StartDashboardTileFanAnimation(FrameworkElement element)
    {
        var state = GetDashboardTileFanAnimationState(element);
        var tile = element.DataContext as DashboardTileViewModel;
        AttachDashboardTileFanAnimationObserver(element, state, tile);

        var currentAngle = GetDashboardTileRotationAngle(element);
        state.Storyboard?.Stop();
        state.Storyboard = null;

        if (tile is null ||
            !tile.IsFanAnimated ||
            tile.FanIconOpacity <= 0)
        {
            element.RenderTransform = null;
            return;
        }

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
        state.Storyboard = storyboard;
        element.Tag = state;
        storyboard.Begin();
    }

    private DashboardTileFanAnimationState GetDashboardTileFanAnimationState(FrameworkElement element)
    {
        if (element.Tag is DashboardTileFanAnimationState state)
        {
            return state;
        }

        if (element.Tag is Storyboard legacyStoryboard)
        {
            legacyStoryboard.Stop();
        }

        state = new DashboardTileFanAnimationState();
        element.Tag = state;
        return state;
    }

    private void AttachDashboardTileFanAnimationObserver(
        FrameworkElement element,
        DashboardTileFanAnimationState state,
        DashboardTileViewModel? tile)
    {
        if (ReferenceEquals(state.Tile, tile))
        {
            return;
        }

        DetachDashboardTileFanAnimationObserver(state);
        state.Tile = tile;

        if (tile is null)
        {
            return;
        }

        state.TilePropertyChangedHandler = (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName) ||
                args.PropertyName == nameof(DashboardTileViewModel.FanRotationSeconds) ||
                args.PropertyName == nameof(DashboardTileViewModel.IsFanAnimated) ||
                args.PropertyName == nameof(DashboardTileViewModel.FanIconOpacity))
            {
                StartDashboardTileFanAnimation(element);
            }
        };
        tile.PropertyChanged += state.TilePropertyChangedHandler;
    }

    private static void DetachDashboardTileFanAnimationObserver(DashboardTileFanAnimationState state)
    {
        if (state.Tile is not null && state.TilePropertyChangedHandler is not null)
        {
            state.Tile.PropertyChanged -= state.TilePropertyChangedHandler;
        }

        state.Tile = null;
        state.TilePropertyChangedHandler = null;
    }

    private static void StopDashboardTileFanAnimation(
        FrameworkElement element,
        bool detachTile,
        bool clearTransform)
    {
        if (element.Tag is DashboardTileFanAnimationState state)
        {
            state.Storyboard?.Stop();
            state.Storyboard = null;

            if (detachTile)
            {
                DetachDashboardTileFanAnimationObserver(state);
            }

            element.Tag = null;
        }
        else if (element.Tag is Storyboard legacyStoryboard)
        {
            legacyStoryboard.Stop();
            element.Tag = null;
        }

        if (clearTransform)
        {
            element.RenderTransform = null;
        }
    }

    private static double GetDashboardTileRotationAngle(FrameworkElement element)
    {
        if (element.RenderTransform is not RotateTransform rotateTransform ||
            double.IsNaN(rotateTransform.Angle) ||
            double.IsInfinity(rotateTransform.Angle))
        {
            return 0;
        }

        var angle = rotateTransform.Angle % 360;
        return angle < 0 ? angle + 360 : angle;
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

        var scaleTransform = new ScaleTransform { ScaleX = 0.98, ScaleY = 0.98 };
        element.RenderTransform = scaleTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleX = new DoubleAnimation
        {
            From = 0.98,
            To = 1.06,
            AutoReverse = true,
            Duration = new Duration(TimeSpan.FromSeconds(tile.ElectricalPulseSeconds)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        var scaleY = new DoubleAnimation
        {
            From = 0.98,
            To = 1.06,
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
            throw new InvalidOperationException(LocalizationService.Format("Dashboard.MetricColorInvalid", hex));
        }

        var alpha = Convert.ToByte(hex.Substring(1, 2), 16);
        var red = Convert.ToByte(hex.Substring(3, 2), 16);
        var green = Convert.ToByte(hex.Substring(5, 2), 16);
        var blue = Convert.ToByte(hex.Substring(7, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    private void TryLoadVisualizationHistory()
    {
        try
        {
            LoadVisualizationHistory();
        }
        catch (Exception ex)
        {
            ReportVisualizationHistoryFailure("Dashboard.HistoryLoadFailed", ex);
        }
    }

    private void LoadVisualizationHistory()
    {
        _sensorHistory.Clear();
        _sensorHistorySequence = 0;

        var historyDirectory = VisualizationHistoryDirectory;
        Directory.CreateDirectory(historyDirectory);
        var now = DateTimeOffset.Now;
        PruneVisualizationHistoryFiles(now);
        var cutoff = now.AddDays(-VisualizationHistoryRetentionDays);

        foreach (var historyPath in Directory.EnumerateFiles(historyDirectory, "chart-history-*.jsonl").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(historyPath, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var point = JsonSerializer.Deserialize<SensorDashboardHistoryPoint>(line, VisualizationJsonOptions)
                    ?? throw new InvalidOperationException(F("Dashboard.HistoryRowEmpty", historyPath, lineNumber));
                var timestamp = GetVisualizationHistoryTimestamp(point, historyPath, lineNumber);
                if (timestamp < cutoff)
                {
                    continue;
                }

                _sensorHistory.Add(point);
                if (long.TryParse(point.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                {
                    _sensorHistorySequence = Math.Max(_sensorHistorySequence, sequence);
                }
            }
        }

        _sensorHistory.Sort((left, right) => left.UnixMilliseconds.CompareTo(right.UnixMilliseconds));
    }

    private void PruneVisualizationHistoryFiles(DateTimeOffset now)
    {
        var historyDirectory = VisualizationHistoryDirectory;
        if (!Directory.Exists(historyDirectory))
        {
            return;
        }

        var cutoffDate = now.AddDays(-VisualizationHistoryRetentionDays).Date;
        foreach (var historyPath in Directory.EnumerateFiles(historyDirectory, "chart-history-*.jsonl"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(historyPath);
            var dateText = name["chart-history-".Length..];
            if (!DateTime.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
            {
                throw new InvalidOperationException(F("Dashboard.HistoryFilenameInvalid", historyPath));
            }

            if (fileDate.Date < cutoffDate)
            {
                File.Delete(historyPath);
            }
        }
    }

    private void RecordVisualizationHistoryPoint(DateTime snapshotTime)
    {
        var snapshot = BuildVisualizationSnapshot(snapshotTime);
        _latestVisualizationSnapshot = snapshot;
        _latestVisualizationSnapshotTime = snapshotTime;
        var timestamp = new DateTimeOffset(snapshotTime);

        var historyPoint = new SensorDashboardHistoryPoint
        {
            Id = (++_sensorHistorySequence).ToString(CultureInfo.InvariantCulture),
            Time = snapshotTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Timestamp = timestamp.ToString("O", CultureInfo.InvariantCulture),
            UnixMilliseconds = timestamp.ToUnixTimeMilliseconds(),
            Summary = snapshot.Summary,
            Current = snapshot.Current,
            TypeCounts = snapshot.TypeCounts,
            SensorTree = snapshot.SensorTree,
            MaxTemperature = snapshot.Summary.MaxTemperature,
            AverageFanRpm = snapshot.Summary.AverageFanRpm,
            CpuUsage = snapshot.Summary.CpuUsage,
            MemUsage = snapshot.Summary.MemUsage,
            IoUsage = snapshot.Summary.IoUsage,
            SysUsage = snapshot.Summary.SysUsage,
            PowerWatts = snapshot.Summary.PowerWatts,
        };

        _sensorHistory.Add(historyPoint);
        PruneVisualizationHistoryMemory(timestamp);
        QueueVisualizationHistoryPersistence(historyPoint, timestamp);
    }

    private void PruneVisualizationHistoryMemory(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-VisualizationHistoryRetentionDays).ToUnixTimeMilliseconds();
        _sensorHistory.RemoveAll(point => point.UnixMilliseconds > 0 && point.UnixMilliseconds < cutoff);
    }

    private void QueueVisualizationHistoryPersistence(SensorDashboardHistoryPoint historyPoint, DateTimeOffset timestamp)
    {
        _ = Task.Run(() => PersistVisualizationHistoryPoint(historyPoint, timestamp))
            .ContinueWith(
                task =>
                {
                    var exception = task.Exception?.GetBaseException();
                    if (exception is null)
                    {
                        return;
                    }

                    DispatcherQueue.TryEnqueue(() =>
                        ReportVisualizationHistoryFailure("Dashboard.HistoryWriteFailed", exception));
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    private void PersistVisualizationHistoryPoint(SensorDashboardHistoryPoint historyPoint, DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(VisualizationHistoryDirectory);
        var json = JsonSerializer.Serialize(historyPoint, VisualizationJsonOptions);
        File.AppendAllText(BuildVisualizationHistoryPath(timestamp), json + Environment.NewLine, Encoding.UTF8);
        PruneVisualizationHistoryFiles(DateTimeOffset.Now);
    }

    private void ReportVisualizationHistoryFailure(string key, Exception ex)
    {
        var message = F(key, ex.Message);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ReportVisualizationHistoryFailure(key, ex));
            return;
        }

        AddLog(T("Log.Error"), message, "Visualization", "HistoryPersistence");
        ShowStatus(message, InfoBarSeverity.Error);
    }

    private string VisualizationHistoryDirectory => System.IO.Path.Combine(_settingsStore.SettingsDirectory, "chart-history");

    private string BuildVisualizationHistoryPath(DateTimeOffset timestamp)
    {
        return System.IO.Path.Combine(VisualizationHistoryDirectory, $"chart-history-{timestamp.LocalDateTime:yyyyMMdd}.jsonl");
    }

    private static DateTimeOffset GetVisualizationHistoryTimestamp(SensorDashboardHistoryPoint point, string historyPath, int lineNumber)
    {
        if (point.UnixMilliseconds > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(point.UnixMilliseconds).ToLocalTime();
        }

        if (DateTimeOffset.TryParse(point.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            point.UnixMilliseconds = timestamp.ToUnixTimeMilliseconds();
            return timestamp;
        }

        throw new InvalidOperationException(LocalizationService.Format("Dashboard.HistoryTimestampInvalid", historyPath, lineNumber));
    }

    private void ScheduleVisualizationSnapshot()
    {
        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            return;
        }

        if (_visualizationSnapshotUpdateScheduled)
        {
            return;
        }

        _visualizationSnapshotUpdateScheduled = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SendScheduledVisualizationSnapshot))
        {
            _visualizationSnapshotUpdateScheduled = false;
            ShowFailure(new InvalidOperationException("Unable to schedule chart update on the UI dispatcher."));
        }
    }

    private void SendScheduledVisualizationSnapshot()
    {
        _visualizationSnapshotUpdateScheduled = false;
        SendVisualizationSnapshot();
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
            _ = Task.Run(() => JsonSerializer.Serialize(payload, VisualizationJsonOptions))
                .ContinueWith(
                    task =>
                    {
                        DispatcherQueue.TryEnqueue(() => CompleteVisualizationSnapshotSend(task));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void CompleteVisualizationSnapshotSend(Task<string> serializationTask)
    {
        if (serializationTask.IsFaulted)
        {
            ShowFailure(serializationTask.Exception?.GetBaseException() ?? new InvalidOperationException("Chart payload serialization failed."));
            return;
        }

        if (serializationTask.IsCanceled)
        {
            ShowFailure(new OperationCanceledException("Chart payload serialization was canceled."));
            return;
        }

        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            return;
        }

        VisualizationWebView.CoreWebView2.PostWebMessageAsJson(serializationTask.Result);
        VisualizationStateText.Text = _lastPollTime.HasValue
            ? F("Dashboard.VisualizationUpdated", _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            : T("Dashboard.VisualizationReady");
    }

    private object BuildVisualizationPayload()
    {
        var snapshotTime = _lastPollTime ?? DateTime.Now;
        var currentSnapshot = _latestVisualizationSnapshot is not null && _latestVisualizationSnapshotTime == snapshotTime
            ? _latestVisualizationSnapshot
            : BuildVisualizationSnapshot(snapshotTime);

        return new
        {
            MessageType = "sensorDashboard",
            Language = _settings.Language,
            Theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light",
            Labels = BuildVisualizationLabels(),
            Summary = currentSnapshot.Summary,
            Current = currentSnapshot.Current,
            History = _sensorHistory.ToArray(),
            TypeCounts = currentSnapshot.TypeCounts,
            SensorTree = currentSnapshot.SensorTree,
        };
    }

    private VisualizationSnapshot BuildVisualizationSnapshot(DateTime timestamp)
    {
        var sensors = Sensors.ToList();
        return new VisualizationSnapshot
        {
            Summary = BuildVisualizationSummary(sensors, timestamp),
            Current = BuildVisualizationCurrent(sensors),
            TypeCounts = BuildTypeCounts(sensors),
            SensorTree = BuildSensorTree(sensors),
        };
    }

    private VisualizationCurrent BuildVisualizationCurrent(IReadOnlyList<SensorReading> sensors)
    {
        return new VisualizationCurrent
        {
            Temperatures = sensors
                .Where(IsTemperatureSensor)
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeTemperature")))
                .ToList(),
            Fans = sensors
                .Where(IsFanSensor)
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeFan")))
                .ToList(),
            Power = sensors
                .Where(IsPowerSensor)
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypePower")))
                .ToList(),
            Performance = sensors
                .Where(IsPerformanceSensor)
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypePerformance")))
                .ToList(),
            Electrical = sensors
                .Where(sensor => IsPowerWattsSensor(sensor) || IsVoltageSensor(sensor) || IsCurrentSensor(sensor))
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
                .ToList(),
            AllNumeric = sensors
                .Where(sensor => sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
                .ToList(),
            StatusSensors = sensors
                .Where(sensor => !sensor.NumericValue.HasValue)
                .Select(sensor => BuildVisualizationPoint(sensor, GetHardwareTypeName(sensor)))
                .ToList(),
            Health = sensors
                .Where(IsHealthSensor)
                .Select(sensor => BuildVisualizationPoint(sensor, T("Dashboard.TypeStatus")))
                .ToList(),
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
            TempUnit = T("SensorUnit.Celsius"),
            CpuUsage = T("Dashboard.CpuUsage"),
            MemoryUsage = T("Dashboard.MemoryUsage"),
            IoUsage = T("Dashboard.IoUsage"),
            SystemUsage = T("Dashboard.SystemUsage"),
            CpuUsageShort = T("Dashboard.CpuUsageShort"),
            MemoryUsageShort = T("Dashboard.MemoryUsageShort"),
            IoUsageShort = T("Dashboard.IoUsageShort"),
            SystemUsageShort = T("Dashboard.SystemUsageShort"),
            FanShortLabel = T("Dashboard.FanShortLabel"),
            CurrentSnapshot = T("Dashboard.CurrentSnapshot"),
            HistoryRange = T("Dashboard.HistoryRange"),
            RangeLive = T("Dashboard.RangeLive"),
            Range6Hours = T("Dashboard.Range6Hours"),
            Range1Day = T("Dashboard.Range1Day"),
            Range3Days = T("Dashboard.Range3Days"),
            Range7Days = T("Dashboard.Range7Days"),
            RangeCustom = T("Dashboard.RangeCustom"),
            RangeStart = T("Dashboard.RangeStart"),
            RangeEnd = T("Dashboard.RangeEnd"),
            RangeAverage = T("Dashboard.RangeAverage"),
            RangePeak = T("Dashboard.RangePeak"),
            RangeLatest = T("Dashboard.RangeLatest"),
            NoHistoryData = T("Dashboard.NoHistoryData"),
            TempPercentUnit = T("Dashboard.TempPercentUnit"),
            CountUnit = T("Dashboard.CountUnit"),
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
            StatusOk = T("Dashboard.StatusOk"),
            StatusWarning = T("Dashboard.StatusWarning"),
            Value = T("Dashboard.Value"),
            Status = T("Dashboard.Status"),
            Unit = T("Dashboard.Unit"),
        };
    }

    private VisualizationSummary BuildVisualizationSummary(IReadOnlyList<SensorReading> sensors, DateTime timestamp)
    {
        var temperatureValues = sensors
            .Where(IsTemperatureSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var fanValues = sensors
            .Where(IsFanSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var performanceValues = sensors
            .Where(IsPerformanceSensor)
            .Where(sensor => sensor.NumericValue.HasValue)
            .Select(sensor => sensor.NumericValue!.Value)
            .ToList();
        var electricalValues = sensors
            .Where(sensor => IsPowerWattsSensor(sensor) || IsVoltageSensor(sensor) || IsCurrentSensor(sensor))
            .Where(sensor => sensor.NumericValue.HasValue)
            .ToList();

        var okCount = sensors.Count(IsOkStatus);
        var warningCount = sensors.Count - okCount;
        var powerWatts = sensors.FirstOrDefault(IsPowerWattsSensor)?.NumericValue;
        var voltageValues = sensors.Where(IsVoltageSensor).Where(sensor => sensor.NumericValue.HasValue).Select(sensor => sensor.NumericValue!.Value).ToList();
        var currentValues = sensors.Where(IsCurrentSensor).Where(sensor => sensor.NumericValue.HasValue).Select(sensor => sensor.NumericValue!.Value).ToList();
        return new VisualizationSummary
        {
            SensorCount = sensors.Count,
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
            CpuUsage = FindNumericSensorValue(sensors, "CPU Usage"),
            MemUsage = FindNumericSensorValue(sensors, "MEM Usage"),
            IoUsage = FindNumericSensorValue(sensors, "IO Usage"),
            SysUsage = FindNumericSensorValue(sensors, "SYS Usage"),
            OkCount = okCount,
            WarningCount = warningCount,
            LastUpdated = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private VisualizationPoint BuildVisualizationPoint(SensorReading sensor, string type)
    {
        return new VisualizationPoint
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

    private object[] BuildTypeCounts(IReadOnlyList<SensorReading> sensors)
    {
        return sensors
            .GroupBy(GetHardwareTypeName)
            .OrderByDescending(group => group.Count())
            .Select(group => new { Name = group.Key, Value = group.Count() })
            .Cast<object>()
            .ToArray();
    }

    private object[] BuildSensorTree(IReadOnlyList<SensorReading> sensors)
    {
        return sensors
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

    private static double? FindNumericSensorValue(IEnumerable<SensorReading> sensors, string key)
    {
        return sensors.FirstOrDefault(sensor => sensor.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.NumericValue;
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
        _localizedSensorsDirty = false;
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

    private void MarkLocalizedSensorRowsDirty()
    {
        _localizedSensorsDirty = true;
        RefreshLocalizedSensorRowsIfVisible();
    }

    private void RefreshLocalizedSensorRowsIfVisible()
    {
        if (!_localizedSensorsDirty)
        {
            return;
        }

        if (SensorsView.Visibility == Visibility.Visible)
        {
            RefreshLocalizedSensorRows();
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

    private static string TranslateSensorValue(string value)
    {
        return LocalizationService.TranslateSensorValue(value);
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
        var normalizedDisplayName = NormalizeSensorToken(key);
        if (LocalizationService.SensorDisplayNameTranslationKeys.ContainsKey(normalizedDisplayName))
        {
            return LocalizationService.TranslateSensorDisplayName(key);
        }

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

        if (ShouldUseGenericHardwareEventTitle(sensor, key))
        {
            var recordId = string.IsNullOrWhiteSpace(sensor.SensorId)
                ? "SDR"
                : SensorSubtitleFormatter.FormatRecordId(sensor.SensorId);
            return F("SensorDisplay.HardwareEvent", recordId);
        }

        return key;
    }

    private static bool ShouldUseGenericHardwareEventTitle(SensorReading sensor, string displayName)
    {
        return ContainsAsciiLetter(displayName) &&
               (!sensor.NumericValue.HasValue || string.IsNullOrWhiteSpace(sensor.Unit));
    }

    private static bool ContainsAsciiLetter(string value)
    {
        return value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    }

    private static void ReplaceTiles(
        ObservableCollection<DashboardTileViewModel> target,
        System.Collections.Generic.IEnumerable<DashboardTileViewModel> tiles)
    {
        var nextTiles = tiles.ToList();
        for (var nextIndex = 0; nextIndex < nextTiles.Count; nextIndex++)
        {
            var nextTile = nextTiles[nextIndex];
            var existingIndex = FindTileIndex(target, nextTile.Id);
            if (existingIndex < 0)
            {
                target.Insert(nextIndex, nextTile);
                continue;
            }

            var existingTile = target[existingIndex];
            existingTile.UpdateFrom(nextTile);
            if (existingIndex != nextIndex)
            {
                target.Move(existingIndex, nextIndex);
            }
        }

        for (var index = target.Count - 1; index >= nextTiles.Count; index--)
        {
            target.RemoveAt(index);
        }
    }

    private static int FindTileIndex(
        ObservableCollection<DashboardTileViewModel> target,
        string id)
    {
        for (var index = 0; index < target.Count; index++)
        {
            if (string.Equals(target[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
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
        return SensorSubtitleFormatter.Format(sensor);
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

    private void MarkActivePreset(string? presetId, bool persistRunningState = false)
    {
        _activePresetId = presetId;
        RefreshPresetRows();
        if (persistRunningState)
        {
            PersistRunningPresetState(presetId);
        }
    }

    private void PersistRunningPresetState(string? presetId)
    {
        _settings.LastRunningPresetId = presetId ?? string.Empty;
        _settings.LastSmartAutoPolicyRunning = false;
        _settingsStore.Save(_settings);
    }

    private void PersistSmartAutoRunningState()
    {
        _settings.LastRunningPresetId = string.Empty;
        _settings.LastSmartAutoPolicyRunning = true;
        _settingsStore.Save(_settings);
    }

    private void ClearPersistedRunningState()
    {
        _settings.LastRunningPresetId = string.Empty;
        _settings.LastSmartAutoPolicyRunning = false;
        _settingsStore.Save(_settings);
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

    private void SortNewCurvePointsInPlace()
    {
        SortCurvePointsInPlace(NewCurvePoints, point => point.TemperatureCelsius);
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

    private void UpdateTemperatureCurvePointFromCanvas(FanCurvePoint point, Point position)
    {
        point.TemperatureCelsius = Math.Round(
            FromCanvasX(position.X, NewCurveCanvas.ActualWidth, TemperatureCurveCanvasMinCelsius, TemperatureCurveCanvasMaxCelsius),
            1);
        point.FanPercent = Math.Round(FromCanvasY(position.Y, NewCurveCanvas.ActualHeight), 0);
    }

    private void UpdateTemperatureCurvePointFromCanvasInput(FanCurvePoint point, Point position)
    {
        _syncingTemperatureCurveInputsFromCanvas = true;
        try
        {
            UpdateTemperatureCurvePointFromCanvas(point, position);
        }
        finally
        {
            _syncingTemperatureCurveInputsFromCanvas = false;
        }
    }

    private FanCurvePoint? FindNearestTemperatureCurvePoint(Point position)
    {
        return FindNearestCurvePoint(
            NewCurvePoints,
            position,
            point => ToCanvasX(point.TemperatureCelsius, NewCurveCanvas.ActualWidth, TemperatureCurveCanvasMinCelsius, TemperatureCurveCanvasMaxCelsius),
            point => ToCanvasY(point.FanPercent, NewCurveCanvas.ActualHeight));
    }

    private void FinishTemperatureCurveDrag()
    {
        _draggingTemperatureCurvePoint = null;
        NewCurveCanvas.ReleasePointerCaptures();
        SortNewCurvePointsInPlace();
        UpdateNewCurvePreview();
    }

    private List<FanCurvePoint> BuildTemperatureCurveCanvasPreviewPoints()
    {
        return NewCurvePoints
            .Select(point => point.Clone())
            .OrderBy(point => point.TemperatureCelsius)
            .ToList();
    }

    private void DrawNewCurveCanvas(List<FanCurvePoint>? validPoints = null, bool useSmoothPreview = true)
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

        double ToX(double temperature)
        {
            return ToCanvasX(temperature, width, TemperatureCurveCanvasMinCelsius, TemperatureCurveCanvasMaxCelsius);
        }

        double ToY(double percent)
        {
            return ToCanvasY(percent, height);
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
            DrawTemperatureCurveHoverOverlay(width, height);
            return;
        }

        if (validPoints is not null && validPoints.Count >= 2)
        {
            var linePoints = new PointCollection();
            if (useSmoothPreview && NewCurveSmoothSwitch.IsOn)
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

        DrawTemperatureCurveHoverOverlay(width, height);
    }

    private void ResetNewPowerCurveEditor()
    {
        _editingPowerCurvePresetId = null;
        NewPowerCurvePresetNameBox.Text = string.Empty;
        NewPowerCurveSmoothSwitch.IsOn = false;
        ReplaceNewPowerCurvePoints(FanPreset.ParsePowerCurvePoints(DefaultPowerCurvePointsText));
        UpdateNewPowerCurveEditorModeText();
        AddPowerCurvePresetButtonText.Text = T("Control.AddCurvePreset");
        UpdateNewPowerCurvePreview();
    }

    private void UpdateNewPowerCurvePreview()
    {
        if (NewPowerCurvePreviewText is null)
        {
            return;
        }

        List<FanCurvePoint>? validPoints = null;
        try
        {
            validPoints = ReadNewPowerCurvePoints();
            NewPowerCurvePreviewText.Text = FanPreset.BuildPowerCurveChartText(validPoints, NewPowerCurveSmoothSwitch.IsOn);
        }
        catch (Exception ex)
        {
            NewPowerCurvePreviewText.Text = F("Status.CurvePreviewInvalid", ex.Message);
        }

        DrawNewPowerCurveCanvas(validPoints);
    }

    private List<FanCurvePoint> ReadNewPowerCurvePoints()
    {
        var preset = new FanPreset
        {
            Kind = FanPreset.PowerCurveKind,
            Name = NewPowerCurvePresetNameBox.Text.Trim(),
            CurvePoints = NewPowerCurvePoints.Select(point => point.Clone()).ToList(),
            SmoothCurve = NewPowerCurveSmoothSwitch.IsOn,
        };
        preset.ValidateCurvePoints();
        return preset.CurvePoints.Select(point => point.Clone()).ToList();
    }

    private void NormalizeNewPowerCurveEditorPoints()
    {
        ReplaceNewPowerCurvePoints(NewPowerCurvePoints.OrderBy(point => point.PowerWatts).Select(point => point.Clone()));
        UpdateNewPowerCurvePreview();
    }

    private void SortNewPowerCurvePointsInPlace()
    {
        SortCurvePointsInPlace(NewPowerCurvePoints, point => point.PowerWatts);
    }

    private void ReplaceNewPowerCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        NewPowerCurvePoints.Clear();
        foreach (var point in points.OrderBy(point => point.PowerWatts))
        {
            NewPowerCurvePoints.Add(point.Clone());
        }
    }

    private void UpdateNewPowerCurveEditorModeText()
    {
        if (NewPowerCurveEditorModeText is null || AddPowerCurvePresetButtonText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_editingPowerCurvePresetId))
        {
            NewPowerCurveEditorModeText.Text = T("Control.NewCurveEditor");
            AddPowerCurvePresetButtonText.Text = T("Control.AddCurvePreset");
            return;
        }

        var preset = Presets.FirstOrDefault(item => item.Id.Equals(_editingPowerCurvePresetId, StringComparison.OrdinalIgnoreCase));
        var presetName = preset?.DisplayName ?? NewPowerCurvePresetNameBox.Text.Trim();
        NewPowerCurveEditorModeText.Text = F("Control.EditingCurvePreset", presetName);
        AddPowerCurvePresetButtonText.Text = T("Control.SaveCurvePreset");
    }

    private void UpdatePowerCurvePointFromCanvas(FanCurvePoint point, Point position)
    {
        point.PowerWatts = Math.Round(
            FromCanvasX(position.X, NewPowerCurveCanvas.ActualWidth, PowerCurveCanvasMinWatts, PowerCurveCanvasMaxWatts),
            0);
        point.FanPercent = Math.Round(FromCanvasY(position.Y, NewPowerCurveCanvas.ActualHeight), 0);
    }

    private void UpdatePowerCurvePointFromCanvasInput(FanCurvePoint point, Point position)
    {
        _syncingPowerCurveInputsFromCanvas = true;
        try
        {
            UpdatePowerCurvePointFromCanvas(point, position);
        }
        finally
        {
            _syncingPowerCurveInputsFromCanvas = false;
        }
    }

    private FanCurvePoint? FindNearestPowerCurvePoint(Point position)
    {
        return FindNearestCurvePoint(
            NewPowerCurvePoints,
            position,
            point => ToCanvasX(point.PowerWatts, NewPowerCurveCanvas.ActualWidth, PowerCurveCanvasMinWatts, PowerCurveCanvasMaxWatts),
            point => ToCanvasY(point.FanPercent, NewPowerCurveCanvas.ActualHeight));
    }

    private void FinishPowerCurveDrag()
    {
        _draggingPowerCurvePoint = null;
        NewPowerCurveCanvas.ReleasePointerCaptures();
        SortNewPowerCurvePointsInPlace();
        UpdateNewPowerCurvePreview();
    }

    private List<FanCurvePoint> BuildPowerCurveCanvasPreviewPoints()
    {
        return NewPowerCurvePoints
            .Select(point => point.Clone())
            .OrderBy(point => point.PowerWatts)
            .ToList();
    }

    private static void SortCurvePointsInPlace(ObservableCollection<FanCurvePoint> points, Func<FanCurvePoint, double> keySelector)
    {
        var ordered = points.OrderBy(keySelector).ToList();
        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = points.IndexOf(ordered[targetIndex]);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                points.Move(currentIndex, targetIndex);
            }
        }
    }

    private static FanCurvePoint? FindNearestCurvePoint(
        IEnumerable<FanCurvePoint> points,
        Point position,
        Func<FanCurvePoint, double> toX,
        Func<FanCurvePoint, double> toY)
    {
        FanCurvePoint? nearest = null;
        var nearestDistanceSquared = CurvePointHitRadius * CurvePointHitRadius;
        foreach (var point in points)
        {
            var dx = position.X - toX(point);
            var dy = position.Y - toY(point);
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearest = point;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    private static double ToCanvasX(double value, double width, double minValue, double maxValue)
    {
        var drawableWidth = Math.Max(1, width - (CurveCanvasPadding * 2));
        var ratio = Math.Clamp((value - minValue) / (maxValue - minValue), 0, 1);
        return CurveCanvasPadding + (drawableWidth * ratio);
    }

    private static double ToCanvasY(double percent, double height)
    {
        var drawableHeight = Math.Max(1, height - (CurveCanvasPadding * 2));
        var ratio = Math.Clamp(percent / 100, 0, 1);
        return CurveCanvasPadding + (drawableHeight * (1 - ratio));
    }

    private static double FromCanvasX(double x, double width, double minValue, double maxValue)
    {
        var drawableWidth = Math.Max(1, width - (CurveCanvasPadding * 2));
        var ratio = Math.Clamp((x - CurveCanvasPadding) / drawableWidth, 0, 1);
        return minValue + ((maxValue - minValue) * ratio);
    }

    private static double FromCanvasY(double y, double height)
    {
        var drawableHeight = Math.Max(1, height - (CurveCanvasPadding * 2));
        var ratio = Math.Clamp((y - CurveCanvasPadding) / drawableHeight, 0, 1);
        return Math.Clamp(100 - (ratio * 100), 0, 100);
    }

    private void DrawNewPowerCurveCanvas(List<FanCurvePoint>? validPoints = null, bool useSmoothPreview = true)
    {
        if (NewPowerCurveCanvas is null)
        {
            return;
        }

        var width = NewPowerCurveCanvas.ActualWidth;
        var height = NewPowerCurveCanvas.ActualHeight;
        NewPowerCurveCanvas.Children.Clear();
        if (width <= 8 || height <= 8)
        {
            return;
        }

        double ToX(double powerWatts)
        {
            return ToCanvasX(powerWatts, width, PowerCurveCanvasMinWatts, PowerCurveCanvasMaxWatts);
        }

        double ToY(double percent)
        {
            return ToCanvasY(percent, height);
        }

        var gridBrush = ToBrush("#223B82F6");
        var axisBrush = ToBrush("#663B82F6");
        for (var index = 0; index <= 4; index++)
        {
            var x = 8 + ((width - 16) * index / 4);
            var y = 8 + ((height - 16) * index / 4);
            NewPowerCurveCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 8,
                Y2 = height - 8,
                Stroke = gridBrush,
                StrokeThickness = 1,
            });
            NewPowerCurveCanvas.Children.Add(new Line
            {
                X1 = 8,
                X2 = width - 8,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
            });
        }

        NewPowerCurveCanvas.Children.Add(new Line
        {
            X1 = 8,
            X2 = width - 8,
            Y1 = height - 8,
            Y2 = height - 8,
            Stroke = axisBrush,
            StrokeThickness = 1.4,
        });
        NewPowerCurveCanvas.Children.Add(new Line
        {
            X1 = 8,
            X2 = 8,
            Y1 = 8,
            Y2 = height - 8,
            Stroke = axisBrush,
            StrokeThickness = 1.4,
        });

        var points = NewPowerCurvePoints
            .Select(point => point.Clone())
            .OrderBy(point => point.PowerWatts)
            .ToList();
        if (points.Count == 0)
        {
            DrawPowerCurveHoverOverlay(width, height);
            return;
        }

        if (validPoints is not null && validPoints.Count >= 2)
        {
            var linePoints = new PointCollection();
            if (useSmoothPreview && NewPowerCurveSmoothSwitch.IsOn)
            {
                var previewPreset = new FanPreset
                {
                    Kind = FanPreset.PowerCurveKind,
                    CurvePoints = validPoints.Select(point => point.Clone()).ToList(),
                    SmoothCurve = true,
                };

                var first = validPoints[0].PowerWatts;
                var last = validPoints[^1].PowerWatts;
                const int samples = 48;
                for (var index = 0; index < samples; index++)
                {
                    var ratio = index / (double)(samples - 1);
                    var powerWatts = first + ((last - first) * ratio);
                    linePoints.Add(new Point(ToX(powerWatts), ToY(previewPreset.CalculateFanPercentForPower(powerWatts))));
                }
            }
            else
            {
                foreach (var point in validPoints)
                {
                    linePoints.Add(new Point(ToX(point.PowerWatts), ToY(point.FanPercent)));
                }
            }

            NewPowerCurveCanvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = ToBrush("#FF2563EB"),
                StrokeThickness = 3,
            });
        }

        foreach (var point in points)
        {
            var marker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = ToBrush("#FF3B82F6"),
                Stroke = ToBrush("#FFFFFFFF"),
                StrokeThickness = 2,
            };
            Canvas.SetLeft(marker, ToX(point.PowerWatts) - 6);
            Canvas.SetTop(marker, ToY(point.FanPercent) - 6);
            NewPowerCurveCanvas.Children.Add(marker);
        }

        DrawPowerCurveHoverOverlay(width, height);
    }

    private void DrawTemperatureCurveHoverOverlay(double width, double height)
    {
        if (_temperatureCurveHoverPosition is not { } position)
        {
            return;
        }

        var temperature = FromCanvasX(position.X, width, TemperatureCurveCanvasMinCelsius, TemperatureCurveCanvasMaxCelsius);
        var percent = FromCanvasY(position.Y, height);
        var fanSpeedLabel = GetCurveHoverFanSpeedLabel();
        DrawCurveHoverOverlay(
            NewCurveCanvas,
            position,
            width,
            height,
            $"{T("Dashboard.TypeTemperature")} {temperature:0.0} {T("SensorUnit.Celsius")}\n{fanSpeedLabel} {percent:0}%",
            "#FF0F766E");
    }

    private void DrawPowerCurveHoverOverlay(double width, double height)
    {
        if (_powerCurveHoverPosition is not { } position)
        {
            return;
        }

        var powerWatts = FromCanvasX(position.X, width, PowerCurveCanvasMinWatts, PowerCurveCanvasMaxWatts);
        var percent = FromCanvasY(position.Y, height);
        var fanSpeedLabel = GetCurveHoverFanSpeedLabel();
        DrawCurveHoverOverlay(
            NewPowerCurveCanvas,
            position,
            width,
            height,
            $"{T("SensorDisplay.PowerConsumption")} {powerWatts:0} {T("SensorUnit.Watts")}\n{fanSpeedLabel} {percent:0}%",
            "#FF2563EB");
    }

    private static string GetCurveHoverFanSpeedLabel()
    {
        return string.Equals(LocalizationService.CurrentLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? CurveHoverFanSpeedChineseLabel
            : LocalizationService.T("Preset.Percent");
    }

    private static void DrawCurveHoverOverlay(
        Canvas canvas,
        Point position,
        double width,
        double height,
        string label,
        string accentHex)
    {
        var x = Math.Clamp(position.X, CurveCanvasPadding, Math.Max(CurveCanvasPadding, width - CurveCanvasPadding));
        var y = Math.Clamp(position.Y, CurveCanvasPadding, Math.Max(CurveCanvasPadding, height - CurveCanvasPadding));
        var guideBrush = ToBrush(accentHex);
        var dash = new DoubleCollection { 3, 3 };
        canvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = CurveCanvasPadding,
            Y2 = height - CurveCanvasPadding,
            Stroke = guideBrush,
            StrokeDashArray = dash,
            StrokeThickness = 1.2,
        });
        canvas.Children.Add(new Line
        {
            X1 = CurveCanvasPadding,
            X2 = width - CurveCanvasPadding,
            Y1 = y,
            Y2 = y,
            Stroke = guideBrush,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            StrokeThickness = 1.2,
        });
        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = guideBrush,
            Stroke = ToBrush("#FFFFFFFF"),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(marker, x - 4);
        Canvas.SetTop(marker, y - 4);
        canvas.Children.Add(marker);

        var labelBorder = new Border
        {
            MaxWidth = 160,
            Padding = new Thickness(8, 5, 8, 5),
            Background = ToBrush("#EE0F172A"),
            BorderBrush = guideBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Foreground = ToBrush("#FFFFFFFF"),
                FontSize = 12,
                Text = label,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Canvas.SetLeft(labelBorder, Math.Clamp(x + 12, CurveCanvasPadding, Math.Max(CurveCanvasPadding, width - 160)));
        Canvas.SetTop(labelBorder, Math.Clamp(y - 58, CurveCanvasPadding, Math.Max(CurveCanvasPadding, height - 58)));
        canvas.Children.Add(labelBorder);
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
            throw new InvalidOperationException(T("Validation.PresetKindRequired"));
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
        else if (clone.Kind.Equals(FanPreset.PowerCurveKind, StringComparison.OrdinalIgnoreCase))
        {
            clone.Kind = FanPreset.PowerCurveKind;
            clone.Percent = 0;
            clone.ApplyCurvePointsText();
            clone.ValidateCurvePoints();
        }
        else
        {
            throw new InvalidOperationException(F("Validation.UnsupportedPresetKind", clone.Kind));
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
            MessageMatchesLocalizedTemplate(message, "Hero.RequestRunning"))
        {
            return T("Hero.RequestShortRunning");
        }

        if (IsPollingSkipMessage(message))
        {
            return T("Hero.RequestShortSkipped");
        }

        if (message.Contains(T("Status.AutoStarted"), StringComparison.OrdinalIgnoreCase) ||
            MessageMatchesLocalizedTemplate(message, "Status.CurvePresetStarted"))
        {
            return T("Hero.RequestShortRunning");
        }

        if (message.Contains(T("Status.DellAutoRestored"), StringComparison.OrdinalIgnoreCase) ||
            MessageMatchesLocalizedTemplate(message, "Status.EmergencyAuto"))
        {
            return T("Hero.RequestShortAuto");
        }

        if (MessageMatchesLocalizedTemplate(message, "Status.SmartFanApplied") ||
            MessageMatchesLocalizedTemplate(message, "Status.CurveFanApplied"))
        {
            return T("Hero.RequestShortApplied");
        }

        return T("Hero.RequestShortUpdated");
    }

    private bool IsPollingSkipMessage(string message)
    {
        return PollingSkipStatusKeys.Any(key => MessageMatchesLocalizedTemplate(message, key));
    }

    private bool MessageMatchesLocalizedTemplate(string message, string resourceKey)
    {
        var template = T(resourceKey);
        var marker = template.Split('{')[0].Trim();
        return !string.IsNullOrWhiteSpace(marker) &&
               message.Contains(marker, StringComparison.OrdinalIgnoreCase);
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
        Logs.Insert(0, new LogEntry
        {
            Level = level,
            SemanticLevel = GetDisplayLogSemanticLevel(level),
            Message = message,
        });
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
        return GetDisplayLogSemanticLevel(level) switch
        {
            "Error" => "Error",
            "Warning" => "Warning",
            _ => "Info",
        };
    }

    private static string GetDisplayLogSemanticLevel(string level)
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

        if (level.Equals(T("Log.Ok"), StringComparison.OrdinalIgnoreCase))
        {
            return "Success";
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
        return LanguageComboBox.SelectedItem is LanguageOption option
            ? option.Code
            : LocalizationService.DefaultLanguage;
    }

    private static string GetSelectedLanguageDisplayName(string language)
    {
        return LocalizationService.SupportedLanguages.First(option => option.Code == language).DisplayName;
    }

    private void SelectLanguage(string language)
    {
        LanguageComboBox.SelectedItem = LocalizationService.SupportedLanguages.First(option => option.Code == language);
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
        NewPowerCurvePresetNameBox.PlaceholderText = T("Preset.PowerCurveNewNamePlaceholder");
        CurveMinAxisText.Text = $"{TemperatureCurveCanvasMinCelsius:0} {T("SensorUnit.Celsius")}";
        CurveMaxAxisText.Text = $"{TemperatureCurveCanvasMaxCelsius:0} {T("SensorUnit.Celsius")}";
        PowerCurveSectionTitleText.Text = T("Control.PowerCurvePresetPoints");
        PowerCurveHelpText.Text = T("Control.PowerCurvePresetHelp");
        PowerCurveMinAxisText.Text = $"{PowerCurveCanvasMinWatts:0} {T("SensorUnit.Watts")}";
        PowerCurveMaxAxisText.Text = $"{PowerCurveCanvasMaxWatts:0} {T("SensorUnit.Watts")}";
        UpdateNewCurveEditorModeText();
        UpdateNewCurvePreview();
        UpdateNewPowerCurveEditorModeText();
        UpdateNewPowerCurvePreview();
        VisualizationStateText.Text = _visualizationReady
            ? T("Dashboard.VisualizationReady")
            : T("Dashboard.VisualizationLoading");
        FanSummaryText.Text = F("Overview.FansCount", _settings.FanCount);
        if (Sensors.Count == 0)
        {
            CpuTemperatureText.Text = T("Hero.LiveWaiting");
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
        UpdateHeroThermalModeTexts();
        SetModeSummary(_modeSummaryKey, _modeSummaryArgs);
        SetAutoPolicySummary(_autoPolicyRunning);
        UpdateHeroRequestStatusTexts();
        UpdatePollingStatusTexts();
        UpdateIndividualFanWarning();
        MarkLocalizedSensorRowsDirty();
        RefreshPresetRows();
        ScheduleVisualizationSnapshot();

        foreach (var fanChannel in FanChannels)
        {
            fanChannel.RefreshLocalization();
        }
    }

    private void SetModeSummary(string key, params object[] args)
    {
        _modeSummaryKey = key;
        _modeSummaryArgs = args;
        var modeText = FormatLocalizedModeSummary(key, args);
        ModeSummaryText.Text = modeText;
        HeroModeStateText.Text = modeText;
        UpdateHeroThermalModeTexts();
        ControlCurrentModeText.Text = F("Control.CurrentMode", modeText);
    }

    private string FormatLocalizedModeSummary(string key, object[] args)
    {
        return args.Length == 0 ? T(key) : F(key, args);
    }

    private void UpdateHeroThermalModeTexts()
    {
        if (HeroThermalModeLabelText is null || HeroThermalModeValueText is null)
        {
            return;
        }

        HeroThermalModeLabelText.Text = T("Hero.ModeStatus");
        HeroThermalModeValueText.Text = FormatHeroThermalModeText(_modeSummaryKey, _modeSummaryArgs);
    }

    private string FormatHeroThermalModeText(string key, IReadOnlyList<object> args)
    {
        var isSimplifiedChinese = string.Equals(LocalizationService.CurrentLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(key, "Mode.DellAuto", StringComparison.Ordinal))
        {
            return isSimplifiedChinese ? HeroThermalDellAutoChinese : T("Control.DellAuto");
        }

        if (string.Equals(key, "Mode.SmartAuto", StringComparison.Ordinal) ||
            string.Equals(key, "Mode.SmartPercent", StringComparison.Ordinal))
        {
            var modeName = isSimplifiedChinese ? HeroThermalSmartPolicyChinese : T("Control.SmartAutoPolicy");
            return args.Count > 0 ? $"{modeName} ({args[0]}%)" : modeName;
        }

        if (string.Equals(key, "Mode.CurveAuto", StringComparison.Ordinal) ||
            string.Equals(key, "Mode.CurvePercent", StringComparison.Ordinal))
        {
            return FormatHeroCurveThermalModeText(args, isSimplifiedChinese);
        }

        return FormatLocalizedModeSummary(key, args.ToArray());
    }

    private string FormatHeroCurveThermalModeText(IReadOnlyList<object> args, bool isSimplifiedChinese)
    {
        var presetName = args.Count > 0
            ? Convert.ToString(args[0], CultureInfo.CurrentCulture) ?? string.Empty
            : _activeCurvePreset?.DisplayName ?? string.Empty;
        var modeName = _activeCurvePreset?.IsPowerCurvePreset == true
            ? isSimplifiedChinese ? HeroThermalPowerCurveAutoChinese : $"{T("Preset.PowerCurveBadge")} {T("Hero.RequestShortAuto")}"
            : isSimplifiedChinese ? HeroThermalTemperatureCurveAutoChinese : $"{T("Preset.CurveBadge")} {T("Hero.RequestShortAuto")}";

        if (args.Count > 1)
        {
            return $"{modeName}: {presetName} ({args[1]}%)";
        }

        return string.IsNullOrWhiteSpace(presetName) ? modeName : $"{modeName}: {presetName}";
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
            UpdatePollingActionButtonLabels();
            return;
        }

        if (_sensorPollingTimer.IsEnabled)
        {
            var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
            ConnectionStateText.Text = _lastPollTime.HasValue
                ? F("State.ConnectedPollingTime", intervalSeconds, _lastPollTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                : F("State.ConnectedPolling", intervalSeconds);
            UpdatePollingActionButtonLabels();
            return;
        }

        ConnectionStateText.Text = _hasDisconnected ? T("State.Disconnected") : T("State.NotConnected");
        UpdatePollingActionButtonLabels();
    }

    private void UpdatePollingActionButtonLabels()
    {
        var isPolling = _sensorPollingTimer.IsEnabled;
        var label = isPolling
            ? T("Action.CancelPolling")
            : T("Action.StartPolling");
        foreach (var button in new[] { QuickPollingButton, SettingsPollingButton })
        {
            if (button is null)
            {
                continue;
            }

            button.Label = label;
            button.IsEnabled = !_isConnecting;
        }
    }

    private void SetConnectingState()
    {
        _isConnecting = true;
        _hasDisconnected = false;
        ConnectionStateText.Text = T("State.Connecting");
        UpdatePollingActionButtonLabels();
        ShowStatus(T("Status.Connecting"), InfoBarSeverity.Informational);
    }

    private void CheckSensorPollingLatency(TimeSpan elapsed)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SensorRefreshSeconds));
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

    private void LogPollingSkip(PollingSkipKind kind, string message)
    {
        if (!_pollingSkipLogGate.ShouldLog(kind))
        {
            return;
        }

        if (PollingSkipLogGate.OpenTopStatusForSkippedTick)
        {
            ShowStatus(message, InfoBarSeverity.Warning);
        }

        AddLog(T("Log.Warn"), message);
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

    private sealed class VisualizationSnapshot
    {
        public VisualizationSummary Summary { get; init; } = new();

        public VisualizationCurrent Current { get; init; } = new();

        public object[] TypeCounts { get; init; } = [];

        public object[] SensorTree { get; init; } = [];
    }

    private sealed class VisualizationSummary
    {
        public int SensorCount { get; init; }

        public int TemperatureCount { get; init; }

        public int FanCount { get; init; }

        public int PerformanceCount { get; init; }

        public int ElectricalCount { get; init; }

        public double? MaxTemperature { get; init; }

        public double? AverageFanRpm { get; init; }

        public double? MinFanRpm { get; init; }

        public double? MaxFanRpm { get; init; }

        public double? MaxPerformance { get; init; }

        public double? AveragePerformance { get; init; }

        public double? PowerWatts { get; init; }

        public int VoltageCount { get; init; }

        public double? AverageVoltage { get; init; }

        public int CurrentCount { get; init; }

        public double? TotalCurrent { get; init; }

        public double? CpuUsage { get; init; }

        public double? MemUsage { get; init; }

        public double? IoUsage { get; init; }

        public double? SysUsage { get; init; }

        public int OkCount { get; init; }

        public int WarningCount { get; init; }

        public string LastUpdated { get; init; } = string.Empty;
    }

    private sealed class VisualizationCurrent
    {
        public List<VisualizationPoint> Temperatures { get; init; } = [];

        public List<VisualizationPoint> Fans { get; init; } = [];

        public List<VisualizationPoint> Power { get; init; } = [];

        public List<VisualizationPoint> Performance { get; init; } = [];

        public List<VisualizationPoint> Electrical { get; init; } = [];

        public List<VisualizationPoint> AllNumeric { get; init; } = [];

        public List<VisualizationPoint> StatusSensors { get; init; } = [];

        public List<VisualizationPoint> Health { get; init; } = [];
    }

    private sealed class VisualizationPoint
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public double? Value { get; init; }

        public string Unit { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Subtitle { get; init; } = string.Empty;
    }

    private sealed class SensorDashboardHistoryPoint
    {
        public string Id { get; set; } = string.Empty;

        public string Time { get; set; } = string.Empty;

        public string Timestamp { get; set; } = string.Empty;

        public long UnixMilliseconds { get; set; }

        public VisualizationSummary? Summary { get; set; }

        public VisualizationCurrent? Current { get; set; }

        public object[] TypeCounts { get; set; } = [];

        public object[] SensorTree { get; set; } = [];

        public double? MaxTemperature { get; set; }

        public double? AverageFanRpm { get; set; }

        public double? CpuUsage { get; set; }

        public double? MemUsage { get; set; }

        public double? IoUsage { get; set; }

        public double? SysUsage { get; set; }

        public double? PowerWatts { get; set; }
    }
}
