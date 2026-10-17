using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace NanoBot.Plugins;

/// <summary>
/// Read-only Linux system diagnostics (Wi-Fi, memory, CPU temperature, uptime, disk), read from /proc and /sys.
/// </summary>
public sealed class SystemInfoPlugin
{
    private const string WirelessFile = "/proc/net/wireless";
    private const string MemInfoFile = "/proc/meminfo";
    private const string UptimeFile = "/proc/uptime";
    private const string ThermalFile = "/sys/class/thermal/thermal_zone0/temp";

    private readonly ILogger _logger;

    public SystemInfoPlugin(ILogger<SystemInfoPlugin> logger)
    {
        _logger = logger;
    }

    [Description("Get a status report of this device: Wi-Fi signal strength, memory usage, CPU temperature, uptime and free disk space. " +
                 "Example: user asks 'how is the wifi signal', 'how much memory is free', 'how hot are you', 'system status'. " +
                 "Report only the parts relevant to the question, or all of it if the user asks for the general status.")]
    public async Task<string> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetSystemStatusAsync)} tool invoked.");

        var parts = new[]
        {
            await GetWifiStatusAsync(cancellationToken),
            await GetMemoryStatusAsync(cancellationToken),
            await GetCpuTemperatureAsync(cancellationToken),
            await GetUptimeAsync(cancellationToken),
            await GetDiskStatusAsync(cancellationToken)
        };

        return string.Join(Environment.NewLine, parts);
    }

    private async Task<string> GetWifiStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetWifiStatusAsync)} tool invoked.");

        try
        {
            var line = (await File.ReadAllLinesAsync(WirelessFile, cancellationToken))
                .Skip(2)
                .FirstOrDefault(l => l.Contains(':'));
            if (line == null)
                return "No wireless interface found.";

            var iface = line[..line.IndexOf(':')].Trim();
            // Columns after the colon: status, link quality, signal level, noise level.
            var cols = line[(line.IndexOf(':') + 1)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => double.TryParse(c.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
                .ToArray();
            if (cols.Length < 3 || cols[2] == null)
                return $"Wireless interface {iface} found but the signal level is unavailable.";

            var level = cols[2]!.Value;
            var ssid = await GetSsidAsync(cancellationToken);
            var network = ssid != null ? $", network {ssid}" : "";

            // Negative values are dBm; some drivers report a positive raw level that cannot be rated.
            if (level >= 0)
                return $"Wi-Fi interface {iface}{network}, link quality {cols[1]:0} out of 70.";

            var rating = level switch
            {
                >= -50 => "excellent",
                >= -60 => "good",
                >= -70 => "fair",
                _ => "weak"
            };
            return $"Wi-Fi signal is {rating} ({level:0} dBm){network}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetWifiStatusAsync));
            return "Wi-Fi status is not available on this system.";
        }
    }

    private async Task<string> GetMemoryStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetMemoryStatusAsync)} tool invoked.");

        try
        {
            var values = (await File.ReadAllLinesAsync(MemInfoFile, cancellationToken))
                .Select(l => l.Split(':', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => p[0].Trim(),
                    p => long.TryParse(p[1].Replace("kB", "").Trim(), out var kb) ? kb : 0L);

            var totalKb = values.GetValueOrDefault("MemTotal");
            var availableKb = values.GetValueOrDefault("MemAvailable");
            if (totalKb <= 0)
                return "Memory information is not available.";

            var usedKb = totalKb - availableKb;
            var result = $"Memory: {usedKb / 1024} MB used of {totalKb / 1024} MB ({100.0 * usedKb / totalKb:0}%), {availableKb / 1024} MB available.";

            var swapTotalKb = values.GetValueOrDefault("SwapTotal");
            if (swapTotalKb > 0)
            {
                var swapUsedKb = swapTotalKb - values.GetValueOrDefault("SwapFree");
                result += $" Swap: {swapUsedKb / 1024} MB used of {swapTotalKb / 1024} MB.";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetMemoryStatusAsync));
            return "Memory status is not available on this system.";
        }
    }

    private async Task<string> GetCpuTemperatureAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetCpuTemperatureAsync)} tool invoked.");

        try
        {
            var text = (await File.ReadAllTextAsync(ThermalFile, cancellationToken)).Trim();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliCelsius))
                return "CPU temperature is not available.";

            return $"CPU temperature is {milliCelsius / 1000.0:0.0} degrees Celsius.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetCpuTemperatureAsync));
            return "CPU temperature is not available on this system.";
        }
    }

    private async Task<string> GetUptimeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetUptimeAsync)} tool invoked.");

        try
        {
            var first = (await File.ReadAllTextAsync(UptimeFile, cancellationToken))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var uptime = TimeSpan.FromSeconds(double.Parse(first, CultureInfo.InvariantCulture));

            return $"The system has been running for {uptime.Days} days, {uptime.Hours} hours and {uptime.Minutes} minutes.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetUptimeAsync));
            return "Uptime is not available on this system.";
        }
    }

    private Task<string> GetDiskStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetDiskStatusAsync)} tool invoked.");

        try
        {
            var drive = new DriveInfo("/");
            const double gb = 1024.0 * 1024 * 1024;
            var used = drive.TotalSize - drive.AvailableFreeSpace;

            return Task.FromResult(
                $"Disk: {used / gb:0.0} GB used of {drive.TotalSize / gb:0.0} GB ({100.0 * used / drive.TotalSize:0}%), {drive.AvailableFreeSpace / gb:0.0} GB free.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetDiskStatusAsync));
            return Task.FromResult("Disk status is not available on this system.");
        }
    }

    // Best effort: iwgetid is optional, so any failure just means the network name is left out.
    private async Task<string> GetSsidAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "iwgetid",
                Arguments = "-r",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process == null)
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var output = (await process.StandardOutput.ReadToEndAsync(cts.Token)).Trim();
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SSID lookup via iwgetid failed.");
            return null;
        }
    }
}
