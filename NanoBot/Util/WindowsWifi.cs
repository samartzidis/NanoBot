using System.Runtime.InteropServices;
using System.Text;

namespace NanoBot.Util;

/// <summary>
/// Wi-Fi connection details on Windows via the Native Wifi API (wlanapi.dll).
/// </summary>
internal static class WindowsWifi
{
    public sealed record Reading(string Interface, string Ssid, double? SignalDbm);

    private const uint ClientVersion = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorServiceNotActive = 1062;

    // WLAN_INTF_OPCODE values.
    private const int OpcodeCurrentConnection = 7;
    private const int OpcodeRssi = 0x10000102;

    // WLAN_INTERFACE_STATE.wlan_interface_state_connected
    private const int InterfaceStateConnected = 1;

    // WLAN_INTERFACE_INFO_LIST: DWORD dwNumberOfItems, DWORD dwIndex, then the WLAN_INTERFACE_INFO array.
    private const int InterfaceListHeaderSize = 8;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    // WLAN_ASSOCIATION_ATTRIBUTES
    [StructLayout(LayoutKind.Sequential)]
    private struct AssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;
        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    // WLAN_CONNECTION_ATTRIBUTES, without the trailing security attributes that are not read here.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionAttributes
    {
        public int State;
        public int Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;
        public AssociationAttributes Association;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
        out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    /// <summary>
    /// Returns the first connected wireless interface, otherwise the first interface with no signal, or null when
    /// the system has no wireless interface or no WLAN service.
    /// </summary>
    public static Reading Get()
    {
        try
        {
            var result = WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var client);
            if (result == ErrorServiceNotActive)
                return null;
            ThrowIfFailed(result);

            try
            {
                return ReadInterfaces(client);
            }
            finally
            {
                WlanCloseHandle(client, IntPtr.Zero);
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    private static Reading ReadInterfaces(IntPtr client)
    {
        ThrowIfFailed(WlanEnumInterfaces(client, IntPtr.Zero, out var list));
        try
        {
            var count = Marshal.ReadInt32(list);
            var size = Marshal.SizeOf<InterfaceInfo>();
            string firstDescription = null;

            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<InterfaceInfo>(list + InterfaceListHeaderSize + i * size);
                firstDescription ??= info.Description;

                if (info.State == InterfaceStateConnected)
                    return ReadConnection(client, info);
            }

            return firstDescription != null ? new Reading(firstDescription, null, null) : null;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private static Reading ReadConnection(IntPtr client, InterfaceInfo info)
    {
        var guid = info.InterfaceGuid;

        if (WlanQueryInterface(client, ref guid, OpcodeCurrentConnection, IntPtr.Zero, out _, out var data, IntPtr.Zero) != ErrorSuccess)
            return new Reading(info.Description, null, null);

        string ssid;
        uint quality;
        try
        {
            var association = Marshal.PtrToStructure<ConnectionAttributes>(data).Association;
            ssid = Encoding.UTF8.GetString(association.Ssid.Bytes, 0, (int)Math.Min(association.Ssid.Length, 32));
            quality = association.SignalQuality;
        }
        finally
        {
            WlanFreeMemory(data);
        }

        return new Reading(info.Description, ssid, ReadRssi(client, guid) ?? QualityToDbm(quality));
    }

    private static double? ReadRssi(IntPtr client, Guid guid)
    {
        if (WlanQueryInterface(client, ref guid, OpcodeRssi, IntPtr.Zero, out var dataSize, out var data, IntPtr.Zero) != ErrorSuccess)
            return null;

        try
        {
            return dataSize >= sizeof(int) ? Marshal.ReadInt32(data) : null;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    // Documented mapping of WLAN_SIGNAL_QUALITY (0-100) to dBm: 0 is -100 dBm and 100 is -50 dBm.
    private static double QualityToDbm(uint quality) => Math.Min(quality, 100) / 2.0 - 100;

    private static void ThrowIfFailed(uint result)
    {
        if (result != ErrorSuccess)
            throw new System.ComponentModel.Win32Exception((int)result);
    }
}
