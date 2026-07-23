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

    public Task SetAllFansSpeedInConfirmedManualModeAsync(
        IdracProfile profile,
        int percent,
        CancellationToken cancellationToken)
    {
        AppSettings.ValidatePercent(percent, nameof(percent));
        return ExecuteAsync(
            profile,
            ["raw", "0x30", "0x30", "0x02", "0xff", ToHexByte(percent)],
            cancellationToken);
    }

    public Task SetSingleFanManualSpeedAsync(IdracProfile profile, int fanIndex, int percent, CancellationToken cancellationToken)
    {
        if (fanIndex is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), fanIndex, "R730xd 风扇编号必须在 1 到 6 之间。");
        }

        AppSettings.ValidatePercent(percent, nameof(percent));
        return ExecuteFanSetSequenceAsync(profile, fanIndex - 1, percent, cancellationToken);
    }

    public async Task SetDellAutomaticModeAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["raw", "0x30", "0x30", "0x01", "0x01"], cancellationToken).ConfigureAwait(false);
    }

    public async Task SetManualModeAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["raw", "0x30", "0x30", "0x01", "0x00"], cancellationToken).ConfigureAwait(false);
    }

    public async Task TestConnectionAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        await ExecuteAsync(profile, ["mc", "info"], cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SensorReading>> ReadSensorsAsync(IdracProfile profile, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(profile, ["sdr", "elist"], cancellationToken).ConfigureAwait(false);
        var readings = ParseSensorReadings(result.StandardOutput).ToList();

        if (readings.Count == 0)
        {
            throw new InvalidOperationException("ipmitool 已完成，但未返回任何 SDR 传感器行。");
        }

        return readings;
    }

    private async Task ExecuteFanSetSequenceAsync(
        IdracProfile profile,
        int targetByte,
        int percent,
        CancellationToken cancellationToken)
    {
        await SetManualModeAsync(profile, cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                profile,
                ["raw", "0x30", "0x30", "0x02", ToHexByte(targetByte), ToHexByte(percent)],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception fanSetFailure)
        {
            try
            {
                await ExecuteAsync(
                    profile,
                    ["raw", "0x30", "0x30", "0x01", "0x01"],
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception recoveryFailure)
            {
                throw new InvalidOperationException(
                    "进入手动风扇模式后，设置目标百分比失败；随后发送一次 Dell 自动恢复命令也失败。当前 BMC 风扇模式无法确认，可能仍停留在手动模式并保持旧转速。",
                    new AggregateException(fanSetFailure, recoveryFailure));
            }

            throw new FanCommandSafetyRecoveryException(
                "进入手动风扇模式后，设置目标百分比失败；已发送一次 Dell 自动恢复命令并确认成功。原手动控制请求仍标记为失败。",
                fanSetFailure);
        }
    }

    private async Task<IpmiCommandResult> ExecuteAsync(
        IdracProfile profile,
        IReadOnlyList<string> ipmiArguments,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profile);

        var toolPath = ResolveToolPath(profile.IpmiToolPath);
        var arguments = BuildArguments(profile, ipmiArguments);
        var commandLine = $"{Quote(toolPath)} {string.Join(" ", arguments.Select(Quote))}";
        var result = await ExecuteProcessAsync(profile, toolPath, arguments, commandLine, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return result;
        }

        throw new InvalidOperationException(BuildCommandFailureMessage(result));
    }

    private async Task<IpmiCommandResult> ExecuteProcessAsync(
        IdracProfile profile,
        string toolPath,
        IReadOnlyList<string> arguments,
        string commandLine,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(profile.CommandTimeoutSeconds));

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

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"无法启动 ipmitool 进程：{profile.IpmiToolPath}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"无法启动内置 ipmitool：{toolPath}。请重新构建应用，确保 BundledTools\\ipmitool 已包含在输出目录中。",
                ex);
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            stopwatch.Stop();
            var finishedAt = DateTimeOffset.Now;
            var result = new IpmiCommandResult(process.ExitCode, stdout, stderr);
            CommandCompleted?.Invoke(
                this,
                new CommandTraceEventArgs
                {
                    CommandLine = commandLine,
                    Succeeded = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Elapsed = stopwatch.Elapsed,
                    StartedAt = startedAt,
                    FinishedAt = finishedAt,
                });

            return result;
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"ipmitool 命令超过配置的 {profile.CommandTimeoutSeconds} 秒超时时间：{commandLine}",
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
            throw new InvalidOperationException("内置 ipmitool 路径为空。");
        }

        var resolvedToolPath = ResolveToolPath(profile.IpmiToolPath);
        if (!File.Exists(resolvedToolPath))
        {
            throw new FileNotFoundException("应用输出目录中缺少内置 ipmitool.exe。", resolvedToolPath);
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new InvalidOperationException("iDRAC 主机/IP 为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.UserName))
        {
            throw new InvalidOperationException("iDRAC 用户名为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.Password))
        {
            throw new InvalidOperationException("iDRAC 密码为空。");
        }

        if (profile.CommandTimeoutSeconds < 5)
        {
            throw new InvalidOperationException("命令超时时间至少需要 5 秒。");
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
        return TryFindCpuTemperatureCelsius(sensors)
            ?? throw new InvalidOperationException("SDR 输出中未找到 CPU 温度传感器。");
    }

    public static double? TryFindCpuTemperatureCelsius(IEnumerable<SensorReading> sensors)
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
            return null;
        }

        return candidates.Max(sensor => sensor.NumericValue!.Value);
    }

    private static string ToHexByte(int value)
    {
        if (value is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "IPMI raw 字节必须在 0 到 255 之间。");
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

        return string.IsNullOrWhiteSpace(detail) ? "ipmitool 未返回任何输出。" : detail;
    }

    private static string BuildCommandFailureMessage(IpmiCommandResult result)
    {
        return $"ipmitool 执行失败，退出码为 {result.ExitCode}。{Environment.NewLine}{BuildFailureDetail(result.StandardOutput, result.StandardError)}";
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
