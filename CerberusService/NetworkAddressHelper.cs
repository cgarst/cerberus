using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RoomAgent
{
    public sealed class WebAccessAddresses
    {
        public string PrimaryDnsUrl { get; set; }
        public string DnsSummary { get; set; }
        public string PrimaryIpUrl { get; set; }
        public string IpSummary { get; set; }
    }

    public sealed class WakeOnLanNetworkInfo
    {
        public string MacAddress { get; set; }
        public string BroadcastIp { get; set; }
    }

    public static class NetworkAddressHelper
    {
        public static WebAccessAddresses GetWebAccessAddresses(int port)
        {
            string hostName = "";
            string domainName = "";
            try
            {
                hostName = Dns.GetHostName() ?? "";
            }
            catch { }

            try
            {
                domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName ?? "";
            }
            catch { }

            List<string> ips = GetLocalIPv4Addresses();
            return BuildWebAccessAddresses(port, hostName, domainName, ips);
        }

        public static WebAccessAddresses BuildWebAccessAddresses(int port, string hostName, string domainName, IEnumerable<string> ipv4Addresses)
        {
            int effectivePort = (port >= 1 && port <= 65535) ? port : 8090;

            List<string> dnsUrls = new List<string>();
            string cleanHost = (hostName ?? "").Trim();
            string cleanDomain = (domainName ?? "").Trim().Trim('.');

            if (string.IsNullOrEmpty(cleanHost))
            {
                dnsUrls.Add(FormatHttpUrl("localhost", effectivePort));
            }
            else
            {
                dnsUrls.Add(FormatHttpUrl(cleanHost, effectivePort));
                if (!string.IsNullOrEmpty(cleanDomain) &&
                    !cleanHost.EndsWith("." + cleanDomain, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(cleanHost, cleanDomain, StringComparison.OrdinalIgnoreCase))
                {
                    dnsUrls.Add(FormatHttpUrl(cleanHost + "." + cleanDomain, effectivePort));
                }
            }

            List<string> ipUrls = new List<string>();
            HashSet<string> seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ipv4Addresses != null)
            {
                foreach (string rawIp in ipv4Addresses)
                {
                    string ip = (rawIp ?? "").Trim();
                    if (!string.IsNullOrEmpty(ip) && seenIps.Add(ip))
                    {
                        ipUrls.Add(FormatHttpUrl(ip, effectivePort));
                    }
                }
            }

            if (ipUrls.Count == 0)
            {
                ipUrls.Add(FormatHttpUrl("127.0.0.1", effectivePort));
            }

            return new WebAccessAddresses
            {
                PrimaryDnsUrl = dnsUrls[0],
                DnsSummary = string.Join(", ", dnsUrls),
                PrimaryIpUrl = ipUrls[0],
                IpSummary = string.Join(", ", ipUrls)
            };
        }

        private static string FormatHttpUrl(string host, int port)
        {
            return port == 80
                ? string.Format("http://{0}", host)
                : string.Format("http://{0}:{1}", host, port);
        }

        private static List<string> GetLocalIPv4Addresses()
        {
            List<string> withGateway = new List<string>();
            List<string> withoutGateway = new List<string>();

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface ni = interfaces[i];
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    IPInterfaceProperties ipProps;
                    try
                    {
                        ipProps = ni.GetIPProperties();
                    }
                    catch
                    {
                        continue;
                    }

                    bool hasGateway = ipProps.GatewayAddresses != null && ipProps.GatewayAddresses.Count > 0;
                    foreach (UnicastIPAddressInformation unicast in ipProps.UnicastAddresses)
                    {
                        IPAddress addr = unicast.Address;
                        if (addr == null || addr.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(addr)) continue;

                        byte[] bytes = addr.GetAddressBytes();
                        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) continue;

                        string ipStr = addr.ToString();
                        if (hasGateway)
                        {
                            withGateway.Add(ipStr);
                        }
                        else
                        {
                            withoutGateway.Add(ipStr);
                        }
                    }
                }
            }
            catch { }

            withGateway.AddRange(withoutGateway);
            return withGateway;
        }

        public static WakeOnLanNetworkInfo GetWakeOnLanInfo()
        {
            WakeOnLanNetworkInfo fallback = null;

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface ni = interfaces[i];
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    string mac = FormatMacAddress(ni.GetPhysicalAddress()?.GetAddressBytes());
                    if (string.IsNullOrEmpty(mac)) continue;

                    IPInterfaceProperties ipProps;
                    try
                    {
                        ipProps = ni.GetIPProperties();
                    }
                    catch
                    {
                        continue;
                    }

                    bool hasGateway = ipProps.GatewayAddresses != null && ipProps.GatewayAddresses.Count > 0;
                    foreach (UnicastIPAddressInformation unicast in ipProps.UnicastAddresses)
                    {
                        IPAddress addr = unicast.Address;
                        if (addr == null || addr.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(addr)) continue;

                        byte[] bytes = addr.GetAddressBytes();
                        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) continue;

                        string broadcast = ComputeBroadcastIp(addr, unicast.IPv4Mask);
                        WakeOnLanNetworkInfo candidate = new WakeOnLanNetworkInfo
                        {
                            MacAddress = mac,
                            BroadcastIp = broadcast
                        };

                        if (hasGateway)
                        {
                            return candidate;
                        }

                        if (fallback == null)
                        {
                            fallback = candidate;
                        }
                    }
                }
            }
            catch { }

            return fallback ?? new WakeOnLanNetworkInfo
            {
                MacAddress = "",
                BroadcastIp = "255.255.255.255"
            };
        }

        public static string FormatMacAddress(byte[] macBytes)
        {
            if (macBytes == null || macBytes.Length != 6)
            {
                return "";
            }

            bool allZero = true;
            for (int i = 0; i < macBytes.Length; i++)
            {
                if (macBytes[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                return "";
            }

            return string.Format(
                "{0:X2}-{1:X2}-{2:X2}-{3:X2}-{4:X2}-{5:X2}",
                macBytes[0], macBytes[1], macBytes[2], macBytes[3], macBytes[4], macBytes[5]);
        }

        public static string ComputeBroadcastIp(IPAddress ipAddress, IPAddress subnetMask)
        {
            if (ipAddress == null || ipAddress.AddressFamily != AddressFamily.InterNetwork ||
                subnetMask == null || subnetMask.AddressFamily != AddressFamily.InterNetwork)
            {
                return "255.255.255.255";
            }

            byte[] ipBytes = ipAddress.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();
            if (ipBytes.Length != 4 || maskBytes.Length != 4)
            {
                return "255.255.255.255";
            }

            byte[] broadcastBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes).ToString();
        }
    }
}
