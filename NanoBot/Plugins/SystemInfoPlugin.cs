using System.ComponentModel;
using Microsoft.Extensions.Logging;
using NanoBot.Services;

namespace NanoBot.Plugins;

/// <summary>
/// Read-only system diagnostics (Wi-Fi, memory, CPU temperature, uptime, disk), phrased for speech. Items the
/// platform cannot report are described as unavailable.
/// </summary>
public sealed class SystemInfoPlugin
{
    private readonly ILogger _logger;
    private readonly ISystemInfoService _systemInfoService;

    public SystemInfoPlugin(ILogger<SystemInfoPlugin> logger, ISystemInfoService systemInfoService)
    {
        _logger = logger;
        _systemInfoService = systemInfoService;
    }

    [Description("Get a status report of this device: Wi-Fi signal strength, memory usage, CPU temperature, uptime and free disk space. " +
                 "Example: user asks 'how is the wifi signal', 'how much memory is free', 'how hot are you', 'system status'. " +
                 "Report only the parts relevant to the question, or all of it if the user asks for the general status.")]
    public async Task<string> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"{nameof(GetSystemStatusAsync)} tool invoked.");

        var status = await _systemInfoService.GetStatusAsync(cancellationToken);

        return string.Join(Environment.NewLine,
            DescribeWifi(status.Wifi),
            DescribeMemory(status.Memory),
            DescribeCpuTemperature(status.CpuTemperatureCelsius),
            DescribeUptime(status.UptimeSeconds),
            DescribeDisk(status.Disk));
    }

    private static string DescribeWifi(WifiStatus wifi)
    {
        if (wifi == null)
            return "No wireless interface found.";

        var network = wifi.Ssid != null ? $", network {wifi.Ssid}" : "";

        if (wifi.SignalDbm != null && wifi.Rating != null)
            return $"Wi-Fi signal is {wifi.Rating} ({wifi.SignalDbm:0} dBm){network}.";

        // Some drivers report a positive raw level that cannot be rated.
        if (wifi.LinkQuality != null)
            return $"Wi-Fi interface {wifi.Interface}{network}, link quality {wifi.LinkQuality:0} out of 70.";

        return $"Wireless interface {wifi.Interface} found but the signal level is unavailable.";
    }

    private static string DescribeMemory(MemoryStatus memory)
    {
        if (memory == null)
            return "Memory information is not available.";

        var result = $"Memory: {memory.UsedMb} MB used of {memory.TotalMb} MB ({100.0 * memory.UsedMb / memory.TotalMb:0}%), {memory.AvailableMb} MB available.";
        if (memory.SwapTotalMb > 0)
            result += $" Swap: {memory.SwapUsedMb} MB used of {memory.SwapTotalMb} MB.";

        return result;
    }

    private static string DescribeCpuTemperature(double? celsius) =>
        celsius != null
            ? $"CPU temperature is {celsius:0.0} degrees Celsius."
            : "CPU temperature is not available on this system.";

    private static string DescribeUptime(double? seconds)
    {
        if (seconds == null)
            return "Uptime is not available on this system.";

        var uptime = TimeSpan.FromSeconds(seconds.Value);
        return $"The system has been running for {uptime.Days} days, {uptime.Hours} hours and {uptime.Minutes} minutes.";
    }

    private static string DescribeDisk(DiskStatus disk) =>
        disk != null
            ? $"Disk: {disk.UsedGb:0.0} GB used of {disk.TotalGb:0.0} GB ({100.0 * disk.UsedGb / disk.TotalGb:0}%), {disk.FreeGb:0.0} GB free."
            : "Disk status is not available on this system.";
}
