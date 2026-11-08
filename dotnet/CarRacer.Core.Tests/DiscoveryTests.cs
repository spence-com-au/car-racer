using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using CarRacer.Core.Discovery;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class DiscoveryTests
    {
        private static DiscoveryAnnouncement Sample(uint session = 42, string name = "Josh's Pixel") => new DiscoveryAnnouncement
        {
            SessionId = session,
            HostName = name,
            GamePort = 47778,
            PlayerCount = 2,
            MaxPlayers = 6,
            State = HostSessionState.Lobby,
        };

        [Fact]
        public void AnnouncementRoundTrips()
        {
            byte[] bytes = Sample().Encode();
            Assert.True(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            Assert.Equal(DiscoveryConstants.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(42u, decoded.SessionId);
            Assert.Equal("Josh's Pixel", decoded.HostName);
            Assert.Equal(47778, decoded.GamePort);
            Assert.Equal(2, decoded.PlayerCount);
            Assert.Equal(6, decoded.MaxPlayers);
            Assert.Equal(HostSessionState.Lobby, decoded.State);
        }

        [Fact]
        public void LongestCleanHostNameFitsAnAnnouncement()
        {
            // Three UTF-8 bytes per character, the most a UTF-16 code unit can cost.
            string name = LobbyRoster.CleanName(new string('\u20AC', 100));
            byte[] bytes = Sample(name: name).Encode();
            Assert.True(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            Assert.Equal(name, decoded.HostName);
        }

        [Fact]
        public void ForeignAndTruncatedPacketsAreIgnored()
        {
            Assert.False(DiscoveryAnnouncement.TryDecode(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, 7, out _));
            byte[] bytes = Sample().Encode();
            Assert.False(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length - 1, out _));
            Assert.False(DiscoveryAnnouncement.TryDecode(bytes, 3, out _));
        }

        [Fact]
        public void OtherProtocolVersionsDecodeAsIncompatible()
        {
            var future = Sample();
            future.ProtocolVersion = DiscoveryConstants.ProtocolVersion + 1;
            byte[] bytes = future.Encode();

            Assert.True(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            var list = new DiscoveredHostList();
            list.Observe(decoded, "10.0.0.5", 0);
            var host = Assert.Single(list.Hosts);
            Assert.False(host.IsCompatible);
            Assert.False(host.CanJoin);
        }

        [Fact]
        public void OtherProtocolVersionsKeepTheirSessionId()
        {
            var future = Sample(session: 7);
            future.ProtocolVersion = DiscoveryConstants.ProtocolVersion + 1;
            byte[] bytes = future.Encode();

            Assert.True(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            Assert.Equal(7u, decoded.SessionId);
        }

        [Fact]
        public void VersionOnlyPacketFromAnotherProtocolDecodesWithoutASessionId()
        {
            byte[] bytes = Sample().Encode();
            var future = (ushort)(DiscoveryConstants.ProtocolVersion + 1);
            bytes[4] = (byte)(future & 0xFF);
            bytes[5] = (byte)(future >> 8);

            Assert.True(DiscoveryAnnouncement.TryDecode(bytes, 6, out var decoded));
            Assert.Equal(future, decoded.ProtocolVersion);
            Assert.Equal(0u, decoded.SessionId);
        }

        [Fact]
        public void DuplicateAnnouncementsFromMulticastAndBroadcastMerge()
        {
            var list = new DiscoveredHostList();
            Assert.True(list.Observe(Sample(), "192.168.1.20", 0));
            Assert.False(list.Observe(Sample(), "192.168.1.20", 5));
            Assert.Single(list.Hosts);
        }

        [Fact]
        public void RepeatAnnouncementsRefreshTheHost()
        {
            var list = new DiscoveredHostList(expiryMs: 3000);
            list.Observe(Sample(), "192.168.1.20", 0);

            var update = Sample();
            update.PlayerCount = 5;
            Assert.False(list.Observe(update, "192.168.1.20", 2500));

            Assert.Equal(0, list.Expire(5000));
            var host = Assert.Single(list.Hosts);
            Assert.Equal(2500, host.LastSeenMs);
            Assert.Equal(5, host.Announcement.PlayerCount);
            Assert.Equal(1, list.Expire(5501));
        }

        [Fact]
        public void HostsExpireAfterSilence()
        {
            var list = new DiscoveredHostList(expiryMs: 3000);
            list.Observe(Sample(1, "A"), "192.168.1.20", 0);
            list.Observe(Sample(2, "B"), "192.168.1.21", 2000);

            Assert.Equal(0, list.Expire(3000));
            Assert.Equal(1, list.Expire(3001));
            Assert.Equal("B", Assert.Single(list.Hosts).Announcement.HostName);
        }

        [Fact]
        public void NewSessionFromSameAddressIsADifferentHost()
        {
            var list = new DiscoveredHostList();
            list.Observe(Sample(1), "192.168.1.20", 0);
            list.Observe(Sample(2), "192.168.1.20", 0);
            Assert.Equal(2, list.Hosts.Count);
        }

        [Fact]
        public void JoinableHostsSortFirst()
        {
            var list = new DiscoveredHostList();
            var full = Sample(1, "Aaron");
            full.PlayerCount = 6;
            var racing = Sample(2, "Bea");
            racing.State = HostSessionState.InEvent;
            list.Observe(full, "10.0.0.1", 0);
            list.Observe(racing, "10.0.0.2", 0);
            list.Observe(Sample(3, "Zed"), "10.0.0.3", 0);

            Assert.Equal(new[] { "Zed", "Aaron", "Bea" }, list.Hosts.Select(h => h.Announcement.HostName));
        }

        [Theory]
        [InlineData("192.168.1.23", "255.255.255.0", "192.168.1.255")]
        [InlineData("10.1.2.3", "255.0.0.0", "10.255.255.255")]
        [InlineData("172.20.10.2", "255.255.255.240", "172.20.10.15")]
        public void SubnetBroadcastAddress(string address, string mask, string expected)
        {
            Assert.Equal(IPAddress.Parse(expected), BroadcastAddress.ForSubnet(IPAddress.Parse(address), IPAddress.Parse(mask)));
        }

        [Fact]
        public void DefaultTargetsIncludeMulticastAndBroadcast()
        {
            var targets = DiscoveryAnnouncer.DefaultTargets();
            Assert.Contains(new IPEndPoint(DiscoveryConstants.MulticastGroup, DiscoveryConstants.DiscoveryPort), targets);
            Assert.Contains(new IPEndPoint(IPAddress.Broadcast, DiscoveryConstants.DiscoveryPort), targets);
        }

        [Fact]
        public void AnnouncerTicksAtTheConfiguredInterval()
        {
            using var announcer = new DiscoveryAnnouncer(new[] { new IPEndPoint(IPAddress.Loopback, 9) }, intervalMs: 1000);
            Assert.True(announcer.Tick(Sample(), 0));
            Assert.False(announcer.Tick(Sample(), 999));
            Assert.True(announcer.Tick(Sample(), 1000));
        }

        [Fact]
        public void ListenerReceivesAnnouncementOverLoopback()
        {
            using var listener = new DiscoveryListener(port: 0, joinMulticast: false);
            using var announcer = new DiscoveryAnnouncer(new[] { new IPEndPoint(IPAddress.Loopback, listener.Port) });
            var hosts = new DiscoveredHostList();

            announcer.SendNow(Sample());

            var clock = Stopwatch.StartNew();
            while (hosts.Hosts.Count == 0 && clock.ElapsedMilliseconds < 2000)
            {
                listener.Poll(hosts, 0);
                Thread.Sleep(10);
            }

            var host = Assert.Single(hosts.Hosts);
            Assert.Equal("127.0.0.1", host.Address);
            Assert.True(host.CanJoin);
        }

        [Fact]
        public void ListenerSkipsAnEmptyDatagram()
        {
            using var listener = new DiscoveryListener(port: 0, joinMulticast: false);
            using var announcer = new DiscoveryAnnouncer(new[] { new IPEndPoint(IPAddress.Loopback, listener.Port) });
            using var probe = new System.Net.Sockets.UdpClient();
            var hosts = new DiscoveredHostList();

            probe.Send(new byte[0], 0, new IPEndPoint(IPAddress.Loopback, listener.Port));
            announcer.SendNow(Sample());

            var clock = Stopwatch.StartNew();
            while (hosts.Hosts.Count == 0 && clock.ElapsedMilliseconds < 2000)
            {
                listener.Poll(hosts, 0);
                Thread.Sleep(10);
            }

            Assert.Single(hosts.Hosts);
        }
    }
}

namespace CarRacer.Core.Tests
{
    public class HostStatusTests
    {
        private static DiscoveredHost Host(ushort version = DiscoveryConstants.ProtocolVersion, byte players = 1, HostSessionState state = HostSessionState.Lobby) => new DiscoveredHost
        {
            Announcement = new DiscoveryAnnouncement { ProtocolVersion = version, PlayerCount = players, MaxPlayers = 6, State = state },
        };

        [Fact]
        public void StatusExplainsWhyAGameCantBeJoined()
        {
            Assert.Equal("Open", Host().StatusText);
            Assert.Equal("Full", Host(players: 6).StatusText);
            Assert.Equal("Racing", Host(state: HostSessionState.InEvent).StatusText);
            Assert.Equal("Update this app to join", Host(version: DiscoveryConstants.ProtocolVersion + 1).StatusText);
            Assert.Equal("Host needs updating", Host(version: DiscoveryConstants.ProtocolVersion - 1).StatusText);
        }

        [Fact]
        public void VersionTrumpsTheOtherReasons()
        {
            Assert.Equal("Update this app to join", Host(version: DiscoveryConstants.ProtocolVersion + 1, players: 6, state: HostSessionState.InEvent).StatusText);
        }
    }
}
