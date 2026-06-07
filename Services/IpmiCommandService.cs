using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DellR730xdFanControlCenter;

public sealed partial class IpmiCommandService
{
    private const string PasswordEnvironmentVariable = "IPMI_PASSWORD";

    public event EventHandler<CommandTraceEventArgs>? CommandCompleted;

    public Task SetAllFansManualSpeedAsync(IdracProfile profile, int percent, CancellationToken cancellationToken)
    {
        AppSettings.ValidatePercent(percent, nameof(percent));
        return ExecuteFanSetSequenceAsync(profile, 0xff, percent, cancellationToken);
    }

    public Task SetSingleFanManualSpeedAsync(IdracProfile profile, int fanIndex, int percent, CancellationToken cancellationToken)
    {
        if (fanIndex is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), fanIndex, "R730xd fan index must be between 1 and 6.");
        }

        AppSettings.ValidatePercent(percent, nameof(percent));
        return ExecuteFanSetSequenceAsync(profile, fanIndex - 1, percent, cancellationToken);
    }

    public async Task SetDellAutomaticModeAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["raw", "0x30", "0x30", "0x01", "0x01"], cancellationToken);
    }

    public async Task SetManualModeAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["raw", "0x30", "0x30", "0x01", "0x00"], cancellationToken);
    }

    public async Task TestConnectionAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["mc", "info"], cancellationToken);
    }

    public async Task<IReadOnlyList<SensorReading>> ReadSensorsAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(profile, ["sdr", "elist"], cancellationToken);
        var readings = ParseSensorReadings(result.StandardOutput).ToList();

        if (readings.Count == 0)
        {
            throw new InvalidOperationException("ipmitool completed but returned no SDR sensor rows.");
        }

        return readings;
    }

    private async Task ExecuteFanSetSequenceAsync(
        IdracProfile profile,
        int targetByte,
        int percent,
        CancellationToken cancellationToken)
    {
        await SetManualModeAsync(profile, cancellationToken);
        await ExecuteAsync(
            profile,
            ["raw", "0x30", "0x30", "0x02", ToHexByte(targetByte), ToHexByte(percent)],
            cancellationToken);
    }

    private async Task<IpmiCommandResult> ExecuteAsync(
        IdracProfile profile,
        IReadOnlyList<string> ipmiArguments,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profile);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(profile.CommandTimeoutSeconds));

        var toolPath = ResolveToolPath(profile.IpmiToolPath);
        var arguments = BuildArguments(profile, ipmiArguments);
        var commandLine = $"{Quote(toolPath)} {string.Join(" ", arguments.Select(Quote))}";
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            WorkingDirectory = Path.GetDirectoryName(toolPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        process.StartInfo.Environment[PasswordEnvironmentVariable] = profile.Password;

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start ipmitool process: {profile.IpmiToolPath}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot start bundled ipmitool at '{toolPath}'. Rebuild the app so BundledTools\\ipmitool is included.",
                ex);
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            stopwatch.Stop();
            var result = new IpmiCommandResult(process.ExitCode, stdout, stderr);
            CommandCompleted?.Invoke(
                this,
                new CommandTraceEventArgs
                {
                    CommandLine = commandLine,
                    Succeeded = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Elapsed = stopwatch.Elapsed,
                });

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ipmitool failed with exit code {process.ExitCode}.{Environment.NewLine}{BuildFailureDetail(stdout, stderr)}");
            }

            return result;
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"ipmitool command exceeded the configured timeout of {profile.CommandTimeoutSeconds} seconds: {commandLine}",
                ex);
        }
    }

    private static IReadOnlyList<string> BuildArguments(IdracProfile profile, IReadOnlyList<string> ipmiArguments)
    {
        return ["-I", "lanplus", "-H", profile.Host, "-U", profile.UserName, "-E", .. ipmiArguments];
    }

    public static string ResolveToolPath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? AppSettings.BundledIpmiToolRelativePath
            : configuredPath;

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static void ValidateProfile(IdracProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.IpmiToolPath))
        {
            throw new InvalidOperationException("Bundled ipmitool path is empty.");
        }

        var resolvedToolPath = ResolveToolPath(profile.IpmiToolPath);
        if (!File.Exists(resolvedToolPath))
        {
            throw new FileNotFoundException("Bundled ipmitool.exe is missing from the application output.", resolvedToolPath);
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new InvalidOperationException("iDRAC host/IP is empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.UserName))
        {
            throw new InvalidOperationException("iDRAC username is empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.Password))
        {
            throw new InvalidOperationException("iDRAC password is empty.");
        }

        if (profile.CommandTimeoutSeconds < 5)
        {
            throw new InvalidOperationException("Command timeout must be at least 5 seconds.");
        }
    }

    private static IEnumerable<SensorReading> ParseSensorReadings(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var status = parts.Length >= 5 ? parts[2] : parts[2];
            var readingText = parts.Length >= 5 ? parts[4] : parts[1];
            var readingParts = SplitReading(readingText);
            yield return new SensorReading
            {
                Key = parts[0],
                SensorId = parts.Length >= 5 ? parts[1] : string.Empty,
                Entity = parts.Length >= 5 ? parts[3] : string.Empty,
                Value = readingParts.Value,
                Unit = readingParts.Unit,
                Status = status,
                NumericValue = readingParts.NumericValue,
            };
        }
    }

    private static (string Value, string Unit, double? NumericValue) SplitReading(string reading)
    {
        var match = NumericPrefixRegex().Match(reading);
        if (!match.Success)
        {
            return (reading, string.Empty, null);
        }

        var valueText = match.Groups["value"].Value;
        var unit = reading[valueText.Length..].Trim();
        var numericValue = double.Parse(valueText, CultureInfo.InvariantCulture);
        return (valueText, unit, numericValue);
    }

    public static double FindCpuTemperatureCelsius(IEnumerable<SensorReading> sensors)
    {
        var temperatureRows = sensors
            .Where(sensor =>
                sensor.NumericValue.HasValue &&
                (sensor.Unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase) ||
                 sensor.Key.Contains("Temp", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var cpuRows = temperatureRows
            .Where(sensor => sensor.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = cpuRows.Count > 0 ? cpuRows : temperatureRows;
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No CPU temperature sensor was found in the SDR output.");
        }

        return candidates.Max(sensor => sensor.NumericValue!.Value);
    }

    private static string ToHexByte(int value)
    {
        if (value is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Raw IPMI byte must be between 0 and 255.");
        }

        return $"0x{value:x2}";
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static string BuildFailureDetail(string stdout, string stderr)
    {
        var detail = string.Join(
            Environment.NewLine,
            new[] { stderr.Trim(), stdout.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));

        return string.IsNullOrWhiteSpace(detail) ? "No output was returned by ipmitool." : detail;
    }

    private static void TryKill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    [GeneratedRegex(@"^\s*(?<value>-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex NumericPrefixRegex();

    private sealed record IpmiCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
