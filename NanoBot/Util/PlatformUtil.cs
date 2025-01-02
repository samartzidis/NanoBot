using System.Runtime.InteropServices;

namespace NanoBot.Util;

public static class PlatformUtil
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string RaspberryPi = "raspberry-pi";

    public static bool IsRaspberryPi()
    {
        try
        {
            if (!IsLinuxPlatform())
                return false;

            // Check for the presence of specific files that are common on Raspberry Pi
            return File.Exists("/proc/cpuinfo") && File.ReadAllText("/proc/cpuinfo").Contains("raspberry", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLinuxPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsWindowsPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string GetPlatformString()
    {
        if (IsWindowsPlatform())
            return Windows;

        if (IsRaspberryPi())
            return RaspberryPi;

        if (IsLinuxPlatform())
            return Linux;

        return null;
    }
}