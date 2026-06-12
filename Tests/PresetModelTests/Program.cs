using DellR730xdFanControlCenter;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

var defaults = FanPreset.CreateDefaultPresets();
Require(defaults.Count >= 5, "Default preset list should include restore, balanced, cooling, performance, and Dell automatic presets.");
Require(new AppSettings().SensorRefreshSeconds == 1, "Default SDR polling interval should remain the original 1 second setting.");

var localizedSensorString = new SensorReading
{
    Key = "温度",
    SensorId = "72h",
    Value = "53 °C",
    Status = "正常",
}.ToString();
Require(
    localizedSensorString.Contains("温度", StringComparison.Ordinal) &&
    !localizedSensorString.Contains(nameof(SensorReading), StringComparison.Ordinal) &&
    !localizedSensorString.Contains("DellR730xdFanControlCenter", StringComparison.Ordinal),
    "Sensor rows should not expose the internal SensorReading type name through UI Automation.");

var restore = defaults.Single(preset => preset.Id == "restore-manual");
Require(restore.CanEditPercent, "Restore-manual preset percent should be editable.");

var balanced = defaults.Single(preset => preset.Id == "balanced");
var originalBalancedName = balanced.EditableName;
Require(!string.IsNullOrWhiteSpace(originalBalancedName), "Built-in presets should expose an editable name.");
Require(!string.IsNullOrWhiteSpace(balanced.EditableDetail), "Built-in presets should expose an editable description.");

balanced.EditableName = "夜间静音";
balanced.EditableDetail = "晚上降低噪音的自定义说明";
balanced.Percent = 18;

var clone = balanced.Clone();
Require(clone.EditableName == "夜间静音", "Edited built-in preset name should survive cloning.");
Require(clone.NameKey.Length == 0, "Editing a built-in preset name should stop forcing the built-in localization key.");
Require(clone.EditableDetail == "晚上降低噪音的自定义说明", "Edited built-in preset description should survive cloning.");
Require(clone.HasCustomDescription, "Edited built-in preset description should be marked as custom.");
Require(clone.Percent == 18, "Edited built-in preset percent should survive cloning.");

var curve = new FanPreset
{
    Kind = FanPreset.CurveKind,
    Name = "CPU curve",
    CurvePoints =
    [
        new FanCurvePoint { TemperatureCelsius = 45, FanPercent = 18 },
        new FanCurvePoint { TemperatureCelsius = 70, FanPercent = 35 },
        new FanCurvePoint { TemperatureCelsius = 84, FanPercent = 60 },
    ],
};
Require(curve.IsCurvePreset, "Temperature curve preset should identify itself as a curve.");
Require(!curve.CanEditPercent, "Temperature curve preset should not expose the single-percent editor as its source of truth.");
Require(curve.CalculateFanPercent(40) == 18, "Curve temperatures below the first point should use the first point percent.");
var currentTemperatureCelsius = 61;
Require(curve.CalculateFanPercent(currentTemperatureCelsius) == 29, "Current temperature readings should be evaluated at their position on the configured temperature curve.");
Require(curve.CalculateFanPercent(90) == 60, "Curve temperatures above the last point should use the last point percent.");
curve.SmoothCurve = true;
Require(curve.CalculateFanPercent(55) == 24, "Smooth curve mode should evaluate the same configured curve points with a softened position.");
curve.SmoothCurve = false;

curve.CurvePointsText = "50 = 20" + Environment.NewLine + "80 = 50";
curve.ApplyCurvePointsText();
Require(curve.CurvePoints.Count == 2, "Editable curve point text should replace the curve point list.");
Require(curve.CalculateFanPercent(65) == 35, "Edited curve point text should drive later current-temperature curve evaluation.");
curve.SmoothCurve = true;
Require(curve.CalculateFanPercent(60) == 28, "Edited curve points should still support smooth current-temperature curve evaluation.");

var powerCurve = new FanPreset
{
    Kind = FanPreset.PowerCurveKind,
    Name = "Power curve",
    CurvePoints =
    [
        new FanCurvePoint { PowerWatts = 280, FanPercent = 18 },
        new FanCurvePoint { PowerWatts = 500, FanPercent = 28 },
        new FanCurvePoint { PowerWatts = 750, FanPercent = 42 },
    ],
};
Require(powerCurve.IsPowerCurvePreset, "Power curve preset should identify itself as a power curve.");
Require(powerCurve.IsCurvePreset, "Power curve preset should still behave as a curve preset in the UI.");
Require(!powerCurve.CanEditPercent, "Power curve preset should not expose the single-percent editor as its source of truth.");
Require(powerCurve.CalculateFanPercentForPower(200) == 18, "Power below the first point should use the first power point percent.");
var currentPowerWatts = 412;
Require(powerCurve.CalculateFanPercentForPower(currentPowerWatts) == 24, "Current power readings should be evaluated at their position on the configured power curve.");
Require(powerCurve.CalculateFanPercentForPower(900) == 42, "Power above the last point should use the last power point percent.");
powerCurve.SmoothCurve = true;
Require(powerCurve.CalculateFanPercentForPower(350) == 20, "Power smooth curve mode should evaluate the same configured power points with a softened position.");
powerCurve.SmoothCurve = false;

powerCurve.CurvePointsText = "300W = 20" + Environment.NewLine + "700W = 50";
powerCurve.ApplyCurvePointsText();
Require(powerCurve.CurvePoints.Count == 2, "Editable power curve text should replace the power curve point list.");
Require(powerCurve.CalculateFanPercentForPower(500) == 35, "Edited power curve point text should drive later current-power curve evaluation.");
powerCurve.SmoothCurve = true;
Require(powerCurve.CalculateFanPercentForPower(400) == 25, "Edited power curve points should still support smooth current-power curve evaluation.");

var notifiedProperties = new List<string>();
var notifyingPoint = new FanCurvePoint();
notifyingPoint.PropertyChanged += (_, args) =>
{
    if (!string.IsNullOrWhiteSpace(args.PropertyName))
    {
        notifiedProperties.Add(args.PropertyName);
    }
};
notifyingPoint.TemperatureCelsius = 51;
notifyingPoint.PowerWatts = 410;
notifyingPoint.FanPercent = 24;
Require(
    notifiedProperties.Contains(nameof(FanCurvePoint.TemperatureCelsius), StringComparer.Ordinal) &&
    notifiedProperties.Contains(nameof(FanCurvePoint.PowerWatts), StringComparer.Ordinal) &&
    notifiedProperties.Contains(nameof(FanCurvePoint.FanPercent), StringComparer.Ordinal),
    "Curve point edits should notify bindings so right-side NumberBox values update while dragging chart points.");

var heroMetrics = HeroRealtimeMetrics.FromSensors(
[
    new SensorReading { Key = "Inlet Temp", Unit = "degrees C", NumericValue = 30 },
    new SensorReading { Key = "Exhaust Temp", Unit = "degrees C", NumericValue = 50 },
    new SensorReading { Key = "Temp 3.1", Unit = "degrees C", NumericValue = 60 },
    new SensorReading { Key = "Temp 3.2", Unit = "degrees C", NumericValue = 64 },
    new SensorReading { Key = "Fan1 RPM", Unit = "RPM", NumericValue = 3240 },
    new SensorReading { Key = "Fan2 RPM", Unit = "RPM", NumericValue = 3360 },
    new SensorReading { Key = "Fan3 RPM", Unit = "RPM", NumericValue = 3480 },
    new SensorReading { Key = "Fan4 RPM", Unit = "RPM", NumericValue = 3600 },
    new SensorReading { Key = "Fan5 RPM", Unit = "RPM", NumericValue = 3720 },
    new SensorReading { Key = "Fan6 RPM", Unit = "RPM", NumericValue = 3840 },
    new SensorReading { Key = "Pwr Consumption", Unit = "Watts", NumericValue = 784 },
    new SensorReading { Key = "1路电压", Unit = "Volts", NumericValue = 232 },
    new SensorReading { Key = "2路电压", Unit = "Volts", NumericValue = 234 },
    new SensorReading { Key = "1路电流", Unit = "Amps", NumericValue = 1.6 },
    new SensorReading { Key = "2路电流", Unit = "Amps", NumericValue = 1.8 },
]);
Require(heroMetrics.CurrentTemperatureCelsius == 51, "Hero live temperature should use the latest current average instead of the highest temperature.");
Require(heroMetrics.AverageFanRpm == 3540, "Hero live fan RPM should use the latest average fan speed.");
Require(heroMetrics.PowerWatts == 784, "Hero live power should use the latest power sensor reading.");
Require(heroMetrics.AverageVoltage == 233, "Hero live voltage should use the latest average voltage.");
Require(heroMetrics.TotalCurrent == 3.4, "Hero live current should use the latest total current.");
Require(heroMetrics.TemperatureItems.Count == 4, "Hero temperature details should include every current temperature sensor item.");
Require(heroMetrics.FanItems.Count == 6, "Hero fan details should include every fan instead of only the first three.");
Require(heroMetrics.HiddenTemperatureItemCount == 0, "Hero temperature details should not hide additional temperature sensors.");
Require(heroMetrics.HiddenFanItemCount == 0, "Hero fan details should not hide additional fans.");
Require(heroMetrics.PowerItems.Single().Key == "Pwr Consumption", "Hero power details should include the current power sensor item.");
Require(heroMetrics.VoltageItems.Count == 2, "Hero voltage details should include voltage rail items.");
Require(heroMetrics.CurrentItems.Count == 2, "Hero current details should include current rail items.");
Require(heroMetrics.HiddenPowerItemCount == 0, "Hero power details should not hide power sensors.");
Require(heroMetrics.HiddenVoltageItemCount == 0, "Hero voltage details should not hide voltage sensors.");
Require(heroMetrics.HiddenCurrentItemCount == 0, "Hero current details should not hide current sensors.");

RunSensorValueLocalizationChecks();
RunSensorDisplayNameLocalizationChecks();
RunSupportedLanguageCatalogChecks();
RunAllLanguageResourceCompletenessChecks();
RunLanguageSelectorXamlChecks();
RunVisibleXamlLocalizationCoverageChecks();
RunInfoBarAccessibilityLocalizationChecks();
RunPackageManifestLocalizationCoverageChecks();
RunDashboardHtmlLocalizationCoverageChecks();
RunRuntimeStatePersistenceSourceChecks();

LocalizationService.SetLanguage("zh-CN");
Require(LocalizationService.T("SensorValue.StateDeasserted") == "事件已解除", "State Deasserted must have a readable Chinese translation.");
Require(LocalizationService.T("SensorValue.StateAsserted") == "事件已触发", "State Asserted must have a readable Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ConnectionStatus")), "Hero connection label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ModeStatus")), "Hero mode label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestStatus")), "Hero request label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestRunning")), "Hero running request format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestSucceeded")), "Hero completed request format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.SensorRefreshSucceeded")), "Hero sensor refresh result format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LastUpdateValue")), "Hero last update format should have a Chinese translation.");
Require(LocalizationService.T("Status.PollingSkippedPreviousRunning").Contains("未启动新的 ipmitool", StringComparison.Ordinal), "Chinese skipped polling log should state that no new ipmitool process is started.");
Require(LocalizationService.T("Status.AutoTickSkippedIpmiBusy").Contains("未启动新的 ipmitool", StringComparison.Ordinal), "Chinese skipped auto tick log should state that no new ipmitool process is started.");
Require(LocalizationService.T("Status.IpmiCommandBusy").Contains("未启动新的 ipmitool", StringComparison.Ordinal), "Chinese busy IPMI error should state that no new ipmitool process is started.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveTemperature")), "Hero live temperature label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveFanRpm")), "Hero live fan RPM label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LivePower")), "Hero live power label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveVoltage")), "Hero live voltage label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveCurrent")), "Hero live current label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveWaiting")), "Hero live waiting value should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveMoreItems")), "Hero hidden item count format should have a Chinese translation.");
Require(LocalizationService.T("Overview.LivePower") == "实时功耗", "Overview live power should have a Chinese translation.");
Require(LocalizationService.T("Overview.AverageVoltage") == "平均电压", "Overview average voltage should have a Chinese translation.");
Require(LocalizationService.T("Overview.TotalCurrent") == "总电流", "Overview total current should have a Chinese translation.");
Require(LocalizationService.T("Action.RestoreDellFactoryFanSpeed").Contains("戴尔出厂设置转速", StringComparison.Ordinal), "Chinese factory restore action should name Dell factory fan speed.");
Require(LocalizationService.T("Action.StartPolling") == "开始轮询", "Chinese polling start action should describe starting sensor polling.");
Require(LocalizationService.T("Action.CancelPolling") == "取消轮询", "Chinese polling cancel action should describe canceling sensor polling.");
Require(LocalizationService.T("Tray.RestoreDefault").Contains("戴尔出厂设置转速", StringComparison.Ordinal), "Chinese tray restore action should name Dell factory fan speed.");
Require(LocalizationService.T("Tray.RefreshSensors") == "刷新传感器", "Chinese tray refresh action should be translated.");
Require(LocalizationService.T("Tray.OpenIdrac") == "打开远程管理网页", "Chinese tray iDRAC action should be translated.");
Require(LocalizationService.T("Tray.OpenLogs") == "打开日志文件夹", "Chinese tray logs action should be translated.");
Require(LocalizationService.T("Tray.StopAuto") == "停止自动策略", "Chinese tray stop-auto action should be translated.");
Require(LocalizationService.T("Control.IndividualEnabledWarning").Contains("0x00 不是 0% 转速", StringComparison.Ordinal), "Chinese individual fan enabled warning should explain that 0x00 is not 0% fan speed.");
Require(LocalizationService.T("Control.IndividualHelp").Contains("目标编号不是转速", StringComparison.Ordinal), "Chinese individual fan help should explain target selectors in plain language.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Preset.SmoothCurve")), "Chinese smooth curve label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Preset.EditCurvePoints")), "Chinese edit curve points label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Control.NewCurveEditor")), "Chinese new curve editor label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Control.SaveCurvePreset")), "Chinese save curve preset label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Field.CurveTemperature")), "Chinese curve temperature field should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Field.CurveFanPercent")), "Chinese curve fan percent field should be translated.");

