using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NanoBot.Util;

namespace NanoBot.Services;

public sealed record WifiStatus(string Interface, string Ssid, double? SignalDbm, double? LinkQuality, string Rating);

public sealed record MemoryStatus(long UsedMb, long TotalMb, long AvailableMb, long SwapUsedMb, long SwapTotalMb);

public sealed record DiskStatus(double UsedGb, double TotalGb, double FreeGb);

/// <summary>One thermal zone reading; Name is the kernel's zone type, e.g. "cpu-thermal".</summary>
public sealed record ThermalReading(string Name, double Celsius);

/// <summary>
/// Snapshot of the device state. A null member means the value is not available on this system.
/// Temperatures lists every thermal zone, the CPU one first.
/// </summary>
public sealed record SystemStatus(
    bool Supported,
    WifiStatus Wifi,
    MemoryStatus Memory,
    double? CpuTemperatureCelsius,
    IReadOnlyList<ThermalReading> Temperatures,
    double? UptimeSeconds,
    DiskStatus Disk);

public interface ISystemInfoService
{
    Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only system diagnostics (Wi-Fi, memory, uptime, disk, and thermal zones on the supported boards only). Linux reads
/// /proc and /sys; Windows uses the Native Wifi API and GlobalMemoryStatusEx.
/// </summary>
internal sealed class SystemInfoService : ISystemInfoService
{
    private const string WirelessFile = "/proc/net/wireless";
    private const string MemInfoFile = "/proc/meminfo";
    private const string UptimeFile = "/proc/uptime";
    private const string ThermalDir = "/sys/class/thermal";
    private const string ThermalZonePrefix = "thermal_zone";

    private static readonly bool IsLinux = PlatformUtil.IsLinuxPlatform();
    private static readonly bool IsWindows = PlatformUtil.IsWindowsPlatform();
    private static readonly bool IsSupportedBoard = PlatformUtil.IsSupportedBoard();
    private static readonly Lazy<IReadOnlyList<ThermalZone>> ThermalZones = new(FindThermalZones);

    // iw is in /usr/sbin, which is not on the PATH of the systemd service, so the absolute path is tried as well.
    private static readonly string[] IwExecutables = ["iw", "/usr/sbin/iw"];

    private readonly ILogger _logger;

    // Set once iw was not found anywhere, so a missing install does not log on every poll.
    private bool _iwMissing;

    public SystemInfoService(ILogger<SystemInfoService> logger)
    {
        _logger = logger;
    }

    public async Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLinux && !IsWindows)
            return new SystemStatus(false, null, null, null, null, null, null);

        // The thermal zones are only known to be meaningful on the supported boards (Raspberry Pi, Orange Pi).
        (double? Cpu, IReadOnlyList<ThermalReading> All) temperatures =
            IsSupportedBoard ? await GetTemperaturesAsync(cancellationToken) : (null, null);

        return new SystemStatus(
            true,
            await GetWifiAsync(cancellationToken),
            await GetMemoryAsync(cancellationToken),
            temperatures.Cpu,
            temperatures.All,
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
            var ssid = await GetSsidAsync(iface, cancellationToken);

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

    private sealed record ThermalZone(string Name, string TempFile, bool IsCpu);

    // Boards number their thermal zones differently (zone 0 is "cpu-thermal" on the Raspberry Pi but "gpu-thermal" on
    // the Orange Pi Zero 2W). The CPU zone is the first whose type names the CPU, else the first zone. It is listed
    // first so it keeps the same graph colour on every board; the rest follow in kernel order.
    private static IReadOnlyList<ThermalZone> FindThermalZones()
    {
        var zones = new List<ThermalZone>();
        try
        {
            var dirs = Directory.GetDirectories(ThermalDir, ThermalZonePrefix + "*")
                .OrderBy(d => int.TryParse(Path.GetFileName(d)[ThermalZonePrefix.Length..], out var n) ? n : int.MaxValue);

            foreach (var dir in dirs)
            {
                try
                {
                    zones.Add(new ThermalZone(File.ReadAllText(Path.Combine(dir, "type")).Trim(), Path.Combine(dir, "temp"), false));
                }
                catch
                {
                    // A zone without a readable type is skipped.
                }
            }
        }
        catch
        {
            return zones;
        }

        var cpu = zones.FirstOrDefault(z => z.Name.Contains("cpu", StringComparison.OrdinalIgnoreCase)) ?? zones.FirstOrDefault();
        if (cpu == null)
            return zones;

        return [cpu with { IsCpu = true }, .. zones.Where(z => z != cpu)];
    }

    private async Task<(double? Cpu, IReadOnlyList<ThermalReading> All)> GetTemperaturesAsync(CancellationToken cancellationToken)
    {
        double? cpu = null;
        var readings = new List<ThermalReading>();

        foreach (var zone in ThermalZones.Value)
        {
            try
            {
                var text = (await File.ReadAllTextAsync(zone.TempFile, cancellationToken)).Trim();
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliCelsius))
                    continue;

                var celsius = milliCelsius / 1000.0;
                readings.Add(new ThermalReading(zone.Name, celsius));
                if (zone.IsCpu)
                    cpu = celsius;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading thermal zone {Zone} failed.", zone.Name);
            }
        }

        return (cpu, readings);
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

    // Best effort: iw is optional, so any failure just means the network name is left out.
    private async Task<string> GetSsidAsync(string iface, CancellationToken cancellationToken)
    {
        if (_iwMissing)
            return null;

        foreach (var iw in IwExecutables)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = iw,
                    ArgumentList = { "dev", iface, "link" },
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

                return process.ExitCode == 0 ? ParseIwSsid(output) : null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                // ENOENT: not at this location, try the next one.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SSID lookup via iw failed.");
                return null;
            }
        }

        _iwMissing = true;
        _logger.LogDebug("iw not found; SSID lookup disabled (install the iw package to show the network name).");
        return null;
    }

    // "iw dev <iface> link" prints "Not connected." or a block containing a "SSID: <name>" line.
    private static string ParseIwSsid(string output) => output
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("SSID:", StringComparison.Ordinal))
        .Select(l => l["SSID:".Length..].Trim())
        .FirstOrDefault();
}
