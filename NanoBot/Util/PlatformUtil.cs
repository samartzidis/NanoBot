using System.Runtime.InteropServices;

namespace NanoBot.Util;

public static class PlatformUtil
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string RaspberryPi = "raspberry-pi";
    public const string OrangePi = "orange-pi";

    private const string DeviceTreeModelFile = "/proc/device-tree/model";

    private static readonly Lazy<string> BoardModel = new(ReadBoardModel);

    public static bool IsRaspberryPi() => BoardModel.Value.Contains("raspberry", StringComparison.OrdinalIgnoreCase);

    public static bool IsOrangePi() => BoardModel.Value.Contains("orangepi", StringComparison.OrdinalIgnoreCase);

    // Boards the Pi-specific features (GPIO LEDs/button, eyes, shutdown, CPU temperature) are written for.
    public static bool IsSupportedBoard() => IsRaspberryPi() || IsOrangePi();

    // The board name, e.g. "Raspberry Pi 4 Model B Rev 1.1" or "OrangePi Zero 2W". Empty when not a Linux board that reports one.
    public static string GetBoardModel() => BoardModel.Value;

    private static string ReadBoardModel()
    {
        try
        {
            if (!IsLinuxPlatform())
                return string.Empty;

            // The device tree is the standard source of the board name (the string is NUL-terminated).
            if (File.Exists(DeviceTreeModelFile))
                return File.ReadAllText(DeviceTreeModelFile).Trim('\0', ' ', '\n', '\r');
        }
        catch
        {
            // Fall through: treat as an unknown board.
        }

        return string.Empty;
    }

    public static bool IsLinuxPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsWindowsPlatform() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string GetPlatformString()
    {
        if (IsWindowsPlatform())
            return Windows;

        if (IsRaspberryPi())
            return RaspberryPi;

        if (IsOrangePi())
            return OrangePi;

        if (IsLinuxPlatform())
            return Linux;

        return null;
    }
}