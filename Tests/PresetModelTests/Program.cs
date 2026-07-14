using DellR730xdFanControlCenter;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

var fakeIpmiLogPath = Environment.GetEnvironmentVariable("R730XD_FAKE_IPMI_LOG_PATH");
if (!string.IsNullOrWhiteSpace(fakeIpmiLogPath))
{
    return RunFakeIpmiProcess(args, fakeIpmiLogPath);
}

try
{
var defaults = FanPreset.CreateDefaultPresets();
Require(defaults.Count >= 5, "Default preset list should include restore, balanced, cooling, performance, and Dell automatic presets.");
Require(defaults.All(preset => preset.CanDelete), "Starter presets should expose the same delete action as custom presets.");
Require(new AppSettings().SensorRefreshSeconds == 1, "Default SDR polling interval should remain the original 1 second setting.");

var wheelScrollState = new VisualizationWheelScrollState();
var rapidWheelTarget = 0d;
for (var wheelIndex = 0; wheelIndex < 5; wheelIndex++)
{
    var inFlightAnimatedOffset = wheelIndex * 30d;
    rapidWheelTarget = wheelScrollState.Accumulate(inFlightAnimatedOffset, 100d, 2000d, wheelIndex * 20L);
}

Require(
    rapidWheelTarget == 500d,
    "Rapid wheel input should accumulate from the prior animation target instead of losing unfinished distance by reading the in-flight visual offset.");
var nextWheelBurstTarget = wheelScrollState.Accumulate(320d, 100d, 2000d, VisualizationWheelScrollState.BurstTimeoutMilliseconds + 100L);
Require(
    nextWheelBurstTarget == 420d,
    "A later wheel burst should start from the current page offset so scrollbar or navigation changes cannot leave a stale target behind.");

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
Require(curve.CalculateFanPercent(55) == 22, "Smooth curve mode should evaluate the configured points with constrained monotone tangents.");
curve.SmoothCurve = false;

var monotoneTemperatureCurve = new FanPreset
{
    Kind = FanPreset.CurveKind,
    SmoothCurve = true,
    CurvePoints =
    [
        new FanCurvePoint { TemperatureCelsius = 40, FanPercent = 10 },
        new FanCurvePoint { TemperatureCelsius = 50, FanPercent = 20 },
        new FanCurvePoint { TemperatureCelsius = 60, FanPercent = 80 },
        new FanCurvePoint { TemperatureCelsius = 70, FanPercent = 80 },
    ],
};
Require(
    monotoneTemperatureCurve.CalculateFanPercent(45) == 13,
    "Smooth temperature curves should use a shared monotone tangent instead of flattening every interval independently.");
Require(
    monotoneTemperatureCurve.CalculateFanPercent(65) == 80,
    "Smooth temperature curves should preserve flat control-point intervals exactly.");
var previousSmoothTemperaturePercent = monotoneTemperatureCurve.CalculateFanPercent(40);
for (var temperature = 40.25; temperature <= 70; temperature += 0.25)
{
    var percent = monotoneTemperatureCurve.CalculateFanPercent(temperature);
    Require(
        percent >= previousSmoothTemperaturePercent && percent is >= 10 and <= 80,
        "Smooth temperature interpolation should remain monotone and never overshoot neighboring control points.");
    previousSmoothTemperaturePercent = percent;
}

curve.CurvePointsText = "50 = 20" + Environment.NewLine + "80 = 50";
curve.ApplyCurvePointsText();
Require(curve.CurvePoints.Count == 2, "Editable curve point text should replace the curve point list.");
Require(curve.CalculateFanPercent(65) == 35, "Edited curve point text should drive later current-temperature curve evaluation.");
curve.SmoothCurve = true;
Require(curve.CalculateFanPercent(60) == 30, "A two-point smooth temperature curve should reduce to its linear segment.");

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
Require(powerCurve.CalculateFanPercentForPower(350) == 21, "Power smooth curve mode should evaluate the configured points with constrained monotone tangents.");
powerCurve.SmoothCurve = false;

var monotonePowerCurve = new FanPreset
{
    Kind = FanPreset.PowerCurveKind,
    SmoothCurve = true,
    CurvePoints =
    [
        new FanCurvePoint { PowerWatts = 100, FanPercent = 10 },
        new FanCurvePoint { PowerWatts = 200, FanPercent = 20 },
        new FanCurvePoint { PowerWatts = 300, FanPercent = 80 },
        new FanCurvePoint { PowerWatts = 400, FanPercent = 80 },
    ],
};
Require(
    monotonePowerCurve.CalculateFanPercentForPower(150) == 13,
    "Smooth power curves should use the same monotone interpolation as temperature curves.");
Require(
    monotonePowerCurve.CalculateFanPercentForPower(350) == 80,
    "Smooth power curves should preserve flat control-point intervals exactly.");
for (var index = 0; index <= 120; index++)
{
    var temperature = 40 + (index * 0.25);
    var power = 100 + (index * 2.5);
    Require(
        monotoneTemperatureCurve.CalculateFanPercent(temperature) == monotonePowerCurve.CalculateFanPercentForPower(power),
        "Temperature and power smooth curves should evaluate equivalent normalized control points identically.");
}

powerCurve.CurvePointsText = "300W = 20" + Environment.NewLine + "700W = 50";
powerCurve.ApplyCurvePointsText();
Require(powerCurve.CurvePoints.Count == 2, "Editable power curve text should replace the power curve point list.");
Require(powerCurve.CalculateFanPercentForPower(500) == 35, "Edited power curve point text should drive later current-power curve evaluation.");
powerCurve.SmoothCurve = true;
Require(powerCurve.CalculateFanPercentForPower(400) == 28, "A two-point smooth power curve should reduce to its linear segment.");

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
RunLocalizationIntegrityChecks();
RunLanguageSelectorXamlChecks();
RunVisibleXamlLocalizationCoverageChecks();
RunInfoBarAccessibilityLocalizationChecks();
RunPackageManifestLocalizationCoverageChecks();
RunPublishScriptChecks();
RunDashboardHtmlLocalizationCoverageChecks();
RunRuntimeStatePersistenceSourceChecks();
RunStartupFailureBoundarySourceChecks();
RunSettingsStoreAtomicSaveSourceChecks();
RunSingleInstanceRaceSourceChecks();

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
Require(LocalizationService.T("Status.PowerCurveFanUnchanged").Contains("未下发风扇命令", StringComparison.Ordinal), "Chinese unchanged automatic fan target should clearly state that no fan command is sent.");
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
Require(LocalizationService.T("Action.RestoreDellFactoryFanSpeed").Contains("Dell 出厂设置转速", StringComparison.Ordinal), "Chinese factory restore action should preserve the Dell brand name.");
Require(LocalizationService.T("Action.StartPolling") == "开始轮询", "Chinese polling start action should describe starting sensor polling.");
Require(LocalizationService.T("Action.CancelPolling") == "取消轮询", "Chinese polling cancel action should describe canceling sensor polling.");
Require(LocalizationService.T("Tray.RestoreDefault").Contains("Dell 出厂设置转速", StringComparison.Ordinal), "Chinese tray restore action should preserve the Dell brand name.");
Require(LocalizationService.T("Tray.RefreshSensors") == "刷新传感器", "Chinese tray refresh action should be translated.");
Require(LocalizationService.T("Tray.OpenIdrac") == "打开 iDRAC 网页", "Chinese tray iDRAC action should preserve the iDRAC product name.");
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
Require(LocalizationService.T("Status.PowerCurveFanUnchanged").Contains("No fan command sent", StringComparison.Ordinal), "English unchanged automatic fan target should clearly state that no fan command is sent.");
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
    Require(
        !Directory.EnumerateFiles(tempSettingsDirectory, ".settings-*.tmp").Any(),
        "A successful settings replacement should not leave temporary files behind.");

    using (File.Open(settingsStore.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Exception? saveFailure = null;
        try
        {
            settingsStore.Save(new AppSettings { SensorRefreshSeconds = 2 });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveFailure = ex;
        }

        Require(
            saveFailure is not null,
            "A locked active settings file should expose the replacement failure instead of reporting a successful save.");
    }

    Require(
        settingsStore.Load().SensorRefreshSeconds == 1,
        "A failed settings replacement should preserve the previously active settings file.");
    Require(
        !Directory.EnumerateFiles(tempSettingsDirectory, ".settings-*.tmp").Any(),
        "A failed settings replacement should clean its unique temporary file.");

    settingsStore.Save(new AppSettings { Presets = savedPresets });
    var loaded = settingsStore.Load();
    var loadedBalanced = loaded.Presets.Single(preset => preset.Id == "balanced");
    Require(loadedBalanced.EditableName == "夜间静音", "Edited built-in preset name should survive settings save/load.");
    Require(loadedBalanced.EditableDetail == "晚上降低噪音的自定义说明", "Edited built-in preset description should survive settings save/load.");
    Require(loadedBalanced.Percent == 18, "Edited built-in preset percent should survive settings save/load.");

    var presetsWithoutBalanced = loaded.Presets
        .Where(preset => !preset.Id.Equals("balanced", StringComparison.OrdinalIgnoreCase))
        .Select(preset => preset.Clone())
        .ToList();
    settingsStore.Save(new AppSettings { Presets = presetsWithoutBalanced });
    var loadedWithoutBalanced = settingsStore.Load();
    Require(
        !loadedWithoutBalanced.Presets.Any(preset => preset.Id.Equals("balanced", StringComparison.OrdinalIgnoreCase)),
        "Deleting a starter preset should persist instead of being re-created on settings load.");

    settingsStore.Save(new AppSettings { Presets = [] });
    Require(
        settingsStore.Load().Presets.Count == 0,
        "Deleting every preset should persist an empty preset list instead of re-seeding starter presets.");

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
RunSensorReadingAvailabilityChecks();
RunDashboardSensorPresentationChecks();
RunDashboardTileViewModelBehaviorChecks();
RunDashboardSnapshotFreshnessChecks();
RunPollingSkipLogGateChecks();
RunIpmiCommandNoRetrySourceChecks();
RunFanCommandSafetyRecoveryChecks();
RunAutoPolicySamplingOwnershipSourceChecks();
RunAutoPolicyUnchangedTargetSkipSourceChecks();
RunAutoPolicyFailureContextSourceChecks();
RunIpmiTemperatureLookupChecks();
RunSensorSubtitleFormatterChecks();
RunHeroRealtimeSummaryLayoutXamlChecks();
RunHeroBannerDividerLayoutXamlChecks();
RunHeroThermalModeStatusXamlChecks();
RunNoPartialOverviewDisplaySourceChecks();
RunDashboardTileInPlaceUpdateChecks();
RunDashboardSensorIconXamlChecks();
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
RunWebView2UserDataSourceChecks();
RunUserCommandLogFlushSourceChecks();
RunReleaseVersionMetadataChecks();
RunTestHarnessFailureExitSourceChecks();

Console.WriteLine("Preset model checks passed.");
return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Preset model checks failed.");
    Console.Error.WriteLine(ex);
    return 1;
}

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
    var projectSource = File.ReadAllText(FindRepositoryFile("DellR730xdFanControlCenter.csproj"));

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
    Require(
        projectSource.Contains("<Platform Condition=\"'$(Platform)' == 'AnyCPU'\">x64</Platform>", StringComparison.Ordinal),
        "Direct dotnet tooling should map the unsupported WinUI AnyCPU default to x64 instead of loading duplicate generated sources.");
}

static void RunFanCommandSafetyRecoveryChecks()
{
    using var temp = TempDirectory.Create("R730xdFanCommandSafetyRecoveryTests");
    var logPath = Path.Combine(temp.Path, "fake-ipmi-commands.log");
    var environmentVariables = new[]
    {
        "R730XD_FAKE_IPMI_LOG_PATH",
        "R730XD_FAKE_IPMI_MANUAL_EXIT_CODE",
        "R730XD_FAKE_IPMI_SPEED_EXIT_CODE",
        "R730XD_FAKE_IPMI_AUTO_EXIT_CODE",
    };
    var previousValues = environmentVariables.ToDictionary(
        name => name,
        Environment.GetEnvironmentVariable,
        StringComparer.Ordinal);

    try
    {
        Environment.SetEnvironmentVariable("R730XD_FAKE_IPMI_LOG_PATH", logPath);
        var processPath = Environment.ProcessPath;
        Require(
            !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath),
            "The test executable must be available as a real child-process ipmitool substitute.");
        var profile = new IdracProfile
        {
            Host = "test-idrac.invalid",
            UserName = "test-user",
            Password = "test-password",
            IpmiToolPath = processPath!,
            CommandTimeoutSeconds = 5,
        };

        File.Delete(logPath);
        ConfigureFakeIpmiExitCodes(manual: 0, speed: 17, automatic: 0);
        var recoveredService = new IpmiCommandService();
        var recoveredTrace = new List<CommandTraceEventArgs>();
        recoveredService.CommandCompleted += (_, trace) => recoveredTrace.Add(trace);
        var recoveredFailure = CaptureException(
            () => recoveredService.SetAllFansManualSpeedAsync(profile, 35, CancellationToken.None).GetAwaiter().GetResult());
        Require(
            recoveredFailure.GetType().Name == "FanCommandSafetyRecoveryException",
            "A rejected fan percentage followed by successful Dell automatic recovery should surface the dedicated recovery exception.");
        RequireSequence(
            logPath,
            ["EnterManual", "SetFanSpeed", "RestoreDellAuto"],
            "A failed percentage command must trigger exactly one Dell automatic recovery after the confirmed manual-mode command.");
        Require(
            recoveredTrace.Count == 3 &&
            recoveredTrace[0].Succeeded &&
            !recoveredTrace[1].Succeeded &&
            recoveredTrace[2].Succeeded,
            "Command tracing must preserve the real success/failure/success result of all three child processes.");

        File.Delete(logPath);
        ConfigureFakeIpmiExitCodes(manual: 0, speed: 18, automatic: 19);
        var failedRecoveryService = new IpmiCommandService();
        var failedRecovery = CaptureException(
            () => failedRecoveryService.SetAllFansManualSpeedAsync(profile, 36, CancellationToken.None).GetAwaiter().GetResult());
        Require(
            failedRecovery.InnerException is AggregateException aggregate && aggregate.InnerExceptions.Count == 2,
            "When Dell automatic recovery also fails, the terminal exception must retain both the percentage failure and recovery failure.");
        RequireSequence(
            logPath,
            ["EnterManual", "SetFanSpeed", "RestoreDellAuto"],
            "A failed recovery must still be attempted exactly once and must never retry the rejected percentage command.");

        File.Delete(logPath);
        ConfigureFakeIpmiExitCodes(manual: 20, speed: 0, automatic: 0);
        var manualFailureService = new IpmiCommandService();
        _ = CaptureException(
            () => manualFailureService.SetAllFansManualSpeedAsync(profile, 37, CancellationToken.None).GetAwaiter().GetResult());
        RequireSequence(
            logPath,
            ["EnterManual"],
            "Dell automatic recovery must not run when entering manual mode itself was never confirmed successful.");

        var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
        Require(
            pageSource.Contains("catch (FanCommandSafetyRecoveryException", StringComparison.Ordinal) &&
            pageSource.Contains("MarkActivePreset(\"dell-auto\", persistRunningState: true)", StringComparison.Ordinal),
            "The UI failure boundary must visibly fail the requested command while aligning its summary and LastRunning state with the confirmed Dell automatic recovery.");
    }
    finally
    {
        foreach (var variable in environmentVariables)
        {
            Environment.SetEnvironmentVariable(variable, previousValues[variable]);
        }
    }
}

