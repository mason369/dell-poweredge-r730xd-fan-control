using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace DellR730xdFanControlCenter.Controls;

public sealed class DashboardSensorIconVisualFailureEventArgs : EventArgs
{
    public DashboardSensorIconVisualFailureEventArgs(Exception exception)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public Exception Exception { get; }
}

public sealed partial class DashboardSensorIcon : UserControl
{
    private const double StaleIconOpacity = 0.65;
    private const float OuterRingBaseOpacity = 0.28f;
    private const float OuterRingPulseOpacity = 0.6f;
    private const double NormalizedLevelEpsilon = 0.000001;
    private const double FanPeriodEpsilon = 0.000001;

    private static readonly DependencyProperty EffectiveAccentBrushProperty = DependencyProperty.Register(
        nameof(EffectiveAccentBrush),
        typeof(Brush),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(null));

    private readonly SolidColorBrush _systemForegroundBrush = new();
    private readonly UIElement[] _primaryGroups;
    private readonly UIElement[] _badgeGroups;

    private ThemeSettings? _themeSettings;
    private UISettings? _uiSettings;
    private AppWindow? _appWindow;
    private Compositor? _compositor;

    private Visual? _fanVisual;
    private Visual? _temperatureLevelVisual;
    private Visual? _cpuUsageLevelVisual;
    private Visual? _memoryUsageLevelVisual;
    private Visual? _ioUsageLevelVisual;
    private Visual? _systemUsageLevelVisual;
    private Visual[] _levelVisuals = [];
    private Visual? _voltageNeedleVisual;
    private Visual? _currentFlowVisual;
    private Visual? _powerActivityVisual;
    private Visual? _outerRingVisual;

    private AnimationController? _fanAnimationController;
    private AnimationController? _currentTranslationController;
    private AnimationController? _currentOpacityController;
    private AnimationController? _powerOpacityController;
    private AnimationController? _alertOpacityController;

    private bool _isInitialized;
    private bool _isLoaded;
    private bool _isHighContrast;
    private bool _animationsEnabled = true;
    private bool _isWindowVisible = true;
    private bool _visualUpdateQueued;
    private bool _visualUpdatePending;
    private bool _fanAnimationPaused;
    private bool _currentFlowPaused;
    private bool _powerActivityPaused;
    private bool _alertPulsePaused;
    private bool _hasAppliedNormalizedLevel;
    private long _lifecycleGeneration;
    private long _queuedVisualUpdateGeneration;
    private double _activeFanPeriodSeconds = double.NaN;
    private double _lastNormalizedLevel;
    private DashboardIconKind _lastNormalizedIconKind = DashboardIconKind.GenericStatus;