LocalizationService.SetLanguage("en-US");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ConnectionStatus")), "Hero connection label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ModeStatus")), "Hero mode label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestStatus")), "Hero request label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestRunning")), "Hero running request format should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestSucceeded")), "Hero completed request format should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.SensorRefreshSucceeded")), "Hero sensor refresh result format should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LastUpdateValue")), "Hero last update format should have an English translation.");
Require(LocalizationService.T("Status.PollingSkippedPreviousRunning").Contains("No new ipmitool", StringComparison.Ordinal), "English skipped polling log should state that no new ipmitool process is started.");
Require(LocalizationService.T("Status.AutoTickSkippedIpmiBusy").Contains("No new ipmitool", StringComparison.Ordinal), "English skipped auto tick log should state that no new ipmitool process is started.");
Require(LocalizationService.T("Status.IpmiCommandBusy").Contains("did not start a new ipmitool", StringComparison.Ordinal), "English busy IPMI error should state that no new ipmitool process is started.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveTemperature")), "Hero live temperature label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveFanRpm")), "Hero live fan RPM label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LivePower")), "Hero live power label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveVoltage")), "Hero live voltage label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveCurrent")), "Hero live current label should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveWaiting")), "Hero live waiting value should have an English translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LiveMoreItems")), "Hero hidden item count format should have an English translation.");
Require(LocalizationService.T("Overview.LivePower") == "Live power", "Overview live power should have an English translation.");
Require(LocalizationService.T("Overview.AverageVoltage") == "Average voltage", "Overview average voltage should have an English translation.");
Require(LocalizationService.T("Overview.TotalCurrent") == "Total current", "Overview total current should have an English translation.");
Require(LocalizationService.T("Action.RestoreDellFactoryFanSpeed").Contains("Dell factory fan speed", StringComparison.Ordinal), "English factory restore action should name Dell factory fan speed.");
Require(LocalizationService.T("Action.StartPolling") == "Start polling", "English polling start action should describe starting sensor polling.");
Require(LocalizationService.T("Action.CancelPolling") == "Cancel polling", "English polling cancel action should describe canceling sensor polling.");
Require(LocalizationService.T("Tray.RestoreDefault").Contains("Dell factory fan speed", StringComparison.Ordinal), "English tray restore action should name Dell factory fan speed.");
Require(LocalizationService.T("Tray.RefreshSensors") == "Refresh sensors", "English tray refresh action should be translated.");
Require(LocalizationService.T("Tray.OpenIdrac") == "Open iDRAC", "English tray iDRAC action should be translated.");
Require(LocalizationService.T("Tray.OpenLogs") == "Open logs", "English tray logs action should be translated.");
Require(LocalizationService.T("Tray.StopAuto") == "Stop auto policy", "English tray stop-auto action should be translated.");
Require(LocalizationService.T("Control.IndividualEnabledWarning").Contains("0x00 is not 0% fan speed", StringComparison.Ordinal), "English individual fan enabled warning should explain that 0x00 is not 0% fan speed.");
Require(LocalizationService.T("Control.IndividualHelp").Contains("target selector is not a speed", StringComparison.Ordinal), "English individual fan help should explain target selectors in plain language.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Preset.SmoothCurve")), "English smooth curve label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Preset.EditCurvePoints")), "English edit curve points label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Control.NewCurveEditor")), "English new curve editor label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Control.SaveCurvePreset")), "English save curve preset label should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Field.CurveTemperature")), "English curve temperature field should be translated.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Field.CurveFanPercent")), "English curve fan percent field should be translated.");
LocalizationService.SetLanguage("zh-CN");

var tempSettingsDirectory = Path.Combine(Path.GetTempPath(), "R730xdPresetModelTests", Guid.NewGuid().ToString("N"));
try
{
    var settingsStore = new SettingsStore(tempSettingsDirectory);
    var savedPresets = defaults.Concat([curve, powerCurve]).ToList();
    Require(
        new AppSettings { SensorRefreshSeconds = 1 }.SensorRefreshSeconds == 1,
        "The 1-second SDR polling setting should stay expressible in app settings.");

    settingsStore.Save(new AppSettings { SensorRefreshSeconds = 1 });
    Require(
        settingsStore.Load().SensorRefreshSeconds == 1,
        "Saving a 1-second SDR polling interval should remain supported.");

    settingsStore.Save(new AppSettings { Presets = savedPresets });
    var loaded = settingsStore.Load();
    var loadedBalanced = loaded.Presets.Single(preset => preset.Id == "balanced");
    Require(loadedBalanced.EditableName == "夜间静音", "Edited built-in preset name should survive settings save/load.");
    Require(loadedBalanced.EditableDetail == "晚上降低噪音的自定义说明", "Edited built-in preset description should survive settings save/load.");
    Require(loadedBalanced.Percent == 18, "Edited built-in preset percent should survive settings save/load.");

    var loadedCurve = loaded.Presets.Single(preset => preset.Id == curve.Id);
    Require(loadedCurve.Kind == FanPreset.CurveKind, "Temperature curve preset kind should survive settings save/load.");
    Require(loadedCurve.CurvePoints.Count == 2, "Temperature curve preset points should survive settings save/load.");
    Require(loadedCurve.SmoothCurve, "Temperature curve smooth mode should survive settings save/load.");
    Require(loadedCurve.CalculateFanPercent(65) == 35, "Loaded temperature curve preset should still evaluate the current temperature position.");

    var loadedPowerCurve = loaded.Presets.Single(preset => preset.Id == powerCurve.Id);
    Require(loadedPowerCurve.Kind == FanPreset.PowerCurveKind, "Power curve preset kind should survive settings save/load.");
    Require(loadedPowerCurve.CurvePoints.Count == 2, "Power curve preset points should survive settings save/load.");
    Require(loadedPowerCurve.SmoothCurve, "Power curve smooth mode should survive settings save/load.");
    Require(loadedPowerCurve.CalculateFanPercentForPower(500) == 35, "Loaded power curve preset should still evaluate the current power position.");

    settingsStore.Save(new AppSettings
    {
        Presets = savedPresets,
        LastRunningPresetId = powerCurve.Id,
        LastSmartAutoPolicyRunning = true,
    });
    var loadedRunningPreset = settingsStore.Load();
    Require(
        loadedRunningPreset.LastRunningPresetId == powerCurve.Id && !loadedRunningPreset.LastSmartAutoPolicyRunning,
        "A saved running preset id should survive settings save/load and take precedence over smart-auto resume state.");

    settingsStore.Save(new AppSettings
    {
        LastSmartAutoPolicyRunning = true,
    });
    Require(
        settingsStore.Load().LastSmartAutoPolicyRunning,
        "Smart-auto running state should survive settings save/load when no preset id is active.");
}
finally
{
    if (Directory.Exists(tempSettingsDirectory))
    {
        Directory.Delete(tempSettingsDirectory, recursive: true);
    }
}

RunAppLogServiceChecks();
RunLogLevelStyleChecks();
RunHeroMetricSeverityStyleChecks();
RunPollingSkipLogGateChecks();
RunIpmiCommandNoRetrySourceChecks();
RunAutoPolicySamplingOwnershipSourceChecks();
RunIpmiTemperatureLookupChecks();
RunSensorSubtitleFormatterChecks();
RunHeroRealtimeSummaryLayoutXamlChecks();
RunHeroBannerDividerLayoutXamlChecks();
RunHeroThermalModeStatusXamlChecks();
RunNoPartialOverviewDisplaySourceChecks();
RunDashboardTileInPlaceUpdateChecks();
RunFanIconXamlChecks();
RunPowerHealthLayoutXamlChecks();
RunContentScrollWidthXamlChecks();
RunDpiTextWrappingXamlChecks();
RunCurveEditorInteractionPerformanceChecks();
RunRequestScrollResponsivenessChecks();
RunRequestIoResponsivenessChecks();
RunGlobalPollingResponsivenessChecks();
RunHardwareTileValueLayoutXamlChecks();
RunFanAnimationSpeedChecks();
RunPostFanCommandRefreshChecks();
RunElectricalIconXamlChecks();
RunOverviewSectionOrderXamlChecks();
RunSettingsCommandBarXamlChecks();
RunTrayMenuSourceChecks();
RunDashboardChartLayoutChecks();

Console.WriteLine("Preset model checks passed.");

static void RunIpmiTemperatureLookupChecks()
{
    var noTemperatureRows = new[]
    {
        new SensorReading { Key = "Fan1 RPM", Unit = "RPM", NumericValue = 3240 },
        new SensorReading { Key = "Pwr Consumption", Unit = "Watts", NumericValue = 460 },
    };
    Require(
        IpmiCommandService.TryFindCpuTemperatureCelsius(noTemperatureRows) is null,
        "Display-only temperature lookup should return null when SDR output has no numeric temperature rows.");
    RequireThrows<InvalidOperationException>(
        () => IpmiCommandService.FindCpuTemperatureCelsius(noTemperatureRows),
        "Control temperature lookup should still fail explicitly when no temperature rows exist.");

    var mixedTemperatureRows = new[]
    {
        new SensorReading { Key = "Inlet Temp", Unit = "degrees C", NumericValue = 26 },
        new SensorReading { Key = "CPU1 Temp", Unit = "degrees C", NumericValue = 61 },
        new SensorReading { Key = "CPU2 Temp", Unit = "degrees C", NumericValue = 64 },
    };
    Require(
        IpmiCommandService.TryFindCpuTemperatureCelsius(mixedTemperatureRows) == 64,
        "CPU temperature lookup should prefer CPU-named temperature rows and use the highest candidate.");

    var genericTemperatureRows = new[]
    {
        new SensorReading { Key = "Inlet Temp", Unit = "degrees C", NumericValue = 27 },
        new SensorReading { Key = "Exhaust Temp", Unit = "degrees C", NumericValue = 45 },
    };
    Require(
        IpmiCommandService.TryFindCpuTemperatureCelsius(genericTemperatureRows) == 45,
        "CPU temperature lookup should fall back to the highest generic temperature when no CPU row exists.");
}

static void RunIpmiCommandNoRetrySourceChecks()
{
    var commandSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "IpmiCommandService.cs")));
    var settingsSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Models", "AppSettings.cs")));
    var traceSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "CommandTraceEventArgs.cs")));
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));

    Require(
        !settingsSource.Contains("FanControlRawCommandRetry", StringComparison.Ordinal) &&
        !commandSource.Contains("GetMaxAttemptsForArguments", StringComparison.Ordinal) &&
        !commandSource.Contains("Task.Delay(TimeSpan.FromMilliseconds", StringComparison.Ordinal),
        "IPMI commands, including Dell raw fan commands, must not add retry or delayed retry behavior.");
    Require(
        !traceSource.Contains("WillRetry", StringComparison.Ordinal) &&
        !traceSource.Contains("MaxAttempts", StringComparison.Ordinal) &&
        !pageSource.Contains("FormatCommandAttemptText", StringComparison.Ordinal),
        "Command logs should report the single real ipmitool execution without attempt/retry fields.");
}

static void RunAutoPolicySamplingOwnershipSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var pollingGateIndex = source.IndexOf("if (_autoPolicyRunning)", StringComparison.Ordinal);
    var pollStartIndex = source.IndexOf("_sensorPollingTickRunning = true;", StringComparison.Ordinal);
    Require(
        pollingGateIndex >= 0 && pollStartIndex > pollingGateIndex,
        "Sensor polling should skip before starting a second SDR path while software auto policy owns sampling.");
    Require(
        source.Contains("RunAutoPolicyOnceCoreAsync", StringComparison.Ordinal) &&
        source.Contains("RecordVisualizationHistoryPoint(snapshotTime)", StringComparison.Ordinal) &&
        source.Contains("ScheduleVisualizationSnapshot()", StringComparison.Ordinal),
        "The auto-policy tick should keep updating sensors, chart history, and dashboard snapshots when normal polling is suppressed.");
}

static void RunSensorSubtitleFormatterChecks()
{
    var sensor = new SensorReading
    {
        SensorId = "76h",
        Entity = "7.1",
    };

    LocalizationService.SetLanguage("zh-CN");
    Require(
        SensorSubtitleFormatter.Format(sensor) == "编号 0x76 / 位置 7.1",
        "Chinese sensor card subtitle should use compact, readable labels for the sensor record and hardware location.");
    Require(
        SensorSubtitleFormatter.Format(sensor) != "76h · 7.1" &&
        !SensorSubtitleFormatter.Format(sensor).Contains("实体", StringComparison.Ordinal),
        "Chinese sensor card subtitle should not expose ambiguous bare SDR metadata or jargon labels.");
    Require(
        SensorSubtitleFormatter.Format(new SensorReading { SensorId = "30h" }) == "编号 0x30",
        "Chinese sensor card subtitle should label a standalone sensor record id without looking like hours.");
    Require(
        SensorSubtitleFormatter.Format(new SensorReading { Entity = "7.1" }) == "位置 7.1",
        "Chinese sensor card subtitle should label a standalone entity id as hardware location.");

    LocalizationService.SetLanguage("en-US");
    Require(
        SensorSubtitleFormatter.Format(sensor) == "ID 0x76 / Location 7.1",
        "English sensor card subtitle should use compact, readable labels for the sensor record and hardware location.");
    Require(
        SensorSubtitleFormatter.Format(new SensorReading()) == "SDR",
        "Sensor card subtitle should keep the SDR fallback when no metadata exists.");
    LocalizationService.SetLanguage("zh-CN");
}

static void RunHeroRealtimeSummaryLayoutXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var detailTextNames = new[]
    {
        "HeroLiveTemperatureItemsText",
        "HeroLiveFanItemsText",
        "HeroLivePowerItemsText",
        "HeroLiveVoltageItemsText",
        "HeroLiveCurrentItemsText",
    };

    foreach (var detailTextName in detailTextNames)
    {
        var detailText = xaml
            .Descendants(ui + "TextBlock")
            .Single(element => element.Attribute(x + "Name")?.Value == detailTextName);
        var card = detailText
            .Ancestors(ui + "Border")
            .FirstOrDefault(element => element.Attribute("Height") is not null || element.Attribute("MinHeight") is not null);

        Require(
            detailText.Attribute("MaxLines") is null,
            $"{detailTextName} should not cap live hardware details to three rows.");
        Require(
            detailText.Attribute("TextWrapping")?.Value == "Wrap",
            $"{detailTextName} should wrap long sensor detail rows instead of clipping them.");
        Require(
            card?.Attribute("Height") is null &&
            card?.Attribute("MinHeight")?.Value == "170",
            $"{detailTextName} card should have a taller baseline but grow with however many sensor rows are returned.");
    }
}

static void RunHeroBannerDividerLayoutXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var layout = xaml
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "HeroBannerLayout");
    var content = xaml
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "HeroBannerContent");
    var metricsPanel = xaml
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "HeroRealtimeMetricsPanel");
    var statusCard = xaml
        .Descendants(ui + "Border")
        .Single(element => element.Attribute(x + "Name")?.Value == "HeroStatusCard");
    var divider = xaml
        .Descendants(ui + "Border")
        .Single(element => element.Attribute(x + "Name")?.Value == "HeroBannerDivider");
    var rowDefinitions = layout
        .Element(ui + "Grid.RowDefinitions")
        ?.Elements(ui + "RowDefinition")
        .ToList() ?? [];

    Require(
        rowDefinitions.Count == 2 &&
        rowDefinitions.All(row => row.Attribute("Height")?.Value == "Auto") &&
        layout.Attribute("RowSpacing")?.Value == "20",
        "Hero banner should reserve a separate auto row with spacing for the bottom divider.");
    Require(
        content.Parent == layout &&
        content.Attribute("Grid.Row") is null,
        "Hero banner content should stay in the first row above the divider.");
    Require(
        divider.Parent == layout &&
        divider.Attribute("Grid.Row")?.Value == "1" &&
        divider.Attribute("VerticalAlignment") is null,
        "Hero banner divider should occupy the second row instead of overlaying the live metric cards.");
    Require(
        metricsPanel.Ancestors(ui + "Grid").Contains(content) &&
        statusCard.Ancestors(ui + "Grid").Contains(content),
        "Hero live metrics and target status should remain in the content row above the divider.");
}

static void RunHeroThermalModeStatusXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));

    var badge = xaml
        .Descendants(ui + "Border")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "HeroThermalModeBadge");
    var label = xaml
        .Descendants(ui + "TextBlock")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "HeroThermalModeLabelText");
    var value = xaml
        .Descendants(ui + "TextBlock")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "HeroThermalModeValueText");

    Require(
        badge is not null &&
        label is not null &&
        value is not null &&
        label.Attribute("Text") is null &&
        value.Attribute("Text") is null,
        "Hero banner should include a runtime-updated current thermal mode badge without hard-coded visible text.");
    Require(
        pageSource.Contains("UpdateHeroThermalModeTexts()", StringComparison.Ordinal) &&
        pageSource.Contains("HeroThermalModeLabelText.Text", StringComparison.Ordinal) &&
        pageSource.Contains("HeroThermalModeValueText.Text", StringComparison.Ordinal),
        "Hero thermal mode badge should be updated from the same mode state source as the right-side hero status card.");
    Require(
        pageSource.Contains("温度曲线自动", StringComparison.Ordinal) &&
        pageSource.Contains("功耗曲线自动", StringComparison.Ordinal) &&
        pageSource.Contains("软件恒温策略", StringComparison.Ordinal) &&
        pageSource.Contains("Dell 自动温控", StringComparison.Ordinal),
        "Hero thermal mode text should name the actual running temperature-control mode instead of only showing a generic curve status.");
}

static void RunNoPartialOverviewDisplaySourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));

    Require(
        !source.Contains(".Take(14)", StringComparison.Ordinal),
        "Power and health board should display every matching sensor instead of only the first 14.");
    Require(
        !source.Contains("items.Take(2)", StringComparison.Ordinal),
        "Overview summary detail text should display every item it receives instead of only the first two.");
}

static void RunDashboardTileInPlaceUpdateChecks()
{
    var tileSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Models", "DashboardTileViewModel.cs")));
    Require(
        tileSource.Contains("INotifyPropertyChanged", StringComparison.Ordinal) &&
        tileSource.Contains("public event PropertyChangedEventHandler? PropertyChanged", StringComparison.Ordinal),
        "Dashboard tile view model should notify bindings when values update in place.");
    Require(
        tileSource.Contains("public void UpdateFrom(DashboardTileViewModel next)", StringComparison.Ordinal),
        "Dashboard tile view model should expose an in-place update method for refreshes.");
    Require(
        tileSource.Contains("OnPropertyChanged(nameof(AccentBrush))", StringComparison.Ordinal) &&
        tileSource.Contains("OnPropertyChanged(nameof(ValueBrush))", StringComparison.Ordinal) &&
        tileSource.Contains("OnPropertyChanged(nameof(IconBackgroundBrush))", StringComparison.Ordinal),
        "Dashboard tile view model should notify brush bindings when color hex values change.");

    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    Require(
        !source.Contains("target.Clear();", StringComparison.Ordinal),
        "Dashboard tile refresh should not clear the whole tile collection because that causes visible flicker.");
    Require(
        source.Contains(".UpdateFrom(nextTile)", StringComparison.Ordinal),
        "Dashboard tile refresh should update existing tile view models in place when sensor identity is unchanged.");
}

