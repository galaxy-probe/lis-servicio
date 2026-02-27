using System.Net.NetworkInformation;

namespace LisServicioSecure;

public static class NetworkInfo {
    public static string[] GetMacAddresses()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.GetPhysicalAddress()?.ToString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();


    public static string GetActiveMacAddress() {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                n.GetPhysicalAddress() != null &&
                n.GetPhysicalAddress().GetAddressBytes().Length == 6)
            // Prioridad: Ethernet > WiFi > resto
            .OrderBy(n =>
                n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 :
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 2)
            .FirstOrDefault();

        return nic?.GetPhysicalAddress().ToString() ?? "";
    }
}