using System.Collections.Generic;
using System.Linq;

namespace CarRacer.Core.Discovery
{
    public sealed class DiscoveredHost
    {
        public string Address;
        public uint SessionId;
        public DiscoveryAnnouncement Announcement;
        public long LastSeenMs;

        public bool IsCompatible => Announcement.ProtocolVersion == DiscoveryConstants.ProtocolVersion;
        public bool IsFull => Announcement.PlayerCount >= Announcement.MaxPlayers;
        public bool CanJoin => IsCompatible && !IsFull && Announcement.State == HostSessionState.Lobby;

        /// <summary>One word or phrase for the join list: why it can't be joined, or that it's open.</summary>
        public string StatusText =>
            !IsCompatible ? (Announcement.ProtocolVersion > DiscoveryConstants.ProtocolVersion ? "Update this app to join" : "Host needs updating")
            : IsFull ? "Full"
            : Announcement.State == HostSessionState.InEvent ? "Racing"
            : "Open";
    }

    /// <summary>
    /// The guest's list of hosts on the network. The same announcement typically arrives
    /// twice (multicast and broadcast), so hosts are keyed by address and session id.
    /// Hosts disappear after <see cref="DiscoveryConstants.HostExpiryMs"/> of silence.
    /// </summary>
    public sealed class DiscoveredHostList
    {
        private readonly Dictionary<(string, uint), DiscoveredHost> _hosts = new Dictionary<(string, uint), DiscoveredHost>();
        private readonly long _expiryMs;

        public DiscoveredHostList(long expiryMs = DiscoveryConstants.HostExpiryMs)
        {
            _expiryMs = expiryMs;
        }

        /// <summary>Returns true if this announcement added a new host to the list.</summary>
        public bool Observe(DiscoveryAnnouncement announcement, string address, long nowMs)
        {
            var key = (address, announcement.SessionId);
            if (_hosts.TryGetValue(key, out var existing))
            {
                existing.Announcement = announcement;
                existing.LastSeenMs = nowMs;
                return false;
            }

            _hosts[key] = new DiscoveredHost
            {
                Address = address,
                SessionId = announcement.SessionId,
                Announcement = announcement,
                LastSeenMs = nowMs,
            };
            return true;
        }

        /// <summary>Removes hosts not heard from recently. Returns the number removed.</summary>
        public int Expire(long nowMs)
        {
            var stale = _hosts.Where(kv => nowMs - kv.Value.LastSeenMs > _expiryMs).Select(kv => kv.Key).ToList();
            foreach (var key in stale)
            {
                _hosts.Remove(key);
            }

            return stale.Count;
        }

        /// <summary>Hosts in a stable display order: joinable first, then by name.</summary>
        public IReadOnlyList<DiscoveredHost> Hosts => _hosts.Values
            .OrderByDescending(h => h.CanJoin)
            .ThenBy(h => h.Announcement.HostName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Address, System.StringComparer.Ordinal)
            .ToList();
    }
}