static void RunFanIconXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    const string oldShareLikeFanPath = "M12,2 C14.2,2";

    var fanNavigationItem = xaml
        .Descendants(ui + "NavigationViewItem")
        .Single(element => element.Attribute("Tag")?.Value == "Control");
    var fanNavigationIcon = fanNavigationItem.Descendants(ui + "PathIcon").Single();
    var fanNavigationPath = fanNavigationIcon.Attribute("Data")?.Value ?? string.Empty;

    var hardwareTileTemplate = xaml
        .Descendants(ui + "DataTemplate")
        .Single(element => element.Attribute(x + "Key")?.Value == "HardwareTileTemplate");
    var fanTileRotor = hardwareTileTemplate
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "FanTileRotor");
    var fanTilePath = hardwareTileTemplate
        .Descendants(ui + "Path")
        .Select(element => element.Attribute("Data")?.Value ?? string.Empty)
        .SingleOrDefault(data => data.StartsWith("M12,3 C13.8", StringComparison.Ordinal)) ?? string.Empty;

    Require(
        fanTileRotor.Attribute("Width")?.Value == "24" &&
        fanTileRotor.Attribute("Height")?.Value == "24",
        "Fan RPM dashboard rotor should use the same 24x24 box as the fan path so rotation stays centered.");
    Require(
        fanTileRotor.Attribute("RenderTransformOrigin")?.Value == "0.5,0.5",
        "Fan RPM dashboard rotor should rotate around the center of its 24x24 box.");
    Require(
        !string.IsNullOrWhiteSpace(fanTilePath),
        "Fan RPM dashboard tiles should keep the previous centered four-blade fan path inline.");

    Require(
        fanTilePath.Contains(" M21,12 ", StringComparison.Ordinal) &&
        fanTilePath.Contains(" M3,12 ", StringComparison.Ordinal),
        "Fan RPM dashboard tiles should keep the previous horizontal/vertical centered fan geometry.");
    Require(
        fanNavigationPath != fanTilePath,
        "Fan control navigation icon can differ from the Fan RPM dashboard icon; only the navigation icon should use the X-arranged blade geometry.");
    Require(
        fanNavigationPath.Contains("M13.55,10.45", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("19.15,4.85", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("19.15,19.15", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("4.85,19.15", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("4.85,4.85", StringComparison.Ordinal),
        "Fan control navigation icon should use the centered X-arranged four-blade geometry.");
    Require(
        !fanNavigationPath.Contains("M13.35,10.25", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains("20.75,2.85", StringComparison.Ordinal),
        "Fan control navigation icon should not use the oversized X-arranged blade geometry.");
    Require(
        !fanNavigationPath.Contains(" M21,12 ", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains(" M3,12 ", StringComparison.Ordinal),
        "Fan control navigation icon should not use the horizontal/vertical cross blade layout.");
    Require(
        !fanNavigationPath.Contains(oldShareLikeFanPath, StringComparison.Ordinal),
        "Fan control navigation should not use the old three-node/share-like fan glyph.");
    Require(
        !xaml.Descendants(ui + "Geometry").Any(element => element.Attribute(x + "Key")?.Value == "FanIconGeometry"),
        "Fan icon path data should stay inline because PathIcon.Data fails at runtime with the shared Geometry resource.");
}

static void RunPowerHealthLayoutXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace local = "using:DellR730xdFanControlCenter";

    var powerHealthTitle = xaml
        .Descendants(ui + "TextBlock")
        .Single(element => element.Attribute(local + "Localization.Key")?.Value == "Overview.PowerHealth");
    var powerHealthPanel = powerHealthTitle.Parent ?? throw new InvalidOperationException("Power and health title should live inside a section panel.");
    var powerHealthGrid = powerHealthPanel
        .Elements(ui + "GridView")
        .SingleOrDefault(element => element.Attribute("ItemsSource")?.Value == "{x:Bind PowerTiles, Mode=OneWay}");

    Require(
        powerHealthGrid is not null,
        "Power and health board should use the same wrapping tile grid as the fan board so right-side cards are never partially clipped.");
    Require(
        powerHealthGrid!.Attribute("ItemTemplate")?.Value == "{StaticResource HardwareTileTemplate}",
        "Power and health board should use the hardware tile card template.");
    Require(
        powerHealthGrid.Attribute("ItemsSource")?.Value == "{x:Bind PowerTiles, Mode=OneWay}",
        "Power and health board should keep binding to PowerTiles.");

    Require(
        powerHealthGrid.Attribute("MaxHeight")?.Value == "420" &&
        powerHealthGrid.Attribute("ScrollViewer.VerticalScrollMode")?.Value == "Enabled" &&
        powerHealthGrid.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value == "Auto" &&
        powerHealthGrid.Attribute("ScrollViewer.HorizontalScrollMode")?.Value == "Disabled" &&
        powerHealthGrid.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value == "Disabled",
        "Power and health board should keep a finite scroll-owned viewport so live hardware rows do not all measure under the page ScrollViewer.");

    var wrapGrid = powerHealthGrid
        .Descendants(ui + "ItemsWrapGrid")
        .SingleOrDefault();
    Require(
        wrapGrid?.Attribute("Orientation")?.Value == "Horizontal" &&
        wrapGrid.Attribute("MaximumRowsOrColumns") is null,
        "Power and health board should wrap hardware cards within its finite viewport instead of forcing an all-row horizontal expansion.");
    RequireGridViewOwnsHardwareTileSpacing(xaml, ui, "PowerTiles", "Power and health tile grid should own hardware card spacing so card borders render fully.");
    Require(
        !powerHealthPanel.Elements(ui + "ListView").Any() &&
        !powerHealthPanel.Elements(ui + "ItemsRepeater").Any(),
        "Power and health board should not use a ListView or ItemsRepeater layout that can show partial right-side cards.");
}

static void RunContentScrollWidthXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var contentScrollViewer = xaml
        .Descendants(ui + "ScrollViewer")
        .Single(element => element.Attribute(x + "Name")?.Value == "ContentScrollViewer");
    var contentPanel = contentScrollViewer
        .Elements(ui + "StackPanel")
        .Single();

    Require(
        contentPanel.Attribute("Width")?.Value == "{Binding ViewportWidth, ElementName=ContentScrollViewer}" &&
        contentPanel.Attribute("MaxWidth") is null &&
        contentPanel.Attribute("HorizontalAlignment")?.Value == "Left",
        "Scrollable page content should keep its viewport-bound left-aligned layout so wide overview sections do not drift away from the navigation edge.");
}

static void RunDpiTextWrappingXamlChecks()
{
    var xamlPath = FindRepositoryFile("MainPage.xaml");
    var xamlSource = File.ReadAllText(xamlPath);
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var xaml = XDocument.Load(xamlPath);
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    Require(
        !xamlSource.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal),
        "Visible WinUI text should wrap instead of being ellipsized at higher DPI or in longer localized languages.");
    Require(
        xamlSource.Contains("SizeChanged=\"OnPageSizeChanged\"", StringComparison.Ordinal) &&
        pageSource.Contains("private enum ResponsiveLayoutSize", StringComparison.Ordinal) &&
        pageSource.Contains("ApplyResponsiveLayout(e.NewSize.Width);", StringComparison.Ordinal) &&
        pageSource.Contains("NavigationViewPaneDisplayMode.Top", StringComparison.Ordinal) &&
        pageSource.Contains("ReflowGridChildren", StringComparison.Ordinal),
        "MainPage should use explicit small, medium, and large effective-pixel layout states instead of a single desktop-only XAML layout.");
    Require(
        xamlSource.Contains("x:Name=\"ContentPanel\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"OverviewSummaryGrid\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"NewPresetGrid\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"CurveEditorGrid\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"PowerCurveEditorGrid\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"SettingsCommandBar\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"ConnectionSettingsCard\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"ApplicationSettingsCard\"", StringComparison.Ordinal),
        "Responsive code should own the page containers that need reflow at different DPI-scaled window widths.");
    Require(
        xamlSource.Contains("x:Name=\"NewPowerCurveCanvas\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerPressed=\"OnNewPowerCurveCanvasPointerPressed\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerMoved=\"OnNewCurveCanvasPointerMoved\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerMoved=\"OnNewPowerCurveCanvasPointerMoved\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerReleased=\"OnNewCurveCanvasPointerReleased\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerReleased=\"OnNewPowerCurveCanvasPointerReleased\"", StringComparison.Ordinal) &&
        pageSource.Contains("if (preset.IsCurvePreset)", StringComparison.Ordinal) &&
        pageSource.Contains("CalculateFanPercentForPower", StringComparison.Ordinal) &&
        pageSource.Contains("FindPowerWatts", StringComparison.Ordinal),
        "Curve presets should have draggable interactive chart editors, switch both curve kinds through the preset path, and use real SDR power readings when calculating power fan percent.");
    Require(
        pageSource.Contains("var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken);", StringComparison.Ordinal) &&
        pageSource.Contains("var cpuTemp = IpmiCommandService.FindCpuTemperatureCelsius(readings);", StringComparison.Ordinal) &&
        pageSource.Contains("CalculateFanPercentForAutoTick(activeCurvePreset, cpuTemp, readings, out powerWatts)", StringComparison.Ordinal) &&
        pageSource.Contains("return activeCurvePreset.CalculateFanPercentForPower(powerWatts.Value);", StringComparison.Ordinal) &&
        pageSource.Contains("return activeCurvePreset.CalculateFanPercent(cpuTemp);", StringComparison.Ordinal),
        "Auto ticks should evaluate the configured curve from the current SDR temperature or power reading instead of using fixed example values.");
    Require(
        !Regex.IsMatch(xamlSource, "(?<!Min)Width=\"320\"", RegexOptions.CultureInvariant) &&
        !xamlSource.Contains("<Setter Property=\"MinWidth\" Value=\"300\" />", StringComparison.Ordinal) &&
        !xamlSource.Contains("IsDynamicOverflowEnabled=\"False\"", StringComparison.Ordinal),
        "Preset cards and command bars should not keep desktop-only fixed widths or disabled overflow behavior.");
    Require(
        pageSource.Contains("TemperatureGridView.MaxHeight = layoutSize == ResponsiveLayoutSize.Large ? 300 : double.PositiveInfinity;", StringComparison.Ordinal) &&
        pageSource.Contains("FanGridView.MaxHeight = layoutSize == ResponsiveLayoutSize.Large ? 260 : double.PositiveInfinity;", StringComparison.Ordinal) &&
        pageSource.Contains("PowerHealthGridView.MaxHeight = layoutSize == ResponsiveLayoutSize.Large ? 420 : 560;", StringComparison.Ordinal) &&
        pageSource.Contains("VisualizationWebView.MinHeight = layoutSize == ResponsiveLayoutSize.Large ? 1520 : 3200;", StringComparison.Ordinal),
        "Nested boards and the chart WebView should size intentionally, while the long power/health board keeps a finite viewport for responsive scrolling.");

    foreach (var binding in new[]
    {
        "DisplayName",
        "ChineseName",
        "Key",
        "SensorId",
        "Entity",
        "Value",
        "Unit",
        "Status",
    })
    {
        var textBlocks = xaml
            .Descendants(ui + "TextBlock")
            .Where(element => element.Attribute("Text")?.Value == $"{{Binding {binding}}}")
            .ToArray();
        Require(
            textBlocks.Length > 0 &&
            textBlocks.All(element => element.Attribute("TextWrapping")?.Value is "Wrap" or "WrapWholeWords"),
            $"The {binding} sensor/fan text cell should wrap so high-DPI and localized values remain visible.");
    }
}

static void RunCurveEditorInteractionPerformanceChecks()
{
    var xamlSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var temperatureMoveBody = ExtractMethodBody(pageSource, "OnNewCurveCanvasPointerMoved");
    var powerMoveBody = ExtractMethodBody(pageSource, "OnNewPowerCurveCanvasPointerMoved");

    Require(
        pageSource.Contains("ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);", StringComparison.Ordinal),
        "Wheel events forwarded from the chart WebView should scroll the outer page without queuing animated ChangeView transitions.");
    Require(
        pageSource.Contains("_syncingTemperatureCurveInputsFromCanvas", StringComparison.Ordinal) &&
        pageSource.Contains("_syncingPowerCurveInputsFromCanvas", StringComparison.Ordinal),
        "Dragging curve points should guard against NumberBox value-change feedback triggering a full preview rebuild on every pointer move.");
    Require(
        xamlSource.Contains("PointerEntered=\"OnNewCurveCanvasPointerEntered\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerExited=\"OnNewCurveCanvasPointerExited\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerEntered=\"OnNewPowerCurveCanvasPointerEntered\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerExited=\"OnNewPowerCurveCanvasPointerExited\"", StringComparison.Ordinal) &&
        pageSource.Contains("_temperatureCurveHoverPosition", StringComparison.Ordinal) &&
        pageSource.Contains("_powerCurveHoverPosition", StringComparison.Ordinal) &&
        pageSource.Contains("DrawCurveHoverOverlay", StringComparison.Ordinal) &&
        pageSource.Contains("风扇速度", StringComparison.Ordinal) &&
        pageSource.Contains("FromCanvasX(position.X", StringComparison.Ordinal),
        "Curve editors should show live hover crosshair coordinates with input values and fan speed before the user clicks.");
    Require(
        temperatureMoveBody.Contains("DrawNewCurveCanvas(BuildTemperatureCurveCanvasPreviewPoints(), useSmoothPreview: false);", StringComparison.Ordinal) &&
        !temperatureMoveBody.Contains("UpdateNewCurvePreview(", StringComparison.Ordinal) &&
        !temperatureMoveBody.Contains("SortNewCurvePointsInPlace(", StringComparison.Ordinal),
        "Temperature curve dragging should use lightweight canvas redraws and defer full validation, sorting, and preview text updates until release.");
    Require(
        powerMoveBody.Contains("DrawNewPowerCurveCanvas(BuildPowerCurveCanvasPreviewPoints(), useSmoothPreview: false);", StringComparison.Ordinal) &&
        !powerMoveBody.Contains("UpdateNewPowerCurvePreview(", StringComparison.Ordinal) &&
        !powerMoveBody.Contains("SortNewPowerCurvePointsInPlace(", StringComparison.Ordinal),
        "Power curve dragging should use lightweight canvas redraws and defer full validation, sorting, and preview text updates until release.");
    Require(
        pageSource.Contains("ScrollEditorIntoView(PowerCurveEditorGrid);", StringComparison.Ordinal) &&
        pageSource.Contains("ScrollEditorIntoView(CurveEditorGrid);", StringComparison.Ordinal) &&
        pageSource.Contains("ScrollPresetIntoView(savedPresetId);", StringComparison.Ordinal) &&
        pageSource.Contains("PresetGridView.ScrollIntoView(preset, ScrollIntoViewAlignment.Leading);", StringComparison.Ordinal),
        "Editing a curve preset should scroll to the matching editor, and saving should scroll back to the saved preset card.");
}

static void RunRequestScrollResponsivenessChecks()
{
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var dashboardHtml = File.ReadAllText(FindRepositoryFile(Path.Combine("Assets", "Charts", "dashboard.html")));
    var wheelHandlerStart = dashboardHtml.IndexOf("window.addEventListener(\"wheel\"", StringComparison.Ordinal);
    var wheelHandlerEnd = dashboardHtml.IndexOf("}, { capture: true, passive: false });", wheelHandlerStart, StringComparison.Ordinal);
    var wheelHandlerBody = wheelHandlerStart >= 0 && wheelHandlerEnd > wheelHandlerStart
        ? dashboardHtml[wheelHandlerStart..wheelHandlerEnd]
        : string.Empty;

    Require(
        pageSource.Contains("ScheduleVisualizationSnapshot();", StringComparison.Ordinal) &&
        pageSource.Contains("DispatcherQueuePriority.Low", StringComparison.Ordinal) &&
        pageSource.Contains("_visualizationSnapshotUpdateScheduled", StringComparison.Ordinal),
        "Sensor request completion should coalesce chart WebView snapshot updates through a low-priority dispatcher task so scrolling/input keeps priority.");
    Require(
        pageSource.Contains("_latestVisualizationSnapshot", StringComparison.Ordinal) &&
        pageSource.Contains("_latestVisualizationSnapshotTime", StringComparison.Ordinal),
        "Chart payload updates should reuse the snapshot built for history persistence instead of rebuilding the same sensor snapshot on the UI thread.");
    Require(
        pageSource.Contains("Task.Run(() => JsonSerializer.Serialize(payload, VisualizationJsonOptions))", StringComparison.Ordinal) &&
        !pageSource.Contains("var payload = BuildVisualizationPayload();\r\n            var json = JsonSerializer.Serialize(payload, VisualizationJsonOptions);", StringComparison.Ordinal) &&
        !pageSource.Contains("var payload = BuildVisualizationPayload();\n            var json = JsonSerializer.Serialize(payload, VisualizationJsonOptions);", StringComparison.Ordinal),
        "Chart payload JSON serialization should run off the UI thread before posting the finished JSON to WebView2.");
    Require(
        !pageSource.Contains("RecordVisualizationHistoryPoint(snapshotTime);\r\n            SendVisualizationSnapshot();", StringComparison.Ordinal) &&
        !pageSource.Contains("RecordVisualizationHistoryPoint(snapshotTime);\n            SendVisualizationSnapshot();", StringComparison.Ordinal),
        "Sensor refresh completion should not synchronously serialize and post the full chart payload on the UI thread immediately after history recording.");
    Require(
        pageSource.Contains("_pendingVisualizationWheelDeltaY", StringComparison.Ordinal) &&
        pageSource.Contains("ScheduleVisualizationWheelScroll", StringComparison.Ordinal),
        "Forwarded WebView wheel messages should be coalesced before calling ScrollViewer.ChangeView.");
    Require(
        pageSource.Contains("_localizedSensorsDirty", StringComparison.Ordinal) &&
        pageSource.Contains("RefreshLocalizedSensorRowsIfVisible", StringComparison.Ordinal) &&
        pageSource.Contains("SensorsView.Visibility == Visibility.Visible", StringComparison.Ordinal),
        "Sensor table localization rows should be deferred while the Sensors page is hidden so request completion does not refresh an off-screen ListView during overview scrolling.");
    Require(
        dashboardHtml.Contains("let pendingHostWheelDeltaY = 0;", StringComparison.Ordinal) &&
        dashboardHtml.Contains("requestAnimationFrame(postPendingHostWheelDelta);", StringComparison.Ordinal),
        "The chart WebView should coalesce wheel forwarding to one host message per animation frame.");
    Require(
        !wheelHandlerBody.Contains("window.chrome.webview.postMessage", StringComparison.Ordinal),
        "The chart WebView should not post one host wheel message for every raw wheel event.");
}

static void RunRequestIoResponsivenessChecks()
{
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var logSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "AppLogService.cs")));

    Require(
        pageSource.Contains("QueueVisualizationHistoryPersistence(historyPoint, timestamp);", StringComparison.Ordinal) &&
        pageSource.Contains("Task.Run(() => PersistVisualizationHistoryPoint(historyPoint, timestamp))", StringComparison.Ordinal),
        "Chart history persistence should be queued to a background task instead of appending JSONL on the UI thread after each request.");
    Require(
        !pageSource.Contains("TryAppendVisualizationHistoryPoint(historyPoint, timestamp);", StringComparison.Ordinal),
        "Request completion should not synchronously append chart history from the UI thread.");
    Require(
        !logSource.Contains("FileOptions.WriteThrough", StringComparison.Ordinal) &&
        !logSource.Contains("Flush(flushToDisk: true)", StringComparison.Ordinal),
        "Runtime logging should not force a physical disk flush for every UI log record because it blocks scrolling and input during requests.");
    Require(
        logSource.Contains("Channel.CreateUnbounded<AppLogRecord>", StringComparison.Ordinal) &&
        logSource.Contains("Task.Run(ProcessWriteQueueAsync)", StringComparison.Ordinal) &&
        logSource.Contains("WriteFailed?.Invoke(this, ex);", StringComparison.Ordinal),
        "Runtime logging should queue disk writes to a single background worker and surface write failures instead of blocking the UI thread.");
    Require(
        pageSource.Contains("_appLog.WriteFailed += OnAppLogWriteFailed;", StringComparison.Ordinal) &&
        pageSource.Contains("ShowAppLogWriteFailure", StringComparison.Ordinal) &&
        pageSource.Contains("AddVolatileLog(T(\"Log.Error\"), message);", StringComparison.Ordinal),
        "The main page should expose background runtime-log write failures in the visible UI log and status bar.");
}

static void RunGlobalPollingResponsivenessChecks()
{
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var ipmiSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "IpmiCommandService.cs")));
    var ipmiAwaitCount = Regex.Matches(ipmiSource, @"\bawait\b", RegexOptions.CultureInvariant).Count;
    var ipmiConfigureAwaitCount = Regex.Matches(ipmiSource, @"ConfigureAwait\(false\)", RegexOptions.CultureInvariant).Count;
    var scheduleVisualizationBody = ExtractMethodBody(pageSource, "ScheduleVisualizationSnapshot");
    var sendVisualizationBody = ExtractMethodBody(pageSource, "SendVisualizationSnapshot");
    var addVolatileLogBody = ExtractMethodBody(pageSource, "AddVolatileLog");

    Require(
        ipmiAwaitCount > 0 &&
        ipmiAwaitCount == ipmiConfigureAwaitCount,
        "Every await inside IpmiCommandService should use ConfigureAwait(false) so command completion, stdout/stderr reading, and SDR parsing do not resume on the WinUI UI thread.");
    Require(
        ipmiSource.Contains("ParseSensorReadings(result.StandardOutput).ToList();", StringComparison.Ordinal) &&
        ipmiSource.Contains("CommandCompleted?.Invoke(", StringComparison.Ordinal),
        "The IPMI service should keep real SDR parsing and command completion events in the service layer while its continuations stay off the UI context.");
    Require(
        pageSource.Contains("private bool IsOverviewViewVisible()", StringComparison.Ordinal) &&
        pageSource.Contains("private void RefreshOverviewDataIfVisible()", StringComparison.Ordinal) &&
        pageSource.Contains("_overviewMetricsDirty = true;", StringComparison.Ordinal) &&
        pageSource.Contains("UpdateOverviewMetricSummaries();", StringComparison.Ordinal),
        "Sensor polling should keep current data but defer overview-only card and chart UI updates while another page is visible.");
    Require(
        scheduleVisualizationBody.Contains("if (!IsOverviewViewVisible())", StringComparison.Ordinal) &&
        scheduleVisualizationBody.IndexOf("if (!IsOverviewViewVisible())", StringComparison.Ordinal) <
        scheduleVisualizationBody.IndexOf("VisualizationWebView.CoreWebView2", StringComparison.Ordinal) &&
        sendVisualizationBody.Contains("if (!IsOverviewViewVisible())", StringComparison.Ordinal),
        "Chart WebView payload updates should not be scheduled or sent while the overview page is hidden.");
    Require(
        pageSource.Contains("_volatileLogEntries", StringComparison.Ordinal) &&
        pageSource.Contains("_volatileLogsDirty", StringComparison.Ordinal) &&
        pageSource.Contains("RefreshVisibleLogsIfDirty();", StringComparison.Ordinal) &&
        addVolatileLogBody.Contains("if (!IsOverviewViewVisible())", StringComparison.Ordinal) &&
        addVolatileLogBody.Contains("Logs.Insert(0, entry);", StringComparison.Ordinal),
        "Visible runtime-log ListView updates should be deferred while the overview page is hidden instead of changing an off-screen bound collection on every command completion.");
}

static void RunHardwareTileValueLayoutXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var hardwareTileTemplate = xaml
        .Descendants(ui + "DataTemplate")
        .Single(element => element.Attribute(x + "Key")?.Value == "HardwareTileTemplate");
    var hardwareTileBorder = hardwareTileTemplate
        .Elements(ui + "Border")
        .Single();
    Require(
        hardwareTileBorder.Attribute("Width")?.Value == "250" &&
        hardwareTileBorder.Attribute("MinHeight")?.Value == "138",
        "Hardware cards should leave enough width and height for labeled sensor metadata without cramped wrapping.");
    Require(
        hardwareTileBorder.Attribute("Margin") is null,
        "Hardware tile template should not own external spacing because repeater and grid containers size items independently.");
    RequireGridViewOwnsHardwareTileSpacing(xaml, ui, "TemperatureTiles", "Temperature tile grid should own hardware card spacing after the shared template margin is removed.");
    RequireGridViewOwnsHardwareTileSpacing(xaml, ui, "FanTiles", "Fan tile grid should own hardware card spacing after the shared template margin is removed.");

    var valueTextBlock = hardwareTileTemplate
        .Descendants(ui + "TextBlock")
        .Single(element => element.Attribute("Text")?.Value == "{Binding Value}");

    Require(
        valueTextBlock.Attribute("FontSize")?.Value == "{Binding ValueFontSize}",
        "Hardware tile values should bind font size so long health status values fit without being clipped.");
    Require(
        valueTextBlock.Attribute("MaxLines")?.Value == "{Binding ValueMaxLines}",
        "Hardware tile values should bind max lines so status text can display fully while numeric values stay compact.");
}

static void RequireGridViewOwnsHardwareTileSpacing(XDocument xaml, XNamespace ui, string boundItemsSource, string message)
{
    var gridView = xaml
        .Descendants(ui + "GridView")
        .Single(element => element.Attribute("ItemsSource")?.Value == $"{{x:Bind {boundItemsSource}, Mode=OneWay}}");
    var itemContainerStyle = gridView
        .Element(ui + "GridView.ItemContainerStyle")?
        .Element(ui + "Style");
    var setters = itemContainerStyle?.Elements(ui + "Setter").ToList() ?? new List<XElement>();

    Require(
        itemContainerStyle?.Attribute("TargetType")?.Value == "GridViewItem" &&
        setters.Any(setter => setter.Attribute("Property")?.Value == "Padding" && setter.Attribute("Value")?.Value == "0") &&
        setters.Any(setter => setter.Attribute("Property")?.Value == "Margin" && setter.Attribute("Value")?.Value == "0,0,10,10") &&
        setters.Any(setter => setter.Attribute("Property")?.Value == "HorizontalContentAlignment" && setter.Attribute("Value")?.Value == "Left") &&
        setters.Any(setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment" && setter.Attribute("Value")?.Value == "Top"),
        message);
}

static void RunFanAnimationSpeedChecks()
{
    var mainPageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var xaml = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));

    Require(
        mainPageSource.Contains("const double MaximumFanAnimationRpm = 18000;", StringComparison.Ordinal) &&
        mainPageSource.Contains("const double FastestFanRotationSeconds = 0.11;", StringComparison.Ordinal) &&
        mainPageSource.Contains("const double SlowestFanRotationSeconds = 5.2;", StringComparison.Ordinal),
        "Fan animation timing should define explicit slowest/full-speed bounds, with full-speed animation twice as fast as the old 0.22 second floor.");
    Require(
        !mainPageSource.Contains("Math.Sqrt(normalized)", StringComparison.Ordinal) &&
        mainPageSource.Contains("var slowestRotationsPerSecond = 1 / SlowestFanRotationSeconds;", StringComparison.Ordinal) &&
        mainPageSource.Contains("var fastestRotationsPerSecond = 1 / FastestFanRotationSeconds;", StringComparison.Ordinal) &&
        mainPageSource.Contains("var rotationsPerSecond = slowestRotationsPerSecond + (normalized * (fastestRotationsPerSecond - slowestRotationsPerSecond));", StringComparison.Ordinal) &&
        mainPageSource.Contains("return Math.Round(1 / rotationsPerSecond, 2);", StringComparison.Ordinal),
        "Fan animation speed should grow linearly with RPM instead of using a nonlinear duration curve.");
    Require(
        mainPageSource.Contains("nameof(DashboardTileViewModel.FanRotationSeconds)", StringComparison.Ordinal) &&
        mainPageSource.Contains("nameof(DashboardTileViewModel.IsFanAnimated)", StringComparison.Ordinal) &&
        mainPageSource.Contains("nameof(DashboardTileViewModel.FanIconOpacity)", StringComparison.Ordinal),
        "Fan animation should listen for live tile speed and visibility changes instead of only restarting on DataContext changes.");
    Require(
        mainPageSource.Contains(".PropertyChanged += state.TilePropertyChangedHandler", StringComparison.Ordinal) &&
        mainPageSource.Contains(".PropertyChanged -= state.TilePropertyChangedHandler", StringComparison.Ordinal),
        "Fan animation should attach and detach a tile property-change handler so RPM refreshes immediately retime the storyboard without leaking handlers.");
    Require(
        mainPageSource.IndexOf("var currentAngle = GetDashboardTileRotationAngle(element);", StringComparison.Ordinal) <
        mainPageSource.IndexOf("state.Storyboard?.Stop();", StringComparison.Ordinal),
        "Fan animation should capture the current angle before stopping the old storyboard so live speed changes do not visibly jump backward.");
    Require(
        xaml.Contains("Unloaded=\"OnDashboardTileFanIconUnloaded\"", StringComparison.Ordinal),
        "Fan animation should detach its property-change observer when the tile icon is unloaded.");
}

static void RunPostFanCommandRefreshChecks()
{
    var mainPageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var localizationSource = File.ReadAllText(FindRepositoryFile("Services/LocalizationService.cs"));

    Require(
        mainPageSource.Contains("private async Task RefreshSensorsAfterFanCommandAsync()", StringComparison.Ordinal) &&
        mainPageSource.Contains("await RefreshSensorsCoreAsync(ReadProfile(), token)", StringComparison.Ordinal) &&
        mainPageSource.Contains("RestartSensorPollingAfterImmediateRefresh();", StringComparison.Ordinal),
        "Successful user fan commands should immediately refresh sensors and dashboard data instead of waiting for the next polling interval.");
    Require(
        CountOccurrences(mainPageSource, "await RefreshSensorsAfterFanCommandAsync();") >= 5,
        "All direct user fan-setting paths should trigger the immediate post-command sensor refresh.");
    Require(
        mainPageSource.Contains("if (succeeded)", StringComparison.Ordinal),
        "Post-command sensor refresh should run only after the fan command reports success.");
    Require(
        localizationSource.Contains("\"Status.RefreshingSensorsAfterFanCommand\"", StringComparison.Ordinal) &&
        localizationSource.Contains("\"Status.FanCommandSensorsRefreshed\"", StringComparison.Ordinal),
        "Post-command sensor refresh should use explicit localized status text.");
}

static void RunElectricalIconXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var hardwareTileTemplate = xaml
        .Descendants(ui + "DataTemplate")
        .Single(element => element.Attribute(x + "Key")?.Value == "HardwareTileTemplate");
    var voltageIcon = hardwareTileTemplate
        .Descendants(ui + "Grid")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "VoltageTilePulse");
    var currentIcon = hardwareTileTemplate
        .Descendants(ui + "Grid")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "CurrentTilePulse");
    var voltagePathData = voltageIcon is null
        ? string.Empty
        : string.Join(" ", voltageIcon.Descendants(ui + "Path").Select(element => element.Attribute("Data")?.Value ?? string.Empty));
    var currentPathData = currentIcon is null
        ? string.Empty
        : string.Join(" ", currentIcon.Descendants(ui + "Path").Select(element => element.Attribute("Data")?.Value ?? string.Empty));

    Require(
        voltageIcon is not null && !voltagePathData.Contains("M5,5 H19 V15 H5 Z", StringComparison.Ordinal),
        "Voltage dashboard tile should use the refined gauge-style icon instead of the old blocky monitor glyph.");
    Require(
        currentIcon is not null && !currentPathData.Contains("M4,13 C6,7 10,7 12,13", StringComparison.Ordinal),
        "Current dashboard tile should use the refined current-flow icon instead of the old cramped wave arrow.");

    var mainPageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    Require(
        mainPageSource.Contains("From = 0.98", StringComparison.Ordinal) &&
        mainPageSource.Contains("To = 1.06", StringComparison.Ordinal),
        "Electrical icon pulse should be subtle enough to avoid distorted voltage/current glyphs.");
}

static void RunOverviewSectionOrderXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace local = "using:DellR730xdFanControlCenter";

    var quickActionsRow = FindOverviewSectionRow(xaml, ui, local, "Overview.QuickActions");
    var visualizationRow = FindOverviewSectionRow(xaml, ui, local, "Overview.VisualizationBoard");
    var tempBoardRow = FindOverviewSectionRow(xaml, ui, local, "Overview.TempBoard");
    var fanBoardRow = FindOverviewSectionRow(xaml, ui, local, "Overview.FanBoard");
    var powerHealthRow = FindOverviewSectionRow(xaml, ui, local, "Overview.PowerHealth");

    Require(
        quickActionsRow < visualizationRow,
        "Quick actions should appear above the interactive data visualization board.");
    Require(
        visualizationRow < tempBoardRow && tempBoardRow < fanBoardRow && fanBoardRow < powerHealthRow,
        "Overview boards should keep visualization, temperature, fan, then power/health order after quick actions.");
}

static void RunSettingsCommandBarXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    XNamespace local = "using:DellR730xdFanControlCenter";

    var settingsView = xaml
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "SettingsView");
    var saveCommands = settingsView
        .Descendants()
        .Where(element => element.Attribute("Click")?.Value == "OnSaveSettingsClick")
        .ToList();
    var connectCommands = settingsView
        .Descendants()
        .Where(element => element.Attribute("Click")?.Value == "OnTestConnectionClick")
        .ToList();
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var xamlSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));

    Require(saveCommands.Count == 1, "Settings page should expose one global save command instead of side-specific save buttons.");
    Require(connectCommands.Count == 1, "Settings page should expose one global polling command beside the save command.");

    var saveCommand = saveCommands.Single();
    var connectCommand = connectCommands.Single();
    var commandBar = settingsView.Elements(ui + "CommandBar").SingleOrDefault();
    Require(commandBar is not null, "Settings page should use a top-level CommandBar for global settings actions.");
    Require(commandBar!.Attribute("Grid.ColumnSpan")?.Value == "2", "Settings action CommandBar should span both settings columns.");
    Require(
        saveCommand.Name == ui + "AppBarButton" &&
        saveCommand.Ancestors(ui + "CommandBar").Contains(commandBar),
        "Settings save command should be an AppBarButton in the global CommandBar.");
    Require(
        connectCommand.Name == ui + "AppBarButton" &&
        connectCommand.Ancestors(ui + "CommandBar").Contains(commandBar),
        "Settings polling command should be an AppBarButton in the global CommandBar.");
    Require(
        saveCommand.Attribute(local + "Localization.Key")?.Value == "Action.Save",
        "Settings global save command should use the Save settings localization key.");
    Require(
        connectCommand.Attribute(x + "Name")?.Value == "SettingsPollingButton" &&
        connectCommand.Attribute(local + "Localization.Key") is null &&
        connectCommand.Attribute("Label")?.Value == string.Empty,
        "Settings polling command should use runtime labels so it can switch between Start polling and Cancel polling.");
    Require(
        xamlSource.Contains("x:Name=\"QuickPollingButton\"", StringComparison.Ordinal) &&
        source.Contains("CancelSensorPollingFromUser()", StringComparison.Ordinal) &&
        source.Contains("T(\"Action.CancelPolling\")", StringComparison.Ordinal) &&
        source.Contains("T(\"Action.StartPolling\")", StringComparison.Ordinal) &&
        source.Contains("button.IsEnabled = !_isConnecting;", StringComparison.Ordinal),
        "Overview and Settings polling commands should share dynamic start/cancel labels and disable while a connection attempt is already running.");
    Require(
        !saveCommand.Ancestors(ui + "Border").Any(),
        "Settings save command should not live inside only the left connection card.");
}

static void RunTrayMenuSourceChecks()
{
    var traySource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "TrayIconManager.cs")));
    var windowSource = File.ReadAllText(FindRepositoryFile("MainWindow.xaml.cs"));
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));

    Require(
        !traySource.Contains("BuildFanControlMenu", StringComparison.Ordinal) &&
        !traySource.Contains("BuildAllFanSpeedMenu", StringComparison.Ordinal),
        "Tray menu should no longer hide common fan commands behind nested fan-control submenus.");
    Require(
        traySource.Contains("AppendCommand(menu, RefreshSensorsCommand", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, OpenIdracCommand", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, OpenLogsCommand", StringComparison.Ordinal),
        "Tray menu should expose refresh, iDRAC, and logs as direct operations.");
    Require(
        traySource.Contains("AppendCommand(menu, RestoreDefaultCommand", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, StopAutoCommand", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, Fans20Command", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, Fans35Command", StringComparison.Ordinal) &&
        traySource.Contains("AppendCommand(menu, Fans50Command", StringComparison.Ordinal),
        "Tray menu should expose Dell auto, stop auto, and common all-fan speeds directly.");
    Require(
        traySource.Contains("AppendPopup(menu, BuildPresetMenu(), LocalizationService.T(\"Tray.Presets\"))", StringComparison.Ordinal),
        "Tray menu should keep dynamic presets grouped in a single one-level submenu.");
    Require(
        windowSource.Contains("RefreshSensorsRequested", StringComparison.Ordinal) &&
        windowSource.Contains("OpenIdracRequested", StringComparison.Ordinal) &&
        windowSource.Contains("OpenLogsRequested", StringComparison.Ordinal) &&
        windowSource.Contains("StopAutoRequested", StringComparison.Ordinal),
        "MainWindow should bridge the expanded tray commands to MainPage actions.");
    Require(
        pageSource.Contains("public Task RefreshSensorsFromTrayAsync()", StringComparison.Ordinal) &&
        pageSource.Contains("public Task OpenIdracFromTrayAsync()", StringComparison.Ordinal) &&
        pageSource.Contains("public void OpenLogFolderFromTray()", StringComparison.Ordinal) &&
        pageSource.Contains("public void StopAutoPolicyFromTray()", StringComparison.Ordinal),
        "MainPage should expose explicit tray-safe wrappers for non-navigation tray actions.");
}

static void RunRuntimeStatePersistenceSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    Require(
        source.Contains("await RestoreLastRunningStateAsync()", StringComparison.Ordinal) &&
        source.Contains("LastRunningPresetId", StringComparison.Ordinal) &&
        source.Contains("LastSmartAutoPolicyRunning", StringComparison.Ordinal),
        "Startup polling should restore the last persisted running preset or smart-auto state after a real connection succeeds.");
    Require(
        source.Contains("PersistRunningPresetState(curvePreset.Id)", StringComparison.Ordinal) &&
        source.Contains("PersistSmartAutoRunningState()", StringComparison.Ordinal) &&
        source.Contains("ClearPersistedRunningState()", StringComparison.Ordinal),
        "Running preset, smart-auto, and stopped states should be written explicitly instead of existing only in memory.");
    Require(
        source.Contains("if (shouldReapply)", StringComparison.Ordinal) &&
        source.Contains("await ApplyPresetAsync(savedPreset)", StringComparison.Ordinal),
        "Saving the active preset or active curve should immediately re-apply it instead of requiring another Switch click.");
    Require(
        !source.Contains("waitForIpmiLock: false", StringComparison.Ordinal),
        "User-triggered fan mode changes should wait for the current IPMI command instead of showing a busy error.");
}

static void RunDashboardChartLayoutChecks()
{
    var html = File.ReadAllText(FindRepositoryFile(Path.Combine("Assets", "Charts", "dashboard.html")));
    var normalizedHtml = html.Replace("\r\n", "\n", StringComparison.Ordinal);
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var xamlSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));

    Require(
        html.Contains("id=\"rangeButtons\"", StringComparison.Ordinal) &&
        html.Contains("id=\"customStart\"", StringComparison.Ordinal) &&
        html.Contains("id=\"customEnd\"", StringComparison.Ordinal) &&
        html.Contains("let selectedRange = \"6h\";", StringComparison.Ordinal) &&
        html.Contains("const rangeDurations", StringComparison.Ordinal) &&
        html.Contains("function renderRangeControls(payload)", StringComparison.Ordinal),
        "Dashboard should expose current, preset, and custom history range controls instead of a per-snapshot time dropdown.");
    Require(
        !html.Contains("id=\"historySelect\"", StringComparison.Ordinal) &&
        !html.Contains("selectedSnapshotId", StringComparison.Ordinal) &&
        !html.Contains("renderHistorySelector", StringComparison.Ordinal) &&
        !html.Contains("selectedSnapshotPayload", StringComparison.Ordinal),
        "Dashboard should remove the old single-snapshot selector path.");
    Require(
        !html.Contains("compactFanLabel", StringComparison.Ordinal) &&
        !html.Contains("shortAxisLabel", StringComparison.Ordinal) &&
        !html.Contains("compactPowerLabel", StringComparison.Ordinal) &&
        !html.Contains("overflow: \"truncate\"", StringComparison.Ordinal) &&
        !html.Contains("hideOverlap", StringComparison.Ordinal) &&
        !html.Contains("text-overflow: ellipsis", StringComparison.Ordinal),
        "History charts should not abbreviate, truncate, or hide chart labels.");
    Require(
        html.Contains("function buildRangePayload(payload)", StringComparison.Ordinal) &&
        html.Contains("function filterHistoryBySelectedRange(history)", StringComparison.Ordinal) &&
        html.Contains("rangeHistory: filtered", StringComparison.Ordinal) &&
        html.Contains("function aggregateSummary(history)", StringComparison.Ordinal),
        "Dashboard should build chart data from every retained point in the selected history range.");
    Require(
        html.Contains("renderTrend(payload);", StringComparison.Ordinal) &&
        html.Contains("renderTemperatureHistory(payload);", StringComparison.Ordinal) &&
        html.Contains("renderFansHistory(payload);", StringComparison.Ordinal) &&
        html.Contains("renderPowerHistory(payload);", StringComparison.Ordinal) &&
        html.Contains("renderTypes(payload);", StringComparison.Ordinal),
        "Dashboard panels should use history-oriented time-series chart renderers.");
    Require(
        html.Contains("xAxis: { type: \"time\"", StringComparison.Ordinal) &&
        html.Contains("dataZoom: chartDataZoom(layout)", StringComparison.Ordinal) &&
        html.Contains("renderEmptyChart", StringComparison.Ordinal),
        "Historical charts should use time axes, zoom controls, and explicit empty-range messages.");
    Require(
        html.Contains("function chartLegend(top)", StringComparison.Ordinal) &&
        html.Contains("legend: chartLegend(layout.legendTop)", StringComparison.Ordinal) &&
        html.Contains("function chartGrid(top, right, bottom, left)", StringComparison.Ordinal) &&
        html.Contains("function chartDataZoom(layout)", StringComparison.Ordinal) &&
        html.Contains("function zoomLabelFormatter(value)", StringComparison.Ordinal) &&
        html.Contains("function chartLayout(chartId, series, options)", StringComparison.Ordinal) &&
        html.Contains("function dpiScale()", StringComparison.Ordinal) &&
        html.Contains("const titleBlockBottom", StringComparison.Ordinal) &&
        html.Contains("const legendToPlotGap", StringComparison.Ordinal) &&
        html.Contains("const bottom = Math.max(settings.bottom, settings.zoomBottom + settings.zoomHeight + scaledLineHeight(38));", StringComparison.Ordinal) &&
        html.Contains("zoomLeft: Math.max(settings.left, 92)", StringComparison.Ordinal) &&
        html.Contains("zoomRight: Math.max(settings.right, 92)", StringComparison.Ordinal) &&
        html.Contains("labelFormatter: zoomLabelFormatter", StringComparison.Ordinal) &&
        !html.Contains("gridTop: Math.min(preferredGridTop, maxGridTop)", StringComparison.Ordinal) &&
        html.Contains("estimateLegendRows(names, width - 32)", StringComparison.Ordinal) &&
        !html.Contains("type: \"scroll\"", StringComparison.Ordinal),
        "Historical chart titles, range labels, full legends, plot grids, and zoom labels should reserve separate space without legend pagination or clipping.");
    Require(
        html.Contains("document.body.dataset.dpiScale = dpiScale().toFixed(2);", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"VisualizationWebView\"", StringComparison.Ordinal) &&
        xamlSource.Contains("MinHeight=\"1520\"", StringComparison.Ordinal) &&
        normalizedHtml.Contains(".grid {\n      display: grid;\n      grid-template-columns: repeat(2, minmax(0, 1fr));", StringComparison.Ordinal) &&
        normalizedHtml.Contains("grid-auto-rows: minmax(400px, 1fr);", StringComparison.Ordinal) &&
        normalizedHtml.Contains(".panel.wide { grid-column: span 1; }", StringComparison.Ordinal) &&
        !normalizedHtml.Contains("grid-template-columns: 1.15fr 1fr 1.25fr;", StringComparison.Ordinal) &&
        !normalizedHtml.Contains("grid-template-rows: repeat(3, minmax(280px, 1fr));", StringComparison.Ordinal),
        "Visualization WebView should use a two-column chart grid with actual panel rows instead of leaving an empty third-column layout.");
    Require(
        !xamlSource.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal),
        "Visible WinUI text should wrap instead of being ellipsized at higher DPI or in longer localized languages.");
    Require(
        pageSource.Contains("private sealed class VisualizationSnapshot", StringComparison.Ordinal) &&
        pageSource.Contains("public VisualizationSummary? Summary", StringComparison.Ordinal) &&
        pageSource.Contains("public VisualizationCurrent? Current", StringComparison.Ordinal) &&
        pageSource.Contains("TypeCounts = snapshot.TypeCounts", StringComparison.Ordinal) &&
        pageSource.Contains("SensorTree = snapshot.SensorTree", StringComparison.Ordinal),
        "Dashboard history points should carry the full sensor snapshot needed by every chart, not only trend summary numbers.");
    Require(
        pageSource.Contains("private const int VisualizationHistoryRetentionDays = 7", StringComparison.Ordinal) &&
        pageSource.Contains("chart-history-*.jsonl", StringComparison.Ordinal) &&
        pageSource.Contains("public string Timestamp", StringComparison.Ordinal) &&
        pageSource.Contains("public long UnixMilliseconds", StringComparison.Ordinal) &&
        pageSource.Contains("LoadVisualizationHistory()", StringComparison.Ordinal) &&
        pageSource.Contains("QueueVisualizationHistoryPersistence", StringComparison.Ordinal) &&
        pageSource.Contains("PersistVisualizationHistoryPoint", StringComparison.Ordinal),
        "Visualization history should persist timestamped chart snapshots for the last seven days.");
}

static int FindOverviewSectionRow(XDocument xaml, XNamespace ui, XNamespace local, string localizationKey)
{
    var title = xaml
        .Descendants(ui + "TextBlock")
        .Single(element => element.Attribute(local + "Localization.Key")?.Value == localizationKey);

    var border = title.Ancestors(ui + "Border")
        .FirstOrDefault(element => element.Attribute("Grid.Row") is not null);
    if (border is null)
    {
        throw new InvalidOperationException($"Unable to find overview section row for {localizationKey}.");
    }

    return int.Parse(border.Attribute("Grid.Row")!.Value, CultureInfo.InvariantCulture);
}

static string ExtractMethodBody(string source, string methodName)
{
    var signature = $"private void {methodName}";
    var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
    if (signatureStart < 0)
    {
        throw new InvalidOperationException($"Unable to find method: {methodName}");
    }

    var bodyStart = source.IndexOf('{', signatureStart);
    if (bodyStart < 0)
    {
        throw new InvalidOperationException($"Unable to find method body: {methodName}");
    }

    var depth = 0;
    for (var index = bodyStart; index < source.Length; index++)
    {
        if (source[index] == '{')
        {
            depth++;
        }
        else if (source[index] == '}')
        {
            depth--;
            if (depth == 0)
            {
                return source.Substring(bodyStart, index - bodyStart + 1);
            }
        }
    }

    throw new InvalidOperationException($"Unable to find method body end: {methodName}");
}

static string FindRepositoryFile(string fileName)
{
    foreach (var startDirectory in new[]
    {
        Environment.CurrentDirectory,
        AppContext.BaseDirectory,
    })
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }
    }

    throw new FileNotFoundException($"Unable to locate repository file: {fileName}", fileName);
}

static void RunHeroMetricSeverityStyleChecks()
{
    Require(HeroMetricSeverityStyle.ForTemperature(52).SemanticName == "Normal", "52 C should be normal for the hero temperature metric.");
    Require(HeroMetricSeverityStyle.ForTemperature(65).SemanticName == "Warning", "65 C should be warning for the hero temperature metric.");
    Require(HeroMetricSeverityStyle.ForTemperature(75).SemanticName == "Caution", "75 C should be caution for the hero temperature metric.");
    Require(HeroMetricSeverityStyle.ForTemperature(82).SemanticName == "Critical", "82 C should be critical for the hero temperature metric.");

    Require(HeroMetricSeverityStyle.ForFanRpm(3320).SemanticName == "Normal", "3320 RPM should be normal for the hero fan metric.");
    Require(HeroMetricSeverityStyle.ForFanRpm(2200).SemanticName == "Warning", "2200 RPM should be warning for the hero fan metric.");
    Require(HeroMetricSeverityStyle.ForFanRpm(1200).SemanticName == "Caution", "1200 RPM should be caution for the hero fan metric.");
    Require(HeroMetricSeverityStyle.ForFanRpm(0).SemanticName == "Critical", "0 RPM should be critical for the hero fan metric.");

    Require(HeroMetricSeverityStyle.ForPowerWatts(294).SemanticName == "Normal", "294 W should be normal for the hero power metric.");
    Require(HeroMetricSeverityStyle.ForPowerWatts(550).SemanticName == "Warning", "550 W should be warning for the hero power metric.");
    Require(HeroMetricSeverityStyle.ForPowerWatts(750).SemanticName == "Caution", "750 W should be caution for the hero power metric.");
    Require(HeroMetricSeverityStyle.ForPowerWatts(950).SemanticName == "Critical", "950 W should be critical for the hero power metric.");

    Require(HeroMetricSeverityStyle.ForVoltage(234).SemanticName == "Normal", "234 V should be normal for the hero voltage metric.");
    Require(HeroMetricSeverityStyle.ForVoltage(205).SemanticName == "Warning", "205 V should be warning for the hero voltage metric.");
    Require(HeroMetricSeverityStyle.ForVoltage(195).SemanticName == "Caution", "195 V should be caution for the hero voltage metric.");
    Require(HeroMetricSeverityStyle.ForVoltage(180).SemanticName == "Critical", "180 V should be critical for the hero voltage metric.");

    Require(HeroMetricSeverityStyle.ForCurrentAmps(1.4).SemanticName == "Normal", "1.4 A should be normal for the hero current metric.");
    Require(HeroMetricSeverityStyle.ForCurrentAmps(5).SemanticName == "Warning", "5 A should be warning for the hero current metric.");
    Require(HeroMetricSeverityStyle.ForCurrentAmps(7).SemanticName == "Caution", "7 A should be caution for the hero current metric.");
    Require(HeroMetricSeverityStyle.ForCurrentAmps(9).SemanticName == "Critical", "9 A should be critical for the hero current metric.");

    Require(HeroMetricSeverityStyle.ForTemperature(null).SemanticName == "Unknown", "Missing hero values should use the unknown metric color.");
}

