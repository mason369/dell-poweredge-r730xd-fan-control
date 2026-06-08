using DellR730xdFanControlCenter;
using System.Text.Json;

var defaults = FanPreset.CreateDefaultPresets();
Require(defaults.Count >= 5, "Default preset list should include restore, balanced, cooling, performance, and Dell automatic presets.");
Require(new AppSettings().SensorRefreshSeconds == 15, "Default SDR polling interval should be 15 seconds to avoid hammering iDRAC with back-to-back RMCP+ sessions.");

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

var tempSettingsDirectory = Path.Combine(Path.GetTempPath(), "R730xdPresetModelTests", Guid.NewGuid().ToString("N"));
try
{
    var settingsStore = new SettingsStore(tempSettingsDirectory);
    RequireThrows<InvalidOperationException>(
        () => settingsStore.Save(new AppSettings { SensorRefreshSeconds = 1 }),
        "Saving a 1-second SDR polling interval should fail instead of silently keeping an unsafe cadence.");

    Directory.CreateDirectory(tempSettingsDirectory);
    File.WriteAllText(
        settingsStore.SettingsPath,
        JsonSerializer.Serialize(new AppSettings { SensorRefreshSeconds = 1 }));
    Require(
        settingsStore.Load().SensorRefreshSeconds == 1,
        "Legacy unsafe SDR polling settings should stay visible on load so startup can stop and explain the configuration problem.");

    settingsStore.Save(new AppSettings { Presets = defaults });
    var loaded = settingsStore.Load();
    var loadedBalanced = loaded.Presets.Single(preset => preset.Id == "balanced");
    Require(loadedBalanced.EditableName == "夜间静音", "Edited built-in preset name should survive settings save/load.");
    Require(loadedBalanced.EditableDetail == "晚上降低噪音的自定义说明", "Edited built-in preset description should survive settings save/load.");
    Require(loadedBalanced.Percent == 18, "Edited built-in preset percent should survive settings save/load.");
}
finally
{
    if (Directory.Exists(tempSettingsDirectory))
    {
        Directory.Delete(tempSettingsDirectory, recursive: true);
    }
}

Console.WriteLine("Preset model checks passed.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
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

    throw new InvalidOperationException(message);
}
