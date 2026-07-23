using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace DellR730xdFanControlCenter;

public sealed partial class MainPage : Page
{
    private const int VisualizationHistoryRetentionDays = 7;
    private const int MaxVisualizationPayloadHistoryPoints = 720;
    private const string DefaultCurvePointsText = "45 = 18%" + "\r\n" + "68 = 28%" + "\r\n" + "78 = 42%";
    private const string DefaultPowerCurvePointsText = "280W = 18%" + "\r\n" + "500W = 28%" + "\r\n" + "750W = 42%";
    private const double TemperatureCurveCanvasMinCelsius = 30;
    private const double TemperatureCurveCanvasMaxCelsius = 95;
    private const double PowerCurveCanvasMinWatts = 0;
    private const double PowerCurveCanvasMaxWatts = 1200;
    private const double CurveCanvasPadding = 34;
    private const double CurvePointHitRadius = 18;
    private const int CurvePreviewSampleCount = 120;
    private const double VisualizationMouseWheelDeltaY = 100d;
    private const string HeroThermalTemperatureCurveAutoChinese = "温度曲线自动";
    private const string HeroThermalPowerCurveAutoChinese = "功耗曲线自动";
    private const string HeroThermalSmartPolicyChinese = "软件恒温策略";
    private const string HeroThermalDellAutoChinese = "Dell 自动温控";
    private const string CurveHoverFanSpeedChineseLabel = "风扇速度";
    private const string SmartAutoPolicyTargetKey = "__smart-auto__";
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
        "Status.SmartFanUnchanged",
        "Status.CurveFanUnchanged",
        "Status.PowerCurveFanUnchanged",
    ];

    private readonly SettingsStore _settingsStore = new();
    private readonly AppLogService _appLog = new();
    private readonly IpmiCommandService _ipmi = new();
    private readonly DispatcherTimer _autoPolicyTimer = new();
    private readonly DispatcherTimer _sensorPollingTimer = new();
    private readonly DispatcherTimer _sensorPollingRetryTimer = new();
    private readonly SemaphoreSlim _ipmiOperationLock = new(1, 1);
    private readonly PollingSkipLogGate _pollingSkipLogGate = new();
    private readonly DashboardSnapshotFreshness _dashboardSnapshotFreshness = new();
    private readonly List<SensorDashboardHistoryPoint> _sensorHistory = [];
    private readonly List<LogEntry> _volatileLogEntries = [];
    private long _sensorHistorySequence;
    private AppSettings _settings = new();
    private bool _syncingAllFanControls;
    private bool _autoPolicyTickRunning;
    private bool _sensorPollingTickRunning;
    private bool _sensorPollingRetryRunning;
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
    private readonly VisualizationWheelScrollState _visualizationWheelScrollState = new();
    private double _pendingVisualizationWheelDeltaY;
    private bool _pendingVisualizationWheelShouldAnimate;
    private bool _visualizationWheelScrollScheduled;
    private bool _overviewMetricsDirty = true;
    private bool _visualizationSnapshotDirty = true;
    private bool _volatileLogsDirty;
    private VisualizationSnapshot? _latestVisualizationSnapshot;
    private DateTime? _latestVisualizationSnapshotTime;
    private string? _activePresetId;
    private FanPreset? _activeCurvePreset;
    private int? _lastAutoPolicyFanPercent;
    private string? _lastAutoPolicyTargetKey;
    private int? _lastFailedAutoPolicyFanPercent;
    private string? _lastFailedAutoPolicyTargetKey;
    private bool _forceNextAutoPolicyFanCommand;
    private long _fanControlIntentVersion;
    private long _activeAutoPolicyIntentVersion;
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

    private sealed class FanControlIntentSupersededException : OperationCanceledException
    {
        public FanControlIntentSupersededException(string message)
            : base(message)
        {
        }
    }

    private sealed class AutoPolicyFanTargetRejectedException : InvalidOperationException
    {
        public AutoPolicyFanTargetRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
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

        _appLog.WriteFailed += OnAppLogWriteFailed;
        _ipmi.CommandCompleted += OnCommandCompleted;
        _autoPolicyTimer.Tick += OnAutoPolicyTimerTick;
        _sensorPollingTimer.Tick += OnSensorPollingTimerTick;
        _sensorPollingRetryTimer.Tick += OnSensorPollingRetryTimerTick;
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

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
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
                await ConnectAndStartPollingAsync();
            }
        }
        catch (Exception ex)
        {
            await HandlePageLoadFailureAsync(ex);
        }
    }

    private async Task HandlePageLoadFailureAsync(Exception ex)
    {
        _sensorPollingTimer.Stop();
        StopSensorPollingRetry();
        _autoPolicyTimer.Stop();
        _settings = new AppSettings();
        LocalizationService.SetLanguage(_settings.Language);
        LoadSettingsToControls(_settings);
        ApplyTheme(_settings.Theme);
        RebuildFanChannels();
        RebuildPresets(_settings.Presets);
        ApplyLocalization();
        ResetNewCurveEditor();
        ResetNewPowerCurveEditor();
        SelectView("Settings");
        ShowFailure(new InvalidOperationException($"{T("Hero.RequestShortFailed")}: {ex.Message}", ex));
        try
        {
            await FlushAppLogAsync();
        }
        catch (Exception logException)
        {
            ShowAppLogWriteFailure(logException);
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

        var userDataFolder = GetVisualizationWebViewUserDataFolder();
        Directory.CreateDirectory(userDataFolder);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder, EnvironmentVariableTarget.Process);
        await VisualizationWebView.EnsureCoreWebView2Async();
        VisualizationWebView.CoreWebView2.WebMessageReceived -= OnVisualizationWebMessageReceived;
        VisualizationWebView.CoreWebView2.WebMessageReceived += OnVisualizationWebMessageReceived;
        VisualizationWebView.Source = new Uri(dashboardPath);
        _visualizationInitialized = true;
        VisualizationStateText.Text = T("Dashboard.VisualizationLoading");
    }

    private static string GetVisualizationWebViewUserDataFolder()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DellR730xdFanControlCenter",
            "WebView2");
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

        var animate = root.TryGetProperty("animate", out var animateProperty) &&
            animateProperty.ValueKind == JsonValueKind.True;
        ScheduleVisualizationWheelScroll(deltaY, animate);
    }

    private void OnContentPanelPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var nativeWheelDelta = e.GetCurrentPoint(ContentPanel).Properties.MouseWheelDelta;
        if (nativeWheelDelta == 0)
        {
            return;
        }

        var animate = Math.Abs(nativeWheelDelta) >= 120;
        var deltaY = -nativeWheelDelta * VisualizationMouseWheelDeltaY / 120d;
        ScheduleVisualizationWheelScroll(deltaY, animate);
        e.Handled = true;
    }

    private void ScheduleVisualizationWheelScroll(double deltaY, bool animate)
    {
        _pendingVisualizationWheelDeltaY = Math.Clamp(_pendingVisualizationWheelDeltaY + deltaY, -6000d, 6000d);
        _pendingVisualizationWheelShouldAnimate |= animate;
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
        var animate = _pendingVisualizationWheelShouldAnimate;
        _pendingVisualizationWheelDeltaY = 0;
        _pendingVisualizationWheelShouldAnimate = false;

        if (Math.Abs(deltaY) < 0.01)
        {
            return;
        }

        var nextOffset = _visualizationWheelScrollState.Accumulate(
            ContentScrollViewer.VerticalOffset,
            deltaY,
            ContentScrollViewer.ScrollableHeight,
            Environment.TickCount64);
        ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: !animate);
    }

    public Task ApplyQuickFanSpeedAsync(int percent)
    {
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

    public void ReportTrayCommandFailure(Exception ex)
    {
        ShowFailure(ex);
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
        try
        {
            if (_sensorPollingTimer.IsEnabled || _sensorPollingRetryTimer.IsEnabled || _sensorPollingRetryRunning)
            {
                CancelSensorPollingFromUser();
                return;
            }

            await ConnectAndStartPollingAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void CancelSensorPollingFromUser()
    {
        var reason = T("Action.CancelPolling");
        StopSensorPolling(reason);
        ShowStatus(F("Status.PollingStopped", reason), InfoBarSeverity.Informational);
    }

    private async void OnRefreshSensorsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshSensorsAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async Task RefreshSensorsAsync()
    {
        await RunUiCommandAsync(T("Status.RefreshingSensors"), async token =>
        {
            await RefreshSensorsCoreAsync(ReadProfile(), token);
        }, successMessageFactory: () => T("Status.SensorsRefreshed"));
    }

    private async Task<TimeSpan> RefreshSensorsAfterFanCommandCoreAsync(IdracProfile profile, CancellationToken token)
    {
        SetHeroRequestStatus(T("Status.RefreshingSensorsAfterFanCommand"));
        var elapsed = await RefreshSensorsCoreAsync(profile, token);
        await CheckSensorPollingLatency(elapsed);
        RestartSensorPollingAfterImmediateRefresh();
        return elapsed;
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

    private async Task ConnectAndStartPollingAsync(bool restoreRunningState = true, bool scheduleRetryOnFailure = true)
    {
        var restoreIntentVersion = CurrentFanControlIntentVersion();
        StopSensorPollingRetry(clearRunning: !_sensorPollingRetryRunning);
        SetConnectingState();
        var connected = await RunUiCommandAsync(T("Status.Connecting"), async token =>
        {
            var profile = ReadProfile();
            await _ipmi.TestConnectionAsync(profile, token);
            var elapsed = await RefreshSensorsCoreAsync(profile, token);
            await CheckSensorPollingLatency(elapsed);
        });
        if (!connected)
        {
            if (scheduleRetryOnFailure)
            {
                ScheduleSensorPollingRetry(T("Status.Connecting"));
            }

            return;
        }

        try
        {
            await StartSensorPollingAsync();
            ShowStatus(T("Status.ConnectedPolling"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            StopSensorPollingRetry();
            _sensorPollingTimer.Stop();
            _isConnecting = false;
            _hasDisconnected = true;
            UpdatePollingStatusTexts();
            ShowFailure(ex);
            return;
        }

        if (connected && restoreRunningState && IsFanControlIntentCurrent(restoreIntentVersion))
        {
            try
            {
                await RestoreLastRunningStateAsync();
            }
            catch (Exception ex)
            {
                StopSensorPolling(ex.Message, stopRetry: false);
                ShowFailure(ex);
                if (scheduleRetryOnFailure)
                {
                    ScheduleSensorPollingRetry(ex.Message);
                }
            }
        }
    }

    private async Task StartSensorPollingAsync()
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        var displayLevel = T("Log.Info");
        var message = F("Status.PollingStarted", intervalSeconds);
        _appLog.Write(new AppLogRecord
        {
            Level = NormalizeLogLevel(displayLevel),
            Category = "Application",
            EventName = "SensorPollingStarted",
            Message = message,
            Properties = MergeLogProperties(displayLevel, null),
        });
        await FlushAppLogAsync();

        StopSensorPollingRetry();
        _sensorPollingTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingTimer.Start();
        _isConnecting = false;
        _hasDisconnected = false;
        _pollingWasDegraded = false;
        _lastPollingWarningAt = DateTimeOffset.MinValue;
        _pollingSkipLogGate.ResetAll();
        UpdatePollingStatusTexts();
        AddVolatileLog(displayLevel, message);
    }

    private void StopSensorPolling(string reason, bool stopRetry = true)
    {
        _sensorPollingTimer.Stop();
        if (stopRetry)
        {
            StopSensorPollingRetry();
        }

        _isConnecting = false;
        _hasDisconnected = true;
        SetDashboardTileFreshness(false);
        UpdatePollingStatusTexts();
        AddLog(T("Log.Warn"), F("Status.PollingStopped", reason));
    }

    private void SetDashboardTileFreshness(bool isFresh)
    {
        if (!isFresh)
        {
            _dashboardSnapshotFreshness.MarkStale();
        }

        foreach (var tiles in new[] { TemperatureTiles, FanTiles, PowerTiles })
        {
            foreach (var tile in tiles)
            {
                tile.IsDataFresh = isFresh;
                tile.AutomationFreshnessText = isFresh ? string.Empty : T("State.Disconnected");
            }
        }
    }

    private void ScheduleSensorPollingRetry(string reason)
    {
        var intervalSeconds = Math.Max(1, _settings.SensorRefreshSeconds);
        _sensorPollingRetryTimer.Stop();
        _sensorPollingRetryTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _sensorPollingRetryTimer.Start();
        _isConnecting = false;
        _hasDisconnected = true;
        UpdatePollingStatusTexts();
        AddLog(T("Log.Warn"), $"{F("Status.PollingStopped", reason)} {T("Status.Connecting")}");
    }

    private void StopSensorPollingRetry(bool clearRunning = true)
    {
        _sensorPollingRetryTimer.Stop();
        if (clearRunning)
        {
            _sensorPollingRetryRunning = false;
        }
    }

    private async void OnSensorPollingRetryTimerTick(object? sender, object e)
    {
        if (_sensorPollingRetryRunning || _sensorPollingTimer.IsEnabled)
        {
            return;
        }

        _sensorPollingRetryRunning = true;
        try
        {
            await ConnectAndStartPollingAsync(restoreRunningState: false, scheduleRetryOnFailure: true);
        }
        catch (Exception ex)
        {
            StopSensorPollingRetry();
            _isConnecting = false;
            _hasDisconnected = true;
            UpdatePollingStatusTexts();
            ShowFailure(ex);
        }
        finally
        {
            _sensorPollingRetryRunning = false;
            UpdatePollingStatusTexts();
        }
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
        try
        {
            await RunSensorPollingTimerTickAsync();
        }
        catch (Exception ex)
        {
            _sensorPollingTimer.Stop();
            StopSensorPollingRetry();
            _sensorPollingTickRunning = false;
            _isConnecting = false;
            _hasDisconnected = true;
            SetDashboardTileFreshness(false);
            UpdatePollingStatusTexts();
            ShowFailure(ex);
        }
    }

    private async Task RunSensorPollingTimerTickAsync()
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
        Exception? pollingFailure = null;
        try
        {
            var elapsed = await RefreshSensorsCoreAsync(BuildProfileFromSettings(), CancellationToken.None);
            await CheckSensorPollingLatency(elapsed);
        }
        catch (Exception ex)
        {
            pollingFailure = ex;
        }
        finally
        {
            _ipmiOperationLock.Release();
            _sensorPollingTickRunning = false;
        }

        if (pollingFailure is not null)
        {
            StopSensorPolling(pollingFailure.Message, stopRetry: false);
            ShowFailure(pollingFailure);
            await ConnectAndStartPollingAsync(restoreRunningState: false, scheduleRetryOnFailure: true);
            if (!_sensorPollingTimer.IsEnabled && !_sensorPollingRetryTimer.IsEnabled)
            {
                ScheduleSensorPollingRetry(pollingFailure.Message);
            }
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
        var operationCompleted = false;

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
            operation.Succeed(
                T("Status.SensorsRefreshed"),
                new Dictionary<string, string>
                {
                    ["sensorCount"] = Sensors.Count.ToString(CultureInfo.InvariantCulture),
                    ["elapsedMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                });
            operationCompleted = true;
            await FlushAppLogAsync();
            SetHeroRequestStatus(F("Hero.SensorRefreshSucceeded", Sensors.Count, stopwatch.Elapsed.TotalSeconds));
            ClearStaleFailureStatusAfterSensorRefreshSuccess();
            return stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            try
            {
                if (!operationCompleted)
                {
                    operation.Fail(ex);
                    await FlushAppLogAsync();
                }
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
        try
        {
            await ApplyAllFansAsync(ReadInt(AllFanPercentBox, T("Field.AllFanPercent")));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async Task ApplyAllFansAsync(int percent)
    {
        var description = F("Status.SetAllFans", percent);
        var intentVersion = BeginFanControlIntent();
        string? successMessage = null;
        await RunUiCommandAsync(
            description,
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                var profile = ReadProfile();
                await _ipmi.SetAllFansManualSpeedAsync(profile, percent, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                AllFanSlider.Value = percent;
                AllFanPercentBox.Value = percent;
                _activeCurvePreset = null;
                ClearAutoPolicyFanTargetCache();
                SetModeSummary("Mode.Manual");
                MarkActivePreset(null, persistRunningState: true);
                var elapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                successMessage = F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds);
            },
            beforeWaitForIpmiLock: StopAutoPolicyForManualOverride,
            successMessageFactory: () => successMessage,
            fanControlIntentVersion: intentVersion);
    }

    private async void OnSetSingleFanClick(object sender, RoutedEventArgs e)
    {
        try
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

            var description = F("Status.FanSet", fanIndex, percent);
            var intentVersion = BeginFanControlIntent();
            string? successMessage = null;
            await RunUiCommandAsync(
                description,
                async token =>
                {
                    ThrowIfFanControlIntentSuperseded(intentVersion, description);
                    var profile = ReadProfile();
                    await _ipmi.SetSingleFanManualSpeedAsync(profile, fanIndex, percent, token);
                    ThrowIfFanControlIntentSuperseded(intentVersion, description);
                    _activeCurvePreset = null;
                    ClearAutoPolicyFanTargetCache();
                    SetModeSummary("Mode.Manual");
                    MarkActivePreset(null, persistRunningState: true);
                    var elapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, token);
                    ThrowIfFanControlIntentSuperseded(intentVersion, description);
                    successMessage = F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds);
                },
                beforeWaitForIpmiLock: StopAutoPolicyForManualOverride,
                successMessageFactory: () => successMessage,
                fanControlIntentVersion: intentVersion);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
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
            var successMessage = F("Status.PresetSaved", preset.DisplayName);
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);
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

    private async void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = ReadPresetFromSender(sender);

            Presets.Remove(preset);
            if (_activePresetId?.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                _activePresetId = null;
                ClearPersistedRunningState();
            }

            if (_activeCurvePreset?.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                InvalidateFanControlIntent();
                StopAutoPolicyForManualOverride();
            }

            _settings.Presets = Presets.Select(ValidateAndClonePreset).ToList();
            _settingsStore.Save(_settings);
            RefreshPresetRows();
            var successMessage = F("Status.PresetDeleted", preset.DisplayName);
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void OnAddPresetClick(object sender, RoutedEventArgs e)
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
            var successMessage = F("Status.PresetAdded", preset.DisplayName);
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);
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
            var successMessage = F(statusKey, name);
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);
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
            var successMessage = F(statusKey, name);
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);
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
        if (!DispatcherQueue.TryEnqueue(() =>
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

                if (!DispatcherQueue.TryEnqueue(() =>
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
                }))
                {
                    ShowFailure(new InvalidOperationException(F("Status.UiDispatchFailed", preset.DisplayName)));
                }
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
        }))
        {
            ShowFailure(new InvalidOperationException(F("Status.UiDispatchFailed", presetId)));
        }
    }

    private async void OnResetAutoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ResetDellAutomaticModeAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void OnRestoreDefaultClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ResetDellAutomaticModeAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
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
        var description = F("Status.SetAllFans", percent);
        var intentVersion = BeginFanControlIntent();
        string? successMessage = null;
        await RunUiCommandAsync(
            description,
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                var profile = ReadProfile();
                await _ipmi.SetAllFansManualSpeedAsync(profile, percent, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                AllFanSlider.Value = percent;
                AllFanPercentBox.Value = percent;
                _activeCurvePreset = null;
                ClearAutoPolicyFanTargetCache();
                SetModeSummary("Mode.PresetManual", preset.DisplayName, percent);
                MarkActivePreset(preset.Id, persistRunningState: true);
                var elapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                successMessage = F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds);
            },
            beforeWaitForIpmiLock: StopAutoPolicyForManualOverride,
            successMessageFactory: () => successMessage,
            fanControlIntentVersion: intentVersion);
    }

    private async Task RestoreDefaultManualAsync(string? activePresetId = "restore-manual", int? percentOverride = null)
    {
        var percent = percentOverride ?? AppSettings.LocalDefaultManualFanPercent;
        var description = F("Status.RestoringDefault", percent);
        var intentVersion = BeginFanControlIntent();
        string? successMessage = null;
        await RunUiCommandAsync(
            description,
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                PersistSettingsFromControls();
                var profile = ReadProfile();
                await _ipmi.SetAllFansManualSpeedAsync(profile, percent, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                AllFanSlider.Value = percent;
                AllFanPercentBox.Value = percent;
                _activeCurvePreset = null;
                ClearAutoPolicyFanTargetCache();
                SetModeSummary("Mode.ManualPercent", percent);
                MarkActivePreset(activePresetId, persistRunningState: true);
                var elapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                successMessage = F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds);
            },
            beforeWaitForIpmiLock: StopAutoPolicyForManualOverride,
            successMessageFactory: () => successMessage,
            fanControlIntentVersion: intentVersion);
    }

    private async Task ResetDellAutomaticModeAsync(string? activePresetId = "dell-auto")
    {
        var description = T("Status.ResettingDellAuto");
        var intentVersion = BeginFanControlIntent();
        string? successMessage = null;
        await RunUiCommandAsync(
            description,
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                var profile = ReadProfile();
                await _ipmi.SetDellAutomaticModeAsync(profile, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                _activeCurvePreset = null;
                ClearAutoPolicyFanTargetCache();
                SetModeSummary("Mode.DellAuto");
                MarkActivePreset(activePresetId, persistRunningState: true);
                var elapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, token);
                ThrowIfFanControlIntentSuperseded(intentVersion, description);
                successMessage = F("Status.FanCommandSensorsRefreshed", elapsed.TotalSeconds);
            },
            beforeWaitForIpmiLock: StopAutoPolicyForManualOverride,
            successMessageFactory: () => successMessage,
            fanControlIntentVersion: intentVersion);
    }

    private async Task ApplyCurvePresetAsync(FanPreset preset)
    {
        var curvePreset = ValidateAndClonePreset(preset);
        var intentVersion = BeginFanControlIntent();
        _activeAutoPolicyIntentVersion = intentVersion;
        _activeCurvePreset = curvePreset;
        SetModeSummary("Mode.CurveStarting", curvePreset.DisplayName);
        SetAutoPolicyPendingSummary();
        var firstTickSucceeded = false;
        var started = await RunUiCommandAsync(
            F("Status.CurvePresetStarted", curvePreset.DisplayName),
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, F("Status.CurvePresetStarted", curvePreset.DisplayName));
                PersistSettingsFromControls();
                _activeCurvePreset = curvePreset;
                ClearAutoPolicyFanTargetCache();
                ForceNextAutoPolicyFanCommand();
                PrepareAutoPolicyRunningState();
                try
                {
                    if (!await RunAutoPolicyOnceCoreAsync(token, intentVersion))
                    {
                        throw new FanControlIntentSupersededException(
                            $"{T("Hero.RequestShortSkipped")}: {F("Status.CurvePresetStarted", curvePreset.DisplayName)}");
                    }

                    firstTickSucceeded = true;
                    MarkActivePreset(curvePreset.Id);
                    PersistRunningPresetState(curvePreset.Id);
                    await WriteDurableUiLogAsync(T("Log.Info"), F("Status.CurvePresetStarted", curvePreset.DisplayName));
                }
                catch (FanControlIntentSupersededException)
                {
                    throw;
                }
                catch
                {
                    if (IsFanControlIntentCurrent(intentVersion))
                    {
                        StopAutoPolicyAfterFailure();
                    }

                    throw;
                }
            },
            fanControlIntentVersion: intentVersion);

        if (started && firstTickSucceeded)
        {
            StartAutoPolicyTimer();
            ShowStatus(F("Status.CurvePresetStarted", curvePreset.DisplayName), InfoBarSeverity.Success);
        }
        else if (IsFanControlIntentCurrent(intentVersion))
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
            ShowFailure(ex);
        }
    }

    private async Task StartSmartAutoPolicyAsync(bool persistControls)
    {
        if (persistControls)
        {
            PersistSettingsFromControls();
        }

        var intentVersion = BeginFanControlIntent();
        _activeAutoPolicyIntentVersion = intentVersion;
        _activeCurvePreset = null;
        ClearAutoPolicyFanTargetCache();
        ForceNextAutoPolicyFanCommand();
        SetModeSummary("Mode.SmartStarting");
        SetAutoPolicyPendingSummary();
        var firstTickSucceeded = false;

        var started = await RunUiCommandAsync(
            T("Status.AutoStarted"),
            async token =>
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                PrepareAutoPolicyRunningState();
                if (!await RunAutoPolicyOnceCoreAsync(token, intentVersion))
                {
                    throw new FanControlIntentSupersededException(
                        $"{T("Hero.RequestShortSkipped")}: {T("Status.AutoStarted")}");
                }

                firstTickSucceeded = true;
                PersistSmartAutoRunningState();
                await WriteDurableUiLogAsync(T("Log.Info"), T("Status.AutoStarted"));
            },
            fanControlIntentVersion: intentVersion);

        if (started && firstTickSucceeded)
        {
            StartAutoPolicyTimer();
        }
        else if (IsFanControlIntentCurrent(intentVersion))
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
        InvalidateFanControlIntent();
        _autoPolicyTimer.Stop();
        _activeAutoPolicyIntentVersion = 0;
        _activeCurvePreset = null;
        ClearAutoPolicyFanTargetCache();
        ClearForcedAutoPolicyFanCommand();
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        ResetAutoPolicyModeSummary();
        MarkActivePreset(null);
        ClearPersistedRunningState();
        AddLog(T("Log.Info"), T("Status.AutoStopped"));
    }

    private void StopAutoPolicyForManualOverride()
    {
        var hadConfirmedAutomaticFanTarget = _autoPolicyRunning && _lastAutoPolicyFanPercent.HasValue;
        var wasUnconfirmedAutomaticStart = _autoPolicyRunning && !_lastAutoPolicyFanPercent.HasValue;
        _autoPolicyTimer.Stop();
        _activeAutoPolicyIntentVersion = 0;
        _activeCurvePreset = null;
        ClearAutoPolicyFanTargetCache();
        ClearForcedAutoPolicyFanCommand();
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        if (hadConfirmedAutomaticFanTarget)
        {
            ResetAutoPolicyModeSummary();
        }
        else if (wasUnconfirmedAutomaticStart)
        {
            SetModeSummary("Mode.Idle");
        }

        MarkActivePreset(null);
        ClearPersistedRunningState();
    }

    private void PrepareAutoPolicyRunningState()
    {
        StartAutoButton.IsEnabled = false;
        StopAutoButton.IsEnabled = true;
        SetAutoPolicySummary(true);
    }

    private void ResetAutoPolicyModeSummary()
    {
        SetModeSummary("Mode.Manual");
    }

    private void StartAutoPolicyTimer()
    {
        _autoPolicyTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SensorRefreshSeconds));
        _autoPolicyTimer.Start();
    }

    private void StopAutoPolicyAfterFailure()
    {
        _autoPolicyTimer.Stop();
        _activeAutoPolicyIntentVersion = 0;
        _activeCurvePreset = null;
        ClearAutoPolicyFanTargetCache();
        ClearForcedAutoPolicyFanCommand();
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        ResetAutoPolicyModeSummary();
        MarkActivePreset(null);
        ClearPersistedRunningState();
    }

    private void StopAutoPolicyAfterEmergencyDellAuto()
    {
        InvalidateFanControlIntent();
        _autoPolicyTimer.Stop();
        _activeAutoPolicyIntentVersion = 0;
        _activeCurvePreset = null;
        ClearAutoPolicyFanTargetCache();
        ClearForcedAutoPolicyFanCommand();
        StartAutoButton.IsEnabled = true;
        StopAutoButton.IsEnabled = false;
        SetAutoPolicySummary(false);
        MarkActivePreset("dell-auto", persistRunningState: true);
    }

    private long BeginFanControlIntent()
    {
        return Interlocked.Increment(ref _fanControlIntentVersion);
    }

    private void InvalidateFanControlIntent()
    {
        Interlocked.Increment(ref _fanControlIntentVersion);
    }

    private long CurrentFanControlIntentVersion()
    {
        return Interlocked.Read(ref _fanControlIntentVersion);
    }

    private bool IsFanControlIntentCurrent(long intentVersion)
    {
        return Interlocked.Read(ref _fanControlIntentVersion) == intentVersion;
    }

    private void ThrowIfFanControlIntentSuperseded(long intentVersion, string description)
    {
        if (IsFanControlIntentCurrent(intentVersion))
        {
            return;
        }

        throw new FanControlIntentSupersededException($"{T("Hero.RequestShortSkipped")}: {description}");
    }

    private async void OnAutoPolicyTimerTick(object? sender, object e)
    {
        var intentVersion = _activeAutoPolicyIntentVersion;
        try
        {
            await RunAutoPolicyTimerTickAsync(intentVersion);
        }
        catch (Exception ex)
        {
            _autoPolicyTickRunning = false;
            if (IsFanControlIntentCurrent(intentVersion))
            {
                try
                {
                    StopAutoPolicyAfterFailure();
                }
                catch (Exception stateException)
                {
                    ShowFailure(new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{stateException.Message}",
                        new AggregateException(ex, stateException)));
                    return;
                }
            }

            ShowFailure(ex);
        }
    }

    private async Task RunAutoPolicyTimerTickAsync(long intentVersion)
    {
        if (intentVersion == 0 || !IsFanControlIntentCurrent(intentVersion))
        {
            _autoPolicyTimer.Stop();
            return;
        }

        if (_autoPolicyTickRunning)
        {
            LogPollingSkip(PollingSkipKind.AutoPolicyTickRunning, T("Status.AutoTickSkipped"));
            return;
        }

        _autoPolicyTickRunning = true;
        _pollingSkipLogGate.Reset(PollingSkipKind.AutoPolicyTickRunning);
        var lockTaken = false;
        try
        {
            if (!await _ipmiOperationLock.WaitAsync(0))
            {
                var message = T("Status.AutoTickSkippedIpmiBusy");
                LogPollingSkip(PollingSkipKind.AutoPolicyIpmiBusy, message);
                return;
            }

            lockTaken = true;
            _pollingSkipLogGate.Reset(PollingSkipKind.AutoPolicyIpmiBusy);
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
            await RunAutoPolicyOnceCoreAsync(CancellationToken.None, intentVersion);
        }
        catch (AutoPolicyTransientSensorReadException ex)
        {
            try
            {
                await ContinueAutoPolicyAfterTransientSensorReadFailureAsync(ex, intentVersion);
            }
            catch (Exception logException)
            {
                if (IsFanControlIntentCurrent(intentVersion))
                {
                    StopAutoPolicyAfterFailure();
                }

                ShowFailure(logException);
            }
        }
        catch (AutoPolicyFanTargetRejectedException ex)
        {
            try
            {
                await ContinueAutoPolicyAfterFanTargetFailureAsync(ex, intentVersion);
            }
            catch (Exception logException)
            {
                if (IsFanControlIntentCurrent(intentVersion))
                {
                    StopAutoPolicyAfterFailure();
                }

                ShowFailure(logException);
            }
        }
        catch (FanCommandSafetyRecoveryException ex)
        {
            try
            {
                if (IsFanControlIntentCurrent(intentVersion))
                {
                    StopAutoPolicyAfterEmergencyDellAuto();
                    SetModeSummary("Mode.DellAuto");
                }

                ShowFailure(ex);
            }
            catch (Exception statePersistenceFailure)
            {
                ShowFailure(new InvalidOperationException(
                    $"{ex.Message}{Environment.NewLine}{F("Status.DellAutoStatePersistenceFailed", statePersistenceFailure.Message)}",
                    new AggregateException(ex, statePersistenceFailure)));
            }
        }
        catch (FanControlIntentSupersededException ex)
        {
            AddLog(T("Log.Info"), ex.Message);
            try
            {
                await FlushAppLogAsync();
            }
            catch (Exception logException)
            {
                ShowFailure(logException);
            }
        }
        catch (Exception ex)
        {
            if (IsFanControlIntentCurrent(intentVersion))
            {
                StopAutoPolicyAfterFailure();
            }

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

    private async Task ContinueAutoPolicyAfterTransientSensorReadFailureAsync(AutoPolicyTransientSensorReadException ex, long intentVersion)
    {
        if (!IsFanControlIntentCurrent(intentVersion))
        {
            return;
        }

        StartAutoButton.IsEnabled = false;
        StopAutoButton.IsEnabled = true;
        SetAutoPolicySummary(true);
        ShowFailure(ex.InnerException ?? ex);
        await FlushAppLogAsync();
    }

    private async Task ContinueAutoPolicyAfterFanTargetFailureAsync(
        AutoPolicyFanTargetRejectedException ex,
        long intentVersion)
    {
        if (!IsFanControlIntentCurrent(intentVersion))
        {
            return;
        }

        StartAutoButton.IsEnabled = false;
        StopAutoButton.IsEnabled = true;
        SetAutoPolicySummary(true);
        ShowFailure(ex);
        await FlushAppLogAsync();
    }

    private async Task<bool> RunAutoPolicyOnceCoreAsync(CancellationToken cancellationToken, long intentVersion)
    {
        ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
        var activeCurvePreset = _activeCurvePreset?.Clone();
        SetHeroRequestStatus(activeCurvePreset is null ? T("Status.AutoStarted") : F("Status.CurvePresetStarted", activeCurvePreset.DisplayName));
        var operationProperties = new Dictionary<string, string>
        {
            ["targetTemperatureCelsius"] = _settings.TargetCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
            ["highTemperatureCelsius"] = _settings.HighCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
            ["emergencyTemperatureCelsius"] = _settings.EmergencyCpuTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture),
            ["forceFanCommand"] = _forceNextAutoPolicyFanCommand.ToString(CultureInfo.InvariantCulture),
            ["fanControlIntentVersion"] = intentVersion.ToString(CultureInfo.InvariantCulture),
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
        Dictionary<string, string>? fanCommandProperties = null;
        var operationCompleted = false;
        var failureStage = AutoPolicyTickFailureStage.Starting;

        try
        {
            var profile = BuildProfileFromSettings();
            failureStage = AutoPolicyTickFailureStage.ReadSensors;
            var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken);
            failureStage = AutoPolicyTickFailureStage.UpdateSensorState;
            ReplaceSensors(readings);
            UpdateMetricSummaries();
            var snapshotTime = DateTime.Now;
            _lastPollTime = snapshotTime;
            RecordVisualizationHistoryPoint(snapshotTime);
            ScheduleVisualizationSnapshot();
            ClearStaleFailureStatusAfterSensorRefreshSuccess();
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));

            var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(readings);
            if (cpuTemp >= _settings.EmergencyCpuTemperatureCelsius)
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                failureStage = AutoPolicyTickFailureStage.ApplyFanCommand;
                await _ipmi.SetDellAutomaticModeAsync(profile, cancellationToken);
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                failureStage = AutoPolicyTickFailureStage.RefreshAfterEmergencyDellAuto;
                var emergencyRefreshElapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, cancellationToken);
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                var emergencyMessage = F("Status.EmergencyAuto", cpuTemp);
                operation.Succeed(
                    emergencyMessage,
                    new Dictionary<string, string>
                    {
                        ["cpuTemperatureCelsius"] = cpuTemp.ToString("0.0", CultureInfo.InvariantCulture),
                        ["action"] = "RestoreDellAutomaticMode",
                        ["postCommandRefreshSeconds"] = emergencyRefreshElapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
                    });
                operationCompleted = true;
                await FlushAppLogAsync();
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                StopAutoPolicyAfterEmergencyDellAuto();
                SetModeSummary("Mode.DellAuto");
                AddVolatileLog(T("Log.Warn"), emergencyMessage);
                ShowStatus(emergencyMessage, InfoBarSeverity.Warning);
                return false;
            }

            double? powerWatts = null;
            var percent = CalculateFanPercentForAutoTick(activeCurvePreset, cpuTemp, readings, out powerWatts);
            var targetKey = GetAutoPolicyTargetKey(activeCurvePreset);
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));

            if (ShouldSkipUnchangedAutoPolicyFanCommand(targetKey, percent))
            {
                var unchangedMessage = FormatAutoFanUnchanged(activeCurvePreset, cpuTemp, powerWatts, percent);
                var unchangedProperties = BuildAutoPolicyFanCommandProperties(cpuTemp, percent, powerWatts, "SkipUnchangedFanPercent");
                operation.Succeed(unchangedMessage, unchangedProperties);
                operationCompleted = true;
                await FlushAppLogAsync();
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                SetAutoModeSummary(activeCurvePreset, percent);
                AddVolatileLog(T("Log.Info"), unchangedMessage);
                SetHeroRequestStatus(unchangedMessage);
                return true;
            }

            if (ShouldSkipPreviouslyFailedAutoPolicyFanCommand(targetKey, percent))
            {
                var failedTargetMessage = F("Status.AutoFanFailedTargetSkipped", percent);
                var failedTargetProperties = BuildAutoPolicyFanCommandProperties(
                    cpuTemp,
                    percent,
                    powerWatts,
                    "SkipPreviouslyFailedFanPercent");
                if (_lastAutoPolicyFanPercent.HasValue)
                {
                    failedTargetProperties["lastConfirmedFanPercent"] =
                        _lastAutoPolicyFanPercent.Value.ToString(CultureInfo.InvariantCulture);
                }

                failedTargetProperties["modeChangeAttempted"] = bool.FalseString;
                failedTargetProperties["retrySuppressed"] = bool.TrueString;
                operation.Succeed(failedTargetMessage, failedTargetProperties);
                operationCompleted = true;
                await FlushAppLogAsync();
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                AddVolatileLog(T("Log.Warn"), failedTargetMessage);
                ShowStatus(failedTargetMessage, InfoBarSeverity.Warning);
                return true;
            }

            var updateConfirmedManualMode = CanUpdateAutoPolicyFanPercentWithoutModeChange(targetKey);
            fanCommandProperties = BuildAutoPolicyFanCommandProperties(
                cpuTemp,
                percent,
                powerWatts,
                updateConfirmedManualMode ? "SetFanSpeedInConfirmedManualMode" : "SetAllFansManualSpeed");
            fanCommandProperties["modeChangeAttempted"] = (!updateConfirmedManualMode).ToString(CultureInfo.InvariantCulture);
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
            failureStage = AutoPolicyTickFailureStage.ApplyFanCommand;
            try
            {
                if (updateConfirmedManualMode)
                {
                    await _ipmi.SetAllFansSpeedInConfirmedManualModeAsync(profile, percent, cancellationToken);
                }
                else
                {
                    await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);
                }
            }
            catch (Exception ex) when (updateConfirmedManualMode)
            {
                ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
                RememberFailedAutoPolicyFanTarget(targetKey, percent);
                fanCommandProperties["retrySuppressed"] = bool.TrueString;
                if (_lastAutoPolicyFanPercent.HasValue)
                {
                    fanCommandProperties["lastConfirmedFanPercent"] =
                        _lastAutoPolicyFanPercent.Value.ToString(CultureInfo.InvariantCulture);
                }

                throw new AutoPolicyFanTargetRejectedException(
                    F(
                        "Status.AutoFanTargetRejected",
                        percent,
                        _lastAutoPolicyFanPercent ?? percent,
                        ex.Message),
                    ex);
            }

            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
            RememberAutoPolicyFanTarget(targetKey, percent);
            failureStage = AutoPolicyTickFailureStage.RefreshAfterFanCommand;
            var appliedRefreshElapsed = await RefreshSensorsAfterFanCommandCoreAsync(profile, cancellationToken);
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
            var message = activeCurvePreset is null
                ? F("Status.SmartFanApplied", cpuTemp, percent)
                : activeCurvePreset.IsPowerCurvePreset
                    ? FormatPowerCurveFanApplied(activeCurvePreset.DisplayName, powerWatts!.Value, percent)
                    : F("Status.CurveFanApplied", activeCurvePreset.DisplayName, cpuTemp, percent);
            var successProperties = new Dictionary<string, string>(fanCommandProperties)
            {
                ["postCommandRefreshSeconds"] = appliedRefreshElapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            };
            operation.Succeed(message, successProperties);
            operationCompleted = true;
            await FlushAppLogAsync();
            ThrowIfFanControlIntentSuperseded(intentVersion, T("Status.AutoStarted"));
            SetAutoModeSummary(activeCurvePreset, percent);
            AddVolatileLog(T("Log.Info"), message);
            SetHeroRequestStatus(message);
            return true;
        }
        catch (FanControlIntentSupersededException ex)
        {
            if (!operationCompleted)
            {
                try
                {
                    operation.Succeed(
                        ex.Message,
                        new Dictionary<string, string>
                        {
                            ["action"] = "SupersededByNewFanControlIntent",
                            ["fanControlIntentVersion"] = intentVersion.ToString(CultureInfo.InvariantCulture),
                        });
                    operationCompleted = true;
                    await FlushAppLogAsync();
                }
                catch (Exception logException)
                {
                    throw new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        new AggregateException(ex, logException));
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            Exception? terminalFailure = null;
            if (!operationCompleted)
            {
                try
                {
                    fanCommandProperties ??= new Dictionary<string, string>();
                    fanCommandProperties["failureStage"] = failureStage.ToString();
                    operation.Fail(ex, fanCommandProperties);
                    await FlushAppLogAsync();
                }
                catch (Exception logException)
                {
                    terminalFailure = new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        new AggregateException(ex, logException));
                }
            }

            if (failureStage is AutoPolicyTickFailureStage.RefreshAfterEmergencyDellAuto)
            {
                if (IsFanControlIntentCurrent(intentVersion))
                {
                    try
                    {
                        StopAutoPolicyAfterEmergencyDellAuto();
                        SetModeSummary("Mode.DellAuto");
                    }
                    catch (Exception statePersistenceFailure)
                    {
                        terminalFailure = new InvalidOperationException(
                            $"{(terminalFailure ?? ex).Message}{Environment.NewLine}{F("Status.DellAutoStatePersistenceFailed", statePersistenceFailure.Message)}",
                            new AggregateException(terminalFailure ?? ex, statePersistenceFailure));
                    }
                }

                if (terminalFailure is not null)
                {
                    throw terminalFailure;
                }

                throw;
            }

            if (failureStage is AutoPolicyTickFailureStage.ReadSensors or
                AutoPolicyTickFailureStage.UpdateSensorState or
                AutoPolicyTickFailureStage.RefreshAfterFanCommand)
            {
                throw new AutoPolicyTransientSensorReadException(terminalFailure ?? ex);
            }

            if (!IsFanControlIntentCurrent(intentVersion))
            {
                throw new FanControlIntentSupersededException($"{T("Hero.RequestShortSkipped")}: {T("Status.AutoStarted")}");
            }

            if (terminalFailure is not null)
            {
                throw terminalFailure;
            }

            throw;
        }
    }

    private static Dictionary<string, string> BuildAutoPolicyFanCommandProperties(double cpuTemp, int percent, double? powerWatts, string action)
    {
        var properties = new Dictionary<string, string>
        {
            ["cpuTemperatureCelsius"] = cpuTemp.ToString("0.0", CultureInfo.InvariantCulture),
            ["fanPercent"] = percent.ToString(CultureInfo.InvariantCulture),
            ["action"] = action,
        };
        if (powerWatts.HasValue)
        {
            properties["powerWatts"] = powerWatts.Value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        return properties;
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

    private string FormatPowerCurveFanUnchanged(string presetName, double powerWatts, int percent)
    {
        return F("Status.PowerCurveFanUnchanged", presetName, powerWatts, T("SensorUnit.Watts"), percent);
    }

    private static string GetAutoPolicyTargetKey(FanPreset? activeCurvePreset)
    {
        return activeCurvePreset?.Id ?? SmartAutoPolicyTargetKey;
    }

    private bool ShouldSkipUnchangedAutoPolicyFanCommand(string targetKey, int percent)
    {
        return !_forceNextAutoPolicyFanCommand &&
               _lastAutoPolicyFanPercent == percent &&
               string.Equals(_lastAutoPolicyTargetKey, targetKey, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldSkipPreviouslyFailedAutoPolicyFanCommand(string targetKey, int percent)
    {
        return !_forceNextAutoPolicyFanCommand &&
               _lastFailedAutoPolicyFanPercent == percent &&
               string.Equals(_lastFailedAutoPolicyTargetKey, targetKey, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanUpdateAutoPolicyFanPercentWithoutModeChange(string targetKey)
    {
        return !_forceNextAutoPolicyFanCommand &&
               _lastAutoPolicyFanPercent.HasValue &&
               string.Equals(_lastAutoPolicyTargetKey, targetKey, StringComparison.OrdinalIgnoreCase);
    }

    private void RememberAutoPolicyFanTarget(string targetKey, int percent)
    {
        _lastAutoPolicyTargetKey = targetKey;
        _lastAutoPolicyFanPercent = percent;
        _lastFailedAutoPolicyTargetKey = null;
        _lastFailedAutoPolicyFanPercent = null;
        ClearForcedAutoPolicyFanCommand();
    }

    private void RememberFailedAutoPolicyFanTarget(string targetKey, int percent)
    {
        _lastFailedAutoPolicyTargetKey = targetKey;
        _lastFailedAutoPolicyFanPercent = percent;
    }

    private void ClearAutoPolicyFanTargetCache()
    {
        _lastAutoPolicyTargetKey = null;
        _lastAutoPolicyFanPercent = null;
        _lastFailedAutoPolicyTargetKey = null;
        _lastFailedAutoPolicyFanPercent = null;
    }

    private void ForceNextAutoPolicyFanCommand()
    {
        _forceNextAutoPolicyFanCommand = true;
    }

    private void ClearForcedAutoPolicyFanCommand()
    {
        _forceNextAutoPolicyFanCommand = false;
    }

    private string FormatAutoFanUnchanged(FanPreset? activeCurvePreset, double cpuTemp, double? powerWatts, int percent)
    {
        if (activeCurvePreset is null)
        {
            return F("Status.SmartFanUnchanged", cpuTemp, percent);
        }

        if (activeCurvePreset.IsPowerCurvePreset)
        {
            return FormatPowerCurveFanUnchanged(activeCurvePreset.DisplayName, powerWatts!.Value, percent);
        }

        return F("Status.CurveFanUnchanged", activeCurvePreset.DisplayName, cpuTemp, percent);
    }

    private void SetAutoModeSummary(FanPreset? activeCurvePreset, int percent)
    {
        if (activeCurvePreset is null)
        {
            SetModeSummary("Mode.SmartPercent", percent);
        }
        else
        {
            SetModeSummary("Mode.CurvePercent", activeCurvePreset.DisplayName, percent);
        }
    }

    private void OnVisitIdracClick(object sender, RoutedEventArgs e)
    {
        OpenIdrac();
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
            var successMessage = T("Status.SettingsSaved");
            await WriteDurableUiLogAsync(T("Log.Info"), successMessage);
            ShowStatus(successMessage, InfoBarSeverity.Success);

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

        if (tag == "Overview")
        {
            RefreshVisibleLogsIfDirty();
            RefreshOverviewDataIfVisible();
        }
    }

    private bool IsOverviewViewVisible()
    {
        return OverviewView.Visibility == Visibility.Visible;
    }

    private void RefreshOverviewDataIfVisible()
    {
        if (!IsOverviewViewVisible())
        {
            return;
        }

        if (_overviewMetricsDirty && Sensors.Count > 0)
        {
            UpdateOverviewMetricSummaries();
        }

        if (_visualizationSnapshotDirty)
        {
            ScheduleVisualizationSnapshot();
        }
    }

    private async Task FlushAppLogAsync()
    {
        try
        {
            await _appLog.FlushAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(F("Status.LogWriteFailed", ex.InnerException?.Message ?? ex.Message), ex);
        }
    }

    private async Task<bool> RunUiCommandAsync(
        string description,
        Func<CancellationToken, Task> command,
        bool waitForIpmiLock = true,
        Action? beforeWaitForIpmiLock = null,
        Func<string?>? successMessageFactory = null,
        long? fanControlIntentVersion = null)
    {
        var lockTaken = false;
        AppLogOperation? operation = null;
        var operationCompleted = false;
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
            beforeWaitForIpmiLock?.Invoke();
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
            if (fanControlIntentVersion.HasValue)
            {
                ThrowIfFanControlIntentSuperseded(fanControlIntentVersion.Value, description);
            }

            operation.Succeed(description);
            operationCompleted = true;
            await FlushAppLogAsync();
            if (fanControlIntentVersion.HasValue)
            {
                ThrowIfFanControlIntentSuperseded(fanControlIntentVersion.Value, description);
            }

            var successMessage = successMessageFactory?.Invoke();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                ShowStatus(successMessage, InfoBarSeverity.Success);
            }
            else if (string.Equals(_heroRequestMessage, runningMessage, StringComparison.Ordinal))
            {
                SetHeroRequestStatus(F("Hero.RequestSucceeded", description));
            }

            return true;
        }
        catch (FanControlIntentSupersededException ex)
        {
            if (!operationCompleted && operation is not null)
            {
                try
                {
                    operation.Succeed(
                        ex.Message,
                        new Dictionary<string, string>
                        {
                            ["action"] = "SupersededByNewFanControlIntent",
                        });
                    operationCompleted = true;
                    await FlushAppLogAsync();
                }
                catch (Exception logException)
                {
                    ShowFailure(new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        new AggregateException(ex, logException)));
                    return false;
                }
            }

            AddLog(T("Log.Info"), ex.Message);
            try
            {
                await FlushAppLogAsync();
            }
            catch (Exception logException)
            {
                ShowFailure(logException);
                return false;
            }

            SetHeroRequestStatus(ex.Message);
            return false;
        }
        catch (FanCommandSafetyRecoveryException ex)
        {
            Exception visibleFailure = ex;
            var supersededByNewFanControlIntent = fanControlIntentVersion.HasValue &&
                !IsFanControlIntentCurrent(fanControlIntentVersion.Value);
            try
            {
                if (!supersededByNewFanControlIntent)
                {
                    StopAutoPolicyAfterEmergencyDellAuto();
                    SetModeSummary("Mode.DellAuto");
                }
            }
            catch (Exception statePersistenceFailure)
            {
                visibleFailure = new InvalidOperationException(
                    $"{ex.Message}{Environment.NewLine}{F("Status.DellAutoStatePersistenceFailed", statePersistenceFailure.Message)}",
                    new AggregateException(ex, statePersistenceFailure));
            }

            if (_isConnecting)
            {
                _isConnecting = false;
                _hasDisconnected = true;
                UpdatePollingStatusTexts();
            }

            if (!operationCompleted && operation is not null)
            {
                try
                {
                    operation.Fail(visibleFailure);
                    await FlushAppLogAsync();
                }
                catch (Exception logException)
                {
                    ShowFailure(new InvalidOperationException(
                        $"{visibleFailure.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        new AggregateException(visibleFailure, logException)));
                    return false;
                }
            }

            ShowFailure(visibleFailure);
            return false;
        }
        catch (Exception ex)
        {
            if (_isConnecting)
            {
                _isConnecting = false;
                _hasDisconnected = true;
                UpdatePollingStatusTexts();
            }

            if (!operationCompleted && operation is not null)
            {
                try
                {
                    operation.Fail(ex);
                    await FlushAppLogAsync();
                }
                catch (Exception logException)
                {
                    ShowFailure(new InvalidOperationException(
                        $"{ex.Message}{Environment.NewLine}{F("Status.LogWriteFailed", logException.Message)}",
                        new AggregateException(ex, logException)));
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
        _dashboardSnapshotFreshness.MarkFresh();
    }

    private void UpdateMetricSummaries()
    {
        UpdateHeroRealtimeMetrics();
        if (!IsOverviewViewVisible())
        {
            _overviewMetricsDirty = true;
            return;
        }

        UpdateOverviewMetricSummaries();
    }

    private void UpdateOverviewMetricSummaries()
    {
        var displayableSensors = Sensors
            .Where(SensorReadingAvailability.IsDisplayable)
            .ToList();
        var temperatureReadings = displayableSensors
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

        var fanReadings = displayableSensors
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

        var metrics = HeroRealtimeMetrics.FromSensors(displayableSensors);
        PowerSummaryText.Text = FormatOverviewSummaryMetric(metrics.PowerWatts, "0.#", T("SensorUnit.Watts"));
        PowerSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.PowerItems, "0.#", "Overview.NoPowerReading");
        VoltageSummaryText.Text = FormatOverviewSummaryMetric(metrics.AverageVoltage, "0.#", T("SensorUnit.Volts"));
        VoltageSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.VoltageItems, "0.#", "Overview.NoVoltageReading");
        CurrentSummaryText.Text = FormatOverviewSummaryMetric(metrics.TotalCurrent, "0.#", T("SensorUnit.Amps"));
        CurrentSummaryDetailText.Text = FormatOverviewSummaryItems(metrics.CurrentItems, "0.#", "Overview.NoCurrentReading");

        var powerAndHealth = displayableSensors
            .Where(sensor => !IsTemperatureSensor(sensor) && !IsFanSensor(sensor))
            .Select(BuildDashboardTile);
        ReplaceTiles(PowerTiles, powerAndHealth);
        _overviewMetricsDirty = false;
    }

    private string GetDashboardAutomationVisualStateText(DashboardVisualState visualState)
    {
        return visualState switch
        {
            DashboardVisualState.Information => T("Log.Info"),
            DashboardVisualState.Inactive => T("SensorValue.Inactive"),
            DashboardVisualState.Unavailable => T("SensorValue.Unknown"),
            DashboardVisualState.Warning => T("Log.Warn"),
            DashboardVisualState.Critical => T("Log.Error"),
            DashboardVisualState.Normal => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(visualState), visualState, "Unsupported dashboard visual state."),
        };
    }

    private void OnDashboardSensorIconVisualUpdateFailed(
        object sender,
        Controls.DashboardSensorIconVisualFailureEventArgs args)
    {
        ShowFailure(args.Exception);
    }

    private DashboardTileViewModel BuildDashboardTile(SensorReading sensor)
    {
        var presentation = DashboardSensorPresentation.FromSensor(sensor);
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
            AccentHex = presentation.AccentHex,
            ValueHex = presentation.AccentHex,
            IconKind = presentation.IconKind,
            VisualState = presentation.VisualState,
            MotionKind = presentation.MotionKind,
            NormalizedLevel = presentation.NormalizedLevel,
            MotionPeriodSeconds = presentation.MotionPeriodSeconds,
            IsMotionActive = presentation.IsMotionActive,
            IsDataFresh = _dashboardSnapshotFreshness.IsFresh,
            AutomationVisualStateText = GetDashboardAutomationVisualStateText(presentation.VisualState),
            AutomationFreshnessText = _dashboardSnapshotFreshness.IsFresh ? string.Empty : T("State.Disconnected"),
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
            LoadVisualizationHistoryFile(historyPath, cutoff);
        }

        _sensorHistory.Sort((left, right) => left.UnixMilliseconds.CompareTo(right.UnixMilliseconds));
    }

    private void LoadVisualizationHistoryFile(string historyPath, DateTimeOffset cutoff)
    {
        var validLines = new List<string>();
        var corruptRows = new List<VisualizationHistoryCorruptRow>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(historyPath, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var point = JsonSerializer.Deserialize<SensorDashboardHistoryPoint>(line, VisualizationJsonOptions)
                    ?? throw new InvalidOperationException(F("Dashboard.HistoryRowEmpty", historyPath, lineNumber));
                var timestamp = GetVisualizationHistoryTimestamp(point, historyPath, lineNumber);
                NormalizeVisualizationHistoryPoint(point);
                validLines.Add(line);
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
            catch (Exception ex) when (IsVisualizationHistoryContentException(ex))
            {
                corruptRows.Add(new VisualizationHistoryCorruptRow(lineNumber, ex.Message));
            }
        }

        if (corruptRows.Count > 0)
        {
            RepairVisualizationHistoryFile(historyPath, validLines, corruptRows);
        }
    }

    private static bool IsVisualizationHistoryContentException(Exception ex)
    {
        return ex is JsonException or InvalidOperationException or ArgumentException;
    }

    private void RepairVisualizationHistoryFile(
        string historyPath,
        IReadOnlyList<string> validLines,
        IReadOnlyList<VisualizationHistoryCorruptRow> corruptRows)
    {
        var backupPath = BuildVisualizationHistoryRepairPath(historyPath);
        var tempPath = $"{historyPath}.repair-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fffffff}.tmp";
        File.Copy(historyPath, backupPath);
        File.WriteAllLines(tempPath, validLines, Encoding.UTF8);
        File.Move(tempPath, historyPath, overwrite: true);

        var firstCorruptRow = corruptRows[0];
        var message =
            $"{historyPath}:{firstCorruptRow.LineNumber} {firstCorruptRow.Message} " +
            $"Original file saved to {backupPath}; removed {corruptRows.Count} invalid row(s), kept {validLines.Count} valid row(s).";
        ReportVisualizationHistoryFailure("Dashboard.HistoryLoadFailed", new InvalidOperationException(message));
    }

    private static string BuildVisualizationHistoryRepairPath(string historyPath)
    {
        var directory = System.IO.Path.GetDirectoryName(historyPath)
            ?? throw new InvalidOperationException(historyPath);
        var fileName = System.IO.Path.GetFileName(historyPath);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        return System.IO.Path.Combine(directory, $"{fileName}.corrupt-{timestamp}.bak");
    }

    private static void NormalizeVisualizationHistoryPoint(SensorDashboardHistoryPoint point)
    {
        if (point.Current is not null)
        {
            point.Current = BuildHistoryCurrent(point.Current);
        }

        point.TypeCounts = [];
        point.SensorTree = [];
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
            Current = BuildHistoryCurrent(snapshot.Current),
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

    private static VisualizationCurrent BuildHistoryCurrent(VisualizationCurrent current)
    {
        return new VisualizationCurrent
        {
            Temperatures = BuildHistoryPoints(current.Temperatures),
            Fans = BuildHistoryPoints(current.Fans),
            Performance = BuildHistoryPoints(current.Performance),
            Electrical = BuildHistoryPoints(current.Electrical),
        };
    }

    private static List<VisualizationPoint> BuildHistoryPoints(IEnumerable<VisualizationPoint> points)
    {
        return points
            .Select(point => new VisualizationPoint
            {
                Id = point.Id,
                Name = point.Name,
                Type = point.Type,
                Value = point.Value,
                Unit = point.Unit,
            })
            .ToList();
    }

    private void PruneVisualizationHistoryMemory(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-VisualizationHistoryRetentionDays).ToUnixTimeMilliseconds();
        _sensorHistory.RemoveAll(point => point.UnixMilliseconds > 0 && point.UnixMilliseconds < cutoff);
    }

    private async void QueueVisualizationHistoryPersistence(SensorDashboardHistoryPoint historyPoint, DateTimeOffset timestamp)
    {
        try
        {
            await Task.Run(() => PersistVisualizationHistoryPoint(historyPoint, timestamp));
        }
        catch (Exception ex)
        {
            ReportVisualizationHistoryFailure("Dashboard.HistoryWriteFailed", ex);
        }
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
            if (!DispatcherQueue.TryEnqueue(() => ReportVisualizationHistoryFailure(key, ex)))
            {
                throw new InvalidOperationException(F("Status.UiDispatchFailed", "JSONL"), ex);
            }

            return;
        }

        try
        {
            AddLog(T("Log.Error"), message, "Visualization", "HistoryPersistence");
            ShowStatus(message, InfoBarSeverity.Error);
        }
        catch (Exception reportException)
        {
            ShowFailure(new InvalidOperationException(
                $"{message}{Environment.NewLine}{F("Status.LogWriteFailed", reportException.Message)}",
                new AggregateException(ex, reportException)));
        }
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
        if (!IsOverviewViewVisible())
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        if (_visualizationSnapshotUpdateScheduled)
        {
            return;
        }

        _visualizationSnapshotDirty = false;
        _visualizationSnapshotUpdateScheduled = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SendScheduledVisualizationSnapshot))
        {
            _visualizationSnapshotUpdateScheduled = false;
            _visualizationSnapshotDirty = true;
            ShowFailure(new InvalidOperationException("Unable to schedule chart update on the UI dispatcher."));
        }
    }

    private async void SendScheduledVisualizationSnapshot()
    {
        _visualizationSnapshotUpdateScheduled = false;
        try
        {
            await SendVisualizationSnapshot();
        }
        catch (Exception ex)
        {
            _visualizationSnapshotDirty = true;
            ShowFailure(ex);
        }
    }

    private async Task SendVisualizationSnapshot()
    {
        if (!IsOverviewViewVisible())
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        var payload = BuildVisualizationPayload();
        var json = await Task.Run(() => JsonSerializer.Serialize(payload, VisualizationJsonOptions));

        if (!IsOverviewViewVisible())
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        if (!_visualizationReady || VisualizationWebView.CoreWebView2 is null)
        {
            _visualizationSnapshotDirty = true;
            return;
        }

        VisualizationWebView.CoreWebView2.PostWebMessageAsJson(json);
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
            History = BuildVisualizationPayloadHistory(),
            TypeCounts = currentSnapshot.TypeCounts,
            SensorTree = currentSnapshot.SensorTree,
        };
    }

    private SensorDashboardHistoryPoint[] BuildVisualizationPayloadHistory()
    {
        if (_sensorHistory.Count <= MaxVisualizationPayloadHistoryPoints)
        {
            return _sensorHistory.ToArray();
        }

        var result = new List<SensorDashboardHistoryPoint>(MaxVisualizationPayloadHistoryPoints);
        var lastIndex = _sensorHistory.Count - 1;
        var lastAddedIndex = -1;
        for (var index = 0; index < MaxVisualizationPayloadHistoryPoints; index++)
        {
            var sourceIndex = (int)Math.Round(index * lastIndex / (double)(MaxVisualizationPayloadHistoryPoints - 1));
            if (sourceIndex <= lastAddedIndex)
            {
                continue;
            }

            result.Add(_sensorHistory[sourceIndex]);
            lastAddedIndex = sourceIndex;
        }

        if (lastAddedIndex != lastIndex)
        {
            result.Add(_sensorHistory[lastIndex]);
        }

        return result.ToArray();
    }

    private VisualizationSnapshot BuildVisualizationSnapshot(DateTime timestamp)
    {
        var sensors = Sensors.Where(SensorReadingAvailability.IsDisplayable).ToList();
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
        foreach (var sensor in Sensors.Where(SensorReadingAvailability.IsDisplayable))
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

        return key;
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
        if (NewCurveHoverReadoutText is null)
        {
            return;
        }

        List<FanCurvePoint>? validPoints = null;
        try
        {
            validPoints = ReadNewCurvePoints();
            NewCurveHoverReadoutText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1:0.#}-{2:0.#} {3} · {4:0.#}-{5:0.#}%",
                F("Preset.CurveSubtitle", validPoints.Count),
                validPoints[0].TemperatureCelsius,
                validPoints[^1].TemperatureCelsius,
                T("SensorUnit.Celsius"),
                validPoints.Min(point => point.FanPercent),
                validPoints.Max(point => point.FanPercent));
        }
        catch (Exception ex)
        {
            NewCurveHoverReadoutText.Text = F("Status.CurvePreviewInvalid", ex.Message);
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

        DrawCurveChartGrid(
            NewCurveCanvas,
            width,
            height,
            TemperatureCurveCanvasMinCelsius,
            TemperatureCurveCanvasMaxCelsius,
            "#FF0F766E");

        var points = NewCurvePoints
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
                for (var index = 0; index < CurvePreviewSampleCount; index++)
                {
                    var ratio = index / (double)(CurvePreviewSampleCount - 1);
                    var temperature = first + ((last - first) * ratio);
                    linePoints.Add(new Point(ToX(temperature), ToY(previewPreset.CalculateFanPercentValue(temperature))));
                }
            }
            else
            {
                foreach (var point in validPoints)
                {
                    linePoints.Add(new Point(ToX(point.TemperatureCelsius), ToY(point.FanPercent)));
                }
            }

            var areaPoints = new PointCollection
            {
                new(linePoints[0].X, ToY(0)),
            };
            foreach (var linePoint in linePoints)
            {
                areaPoints.Add(linePoint);
            }

            areaPoints.Add(new Point(linePoints[^1].X, ToY(0)));
            NewCurveCanvas.Children.Add(new Polygon
            {
                Points = areaPoints,
                Fill = ToBrush("#2414B8A6"),
                IsHitTestVisible = false,
            });
            NewCurveCanvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = ToBrush("#FF0F766E"),
                StrokeThickness = 3.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            });
        }

        foreach (var point in points)
        {
            if (ReferenceEquals(point, _draggingTemperatureCurvePoint))
            {
                var selectionRing = new Ellipse
                {
                    Width = 24,
                    Height = 24,
                    Fill = ToBrush("#0014B8A6"),
                    Stroke = ToBrush("#6614B8A6"),
                    StrokeThickness = 4,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(selectionRing, ToX(point.TemperatureCelsius) - 12);
                Canvas.SetTop(selectionRing, ToY(point.FanPercent) - 12);
                NewCurveCanvas.Children.Add(selectionRing);
            }

            var marker = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = ToBrush("#FF14B8A6"),
                Stroke = ToBrush("#FFFFFFFF"),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(marker, ToX(point.TemperatureCelsius) - 7);
            Canvas.SetTop(marker, ToY(point.FanPercent) - 7);
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
        if (NewPowerCurveHoverReadoutText is null)
        {
            return;
        }

        List<FanCurvePoint>? validPoints = null;
        try
        {
            validPoints = ReadNewPowerCurvePoints();
            NewPowerCurveHoverReadoutText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1:0.#}-{2:0.#} {3} · {4:0.#}-{5:0.#}%",
                F("Preset.PowerCurveSubtitle", validPoints.Count),
                validPoints[0].PowerWatts,
                validPoints[^1].PowerWatts,
                T("SensorUnit.Watts"),
                validPoints.Min(point => point.FanPercent),
                validPoints.Max(point => point.FanPercent));
        }
        catch (Exception ex)
        {
            NewPowerCurveHoverReadoutText.Text = F("Status.CurvePreviewInvalid", ex.Message);
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

        DrawCurveChartGrid(
            NewPowerCurveCanvas,
            width,
            height,
            PowerCurveCanvasMinWatts,
            PowerCurveCanvasMaxWatts,
            "#FF2563EB");

        var points = NewPowerCurvePoints
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
                for (var index = 0; index < CurvePreviewSampleCount; index++)
                {
                    var ratio = index / (double)(CurvePreviewSampleCount - 1);
                    var powerWatts = first + ((last - first) * ratio);
                    linePoints.Add(new Point(ToX(powerWatts), ToY(previewPreset.CalculateFanPercentValueForPower(powerWatts))));
                }
            }
            else
            {
                foreach (var point in validPoints)
                {
                    linePoints.Add(new Point(ToX(point.PowerWatts), ToY(point.FanPercent)));
                }
            }

            var areaPoints = new PointCollection
            {
                new(linePoints[0].X, ToY(0)),
            };
            foreach (var linePoint in linePoints)
            {
                areaPoints.Add(linePoint);
            }

            areaPoints.Add(new Point(linePoints[^1].X, ToY(0)));
            NewPowerCurveCanvas.Children.Add(new Polygon
            {
                Points = areaPoints,
                Fill = ToBrush("#243B82F6"),
                IsHitTestVisible = false,
            });
            NewPowerCurveCanvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = ToBrush("#FF2563EB"),
                StrokeThickness = 3.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            });
        }

        foreach (var point in points)
        {
            if (ReferenceEquals(point, _draggingPowerCurvePoint))
            {
                var selectionRing = new Ellipse
                {
                    Width = 24,
                    Height = 24,
                    Fill = ToBrush("#003B82F6"),
                    Stroke = ToBrush("#663B82F6"),
                    StrokeThickness = 4,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(selectionRing, ToX(point.PowerWatts) - 12);
                Canvas.SetTop(selectionRing, ToY(point.FanPercent) - 12);
                NewPowerCurveCanvas.Children.Add(selectionRing);
            }

            var marker = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = ToBrush("#FF3B82F6"),
                Stroke = ToBrush("#FFFFFFFF"),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(marker, ToX(point.PowerWatts) - 7);
            Canvas.SetTop(marker, ToY(point.FanPercent) - 7);
            NewPowerCurveCanvas.Children.Add(marker);
        }

        DrawPowerCurveHoverOverlay(width, height);
    }

    private static void DrawCurveChartGrid(
        Canvas canvas,
        double width,
        double height,
        double minimumInput,
        double maximumInput,
        string accentHex)
    {
        var plotLeft = CurveCanvasPadding;
        var plotTop = CurveCanvasPadding;
        var plotRight = width - CurveCanvasPadding;
        var plotBottom = height - CurveCanvasPadding;
        var plotWidth = Math.Max(1, plotRight - plotLeft);
        var plotHeight = Math.Max(1, plotBottom - plotTop);
        var accentBrush = ToBrush(accentHex);
        var gridBrush = ToBrush("#243B526B");
        var majorGridBrush = ToBrush("#3A3B526B");

        var plotBackground = new Rectangle
        {
            Width = plotWidth,
            Height = plotHeight,
            Fill = ToBrush("#080F172A"),
            Stroke = ToBrush("#303B526B"),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(plotBackground, plotLeft);
        Canvas.SetTop(plotBackground, plotTop);
        canvas.Children.Add(plotBackground);

        const int horizontalIntervals = 4;
        for (var index = 0; index <= horizontalIntervals; index++)
        {
            var ratio = index / (double)horizontalIntervals;
            var y = plotTop + (plotHeight * ratio);
            canvas.Children.Add(new Line
            {
                X1 = plotLeft,
                X2 = plotRight,
                Y1 = y,
                Y2 = y,
                Stroke = index is 0 or horizontalIntervals ? majorGridBrush : gridBrush,
                StrokeThickness = index is 0 or horizontalIntervals ? 1.2 : 1,
                IsHitTestVisible = false,
            });
            DrawCurveAxisLabel(canvas, $"{100 - (index * 25)}%", 3, y - 8);
        }

        const int verticalIntervals = 5;
        for (var index = 0; index <= verticalIntervals; index++)
        {
            var ratio = index / (double)verticalIntervals;
            var x = plotLeft + (plotWidth * ratio);
            canvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = plotTop,
                Y2 = plotBottom,
                Stroke = index is 0 or verticalIntervals ? majorGridBrush : gridBrush,
                StrokeThickness = index is 0 or verticalIntervals ? 1.2 : 1,
                IsHitTestVisible = false,
            });
            var input = minimumInput + ((maximumInput - minimumInput) * ratio);
            DrawCurveAxisLabel(
                canvas,
                input.ToString("0", CultureInfo.CurrentCulture),
                Math.Clamp(x - 18, 2, Math.Max(2, width - 40)),
                plotBottom + 5);
        }

        canvas.Children.Add(new Line
        {
            X1 = plotLeft,
            X2 = plotRight,
            Y1 = plotBottom,
            Y2 = plotBottom,
            Stroke = accentBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        });
    }

    private static void DrawCurveAxisLabel(Canvas canvas, string text, double left, double top)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = ToBrush("#B56B7280"),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private void DrawTemperatureCurveHoverOverlay(double width, double height)
    {
        if (_temperatureCurveHoverPosition is not { } position)
        {
            return;
        }

        var temperature = FromCanvasX(position.X, width, TemperatureCurveCanvasMinCelsius, TemperatureCurveCanvasMaxCelsius);
        try
        {
            var previewPreset = new FanPreset
            {
                Kind = FanPreset.CurveKind,
                CurvePoints = ReadNewCurvePoints(),
                SmoothCurve = NewCurveSmoothSwitch.IsOn,
            };
            var percent = previewPreset.CalculateFanPercentValue(temperature);
            NewCurveHoverReadoutText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1:0.0} {2} · {3} {4:0.#}%",
                T("Dashboard.TypeTemperature"),
                temperature,
                T("SensorUnit.Celsius"),
                GetCurveHoverFanSpeedLabel(),
                percent);
        }
        catch (Exception ex)
        {
            NewCurveHoverReadoutText.Text = F("Status.CurvePreviewInvalid", ex.Message);
        }

        DrawCurveHoverOverlay(
            NewCurveCanvas,
            position,
            width,
            height,
            "#FF0F766E");
    }

    private void DrawPowerCurveHoverOverlay(double width, double height)
    {
        if (_powerCurveHoverPosition is not { } position)
        {
            return;
        }

        var powerWatts = FromCanvasX(position.X, width, PowerCurveCanvasMinWatts, PowerCurveCanvasMaxWatts);
        try
        {
            var previewPreset = new FanPreset
            {
                Kind = FanPreset.PowerCurveKind,
                CurvePoints = ReadNewPowerCurvePoints(),
                SmoothCurve = NewPowerCurveSmoothSwitch.IsOn,
            };
            var percent = previewPreset.CalculateFanPercentValueForPower(powerWatts);
            NewPowerCurveHoverReadoutText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1:0} {2} · {3} {4:0.#}%",
                T("SensorDisplay.PowerConsumption"),
                powerWatts,
                T("SensorUnit.Watts"),
                GetCurveHoverFanSpeedLabel(),
                percent);
        }
        catch (Exception ex)
        {
            NewPowerCurveHoverReadoutText.Text = F("Status.CurvePreviewInvalid", ex.Message);
        }

        DrawCurveHoverOverlay(
            NewPowerCurveCanvas,
            position,
            width,
            height,
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
            IsHitTestVisible = false,
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
            IsHitTestVisible = false,
        });
        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = guideBrush,
            Stroke = ToBrush("#FFFFFFFF"),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marker, x - 4);
        Canvas.SetTop(marker, y - 4);
        canvas.Children.Add(marker);
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
        AddCommandLog(e);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                AddVolatileCommandLog(e);
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
        }))
        {
            throw new InvalidOperationException("ipmitool 命令已经完成并写入日志，但无法将命令明细加入界面线程队列。");
        }
    }

    private void OnAppLogWriteFailed(object? sender, Exception ex)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ShowAppLogWriteFailure(ex);
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => ShowAppLogWriteFailure(ex)))
        {
            throw new InvalidOperationException(F("Status.LogWriteFailed", ex.Message), ex);
        }
    }

    private void ShowAppLogWriteFailure(Exception ex)
    {
        var message = F("Status.LogWriteFailed", ex.Message);
        AddVolatileLog(T("Log.Error"), message);
        ShowStatus(message, InfoBarSeverity.Error);
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

    private void ClearStaleFailureStatusAfterSensorRefreshSuccess()
    {
        if (StatusInfoBar.Severity != InfoBarSeverity.Error)
        {
            return;
        }

        StatusInfoBar.IsOpen = false;
    }

    private void AddCommandLog(CommandTraceEventArgs e)
    {
        var level = e.Succeeded ? T("Log.Ok") : T("Log.Fail");
        var message = $"{e.CommandLine} [{e.ExitCode}] {e.Elapsed.TotalSeconds:0.0}s";
        _appLog.Write(new AppLogRecord
        {
            Timestamp = e.FinishedAt,
            Level = e.Succeeded ? "Info" : "Error",
            Category = "IpmiCommand",
            EventName = "CommandCompleted",
            Message = message,
            StartedAt = e.StartedAt,
            FinishedAt = e.FinishedAt,
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

    private void AddVolatileCommandLog(CommandTraceEventArgs e)
    {
        var level = e.Succeeded ? T("Log.Ok") : T("Log.Fail");
        var message = $"{e.CommandLine} [{e.ExitCode}] {e.Elapsed.TotalSeconds:0.0}s";
        AddVolatileLog(level, message);
    }

    private async Task WriteDurableUiLogAsync(
        string level,
        string message,
        string category = "Application",
        string eventName = "UiLog",
        IReadOnlyDictionary<string, string>? properties = null)
    {
        _appLog.Write(new AppLogRecord
        {
            Level = NormalizeLogLevel(level),
            Category = category,
            EventName = eventName,
            Message = message,
            Properties = MergeLogProperties(level, properties),
        });
        await FlushAppLogAsync();
        AddVolatileLog(level, message);
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
        var entry = new LogEntry
        {
            Level = level,
            SemanticLevel = GetDisplayLogSemanticLevel(level),
            Message = message,
        };

        _volatileLogEntries.Insert(0, entry);
        while (_volatileLogEntries.Count > 80)
        {
            _volatileLogEntries.RemoveAt(_volatileLogEntries.Count - 1);
        }

        if (!IsOverviewViewVisible())
        {
            _volatileLogsDirty = true;
            return;
        }

        Logs.Insert(0, entry);
        while (Logs.Count > 80)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void RefreshVisibleLogsIfDirty()
    {
        if (!_volatileLogsDirty || !IsOverviewViewVisible())
        {
            return;
        }

        Logs.Clear();
        foreach (var entry in _volatileLogEntries)
        {
            Logs.Add(entry);
        }

        _volatileLogsDirty = false;
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
        PowerCurveSectionTitleText.Text = T("Control.PowerCurvePresetPoints");
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
        if (string.Equals(key, "Mode.SmartStarting", StringComparison.Ordinal))
        {
            return $"{T("Hero.RequestShortRunning")} · {T("Mode.SmartAuto")}";
        }

        if (string.Equals(key, "Mode.CurveStarting", StringComparison.Ordinal))
        {
            var modeText = args.Length == 0 ? string.Empty : F("Mode.CurveAuto", args);
            return string.IsNullOrWhiteSpace(modeText)
                ? T("Hero.RequestShortRunning")
                : $"{T("Hero.RequestShortRunning")} · {modeText}";
        }

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

        if (string.Equals(key, "Mode.SmartStarting", StringComparison.Ordinal))
        {
            var modeName = isSimplifiedChinese ? HeroThermalSmartPolicyChinese : T("Control.SmartAutoPolicy");
            return $"{T("Hero.RequestShortRunning")} · {modeName}";
        }

        if (string.Equals(key, "Mode.SmartAuto", StringComparison.Ordinal) ||
            string.Equals(key, "Mode.SmartPercent", StringComparison.Ordinal))
        {
            var modeName = isSimplifiedChinese ? HeroThermalSmartPolicyChinese : T("Control.SmartAutoPolicy");
            return args.Count > 0 ? $"{modeName} ({args[0]}%)" : modeName;
        }

        if (string.Equals(key, "Mode.CurveStarting", StringComparison.Ordinal))
        {
            return $"{T("Hero.RequestShortRunning")} · {FormatHeroCurveThermalModeText(args, isSimplifiedChinese)}";
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

    private void SetAutoPolicyPendingSummary()
    {
        _autoPolicyRunning = false;
        AutoPolicySummaryText.Text = T("Hero.RequestShortRunning");
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

    private async Task CheckSensorPollingLatency(TimeSpan elapsed)
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
            await WriteDurableUiLogAsync(T("Log.Info"), message);
            ShowStatus(message, InfoBarSeverity.Success);
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
        if (IndividualFanStatusPanel is null)
        {
            return;
        }

        var isEnabled = _settings.EnableIndividualFanTargets;
        var safetyMessage = T(isEnabled
            ? "Control.IndividualEnabledWarning"
            : "Control.IndividualDisabledWarning");
        var statusBrush = ToBrush(isEnabled ? "#FFD97706" : "#FF6B7280");

        IndividualFanStatusToggle.IsOn = isEnabled;
        IndividualFanStatusIcon.Glyph = isEnabled ? "\uE7BA" : "\uE72E";
        IndividualFanStatusIcon.Foreground = statusBrush;
        IndividualFanRiskButton.Foreground = statusBrush;
        ToolTipService.SetToolTip(IndividualFanRiskButton, safetyMessage);
        ToolTipService.SetToolTip(IndividualFanSettingsButton, T("Nav.Settings"));
        AutomationProperties.SetName(IndividualFanRiskButton, safetyMessage);
        AutomationProperties.SetHelpText(IndividualFanStatusPanel, safetyMessage);
    }

    private void OnOpenIndividualFanSettingsClick(object sender, RoutedEventArgs e)
    {
        SelectView("Settings");
        IndividualFanSwitch.Focus(FocusState.Programmatic);
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

    private enum AutoPolicyTickFailureStage
    {
        Starting,
        ReadSensors,
        UpdateSensorState,
        ApplyFanCommand,
        RefreshAfterFanCommand,
        RefreshAfterEmergencyDellAuto,
    }

    private sealed class AutoPolicyTransientSensorReadException : Exception
    {
        public AutoPolicyTransientSensorReadException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }

    private sealed record VisualizationHistoryCorruptRow(int LineNumber, string Message);

    private sealed class SensorDashboardHistoryPoint
    {
        public string Id { get; set; } = string.Empty;

        public string Time { get; set; } = string.Empty;

        public string Timestamp { get; set; } = string.Empty;

        public long UnixMilliseconds { get; set; }

        public VisualizationSummary? Summary { get; set; }

        public VisualizationCurrent? Current { get; set; }

        [JsonIgnore]
        public object[] TypeCounts { get; set; } = [];

        [JsonIgnore]
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