static int RunFakeIpmiProcess(string[] commandArguments, string logPath)
{
    var rawIndex = Array.FindIndex(commandArguments, argument => string.Equals(argument, "raw", StringComparison.Ordinal));
    var rawArguments = rawIndex >= 0 ? commandArguments[(rawIndex + 1)..] : [];
    string action;
    string exitCodeVariable;
    if (rawArguments.SequenceEqual(["0x30", "0x30", "0x01", "0x00"], StringComparer.OrdinalIgnoreCase))
    {
        action = "EnterManual";
        exitCodeVariable = "R730XD_FAKE_IPMI_MANUAL_EXIT_CODE";
    }
    else if (rawArguments.Length == 5 &&
             rawArguments.Take(3).SequenceEqual(["0x30", "0x30", "0x02"], StringComparer.OrdinalIgnoreCase))
    {
        action = "SetFanSpeed";
        exitCodeVariable = "R730XD_FAKE_IPMI_SPEED_EXIT_CODE";
    }
    else if (rawArguments.SequenceEqual(["0x30", "0x30", "0x01", "0x01"], StringComparer.OrdinalIgnoreCase))
    {
        action = "RestoreDellAuto";
        exitCodeVariable = "R730XD_FAKE_IPMI_AUTO_EXIT_CODE";
    }
    else
    {
        action = "UnexpectedCommand";
        exitCodeVariable = string.Empty;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    File.AppendAllText(logPath, action + Environment.NewLine);
    var configuredExitCode = string.IsNullOrEmpty(exitCodeVariable)
        ? 97
        : int.TryParse(Environment.GetEnvironmentVariable(exitCodeVariable), out var parsedExitCode)
            ? parsedExitCode
            : 0;
    if (configuredExitCode != 0)
    {
        Console.Error.WriteLine($"Fake ipmitool rejected {action} with exit code {configuredExitCode}.");
    }

    return configuredExitCode;
}

static void ConfigureFakeIpmiExitCodes(int manual, int speed, int automatic)
{
    Environment.SetEnvironmentVariable("R730XD_FAKE_IPMI_MANUAL_EXIT_CODE", manual.ToString(CultureInfo.InvariantCulture));
    Environment.SetEnvironmentVariable("R730XD_FAKE_IPMI_SPEED_EXIT_CODE", speed.ToString(CultureInfo.InvariantCulture));
    Environment.SetEnvironmentVariable("R730XD_FAKE_IPMI_AUTO_EXIT_CODE", automatic.ToString(CultureInfo.InvariantCulture));
}

static Exception CaptureException(Action action)
{
    try
    {
        action();
    }
    catch (Exception ex)
    {
        return ex;
    }

    throw new InvalidOperationException("The operation was expected to fail, but it completed successfully.");
}

static void RequireSequence(string path, IReadOnlyList<string> expected, string message)
{
    var actual = File.Exists(path) ? File.ReadAllLines(path) : [];
    Require(actual.SequenceEqual(expected, StringComparer.Ordinal), $"{message} Actual: [{string.Join(", ", actual)}].");
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

static void RunAutoPolicyUnchangedTargetSkipSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var tickStartIndex = source.IndexOf("private async Task<bool> RunAutoPolicyOnceCoreAsync", StringComparison.Ordinal);
    var percentIndex = source.IndexOf("var percent = CalculateFanPercentForAutoTick", tickStartIndex, StringComparison.Ordinal);
    var skipIndex = source.IndexOf("ShouldSkipUnchangedAutoPolicyFanCommand", percentIndex, StringComparison.Ordinal);
    var commandIndex = source.IndexOf("await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken)", percentIndex, StringComparison.Ordinal);
    var rememberIndex = source.IndexOf("RememberAutoPolicyFanTarget(targetKey, percent)", commandIndex, StringComparison.Ordinal);
    var postCommandRefreshIndex = source.IndexOf("await RefreshSensorsAfterFanCommandCoreAsync(profile, cancellationToken)", commandIndex, StringComparison.Ordinal);
    var readSensorsIndex = source.IndexOf("var readings = await _ipmi.ReadSensorsAsync(profile, cancellationToken)", tickStartIndex, StringComparison.Ordinal);
    var clearStaleIndex = source.IndexOf("ClearStaleFailureStatusAfterSensorRefreshSuccess();", readSensorsIndex, StringComparison.Ordinal);

    Require(
        tickStartIndex >= 0 &&
        percentIndex > tickStartIndex &&
        skipIndex > percentIndex &&
        commandIndex > skipIndex &&
        rememberIndex > commandIndex &&
        postCommandRefreshIndex > commandIndex,
        "Auto-policy ticks should skip unchanged calculated fan percentages before sending another all-fan raw command, and should read sensors again after an actual fan command.");
    Require(
        source.Contains("BuildAutoPolicyFanCommandProperties(cpuTemp, percent, powerWatts, \"SkipUnchangedFanPercent\")", StringComparison.Ordinal) &&
        source.Contains("ClearAutoPolicyFanTargetCache();", StringComparison.Ordinal),
        "Unchanged auto-policy skips should be logged explicitly, and manual/Dell/stop paths should clear the cached automatic fan target.");
    Require(
        source.Contains("ForceNextAutoPolicyFanCommand();", StringComparison.Ordinal) &&
        source.Contains("!_forceNextAutoPolicyFanCommand", StringComparison.Ordinal) &&
        source.Contains("[\"forceFanCommand\"] = _forceNextAutoPolicyFanCommand.ToString(CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
        source.Contains("ClearForcedAutoPolicyFanCommand();", StringComparison.Ordinal),
        "Starting or restoring an automatic policy should force the first fan command so a stale in-memory target cannot mask the real BMC state.");
    Require(
        readSensorsIndex > tickStartIndex &&
        clearStaleIndex > readSensorsIndex &&
        clearStaleIndex < skipIndex,
        "A successful auto-policy SDR read should clear stale top-level failure banners before any unchanged-target skip can return.");
}

static void RunAutoPolicyFailureContextSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var autoPolicyStart = source.IndexOf("private async Task<bool> RunAutoPolicyOnceCoreAsync", StringComparison.Ordinal);
    var autoPolicyEnd = source.IndexOf("private int CalculateAutoFanPercent", autoPolicyStart, StringComparison.Ordinal);
    var autoPolicyBody = autoPolicyStart >= 0 && autoPolicyEnd > autoPolicyStart
        ? source[autoPolicyStart..autoPolicyEnd]
        : string.Empty;
    Require(
        autoPolicyBody.Length > 0 &&
        autoPolicyBody.Contains("Dictionary<string, string>? fanCommandProperties = null;", StringComparison.Ordinal) &&
        autoPolicyBody.Contains("fanCommandProperties = BuildAutoPolicyFanCommandProperties(cpuTemp, percent, powerWatts, \"SetAllFansManualSpeed\");", StringComparison.Ordinal) &&
        autoPolicyBody.Contains("operation.Fail(ex, fanCommandProperties);", StringComparison.Ordinal),
        "Auto-policy failures after calculating a fan target should log the computed CPU temperature, power reading, target percent, and raw-command action with the failed operation.");
    Require(
        source.Contains("private static Dictionary<string, string> BuildAutoPolicyFanCommandProperties(double cpuTemp, int percent, double? powerWatts, string action)", StringComparison.Ordinal) &&
        source.Contains("[\"cpuTemperatureCelsius\"] = cpuTemp.ToString(\"0.0\", CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
        source.Contains("[\"fanPercent\"] = percent.ToString(CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
        source.Contains("[\"action\"] = action", StringComparison.Ordinal) &&
        source.Contains("properties[\"powerWatts\"] = powerWatts.Value.ToString(\"0.0\", CultureInfo.InvariantCulture);", StringComparison.Ordinal),
        "Auto-policy fan-command log properties should be built in one place so success, unchanged, and failed command paths keep the same diagnostic fields.");
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
    var overviewBody = ExtractMethodBody(source, "UpdateOverviewMetricSummaries");

    Require(
        !source.Contains(".Take(14)", StringComparison.Ordinal),
        "Power and health board should display every matching sensor instead of only the first 14.");
    Require(
        !source.Contains("items.Take(2)", StringComparison.Ordinal),
        "Overview summary detail text should display every item it receives instead of only the first two.");
    Require(
        overviewBody.Contains("SensorReadingAvailability.IsDisplayable", StringComparison.Ordinal),
        "Overview tiles and summaries should exclude unavailable SDR rows through the shared display-only availability policy.");
    Require(
        overviewBody.Contains("!IsTemperatureSensor(sensor) && !IsFanSensor(sensor)", StringComparison.Ordinal),
        "The status board should include every valid non-temperature/non-fan SDR item instead of a partial power/health allow-list.");
    Require(
        source.Contains("var sensors = Sensors.Where(SensorReadingAvailability.IsDisplayable).ToList();", StringComparison.Ordinal),
        "Chart snapshots should use the same real valid SDR rows as native boards without changing the raw control-data collection.");
    Require(
        source.Contains("foreach (var sensor in Sensors.Where(SensorReadingAvailability.IsDisplayable))", StringComparison.Ordinal),
        "The detailed sensor table should list every valid SDR row and omit unavailable rows.");
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
    var presentationProperties = new[]
    {
        (Type: "DashboardIconKind", Name: "IconKind", Field: "_iconKind"),
        (Type: "DashboardVisualState", Name: "VisualState", Field: "_visualState"),
        (Type: "DashboardMotionKind", Name: "MotionKind", Field: "_motionKind"),
        (Type: "double", Name: "NormalizedLevel", Field: "_normalizedLevel"),
        (Type: "double", Name: "MotionPeriodSeconds", Field: "_motionPeriodSeconds"),
        (Type: "bool", Name: "IsMotionActive", Field: "_isMotionActive"),
        (Type: "bool", Name: "IsDataFresh", Field: "_isDataFresh"),
    };
    Require(
        presentationProperties.All(property =>
            tileSource.Contains($"public {property.Type} {property.Name}", StringComparison.Ordinal) &&
            tileSource.Contains($"set => SetField(ref {property.Field}, value);", StringComparison.Ordinal)),
        "Dashboard tile presentation properties should notify bindings through SetField.");
    Require(
        tileSource.Contains("private DashboardIconKind _iconKind = DashboardIconKind.GenericStatus;", StringComparison.Ordinal) &&
        tileSource.Contains("private DashboardVisualState _visualState = DashboardVisualState.Unavailable;", StringComparison.Ordinal) &&
        tileSource.Contains("private DashboardMotionKind _motionKind = DashboardMotionKind.None;", StringComparison.Ordinal),
        "Dashboard tile presentation should default to a neutral unavailable icon with no motion.");
    Require(
        presentationProperties.All(property =>
            tileSource.Contains($"{property.Name} = next.{property.Name};", StringComparison.Ordinal)),
        "Dashboard tile in-place updates should copy every presentation and freshness property.");
    var legacyTileAnimationMembers = new[]
    {
        "TemperatureIconOpacity",
        "FanIconOpacity",
        "PowerIconOpacity",
        "VoltageIconOpacity",
        "CurrentIconOpacity",
        "HealthIconOpacity",
        "IsFanAnimated",
        "FanRotationSeconds",
        "ElectricalIconOpacity",
        "IsElectricalAnimated",
        "ElectricalPulseSeconds",
    };
    Require(
        legacyTileAnimationMembers.All(member => !tileSource.Contains(member, StringComparison.Ordinal)),
        "DashboardTileViewModel should remove every page-level opacity, storyboard, and legacy animation property after Composition owns motion.");
    Require(
        tileSource.Contains("public string AutomationVisualStateText", StringComparison.Ordinal) &&
        tileSource.Contains("public string AutomationFreshnessText", StringComparison.Ordinal) &&
        tileSource.Contains("AutomationVisualStateText = next.AutomationVisualStateText;", StringComparison.Ordinal) &&
        tileSource.Contains("AutomationFreshnessText = next.AutomationFreshnessText;", StringComparison.Ordinal),
        "Dashboard tile updates should carry localized semantic and freshness text into the computed automation name.");

    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    Require(
        !source.Contains("target.Clear();", StringComparison.Ordinal),
        "Dashboard tile refresh should not clear the whole tile collection because that causes visible flicker.");
    Require(
        source.Contains(".UpdateFrom(nextTile)", StringComparison.Ordinal),
        "Dashboard tile refresh should update existing tile view models in place when sensor identity is unchanged.");

    var buildTileStart = source.IndexOf(
        "private DashboardTileViewModel BuildDashboardTile(SensorReading sensor)",
        StringComparison.Ordinal);
    var buildTileEnd = source.IndexOf(
        "private string BuildDashboardTileValue(SensorReading sensor)",
        buildTileStart,
        StringComparison.Ordinal);
    Require(
        buildTileStart >= 0 && buildTileEnd > buildTileStart,
        "Dashboard tile construction source should remain discoverable for presentation integration checks.");
    var buildTileBody = source[buildTileStart..buildTileEnd];
    Require(
        CountOccurrences(buildTileBody, "DashboardSensorPresentation.FromSensor(sensor)") == 1,
        "Dashboard tile construction should resolve sensor presentation exactly once per tile.");
    Require(
        new[]
        {
            "IconKind = presentation.IconKind,",
            "VisualState = presentation.VisualState,",
            "MotionKind = presentation.MotionKind,",
            "NormalizedLevel = presentation.NormalizedLevel,",
            "MotionPeriodSeconds = presentation.MotionPeriodSeconds,",
            "IsMotionActive = presentation.IsMotionActive,",
            "IsDataFresh = _dashboardSnapshotFreshness.IsFresh,",
            "AutomationVisualStateText = GetDashboardAutomationVisualStateText(presentation.VisualState),",
            "AutomationFreshnessText = _dashboardSnapshotFreshness.IsFresh ? string.Empty : T(\"State.Disconnected\"),",
            "AccentHex = presentation.AccentHex,",
            "ValueHex = presentation.AccentHex,",
        }.All(fragment => buildTileBody.Contains(fragment, StringComparison.Ordinal)),
        "Dashboard tile construction should copy the resolved presentation and mark successful snapshot data fresh.");
    Require(
        !buildTileBody.Contains("IsDataFresh = true", StringComparison.Ordinal),
        "Dashboard tile construction should use snapshot freshness instead of marking cached sensor data fresh.");
    Require(
        legacyTileAnimationMembers.All(member => !buildTileBody.Contains(member, StringComparison.Ordinal)) &&
        !buildTileBody.Contains("CalculateFanRotationSeconds", StringComparison.Ordinal) &&
        !buildTileBody.Contains("CalculateElectricalPulseSeconds", StringComparison.Ordinal),
        "Dashboard tile construction should pass only presentation fields and leave all motion to DashboardSensorIcon.");

    var automationVisualStateBody = ExtractMethodBody(source, "GetDashboardAutomationVisualStateText");
    Require(
        automationVisualStateBody.Contains("DashboardVisualState.Information => T(\"Log.Info\")", StringComparison.Ordinal) &&
        automationVisualStateBody.Contains("DashboardVisualState.Inactive => T(\"SensorValue.Inactive\")", StringComparison.Ordinal) &&
        automationVisualStateBody.Contains("DashboardVisualState.Unavailable => T(\"SensorValue.Unknown\")", StringComparison.Ordinal) &&
        automationVisualStateBody.Contains("DashboardVisualState.Warning => T(\"Log.Warn\")", StringComparison.Ordinal) &&
        automationVisualStateBody.Contains("DashboardVisualState.Critical => T(\"Log.Error\")", StringComparison.Ordinal) &&
        automationVisualStateBody.Contains("DashboardVisualState.Normal => string.Empty", StringComparison.Ordinal),
        "Dashboard accessibility state text should announce inactive, information, unknown, warning, and error states while omitting only normal noise.");

    var freshnessBody = ExtractMethodBody(source, "SetDashboardTileFreshness");
    Require(
        freshnessBody.Contains("_dashboardSnapshotFreshness.MarkStale();", StringComparison.Ordinal) &&
        freshnessBody.Contains("TemperatureTiles", StringComparison.Ordinal) &&
        freshnessBody.Contains("FanTiles", StringComparison.Ordinal) &&
        freshnessBody.Contains("PowerTiles", StringComparison.Ordinal) &&
        freshnessBody.Contains("tile.IsDataFresh = isFresh;", StringComparison.Ordinal) &&
        freshnessBody.Contains("tile.AutomationFreshnessText = isFresh ? string.Empty : T(\"State.Disconnected\");", StringComparison.Ordinal),
        "Dashboard freshness updates should cover every existing dashboard tile collection.");
    Require(
        !freshnessBody.Contains("ReplaceTiles(", StringComparison.Ordinal) &&
        !freshnessBody.Contains(".Clear(", StringComparison.Ordinal) &&
        !freshnessBody.Contains(".Add(", StringComparison.Ordinal),
        "Dashboard freshness updates should not rebuild retained tile collections.");

    var replaceSensorsBody = ExtractMethodBody(source, "ReplaceSensors");
    Require(
        source.Contains("private readonly DashboardSnapshotFreshness _dashboardSnapshotFreshness = new();", StringComparison.Ordinal) &&
        replaceSensorsBody.Contains("_dashboardSnapshotFreshness.MarkFresh();", StringComparison.Ordinal) &&
        CountOccurrences(source, "_dashboardSnapshotFreshness.MarkFresh();") == 1,
        "Only a real sensor snapshot replacement should mark dashboard data fresh before cached tiles are rebuilt.");
    Require(
        !source.Contains("GetDashboardTileStyle(", StringComparison.Ordinal),
        "Dashboard tile construction should not retain the obsolete severity helper after presentation resolution replaces it.");

    var stopPollingBody = ExtractMethodBody(source, "StopSensorPolling");
    var timerStopIndex = stopPollingBody.IndexOf("_sensorPollingTimer.Stop();", StringComparison.Ordinal);
    var disconnectedIndex = stopPollingBody.IndexOf("_hasDisconnected = true;", StringComparison.Ordinal);
    var markStaleIndex = stopPollingBody.IndexOf("SetDashboardTileFreshness(false);", StringComparison.Ordinal);
    Require(
        timerStopIndex >= 0 &&
        disconnectedIndex > timerStopIndex &&
        markStaleIndex > disconnectedIndex,
        "Stopping sensor polling should mark retained dashboard tiles stale after the timer and disconnect state are updated.");
}

static void RunDashboardTileViewModelBehaviorChecks()
{
    var tile = new DashboardTileViewModel();
    var changedProperties = new List<string?>();
    tile.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

    var assignments = new (string PropertyName, Action Assign)[]
    {
        (nameof(DashboardTileViewModel.IconKind), () => tile.IconKind = DashboardIconKind.Fan),
        (nameof(DashboardTileViewModel.VisualState), () => tile.VisualState = DashboardVisualState.Warning),
        (nameof(DashboardTileViewModel.MotionKind), () => tile.MotionKind = DashboardMotionKind.FanRotation),
        (nameof(DashboardTileViewModel.NormalizedLevel), () => tile.NormalizedLevel = 0.5),
        (nameof(DashboardTileViewModel.MotionPeriodSeconds), () => tile.MotionPeriodSeconds = 0.4),
        (nameof(DashboardTileViewModel.IsMotionActive), () => tile.IsMotionActive = true),
        (nameof(DashboardTileViewModel.IsDataFresh), () => tile.IsDataFresh = true),
    };
    foreach (var assignment in assignments)
    {
        var beforeChange = changedProperties.Count;
        assignment.Assign();
        Require(
            changedProperties.Count == beforeChange + 1 &&
            changedProperties[^1] == assignment.PropertyName,
            $"Changing {assignment.PropertyName} should raise PropertyChanged exactly once for that property.");

        assignment.Assign();
        Require(
            changedProperties.Count == beforeChange + 1,
            $"Assigning the same {assignment.PropertyName} value should not raise PropertyChanged again.");
    }

    changedProperties.Clear();
    tile.AccentHex = "#FF123456";
    Require(
        changedProperties.SequenceEqual(
        [
            nameof(DashboardTileViewModel.AccentHex),
            nameof(DashboardTileViewModel.AccentBrush),
            nameof(DashboardTileViewModel.IconBackgroundBrush),
        ]),
        "Changing AccentHex should notify the hex value and both dependent brushes exactly once.");

    var accessibleTile = new DashboardTileViewModel
    {
        Title = "CPU 1 Temp",
        Value = "64",
        Unit = "degrees C",
        Status = "ok",
    };
    Require(
        accessibleTile.AutomationName == "CPU 1 Temp, 64 degrees C, ok",
        "Dashboard tile automation names should combine title, value with unit, and status into one concise announcement.");

    var accessibilityNotifications = new List<string?>();
    accessibleTile.PropertyChanged += (_, args) => accessibilityNotifications.Add(args.PropertyName);
    var accessibleAssignments = new (string PropertyName, Action Assign, string ExpectedName)[]
    {
        (nameof(DashboardTileViewModel.Title), () => accessibleTile.Title = "CPU 2 Temp", "CPU 2 Temp, 64 degrees C, ok"),
        (nameof(DashboardTileViewModel.Value), () => accessibleTile.Value = "67", "CPU 2 Temp, 67 degrees C, ok"),
        (nameof(DashboardTileViewModel.Unit), () => accessibleTile.Unit = "C", "CPU 2 Temp, 67 C, ok"),
        (nameof(DashboardTileViewModel.Status), () => accessibleTile.Status = "warning", "CPU 2 Temp, 67 C, warning"),
    };
    foreach (var assignment in accessibleAssignments)
    {
        accessibilityNotifications.Clear();
        assignment.Assign();
        Require(
            accessibilityNotifications.SequenceEqual(
            [
                assignment.PropertyName,
                nameof(DashboardTileViewModel.AutomationName),
            ]) &&
            accessibleTile.AutomationName == assignment.ExpectedName,
            $"Changing {assignment.PropertyName} should notify its dependent automation name exactly once.");

        accessibilityNotifications.Clear();
        assignment.Assign();
        Require(
            accessibilityNotifications.Count == 0,
            $"Assigning the same {assignment.PropertyName} should not repeat its automation-name notification.");
    }

    var staleCriticalTile = new DashboardTileViewModel
    {
        Title = "Voltage 1",
        Value = "180",
        Unit = "Volts",
        Status = "critical",
        AutomationVisualStateText = "Error",
        AutomationFreshnessText = "Disconnected",
    };
    Require(
        staleCriticalTile.AutomationName == "Voltage 1, 180 Volts, critical, Error, Disconnected",
        "Dashboard automation names should announce both the semantic critical state and stale/disconnected freshness.");

    var semanticNotifications = new List<string?>();
    staleCriticalTile.PropertyChanged += (_, args) => semanticNotifications.Add(args.PropertyName);
    staleCriticalTile.AutomationVisualStateText = "Warn";
    Require(
        semanticNotifications.SequenceEqual(
        [
            nameof(DashboardTileViewModel.AutomationVisualStateText),
            nameof(DashboardTileViewModel.AutomationName),
        ]) &&
        staleCriticalTile.AutomationName == "Voltage 1, 180 Volts, critical, Warn, Disconnected",
        "Changing localized semantic automation text should notify the field and computed automation name exactly once.");

    semanticNotifications.Clear();
    staleCriticalTile.AutomationFreshnessText = string.Empty;
    Require(
        semanticNotifications.SequenceEqual(
        [
            nameof(DashboardTileViewModel.AutomationFreshnessText),
            nameof(DashboardTileViewModel.AutomationName),
        ]) &&
        staleCriticalTile.AutomationName == "Voltage 1, 180 Volts, critical, Warn",
        "Clearing stale automation text after a fresh snapshot should notify Narrator and remove the disconnected announcement.");

    var existing = new DashboardTileViewModel { Id = "sensor-1" };
    var tiles = new List<DashboardTileViewModel> { existing };
    var next = new DashboardTileViewModel
    {
        Id = "sensor-1",
        IconKind = DashboardIconKind.Power,
        VisualState = DashboardVisualState.Critical,
        MotionKind = DashboardMotionKind.WarningPulse,
        NormalizedLevel = 0.75,
        MotionPeriodSeconds = 1.25,
        IsMotionActive = true,
        IsDataFresh = true,
        AutomationVisualStateText = "Error",
        AutomationFreshnessText = "Disconnected",
    };

    tiles[0].UpdateFrom(next);
    Require(ReferenceEquals(existing, tiles[0]), "Dashboard tile refresh should preserve the bound tile object identity.");
    Require(
        existing.IconKind == next.IconKind &&
        existing.VisualState == next.VisualState &&
        existing.MotionKind == next.MotionKind &&
        existing.NormalizedLevel == next.NormalizedLevel &&
        existing.MotionPeriodSeconds == next.MotionPeriodSeconds &&
        existing.IsMotionActive == next.IsMotionActive &&
        existing.IsDataFresh == next.IsDataFresh &&
        existing.AutomationVisualStateText == next.AutomationVisualStateText &&
        existing.AutomationFreshnessText == next.AutomationFreshnessText,
        "Dashboard tile in-place refresh should copy every presentation and freshness value.");
}

static void RunDashboardSensorIconXamlChecks()
{
    var controlPath = FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml"));
    var controlSource = File.ReadAllText(controlPath);
    var controlCode = File.ReadAllText(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml.cs")));
    var controlXaml = XDocument.Parse(controlSource);
    var mainPageXaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    XNamespace controls = "using:DellR730xdFanControlCenter.Controls";

    var root = controlXaml.Root ?? throw new InvalidOperationException("DashboardSensorIcon XAML should have a root element.");
    Require(
        root.Name == ui + "UserControl" &&
        root.Attribute(x + "Name")?.Value == "Root" &&
        root.Attribute("AutomationProperties.AccessibilityView")?.Value == "Raw",
        "DashboardSensorIcon should expose one Raw automation root so the named card remains the only Narrator content element.");
    var boundVectorBrushes = controlXaml
        .Descendants()
        .Attributes()
        .Where(attribute =>
            (attribute.Name.LocalName == "Fill" || attribute.Name.LocalName == "Stroke") &&
            attribute.Value.StartsWith("{Binding ", StringComparison.Ordinal))
        .Select(attribute => attribute.Value)
        .ToArray();
    Require(
        boundVectorBrushes.Length > 0 &&
        boundVectorBrushes.All(value => value == "{Binding EffectiveAccentBrush, ElementName=Root}") &&
        !controlSource.Contains("{Binding AccentBrush, ElementName=Root}", StringComparison.Ordinal),
        "DashboardSensorIcon vectors should bind to EffectiveAccentBrush so high contrast can replace semantic colors with the system foreground.");

    var primaryGroupNames = Enum.GetNames<DashboardIconKind>()
        .Select(kind => $"{kind}Group")
        .ToArray();
    foreach (var groupName in primaryGroupNames)
    {
        var group = controlXaml
            .Descendants(ui + "Grid")
            .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == groupName);
        Require(
            group?.Attribute("Visibility")?.Value == "Collapsed",
            $"DashboardSensorIcon should declare {groupName} once and keep it collapsed until IconKind selects it.");
    }

    var storageGroupNames = new[] { "StorageDriveGroup", "RaidControllerGroup", "StorageCacheGroup" };
    Require(
        storageGroupNames.All(groupName => controlXaml
            .Descendants(ui + "Grid")
            .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == groupName)?
            .Attribute("Visibility")?.Value == "Collapsed"),
        "Drive, RAID controller, and cache sensors should each have a dedicated vector icon group.");
    Require(
        storageGroupNames.All(groupName => controlCode.Contains($"DashboardIconKind.{groupName[..^5]} => {groupName}", StringComparison.Ordinal)),
        "DashboardSensorIcon should route each storage icon kind to its matching vector group.");

    var fanRotor = controlXaml
        .Descendants(ui + "Grid")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "FanRotor");
    var fanPath = fanRotor?
        .Descendants(ui + "Path")
        .Select(element => element.Attribute("Data")?.Value ?? string.Empty)
        .SingleOrDefault(data => data.StartsWith("M12,3 C13.8", StringComparison.Ordinal)) ?? string.Empty;
    Require(
        fanRotor?.Attribute("Width")?.Value == "24" &&
        fanRotor.Attribute("Height")?.Value == "24" &&
        fanRotor.Attribute("RenderTransformOrigin") is null &&
        fanPath.Contains(" M21,12 ", StringComparison.Ordinal) &&
        fanPath.Contains(" M3,12 ", StringComparison.Ordinal),
        "DashboardSensorIcon should preserve the centered 24x24 four-blade FanRotor geometry without a competing XAML transform origin.");

    Require(
        controlXaml.Descendants().Any(element => element.Attribute(x + "Name")?.Value == "TemperatureLevel"),
        "Temperature icon should expose a named TemperatureLevel for level scaling.");
    var utilizationShapes = new[] { "CpuUsageGroup", "MemoryUsageGroup", "IoUsageGroup", "SystemUsageGroup" }
        .Select(groupName => controlXaml
            .Descendants(ui + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == groupName)
            .Descendants(ui + "Path")
            .Select(path => path.Attribute("Data")?.Value ?? string.Empty)
            .Where(data => !string.IsNullOrWhiteSpace(data))
            .Aggregate(string.Empty, (current, data) => current + "|" + data))
        .ToArray();
    Require(
        utilizationShapes.All(shape => !string.IsNullOrWhiteSpace(shape)) &&
        utilizationShapes.Distinct(StringComparer.Ordinal).Count() == utilizationShapes.Length,
        "CPU, memory, IO, and system utilization icons should use four distinct vector silhouettes.");

    Require(
        controlXaml.Descendants(ui + "Path").Any(element => element.Attribute(x + "Name")?.Value == "VoltageNeedle") &&
        controlXaml.Descendants().Any(element => element.Attribute(x + "Name")?.Value == "CurrentFlowMarker") &&
        controlXaml.Descendants().Any(element => element.Attribute(x + "Name")?.Value == "PowerActivityElement"),
        "Voltage, current, and power icons should expose their named motion targets for the later Composition layer.");

    var outerRing = controlXaml
        .Descendants(ui + "Ellipse")
        .Single(element => element.Attribute(x + "Name")?.Value == "OuterRing");
    Require(
        outerRing.Attribute("Width")?.Value == "22" && outerRing.Attribute("Height")?.Value == "22",
        "The sensor icon outer ring should stay inside the 24x24 viewport so its stroke is not clipped.");

    var voltageNeedle = controlXaml
        .Descendants(ui + "Path")
        .Single(element => element.Attribute(x + "Name")?.Value == "VoltageNeedle");
    Require(
        voltageNeedle.Attribute("RenderTransformOrigin") is null &&
        voltageNeedle.Element(ui + "Path.RenderTransform") is null &&
        !controlXaml.Descendants(ui + "RotateTransform").Any(),
        "VoltageNeedle should leave rotation entirely to Composition without a competing XAML RotateTransform.");
    Require(
        !controlXaml.Descendants(ui + "ScaleTransform").Any(),
        "Level fills should leave scaling entirely to Composition without competing XAML ScaleTransforms.");

    var badgeGroupNames = new[]
    {
        "NormalBadgeGroup",
        "InformationBadgeGroup",
        "InactiveBadgeGroup",
        "UnavailableBadgeGroup",
        "WarningBadgeGroup",
        "CriticalBadgeGroup",
        "StaleBadgeGroup",
    };
    foreach (var groupName in badgeGroupNames)
    {
        var group = controlXaml
            .Descendants(ui + "Grid")
            .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == groupName);
        Require(
            group?.Attribute("Visibility")?.Value == "Collapsed" &&
            group.Descendants().Any(element => element.Name == ui + "Path" || element.Name == ui + "Ellipse"),
            $"DashboardSensorIcon should define a collapsed vector {groupName} status badge.");
    }

    var distinctiveBadgePaths = new[]
    {
        "UnavailableBadgeGroup",
        "CriticalBadgeGroup",
        "StaleBadgeGroup",
    }
        .Select(groupName => controlXaml
            .Descendants(ui + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == groupName)
            .Descendants(ui + "Path")
            .Select(path => path.Attribute("Data")?.Value ?? string.Empty)
            .Where(data => !string.IsNullOrWhiteSpace(data))
            .Aggregate(string.Empty, (current, data) => current + "|" + data))
        .ToArray();
    Require(
        distinctiveBadgePaths.All(pathData => !string.IsNullOrWhiteSpace(pathData)) &&
        distinctiveBadgePaths.Distinct(StringComparer.Ordinal).Count() == distinctiveBadgePaths.Length,
        "Unavailable, critical, and stale badges should have distinct question-mark, exclamation, and clock path semantics.");

    var dependencyProperties = new[]
    {
        "AccentBrush",
        "IconKind",
        "VisualState",
        "MotionKind",
        "NormalizedLevel",
        "MotionPeriodSeconds",
        "IsMotionActive",
        "IsDataFresh",
    };
    Require(
        dependencyProperties.All(property =>
            controlCode.Contains($"{property}Property = DependencyProperty.Register(", StringComparison.Ordinal) &&
            controlCode.Contains($"nameof({property})", StringComparison.Ordinal)),
        "DashboardSensorIcon should expose every tile presentation input as a dependency property.");
    Require(
        controlCode.Contains("private static readonly DependencyProperty EffectiveAccentBrushProperty", StringComparison.Ordinal) &&
        controlCode.Contains("nameof(EffectiveAccentBrush)", StringComparison.Ordinal) &&
        controlCode.Contains("typeof(Brush)", StringComparison.Ordinal) &&
        controlCode.Contains("ThemeSettings.CreateForWindowId", StringComparison.Ordinal) &&
        controlCode.Contains("_themeSettings.HighContrast", StringComparison.Ordinal) &&
        !controlCode.Contains("AccessibilitySettings", StringComparison.Ordinal) &&
        controlCode.Contains("UIColorType.Foreground", StringComparison.Ordinal) &&
        controlCode.Contains("EffectiveAccentBrush = _isHighContrast ? _systemForegroundBrush : AccentBrush;", StringComparison.Ordinal),
        "DashboardSensorIcon should use per-window ThemeSettings and the current system foreground for its effective high-contrast brush.");
    Require(
        controlCode.Contains("private readonly UIElement[] _primaryGroups;", StringComparison.Ordinal) &&
        controlCode.Contains("private readonly UIElement[] _badgeGroups;", StringComparison.Ordinal) &&
        controlCode.Contains("private bool _visualUpdateQueued;", StringComparison.Ordinal) &&
        controlCode.Contains("private bool _isLoaded;", StringComparison.Ordinal) &&
        controlCode.Contains("private long _lifecycleGeneration;", StringComparison.Ordinal) &&
        controlCode.Contains("control.QueueVisualUpdate();", StringComparison.Ordinal) &&
        !controlCode.Contains("control.ApplyVisuals();", StringComparison.Ordinal) &&
        controlCode.Contains("DispatcherQueue.TryEnqueue", StringComparison.Ordinal) &&
        controlCode.Contains("generation != _lifecycleGeneration", StringComparison.Ordinal) &&
        controlCode.Contains("foreach (var group in _primaryGroups)", StringComparison.Ordinal) &&
        controlCode.Contains("foreach (var group in _badgeGroups)", StringComparison.Ordinal) &&
        !controlCode.Contains("foreach (var group in new UIElement[]", StringComparison.Ordinal),
        "DashboardSensorIcon should cache visual groups and coalesce dependency-property callbacks through one DispatcherQueue visual update.");
    Require(
        controlCode.Contains("StartAnimation(\"Scale\"", StringComparison.Ordinal) &&
        controlCode.Contains("StartAnimation(\"RotationAngleInDegrees\"", StringComparison.Ordinal) &&
        controlCode.Contains("MotionKind == DashboardMotionKind.GaugeTransition", StringComparison.Ordinal),
        "Composition should transition level Scale and the trusted voltage gauge angle while unit-only MotionKind.None stays static.");
    Require(
        controlCode.Contains("private const double StaleIconOpacity = 0.65;", StringComparison.Ordinal) &&
        controlCode.Contains("IconLayer.Opacity = !IsDataFresh && !_isHighContrast ? StaleIconOpacity : 1;", StringComparison.Ordinal) &&
        controlCode.Contains("foreach (var group in _badgeGroups)", StringComparison.Ordinal) &&
        controlCode.Contains("StaleBadgeGroup.Visibility = Visibility.Visible;", StringComparison.Ordinal),
        "Stale dashboard data should remain fully opaque in high contrast, use at least 0.65 opacity otherwise, and force the dedicated clock badge.");
    Require(
        !controlSource.Contains("Storyboard", StringComparison.Ordinal) &&
        !controlSource.Contains("EnableDependentAnimation", StringComparison.Ordinal) &&
        !controlCode.Contains("Storyboard", StringComparison.Ordinal) &&
        !controlCode.Contains("EnableDependentAnimation", StringComparison.Ordinal),
        "DashboardSensorIcon Task 3 should apply static visuals only, without XAML storyboards or dependent animation.");

    var hardwareTileTemplate = mainPageXaml
        .Descendants(ui + "DataTemplate")
        .Single(element => element.Attribute(x + "Key")?.Value == "HardwareTileTemplate");
    var icon = hardwareTileTemplate.Descendants(controls + "DashboardSensorIcon").SingleOrDefault();
    Require(icon is not null, "HardwareTileTemplate should render the reusable DashboardSensorIcon control.");
    foreach (var property in dependencyProperties)
    {
        Require(
            icon!.Attribute(property)?.Value == $"{{Binding {property}}}",
            $"HardwareTileTemplate should bind DashboardSensorIcon.{property} to the tile view model.");
    }

    var templateSource = hardwareTileTemplate.ToString(SaveOptions.DisableFormatting);
    Require(
        !new[]
        {
            "TemperatureIconOpacity",
            "FanIconOpacity",
            "PowerIconOpacity",
            "VoltageIconOpacity",
            "CurrentIconOpacity",
            "HealthIconOpacity",
            "OnDashboardTileFanIcon",
            "OnDashboardTileElectricalIcon",
        }.Any(fragment => templateSource.Contains(fragment, StringComparison.Ordinal)),
        "HardwareTileTemplate should not retain the old opacity icon stack or page-level icon event handlers.");
    var automationPresenter = hardwareTileTemplate.Elements(ui + "ContentControl").Single();
    Require(
        automationPresenter.Attribute("AutomationProperties.AccessibilityView")?.Value == "Content" &&
        automationPresenter.Attribute("AutomationProperties.Name")?.Value == "{Binding AutomationName}",
        "The hardware card should expose one named ContentControl automation peer.");
    Require(
        automationPresenter.Elements(ui + "Border").Single().Attribute("AutomationProperties.AccessibilityView")?.Value == "Raw",
        "The visual border inside the named card presenter should stay out of the Content automation view.");
    Require(
        !mainPageXaml.Descendants(ui + "DataTemplate").Any(element => element.Attribute(x + "Key")?.Value == "HardwareStatusRowTemplate"),
        "The unused HardwareStatusRowTemplate should be removed rather than keeping a second obsolete icon implementation.");
}

