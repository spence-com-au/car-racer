using System.Net;
using CarRacer.Core.Net;

namespace CarRacer.Core.Discovery
{
    public static class DiscoveryConstants
    {
        /// <summary>
        /// Bump whenever any network message format changes, or a message's meaning does (e.g. a
        /// new track index an older build doesn't have); mismatched hosts show as "Update required".
        /// </summary>
        public const ushort ProtocolVersion = 26;

        /// <summary>Administratively-scoped (site-local) multicast group used for lobby announcements.</summary>
        public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.42.99");

        public const int DiscoveryPort = 47777;
        public const int DefaultGamePort = 47778;
        public const int AnnounceIntervalMs = 1000;
        public const int HostExpiryMs = 3000;
        public const int MaxHostNameChars = 24;

        internal static readonly byte[] Magic = { (byte)'C', (byte)'R', (byte)'C', (byte)'R' };
    }

    public enum HostSessionState : byte
    {
        Lobby = 0,
        InEvent = 1,
    }

    /// <summary>The packet a host broadcasts once a second while its session exists.</summary>
    public sealed class DiscoveryAnnouncement
    {
        public ushort ProtocolVersion = DiscoveryConstants.ProtocolVersion;

        /// <summary>Random per hosted session; distinguishes two sessions from the same address.</summary>
        public uint SessionId;

        public string HostName = string.Empty;
        public ushort GamePort = DiscoveryConstants.DefaultGamePort;
        public byte PlayerCount;
        public byte MaxPlayers = 6;
        public HostSessionState State;

        public byte[] Encode()
        {
            var w = new NetWriter(48);
            w.WriteBytes(DiscoveryConstants.Magic);
            // Protocol version comes first after the magic so future layouts can still be identified.
            w.WriteUShort(ProtocolVersion);
            w.WriteUInt(SessionId);
            w.WriteString(Truncate(HostName, DiscoveryConstants.MaxHostNameChars));
            w.WriteUShort(GamePort);
            w.WriteByte(PlayerCount);
            w.WriteByte(MaxPlayers);
            w.WriteByte((byte)State);
            return w.ToArray();
        }

        /// <summary>
        /// Decodes a datagram. Returns false for anything that isn't one of our announcements.
        /// An announcement from a different protocol version decodes only its version and
        /// session id (enough to show "Update required").
        /// </summary>
        public static bool TryDecode(byte[] data, int length, out DiscoveryAnnouncement announcement)
        {
            announcement = null;
            if (data == null || length < DiscoveryConstants.Magic.Length + 2)
            {
                return false;
            }

            for (int i = 0; i < DiscoveryConstants.Magic.Length; i++)
            {
                if (data[i] != DiscoveryConstants.Magic[i])
                {
                    return false;
                }
            }

            try
            {
                var r = new NetReader(data, 0, length);
                r.ReadBytes(DiscoveryConstants.Magic.Length);
                var result = new DiscoveryAnnouncement { ProtocolVersion = r.ReadUShort() };
                if (result.ProtocolVersion != DiscoveryConstants.ProtocolVersion)
                {
                    result.SessionId = r.Remaining >= 4 ? r.ReadUInt() : 0;
                    announcement = result;
                    return true;
                }

                result.SessionId = r.ReadUInt();
                result.HostName = r.ReadString();
                result.GamePort = r.ReadUShort();
                result.PlayerCount = r.ReadByte();
                result.MaxPlayers = r.ReadByte();
                result.State = (HostSessionState)r.ReadByte();
                announcement = result;
                return true;
            }
            catch (NetFormatException)
            {
                return false;
            }
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxChars ? value : value.Substring(0, maxChars);
        }
    }
}