static void RunPollingSkipLogGateChecks()
{
    var gate = new PollingSkipLogGate();
    Require(gate.ShouldLog(PollingSkipKind.PreviousPollRunning), "The first previous-poll skip in a busy period should be logged.");
    Require(!gate.ShouldLog(PollingSkipKind.PreviousPollRunning), "Repeated previous-poll skips in the same busy period should not spam logs.");
    gate.Reset(PollingSkipKind.PreviousPollRunning);
    Require(gate.ShouldLog(PollingSkipKind.PreviousPollRunning), "Previous-poll skip logging should resume after the next real poll starts.");

    Require(gate.ShouldLog(PollingSkipKind.IpmiCommandBusy), "The first IPMI-busy skip in a busy period should be logged.");
    Require(!gate.ShouldLog(PollingSkipKind.IpmiCommandBusy), "Repeated IPMI-busy skips in the same busy period should not spam logs.");
    gate.Reset(PollingSkipKind.IpmiCommandBusy);
    Require(gate.ShouldLog(PollingSkipKind.IpmiCommandBusy), "IPMI-busy skip logging should resume after polling acquires the IPMI lock again.");

    Require(gate.ShouldLog(PollingSkipKind.AutoPolicyTickRunning), "The first overlapping auto-policy tick skip should be logged.");
    Require(!gate.ShouldLog(PollingSkipKind.AutoPolicyTickRunning), "Repeated overlapping auto-policy tick skips in the same busy period should not spam logs.");
    gate.Reset(PollingSkipKind.AutoPolicyTickRunning);
    Require(gate.ShouldLog(PollingSkipKind.AutoPolicyTickRunning), "Overlapping auto-policy tick logging should resume after the next real auto tick starts.");

    Require(gate.ShouldLog(PollingSkipKind.AutoPolicyIpmiBusy), "The first auto-policy IPMI-busy skip should be logged.");
    Require(!gate.ShouldLog(PollingSkipKind.AutoPolicyIpmiBusy), "Repeated auto-policy IPMI-busy skips in the same busy period should not spam logs.");
    gate.Reset(PollingSkipKind.AutoPolicyIpmiBusy);
    Require(gate.ShouldLog(PollingSkipKind.AutoPolicyIpmiBusy), "Auto-policy IPMI-busy skip logging should resume after auto policy acquires the IPMI lock again.");

    Require(!PollingSkipLogGate.OpenTopStatusForSkippedTick, "Skipped background ticks should not open or overwrite the top InfoBar.");
}

static void RunSensorValueLocalizationChecks()
{
    Require(LocalizationService.SupportedLanguages.Count > 0, "Sensor value i18n checks need at least one supported UI language.");
    Require(LocalizationService.SensorValueTranslationKeys.Count > 0, "Sensor value i18n checks need registered sensor value mappings.");

    foreach (var mapping in LocalizationService.SensorValueTranslationKeys)
    {
        Require(!string.IsNullOrWhiteSpace(mapping.Key), "Sensor value translation tokens must not be empty.");
        Require(!string.IsNullOrWhiteSpace(mapping.Value), $"Sensor value token '{mapping.Key}' must point to a translation key.");

        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var resourceValue = LocalizationService.T(mapping.Value, language.Code);
            Require(
                !string.IsNullOrWhiteSpace(resourceValue),
                $"Sensor value token '{mapping.Key}' must have a non-empty {language.Code} translation for {mapping.Value}.");

            var translatedValue = LocalizationService.TranslateSensorValue($"  {mapping.Key.ToUpperInvariant()}  ", language.Code);
            Require(
                translatedValue == resourceValue,
                $"Sensor value token '{mapping.Key}' should translate through the public sensor-value API in {language.Code}.");
        }
    }

    Require(
        LocalizationService.T("SensorDisplay.PowerOptimized", "zh-CN") == "电源优化策略",
        "Power Optimized should display as a readable policy name in Chinese.");
    Require(
        LocalizationService.T("SensorDisplay.PowerOptimized", "en-US") == "Power optimization policy",
        "Power Optimized should display as a readable policy name in English.");
    Require(
        LocalizationService.TranslateSensorValue("General Chassis Intrusion", "zh-CN") == "机箱入侵状态",
        "General chassis intrusion should not use redundant generic wording in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Presence Detected", "zh-CN") == "设备在位",
        "Presence detected should display as a concise device presence state in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Present", "zh-CN") == "设备在位",
        "Present should display with a clear subject in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Not Present", "zh-CN") == "设备不在位",
        "Not present should display with a clear subject in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Absent", "zh-CN") == "设备不在位",
        "Absent should display with a clear subject in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("State Asserted", "zh-CN") == "事件已触发" &&
        LocalizationService.TranslateSensorValue("Asserted", "zh-CN") == "事件已触发",
        "Asserted states should display as a user-readable event trigger in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("State Deasserted", "zh-CN") == "事件已解除" &&
        LocalizationService.TranslateSensorValue("Deasserted", "zh-CN") == "事件已解除",
        "Deasserted states should display as a user-readable event clear in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Bus Uncorrectable error", "zh-CN") == "总线硬件错误（不可纠正）",
        "Bus Uncorrectable error must display as a readable hardware error in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("Bus Uncorrectable error", "en-US") == "Uncorrectable bus hardware error",
        "Bus Uncorrectable error must display as a readable hardware error in English.");
    Require(
        LocalizationService.TranslateSensorValue("Unknown", "zh-CN") == "未知状态",
        "Unknown should display as an explicit unknown state in Chinese.");
    Require(
        LocalizationService.TranslateSensorValue("OEM Specific", "zh-CN") == "Dell 自定义状态",
        "OEM Specific should display as a readable Dell custom state in Chinese instead of the vague literal vendor wording.");
    Require(
        LocalizationService.TranslateSensorValue("Vendor specific", "zh-CN") == "Dell 自定义状态",
        "Vendor specific should share the same readable Dell-specific Chinese state wording.");
    Require(
        LocalizationService.TranslateSensorValue("OEM Specific", "en-US") == "Dell custom state",
        "OEM Specific should display as a readable Dell custom state in English.");
    Require(
        LocalizationService.TranslateSensorValue("Vendor specific", "en-US") == "Dell custom state",
        "Vendor specific should share the same readable Dell-specific English state wording.");
    Require(
        LocalizationService.TranslateSensorValue("Vendor future state", "zh-CN") == "Vendor future state",
        "Unknown BMC sensor values should stay raw instead of being guessed.");
}

static void RunSensorDisplayNameLocalizationChecks()
{
    Require(LocalizationService.SensorDisplayNameTranslationKeys.Count > 0, "Sensor display-name i18n checks need registered display-name mappings.");
    var criticalSensorResourceKeys = new[]
    {
        "Dashboard.TypeTemperature",
        "SensorDisplay.Redundancy",
        "SensorDisplay.UsbOverCurrent",
        "SensorDisplay.RombBattery",
        "SensorDisplay.CmosBattery",
        "SensorDisplay.HardwareEvent",
    };

    foreach (var mapping in LocalizationService.SensorDisplayNameTranslationKeys)
    {
        Require(!string.IsNullOrWhiteSpace(mapping.Key), "Sensor display-name tokens must not be empty.");
        Require(!string.IsNullOrWhiteSpace(mapping.Value), $"Sensor display-name token '{mapping.Key}' must point to a translation key.");

        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var resourceValue = LocalizationService.T(mapping.Value, language.Code);
            Require(!string.IsNullOrWhiteSpace(resourceValue), $"Sensor display-name token '{mapping.Key}' must have a {language.Code} translation.");
            Require(
                LocalizationService.TranslateSensorDisplayName($"  {mapping.Key.ToUpperInvariant()}  ", language.Code) == resourceValue,
                $"Sensor display-name token '{mapping.Key}' should translate through the public API in {language.Code}.");
        }
    }

    foreach (var language in LocalizationService.SupportedLanguages)
    {
        foreach (var resourceKey in criticalSensorResourceKeys)
        {
            var value = LocalizationService.T(resourceKey, language.Code).Trim();
            Require(value.Length > 1, $"{resourceKey} in {language.Code} should be a readable localized label, not a single-letter placeholder.");
            Require(
                !value.Contains("<<<", StringComparison.Ordinal) && !value.Contains(">>>", StringComparison.Ordinal),
                $"{resourceKey} in {language.Code} should not contain generation markers.");
        }

        var rombBattery = LocalizationService.T("SensorDisplay.RombBattery", language.Code);
        Require(
            rombBattery.Contains("ROMB", StringComparison.OrdinalIgnoreCase),
            $"ROMB battery translations must keep the ROMB hardware term in {language.Code}.");

        var cmosBattery = LocalizationService.T("SensorDisplay.CmosBattery", language.Code);
        Require(
            cmosBattery.Contains("CMOS", StringComparison.OrdinalIgnoreCase),
            $"CMOS battery translations must keep the CMOS hardware term in {language.Code}.");
    }

    Require(
        LocalizationService.TranslateSensorDisplayName("Redundancy", "zh-CN") == "冗余状态",
        "Bare Redundancy sensor titles should be localized instead of displayed as fixed English.");
    Require(
        LocalizationService.TranslateSensorDisplayName("USB Over-current", "zh-CN") == "USB 过流",
        "USB Over-current sensor titles should be localized instead of displayed as fixed English.");
    Require(
        LocalizationService.TranslateSensorDisplayName("ROMB Battery", "zh-CN") == "ROMB 电池",
        "ROMB Battery sensor titles should be localized instead of displayed as fixed English.");
    Require(
        LocalizationService.TranslateSensorDisplayName("CMOS Battery", "zh-CN") == "CMOS 电池",
        "CMOS Battery sensor titles should be localized instead of displayed as fixed English.");
    Require(
        LocalizationService.TranslateSensorDisplayName("Temp", "zh-CN") == "温度",
        "Bare Temp sensor titles should be localized in hero realtime detail rows instead of displayed as fixed English.");
    Require(
        LocalizationService.T("SensorDisplay.HardwareEvent", "zh-CN") == "硬件事件 {0}",
        "Unknown English SDR event titles should have a localized generic hardware-event label available.");
    Require(
        LocalizationService.TranslateSensorDisplayName("Vendor future sensor", "zh-CN") == "Vendor future sensor",
        "Unknown BMC sensor names should stay raw instead of being guessed.");
}

static void RunSupportedLanguageCatalogChecks()
{
    var expectedLanguages = new (string Code, string DisplayName)[]
    {
        ("en-US", "English"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
        ("ko-KR", "한국어"),
        ("de-DE", "Deutsch"),
        ("es-ES", "Español"),
        ("fr-FR", "Français"),
        ("it-IT", "Italiano"),
        ("da-DK", "Dansk"),
        ("ja-JP", "日本語"),
        ("pl-PL", "Polski"),
        ("ru-RU", "Русский"),
        ("bs-BA", "Bosanski"),
        ("ar-SA", "العربية"),
        ("nb-NO", "Norsk"),
        ("pt-BR", "Português (Brasil)"),
        ("th-TH", "ไทย"),
        ("tr-TR", "Türkçe"),
        ("uk-UA", "Українська"),
        ("bn-BD", "বাংলা"),
        ("el-GR", "Ελληνικά"),
        ("vi-VN", "Tiếng Việt"),
    };

    Require(
        LocalizationService.SupportedLanguages.Count == expectedLanguages.Length,
        "The supported UI language catalog should include every requested README language.");

    foreach (var expected in expectedLanguages)
    {
        var option = LocalizationService.SupportedLanguages.SingleOrDefault(language => language.Code == expected.Code);
        Require(option is not null, $"Supported language catalog should include {expected.Code}.");
        if (option is null)
        {
            throw new InvalidOperationException($"Supported language catalog should include {expected.Code}.");
        }

        Require(
            option.DisplayName == expected.DisplayName,
            $"{expected.Code} should be shown with its native language name in the selector.");
        Require(
            LocalizationService.IsSupportedLanguage(expected.Code),
            $"{expected.Code} should have a registered UI resource pack.");
    }
}

static void RunAllLanguageResourceCompletenessChecks()
{
    var resources = GetLocalizationResources();
    var referenceKeys = resources["zh-CN"].Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
    Require(referenceKeys.Length > 0, "Chinese resources should expose the full reference key set.");

    foreach (var language in LocalizationService.SupportedLanguages)
    {
        Require(resources.ContainsKey(language.Code), $"{language.Code} should have a concrete resource dictionary.");
        var languageResources = resources[language.Code];
        foreach (var key in referenceKeys)
        {
            Require(languageResources.ContainsKey(key), $"{language.Code} is missing UI translation key {key}.");
            Require(!string.IsNullOrWhiteSpace(languageResources[key]), $"{language.Code}/{key} must not be blank.");
            Require(
                !languageResources[key].Contains("<<<", StringComparison.Ordinal) &&
                !languageResources[key].Contains(">>>", StringComparison.Ordinal),
                $"{language.Code}/{key} must not contain translation generation markers.");
            Require(LocalizationService.T(key, language.Code) == languageResources[key], $"{language.Code}/{key} should resolve without fallback.");
        }
    }

    Require(LocalizationService.T("Action.Save", "de-DE") == "Einstellungen speichern", "German save action should be translated.");
    Require(LocalizationService.T("Action.Save", "ja-JP") == "設定を保存", "Japanese save action should be translated.");
    Require(LocalizationService.T("Action.Save", "ar-SA") == "حفظ الإعدادات", "Arabic save action should be translated.");
Require(LocalizationService.T("Dashboard.FanShortLabel", "zh-CN") == "{0}号", "Chinese chart fan short label should stay localized.");
Require(LocalizationService.T("Dashboard.FanShortLabel", "en-US") == "Fan {0}", "English chart fan short label should stay localized.");
Require(LocalizationService.T("Dashboard.HistoryRange", "zh-CN") == "历史范围", "Chinese dashboard history range label should be localized.");
Require(LocalizationService.T("Dashboard.HistoryRange", "en-US") == "History range", "English dashboard history range label should be localized.");
Require(LocalizationService.T("Dashboard.Range6Hours", "zh-CN") == "近 6 小时", "Chinese dashboard six-hour range should be localized.");
Require(LocalizationService.T("Dashboard.Range7Days", "en-US") == "Last 7 days", "English dashboard seven-day range should be localized.");
Require(LocalizationService.T("Dashboard.RangeCustom", "zh-CN") == "自定义", "Chinese dashboard custom range should be localized.");
Require(LocalizationService.T("Dashboard.NoHistoryData", "en-US") == "No history data in this range", "English dashboard empty history message should be localized.");
Require(LocalizationService.T("Sensors.Id", "zh-CN") == "编号", "Chinese sensor ID header should not remain the fixed English abbreviation.");
Require(LocalizationService.T("Sensors.Id", "zh-TW") == "編號", "Traditional Chinese sensor ID header should not remain the fixed English abbreviation.");
Require(LocalizationService.T("Settings.Host", "zh-CN") == "管理控制器地址", "Chinese BMC/iDRAC host field should be localized.");
}

static Dictionary<string, Dictionary<string, string>> GetLocalizationResources()
{
    var field = typeof(LocalizationService).GetField("Resources", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to inspect localization resources.");
    return (Dictionary<string, Dictionary<string, string>>)field.GetValue(null)!;
}

static void RunLanguageSelectorXamlChecks()
{
    var xaml = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));
    var mainWindowXaml = File.ReadAllText(FindRepositoryFile("MainWindow.xaml"));
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));

    Require(
        !xaml.Contains("<ComboBoxItem Content=\"简体中文\" Tag=\"zh-CN\"", StringComparison.Ordinal) &&
        !xaml.Contains("<ComboBoxItem Content=\"English\" Tag=\"en-US\"", StringComparison.Ordinal),
        "Language selector should be populated from LocalizationService.SupportedLanguages instead of hardcoded ComboBoxItem entries.");
    Require(
        pageSource.Contains("LanguageOption", StringComparison.Ordinal) &&
        pageSource.Contains("LanguageComboBox.ItemsSource = LocalizationService.SupportedLanguages", StringComparison.Ordinal),
        "MainPage should bind the language selector to the native-name language catalog.");
    Require(
        pageSource.Contains("SelectedItem is LanguageOption", StringComparison.Ordinal),
        "MainPage should read the selected language by code instead of by translated text or fixed index.");
    Require(
        !mainWindowXaml.Contains("Title=\"R730XD 智控风扇中心\"", StringComparison.Ordinal),
        "MainWindow XAML should not keep a fixed Chinese title; the runtime title is localized through App.Title.");
    Require(
        !pageSource.Contains("message.Contains(\"跳过\"", StringComparison.Ordinal) &&
        !pageSource.Contains("message.Contains(\"skipped\"", StringComparison.Ordinal) &&
        !pageSource.Contains("message.Contains(\"Requesting\"", StringComparison.Ordinal),
        "Hero request status classification should use localized resource templates instead of fixed Chinese or English words.");
}

