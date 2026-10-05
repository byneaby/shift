using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ShiftClub.Infrastructure.Networking;

/// <summary>Sends Wake-on-LAN magic packets (CCBoot-style remote power-on).</summary>
public static class WakeOnLanSender
{
    public static byte[] ParseMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            throw new InvalidOperationException("Нет MAC-адреса.");

        var cleaned = Regex.Replace(mac.Trim(), @"[^0-9A-Fa-f]", "");
        if (cleaned.Length != 12)
            throw new InvalidOperationException($"Некорректный MAC-адрес: {mac}");

        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
            bytes[i] = byte.Parse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    public static byte[] BuildMagicPacket(byte[] mac)
    {
        if (mac.Length != 6)
            throw new ArgumentException("MAC must be 6 bytes.", nameof(mac));

        var packet = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    /// <summary>
    /// UDP broadcast + L2 EtherType 0x0842 via Npcap (physical NICs — bypass LBFO MERGED).
    /// </summary>
    public static async Task SendAsync(string macAddress, string? lastIpAddress, CancellationToken cancellationToken = default)
    {
        var mac = ParseMac(macAddress);
        var packet = BuildMagicPacket(mac);
        var endpoints = BuildEndpoints(lastIpAddress);
        var localBinds = ResolveLanBindAddresses();

        for (var round = 0; round < 3; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var local in localBinds)
                await SendFromLocalAsync(local, packet, endpoints, cancellationToken);

            SendLayer2(mac, packet);

            if (round < 2)
                await Task.Delay(120, cancellationToken);
        }
    }

    private static void SendLayer2(byte[] targetMac, byte[] magicPacket)
    {
        try
        {
            foreach (var device in CaptureDeviceList.Instance)
            {
                try
                {
                    var desc = device.Description ?? "";
                    if (desc.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (device is not LibPcapLiveDevice live)
                        continue;

                    live.Open(new DeviceConfiguration
                    {
                        Mode = DeviceModes.Promiscuous,
                        ReadTimeout = 1,
                    });
                    try
                    {
                        var src = live.MacAddress?.GetAddressBytes() is { Length: 6 } m
                            ? m
                            : [0x00, 0x10, 0x9B, 0x8B, 0xF2, 0xD0];

                        live.SendPacket(BuildEthernetWolFrame(true, src, targetMac, magicPacket));
                        live.SendPacket(BuildEthernetWolFrame(false, src, targetMac, magicPacket));
                    }
                    finally
                    {
                        live.Close();
                    }
                }
                catch
                {
                    try { device.Close(); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // Npcap missing / access denied — UDP path still attempted.
        }
    }

    private static byte[] BuildEthernetWolFrame(bool broadcastDest, byte[] srcMac, byte[] targetMac, byte[] magicPacket)
    {
        var frame = new byte[14 + magicPacket.Length];
        if (broadcastDest)
        {
            for (var i = 0; i < 6; i++)
                frame[i] = 0xFF;
        }
        else
            Buffer.BlockCopy(targetMac, 0, frame, 0, 6);

        Buffer.BlockCopy(srcMac, 0, frame, 6, 6);
        frame[12] = 0x08;
        frame[13] = 0x42;
        Buffer.BlockCopy(magicPacket, 0, frame, 14, magicPacket.Length);
        return frame;
    }

    private static List<IPEndPoint> BuildEndpoints(string? lastIpAddress)
    {
        var endpoints = new List<IPEndPoint>
        {
            new(IPAddress.Broadcast, 9),
            new(IPAddress.Broadcast, 7),
        };

        if (TrySubnetBroadcast(lastIpAddress, out var subnet))
        {
            endpoints.Add(new IPEndPoint(subnet, 9));
            endpoints.Add(new IPEndPoint(subnet, 7));
        }

        if (!string.IsNullOrWhiteSpace(lastIpAddress)
            && IPAddress.TryParse(lastIpAddress.Trim(), out var ip)
            && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            endpoints.Add(new IPEndPoint(ip, 9));
            endpoints.Add(new IPEndPoint(ip, 7));
        }

        return endpoints.DistinctBy(e => (e.Address.ToString(), e.Port)).ToList();
    }

    private static async Task SendFromLocalAsync(
        IPAddress? localBind,
        byte[] packet,
        IReadOnlyList<IPEndPoint> endpoints,
        CancellationToken cancellationToken)
    {
        try
        {
            using var udp = localBind is null
                ? new UdpClient()
                : new UdpClient(new IPEndPoint(localBind, 0));
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            foreach (var ep in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await udp.SendAsync(packet, packet.Length, ep); }
                catch { /* best-effort */ }
            }
        }
        catch
        {
            // Bind may fail on APIPA / down NIC.
        }
    }

    private static List<IPAddress?> ResolveLanBindAddresses()
    {
        var result = new List<IPAddress?> { null };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254)
                        continue;
                    if (IPAddress.IsLoopback(ua.Address))
                        continue;
                    if (!result.Any(x => x is not null && x.Equals(ua.Address)))
                        result.Add(ua.Address);
                }
            }
        }
        catch
        {
            // Fall back to unbound send only.
        }

        return result;
    }

    private static bool TrySubnetBroadcast(string? ipRaw, out IPAddress broadcast)
    {
        broadcast = IPAddress.None;
        if (string.IsNullOrWhiteSpace(ipRaw))
            return false;
        if (!IPAddress.TryParse(ipRaw.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = ip.GetAddressBytes();
        bytes[3] = 255;
        broadcast = new IPAddress(bytes);
        return true;
    }
}