    public event EventHandler<DashboardSensorIconVisualFailureEventArgs>? VisualUpdateFailed;

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind),
        typeof(DashboardIconKind),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(DashboardIconKind.GenericStatus, OnVisualPropertyChanged));

    public static readonly DependencyProperty VisualStateProperty = DependencyProperty.Register(
        nameof(VisualState),
        typeof(DashboardVisualState),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(DashboardVisualState.Unavailable, OnVisualPropertyChanged));

    public static readonly DependencyProperty MotionKindProperty = DependencyProperty.Register(
        nameof(MotionKind),
        typeof(DashboardMotionKind),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(DashboardMotionKind.None, OnVisualPropertyChanged));

    public static readonly DependencyProperty NormalizedLevelProperty = DependencyProperty.Register(
        nameof(NormalizedLevel),
        typeof(double),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty MotionPeriodSecondsProperty = DependencyProperty.Register(
        nameof(MotionPeriodSeconds),
        typeof(double),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsMotionActiveProperty = DependencyProperty.Register(
        nameof(IsMotionActive),
        typeof(bool),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsDataFreshProperty = DependencyProperty.Register(
        nameof(IsDataFresh),
        typeof(bool),
        typeof(DashboardSensorIcon),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public DashboardSensorIcon()
    {
        InitializeComponent();
        _primaryGroups =
        [
            TemperatureGroup,
            FanGroup,
            CpuUsageGroup,
            MemoryUsageGroup,
            IoUsageGroup,
            SystemUsageGroup,
            PowerGroup,
            VoltageGroup,
            CurrentGroup,
            IntrusionGroup,
            FanRedundancyGroup,
            PowerRedundancyGroup,
            CmosBatteryGroup,
            RombBatteryGroup,
            UsbOverCurrentGroup,
            PowerPolicyGroup,
            StorageDriveGroup,
            RaidControllerGroup,
            StorageCacheGroup,
            GenericStatusGroup,
        ];
        _badgeGroups =
        [
            NormalBadgeGroup,
            InformationBadgeGroup,
            InactiveBadgeGroup,
            UnavailableBadgeGroup,
            WarningBadgeGroup,
            CriticalBadgeGroup,
            StaleBadgeGroup,
        ];

        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
        _isInitialized = true;
        EffectiveAccentBrush = AccentBrush;
        ApplyPresentationVisibility();
    }

    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush? EffectiveAccentBrush
    {
        get => (Brush?)GetValue(EffectiveAccentBrushProperty);
        private set => SetValue(EffectiveAccentBrushProperty, value);
    }

    public DashboardIconKind IconKind
    {
        get => (DashboardIconKind)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public DashboardVisualState VisualState
    {
        get => (DashboardVisualState)GetValue(VisualStateProperty);
        set => SetValue(VisualStateProperty, value);
    }

    public DashboardMotionKind MotionKind
    {
        get => (DashboardMotionKind)GetValue(MotionKindProperty);
        set => SetValue(MotionKindProperty, value);
    }

    public double NormalizedLevel
    {
        get => (double)GetValue(NormalizedLevelProperty);
        set => SetValue(NormalizedLevelProperty, value);
    }

    public double MotionPeriodSeconds
    {
        get => (double)GetValue(MotionPeriodSecondsProperty);
        set => SetValue(MotionPeriodSecondsProperty, value);
    }

    public bool IsMotionActive
    {
        get => (bool)GetValue(IsMotionActiveProperty);
        set => SetValue(IsMotionActiveProperty, value);
    }

    public bool IsDataFresh
    {
        get => (bool)GetValue(IsDataFreshProperty);
        set => SetValue(IsDataFreshProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not DashboardSensorIcon control || !control._isInitialized)
        {
            return;
        }

        control._visualUpdatePending = true;
        if (!control._isLoaded)
        {
            return;
        }

        control.QueueVisualUpdate();
    }

    private void OnControlLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _lifecycleGeneration++;

        var xamlRoot = XamlRoot ?? throw new InvalidOperationException("DashboardSensorIcon requires a XamlRoot when loaded.");
        var windowId = xamlRoot.ContentIslandEnvironment.AppWindowId;

        _themeSettings = ThemeSettings.CreateForWindowId(windowId);
        _themeSettings.Changed += OnThemeSettingsChanged;

        _uiSettings = new UISettings();
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;

        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Changed += OnAppWindowChanged;
        _isWindowVisible = _appWindow.IsVisible;

        RefreshSystemSettings();
        AcquireCompositionResources();
        if (_visualUpdatePending)
        {
            _visualUpdatePending = false;
        }

        ApplyVisuals();
    }

    private void OnControlUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _lifecycleGeneration++;
        _visualUpdatePending = true;

        if (_themeSettings is not null)
        {
            _themeSettings.Changed -= OnThemeSettingsChanged;
        }

        if (_uiSettings is not null)
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        }

        if (_appWindow is not null)
        {
            _appWindow.Changed -= OnAppWindowChanged;
        }

        StopAllCompositionAnimations();
        ReleaseCompositionResources();

        _themeSettings = null;
        _uiSettings = null;
        _appWindow = null;
    }

    private void OnThemeSettingsChanged(ThemeSettings sender, object args)
    {
        QueueLifecycleUpdate(() =>
        {
            RefreshSystemSettings();
            ApplyVisuals();
        });
    }

    private void OnAnimationsEnabledChanged(UISettings sender, object args)
    {
        QueueLifecycleUpdate(() =>
        {
            RefreshSystemSettings();
            ApplyVisuals();
        });
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange)
        {
            return;
        }

        QueueLifecycleUpdate(() =>
        {
            _isWindowVisible = _appWindow?.IsVisible
                ?? throw new InvalidOperationException("DashboardSensorIcon AppWindow is unavailable while loaded.");
            ApplyVisuals();
        });
    }

    private void QueueLifecycleUpdate(Action update)
    {
        if (!_isLoaded)
        {
            return;
        }

        var generation = _lifecycleGeneration;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!_isLoaded || generation != _lifecycleGeneration)
                {
                    return;
                }

                update();
            }
            catch (Exception ex)
            {
                ReportVisualUpdateFailure(ex);
            }
        }) &&
            _isLoaded &&
            generation == _lifecycleGeneration)
        {
            throw new InvalidOperationException("Unable to queue a dashboard sensor lifecycle update.");
        }
    }

    private void QueueVisualUpdate()
    {
        if (!_isLoaded)
        {
            _visualUpdatePending = true;
            return;
        }

        var generation = _lifecycleGeneration;
        if (_visualUpdateQueued && _queuedVisualUpdateGeneration == generation)
        {
            return;
        }

        _visualUpdateQueued = true;
        _queuedVisualUpdateGeneration = generation;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_queuedVisualUpdateGeneration == generation)
                {
                    _visualUpdateQueued = false;
                }

                if (!_isLoaded || generation != _lifecycleGeneration)
                {
                    return;
                }

                _visualUpdatePending = false;
                ApplyVisuals();
            }
            catch (Exception ex)
            {
                ReportVisualUpdateFailure(ex);
            }
        }))
        {
            if (!_isLoaded || generation != _lifecycleGeneration)
            {
                return;
            }

            _visualUpdateQueued = false;
            throw new InvalidOperationException("Unable to queue the dashboard sensor visual update.");
        }
    }

    private void ReportVisualUpdateFailure(Exception exception)
    {
        var handler = VisualUpdateFailed;
        if (handler is null)
        {
            throw new InvalidOperationException("Dashboard sensor visual update failed without an error handler.", exception);
        }

        handler(this, new DashboardSensorIconVisualFailureEventArgs(exception));
    }

    private void RefreshSystemSettings()
    {
        if (_themeSettings is null || _uiSettings is null)
        {
            throw new InvalidOperationException("DashboardSensorIcon system settings are unavailable while loaded.");
        }

        _isHighContrast = _themeSettings.HighContrast;
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        if (_isHighContrast)
        {
            _systemForegroundBrush.Color = _uiSettings.GetColorValue(UIColorType.Foreground);
        }
    }

    private void AcquireCompositionResources()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(CurrentFlowMarker, true);

        _fanVisual = ElementCompositionPreview.GetElementVisual(FanRotor);
        _temperatureLevelVisual = ElementCompositionPreview.GetElementVisual(TemperatureLevel);
        _cpuUsageLevelVisual = ElementCompositionPreview.GetElementVisual(CpuUsageLevel);
        _memoryUsageLevelVisual = ElementCompositionPreview.GetElementVisual(MemoryUsageLevel);
        _ioUsageLevelVisual = ElementCompositionPreview.GetElementVisual(IoUsageLevel);
        _systemUsageLevelVisual = ElementCompositionPreview.GetElementVisual(SystemUsageLevel);
        _voltageNeedleVisual = ElementCompositionPreview.GetElementVisual(VoltageNeedle);
        _currentFlowVisual = ElementCompositionPreview.GetElementVisual(CurrentFlowMarker);
        _powerActivityVisual = ElementCompositionPreview.GetElementVisual(PowerActivityElement);
        _outerRingVisual = ElementCompositionPreview.GetElementVisual(OuterRing);

        _levelVisuals =
        [
            _temperatureLevelVisual,
            _cpuUsageLevelVisual,
            _memoryUsageLevelVisual,
            _ioUsageLevelVisual,
            _systemUsageLevelVisual,
        ];
        _compositor = _fanVisual.Compositor;

        _fanVisual.CenterPoint = new Vector3(12, 12, 0);
        _temperatureLevelVisual.CenterPoint = new Vector3((float)(TemperatureLevel.Width / 2), (float)TemperatureLevel.Height, 0);
        _cpuUsageLevelVisual.CenterPoint = new Vector3((float)(CpuUsageLevel.Width / 2), (float)CpuUsageLevel.Height, 0);
        _memoryUsageLevelVisual.CenterPoint = new Vector3((float)(MemoryUsageLevel.Width / 2), (float)MemoryUsageLevel.Height, 0);
        _ioUsageLevelVisual.CenterPoint = new Vector3((float)(IoUsageLevel.Width / 2), (float)IoUsageLevel.Height, 0);
        _systemUsageLevelVisual.CenterPoint = new Vector3((float)(SystemUsageLevel.Width / 2), (float)SystemUsageLevel.Height, 0);
        _voltageNeedleVisual.CenterPoint = new Vector3(12, 15, 0);

        _hasAppliedNormalizedLevel = false;
    }

    private void ReleaseCompositionResources()
    {
        _compositor = null;
        _fanVisual = null;
        _temperatureLevelVisual = null;
        _cpuUsageLevelVisual = null;
        _memoryUsageLevelVisual = null;
        _ioUsageLevelVisual = null;
        _systemUsageLevelVisual = null;
        _levelVisuals = [];
        _voltageNeedleVisual = null;
        _currentFlowVisual = null;
        _powerActivityVisual = null;
        _outerRingVisual = null;

        _fanAnimationController = null;
        _currentTranslationController = null;
        _currentOpacityController = null;
        _powerOpacityController = null;
        _alertOpacityController = null;

        _fanAnimationPaused = false;
        _currentFlowPaused = false;
        _powerActivityPaused = false;
        _alertPulsePaused = false;
        _activeFanPeriodSeconds = double.NaN;
        _hasAppliedNormalizedLevel = false;
    }

    private void ApplyVisuals()
    {
        EffectiveAccentBrush = _isHighContrast ? _systemForegroundBrush : AccentBrush;
        ApplyPresentationVisibility();

        if (!_isLoaded)
        {
            _visualUpdatePending = true;
            return;
        }

        ApplyNormalizedLevel();
        UpdateContinuousAnimations();
    }

    private void ApplyPresentationVisibility()
    {
        foreach (var group in _primaryGroups)
        {
            group.Visibility = Visibility.Collapsed;
        }

        SelectPrimaryGroup().Visibility = Visibility.Visible;
        ApplyStatusBadge();
    }

    private UIElement SelectPrimaryGroup()
    {
        return IconKind switch
        {
            DashboardIconKind.Temperature => TemperatureGroup,
            DashboardIconKind.Fan => FanGroup,
            DashboardIconKind.CpuUsage => CpuUsageGroup,
            DashboardIconKind.MemoryUsage => MemoryUsageGroup,
            DashboardIconKind.IoUsage => IoUsageGroup,
            DashboardIconKind.SystemUsage => SystemUsageGroup,
            DashboardIconKind.Power => PowerGroup,
            DashboardIconKind.Voltage => VoltageGroup,
            DashboardIconKind.Current => CurrentGroup,
            DashboardIconKind.Intrusion => IntrusionGroup,
            DashboardIconKind.FanRedundancy => FanRedundancyGroup,
            DashboardIconKind.PowerRedundancy => PowerRedundancyGroup,
            DashboardIconKind.CmosBattery => CmosBatteryGroup,
            DashboardIconKind.RombBattery => RombBatteryGroup,
            DashboardIconKind.UsbOverCurrent => UsbOverCurrentGroup,
            DashboardIconKind.PowerPolicy => PowerPolicyGroup,
            DashboardIconKind.StorageDrive => StorageDriveGroup,
            DashboardIconKind.RaidController => RaidControllerGroup,
            DashboardIconKind.StorageCache => StorageCacheGroup,
            DashboardIconKind.GenericStatus => GenericStatusGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(IconKind), IconKind, "Unsupported dashboard icon kind."),
        };
    }

    private void ApplyStatusBadge()
    {
        foreach (var group in _badgeGroups)
        {
            group.Visibility = Visibility.Collapsed;
        }

        IconLayer.Opacity = !IsDataFresh && !_isHighContrast ? StaleIconOpacity : 1;
        if (!IsDataFresh)
        {
            StaleBadgeGroup.Visibility = Visibility.Visible;
            return;
        }

        var badge = VisualState switch
        {
            DashboardVisualState.Normal => NormalBadgeGroup,
            DashboardVisualState.Information => InformationBadgeGroup,
            DashboardVisualState.Inactive => InactiveBadgeGroup,
            DashboardVisualState.Unavailable => UnavailableBadgeGroup,
            DashboardVisualState.Warning => WarningBadgeGroup,
            DashboardVisualState.Critical => CriticalBadgeGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(VisualState), VisualState, "Unsupported dashboard visual state."),
        };
        badge.Visibility = Visibility.Visible;
    }

    private void ApplyNormalizedLevel()
    {
        if (!double.IsFinite(NormalizedLevel))
        {
            throw new InvalidOperationException("Dashboard sensor normalized level must be finite.");
        }

        var normalizedLevel = Math.Clamp(NormalizedLevel, 0, 1);
        var hasChanged = !_hasAppliedNormalizedLevel ||
                         _lastNormalizedIconKind != IconKind ||
                         Math.Abs(_lastNormalizedLevel - normalizedLevel) > NormalizedLevelEpsilon;
        var canAnimate = _isLoaded &&
                         _isWindowVisible &&
                         _animationsEnabled &&
                         !_isHighContrast &&
                         IsDataFresh &&
                         hasChanged &&
                         _hasAppliedNormalizedLevel &&
                         _lastNormalizedIconKind == IconKind;

        var levelVisual = GetLevelVisual(IconKind);
        if (levelVisual is not null)
        {
            SetLevelScale(
                levelVisual,
                normalizedLevel,
                animate: canAnimate &&
                         IsMotionActive &&
                         MotionKind == DashboardMotionKind.LevelTransition);
        }
        else if (IconKind == DashboardIconKind.Voltage)
        {
            if (MotionKind == DashboardMotionKind.GaugeTransition)
            {
                SetVoltageAngle(normalizedLevel, animate: canAnimate && IsMotionActive);
            }
            else
            {
                SetVoltageAngle(normalizedLevel, animate: false);
            }
        }

        _lastNormalizedLevel = normalizedLevel;
        _lastNormalizedIconKind = IconKind;
        _hasAppliedNormalizedLevel = true;
    }

    private Visual? GetLevelVisual(DashboardIconKind iconKind)
    {
        return iconKind switch
        {
            DashboardIconKind.Temperature => _temperatureLevelVisual,
            DashboardIconKind.CpuUsage => _cpuUsageLevelVisual,
            DashboardIconKind.MemoryUsage => _memoryUsageLevelVisual,
            DashboardIconKind.IoUsage => _ioUsageLevelVisual,
            DashboardIconKind.SystemUsage => _systemUsageLevelVisual,
            _ => null,
        };
    }

    private void SetLevelScale(Visual visual, double normalizedLevel, bool animate)
    {
        var targetScale = new Vector3(1, (float)normalizedLevel, 1);
        if (!animate)
        {
            visual.StopAnimation("Scale");
            visual.Scale = targetScale;
            return;
        }

        StartLevelTransition(visual, targetScale);
    }

    private void StartLevelTransition(Visual visual, Vector3 targetScale)
    {
        if (_compositor is null)
        {
            throw new InvalidOperationException("Dashboard sensor compositor is unavailable.");
        }

        var startScale = visual.Scale;
        visual.StopAnimation("Scale");
        visual.Scale = targetScale;

        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, startScale);
        animation.InsertKeyFrame(1, targetScale, _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0),
            new Vector2(0, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(240);
        visual.StartAnimation("Scale", animation);
    }

    private void SetVoltageAngle(double normalizedLevel, bool animate)
    {
        if (_voltageNeedleVisual is null)
        {
            return;
        }

        var targetAngle = -55f + ((float)normalizedLevel * 110f);
        if (!animate)
        {
            _voltageNeedleVisual.StopAnimation("RotationAngleInDegrees");
            _voltageNeedleVisual.RotationAngleInDegrees = targetAngle;
            return;
        }

        StartVoltageTransition(targetAngle);
    }

    private void StartVoltageTransition(float targetAngle)
    {
        if (_compositor is null || _voltageNeedleVisual is null)
        {
            throw new InvalidOperationException("Dashboard voltage composition resources are unavailable.");
        }

        var startAngle = _voltageNeedleVisual.RotationAngleInDegrees;
        _voltageNeedleVisual.StopAnimation("RotationAngleInDegrees");
        _voltageNeedleVisual.RotationAngleInDegrees = targetAngle;

        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, startAngle);
        animation.InsertKeyFrame(1, targetAngle, _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0),
            new Vector2(0, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(260);
        _voltageNeedleVisual.StartAnimation("RotationAngleInDegrees", animation);
    }

    private void UpdateContinuousAnimations()
    {
        if (!_animationsEnabled || _isHighContrast || !IsDataFresh)
        {
            PauseFanAnimation();
            StopCurrentFlowAnimation();
            StopPowerActivityAnimation();
            StopAlertPulseAnimation();
            return;
        }

        if (!_isWindowVisible)
        {
            PauseContinuousAnimations();
            return;
        }

        ResumeContinuousAnimations();
    }

    private void ResumeContinuousAnimations()
    {
        UpdateFanAnimation();
        UpdateCurrentFlowAnimation();
        UpdatePowerActivityAnimation();
        UpdateAlertPulseAnimation();
    }

    private void PauseContinuousAnimations()
    {
        PauseFanAnimation();
        PauseCurrentFlowAnimation();
        PausePowerActivityAnimation();
        PauseAlertPulseAnimation();
    }

    private void UpdateFanAnimation()
    {
        var hasFanIntent = IconKind == DashboardIconKind.Fan &&
                           IsMotionActive &&
                           MotionKind == DashboardMotionKind.FanRotation;
        if (!hasFanIntent)
        {
            StopFanAnimation();
            return;
        }

        if (_fanAnimationController is null)
        {
            StartFanAnimation(MotionPeriodSeconds);
        }
        else
        {
            SetFanPlaybackRate(MotionPeriodSeconds);
        }

        if (_animationsEnabled && !_isHighContrast && IsDataFresh && _isWindowVisible)
        {
            ResumeFanAnimation();
        }
        else
        {
            PauseFanAnimation();
        }
    }

    private void StartFanAnimation(double periodSeconds)
    {
        if (_compositor is null || _fanVisual is null)
        {
            throw new InvalidOperationException("Dashboard fan composition resources are unavailable.");
        }

        var playbackRate = GetFanPlaybackRate(periodSeconds);
        var fanAnimation = _compositor.CreateScalarKeyFrameAnimation();
        fanAnimation.InsertKeyFrame(0, 0);
        fanAnimation.InsertKeyFrame(1, 360, _compositor.CreateLinearEasingFunction());
        fanAnimation.Duration = TimeSpan.FromSeconds(1);
        fanAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        _fanAnimationController = _compositor.CreateAnimationController();
        _fanAnimationController.PlaybackRate = playbackRate;
        _activeFanPeriodSeconds = periodSeconds;
        _fanVisual.StartAnimation("RotationAngleInDegrees", fanAnimation, _fanAnimationController);
        _fanAnimationPaused = false;
    }

    private void SetFanPlaybackRate(double periodSeconds)
    {
        if (_fanAnimationController is null)
        {
            throw new InvalidOperationException("Fan animation controller is unavailable.");
        }

        if (double.IsFinite(_activeFanPeriodSeconds) &&
            Math.Abs(_activeFanPeriodSeconds - periodSeconds) <= FanPeriodEpsilon)
        {
            return;
        }

        var playbackRate = GetFanPlaybackRate(periodSeconds);
        _fanAnimationController.PlaybackRate = playbackRate;
        _activeFanPeriodSeconds = periodSeconds;
    }

    private static float GetFanPlaybackRate(double periodSeconds)
    {
        if (!double.IsFinite(periodSeconds) || periodSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds), periodSeconds, "Fan motion period must be finite and greater than zero.");
        }

        var playbackRate = 1f / (float)periodSeconds;
        if (!float.IsFinite(playbackRate) ||
            playbackRate < AnimationController.MinPlaybackRate ||
            playbackRate > AnimationController.MaxPlaybackRate)
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds), periodSeconds, "Fan playback rate is outside the controller range.");
        }

        return playbackRate;
    }

    private void PauseFanAnimation()
    {
        if (_fanAnimationController is null || _fanAnimationPaused)
        {
            return;
        }

        _fanAnimationController.Pause();
        _fanAnimationPaused = true;
    }

    private void ResumeFanAnimation()
    {
        if (_fanAnimationController is null || !_fanAnimationPaused)
        {
            return;
        }

        _fanAnimationController.Resume();
        _fanAnimationPaused = false;
    }

    private void StopFanAnimation()
    {
        if (_fanVisual is not null)
        {
            _fanVisual.StopAnimation("RotationAngleInDegrees");
            _fanVisual.RotationAngleInDegrees = 0;
        }

        _fanAnimationController = null;
        _fanAnimationPaused = false;
        _activeFanPeriodSeconds = double.NaN;
    }

    private void UpdateCurrentFlowAnimation()
    {
        var hasCurrentIntent = IconKind == DashboardIconKind.Current &&
                               IsMotionActive &&
                               MotionKind == DashboardMotionKind.CurrentFlow;
        if (!hasCurrentIntent || !_animationsEnabled || _isHighContrast || !IsDataFresh)
        {
            StopCurrentFlowAnimation();
            return;
        }

        if (!_isWindowVisible)
        {
            PauseCurrentFlowAnimation();
            return;
        }

        if (_currentTranslationController is null || _currentOpacityController is null)
        {
            StartCurrentFlowAnimation();
        }
        else
        {
            ResumeCurrentFlowAnimation();
        }
    }

    private void StartCurrentFlowAnimation()
    {
        if (_compositor is null || _currentFlowVisual is null)
        {
            throw new InvalidOperationException("Dashboard current-flow composition resources are unavailable.");
        }

        CurrentFlowMarker.Translation = Vector3.Zero;
        _currentFlowVisual.Opacity = 1;

        var translation = _compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new Vector3(-5, 0, 0));
        translation.InsertKeyFrame(1, new Vector3(12, 0, 0), _compositor.CreateLinearEasingFunction());
        translation.Duration = TimeSpan.FromSeconds(1.15);
        translation.IterationBehavior = AnimationIterationBehavior.Forever;

        var opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(0.2f, 1);
        opacity.InsertKeyFrame(0.8f, 1);
        opacity.InsertKeyFrame(1, 0);
        opacity.Duration = TimeSpan.FromSeconds(1.15);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        _currentTranslationController = _compositor.CreateAnimationController();
        _currentOpacityController = _compositor.CreateAnimationController();
        _currentFlowVisual.StartAnimation("Translation", translation, _currentTranslationController);
        _currentFlowVisual.StartAnimation("Opacity", opacity, _currentOpacityController);
        _currentFlowPaused = false;
    }

    private void PauseCurrentFlowAnimation()
    {
        if (_currentTranslationController is null ||
            _currentOpacityController is null ||
            _currentFlowPaused)
        {
            return;
        }

        _currentTranslationController.Pause();
        _currentOpacityController.Pause();
        _currentFlowPaused = true;
    }

    private void ResumeCurrentFlowAnimation()
    {
        if (_currentTranslationController is null ||
            _currentOpacityController is null ||
            !_currentFlowPaused)
        {
            return;
        }

        _currentTranslationController.Resume();
        _currentOpacityController.Resume();
        _currentFlowPaused = false;
    }

    private void StopCurrentFlowAnimation()
    {
        if (_currentFlowVisual is not null)
        {
            _currentFlowVisual.StopAnimation("Translation");
            _currentFlowVisual.StopAnimation("Opacity");
            CurrentFlowMarker.Translation = Vector3.Zero;
            _currentFlowVisual.Opacity = 1;
        }

        _currentTranslationController = null;
        _currentOpacityController = null;
        _currentFlowPaused = false;
    }

    private void UpdatePowerActivityAnimation()
    {
        var hasPowerIntent = IconKind == DashboardIconKind.Power &&
                             IsMotionActive &&
                             MotionKind == DashboardMotionKind.PowerActivity;
        if (!hasPowerIntent || !_animationsEnabled || _isHighContrast || !IsDataFresh)
        {
            StopPowerActivityAnimation();
            return;
        }

        if (!_isWindowVisible)
        {
            PausePowerActivityAnimation();
            return;
        }

        if (_powerOpacityController is null)
        {
            StartPowerActivityAnimation();
        }
        else
        {
            ResumePowerActivityAnimation();
        }
    }

    private void StartPowerActivityAnimation()
    {
        if (_compositor is null || _powerActivityVisual is null)
        {
            throw new InvalidOperationException("Dashboard power composition resources are unavailable.");
        }

        _powerActivityVisual.Opacity = 1;

        var opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 1);
        opacity.InsertKeyFrame(0.5f, 0.72f);
        opacity.InsertKeyFrame(1, 1);
        opacity.Duration = TimeSpan.FromSeconds(1.8);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        _powerOpacityController = _compositor.CreateAnimationController();
        _powerActivityVisual.StartAnimation("Opacity", opacity, _powerOpacityController);
        _powerActivityPaused = false;
    }

    private void PausePowerActivityAnimation()
    {
        if (_powerOpacityController is null || _powerActivityPaused)
        {
            return;
        }

        _powerOpacityController.Pause();
        _powerActivityPaused = true;
    }

    private void ResumePowerActivityAnimation()
    {
        if (_powerOpacityController is null || !_powerActivityPaused)
        {
            return;
        }

        _powerOpacityController.Resume();
        _powerActivityPaused = false;
    }

    private void StopPowerActivityAnimation()
    {
        if (_powerActivityVisual is not null)
        {
            _powerActivityVisual.StopAnimation("Opacity");
            _powerActivityVisual.Opacity = 1;
        }

        _powerOpacityController = null;
        _powerActivityPaused = false;
    }

    private void UpdateAlertPulseAnimation()
    {
        var hasAlertIntent = IsMotionActive &&
                             VisualState is DashboardVisualState.Warning or DashboardVisualState.Critical;
        if (!hasAlertIntent || !_animationsEnabled || _isHighContrast || !IsDataFresh)
        {
            StopAlertPulseAnimation();
            return;
        }

        if (!_isWindowVisible)
        {
            PauseAlertPulseAnimation();
            return;
        }

        if (_alertOpacityController is null)
        {
            StartAlertPulseAnimation();
        }
        else
        {
            ResumeAlertPulseAnimation();
        }
    }

    private void StartAlertPulseAnimation()
    {
        if (_compositor is null || _outerRingVisual is null)
        {
            throw new InvalidOperationException("Dashboard alert composition resources are unavailable.");
        }

        _outerRingVisual.Opacity = OuterRingBaseOpacity;

        var opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, OuterRingBaseOpacity);
        opacity.InsertKeyFrame(0.5f, OuterRingPulseOpacity);
        opacity.InsertKeyFrame(1, OuterRingBaseOpacity);
        opacity.Duration = TimeSpan.FromSeconds(1.9);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        _alertOpacityController = _compositor.CreateAnimationController();
        _outerRingVisual.StartAnimation("Opacity", opacity, _alertOpacityController);
        _alertPulsePaused = false;
    }

    private void PauseAlertPulseAnimation()
    {
        if (_alertOpacityController is null || _alertPulsePaused)
        {
            return;
        }

        _alertOpacityController.Pause();
        _alertPulsePaused = true;
    }

    private void ResumeAlertPulseAnimation()
    {
        if (_alertOpacityController is null || !_alertPulsePaused)
        {
            return;
        }

        _alertOpacityController.Resume();
        _alertPulsePaused = false;
    }

    private void StopAlertPulseAnimation()
    {
        if (_outerRingVisual is not null)
        {
            _outerRingVisual.StopAnimation("Opacity");
            _outerRingVisual.Opacity = _isHighContrast ? 1 : OuterRingBaseOpacity;
        }

        _alertOpacityController = null;
        _alertPulsePaused = false;
    }

    private void StopAllCompositionAnimations()
    {
        StopFanAnimation();

        foreach (var visual in _levelVisuals)
        {
            visual.StopAnimation("Scale");
        }

        if (_voltageNeedleVisual is not null)
        {
            _voltageNeedleVisual.StopAnimation("RotationAngleInDegrees");
        }

        StopCurrentFlowAnimation();
        StopPowerActivityAnimation();
        StopAlertPulseAnimation();
    }
}