static void RunVisibleXamlLocalizationCoverageChecks()
{
    var visibleAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Text",
        "Content",
        "Header",
        "PlaceholderText",
        "Message",
        "Label",
        "Title",
        "ToolTip",
        "Name",
    };
    var benignValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--",
        "-- °C",
        "192.168.1.73",
        "30°C",
        "95°C",
        "100%",
        "%",
    };
    XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    var uncovered = new List<string>();

    foreach (var fileName in new[] { "MainPage.xaml", "MainWindow.xaml" })
    {
        var path = FindRepositoryFile(fileName);
        var document = XDocument.Load(path, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException($"{fileName} should have a XAML root element.");
        foreach (var element in new[] { root }.Concat(root.Descendants()))
        {
            var hasLocalizationKey = element.Attributes().Any(attribute => attribute.Name.LocalName == "Localization.Key") ||
                element.Ancestors().Any(ancestor => ancestor.Attributes().Any(attribute => attribute.Name.LocalName == "Localization.Key"));

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    attribute.Name.Namespace == xamlNamespace ||
                    !visibleAttributes.Contains(attribute.Name.LocalName))
                {
                    continue;
                }

                var value = attribute.Value.Trim();
                if (value.Length == 0 ||
                    value.StartsWith('{') ||
                    benignValues.Contains(value) ||
                    hasLocalizationKey)
                {
                    continue;
                }

                var lineInfo = (IXmlLineInfo)element;
                var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
                uncovered.Add($"{fileName}:{line} {element.Name.LocalName}.{attribute.Name.LocalName}=\"{value}\"");
            }
        }
    }

    Require(
        uncovered.Count == 0,
        "Visible XAML strings should be localized through Localization.Key or populated by runtime state: " + string.Join("; ", uncovered.Take(20)));
}

static void RunDashboardHtmlLocalizationCoverageChecks()
{
    var html = File.ReadAllText(FindRepositoryFile(Path.Combine("Assets", "Charts", "dashboard.html")));

    Require(
        html.Contains("<html lang=\"zh-CN\">", StringComparison.Ordinal),
        "Dashboard HTML should use the app's Chinese default language before runtime payload localization is applied.");
    Require(
        !html.Contains("payload.language || \"en\"", StringComparison.Ordinal),
        "Dashboard HTML should not fall back to an English language code when no payload language is present.");
    Require(
        !Regex.IsMatch(html, @">\s*(Loading|No data|History|Temperature|Fan|Power|Status|Current|Voltage|Warning|OK)\s*<", RegexOptions.IgnoreCase),
        "Dashboard HTML visible shell text should be populated from localized payload labels instead of fixed English text nodes.");
}

static void RunPackageManifestLocalizationCoverageChecks()
{
    XNamespace manifest = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    var packageManifest = XDocument.Load(FindRepositoryFile("Package.appxmanifest"), LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    var propertiesDisplayName = packageManifest.Root?
        .Element(manifest + "Properties")?
        .Element(manifest + "DisplayName")?
        .Value;
    var visualElements = packageManifest.Descendants(uap + "VisualElements").Single();

    Require(
        propertiesDisplayName == "ms-resource:AppDisplayName",
        "Package manifest Properties.DisplayName should use a resource reference instead of fixed text.");
    Require(
        visualElements.Attribute("DisplayName")?.Value == "ms-resource:AppDisplayName",
        "Package manifest VisualElements.DisplayName should use a resource reference instead of fixed text.");
    Require(
        visualElements.Attribute("Description")?.Value == "ms-resource:AppDescription",
        "Package manifest VisualElements.Description should use a resource reference instead of fixed text.");

    foreach (var language in LocalizationService.SupportedLanguages)
    {
        var reswPath = FindRepositoryFile(Path.Combine("Strings", language.Code, "Resources.resw"));
        var resources = XDocument.Load(reswPath);
        Require(
            ReadReswValue(resources, "AppDisplayName") == LocalizationService.T("App.Title", language.Code),
            $"{language.Code} package AppDisplayName should match the runtime localized app title.");
        Require(
            ReadReswValue(resources, "AppDescription") == LocalizationService.T("Hero.Subtitle", language.Code),
            $"{language.Code} package AppDescription should match the runtime localized app subtitle.");
    }
}

static string ReadReswValue(XDocument resources, string name)
{
    var value = resources.Root?
        .Elements("data")
        .SingleOrDefault(element => element.Attribute("name")?.Value == name)?
        .Element("value")?
        .Value;

    Require(!string.IsNullOrWhiteSpace(value), $"Package resource {name} should be present and non-empty.");
    return value!;
}

static void RunInfoBarAccessibilityLocalizationChecks()
{
    XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    var document = XDocument.Load(FindRepositoryFile("MainPage.xaml"), LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    var infoBars = document.Descendants().Where(element => element.Name.LocalName == "InfoBar").ToArray();

    foreach (var infoBarName in new[] { "StatusInfoBar", "IndividualFanInfoBar" })
    {
        var infoBar = infoBars.SingleOrDefault(element => element.Attribute(xamlNamespace + "Name")?.Value == infoBarName);
        Require(infoBar is not null, $"{infoBarName} should exist in MainPage.xaml.");
        Require(
            string.Equals(infoBar!.Attribute("IsIconVisible")?.Value, "False", StringComparison.Ordinal),
            $"{infoBarName} should hide WinUI's default InfoBar icon so UI Automation does not expose fixed English icon names.");
    }
}

static void RunLogLevelStyleChecks()
{
    LocalizationService.SetLanguage("zh-CN");
    var zhInfo = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Info"));
    var zhWarn = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Warn"));
    var zhOk = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Ok"));
    var zhError = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Error"));
    var zhFail = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Fail"));

    Require(zhInfo.SemanticName == "Info", "Chinese info log level should use the info color state.");
    Require(zhWarn.SemanticName == "Warning", "Chinese warning log level should use the warning color state.");
    Require(zhOk.SemanticName == "Success", "Chinese success log level should use the success color state.");
    Require(zhError.SemanticName == "Error", "Chinese error log level should use the error color state.");
    Require(zhFail.SemanticName == "Error", "Chinese failed command log level should use the error color state.");
    Require(zhWarn.ForegroundHex != zhError.ForegroundHex, "Warnings must not use the red error color.");
    Require(zhInfo.ForegroundHex != zhWarn.ForegroundHex, "Info and warning log levels should be visually distinct.");
    Require(zhOk.ForegroundHex != zhWarn.ForegroundHex, "Success and warning log levels should be visually distinct.");

    LocalizationService.SetLanguage("en-US");
    var enInfo = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Info"));
    var enWarn = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Warn"));
    var enOk = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Ok"));
    var enError = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Error"));
    var enFail = LogLevelStyle.FromDisplayLevel(LocalizationService.T("Log.Fail"));

    Require(enInfo.SemanticName == "Info", "English info log level should use the info color state.");
    Require(enWarn.SemanticName == "Warning", "English warning log level should use the warning color state.");
    Require(enOk.SemanticName == "Success", "English success log level should use the success color state.");
    Require(enError.SemanticName == "Error", "English error log level should use the error color state.");
    Require(enFail.SemanticName == "Error", "English failed command log level should use the error color state.");
    Require(enWarn.ForegroundHex != enError.ForegroundHex, "English warnings must not use the red error color.");

    var explicitWarning = LogLevelStyle.FromSemanticLevel("Warning");
    var explicitError = LogLevelStyle.FromSemanticLevel("Error");
    Require(explicitWarning.SemanticName == "Warning", "Stable warning log semantics should not depend on the localized display label.");
    Require(explicitError.SemanticName == "Error", "Stable error log semantics should not depend on the localized display label.");
    LocalizationService.SetLanguage("zh-CN");
    var logEntrySource = File.ReadAllText(FindRepositoryFile(Path.Combine("Models", "LogEntry.cs")));
    Require(
        logEntrySource.Contains("Level { get; set; } = LocalizationService.T(\"Log.Info\")", StringComparison.Ordinal),
        "Default visible log entry level should use the active UI language instead of fixed English.");
}

static void RunAppLogServiceChecks()
{
    var baseTime = new DateTimeOffset(2026, 6, 7, 10, 15, 30, TimeSpan.FromHours(8));

    using (var temp = TempDirectory.Create("R730xdAppLogRecordTests"))
    {
        var log = new AppLogService(temp.Path, () => baseTime);
        log.Write(new AppLogRecord
        {
            Level = "Info",
            Category = "UnitTest",
            EventName = "AtomicRecord",
            Message = "line one" + Environment.NewLine + "line two",
            Properties = new Dictionary<string, string>
            {
                ["scope"] = "atomic-jsonl",
            },
        });
        log.FlushAsync().GetAwaiter().GetResult();

        var lines = File.ReadAllLines(log.CurrentLogPath);
        Require(lines.Length == 1, "A log event must be written as one atomic JSONL record.");

        using var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;
        Require(root.GetProperty("level").GetString() == "Info", "Log level should be serialized.");
        Require(root.GetProperty("category").GetString() == "UnitTest", "Log category should be serialized.");
        Require(root.GetProperty("eventName").GetString() == "AtomicRecord", "Log event name should be serialized.");
        Require(root.GetProperty("message").GetString()!.Contains("line two", StringComparison.Ordinal), "Multiline messages should stay inside the JSON value.");
        Require(root.GetProperty("properties").GetProperty("scope").GetString() == "atomic-jsonl", "Structured properties should be serialized.");
    }

    using (var temp = TempDirectory.Create("R730xdAppLogOperationTests"))
    {
        var times = new Queue<DateTimeOffset>(
        [
            baseTime,
            baseTime.AddMilliseconds(2450),
            baseTime.AddMilliseconds(5000),
            baseTime.AddMilliseconds(5800),
        ]);
        var log = new AppLogService(temp.Path, () => times.Dequeue());
        var success = log.StartOperation("RefreshSensors", "Reading BMC sensors");
        success.Succeed("BMC sensors refreshed");

        var failure = log.StartOperation("SetFans", "Setting all fans to 35%");
        failure.Fail(new InvalidOperationException("raw command rejected"));
        log.FlushAsync().GetAwaiter().GetResult();

        var lines = File.ReadAllLines(log.CurrentLogPath);
        Require(lines.Length == 4, "Each operation must write explicit start and terminal records.");

        using var startedDocument = JsonDocument.Parse(lines[0]);
        using var succeededDocument = JsonDocument.Parse(lines[1]);
        using var failedDocument = JsonDocument.Parse(lines[3]);
        var started = startedDocument.RootElement;
        var succeeded = succeededDocument.RootElement;
        var failed = failedDocument.RootElement;
        var operationId = started.GetProperty("operationId").GetString();

        Require(started.GetProperty("phase").GetString() == "Started", "Operation start should be marked as Started.");
        Require(succeeded.GetProperty("phase").GetString() == "Succeeded", "Successful operation end should be marked as Succeeded.");
        Require(succeeded.GetProperty("operationId").GetString() == operationId, "Start and terminal operation records should share an operation id.");
        Require(succeeded.GetProperty("durationMilliseconds").GetDouble() == 2450, "Operation duration should be calculated from start and finish timestamps.");
        Require(succeeded.GetProperty("succeeded").GetBoolean(), "Successful terminal record should expose succeeded=true.");
        Require(failed.GetProperty("phase").GetString() == "Failed", "Failed operation end should be marked as Failed.");
        Require(!failed.GetProperty("succeeded").GetBoolean(), "Failed terminal record should expose succeeded=false.");
        Require(failed.GetProperty("errorType").GetString() == nameof(InvalidOperationException), "Failed terminal record should include the exception type.");
        Require(failed.GetProperty("errorMessage").GetString() == "raw command rejected", "Failed terminal record should include the exception message.");
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static int CountOccurrences(string source, string value)
{
    var count = 0;
    var index = 0;
    while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

static void RequireThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}, got {ex.GetType().Name}.", ex);
    }

    throw new InvalidOperationException(message);
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempDirectory Create(string name)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
