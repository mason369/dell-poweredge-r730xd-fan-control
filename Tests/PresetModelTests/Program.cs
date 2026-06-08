using DellR730xdFanControlCenter;
using System.Text.Json;

var defaults = FanPreset.CreateDefaultPresets();
Require(defaults.Count >= 5, "Default preset list should include restore, balanced, cooling, performance, and Dell automatic presets.");
Require(new AppSettings().SensorRefreshSeconds == 1, "Default SDR polling interval should remain the original 1 second setting.");

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
Require(curve.CalculateFanPercent(57.5) == 27, "Curve temperatures between points should use rounded linear interpolation.");
Require(curve.CalculateFanPercent(90) == 60, "Curve temperatures above the last point should use the last point percent.");
curve.SmoothCurve = true;
Require(curve.CalculateFanPercent(55) == 24, "Smooth curve mode should use softened interpolation between curve points.");
curve.SmoothCurve = false;

curve.CurvePointsText = "50 = 20" + Environment.NewLine + "80 = 50";
curve.ApplyCurvePointsText();
Require(curve.CurvePoints.Count == 2, "Editable curve point text should replace the curve point list.");
Require(curve.CalculateFanPercent(65) == 35, "Edited curve point text should drive later interpolation.");
curve.SmoothCurve = true;
Require(curve.CalculateFanPercent(60) == 28, "Edited curve points should still support smooth interpolation.");

var heroMetrics = HeroRealtimeMetrics.FromSensors(
[
    new SensorReading { Key = "Inlet Temp", Unit = "degrees C", NumericValue = 30 },
    new SensorReading { Key = "Exhaust Temp", Unit = "degrees C", NumericValue = 50 },
    new SensorReading { Key = "Fan1 RPM", Unit = "RPM", NumericValue = 3240 },
    new SensorReading { Key = "Fan2 RPM", Unit = "RPM", NumericValue = 3360 },
    new SensorReading { Key = "Fan3 RPM", Unit = "RPM", NumericValue = 3480 },
    new SensorReading { Key = "Fan4 RPM", Unit = "RPM", NumericValue = 3600 },
    new SensorReading { Key = "Pwr Consumption", Unit = "Watts", NumericValue = 784 },
    new SensorReading { Key = "1路电压", Unit = "Volts", NumericValue = 232 },
    new SensorReading { Key = "2路电压", Unit = "Volts", NumericValue = 234 },
    new SensorReading { Key = "1路电流", Unit = "Amps", NumericValue = 1.6 },
    new SensorReading { Key = "2路电流", Unit = "Amps", NumericValue = 1.8 },
]);
Require(heroMetrics.CurrentTemperatureCelsius == 40, "Hero live temperature should use the latest current average instead of the highest temperature.");
Require(heroMetrics.AverageFanRpm == 3420, "Hero live fan RPM should use the latest average fan speed.");
Require(heroMetrics.PowerWatts == 784, "Hero live power should use the latest power sensor reading.");
Require(heroMetrics.AverageVoltage == 233, "Hero live voltage should use the latest average voltage.");
Require(heroMetrics.TotalCurrent == 3.4, "Hero live current should use the latest total current.");
Require(heroMetrics.TemperatureItems.Count == 2, "Hero temperature details should include current temperature sensor items.");
Require(heroMetrics.FanItems.Count == 3, "Hero fan details should be limited for compact display.");
Require(heroMetrics.HiddenFanItemCount == 1, "Hero fan details should report hidden fan item count.");
Require(heroMetrics.PowerItems.Single().Key == "Pwr Consumption", "Hero power details should include the current power sensor item.");
Require(heroMetrics.VoltageItems.Count == 2, "Hero voltage details should include voltage rail items.");
Require(heroMetrics.CurrentItems.Count == 2, "Hero current details should include current rail items.");

LocalizationService.SetLanguage("zh-CN");
Require(LocalizationService.T("SensorValue.StateDeasserted") == "状态解除", "State Deasserted must have a Chinese translation.");
Require(LocalizationService.T("SensorValue.StateAsserted") == "状态触发", "State Asserted must have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ConnectionStatus")), "Hero connection label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.ModeStatus")), "Hero mode label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestStatus")), "Hero request label should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestRunning")), "Hero running request format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.RequestSucceeded")), "Hero completed request format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.SensorRefreshSucceeded")), "Hero sensor refresh result format should have a Chinese translation.");
Require(!string.IsNullOrWhiteSpace(LocalizationService.T("Hero.LastUpdateValue")), "Hero last update format should have a Chinese translation.");
Require(LocalizationService.T("Status.PollingSkippedPreviousRunning").Contains("未启动新的 ipmitool", StringComparison.Ordinal), "Chinese skipped polling log should state that no new ipmitool process is started.");
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
Require(LocalizationService.T("Tray.RestoreDefault").Contains("戴尔出厂设置转速", StringComparison.Ordinal), "Chinese tray restore action should name Dell factory fan speed.");
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
Require(LocalizationService.T("Tray.RestoreDefault").Contains("Dell factory fan speed", StringComparison.Ordinal), "English tray restore action should name Dell factory fan speed.");
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
    var savedPresets = defaults.Concat([curve]).ToList();
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
    Require(loadedCurve.CalculateFanPercent(65) == 35, "Loaded temperature curve preset should still interpolate fan percent.");
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

Console.WriteLine("Preset model checks passed.");

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

    Require(!PollingSkipLogGate.OpenTopStatusForSkippedTick, "Skipped polling ticks should not open or overwrite the top InfoBar.");
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
    LocalizationService.SetLanguage("zh-CN");
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