static void RunDashboardSnapshotFreshnessChecks()
{
    var freshness = new DashboardSnapshotFreshness();
    Require(!freshness.IsFresh, "Dashboard snapshot freshness should start stale before the first successful SDR read.");

    freshness.MarkFresh();
    Require(freshness.IsFresh, "A successful sensor replacement should mark the dashboard snapshot fresh.");

    freshness.MarkStale();
    var cachedRebuild = new DashboardTileViewModel { IsDataFresh = freshness.IsFresh };
    Require(
        !cachedRebuild.IsDataFresh,
        "Rebuilding a tile from cached sensors after polling stops should keep the tile stale.");

    freshness.MarkFresh();
    var refreshedTile = new DashboardTileViewModel { IsDataFresh = freshness.IsFresh };
    Require(
        refreshedTile.IsDataFresh,
        "Building a tile after a real successful sensor replacement should mark the tile fresh.");
}

static void RunFanIconXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    var controlXaml = XDocument.Load(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml")));
    var controlSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml.cs")));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    const string oldShareLikeFanPath = "M12,2 C14.2,2";

    var fanNavigationItem = xaml
        .Descendants(ui + "NavigationViewItem")
        .Single(element => element.Attribute("Tag")?.Value == "Control");
    var fanNavigationIcon = fanNavigationItem.Descendants(ui + "PathIcon").Single();
    var fanNavigationPath = fanNavigationIcon.Attribute("Data")?.Value ?? string.Empty;

    var fanTileRotor = controlXaml
        .Descendants(ui + "Grid")
        .Single(element => element.Attribute(x + "Name")?.Value == "FanRotor");
    var fanTilePath = controlXaml
        .Descendants(ui + "Path")
        .Select(element => element.Attribute("Data")?.Value ?? string.Empty)
        .SingleOrDefault(data => data.StartsWith("M12,3 C13.8", StringComparison.Ordinal)) ?? string.Empty;

    Require(
        fanTileRotor.Attribute("Width")?.Value == "24" &&
        fanTileRotor.Attribute("Height")?.Value == "24",
        "Fan RPM dashboard rotor should use the same 24x24 box as the fan path so rotation stays centered.");
    Require(
        fanTileRotor.Attribute("RenderTransformOrigin") is null &&
        controlSource.Contains("_fanVisual.CenterPoint = new Vector3(12, 12, 0);", StringComparison.Ordinal),
        "Fan RPM dashboard rotor should use the Composition center of its 24x24 box without a competing XAML origin.");
    Require(
        !string.IsNullOrWhiteSpace(fanTilePath),
        "Fan RPM dashboard tiles should keep the previous centered four-blade fan path inline.");

    Require(
        fanTilePath.Contains(" M21,12 ", StringComparison.Ordinal) &&
        fanTilePath.Contains(" M3,12 ", StringComparison.Ordinal),
        "Fan RPM dashboard tiles should keep the previous horizontal/vertical centered fan geometry.");
    Require(
        fanNavigationPath != fanTilePath,
        "Fan control navigation icon should add a compact fan housing while keeping the dashboard rotor visually distinct.");
    Require(
        fanNavigationPath.StartsWith("F1 M12,1.5 A10.5,10.5", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("M12,3.25 A8.75,8.75", StringComparison.Ordinal),
        "Fan control navigation icon should use an outlined circular housing that reads as a physical fan at navigation size.");
    Require(
        fanNavigationPath.Contains("M12.1,4.25", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("M19.75,12.1", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("M11.9,19.75", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("M4.25,11.9", StringComparison.Ordinal) &&
        fanNavigationPath.Contains("M12,9.85 A2.15,2.15", StringComparison.Ordinal),
        "Fan control navigation icon should use four centered curved blades and a hub rather than diagonal X-arranged blades.");
    Require(
        !fanNavigationPath.Contains("M13.55,10.45", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains("19.15,4.85", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains("M13.35,10.25", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains("20.75,2.85", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains(" M21,12 ", StringComparison.Ordinal) &&
        !fanNavigationPath.Contains(" M3,12 ", StringComparison.Ordinal),
        "Fan control navigation icon should retain neither previous diagonal X geometry nor the dashboard cross geometry.");
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
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
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
        powerHealthGrid.Attribute("MaxHeight") is null &&
        powerHealthGrid.Attribute("ScrollViewer.VerticalScrollMode")?.Value == "Disabled" &&
        powerHealthGrid.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value == "Disabled" &&
        powerHealthGrid.Attribute("ScrollViewer.HorizontalScrollMode")?.Value == "Disabled" &&
        powerHealthGrid.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value == "Disabled",
        "Power and health board should expand all valid cards and leave vertical scrolling to the native page ScrollViewer.");

    var singlePageScrollCollections = xaml
        .Descendants()
        .Where(element =>
            element.Name == ui + "GridView" ||
            (element.Name == ui + "ListView" && element.Attribute("ItemsSource")?.Value == "{x:Bind LocalizedSensors, Mode=OneWay}"))
        .Where(element =>
            element.Attribute("ItemsSource")?.Value is "{x:Bind TemperatureTiles, Mode=OneWay}" or
                "{x:Bind FanTiles, Mode=OneWay}" or
                "{x:Bind PowerTiles, Mode=OneWay}" or
                "{x:Bind LocalizedSensors, Mode=OneWay}")
        .ToArray();
    Require(
        singlePageScrollCollections.Length == 4 &&
        singlePageScrollCollections.All(element =>
            element.Attribute("MaxHeight") is null &&
            element.Attribute("ScrollViewer.VerticalScrollMode")?.Value == "Disabled" &&
            element.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value == "Disabled"),
        "Overview tile boards and the detailed sensor list should not create nested vertical scrollbars.");
    Require(
        !pageSource.Contains("TemperatureGridView.MaxHeight", StringComparison.Ordinal) &&
        !pageSource.Contains("FanGridView.MaxHeight", StringComparison.Ordinal) &&
        !pageSource.Contains("PowerHealthGridView.MaxHeight", StringComparison.Ordinal),
        "Responsive layout code should not restore nested tile-board height caps at runtime.");

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
        !pageSource.Contains("TemperatureGridView.MaxHeight", StringComparison.Ordinal) &&
        !pageSource.Contains("FanGridView.MaxHeight", StringComparison.Ordinal) &&
        !pageSource.Contains("PowerHealthGridView.MaxHeight", StringComparison.Ordinal) &&
        pageSource.Contains("VisualizationWebView.MinHeight = layoutSize == ResponsiveLayoutSize.Large ? 1520 : 3200;", StringComparison.Ordinal),
        "Tile boards should expand into the native page scroll surface while the chart WebView keeps an intentional responsive height.");

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
    var hoverOverlayBody = ExtractMethodBody(pageSource, "DrawCurveHoverOverlay");

    Require(
        pageSource.Contains("_pendingVisualizationWheelShouldAnimate", StringComparison.Ordinal) &&
        pageSource.Contains("VisualizationMouseWheelDeltaY", StringComparison.Ordinal) &&
        pageSource.Contains("_visualizationWheelScrollState.Accumulate(", StringComparison.Ordinal) &&
        pageSource.Contains("ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: !animate);", StringComparison.Ordinal),
        "Wheel events forwarded from the chart WebView should accumulate an absolute target across an animated burst and still use animated ChangeView instead of direct presenter wheel calls.");
    Require(
        pageSource.Contains("_syncingTemperatureCurveInputsFromCanvas", StringComparison.Ordinal) &&
        pageSource.Contains("_syncingPowerCurveInputsFromCanvas", StringComparison.Ordinal),
        "Dragging curve points should guard against NumberBox value-change feedback triggering a full preview rebuild on every pointer move.");
    Require(
        xamlSource.Contains("PointerEntered=\"OnNewCurveCanvasPointerEntered\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerExited=\"OnNewCurveCanvasPointerExited\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerEntered=\"OnNewPowerCurveCanvasPointerEntered\"", StringComparison.Ordinal) &&
        xamlSource.Contains("PointerExited=\"OnNewPowerCurveCanvasPointerExited\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"NewCurveHoverReadoutText\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"NewPowerCurveHoverReadoutText\"", StringComparison.Ordinal) &&
        pageSource.Contains("_temperatureCurveHoverPosition", StringComparison.Ordinal) &&
        pageSource.Contains("_powerCurveHoverPosition", StringComparison.Ordinal) &&
        pageSource.Contains("DrawCurveHoverOverlay", StringComparison.Ordinal) &&
        pageSource.Contains("CalculateFanPercentValue", StringComparison.Ordinal) &&
        pageSource.Contains("FromCanvasX(position.X", StringComparison.Ordinal),
        "Curve editors should show live curve output in stable readout rows instead of covering the pointer with a floating label.");
    Require(
        hoverOverlayBody.Contains("IsHitTestVisible = false", StringComparison.Ordinal) &&
        !hoverOverlayBody.Contains("new Border", StringComparison.Ordinal) &&
        !hoverOverlayBody.Contains("new TextBlock", StringComparison.Ordinal),
        "Curve crosshairs and markers should never intercept pointer input, and hover text should stay outside the canvas.");
    Require(
        pageSource.Contains("DrawCurveChartGrid", StringComparison.Ordinal) &&
        pageSource.Contains("new Polygon", StringComparison.Ordinal) &&
        pageSource.Contains("CurvePreviewSampleCount", StringComparison.Ordinal) &&
        pageSource.Contains("DrawCurveAxisLabel", StringComparison.Ordinal),
        "Curve charts should use a filled plot, dense shared preview sampling, and readable axis tick labels.");
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
    Require(
        !xamlSource.Contains("Localization.Key=\"Hero.Subtitle\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("Localization.Key=\"Overview.VisualizationHelp\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("Localization.Key=\"Overview.TempBoardHelp\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("Localization.Key=\"Control.CurvePresetHelp\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("x:Name=\"PowerCurveHelpText\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("Localization.Key=\"Control.SmartAutoHelp\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("x:Name=\"NewCurvePreviewText\"", StringComparison.Ordinal) &&
        !xamlSource.Contains("x:Name=\"NewPowerCurvePreviewText\"", StringComparison.Ordinal),
        "Operational pages should not keep visible how-to paragraphs or ASCII curve previews after the controls make the interaction clear.");
    Require(
        !xamlSource.Contains("x:Name=\"IndividualFanInfoBar\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"IndividualFanStatusPanel\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"IndividualFanStatusToggle\"", StringComparison.Ordinal) &&
        xamlSource.Contains("x:Name=\"IndividualFanRiskButton\"", StringComparison.Ordinal) &&
        xamlSource.Contains("Click=\"OnOpenIndividualFanSettingsClick\"", StringComparison.Ordinal) &&
        pageSource.Contains("AutomationProperties.SetHelpText(IndividualFanStatusPanel, safetyMessage);", StringComparison.Ordinal) &&
        pageSource.Contains("ToolTipService.SetToolTip(IndividualFanRiskButton, safetyMessage);", StringComparison.Ordinal),
        "Individual fan control should use a compact state row while preserving the full firmware warning in tooltip and accessibility text.");

    XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    var xamlDocument = XDocument.Parse(xamlSource);
    var statusPanel = xamlDocument
        .Descendants()
        .Single(element => element.Attribute(xamlNamespace + "Name")?.Value == "IndividualFanStatusPanel");
    var statusColumns = statusPanel
        .Elements()
        .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
        .Elements()
        .Select(element => element.Attribute("Width")?.Value)
        .ToArray();
    Require(
        statusColumns.SequenceEqual(new[] { "Auto", "Auto", "Auto", "Auto", "*" }, StringComparer.Ordinal) &&
        statusPanel.Descendants().Single(element => element.Attribute(xamlNamespace + "Name")?.Value == "IndividualFanStatusText").Attribute("MaxWidth")?.Value == "420",
        "The compact individual-fan toggle, risk, and settings actions should stay beside the status text instead of being pushed beyond the visible card edge.");
}

static void RunRequestScrollResponsivenessChecks()
{
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var pageXaml = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));
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
        pageSource.Contains("private const int MaxVisualizationPayloadHistoryPoints = 720", StringComparison.Ordinal) &&
        pageSource.Contains("History = BuildVisualizationPayloadHistory()", StringComparison.Ordinal) &&
        pageSource.Contains("private SensorDashboardHistoryPoint[] BuildVisualizationPayloadHistory()", StringComparison.Ordinal) &&
        !pageSource.Contains("History = _sensorHistory.ToArray()", StringComparison.Ordinal),
        "Chart WebView payloads should send a bounded sampled history instead of cloning and serializing the entire retained history on every refresh.");
    Require(
        pageSource.Contains("_pendingVisualizationWheelDeltaY", StringComparison.Ordinal) &&
        pageSource.Contains("_pendingVisualizationWheelShouldAnimate", StringComparison.Ordinal) &&
        pageSource.Contains("_visualizationWheelScrollState", StringComparison.Ordinal) &&
        pageSource.Contains("ScheduleVisualizationWheelScroll(deltaY, animate)", StringComparison.Ordinal) &&
        !pageSource.Contains("_pendingVisualizationWheelTicks", StringComparison.Ordinal),
        "Forwarded WebView wheel messages should coalesce normalized distance while preserving one cumulative target for the full animated wheel burst.");
    Require(
        pageXaml.Contains("PointerWheelChanged=\"OnContentPanelPointerWheelChanged\"", StringComparison.Ordinal) &&
        pageSource.Contains("private void OnContentPanelPointerWheelChanged", StringComparison.Ordinal) &&
        pageSource.Contains("e.GetCurrentPoint(ContentPanel).Properties.MouseWheelDelta", StringComparison.Ordinal) &&
        pageSource.Contains("ScheduleVisualizationWheelScroll(deltaY, animate);", StringComparison.Ordinal) &&
        pageSource.Contains("e.Handled = true;", StringComparison.Ordinal),
        "Native page wheel input and chart WebView wheel messages should converge on the same queued scroll path and distance model.");
    Require(
        !pageSource.Contains("MouseWheelDown", StringComparison.Ordinal) &&
        !pageSource.Contains("MouseWheelUp", StringComparison.Ordinal) &&
        !pageSource.Contains("TryApplyPlatformMouseWheelScroll", StringComparison.Ordinal),
        "Forwarded chart mouse-wheel input should not call ScrollContentPresenter.MouseWheelUp/Down because that path produces visible jump scrolling when invoked directly.");
    Require(
        pageSource.Contains("ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: !animate);", StringComparison.Ordinal) &&
        !pageSource.Contains("ContentScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);", StringComparison.Ordinal),
        "Forwarded chart mouse-wheel and precision scrolling should not force host scroll steps to jump without animation.");
    Require(
        pageSource.Contains("_localizedSensorsDirty", StringComparison.Ordinal) &&
        pageSource.Contains("RefreshLocalizedSensorRowsIfVisible", StringComparison.Ordinal) &&
        pageSource.Contains("SensorsView.Visibility == Visibility.Visible", StringComparison.Ordinal),
        "Sensor table localization rows should be deferred while the Sensors page is hidden so request completion does not refresh an off-screen ListView during overview scrolling.");
    Require(
        dashboardHtml.Contains("const deltaY = getHostWheelDeltaY(event);", StringComparison.Ordinal) &&
        dashboardHtml.Contains("const shouldAnimateHostWheel = event.deltaMode !== WheelEvent.DOM_DELTA_PIXEL || Math.abs(event.deltaY) >= 80;", StringComparison.Ordinal) &&
        !dashboardHtml.Contains("requestAnimationFrame(postPendingHostWheelDelta);", StringComparison.Ordinal) &&
        !dashboardHtml.Contains("pendingHostWheelTicks", StringComparison.Ordinal),
        "The chart WebView should normalize each wheel event before forwarding it without adding a second animation-frame queue ahead of the host dispatcher.");
    Require(
        wheelHandlerBody.Contains("window.chrome.webview.postMessage({ type: \"wheel\", deltaY, animate: shouldAnimateHostWheel });", StringComparison.Ordinal) &&
        dashboardHtml.Contains("zoomOnMouseWheel: false", StringComparison.Ordinal) &&
        dashboardHtml.Contains("moveOnMouseWheel: false", StringComparison.Ordinal) &&
        dashboardHtml.Contains("moveOnMouseMove: false", StringComparison.Ordinal),
        "ECharts inside dataZoom should keep the slider handles for range selection without consuming mouse-wheel page scrolling.");
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
        logSource.Contains("NotifyWriteFailed(ex);", StringComparison.Ordinal) &&
        logSource.Contains("handler.GetInvocationList()", StringComparison.Ordinal),
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
    Require(
        pageSource.Contains("private async Task ConnectAndStartPollingAsync(bool restoreRunningState = true, bool scheduleRetryOnFailure = true)", StringComparison.Ordinal) &&
        pageSource.Contains("if (connected && restoreRunningState && IsFanControlIntentCurrent(restoreIntentVersion))", StringComparison.Ordinal) &&
        pageSource.Contains("await ConnectAndStartPollingAsync(restoreRunningState: false, scheduleRetryOnFailure: true);", StringComparison.Ordinal) &&
        pageSource.Contains("ScheduleSensorPollingRetry(pollingFailure.Message);", StringComparison.Ordinal),
        "A background sensor polling failure should visibly retry mc info + sdr elist after releasing the IPMI lock, without reapplying saved fan presets or hiding the original failure.");
}

static void RunHardwareTileValueLayoutXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile("MainPage.xaml"));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var hardwareTileTemplate = xaml
        .Descendants(ui + "DataTemplate")
        .Single(element => element.Attribute(x + "Key")?.Value == "HardwareTileTemplate");
    var hardwareTilePresenter = hardwareTileTemplate
        .Elements(ui + "ContentControl")
        .Single();
    var hardwareTileBorder = hardwareTilePresenter
        .Elements(ui + "Border")
        .Single();
    Require(
        hardwareTilePresenter.Attribute("Width")?.Value == "250" &&
        hardwareTilePresenter.Attribute("MinHeight")?.Value == "138",
        "Hardware cards should leave enough width and height for labeled sensor metadata without cramped wrapping.");
    Require(
        hardwareTileBorder.Attribute("Margin") is null,
        "Hardware tile template should not own external spacing because repeater and grid containers size items independently.");
    Require(
        hardwareTileBorder.Attribute("AutomationProperties.AccessibilityView")?.Value == "Raw" &&
        hardwareTileBorder.Attribute("AutomationProperties.Name") is null,
        "The named ContentControl should own the accessible card while its visual border stays Raw.");
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
        setters.Any(setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment" && setter.Attribute("Value")?.Value == "Top") &&
        setters.Any(setter => setter.Attribute("Property")?.Value == "AutomationProperties.AccessibilityView" && setter.Attribute("Value")?.Value == "Raw") &&
        !setters.Any(setter => setter.Attribute("Property")?.Value == "AutomationProperties.Name"),
        message);
}

static void RunFanAnimationSpeedChecks()
{
    var controlSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml.cs")));
    var controlXaml = File.ReadAllText(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml")));
    var mainPageXaml = File.ReadAllText(FindRepositoryFile("MainPage.xaml"));
    var mainPageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var tileSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Models", "DashboardTileViewModel.cs")));

    Require(
        controlSource.Contains("Loaded += OnControlLoaded;", StringComparison.Ordinal) &&
        controlSource.Contains("Unloaded += OnControlUnloaded;", StringComparison.Ordinal) &&
        controlSource.Contains("private bool _isLoaded;", StringComparison.Ordinal) &&
        controlSource.Contains("private long _lifecycleGeneration;", StringComparison.Ordinal),
        "DashboardSensorIcon should own an explicit Loaded/Unloaded lifecycle with generation-guarded queued work.");

    var loadedBody = ExtractMethodBody(controlSource, "OnControlLoaded");
    var unloadedBody = ExtractMethodBody(controlSource, "OnControlUnloaded");
    Require(
        loadedBody.Contains("ThemeSettings.CreateForWindowId", StringComparison.Ordinal) &&
        loadedBody.Contains("AppWindow.GetFromWindowId", StringComparison.Ordinal) &&
        loadedBody.Contains("_themeSettings.Changed += OnThemeSettingsChanged;", StringComparison.Ordinal) &&
        loadedBody.Contains("_uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;", StringComparison.Ordinal) &&
        loadedBody.Contains("_appWindow.Changed += OnAppWindowChanged;", StringComparison.Ordinal) &&
        loadedBody.Contains("AcquireCompositionResources();", StringComparison.Ordinal) &&
        loadedBody.Contains("ApplyVisuals();", StringComparison.Ordinal),
        "Loaded should acquire per-window settings, visibility, Composition visuals, and force one accurate visual application.");
    Require(
        unloadedBody.Contains("_isLoaded = false;", StringComparison.Ordinal) &&
        unloadedBody.Contains("_lifecycleGeneration++;", StringComparison.Ordinal) &&
        unloadedBody.Contains("_themeSettings.Changed -= OnThemeSettingsChanged;", StringComparison.Ordinal) &&
        unloadedBody.Contains("_uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;", StringComparison.Ordinal) &&
        unloadedBody.Contains("_appWindow.Changed -= OnAppWindowChanged;", StringComparison.Ordinal) &&
        unloadedBody.Contains("StopAllCompositionAnimations();", StringComparison.Ordinal) &&
        unloadedBody.Contains("ReleaseCompositionResources();", StringComparison.Ordinal),
        "Unloaded should invalidate queued work, detach every system event, stop animations, and release Composition references.");

    Require(
        controlSource.Contains("using Microsoft.UI.Composition;", StringComparison.Ordinal) &&
        controlSource.Contains("using Microsoft.UI.System;", StringComparison.Ordinal) &&
        controlSource.Contains("using Microsoft.UI.Windowing;", StringComparison.Ordinal) &&
        controlSource.Contains("using Microsoft.UI.Xaml.Hosting;", StringComparison.Ordinal) &&
        controlSource.Contains("ElementCompositionPreview.GetElementVisual", StringComparison.Ordinal) &&
        controlSource.Contains("ElementCompositionPreview.SetIsTranslationEnabled(CurrentFlowMarker, true);", StringComparison.Ordinal) &&
        !controlSource.Contains("AccessibilitySettings", StringComparison.Ordinal),
        "Dashboard motion should use lifted WinAppSDK Composition and supported per-window ThemeSettings APIs only.");
    var lifecycleQueueBody = ExtractMethodBody(controlSource, "QueueLifecycleUpdate");
    var visualQueueBody = ExtractMethodBody(controlSource, "QueueVisualUpdate");
    Require(
        controlSource.Contains("OnThemeSettingsChanged", StringComparison.Ordinal) &&
        controlSource.Contains("OnAnimationsEnabledChanged", StringComparison.Ordinal) &&
        controlSource.Contains("DispatcherQueue.TryEnqueue", StringComparison.Ordinal) &&
        controlSource.Contains("generation != _lifecycleGeneration", StringComparison.Ordinal) &&
        lifecycleQueueBody.IndexOf("if (!_isLoaded)", StringComparison.Ordinal) >= 0 &&
        lifecycleQueueBody.IndexOf("if (!_isLoaded)", StringComparison.Ordinal) <
        lifecycleQueueBody.IndexOf("DispatcherQueue.TryEnqueue", StringComparison.Ordinal) &&
        visualQueueBody.IndexOf("if (!_isLoaded)", StringComparison.Ordinal) >= 0 &&
        visualQueueBody.IndexOf("if (!_isLoaded)", StringComparison.Ordinal) <
        visualQueueBody.IndexOf("DispatcherQueue.TryEnqueue", StringComparison.Ordinal),
        "System callbacks and DP refreshes should marshal through generation-guarded DispatcherQueue work and stop queuing after unload.");
    Require(
        controlSource.Contains("event EventHandler<DashboardSensorIconVisualFailureEventArgs>? VisualUpdateFailed", StringComparison.Ordinal) &&
        lifecycleQueueBody.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        lifecycleQueueBody.Contains("ReportVisualUpdateFailure(ex);", StringComparison.Ordinal) &&
        visualQueueBody.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        visualQueueBody.Contains("ReportVisualUpdateFailure(ex);", StringComparison.Ordinal) &&
        controlSource.Contains("throw new InvalidOperationException(\"Dashboard sensor visual update failed without an error handler.\", exception);", StringComparison.Ordinal),
        "Every queued visual callback should report a real failure through a required top-level boundary instead of swallowing it or escaping silently.");
    Require(
        controlSource.Contains("args.DidVisibilityChange", StringComparison.Ordinal) &&
        controlSource.Contains("_appWindow.IsVisible", StringComparison.Ordinal) &&
        controlSource.Contains("PauseContinuousAnimations();", StringComparison.Ordinal) &&
        controlSource.Contains("ResumeContinuousAnimations();", StringComparison.Ordinal),
        "AppWindow visibility changes should pause hidden-window continuous motion and resume it when visible.");

    var fanBody = ExtractMethodBody(controlSource, "UpdateFanAnimation");
    var startFanBody = ExtractMethodBody(controlSource, "StartFanAnimation");
    var fanRateBody = ExtractMethodBody(controlSource, "SetFanPlaybackRate");
    var getFanRateBody = ExtractMethodBody(controlSource, "GetFanPlaybackRate");
    Require(
        controlSource.Contains("_fanVisual.CenterPoint = new Vector3(12, 12, 0);", StringComparison.Ordinal) &&
        controlSource.Contains("CreateScalarKeyFrameAnimation()", StringComparison.Ordinal) &&
        controlSource.Contains("CreateLinearEasingFunction()", StringComparison.Ordinal) &&
        controlSource.Contains("Duration = TimeSpan.FromSeconds(1)", StringComparison.Ordinal) &&
        controlSource.Contains("IterationBehavior = AnimationIterationBehavior.Forever", StringComparison.Ordinal) &&
        controlSource.Contains("_fanAnimationController = _compositor.CreateAnimationController();", StringComparison.Ordinal) &&
        CountOccurrences(controlSource, "_fanVisual.StartAnimation(\"RotationAngleInDegrees\", fanAnimation, _fanAnimationController);") == 1,
        "Fan rotation should use one centered, linear, one-second forever Composition animation with one retained controller.");
    Require(
        startFanBody.Contains("GetFanPlaybackRate(periodSeconds)", StringComparison.Ordinal) &&
        startFanBody.IndexOf("GetFanPlaybackRate(periodSeconds)", StringComparison.Ordinal) <
        startFanBody.IndexOf("CreateAnimationController()", StringComparison.Ordinal),
        "Fan startup should reject an invalid period before retaining or starting a Composition controller.");
    Require(
        fanBody.Contains("MotionKind == DashboardMotionKind.FanRotation", StringComparison.Ordinal) &&
        fanBody.Contains("MotionPeriodSeconds", StringComparison.Ordinal) &&
        fanBody.Contains("StopFanAnimation();", StringComparison.Ordinal) &&
        fanBody.Contains("ResumeFanAnimation();", StringComparison.Ordinal),
        "Fan rotation should reset to the cross when intent ends and retain phase only for temporary pauses.");
    Require(
        fanRateBody.Contains("GetFanPlaybackRate(periodSeconds)", StringComparison.Ordinal) &&
        fanRateBody.Contains("_fanAnimationController.PlaybackRate = playbackRate;", StringComparison.Ordinal) &&
        !fanRateBody.Contains("StopAnimation", StringComparison.Ordinal),
        "Fan retiming should validate controller playback bounds and update only PlaybackRate on the existing controller.");
    Require(
        getFanRateBody.Contains("double.IsFinite(periodSeconds)", StringComparison.Ordinal) &&
        getFanRateBody.Contains("1f / (float)periodSeconds", StringComparison.Ordinal) &&
        getFanRateBody.Contains("AnimationController.MinPlaybackRate", StringComparison.Ordinal) &&
        getFanRateBody.Contains("AnimationController.MaxPlaybackRate", StringComparison.Ordinal) &&
        getFanRateBody.Contains("throw new ArgumentOutOfRangeException(nameof(periodSeconds)", StringComparison.Ordinal),
        "Invalid or non-finite fan periods should fail explicitly instead of starting fallback motion.");

    Require(
        controlSource.Contains("StartAnimation(\"Scale\"", StringComparison.Ordinal) &&
        controlSource.Contains("StopAnimation(\"Scale\")", StringComparison.Ordinal) &&
        controlSource.Contains("StartAnimation(\"RotationAngleInDegrees\"", StringComparison.Ordinal) &&
        controlSource.Contains("StopAnimation(\"RotationAngleInDegrees\")", StringComparison.Ordinal) &&
        controlSource.Contains("-55f + ((float)normalizedLevel * 110f)", StringComparison.Ordinal),
        "Level and trusted gauge transitions should use one-shot Composition Scale and RotationAngleInDegrees with explicit stops.");
    Require(
        controlSource.Contains("MotionKind == DashboardMotionKind.GaugeTransition", StringComparison.Ordinal) &&
        controlSource.Contains("SetVoltageAngle(normalizedLevel, animate: false);", StringComparison.Ordinal),
        "Unit-only voltage MotionKind.None should land directly on the static 0.5 gauge angle without threshold animation.");
    var normalizedLevelBody = ExtractMethodBody(controlSource, "ApplyNormalizedLevel");
    Require(
        CountOccurrences(normalizedLevelBody, "IsMotionActive") >= 2,
        "Level and gauge transitions should require the explicit IsMotionActive contract instead of inferring motion from MotionKind alone.");

    var currentBody = ExtractMethodBody(controlSource, "UpdateCurrentFlowAnimation");
    Require(
        currentBody.Contains("MotionKind == DashboardMotionKind.CurrentFlow", StringComparison.Ordinal) &&
        controlSource.Contains("StartAnimation(\"Translation\"", StringComparison.Ordinal) &&
        controlSource.Contains("StartAnimation(\"Opacity\"", StringComparison.Ordinal) &&
        controlSource.Contains("StopAnimation(\"Translation\")", StringComparison.Ordinal) &&
        controlSource.Contains("StopAnimation(\"Opacity\")", StringComparison.Ordinal) &&
        controlSource.Contains("CurrentFlowMarker.Translation = Vector3.Zero;", StringComparison.Ordinal) &&
        !controlSource.Contains("RelativeOffsetAdjustment", StringComparison.Ordinal),
        "Current flow should animate Translation plus Opacity, stop both explicitly, and restore layout-neutral static values.");

    var powerBody = ExtractMethodBody(controlSource, "UpdatePowerActivityAnimation");
    var startPowerBody = ExtractMethodBody(controlSource, "StartPowerActivityAnimation");
    Require(
        powerBody.Contains("MotionKind == DashboardMotionKind.PowerActivity", StringComparison.Ordinal) &&
        startPowerBody.Contains("_powerActivityVisual.StartAnimation(\"Opacity\"", StringComparison.Ordinal) &&
        !startPowerBody.Contains("\"Scale\"", StringComparison.Ordinal) &&
        controlSource.Contains("_powerActivityVisual.StopAnimation(\"Opacity\");", StringComparison.Ordinal) &&
        controlSource.Contains("_powerActivityVisual.Opacity = 1;", StringComparison.Ordinal),
        "Power activity should use only a subtle opacity breath and restore full opacity without implying capacity through scale.");

    var alertBody = ExtractMethodBody(controlSource, "UpdateAlertPulseAnimation");
    Require(
        alertBody.Contains("VisualState is DashboardVisualState.Warning or DashboardVisualState.Critical", StringComparison.Ordinal) &&
        alertBody.Contains("IsMotionActive", StringComparison.Ordinal) &&
        !alertBody.Contains("MotionKind", StringComparison.Ordinal) &&
        controlSource.Contains("_outerRingVisual.StartAnimation(\"Opacity\"", StringComparison.Ordinal) &&
        controlSource.Contains("_outerRingVisual.StopAnimation(\"Opacity\");", StringComparison.Ordinal) &&
        controlSource.Contains("_outerRingVisual.Opacity = _isHighContrast ? 1 : OuterRingBaseOpacity;", StringComparison.Ordinal),
        "Every motion-active warning or critical state should pulse only the outer ring, including numeric sensors whose primary motion kind is not WarningPulse.");

    var stopAllBody = ExtractMethodBody(controlSource, "StopAllCompositionAnimations");
    Require(
        stopAllBody.Contains("StopFanAnimation();", StringComparison.Ordinal) &&
        stopAllBody.Contains("visual.StopAnimation(\"Scale\")", StringComparison.Ordinal) &&
        stopAllBody.Contains("_voltageNeedleVisual.StopAnimation(\"RotationAngleInDegrees\")", StringComparison.Ordinal) &&
        stopAllBody.Contains("StopCurrentFlowAnimation();", StringComparison.Ordinal) &&
        stopAllBody.Contains("StopPowerActivityAnimation();", StringComparison.Ordinal) &&
        stopAllBody.Contains("StopAlertPulseAnimation();", StringComparison.Ordinal) &&
        controlSource.Contains("_fanVisual.StopAnimation(\"RotationAngleInDegrees\")", StringComparison.Ordinal) &&
        controlSource.Contains("_currentFlowVisual.StopAnimation(\"Translation\")", StringComparison.Ordinal) &&
        controlSource.Contains("_currentFlowVisual.StopAnimation(\"Opacity\")", StringComparison.Ordinal) &&
        controlSource.Contains("_powerActivityVisual.StopAnimation(\"Opacity\")", StringComparison.Ordinal) &&
        controlSource.Contains("_outerRingVisual.StopAnimation(\"Opacity\")", StringComparison.Ordinal),
        "Unload cleanup should explicitly stop every Composition property that Task 4 can animate.");

    Require(
        controlSource.Contains("!_isHighContrast", StringComparison.Ordinal) &&
        controlSource.Contains("_animationsEnabled", StringComparison.Ordinal) &&
        controlSource.Contains("IsDataFresh", StringComparison.Ordinal) &&
        controlSource.Contains("IconLayer.Opacity = !IsDataFresh && !_isHighContrast ? StaleIconOpacity : 1;", StringComparison.Ordinal),
        "Every motion path should respect freshness, system animation preference, window visibility, and high contrast while preserving static state.");

    var forbiddenPageAnimationTokens = new[]
    {
        "DashboardTileFanAnimationState",
        "OnDashboardTileFanIcon",
        "OnDashboardTileElectricalIcon",
        "StartDashboardTileFanAnimation",
        "StopDashboardTileFanAnimation",
        "GetDashboardTileRotationAngle",
        "StartDashboardTileElectricalAnimation",
        "CalculateFanRotationSeconds",
        "CalculateElectricalPulseSeconds",
        "Storyboard",
        "DoubleAnimation",
        "element.Tag",
        "Microsoft.UI.Xaml.Media.Animation",
    };
    Require(
        forbiddenPageAnimationTokens.All(token => !mainPageSource.Contains(token, StringComparison.Ordinal)),
        "MainPage should contain no legacy dashboard storyboard, Tag state, timing helper, handler, or animation namespace.");
    Require(
        mainPageXaml.Contains("VisualUpdateFailed=\"OnDashboardSensorIconVisualUpdateFailed\"", StringComparison.Ordinal) &&
        mainPageSource.Contains("private void OnDashboardSensorIconVisualUpdateFailed", StringComparison.Ordinal) &&
        mainPageSource.Contains("ShowFailure(args.Exception);", StringComparison.Ordinal),
        "Dashboard sensor visual failures should reach MainPage's visible and logged failure path.");
    Require(
        !controlXaml.Contains("Storyboard", StringComparison.Ordinal) &&
        !controlXaml.Contains("DoubleAnimation", StringComparison.Ordinal) &&
        !controlXaml.Contains("EnableDependentAnimation", StringComparison.Ordinal) &&
        !controlSource.Contains("Storyboard", StringComparison.Ordinal) &&
        !controlSource.Contains("DoubleAnimation", StringComparison.Ordinal) &&
        !controlSource.Contains("EnableDependentAnimation", StringComparison.Ordinal),
        "DashboardSensorIcon should use Composition exclusively without XAML dependent animations.");

    var legacyViewModelTokens = new[]
    {
        "TemperatureIconOpacity",
        "FanIconOpacity",
        "PowerIconOpacity",
        "VoltageIconOpacity",
        "CurrentIconOpacity",
        "HealthIconOpacity",
        "IsFanAnimated",
        "FanRotationSeconds",
        "ElectricalIconOpacity",
        "IsElectricalAnimated",
        "ElectricalPulseSeconds",
    };
    Require(
        legacyViewModelTokens.All(token => !tileSource.Contains(token, StringComparison.Ordinal)),
        "DashboardTileViewModel should retain only semantic presentation fields after Composition replaces legacy page animation.");
}

static void RunPostFanCommandRefreshChecks()
{
    var mainPageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var localizationSource = File.ReadAllText(FindRepositoryFile("Services/LocalizationService.cs"));

    Require(
        mainPageSource.Contains("private async Task<TimeSpan> RefreshSensorsAfterFanCommandCoreAsync(IdracProfile profile, CancellationToken token)", StringComparison.Ordinal) &&
        mainPageSource.Contains("await RefreshSensorsCoreAsync(profile, token)", StringComparison.Ordinal) &&
        mainPageSource.Contains("RestartSensorPollingAfterImmediateRefresh();", StringComparison.Ordinal),
        "Successful user fan commands should share one control-and-read operation that refreshes sensors and dashboard data before releasing the IPMI lock.");
    Require(
        !mainPageSource.Contains("await RefreshSensorsAfterFanCommandAsync();", StringComparison.Ordinal),
        "Post-command sensor refresh must not be started as a second UI command after the fan command has already released the IPMI lock.");
    Require(
        CountOccurrences(mainPageSource, "await RefreshSensorsAfterFanCommandCoreAsync(profile, token);") >= 5,
        "All direct user fan-setting paths should run the immediate sensor refresh inside the same locked command body as the fan command.");
    Require(
        mainPageSource.Contains("SetHeroRequestStatus(T(\"Status.RefreshingSensorsAfterFanCommand\"));", StringComparison.Ordinal),
        "The combined control-and-read operation should make the sensor refresh phase visible instead of reporting fan control as complete before readings update.");
    Require(
        localizationSource.Contains("\"Status.RefreshingSensorsAfterFanCommand\"", StringComparison.Ordinal) &&
        localizationSource.Contains("\"Status.FanCommandSensorsRefreshed\"", StringComparison.Ordinal),
        "Post-command sensor refresh should use explicit localized status text.");
}

static void RunElectricalIconXamlChecks()
{
    var xaml = XDocument.Load(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml")));
    XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    var voltageIcon = xaml
        .Descendants(ui + "Grid")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "VoltageGroup");
    var currentIcon = xaml
        .Descendants(ui + "Grid")
        .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == "CurrentGroup");
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

    var controlSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Controls", "DashboardSensorIcon.xaml.cs")));
    Require(
        controlSource.Contains("UpdateCurrentFlowAnimation", StringComparison.Ordinal) &&
        controlSource.Contains("UpdatePowerActivityAnimation", StringComparison.Ordinal) &&
        !controlSource.Contains("From = 0.98", StringComparison.Ordinal) &&
        !controlSource.Contains("To = 1.06", StringComparison.Ordinal),
        "Electrical icons should use their dedicated Composition motion instead of the old shared scale pulse.");
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
    var wndProcStart = traySource.IndexOf("private IntPtr WndProc", StringComparison.Ordinal);
    var wndProcEnd = traySource.IndexOf("private void ShowContextMenu", wndProcStart, StringComparison.Ordinal);
    var wndProcBody = wndProcStart >= 0 && wndProcEnd > wndProcStart
        ? traySource[wndProcStart..wndProcEnd]
        : string.Empty;

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
        pageSource.Contains("public void StopAutoPolicyFromTray()", StringComparison.Ordinal) &&
        pageSource.Contains("public void ReportTrayCommandFailure(Exception ex)", StringComparison.Ordinal),
        "MainPage should expose explicit tray-safe wrappers for non-navigation tray actions.");
    Require(
        windowSource.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        windowSource.Contains("page.ReportTrayCommandFailure(ex);", StringComparison.Ordinal),
        "Async tray commands should surface exceptions through MainPage instead of letting dispatcher async-void failures escape unhandled.");
    Require(
        traySource.Contains("public event EventHandler<Exception>? CommandFailed;", StringComparison.Ordinal) &&
        wndProcBody.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        wndProcBody.Contains("CommandFailed?.Invoke(this, ex);", StringComparison.Ordinal),
        "The native tray callback should catch settings, menu, and command exceptions and forward the real failure to the main page boundary.");
    Require(
        windowSource.Contains("trayIcon.CommandFailed +=", StringComparison.Ordinal) &&
        ExtractMethodBody(windowSource, "RunPageAction").Contains("if (!DispatcherQueue.TryEnqueue", StringComparison.Ordinal) &&
        ExtractMethodBody(windowSource, "RunPageCommand").Contains("if (!DispatcherQueue.TryEnqueue", StringComparison.Ordinal),
        "Tray dispatcher enqueue failures should be surfaced explicitly instead of silently dropping user commands.");
    Require(
        windowSource.IndexOf("RootFrame.Navigate(typeof(MainPage));", StringComparison.Ordinal) <
            windowSource.IndexOf("_trayIcon = CreateTrayIcon();", StringComparison.Ordinal),
        "MainPage should be created before the native tray callback is registered so startup tray failures always have a visible page boundary.");
}

static void RunRuntimeStatePersistenceSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var appSource = File.ReadAllText(FindRepositoryFile("App.xaml.cs"));
    var pollingTickStart = source.IndexOf("private async void OnSensorPollingTimerTick", StringComparison.Ordinal);
    var pollingTickEnd = source.IndexOf("private async Task<TimeSpan> RefreshSensorsCoreAsync", pollingTickStart, StringComparison.Ordinal);
    var pollingTickBody = pollingTickStart >= 0 && pollingTickEnd > pollingTickStart
        ? source[pollingTickStart..pollingTickEnd]
        : string.Empty;
    var connectStart = source.IndexOf("private async Task ConnectAndStartPollingAsync", StringComparison.Ordinal);
    var connectEnd = source.IndexOf("private async Task StartSensorPollingAsync", connectStart, StringComparison.Ordinal);
    var connectBody = connectStart >= 0 && connectEnd > connectStart
        ? source[connectStart..connectEnd]
        : string.Empty;
    var applyCurveStart = source.IndexOf("private async Task ApplyCurvePresetAsync", StringComparison.Ordinal);
    var applyCurveEnd = source.IndexOf("private async void OnStartAutoPolicyClick", applyCurveStart, StringComparison.Ordinal);
    var applyCurveBody = applyCurveStart >= 0 && applyCurveEnd > applyCurveStart
        ? source[applyCurveStart..applyCurveEnd]
        : string.Empty;
    var curveFirstTickIndex = applyCurveBody.IndexOf("await RunAutoPolicyOnceCoreAsync(token, intentVersion)", StringComparison.Ordinal);
    var curveMarkActiveIndex = applyCurveBody.IndexOf("MarkActivePreset(curvePreset.Id);", StringComparison.Ordinal);
    var curvePersistIndex = applyCurveBody.IndexOf("PersistRunningPresetState(curvePreset.Id);", StringComparison.Ordinal);
    var startSmartStart = source.IndexOf("private async Task StartSmartAutoPolicyAsync", StringComparison.Ordinal);
    var startSmartEnd = source.IndexOf("private void OnStopAutoPolicyClick", startSmartStart, StringComparison.Ordinal);
    var startSmartBody = startSmartStart >= 0 && startSmartEnd > startSmartStart
        ? source[startSmartStart..startSmartEnd]
        : string.Empty;
    var smartFirstTickIndex = startSmartBody.IndexOf("await RunAutoPolicyOnceCoreAsync(token, intentVersion)", StringComparison.Ordinal);
    var smartPersistIndex = startSmartBody.IndexOf("PersistSmartAutoRunningState();", StringComparison.Ordinal);
    var stopAutoPolicyBody = ExtractMethodBody(source, "StopAutoPolicy");
    var stopAutoFailureBody = ExtractMethodBody(source, "StopAutoPolicyAfterFailure");
    var stopAutoEmergencyBody = source.Contains("private void StopAutoPolicyAfterEmergencyDellAuto()", StringComparison.Ordinal)
        ? ExtractMethodBody(source, "StopAutoPolicyAfterEmergencyDellAuto")
        : string.Empty;
    var connectBodyForIntent = ExtractMethodBody(source, "ConnectAndStartPollingAsync");
    var deletePresetBody = ExtractMethodBody(source, "OnDeletePresetClick");
    var autoPolicyTimerBody = ExtractMethodBody(source, "OnAutoPolicyTimerTick");
    var autoPolicyCoreBody = ExtractMethodBody(source, "RunAutoPolicyOnceCoreAsync");
    var transientSensorFailureBody = source.Contains("private async Task ContinueAutoPolicyAfterTransientSensorReadFailureAsync", StringComparison.Ordinal)
        ? ExtractMethodBody(source, "ContinueAutoPolicyAfterTransientSensorReadFailureAsync")
        : string.Empty;
    var applyAllFansBody = ExtractMethodBody(source, "ApplyAllFansAsync");
    var setSingleFanBody = ExtractMethodBody(source, "OnSetSingleFanClick");
    var applyManualPresetBody = ExtractMethodBody(source, "ApplyManualPresetAsync");
    var restoreDefaultManualBody = ExtractMethodBody(source, "RestoreDefaultManualAsync");
    var resetDellAutomaticBody = ExtractMethodBody(source, "ResetDellAutomaticModeAsync");
    var runUiCommandBody = ExtractMethodBody(source, "RunUiCommandAsync");
    var stopAutoManualOverrideBody = source.Contains("private void StopAutoPolicyForManualOverride()", StringComparison.Ordinal)
        ? ExtractMethodBody(source, "StopAutoPolicyForManualOverride")
        : string.Empty;
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
        source.Contains("private readonly DispatcherTimer _sensorPollingRetryTimer = new();", StringComparison.Ordinal) &&
        source.Contains("_sensorPollingRetryTimer.Tick += OnSensorPollingRetryTimerTick;", StringComparison.Ordinal) &&
        source.Contains("private async void OnSensorPollingRetryTimerTick", StringComparison.Ordinal) &&
        source.Contains("ScheduleSensorPollingRetry(pollingFailure.Message);", StringComparison.Ordinal) &&
        source.Contains("ConnectAndStartPollingAsync(restoreRunningState: false, scheduleRetryOnFailure: true)", StringComparison.Ordinal) &&
        source.Contains("StopSensorPollingRetry();", StringComparison.Ordinal) &&
        source.Contains("StopSensorPollingRetry(clearRunning: !_sensorPollingRetryRunning);", StringComparison.Ordinal) &&
        source.Contains("private void StopSensorPollingRetry(bool clearRunning = true)", StringComparison.Ordinal) &&
        !pollingTickBody.Contains("await ConnectAndStartPollingAsync(restoreRunningState: false);", StringComparison.Ordinal) &&
        connectBody.Contains("scheduleRetryOnFailure", StringComparison.Ordinal),
        "Background sensor polling failures should keep scheduling visible reconnect attempts until one succeeds or the user cancels polling.");
    Require(
        curveFirstTickIndex >= 0 &&
        curveMarkActiveIndex > curveFirstTickIndex &&
        curvePersistIndex > curveFirstTickIndex &&
        !applyCurveBody.Contains("SetModeSummary(\"Mode.CurveAuto\"", StringComparison.Ordinal) &&
        smartFirstTickIndex >= 0 &&
        smartPersistIndex > smartFirstTickIndex &&
        !startSmartBody.Contains("SetModeSummary(\"Mode.SmartAuto\"", StringComparison.Ordinal),
        "Curve and smart auto modes should become visibly active only after the first real auto-policy tick succeeds.");
    Require(
        source.Contains("private async Task<bool> RunAutoPolicyOnceCoreAsync", StringComparison.Ordinal) &&
        applyCurveBody.Contains("if (!await RunAutoPolicyOnceCoreAsync(token, intentVersion))", StringComparison.Ordinal) &&
        startSmartBody.Contains("if (!await RunAutoPolicyOnceCoreAsync(token, intentVersion))", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("return false;", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("return true;", StringComparison.Ordinal),
        "Auto-policy ticks should report whether the policy may continue, so emergency Dell-auto protection cannot be followed by first-tick success persistence.");
    Require(
        applyCurveBody.Contains("SetModeSummary(\"Mode.CurveStarting\", curvePreset.DisplayName);", StringComparison.Ordinal) &&
        applyCurveBody.IndexOf("SetModeSummary(\"Mode.CurveStarting\", curvePreset.DisplayName);", StringComparison.Ordinal) < curveFirstTickIndex &&
        applyCurveBody.Contains("SetAutoPolicyPendingSummary();", StringComparison.Ordinal) &&
        startSmartBody.Contains("SetModeSummary(\"Mode.SmartStarting\");", StringComparison.Ordinal) &&
        startSmartBody.IndexOf("SetModeSummary(\"Mode.SmartStarting\");", StringComparison.Ordinal) < smartFirstTickIndex &&
        startSmartBody.Contains("SetAutoPolicyPendingSummary();", StringComparison.Ordinal) &&
        source.Contains("private void SetAutoPolicyPendingSummary()", StringComparison.Ordinal) &&
        source.Contains("string.Equals(key, \"Mode.CurveStarting\", StringComparison.Ordinal)", StringComparison.Ordinal) &&
        source.Contains("string.Equals(key, \"Mode.SmartStarting\", StringComparison.Ordinal)", StringComparison.Ordinal),
        "While the first auto-policy tick is waiting on IPMI or running, the UI should show a pending automatic mode instead of stale manual mode, without marking the policy as fully active.");
    Require(
        source.Contains("private void ResetAutoPolicyModeSummary()", StringComparison.Ordinal) &&
        stopAutoPolicyBody.Contains("ResetAutoPolicyModeSummary();", StringComparison.Ordinal) &&
        stopAutoFailureBody.Contains("ResetAutoPolicyModeSummary();", StringComparison.Ordinal),
        "Stopping or failing an auto policy should clear stale curve/smart mode text so the UI cannot report automatic fan control after the timer has stopped.");
    Require(
        source.Contains("private long _fanControlIntentVersion;", StringComparison.Ordinal) &&
        source.Contains("private long _activeAutoPolicyIntentVersion;", StringComparison.Ordinal) &&
        source.Contains("private long BeginFanControlIntent()", StringComparison.Ordinal) &&
        source.Contains("private bool IsFanControlIntentCurrent(long intentVersion)", StringComparison.Ordinal) &&
        connectBodyForIntent.Contains("var restoreIntentVersion = CurrentFanControlIntentVersion();", StringComparison.Ordinal) &&
        connectBodyForIntent.Contains("restoreRunningState && IsFanControlIntentCurrent(restoreIntentVersion)", StringComparison.Ordinal) &&
        applyCurveBody.Contains("var intentVersion = BeginFanControlIntent();", StringComparison.Ordinal) &&
        applyCurveBody.Contains("_activeAutoPolicyIntentVersion = intentVersion;", StringComparison.Ordinal) &&
        startSmartBody.Contains("var intentVersion = BeginFanControlIntent();", StringComparison.Ordinal) &&
        startSmartBody.Contains("_activeAutoPolicyIntentVersion = intentVersion;", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("var intentVersion = _activeAutoPolicyIntentVersion;", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("!IsFanControlIntentCurrent(intentVersion)", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("ThrowIfFanControlIntentSuperseded(intentVersion, T(\"Status.AutoStarted\"));", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);", StringComparison.Ordinal) &&
        autoPolicyCoreBody.IndexOf("ThrowIfFanControlIntentSuperseded(intentVersion, T(\"Status.AutoStarted\"));", StringComparison.Ordinal) <
        autoPolicyCoreBody.IndexOf("await _ipmi.SetAllFansManualSpeedAsync(profile, percent, cancellationToken);", StringComparison.Ordinal),
        "Fan-control commands should use a latest-user-intent version so startup restore, queued auto starts, and in-flight auto ticks cannot override a newer manual or Dell-auto command.");
    Require(
        source.Contains("private void StopAutoPolicyForManualOverride()", StringComparison.Ordinal) &&
        ContainsBefore(runUiCommandBody, "beforeWaitForIpmiLock?.Invoke();", "await _ipmiOperationLock.WaitAsync") &&
        applyAllFansBody.Contains("beforeWaitForIpmiLock: StopAutoPolicyForManualOverride", StringComparison.Ordinal) &&
        setSingleFanBody.Contains("beforeWaitForIpmiLock: StopAutoPolicyForManualOverride", StringComparison.Ordinal) &&
        applyManualPresetBody.Contains("beforeWaitForIpmiLock: StopAutoPolicyForManualOverride", StringComparison.Ordinal) &&
        restoreDefaultManualBody.Contains("beforeWaitForIpmiLock: StopAutoPolicyForManualOverride", StringComparison.Ordinal) &&
        resetDellAutomaticBody.Contains("beforeWaitForIpmiLock: StopAutoPolicyForManualOverride", StringComparison.Ordinal) &&
        stopAutoManualOverrideBody.Contains("_autoPolicyTimer.Stop();", StringComparison.Ordinal) &&
        stopAutoManualOverrideBody.Contains("SetAutoPolicySummary(false);", StringComparison.Ordinal) &&
        stopAutoManualOverrideBody.Contains("ClearPersistedRunningState();", StringComparison.Ordinal) &&
        stopAutoManualOverrideBody.Contains("ClearAutoPolicyFanTargetCache();", StringComparison.Ordinal) &&
        stopAutoManualOverrideBody.Contains("ClearForcedAutoPolicyFanCommand();", StringComparison.Ordinal),
        "Manual fan and Dell automatic commands should stop software auto policy before waiting for the IPMI lock so later temperature-based ticks cannot overwrite the user command.");
    Require(
        deletePresetBody.Contains("InvalidateFanControlIntent();", StringComparison.Ordinal) &&
        deletePresetBody.Contains("StopAutoPolicyForManualOverride();", StringComparison.Ordinal),
        "Deleting the currently running curve preset should fully stop and persistently clear automatic fan control instead of only stopping the timer.");
    Require(
        source.Contains("private void StopAutoPolicyAfterEmergencyDellAuto()", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("StopAutoPolicyAfterEmergencyDellAuto();", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("ShowStatus(emergencyMessage, InfoBarSeverity.Warning);", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("_autoPolicyTimer.Stop();", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("_activeAutoPolicyIntentVersion = 0;", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("SetAutoPolicySummary(false);", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("ClearAutoPolicyFanTargetCache();", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("ClearForcedAutoPolicyFanCommand();", StringComparison.Ordinal) &&
        stopAutoEmergencyBody.Contains("MarkActivePreset(\"dell-auto\", persistRunningState: true);", StringComparison.Ordinal) &&
        !stopAutoEmergencyBody.Contains("ResetAutoPolicyModeSummary();", StringComparison.Ordinal),
        "Emergency Dell-auto protection should stop the software auto timer, persist Dell auto as the active hardware mode, clear auto caches, and show a visible warning so stale curve/smart-auto state cannot keep running.");
    Require(
        autoPolicyTimerBody.Contains("catch (FanCommandSafetyRecoveryException ex)", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("StopAutoPolicyAfterEmergencyDellAuto();", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("SetModeSummary(\"Mode.DellAuto\");", StringComparison.Ordinal) &&
        ContainsBefore(
            autoPolicyTimerBody,
            "catch (FanCommandSafetyRecoveryException ex)",
            "catch (Exception ex)"),
        "A running auto-policy fan command that fails after confirmed Dell automatic recovery must stop the software timer and persist/show Dell auto instead of falling through to the generic manual-state failure handler.");
    Require(
        source.Contains("private sealed class AutoPolicyTransientSensorReadException", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("AutoPolicyTickFailureStage.ReadSensors", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("AutoPolicyTickFailureStage.RefreshAfterFanCommand", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("AutoPolicyTickFailureStage.RefreshAfterEmergencyDellAuto", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("failureStage is AutoPolicyTickFailureStage.ReadSensors or AutoPolicyTickFailureStage.RefreshAfterFanCommand", StringComparison.Ordinal) &&
        autoPolicyCoreBody.Contains("throw new AutoPolicyTransientSensorReadException(ex);", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("catch (AutoPolicyTransientSensorReadException ex)", StringComparison.Ordinal) &&
        autoPolicyTimerBody.Contains("await ContinueAutoPolicyAfterTransientSensorReadFailureAsync(ex, intentVersion);", StringComparison.Ordinal) &&
        transientSensorFailureBody.Contains("IsFanControlIntentCurrent(intentVersion)", StringComparison.Ordinal) &&
        !transientSensorFailureBody.Contains("StopAutoPolicyAfterFailure()", StringComparison.Ordinal) &&
        !transientSensorFailureBody.Contains("ClearPersistedRunningState()", StringComparison.Ordinal),
        "Already-running auto policy ticks should keep retrying after transient SDR/RMCP+ reads, including the refresh after a successful normal fan command, without treating emergency Dell-auto refresh failures as retryable software-auto state.");
    Require(
        source.Contains("if (shouldReapply)", StringComparison.Ordinal) &&
        source.Contains("await ApplyPresetAsync(savedPreset)", StringComparison.Ordinal),
        "Saving the active preset or active curve should immediately re-apply it instead of requiring another Switch click.");
    Require(
        source.Contains("ClearStaleFailureStatusAfterSensorRefreshSuccess()", StringComparison.Ordinal) &&
        source.Contains("StatusInfoBar.Severity != InfoBarSeverity.Error", StringComparison.Ordinal) &&
        source.Contains("StatusInfoBar.IsOpen = false", StringComparison.Ordinal),
        "A real successful sensor refresh should close stale top-level failure banners while preserving the logged failure record.");
    Require(
        !source.Contains("waitForIpmiLock: false", StringComparison.Ordinal),
        "User-triggered fan mode changes should wait for the current IPMI command instead of showing a busy error.");
    Require(
        appSource.Contains("NormalizeProcessWorkingDirectory()", StringComparison.Ordinal) &&
        appSource.Contains("Directory.SetCurrentDirectory(applicationDirectory)", StringComparison.Ordinal),
        "Startup should normalize the process working directory to the app directory so Start Menu shortcut working directories cannot affect relative paths.");
    Require(
        appSource.Contains("EnsureSingleInstance()", StringComparison.Ordinal) &&
        appSource.Contains("Process.GetProcessesByName(currentProcess.ProcessName)", StringComparison.Ordinal) &&
        appSource.Contains("SingleInstanceMutexName", StringComparison.Ordinal) &&
        appSource.Contains("DuplicateStartupExitCode", StringComparison.Ordinal) &&
        appSource.Contains("Environment.Exit(DuplicateStartupExitCode)", StringComparison.Ordinal) &&
        appSource.Contains("MessageBoxW", StringComparison.Ordinal) &&
        appSource.Contains("settings.json", StringComparison.Ordinal) &&
        appSource.Contains("IPMI", StringComparison.Ordinal),
        "Startup should explicitly block duplicate app instances with a non-zero exit code so settings persistence and IPMI fan commands cannot race across old and new builds.");
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
        pageSource.Contains("Current = BuildHistoryCurrent(snapshot.Current)", StringComparison.Ordinal) &&
        pageSource.Contains("private static VisualizationCurrent BuildHistoryCurrent", StringComparison.Ordinal) &&
        pageSource.Contains("NormalizeVisualizationHistoryPoint(point)", StringComparison.Ordinal) &&
        Regex.IsMatch(pageSource, @"\[JsonIgnore\]\s+public object\[\] TypeCounts", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(pageSource, @"\[JsonIgnore\]\s+public object\[\] SensorTree", RegexOptions.CultureInvariant) &&
        !pageSource.Contains("TypeCounts = snapshot.TypeCounts", StringComparison.Ordinal) &&
        !pageSource.Contains("SensorTree = snapshot.SensorTree", StringComparison.Ordinal),
        "Dashboard history points should keep compact trend data and must not persist repeated sensor trees/type counts for every poll.");
    Require(
        pageSource.Contains("private const int VisualizationHistoryRetentionDays = 7", StringComparison.Ordinal) &&
        pageSource.Contains("chart-history-*.jsonl", StringComparison.Ordinal) &&
        pageSource.Contains("public string Timestamp", StringComparison.Ordinal) &&
        pageSource.Contains("public long UnixMilliseconds", StringComparison.Ordinal) &&
        pageSource.Contains("LoadVisualizationHistory()", StringComparison.Ordinal) &&
        pageSource.Contains("QueueVisualizationHistoryPersistence", StringComparison.Ordinal) &&
        pageSource.Contains("PersistVisualizationHistoryPoint", StringComparison.Ordinal),
        "Visualization history should persist timestamped chart snapshots for the last seven days.");
    Require(
        pageSource.Contains("RepairVisualizationHistoryFile", StringComparison.Ordinal) &&
        pageSource.Contains("BuildVisualizationHistoryRepairPath", StringComparison.Ordinal) &&
        pageSource.Contains(".corrupt-", StringComparison.Ordinal) &&
        pageSource.Contains("File.Move(tempPath, historyPath, overwrite: true)", StringComparison.Ordinal) &&
        pageSource.Contains("JsonException", StringComparison.Ordinal) &&
        pageSource.Contains("validLines.Add(line);", StringComparison.Ordinal),
        "Corrupt chart-history JSONL rows should be reported visibly, quarantined with the original file preserved, and removed from the active history file so startup does not fail repeatedly.");
}

static void RunWebView2UserDataSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var initStart = source.IndexOf("private async Task InitializeVisualizationAsync", StringComparison.Ordinal);
    var ensureIndex = source.IndexOf("await VisualizationWebView.EnsureCoreWebView2Async();", initStart, StringComparison.Ordinal);
    var userDataIndex = source.IndexOf("Environment.SetEnvironmentVariable(\"WEBVIEW2_USER_DATA_FOLDER\", userDataFolder, EnvironmentVariableTarget.Process)", initStart, StringComparison.Ordinal);
    Require(
        initStart >= 0 &&
        userDataIndex > initStart &&
        ensureIndex > userDataIndex &&
        source.Contains("Environment.SetEnvironmentVariable(\"WEBVIEW2_USER_DATA_FOLDER\", userDataFolder, EnvironmentVariableTarget.Process)", StringComparison.Ordinal) &&
        source.Contains("Environment.SpecialFolder.LocalApplicationData", StringComparison.Ordinal) &&
        source.Contains("\"WebView2\"", StringComparison.Ordinal),
        "WebView2 user data should be placed under LocalAppData before initialization so release folders stay clean and writable.");
}

static void RunUserCommandLogFlushSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var commandTraceSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "CommandTraceEventArgs.cs")));
    var ipmiSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "IpmiCommandService.cs")));
    var commandCompletedBody = ExtractMethodBody(source, "OnCommandCompleted");
    var addCommandLogBody = ExtractMethodBody(source, "AddCommandLog");
    var persistCommandIndex = commandCompletedBody.IndexOf("AddCommandLog(e);", StringComparison.Ordinal);
    var dispatchCommandIndex = commandCompletedBody.IndexOf("DispatcherQueue.TryEnqueue", StringComparison.Ordinal);
    Require(
        commandTraceSource.Contains("public required DateTimeOffset StartedAt", StringComparison.Ordinal) &&
        commandTraceSource.Contains("public required DateTimeOffset FinishedAt", StringComparison.Ordinal) &&
        ipmiSource.Contains("StartedAt = startedAt", StringComparison.Ordinal) &&
        ipmiSource.Contains("FinishedAt = finishedAt", StringComparison.Ordinal),
        "IPMI command traces should preserve process start and finish timestamps instead of assigning delayed UI-dispatch timestamps.");
    Require(
        persistCommandIndex >= 0 &&
        dispatchCommandIndex > persistCommandIndex &&
        commandCompletedBody.Contains("if (!DispatcherQueue.TryEnqueue", StringComparison.Ordinal) &&
        addCommandLogBody.Contains("Timestamp = e.FinishedAt", StringComparison.Ordinal) &&
        addCommandLogBody.Contains("StartedAt = e.StartedAt", StringComparison.Ordinal) &&
        addCommandLogBody.Contains("FinishedAt = e.FinishedAt", StringComparison.Ordinal),
        "IPMI command completion records should enter the durable log queue before UI dispatch so the enclosing operation flush covers them.");
    var runCommandStart = source.IndexOf("private async Task<bool> RunUiCommandAsync", StringComparison.Ordinal);
    var runCommandEnd = source.IndexOf("private void ReplaceSensors", runCommandStart, StringComparison.Ordinal);
    var runCommandBody = runCommandStart >= 0 && runCommandEnd > runCommandStart
        ? source[runCommandStart..runCommandEnd]
        : string.Empty;
    var successIndex = runCommandBody.IndexOf("operation.Succeed(description);", StringComparison.Ordinal);
    var flushAfterSuccessIndex = runCommandBody.IndexOf("await FlushAppLogAsync();", successIndex, StringComparison.Ordinal);
    var heroSuccessIndex = runCommandBody.IndexOf("SetHeroRequestStatus(F(\"Hero.RequestSucceeded\", description));", StringComparison.Ordinal);
    Require(
        runCommandBody.Contains("var operationCompleted = false;", StringComparison.Ordinal) &&
        successIndex >= 0 &&
        flushAfterSuccessIndex > successIndex &&
        heroSuccessIndex > flushAfterSuccessIndex &&
        runCommandBody.Contains("if (!operationCompleted && operation is not null)", StringComparison.Ordinal),
        "User commands should flush the terminal operation log before showing success and should not try to fail an already-completed operation after a log-write failure.");

    var autoPolicyStart = source.IndexOf("private async Task<bool> RunAutoPolicyOnceCoreAsync", StringComparison.Ordinal);
    var autoPolicyEnd = source.IndexOf("private static Dictionary<string, string> BuildAutoPolicyFanCommandProperties", autoPolicyStart, StringComparison.Ordinal);
    var autoPolicyBody = autoPolicyStart >= 0 && autoPolicyEnd > autoPolicyStart
        ? source[autoPolicyStart..autoPolicyEnd]
        : string.Empty;
    Require(
        autoPolicyBody.Contains("var operationCompleted = false;", StringComparison.Ordinal) &&
        autoPolicyBody.Contains("await FlushAppLogAsync();", StringComparison.Ordinal) &&
        autoPolicyBody.Contains("if (!operationCompleted)", StringComparison.Ordinal),
        "Auto-policy ticks should treat runtime-log write failures as real operation failures, including unchanged-target and background timer paths.");
    var emergencyStart = autoPolicyBody.IndexOf("if (cpuTemp >= _settings.EmergencyCpuTemperatureCelsius)", StringComparison.Ordinal);
    var unchangedStart = autoPolicyBody.IndexOf("if (ShouldSkipUnchangedAutoPolicyFanCommand", StringComparison.Ordinal);
    var appliedStart = autoPolicyBody.IndexOf("fanCommandProperties = BuildAutoPolicyFanCommandProperties", StringComparison.Ordinal);
    var emergencyBody = emergencyStart >= 0 && unchangedStart > emergencyStart
        ? autoPolicyBody[emergencyStart..unchangedStart]
        : string.Empty;
    var unchangedBody = unchangedStart >= 0 && appliedStart > unchangedStart
        ? autoPolicyBody[unchangedStart..appliedStart]
        : string.Empty;
    var appliedBody = appliedStart >= 0
        ? autoPolicyBody[appliedStart..]
        : string.Empty;
    Require(
        emergencyBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) < emergencyBody.IndexOf("ShowStatus(", StringComparison.Ordinal) &&
        unchangedBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) < unchangedBody.IndexOf("SetAutoModeSummary(", StringComparison.Ordinal) &&
        appliedBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) < appliedBody.IndexOf("SetAutoModeSummary(", StringComparison.Ordinal) &&
        !autoPolicyBody.Contains("AddLog(T(\"Log.Info\"), unchangedMessage);", StringComparison.Ordinal) &&
        !autoPolicyBody.Contains("AddLog(T(\"Log.Info\"), message);", StringComparison.Ordinal),
        "Auto-policy terminal records should be flushed before success, unchanged-target, or emergency state is shown, without adding a second unflushed success record.");

    var sensorRefreshStart = source.IndexOf("private async Task<TimeSpan> RefreshSensorsCoreAsync", StringComparison.Ordinal);
    var sensorRefreshEnd = source.IndexOf("private async void OnSetAllFansClick", sensorRefreshStart, StringComparison.Ordinal);
    var sensorRefreshBody = sensorRefreshStart >= 0 && sensorRefreshEnd > sensorRefreshStart
        ? source[sensorRefreshStart..sensorRefreshEnd]
        : string.Empty;
    Require(
        sensorRefreshBody.Contains("var operationCompleted = false;", StringComparison.Ordinal) &&
        sensorRefreshBody.Contains("operationCompleted = true;", StringComparison.Ordinal) &&
        sensorRefreshBody.Contains("await FlushAppLogAsync();", StringComparison.Ordinal) &&
        sensorRefreshBody.Contains("if (!operationCompleted)", StringComparison.Ordinal) &&
        sensorRefreshBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) <
            sensorRefreshBody.IndexOf("SetHeroRequestStatus(F(\"Hero.SensorRefreshSucceeded\"", StringComparison.Ordinal),
        "Sensor refresh operations should flush their own terminal log writes before returning success, including background polling paths.");

    var durableUiLogBody = ExtractMethodBody(source, "WriteDurableUiLogAsync");
    Require(
        durableUiLogBody.IndexOf("_appLog.Write(", StringComparison.Ordinal) < durableUiLogBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) &&
        durableUiLogBody.IndexOf("await FlushAppLogAsync();", StringComparison.Ordinal) < durableUiLogBody.IndexOf("AddVolatileLog(", StringComparison.Ordinal),
        "Durable UI log entries should reach disk before they appear in the visible activity list.");
    foreach (var methodName in new[]
             {
                 "OnSavePresetClick",
                 "OnDeletePresetClick",
                 "OnAddPresetClick",
                 "OnAddCurvePresetClick",
                 "OnAddPowerCurvePresetClick",
                 "OnSaveSettingsClick",
             })
    {
        var methodBody = ExtractMethodBody(source, methodName);
        Require(
            methodBody.IndexOf("await WriteDurableUiLogAsync(", StringComparison.Ordinal) >= 0 &&
            methodBody.IndexOf("await WriteDurableUiLogAsync(", StringComparison.Ordinal) < methodBody.IndexOf("ShowStatus(", StringComparison.Ordinal),
            $"{methodName} should flush its success log before showing success.");
    }

    Require(
        runCommandBody.Contains("Func<string?>? successMessageFactory", StringComparison.Ordinal) &&
        runCommandBody.IndexOf("await FlushAppLogAsync();", successIndex, StringComparison.Ordinal) <
            runCommandBody.IndexOf("successMessageFactory?.Invoke()", StringComparison.Ordinal),
        "UI command callbacks should defer their success message until the enclosing terminal operation log has been flushed.");
    foreach (var methodName in new[]
             {
                 "RefreshSensorsAsync",
                 "ApplyAllFansAsync",
                 "OnSetSingleFanClick",
                 "ApplyManualPresetAsync",
                 "RestoreDefaultManualAsync",
                 "ResetDellAutomaticModeAsync",
             })
    {
        var methodBody = ExtractMethodBody(source, methodName);
        Require(
            methodBody.Contains("successMessageFactory:", StringComparison.Ordinal) &&
            !methodBody.Contains("InfoBarSeverity.Success", StringComparison.Ordinal),
            $"{methodName} should provide a deferred success message instead of displaying it inside the unflushed command callback.");
    }

    var latencyBody = ExtractMethodBody(source, "CheckSensorPollingLatency");
    Require(
        source.Contains("private async Task CheckSensorPollingLatency", StringComparison.Ordinal) &&
        CountOccurrences(source, "await CheckSensorPollingLatency(elapsed);") == 3 &&
        latencyBody.IndexOf("await WriteDurableUiLogAsync(", StringComparison.Ordinal) < latencyBody.IndexOf("ShowStatus(", StringComparison.Ordinal),
        "Polling recovery should flush its recovery record before showing a successful recovery state on every sensor-refresh path.");
}

static void RunStartupFailureBoundarySourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var pageLoadedBody = ExtractMethodBody(source, "OnPageLoaded");
    var reconnectBody = ExtractMethodBody(source, "OnSensorPollingRetryTimerTick");
    var connectBody = ExtractMethodBody(source, "ConnectAndStartPollingAsync");
    var startPollingBody = source.Contains("private async Task StartSensorPollingAsync", StringComparison.Ordinal)
        ? ExtractMethodBody(source, "StartSensorPollingAsync")
        : string.Empty;

    Require(
        source.Contains("private async void OnPageLoaded", StringComparison.Ordinal) &&
        pageLoadedBody.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        pageLoadedBody.Contains("await HandlePageLoadFailureAsync(ex);", StringComparison.Ordinal) &&
        source.Contains("private async Task HandlePageLoadFailureAsync", StringComparison.Ordinal),
        "Page loading should keep damaged settings and invalid presets inside a top-level async exception boundary that reports the real failure.");
    Require(
        reconnectBody.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        reconnectBody.Contains("ShowFailure(ex);", StringComparison.Ordinal) &&
        reconnectBody.Contains("StopSensorPollingRetry();", StringComparison.Ordinal),
        "The async-void polling reconnect callback should stop and surface unexpected failures instead of escaping through DispatcherQueue.");
    Require(
        connectBody.Contains("await StartSensorPollingAsync();", StringComparison.Ordinal) &&
        !connectBody.Contains("StartSensorPolling();", StringComparison.Ordinal) &&
        startPollingBody.Contains("_appLog.Write(", StringComparison.Ordinal),
        "Connection startup should use a log-gated asynchronous polling start instead of starting the timer inside the connection command body.");

    var writeIndex = startPollingBody.IndexOf("_appLog.Write(", StringComparison.Ordinal);
    var flushIndex = startPollingBody.IndexOf("await FlushAppLogAsync();", writeIndex, StringComparison.Ordinal);
    var timerStartIndex = startPollingBody.IndexOf("_sensorPollingTimer.Start();", StringComparison.Ordinal);
    Require(
        writeIndex >= 0 && flushIndex > writeIndex && timerStartIndex > flushIndex,
        "Sensor polling must persist and flush its start record before the background timer is allowed to run.");
}

static void RunSettingsStoreAtomicSaveSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "SettingsStore.cs")));
    Require(
        source.Contains("FileMode.CreateNew", StringComparison.Ordinal) &&
        source.Contains("FileOptions.WriteThrough", StringComparison.Ordinal) &&
        source.Contains("stream.Flush(flushToDisk: true);", StringComparison.Ordinal) &&
        source.Contains("File.Move(tempSettingsPath, SettingsPath, overwrite: true);", StringComparison.Ordinal) &&
        source.Contains("finally", StringComparison.Ordinal) &&
        !source.Contains("File.WriteAllText(SettingsPath", StringComparison.Ordinal),
        "SettingsStore should durably write a unique temporary file and atomically replace settings.json instead of truncating the active file in place.");
}

static void RunSingleInstanceRaceSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("App.xaml.cs"));
    var methodStart = source.IndexOf("private static void EnsureSingleInstance", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("private static void StopDuplicateStartup", methodStart, StringComparison.Ordinal);
    var body = methodStart >= 0 && methodEnd > methodStart
        ? source[methodStart..methodEnd]
        : string.Empty;
    var mutexIndex = body.IndexOf("new Mutex(initiallyOwned: true", StringComparison.Ordinal);
    var processScanIndex = body.IndexOf("Process.GetProcessesByName", StringComparison.Ordinal);
    Require(
        mutexIndex >= 0 && processScanIndex > mutexIndex &&
        body.Contains("FileVersionInfo", StringComparison.Ordinal) &&
        body.Contains("createdNew", StringComparison.Ordinal),
        "Single-instance startup should acquire the mutex before legacy-process discovery and ignore same-version startup races.");
}

static void RunReleaseVersionMetadataChecks()
{
    var project = File.ReadAllText(FindRepositoryFile("DellR730xdFanControlCenter.csproj"));
    var manifest = File.ReadAllText(FindRepositoryFile("Package.appxmanifest"));
    Require(
        project.Contains("<Version>1.1.2</Version>", StringComparison.Ordinal) &&
        project.Contains("<AssemblyVersion>1.1.2.0</AssemblyVersion>", StringComparison.Ordinal) &&
        project.Contains("<FileVersion>1.1.2.0</FileVersion>", StringComparison.Ordinal) &&
        project.Contains("<InformationalVersion>1.1.2</InformationalVersion>", StringComparison.Ordinal),
        "Release binaries should carry an explicit application version so fixed builds are distinguishable from 1.0.0 artifacts.");
    Require(
        manifest.Contains("Version=\"1.1.2.0\"", StringComparison.Ordinal),
        "MSIX package identity version should be bumped with release fixes so Windows can install an update instead of rejecting same-version packages.");
}

