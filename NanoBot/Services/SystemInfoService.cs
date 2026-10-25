using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NanoBot.Util;

namespace NanoBot.Services;

public sealed record WifiStatus(string Interface, string Ssid, double? SignalDbm, double? LinkQuality, string Rating);

public sealed record MemoryStatus(long UsedMb, long TotalMb, long AvailableMb, long SwapUsedMb, long SwapTotalMb);

public sealed record DiskStatus(double UsedGb, double TotalGb, double FreeGb);

/// <summary>
/// Snapshot of the device state. A null member means the value is not available on this system.
/// </summary>
public sealed record SystemStatus(
    bool Supported,
    WifiStatus Wifi,
    MemoryStatus Memory,
    double? CpuTemperatureCelsius,
    double? UptimeSeconds,
    DiskStatus Disk);

public interface ISystemInfoService
{
    Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only system diagnostics (Wi-Fi, memory, uptime, disk, and CPU temperature on a Raspberry Pi only). Linux reads
/// /proc and /sys; Windows uses the Native Wifi API and GlobalMemoryStatusEx.
/// </summary>
internal sealed class SystemInfoService : ISystemInfoService
{
    private const string WirelessFile = "/proc/net/wireless";
    private const string MemInfoFile = "/proc/meminfo";
    private const string UptimeFile = "/proc/uptime";
    private const string ThermalFile = "/sys/class/thermal/thermal_zone0/temp";

    private static readonly bool IsLinux = PlatformUtil.IsLinuxPlatform();
    private static readonly bool IsWindows = PlatformUtil.IsWindowsPlatform();
    private static readonly bool IsRaspberryPi = PlatformUtil.IsRaspberryPi();

    private readonly ILogger _logger;

    public SystemInfoService(ILogger<SystemInfoService> logger)
    {
        _logger = logger;
    }

    public async Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLinux && !IsWindows)
            return new SystemStatus(false, null, null, null, null, null);

        return new SystemStatus(
            true,
            await GetWifiAsync(cancellationToken),
            await GetMemoryAsync(cancellationToken),
            // The thermal zone layout is only known to be right on the Raspberry Pi.
            IsRaspberryPi ? await GetCpuTemperatureAsync(cancellationToken) : null,
            await GetUptimeSecondsAsync(cancellationToken),
            GetDisk());
    }

    private async Task<WifiStatus> GetWifiAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (IsWindows)
            {
                var reading = await Task.Run(WindowsWifi.Get, cancellationToken);
                if (reading == null)
                    return null;

                // Windows has no 0-70 link quality scale, so only the dBm figure is reported.
                return new WifiStatus(reading.Interface, reading.Ssid, reading.SignalDbm, null,
                    reading.SignalDbm != null ? RateSignal(reading.SignalDbm.Value) : null);
            }

            var line = (await File.ReadAllLinesAsync(WirelessFile, cancellationToken))
                .Skip(2)
                .FirstOrDefault(l => l.Contains(':'));
            if (line == null)
                return null;

            var iface = line[..line.IndexOf(':')].Trim();
            // Columns after the colon: status, link quality, signal level, noise level.
            var cols = line[(line.IndexOf(':') + 1)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => double.TryParse(c.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
                .ToArray();
            if (cols.Length < 3 || cols[2] == null)
                return new WifiStatus(iface, null, null, null, null);

            var level = cols[2]!.Value;
            var ssid = await GetSsidAsync(cancellationToken);

            // Negative values are dBm; some drivers report a positive raw level that cannot be rated.
            if (level >= 0)
                return new WifiStatus(iface, ssid, null, cols[1], null);

            return new WifiStatus(iface, ssid, level, cols[1], RateSignal(level));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetWifiAsync));
            return null;
        }
    }

    private static string RateSignal(double dbm) => dbm switch
    {
        >= -50 => "excellent",
        >= -60 => "good",
        >= -70 => "fair",
        _ => "weak"
    };

    private async Task<MemoryStatus> GetMemoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (IsWindows)
            {
                // The page file is not reported: commit charge would be misleading as "swap".
                var (total, available) = WindowsMemory.GetPhysicalMemory();
                const long mb = 1024 * 1024;
                return new MemoryStatus((long)(total - available) / mb, (long)total / mb, (long)available / mb, 0, 0);
            }

            var values = (await File.ReadAllLinesAsync(MemInfoFile, cancellationToken))
                .Select(l => l.Split(':', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => p[0].Trim(),
                    p => long.TryParse(p[1].Replace("kB", "").Trim(), out var kb) ? kb : 0L);

            var totalKb = values.GetValueOrDefault("MemTotal");
            var availableKb = values.GetValueOrDefault("MemAvailable");
            if (totalKb <= 0)
                return null;

            var swapTotalKb = values.GetValueOrDefault("SwapTotal");
            var swapUsedKb = swapTotalKb > 0 ? swapTotalKb - values.GetValueOrDefault("SwapFree") : 0;

            return new MemoryStatus(
                (totalKb - availableKb) / 1024,
                totalKb / 1024,
                availableKb / 1024,
                swapUsedKb / 1024,
                swapTotalKb / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetMemoryAsync));
            return null;
        }
    }

    private async Task<double?> GetCpuTemperatureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = (await File.ReadAllTextAsync(ThermalFile, cancellationToken)).Trim();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliCelsius))
                return null;

            return milliCelsius / 1000.0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetCpuTemperatureAsync));
            return null;
        }
    }

    private async Task<double?> GetUptimeSecondsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (IsWindows)
                return Environment.TickCount64 / 1000.0;

            var first = (await File.ReadAllTextAsync(UptimeFile, cancellationToken))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return double.Parse(first, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetUptimeSecondsAsync));
            return null;
        }
    }

    private DiskStatus GetDisk()
    {
        try
        {
            var drive = new DriveInfo(IsWindows ? Path.GetPathRoot(Environment.SystemDirectory)! : "/");
            const double gb = 1024.0 * 1024 * 1024;
            var used = drive.TotalSize - drive.AvailableFreeSpace;

            return new DiskStatus(used / gb, drive.TotalSize / gb, drive.AvailableFreeSpace / gb);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} failed.", nameof(GetDisk));
            return null;
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
