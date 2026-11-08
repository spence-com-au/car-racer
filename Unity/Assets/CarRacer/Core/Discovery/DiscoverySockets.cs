using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CarRacer.Core.Discovery
{
    /// <summary>
    /// Sends a host's announcement to the multicast group and to broadcast addresses
    /// (some Wi-Fi access points drop multicast but pass broadcast). Non-blocking; drive it
    /// from the game loop with <see cref="Tick"/>.
    ///
    /// On Android the caller must hold a WifiManager.MulticastLock while discovery runs.
    /// </summary>
    public sealed class DiscoveryAnnouncer : IDisposable
    {
        private readonly Socket _socket;
        private readonly List<IPEndPoint> _targets;
        private readonly int _intervalMs;
        private long _nextSendMs = long.MinValue;

        public DiscoveryAnnouncer(IEnumerable<IPEndPoint> targets, int intervalMs = DiscoveryConstants.AnnounceIntervalMs)
        {
            _targets = new List<IPEndPoint>(targets);
            _intervalMs = intervalMs;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
                Blocking = false,
            };
            // Keep announcements on the local network segment.
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        }

        /// <summary>Sends the announcement if the interval has elapsed. Returns true if it sent.</summary>
        public bool Tick(DiscoveryAnnouncement announcement, long nowMs)
        {
            if (nowMs < _nextSendMs)
            {
                return false;
            }

            _nextSendMs = nowMs + _intervalMs;
            SendNow(announcement);
            return true;
        }

        /// <summary>Sends immediately, e.g. when the player count changes. Returns how many targets accepted the datagram.</summary>
        public int SendNow(DiscoveryAnnouncement announcement)
        {
            byte[] payload = announcement.Encode();
            int sent = 0;
            foreach (var target in _targets)
            {
                try
                {
                    _socket.SendTo(payload, target);
                    sent++;
                }
                catch (SocketException)
                {
                    // An unreachable target (e.g. no route for broadcast) must not stop the others.
                }
            }

            return sent;
        }

        /// <summary>Multicast group, limited broadcast, and each IPv4 interface's subnet broadcast.</summary>
        public static List<IPEndPoint> DefaultTargets(int port = DiscoveryConstants.DiscoveryPort)
        {
            var targets = new List<IPEndPoint>
            {
                new IPEndPoint(DiscoveryConstants.MulticastGroup, port),
                new IPEndPoint(IPAddress.Broadcast, port),
            };

            foreach (var broadcast in LocalSubnetBroadcastAddresses())
            {
                var endpoint = new IPEndPoint(broadcast, port);
                if (!targets.Contains(endpoint))
                {
                    targets.Add(endpoint);
                }
            }

            return targets;
        }

        /// <summary>
        /// Best effort: interface enumeration is restricted on some Android versions, in which
        /// case this returns nothing and the limited broadcast + multicast targets still apply.
        /// </summary>
        public static IEnumerable<IPAddress> LocalSubnetBroadcastAddresses()
        {
            var result = new List<IPAddress>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                        {
                            continue;
                        }

                        var broadcast = BroadcastAddress.ForSubnet(unicast.Address, unicast.IPv4Mask);
                        if (broadcast != null && !result.Contains(broadcast))
                        {
                            result.Add(broadcast);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // NetworkInformationException, NotImplementedException, etc. on restricted platforms.
            }

            return result;
        }

        public void Dispose() => _socket.Dispose();
    }

    /// <summary>
    /// Receives announcements on the discovery port and feeds them into a
    /// <see cref="DiscoveredHostList"/>. Non-blocking; call <see cref="Poll"/> every frame.
    /// </summary>
    public sealed class DiscoveryListener : IDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _buffer = new byte[512];

        public DiscoveryListener(int port = DiscoveryConstants.DiscoveryPort, bool joinMulticast = true)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // Lets several game instances on one machine (desktop testing) share the port.
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket.Blocking = false;
            Port = ((IPEndPoint)_socket.LocalEndPoint).Port;

            if (joinMulticast)
            {
                try
                {
                    _socket.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.AddMembership,
                        new MulticastOption(DiscoveryConstants.MulticastGroup, IPAddress.Any));
                }
                catch (SocketException)
                {
                    // No multicast-capable interface; broadcast still works.
                }
            }
        }

        /// <summary>The bound port (useful when constructed with port 0 in tests).</summary>
        public int Port { get; }

        /// <summary>Drains pending datagrams into <paramref name="hosts"/>, then expires stale hosts. Returns the number of new hosts.</summary>
        public int Poll(DiscoveredHostList hosts, long nowMs)
        {
            int added = 0;

            // Not `while (Available > 0)`: for UDP that is the size of the next datagram, so an empty one would end the
            // loop without being consumed and block every announcement behind it. Drain until the socket would block.
            while (true)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int received;
                try
                {
                    received = _socket.ReceiveFrom(_buffer, ref remote);
                }
                catch (SocketException)
                {
                    break;
                }

                if (DiscoveryAnnouncement.TryDecode(_buffer, received, out var announcement)
                    && hosts.Observe(announcement, ((IPEndPoint)remote).Address.ToString(), nowMs))
                {
                    added++;
                }
            }

            hosts.Expire(nowMs);
            return added;
        }

        public void Dispose() => _socket.Dispose();
    }

    public static class BroadcastAddress
    {
        /// <summary>The directed broadcast address for an IPv4 address and mask, e.g. 192.168.1.23/24 → 192.168.1.255.</summary>
        public static IPAddress ForSubnet(IPAddress address, IPAddress mask)
        {
            byte[] a = address.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            if (a.Length != 4 || m.Length != 4)
            {
                return null;
            }

            var b = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                b[i] = (byte)(a[i] | ~m[i]);
            }

            return new IPAddress(b);
        }
    }
}