static void RunTestHarnessFailureExitSourceChecks()
{
    var source = File.ReadAllText(FindRepositoryFile("Program.cs"));
    Require(
        source.Contains("catch (Exception ex)", StringComparison.Ordinal) &&
        source.Contains("Preset model checks failed.", StringComparison.Ordinal) &&
        source.Contains("return 1;", StringComparison.Ordinal),
        "PresetModelTests should convert assertion failures into an explicit non-zero test result instead of an unhandled exception recorded as APPCRASH.");
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
    var signatureStart = source.IndexOf($"private void {methodName}", StringComparison.Ordinal);
    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private async void {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private Task {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private async Task {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private Task<bool> {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private async Task<bool> {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private string {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private static void {methodName}", StringComparison.Ordinal);
    }

    if (signatureStart < 0)
    {
        signatureStart = source.IndexOf($"private static float {methodName}", StringComparison.Ordinal);
    }

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

static bool ContainsBefore(string source, string first, string second)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    return firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex;
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

static void RunSensorReadingAvailabilityChecks()
{
    static SensorReading Reading(
        string key,
        string status = "ok",
        string value = "",
        double? numericValue = null)
    {
        return new SensorReading
        {
            Key = key,
            Status = status,
            Value = value,
            NumericValue = numericValue,
        };
    }

    Require(SensorReadingAvailability.IsDisplayable(Reading("CPU Usage", numericValue: 0)), "A real zero-percent performance reading should remain visible.");
    Require(SensorReadingAvailability.IsDisplayable(Reading("ROMB Battery")), "An ok discrete battery sensor with an empty numeric reading should remain visible.");
    Require(SensorReadingAvailability.IsDisplayable(Reading("Drive 0", value: "Drive Present")), "A real present-drive status should remain visible.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("Memory RAID", "ns", "No Reading")), "An unscanned Memory RAID placeholder should not be displayed.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("ROMB Battery", "ns", "Disabled")), "A disabled non-scannable ROMB placeholder should not be displayed.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("Drive 15", "ok", "Disabled")), "An explicitly disabled item should not be displayed as live hardware.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("Unknown", "na", "N/A")), "An unavailable SDR row should not be displayed.");
    Require(SensorReadingAvailability.IsDisplayable(Reading("Storage Alert", "cr", "No Reading")), "A real critical SDR status should remain visible even when it has no numeric reading.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("CPU Usage", numericValue: double.NaN)), "A non-finite numeric reading should not be displayed.");
    Require(!SensorReadingAvailability.IsDisplayable(Reading("", "", "")), "A completely empty SDR row should not be displayed.");
}

static void RunDashboardSensorPresentationChecks()
{
    static DashboardSensorPresentation Present(
        string key,
        double? numericValue = null,
        string unit = "",
        string status = "ok",
        string value = "")
    {
        return DashboardSensorPresentation.FromSensor(new SensorReading
        {
            Key = key,
            NumericValue = numericValue,
            Unit = unit,
            Status = status,
            Value = value,
        });
    }

    var kindCases = new (string Key, DashboardIconKind Kind, double? NumericValue)[]
    {
        ("Temp", DashboardIconKind.Temperature, 52),
        ("Fan1 RPM", DashboardIconKind.Fan, 3600),
        ("CPU Usage", DashboardIconKind.CpuUsage, 25),
        ("MEM Usage", DashboardIconKind.MemoryUsage, 40),
        ("IO Usage", DashboardIconKind.IoUsage, 10),
        ("SYS Usage", DashboardIconKind.SystemUsage, 30),
        ("Pwr Consumption", DashboardIconKind.Power, 420),
        ("Voltage 1", DashboardIconKind.Voltage, 230),
        ("Current 1", DashboardIconKind.Current, 1.8),
        ("Intrusion", DashboardIconKind.Intrusion, null),
        ("Fan Redundancy", DashboardIconKind.FanRedundancy, null),
        ("PS Redundancy", DashboardIconKind.PowerRedundancy, null),
        ("CMOS Battery", DashboardIconKind.CmosBattery, null),
        ("ROMB Battery", DashboardIconKind.RombBattery, null),
        ("BBU", DashboardIconKind.RombBattery, null),
        ("Drive 0", DashboardIconKind.StorageDrive, null),
        ("PERC Controller", DashboardIconKind.RaidController, null),
        ("Cache Policy", DashboardIconKind.StorageCache, null),
        ("USB Over-current", DashboardIconKind.UsbOverCurrent, null),
        ("Power Optimized", DashboardIconKind.PowerPolicy, null),
        ("Vendor Event", DashboardIconKind.GenericStatus, null),
    };
    foreach (var testCase in kindCases)
    {
        var presentation = DashboardSensorPresentation.FromSensor(new SensorReading
        {
            Key = testCase.Key,
            Status = "ok",
            NumericValue = testCase.NumericValue,
        });
        Require(presentation.IconKind == testCase.Kind, $"Raw sensor key '{testCase.Key}' should resolve to {testCase.Kind}.");
    }

    var nearMatch = DashboardSensorPresentation.FromSensor(new SensorReading
    {
        Key = "CPU Usage Detail",
        Unit = "percent",
        Status = "ok",
        NumericValue = 45,
    });
    Require(
        nearMatch.IconKind == DashboardIconKind.GenericStatus && nearMatch.VisualState == DashboardVisualState.Normal,
        "Classification should keep an unmatched ok sensor on a healthy generic state icon instead of guessing a metric type or threshold.");

    foreach (var fallback in new[]
             {
                 (Unit: "degrees C", Kind: DashboardIconKind.Temperature),
                 (Unit: "RPM", Kind: DashboardIconKind.Fan),
                 (Unit: "Watts", Kind: DashboardIconKind.Power),
                 (Unit: "Volts", Kind: DashboardIconKind.Voltage),
                 (Unit: "Amps", Kind: DashboardIconKind.Current),
             })
    {
        Require(
            Present("Vendor Reading", 25, fallback.Unit).IconKind == fallback.Kind,
            $"An otherwise unknown sensor with the unambiguous {fallback.Unit} unit should use {fallback.Kind}.");
    }

    foreach (var fallback in new[]
             {
                 (Unit: "Watts", Kind: DashboardIconKind.Power, Value: 420d),
                 (Unit: "Volts", Kind: DashboardIconKind.Voltage, Value: 230d),
                 (Unit: "Amps", Kind: DashboardIconKind.Current, Value: 1.5d),
             })
    {
        var unitOnlyElectrical = Present("Vendor Reading", fallback.Value, fallback.Unit);
        Require(
            unitOnlyElectrical.IconKind == fallback.Kind &&
            unitOnlyElectrical.VisualState == DashboardVisualState.Information &&
            unitOnlyElectrical.MotionKind == DashboardMotionKind.None &&
            !unitOnlyElectrical.IsMotionActive &&
            unitOnlyElectrical.NormalizedLevel == 0.5,
            $"An unknown {fallback.Unit} sensor should keep the electrical icon but use neutral informational presentation without trusted hardware thresholds.");

        var nonFiniteUnitOnlyElectrical = Present("Vendor Reading", double.NaN, fallback.Unit);
        Require(
            nonFiniteUnitOnlyElectrical.VisualState == DashboardVisualState.Unavailable &&
            nonFiniteUnitOnlyElectrical.MotionKind == DashboardMotionKind.None &&
            nonFiniteUnitOnlyElectrical.NormalizedLevel == 0,
            $"A non-finite unknown {fallback.Unit} sensor should remain unavailable rather than receiving neutral electrical presentation.");
    }

    var knownVoltagePresentation = Present("Voltage 1", 230, "Volts");
    Require(
        knownVoltagePresentation.VisualState == DashboardVisualState.Normal &&
        knownVoltagePresentation.MotionKind == DashboardMotionKind.GaugeTransition &&
        knownVoltagePresentation.IsMotionActive &&
        Math.Abs(knownVoltagePresentation.NormalizedLevel - (40d / 70d)) < 0.000001,
        "A known Voltage 1 sensor should retain trusted voltage severity, gauge motion, and 190-260 V normalization.");

    var unitOnlyCritical = Present("Vendor Reading", 230, "Volts", status: "cr");
    var unitOnlyWarning = Present("Vendor Reading", 230, "Volts", status: "nc");
    var unitOnlyDisabled = Present("Vendor Reading", 230, "Volts", status: "ok", value: "Disabled");
    Require(
        unitOnlyCritical.VisualState == DashboardVisualState.Critical &&
        unitOnlyCritical.MotionKind == DashboardMotionKind.WarningPulse &&
        unitOnlyCritical.IsMotionActive,
        "A unit-only Volts sensor should still honor a trusted critical status code before neutral threshold-free presentation.");
    Require(
        unitOnlyWarning.VisualState == DashboardVisualState.Warning &&
        unitOnlyWarning.MotionKind == DashboardMotionKind.WarningPulse &&
        unitOnlyWarning.IsMotionActive,
        "A unit-only Volts sensor should still honor a trusted warning status code.");
    Require(
        unitOnlyDisabled.VisualState == DashboardVisualState.Inactive &&
        unitOnlyDisabled.MotionKind == DashboardMotionKind.None &&
        !unitOnlyDisabled.IsMotionActive,
        "A unit-only Volts sensor should still honor an explicit Disabled value before neutral presentation.");

    var numericFanWarning = Present("Fan1 RPM", 3600, "RPM", status: "nc");
    var numericTemperatureCritical = Present("Temp 3.1", 65, "degrees C", status: "cr");
    var numericVoltageWarning = Present("Voltage 1", 250, "Volts", status: "nc");
    Require(
        numericFanWarning.VisualState == DashboardVisualState.Warning &&
        numericFanWarning.MotionKind == DashboardMotionKind.FanRotation &&
        numericFanWarning.MotionPeriodSeconds > 0 &&
        numericFanWarning.IsMotionActive,
        "A fan warning should preserve the real RPM rotation while adding warning state.");
    Require(
        numericTemperatureCritical.VisualState == DashboardVisualState.Critical &&
        numericTemperatureCritical.MotionKind == DashboardMotionKind.LevelTransition &&
        numericTemperatureCritical.NormalizedLevel == 0.65 &&
        numericTemperatureCritical.IsMotionActive,
        "A temperature critical status should preserve its real level transition while adding critical state.");
    Require(
        numericVoltageWarning.VisualState == DashboardVisualState.Warning &&
        numericVoltageWarning.MotionKind == DashboardMotionKind.GaugeTransition &&
        Math.Abs(numericVoltageWarning.NormalizedLevel - (60d / 70d)) < 0.000001 &&
        numericVoltageWarning.IsMotionActive,
        "A known-voltage warning should preserve its trusted gauge position while adding warning state.");

    var ambiguousPercent = Present("Vendor Usage", 25, "percent");
    Require(
        ambiguousPercent.IconKind == DashboardIconKind.GenericStatus && ambiguousPercent.VisualState == DashboardVisualState.Normal,
        "An unknown ok percent sensor should keep a healthy generic icon because percent alone cannot select a CPU, memory, IO, or system icon.");
    Require(
        Present("CPU Usage", 25, "Watts").IconKind == DashboardIconKind.CpuUsage &&
        Present("Pwr Consumption", 25, "RPM").IconKind == DashboardIconKind.Power &&
        Present("Voltage 1", 25, "degrees C").IconKind == DashboardIconKind.Voltage,
        "Exact raw keys should win over misleading fallback units.");

    foreach (var unavailable in new[]
             {
                  new SensorReading { Key = "CPU Usage", Status = "ns", NumericValue = 30 },
                  new SensorReading { Key = "Voltage 1", Status = "na", NumericValue = 230 },
                  new SensorReading { Key = "Fan Redundancy", Status = "ok", Value = "No Reading" },
                  new SensorReading { Key = "Fan Redundancy", Status = "ok", Value = "Unknown" },
                  new SensorReading { Key = "Vendor Battery", Status = "ok", Value = "Unknown" },
              })
    {
        var presentation = DashboardSensorPresentation.FromSensor(unavailable);
        Require(
            presentation.VisualState == DashboardVisualState.Unavailable && presentation.MotionKind == DashboardMotionKind.None && !presentation.IsMotionActive,
            "ns, na, No Reading, and Unknown sensors should be unavailable without motion.");
    }

    foreach (var inactiveValue in new[] { "Disabled", "Inactive" })
    {
        var presentation = Present("Power Optimized", status: "ok", value: inactiveValue);
        Require(presentation.VisualState == DashboardVisualState.Inactive, $"{inactiveValue} sensors should be inactive.");
    }

    foreach (var policyValue in new[] { "OEM", "Dell policy", "custom policy" })
    {
        var policy = Present("Power Optimized", value: policyValue);
        Require(
            policy.VisualState == DashboardVisualState.Information && policy.MotionKind == DashboardMotionKind.None,
            $"The standalone '{policyValue}' power policy reading should be informational and static.");
    }

    var vendorSpecificPolicy = Present("Power Optimized", value: "Vendor specific");
    Require(
        vendorSpecificPolicy.VisualState == DashboardVisualState.Information && vendorSpecificPolicy.MotionKind == DashboardMotionKind.None,
        "A vendor-specific power policy reading should be informational and static.");

    foreach (var status in new[] { "cr", "nr", "lc", "lcr", "lnr", "uc", "ucr", "unr", "critical" })
    {
        var presentation = Present("Fan Redundancy", status: status, value: "Disabled");
        Require(
            presentation.VisualState == DashboardVisualState.Critical && presentation.MotionKind == DashboardMotionKind.WarningPulse,
            $"Critical iDRAC status '{status}' should take precedence over a Disabled discrete value.");
    }

    foreach (var status in new[] { "nc", "lnc", "unc", "warning" })
    {
        var presentation = Present("Fan Redundancy", status: status, value: "Inactive");
        Require(
            presentation.VisualState == DashboardVisualState.Warning && presentation.MotionKind == DashboardMotionKind.WarningPulse,
            $"Warning iDRAC status '{status}' should take precedence over an Inactive discrete value.");
    }

    foreach (var unavailable in new[]
             {
                 Present("Fan Redundancy", status: "ns", value: "Failure Disabled"),
                 Present("Fan Redundancy", status: "na", value: "Failure Disabled"),
             })
    {
        Require(unavailable.VisualState == DashboardVisualState.Unavailable, "ns and na should remain higher priority than severity or inactive state text.");
    }
    Require(
        Present("Fan Redundancy", status: "cr", value: "No Reading").VisualState == DashboardVisualState.Critical,
        "A real critical SDR status should remain critical even when no numeric reading is available.");

    var degraded = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Fan Redundancy", Status = "Degraded" });
    var lost = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "PS Redundancy", Status = "ok", Value = "Redundancy Lost" });
    var failed = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "CMOS Battery", Status = "Failure" });
    var overCurrent = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "USB Over-current", Status = "ok", Value = "State Asserted" });
    Require(degraded.VisualState == DashboardVisualState.Warning, "Degraded redundancy should be a warning.");
    Require(lost.VisualState == DashboardVisualState.Critical, "Lost redundancy should be critical.");
    Require(failed.VisualState == DashboardVisualState.Critical, "Confirmed hardware failure should be critical.");
    Require(overCurrent.VisualState == DashboardVisualState.Critical, "An asserted USB over-current event should be critical.");
    Require(
        new[] { degraded, lost, failed, overCurrent }.All(item => item.MotionKind == DashboardMotionKind.WarningPulse && item.IsMotionActive),
        "Warning and critical discrete health states should pulse.");

    var normalHealth = DashboardSensorPresentation.FromSensor(new SensorReading
    {
        Key = "Fan Redundancy",
        Status = "ok",
        Value = "Fully Redundant",
    });
    Require(
        normalHealth.VisualState == DashboardVisualState.Normal && normalHealth.MotionKind == DashboardMotionKind.None && !normalHealth.IsMotionActive,
        "Normal discrete health should not run continuous motion.");

    var genericRedundancyNormal = Present("Redundancy", status: "ok", value: "Fully Redundant");
    var genericRedundancyInactive = Present("Redundancy", status: "ok", value: "Disabled");
    var genericRedundancyWarning = Present("Redundancy", status: "nc", value: "Degraded");
    var genericRedundancyCritical = Present("Redundancy", status: "cr", value: "Redundancy Lost");
    Require(
        genericRedundancyNormal.IconKind == DashboardIconKind.GenericStatus &&
        genericRedundancyNormal.VisualState == DashboardVisualState.Normal &&
        genericRedundancyNormal.MotionKind == DashboardMotionKind.None,
        "An unmatched ok redundancy sensor without a trusted type-specific rule should retain a healthy generic static state.");
    Require(
        genericRedundancyInactive.IconKind == DashboardIconKind.GenericStatus &&
        genericRedundancyInactive.VisualState == DashboardVisualState.Inactive &&
        genericRedundancyInactive.MotionKind == DashboardMotionKind.None,
        "The bare Redundancy key returned by iDRAC should show Disabled as an inactive static state.");
    Require(
        genericRedundancyWarning.IconKind == DashboardIconKind.GenericStatus &&
        genericRedundancyWarning.VisualState == DashboardVisualState.Warning &&
        genericRedundancyWarning.MotionKind == DashboardMotionKind.WarningPulse,
        "An unmatched redundancy warning should keep the generic icon and warning pulse.");
    Require(
        genericRedundancyCritical.IconKind == DashboardIconKind.GenericStatus &&
        genericRedundancyCritical.VisualState == DashboardVisualState.Critical &&
        genericRedundancyCritical.MotionKind == DashboardMotionKind.WarningPulse,
        "An unmatched redundancy failure should keep the generic icon and critical pulse.");

    var percentageLow = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "CPU Usage", Status = "ok", NumericValue = -5 });
    var percentageMid = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "MEM Usage", Status = "ok", NumericValue = 50 });
    var percentageHigh = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "IO Usage", Status = "ok", NumericValue = 130 });
    var temperature = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Temp 3.1", Status = "ok", NumericValue = 75 });
    Require(percentageLow.NormalizedLevel == 0 && percentageMid.NormalizedLevel == 0.5 && percentageHigh.NormalizedLevel == 1, "Percentage levels should clamp to 0..1.");
    Require(temperature.NormalizedLevel == 0.75 && temperature.MotionKind == DashboardMotionKind.LevelTransition, "Temperature level should normalize Celsius against 100 and transition between levels.");
    Require(temperature.AccentHex == HeroMetricSeverityStyle.ForTemperature(75).ForegroundHex, "Temperature accent should reuse hero metric severity styling.");

    var voltageBelowRange = Present("Voltage 1", 180);
    var voltageAtMinimum = Present("Voltage 1", 190);
    var voltageAtMidpoint = Present("Voltage 1", 225);
    var voltageAtMaximum = Present("Voltage 1", 260);
    var voltageAboveRange = Present("Voltage 1", 280);
    Require(
        voltageBelowRange.NormalizedLevel == 0 && voltageAtMinimum.NormalizedLevel == 0,
        "Voltage at or below the 190 V display floor should normalize to zero.");
    Require(
        voltageAtMidpoint.NormalizedLevel == 0.5,
        "Voltage at the 225 V midpoint of the 190-260 V display range should normalize to 0.5.");
    Require(
        voltageAtMaximum.NormalizedLevel == 1 && voltageAboveRange.NormalizedLevel == 1,
        "Voltage at or above the 260 V display ceiling should normalize to one.");

    foreach (var rpm in new double?[] { null, -1, 0 })
    {
        var fan = Present("Fan1 RPM", rpm);
        Require(
            fan.VisualState == DashboardVisualState.Inactive && fan.MotionKind == DashboardMotionKind.None && !fan.IsMotionActive &&
            fan.NormalizedLevel == 0 && fan.MotionPeriodSeconds == 0,
            $"Fan RPM '{rpm?.ToString(CultureInfo.InvariantCulture) ?? "null"}' should be inactive and static.");
    }

    var numericKeys = new[]
    {
        "Temp", "Fan1 RPM", "CPU Usage", "MEM Usage", "IO Usage", "SYS Usage", "Pwr Consumption", "Voltage 1", "Current 1",
    };
    foreach (var key in numericKeys)
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var presentation = Present(key, value);
            Require(
                presentation.VisualState == DashboardVisualState.Unavailable && presentation.MotionKind == DashboardMotionKind.None && !presentation.IsMotionActive &&
                presentation.NormalizedLevel == 0 && presentation.MotionPeriodSeconds == 0 &&
                double.IsFinite(presentation.NormalizedLevel) && double.IsFinite(presentation.MotionPeriodSeconds),
                $"Non-finite numeric value {value} for {key} should be unavailable with finite zero animation values.");
        }
    }

    foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        var genericNonFinite = Present("Vendor Usage", value, "percent");
        Require(
            genericNonFinite.IconKind == DashboardIconKind.GenericStatus &&
            genericNonFinite.VisualState == DashboardVisualState.Unavailable &&
            genericNonFinite.MotionKind == DashboardMotionKind.None,
            $"A generic sensor with non-finite numeric value {value} should be unavailable instead of appearing healthy.");
    }

    var stoppedFan = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Fan1 RPM", Status = "ok", NumericValue = 0 });
    var runningFan = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Fan1 RPM", Status = "ok", NumericValue = 9000 });
    var fan3480 = Present("Fan1 RPM", 3480);
    var fan3600 = Present("Fan1 RPM", 3600);
    var maximumFan = Present("Fan1 RPM", 18000);
    var overMaximumFan = Present("Fan1 RPM", 24000);
    var expectedFanPeriod = 1 / ((1 / 5.2) + (0.5 * ((1 / 0.11) - (1 / 5.2))));
    Require(
        stoppedFan.VisualState == DashboardVisualState.Inactive && stoppedFan.MotionKind == DashboardMotionKind.None && !stoppedFan.IsMotionActive && stoppedFan.MotionPeriodSeconds == 0,
        "A zero-RPM fan should be inactive without animation.");
    Require(
        runningFan.MotionKind == DashboardMotionKind.FanRotation && runningFan.IsMotionActive && Math.Abs(runningFan.MotionPeriodSeconds - expectedFanPeriod) < 0.000001,
        "Positive fan RPM should interpolate linearly in rotations per second between 5.2 s and 0.11 s periods.");
    Require(fan3600.MotionPeriodSeconds < fan3480.MotionPeriodSeconds, "3600 RPM should rotate faster than 3480 RPM.");
    Require(maximumFan.MotionPeriodSeconds == 0.11, "18000 RPM should use the exact 0.11-second fastest period.");
    Require(overMaximumFan.MotionPeriodSeconds == 0.11, "Fan RPM above 18000 should stay clamped to the exact 0.11-second fastest period.");
    Require(runningFan.AccentHex == HeroMetricSeverityStyle.ForFanRpm(9000).ForegroundHex, "Fan accent should reuse hero metric severity styling.");

    var power = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Pwr Consumption", Status = "ok", NumericValue = 550 });
    var voltage = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Voltage 1", Status = "ok", NumericValue = 230 });
    var current = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Current 1", Status = "ok", NumericValue = 1.5 });
    var zeroCurrent = DashboardSensorPresentation.FromSensor(new SensorReading { Key = "Current 1", Status = "ok", NumericValue = 0 });
    Require(power.MotionKind == DashboardMotionKind.PowerActivity && power.IsMotionActive, "Positive wattage should show power activity.");
    Require(voltage.MotionKind == DashboardMotionKind.GaugeTransition && voltage.IsMotionActive, "Voltage should use a gauge transition.");
    Require(current.MotionKind == DashboardMotionKind.CurrentFlow && current.IsMotionActive, "Positive current should show current flow.");
    Require(zeroCurrent.MotionKind == DashboardMotionKind.None && !zeroCurrent.IsMotionActive, "Zero current should not show current flow.");
    foreach (var key in new[] { "Pwr Consumption", "Current 1" })
    {
        foreach (var value in new[] { -1d, 0d })
        {
            var presentation = Present(key, value);
            Require(presentation.MotionKind == DashboardMotionKind.None && !presentation.IsMotionActive, $"Nonpositive {key} value {value} should not animate.");
        }
    }
    Require(power.AccentHex == HeroMetricSeverityStyle.ForPowerWatts(550).ForegroundHex, "Power accent should reuse hero metric severity styling.");
    Require(voltage.AccentHex == HeroMetricSeverityStyle.ForVoltage(230).ForegroundHex, "Voltage accent should reuse hero metric severity styling.");
    Require(current.AccentHex == HeroMetricSeverityStyle.ForCurrentAmps(1.5).ForegroundHex, "Current accent should reuse hero metric severity styling.");
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
    var pageSource = File.ReadAllText(FindRepositoryFile("MainPage.xaml.cs"));
    var displayNameBody = ExtractMethodBody(pageSource, "BuildVisualizationSensorName");
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
    Require(
        !displayNameBody.Contains("SensorDisplay.HardwareEvent", StringComparison.Ordinal) &&
        !pageSource.Contains("ShouldUseGenericHardwareEventTitle", StringComparison.Ordinal),
        "Unknown valid SDR names such as Drive 0 should remain their real names instead of being replaced by a fabricated generic hardware-event title.");
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
    Require(LocalizationService.T("Settings.Host", "zh-CN") == "iDRAC/BMC 地址", "Chinese BMC/iDRAC host field should preserve both controller terms.");

    var corruptedArabicUnits = resources["ar-SA"]
        .Where(resource => Regex.IsMatch(resource.Value, @"[\u0600-\u06FF]°C|°C[\u0600-\u06FF]"))
        .Select(resource => resource.Key)
        .ToArray();
    Require(
        corruptedArabicUnits.Length == 0,
        $"ar-SA contains a temperature unit embedded inside a word: {string.Join(", ", corruptedArabicUnits)}.");

    var domainMistranslations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ar-SA"] = ["المشجع", "معجب"],
        ["bn-BD"] = ["ভক্ত"],
        ["da-DK"] = ["alle fans", "individuelle fans", "alle fan procent", "forfriskende"],
        ["de-DE"] = ["alle fans", "einzelne fans", "anzahl der fans", "fan-index"],
        ["el-GR"] = ["θαυμαστ"],
        ["es-ES"] = ["aficionad", "todos los fans", "porcentaje de fans", "regla de fans", "recuento de fans"],
        ["fr-FR"] = ["tous les fans", "nombre de fans"],
        ["it-IT"] = ["tutti i fan", "regola fan"],
        ["nb-NO"] = ["alle fans", "individuelle fans", "antall fans", "fan-kommandoen", "forfriskende"],
        ["pl-PL"] = ["wszyscy fani"],
        ["th-TH"] = ["แฟน"],
        ["uk-UA"] = ["шанувальник"],
    };
    foreach (var locale in domainMistranslations)
    {
        foreach (var phrase in locale.Value)
        {
            var offenders = resources[locale.Key]
                .Where(resource => resource.Value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .Select(resource => resource.Key)
                .ToArray();
            Require(
                offenders.Length == 0,
                $"{locale.Key} translates hardware fans as admirers in: {string.Join(", ", offenders)}.");
        }
    }

    foreach (var locale in new[] { "bs-BA", "da-DK", "de-DE", "nb-NO", "pl-PL" })
    {
        Require(
            resources[locale]["Preset.DellAutoSubtitle"] != resources["en-US"]["Preset.DellAutoSubtitle"],
            $"{locale}/Preset.DellAutoSubtitle should localize the descriptive 'BMC auto' label.");
    }
}

static Dictionary<string, Dictionary<string, string>> GetLocalizationResources()
{
    var field = typeof(LocalizationService).GetField("Resources", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to inspect localization resources.");
    return (Dictionary<string, Dictionary<string, string>>)field.GetValue(null)!;
}

static void RunLocalizationIntegrityChecks()
{
    var resources = GetLocalizationResources();
    var reference = resources["en-US"];
    var protectedTerms = new[]
    {
        "Dell",
        "PowerEdge",
        "iDRAC",
        "IPMI",
        "BMC",
        "SDR",
        "CPU",
        "RPM",
        "OEM",
        "RMCP+",
        "ipmitool",
        "WebView2",
        "ECharts",
        "DPAPI",
        "JSONL",
        "°C",
    };
    var unitKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SensorUnit.Celsius"] = "°C",
        ["SensorUnit.Rpm"] = "RPM",
        ["SensorUnit.Watts"] = "W",
        ["SensorUnit.Volts"] = "V",
        ["SensorUnit.Amps"] = "A",
        ["SensorUnit.Percent"] = "%",
        ["Dashboard.TempPercentUnit"] = "°C / %",
    };

    foreach (var language in LocalizationService.SupportedLanguages)
    {
        var localized = resources[language.Code];
        var missingKeys = reference.Keys.Except(localized.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToArray();
        var extraKeys = localized.Keys.Except(reference.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToArray();
        Require(missingKeys.Length == 0, $"{language.Code} is missing localization keys: {string.Join(", ", missingKeys)}.");
        Require(extraKeys.Length == 0, $"{language.Code} has stale localization keys: {string.Join(", ", extraKeys)}.");

        foreach (var key in reference.Keys)
        {
            var referenceValue = reference[key];
            var localizedValue = localized[key];
            var referencePlaceholders = Regex.Matches(referenceValue, @"\{\d+(?::[^}]*)?\}")
                .Select(match => match.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var localizedPlaceholders = Regex.Matches(localizedValue, @"\{\d+(?::[^}]*)?\}")
                .Select(match => match.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Require(
                referencePlaceholders.SequenceEqual(localizedPlaceholders, StringComparer.Ordinal),
                $"{language.Code}/{key} changed format placeholders. Expected [{string.Join(", ", referencePlaceholders)}], got [{string.Join(", ", localizedPlaceholders)}].");
            Require(
                !localizedValue.Contains('\uFFFD') &&
                !localizedValue.Contains("<<<", StringComparison.Ordinal) &&
                !localizedValue.Contains(">>>", StringComparison.Ordinal),
                $"{language.Code}/{key} contains corrupt or generated placeholder text.");

            Require(
                !Regex.IsMatch(referenceValue, @"(?<![A-Za-z°])C(?![A-Za-z])"),
                $"en-US/{key} must use the canonical °C unit instead of a bare C.");

            foreach (Match unitMatch in Regex.Matches(
                         referenceValue,
                         @"(?<operand>\{\d+(?::[^}]*)?\}|\b\d+(?:\.\d+)?)\s*(?<unit>°C|W|V|A|%|s)(?![A-Za-z0-9])"))
            {
                var operandPattern = Regex.Escape(unitMatch.Groups["operand"].Value);
                var unitPattern = Regex.Escape(unitMatch.Groups["unit"].Value);
                var localizedUnitPattern = unitMatch.Groups["unit"].Value == "%"
                    ? $@"(?:{operandPattern}\s*{unitPattern}|{unitPattern}\s*{operandPattern})"
                    : operandPattern + @"\s*" + unitPattern + @"(?![A-Za-z0-9])";
                Require(
                    Regex.IsMatch(localizedValue, localizedUnitPattern),
                    $"{language.Code}/{key} must preserve the unit expression '{unitMatch.Value}'.");
            }

            foreach (var term in protectedTerms)
            {
                if (referenceValue.Contains(term, StringComparison.Ordinal))
                {
                    Require(
                        localizedValue.Contains(term, StringComparison.Ordinal),
                        $"{language.Code}/{key} must preserve the technical term '{term}' exactly: {localizedValue}");
                }
            }

            if (referenceValue.Contains("R730xd", StringComparison.OrdinalIgnoreCase))
            {
                Require(
                    localizedValue.Contains("R730xd", StringComparison.OrdinalIgnoreCase),
                    $"{language.Code}/{key} must preserve the R730xd model identifier: {localizedValue}");
            }
        }

        foreach (var unit in unitKeys)
        {
            Require(
                localized[unit.Key] == unit.Value,
                $"{language.Code}/{unit.Key} must preserve the unit '{unit.Value}' exactly.");
        }
    }

    var bannedMachineTranslationPhrases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ko-KR"] = ["투표", "설문조사", "진드기"],
        ["de-DE"] = ["Umfrage", "Häkchen"],
        ["es-ES"] = ["encuesta", "este tic"],
        ["fr-FR"] = ["sondage", "coche"],
        ["it-IT"] = ["sondaggio"],
        ["da-DK"] = ["afstemning", "flueben"],
        ["ja-JP"] = ["投票"],
        ["ru-RU"] = ["галочка"],
        ["bs-BA"] = ["glasanje", "anketa", "anketiranje", "obožavatelj"],
        ["pt-BR"] = ["fãs", "votação", "pesquisa", "marcação"],
        ["nb-NO"] = ["avstemning", "undersøkelse", "haken"],
        ["th-TH"] = ["สำรวจ", "หยั่งเสียง"],
        ["tr-TR"] = ["anket", "hayran", "onay işareti"],
        ["uk-UA"] = ["галочка"],
        ["bn-BD"] = ["ভোট"],
        ["el-GR"] = ["ψηφοφορία", "δημοσκόπηση", "τσιμπούρι"],
        ["vi-VN"] = ["người hâm mộ", "bỏ phiếu", "đánh dấu"],
        ["ar-SA"] = ["حقوق السحب الخاصة", "ipmitol", "المعجبين", "علامة التجزئة"],
    };
    foreach (var localePhrases in bannedMachineTranslationPhrases)
    {
        foreach (var phrase in localePhrases.Value)
        {
            var offenders = resources[localePhrases.Key]
                .Where(resource => resource.Value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .Select(resource => resource.Key)
                .ToArray();
            Require(
                offenders.Length == 0,
                $"{localePhrases.Key} contains the domain-inappropriate phrase '{phrase}' in: {string.Join(", ", offenders)}.");
        }
    }

    const string invalidLanguage = "xx-ZZ";
    const string missingKey = "Missing.Test.Key";
    foreach (var language in LocalizationService.SupportedLanguages)
    {
        Require(
            resources[language.Code].ContainsKey("Error.UnsupportedLanguage"),
            $"{language.Code} must localize unsupported-language failures.");
        Require(
            resources[language.Code].ContainsKey("Error.MissingTranslation"),
            $"{language.Code} must localize missing-translation failures.");

        LocalizationService.SetLanguage(language.Code);
        var culture = CultureInfo.GetCultureInfo(language.Code);
        var expectedUnsupported = string.Format(
            culture,
            resources[language.Code]["Error.UnsupportedLanguage"],
            invalidLanguage);
        var unsupportedOperations = new Action[]
        {
            () => LocalizationService.SetLanguage(invalidLanguage),
            () => LocalizationService.TranslateSensorValue("ok", invalidLanguage),
            () => LocalizationService.TranslateSensorDisplayName("temp", invalidLanguage),
            () => LocalizationService.T("Action.Save", invalidLanguage),
        };
        foreach (var operation in unsupportedOperations)
        {
            var exception = RecordInvalidOperation(operation);
            Require(
                exception.Message == expectedUnsupported,
                $"Unsupported-language failures should use the active {language.Code} resource text.");
        }

        var expectedMissing = string.Format(
            culture,
            resources[language.Code]["Error.MissingTranslation"],
            language.Code,
            missingKey);
        Require(
            RecordInvalidOperation(() => LocalizationService.T(missingKey, language.Code)).Message == expectedMissing,
            $"Missing-key failures should use the requested {language.Code} resource text.");
    }

    LocalizationService.SetLanguage(LocalizationService.DefaultLanguage);
}

static InvalidOperationException RecordInvalidOperation(Action operation)
{
    try
    {
        operation();
    }
    catch (InvalidOperationException exception)
    {
        return exception;
    }

    throw new InvalidOperationException("Expected the localization operation to throw InvalidOperationException.");
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
        "192.0.2.10",
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

static void RunPublishScriptChecks()
{
    var msixScript = File.ReadAllText(FindRepositoryFile(Path.Combine("tools", "Publish-SignedMsix.ps1")));
    var unpackagedScript = File.ReadAllText(FindRepositoryFile(Path.Combine("tools", "Publish-UnpackagedExe.ps1")));
    var releaseZipScript = File.ReadAllText(FindRepositoryFile(Path.Combine("tools", "Publish-ReleaseZip.ps1")));
    var releaseWorkflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));
    Require(
        msixScript.Contains("/p:WindowsAppSDKSelfContained=true", StringComparison.Ordinal),
        "Signed MSIX publishing should be Windows App SDK self-contained so the package does not require a separate Windows App Runtime install.");
    Require(
        msixScript.Contains("Package.Dependencies.PackageDependency", StringComparison.Ordinal) &&
        msixScript.Contains("external package dependencies", StringComparison.Ordinal),
        "Signed MSIX publishing should inspect the generated manifest and fail when external package dependencies remain.");
    Require(
        msixScript.Contains("\"Microsoft.WindowsAppRuntime.dll\"", StringComparison.Ordinal) &&
        msixScript.Contains("\"Microsoft.ui.xaml.dll\"", StringComparison.Ordinal) &&
        msixScript.Contains("\"BundledTools\\ipmitool\\ipmitool.exe\"", StringComparison.Ordinal),
        "Signed MSIX publishing should verify that Windows App SDK, WinUI, and bundled ipmitool runtime files are actually inside the package.");
    Require(
        msixScript.Contains("Get-AuthenticodeSignature", StringComparison.Ordinal),
        "Signed MSIX publishing should keep signature verification in addition to runtime-content validation.");
    Require(
        msixScript.Contains("$inspectionArchivePath", StringComparison.Ordinal) &&
        msixScript.Contains("Copy-Item -LiteralPath $package.FullName -Destination $inspectionArchivePath", StringComparison.Ordinal) &&
        msixScript.Contains("Expand-Archive -LiteralPath $inspectionArchivePath", StringComparison.Ordinal) &&
        !msixScript.Contains("Expand-Archive -LiteralPath $package.FullName", StringComparison.Ordinal),
        "Windows PowerShell 5.1 MSIX inspection should copy the package to a temporary .zip before Expand-Archive instead of passing the unsupported .msix extension directly.");
    Require(
        msixScript.Contains("Cert:\\LocalMachine\\TrustedPeople", StringComparison.Ordinal) &&
        msixScript.Contains("Cert:\\LocalMachine\\Root", StringComparison.Ordinal) &&
        msixScript.Contains("0x800B0109", StringComparison.Ordinal),
        "Signed MSIX publishing should verify LocalMachine deployment trust because Authenticode validity alone does not prove Add-AppxPackage will accept the package.");
    Require(
        msixScript.Contains("/p:PublishDir=$msixPublishDirectory\\", StringComparison.Ordinal) &&
        msixScript.Contains("legacyPackagedPublishDirectory", StringComparison.Ordinal),
        "Signed MSIX publishing should keep MSIX-only publish intermediates out of bin/Release publish folders so users do not run a non-release exe artifact.");
    Require(
        unpackagedScript.Contains("-p:WindowsPackageType=None", StringComparison.Ordinal) &&
        unpackagedScript.Contains("-p:WindowsAppSDKSelfContained=true", StringComparison.Ordinal) &&
        unpackagedScript.Contains("artifacts\\exe\\win-x64", StringComparison.Ordinal),
        "Direct exe releases should be produced only by the unpackaged publish script using WindowsPackageType=None and the dedicated artifacts/exe output.");
    Require(
        unpackagedScript.Contains("Remove-DirectoryIfPresent -Path $resolvedOutputDirectory", StringComparison.Ordinal) &&
        unpackagedScript.Contains("Assert-PathUnderRoot -Path $resolvedOutputDirectory", StringComparison.Ordinal),
        "Direct exe publishing should clean the dedicated output directory before publishing so stale WebView2 user-data folders or old files cannot leak into release zips.");
    Require(
        msixScript.Contains("function Assert-PathUnderRoot", StringComparison.Ordinal) &&
        msixScript.Contains("Assert-PathUnderRoot -Path $resolvedOutputDirectory", StringComparison.Ordinal) &&
        msixScript.Contains("Assert-PathUnderRoot -Path $intermediateDirectory", StringComparison.Ordinal) &&
        msixScript.Contains("Assert-PathUnderRoot -Path $legacyPackagedPublishDirectory", StringComparison.Ordinal),
        "Signed MSIX publishing should reject output and recursive-cleanup paths that resolve outside the repository.");
    Require(
        releaseZipScript.Contains("Publish-UnpackagedExe.ps1", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Compress-Archive", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Expand-Archive", StringComparison.Ordinal),
        "Release zip publishing should reuse the unpackaged exe script and verify the downloaded-zip shape after compression.");
    Require(
        releaseZipScript.Contains("function Assert-PathUnderRoot", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Assert-PathUnderRoot -Path $resolvedReleaseOutputDirectory", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Assert-PathUnderRoot -Path $zipPath", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Assert-PathUnderRoot -Path $verificationDirectory", StringComparison.Ordinal),
        "Release zip publishing should reject archive and recursive verification paths that resolve outside the repository.");
    Require(
        releaseZipScript.Contains("Remove-DirectoryIfPresent -Path $resolvedReleaseOutputDirectory", StringComparison.Ordinal),
        "Release zip publishing should clean the dedicated release output directory before compression so stale downloaded builds or WebView2 user data cannot remain beside the new zip.");
    Require(
        releaseZipScript.Contains("Microsoft.WindowsAppRuntime.dll", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Microsoft.ui.xaml.dll", StringComparison.Ordinal) &&
        releaseZipScript.Contains("DellR730xdFanControlCenter.pri", StringComparison.Ordinal) &&
        releaseZipScript.Contains("BundledTools\\ipmitool\\ipmitool.exe", StringComparison.Ordinal) &&
        releaseZipScript.Contains("VerifyLaunch", StringComparison.Ordinal),
        "Release zip verification should check WinUI runtime files, bundled ipmitool, and provide an explicit local launch verification mode.");
    Require(
        releaseZipScript.Contains("\".msix\"", StringComparison.Ordinal) &&
        releaseZipScript.Contains("\".pfx\"", StringComparison.Ordinal) &&
        releaseZipScript.Contains("\".cer\"", StringComparison.Ordinal) &&
        releaseZipScript.Contains("AppxManifest.xml", StringComparison.Ordinal) &&
        releaseZipScript.Contains("Package.appxmanifest", StringComparison.Ordinal) &&
        releaseZipScript.Contains("DellR730xdFanControlCenter.exe.WebView2", StringComparison.Ordinal) &&
        releaseZipScript.Contains("signed/package-identity files are not allowed", StringComparison.Ordinal),
        "Release zip verification should fail when signed MSIX, certificate, package identity files, or WebView2 user data leak into the unsigned downloadable zip.");
    Require(
        releaseWorkflow.Contains("windows-latest", StringComparison.Ordinal) &&
        releaseWorkflow.Contains("dotnet run --project .\\Tests\\PresetModelTests\\PresetModelTests.csproj", StringComparison.Ordinal) &&
        releaseWorkflow.Contains(".\\tools\\Publish-ReleaseZip.ps1", StringComparison.Ordinal) &&
        releaseWorkflow.Contains("actions/upload-artifact@v4", StringComparison.Ordinal) &&
        releaseWorkflow.Contains("gh release upload", StringComparison.Ordinal) &&
        releaseWorkflow.Contains("--clobber", StringComparison.Ordinal),
        "GitHub Actions release workflow should build on Windows, run tests, package the release zip, upload the workflow artifact, and replace release assets on tag reruns.");
    Require(
        releaseWorkflow.IndexOf("Publish GitHub Release asset", StringComparison.Ordinal) <
        releaseWorkflow.IndexOf("Upload workflow artifact", StringComparison.Ordinal) &&
        releaseWorkflow.Contains("if: ${{ !startsWith(github.ref, 'refs/tags/') }}", StringComparison.Ordinal),
        "Tag-triggered releases should upload the GitHub Release asset before the manual-run workflow artifact path so artifact quota does not block release publication.");
    Require(
        !releaseWorkflow.Contains("Publish-SignedMsix.ps1", StringComparison.Ordinal) &&
        !releaseWorkflow.Contains("Add-AppxPackage", StringComparison.Ordinal) &&
        !releaseWorkflow.Contains("Get-AuthenticodeSignature", StringComparison.Ordinal) &&
        !releaseWorkflow.Contains(".msix", StringComparison.Ordinal) &&
        !releaseWorkflow.Contains(".pfx", StringComparison.Ordinal) &&
        !releaseWorkflow.Contains(".cer", StringComparison.Ordinal),
        "GitHub Actions release workflow should publish the unsigned unpackaged zip only, not a signed MSIX or certificate-dependent artifact.");
}

static void RunInfoBarAccessibilityLocalizationChecks()
{
    XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    var document = XDocument.Load(FindRepositoryFile("MainPage.xaml"), LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    var infoBars = document.Descendants().Where(element => element.Name.LocalName == "InfoBar").ToArray();

    var statusInfoBar = infoBars.SingleOrDefault(element => element.Attribute(xamlNamespace + "Name")?.Value == "StatusInfoBar");
    Require(statusInfoBar is not null, "StatusInfoBar should exist in MainPage.xaml.");
    Require(
        string.Equals(statusInfoBar!.Attribute("IsIconVisible")?.Value, "False", StringComparison.Ordinal),
        "StatusInfoBar should hide WinUI's default InfoBar icon so UI Automation does not expose a fixed English icon name.");

    var individualFanStatusPanel = document
        .Descendants()
        .SingleOrDefault(element => element.Attribute(xamlNamespace + "Name")?.Value == "IndividualFanStatusPanel");
    Require(
        individualFanStatusPanel?.Attribute("AutomationProperties.Name") is not null,
        "The compact individual-fan state should keep an accessible name after replacing its visible InfoBar.");
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

    using (var temp = TempDirectory.Create("R730xdAppLogConcurrencyTests"))
    {
        const int writerCount = 8;
        const int recordsPerWriter = 500;
        var log = new AppLogService(temp.Path, () => baseTime);
        var writers = Enumerable.Range(0, writerCount)
            .Select(writer => Task.Run(() =>
            {
                for (var record = 0; record < recordsPerWriter; record++)
                {
                    log.Write(new AppLogRecord
                    {
                        Level = "Info",
                        Category = "ConcurrencyTest",
                        EventName = "ParallelWrite",
                        Message = $"writer={writer};record={record}",
                    });
                }
            }))
            .ToArray();
        Task.WhenAll(writers).GetAwaiter().GetResult();
        log.FlushAsync().GetAwaiter().GetResult();

        var lines = File.ReadAllLines(log.CurrentLogPath);
        Require(lines.Length == writerCount * recordsPerWriter, "Concurrent runtime-log writers should not lose or merge JSONL records.");
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Require(
                document.RootElement.GetProperty("eventName").GetString() == "ParallelWrite",
                "Every concurrently written runtime-log line should remain valid, complete JSON.");
        }
    }

    using (var temp = TempDirectory.Create("R730xdAppLogWriteFailureTests"))
    {
        var blockedDirectory = Path.Combine(temp.Path, "logs-blocked-by-file");
        File.WriteAllText(blockedDirectory, "not a directory");
        var log = new AppLogService(blockedDirectory, () => baseTime);
        Exception? observedFailure = null;
        var handlerFailureCount = 0;
        log.WriteFailed += (_, _) =>
        {
            handlerFailureCount++;
            throw new InvalidOperationException("subscriber failed");
        };
        log.WriteFailed += (_, ex) => observedFailure = ex;
        log.Write(new AppLogRecord
        {
            Level = "Info",
            Category = "UnitTest",
            EventName = "BlockedLogDirectory",
            Message = "This write must fail during the real file append.",
        });
        RequireThrows<InvalidOperationException>(
            () => log.FlushAsync().GetAwaiter().GetResult(),
            "FlushAsync should expose runtime log write failures instead of allowing callers to report success.");
        Require(observedFailure is not null, "The WriteFailed event should still expose the underlying runtime log write exception to the UI.");
        Require(handlerFailureCount == 1, "A failing WriteFailed subscriber should be observed exactly once for the first failed write.");

        log.Write(new AppLogRecord
        {
            Level = "Info",
            Category = "UnitTest",
            EventName = "BlockedLogDirectoryAgain",
            Message = "The write worker must keep processing after a WriteFailed subscriber throws.",
        });
        var secondFlush = log.FlushAsync();
        var secondFlushCompleted = Task.WhenAny(secondFlush, Task.Delay(TimeSpan.FromSeconds(2))).GetAwaiter().GetResult() == secondFlush;
        Require(secondFlushCompleted, "The runtime log write worker should not stop when a WriteFailed subscriber throws.");
        RequireThrows<InvalidOperationException>(
            () => secondFlush.GetAwaiter().GetResult(),
            "The second failed write should still surface the real log write failure after a subscriber exception.");
        Require(handlerFailureCount == 2, "A failing WriteFailed subscriber should be notified for each failed write without stopping later writes.");
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
