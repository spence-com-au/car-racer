using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CarRacer.Core.Cast
{
    /// <summary>A Chromecast (or a TV with one built in) found on the network.</summary>
    public sealed class CastDevice
    {
        /// <summary>Its stable id (the TXT record's <c>id</c>), or its mDNS instance name.</summary>
        public string Id = string.Empty;

        /// <summary>What its owner called it ("Living Room TV").</summary>
        public string Name = string.Empty;

        /// <summary>The model ("Chromecast", "Google TV Streamer"…), or empty.</summary>
        public string Model = string.Empty;
        public IPAddress Address = IPAddress.None;
        public int Port = Mdns.CastPort;

        /// <summary>When it last answered, in the caller's clock.</summary>
        public long SeenAtMs;

        public IPEndPoint EndPoint => new IPEndPoint(Address, Port);

        /// <summary>
        /// Whether <paramref name="wanted"/> (from <c>-cast</c>, or a leader's pick) means this
        /// device: its id, its name or its address, ignoring case and spaces at the ends.
        /// </summary>
        public bool Matches(string wanted)
        {
            wanted = (wanted ?? string.Empty).Trim();
            return wanted.Length > 0
                && (string.Equals(wanted, Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(wanted, Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || wanted == Address.ToString());
        }

        public override string ToString() => $"{Name} ({Model}) at {Address}:{Port}";
    }

    /// <summary>
    /// The DNS messages of mDNS discovery (RFC 6762/6763) for <c>_googlecast._tcp.local</c>: the
    /// query, and reading a reply's PTR, SRV, TXT and A records into <see cref="CastDevice"/>s.
    /// </summary>
    public static class Mdns
    {
        public const string CastService = "_googlecast._tcp.local";
        public const int Port = 5353;
        public const int CastPort = 8009;
        public static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");

        private const ushort TypeA = 1;
        private const ushort TypePtr = 12;
        private const ushort TypeTxt = 16;
        private const ushort TypeSrv = 33;

        /// <summary>A PTR query for the Cast service.</summary>
        public static byte[] Query()
        {
            var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (string label in CastService.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(label));
            }

            bytes.AddRange(new byte[] { 0, (byte)(TypePtr >> 8), (byte)TypePtr, 0, 1 });
            return bytes.ToArray();
        }

        /// <summary>
        /// The Cast devices a reply describes. A device whose A record wasn't included is taken to
        /// be at <paramref name="source"/>, the address the reply came from. Malformed replies give none.
        /// </summary>
        public static List<CastDevice> ParseReply(byte[] data, int length, IPAddress source)
        {
            var devices = new List<CastDevice>();
            try
            {
                if (length < 12 || (data[2] & 0x80) == 0)
                {
                    return devices; // Not a response.
                }

                int questions = Read16(data, 4, length);
                int records = Read16(data, 6, length) + Read16(data, 8, length) + Read16(data, 10, length);
                int i = 12;
                for (int q = 0; q < questions; q++)
                {
                    ReadName(data, ref i, length);
                    i += 4;
                }

                var instances = new List<string>();
                var srv = new Dictionary<string, (int Port, string Target)>(StringComparer.OrdinalIgnoreCase);
                var txt = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
                for (int r = 0; r < records; r++)
                {
                    string owner = ReadName(data, ref i, length);
                    int type = Read16(data, i, length);
                    int rdLength = Read16(data, i + 8, length);
                    i += 10;
                    int rdata = i;
                    if (rdata + rdLength > length)
                    {
                        return devices;
                    }

                    if (type == TypePtr && string.Equals(owner, CastService, StringComparison.OrdinalIgnoreCase))
                    {
                        int at = rdata;
                        string instance = ReadName(data, ref at, length);
                        if (!instances.Contains(instance))
                        {
                            instances.Add(instance);
                        }
                    }
                    else if (type == TypeSrv && rdLength >= 7)
                    {
                        int at = rdata + 6;
                        srv[owner] = (Read16(data, rdata + 4, length), ReadName(data, ref at, length));
                    }
                    else if (type == TypeTxt)
                    {
                        txt[owner] = ReadTxt(data, rdata, rdLength);
                    }
                    else if (type == TypeA && rdLength == 4)
                    {
                        addresses[owner] = new IPAddress(new[] { data[rdata], data[rdata + 1], data[rdata + 2], data[rdata + 3] });
                    }

                    i = rdata + rdLength;
                }

                foreach (string instance in instances)
                {
                    var device = new CastDevice { Id = instance, Name = instance.Split('.')[0], Address = source };
                    if (srv.TryGetValue(instance, out var service))
                    {
                        device.Port = service.Port;
                        if (addresses.TryGetValue(service.Target, out var address))
                        {
                            device.Address = address;
                        }
                    }

                    if (txt.TryGetValue(instance, out var values))
                    {
                        if (values.TryGetValue("id", out string id) && id.Length > 0)
                        {
                            device.Id = id;
                        }

                        if (values.TryGetValue("fn", out string name) && name.Length > 0)
                        {
                            device.Name = name;
                        }

                        values.TryGetValue("md", out string model);
                        device.Model = model ?? string.Empty;
                    }

                    devices.Add(device);
                }
            }
            catch (IndexOutOfRangeException)
            {
                devices.Clear();
            }
            catch (FormatException)
            {
                devices.Clear();
            }

            return devices;
        }

        private static Dictionary<string, string> ReadTxt(byte[] data, int start, int length)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = start;
            while (i < start + length)
            {
                int size = data[i++];
                if (i + size > start + length)
                {
                    break;
                }

                string entry = Encoding.UTF8.GetString(data, i, size);
                int equals = entry.IndexOf('=');
                if (equals > 0)
                {
                    values[entry.Substring(0, equals)] = entry.Substring(equals + 1);
                }

                i += size;
            }

            return values;
        }

        /// <summary>A possibly compressed name at <paramref name="i"/>; leaves <paramref name="i"/> just past it.</summary>
        private static string ReadName(byte[] data, ref int i, int length)
        {
            var labels = new List<string>();
            int at = i;
            int jumps = 0;
            bool jumped = false;
            while (true)
            {
                if (at >= length)
                {
                    throw new FormatException("Name runs past the message");
                }

                int size = data[at];
                if (size == 0)
                {
                    at++;
                    break;
                }

                if ((size & 0xC0) == 0xC0)
                {
                    if (++jumps > 16 || at + 1 >= length)
                    {
                        throw new FormatException("Bad name pointer");
                    }

                    int target = ((size & 0x3F) << 8) | data[at + 1];
                    if (!jumped)
                    {
                        i = at + 2;
                        jumped = true;
                    }

                    at = target;
                    continue;
                }

                if (at + 1 + size > length)
                {
                    throw new FormatException("Label runs past the message");
                }

                labels.Add(Encoding.UTF8.GetString(data, at + 1, size));
                at += 1 + size;
            }

            if (!jumped)
            {
                i = at;
            }

            return string.Join(".", labels);
        }

        private static int Read16(byte[] data, int i, int length)
        {
            if (i + 2 > length)
            {
                throw new FormatException("Message too short");
            }

            return (data[i] << 8) | data[i + 1];
        }
    }

    /// <summary>
    /// Finds Chromecasts on the LAN (design doc §2.4). It asks the mDNS group from a port of its
    /// own (a "legacy unicast" query), so devices answer it directly and it never competes with a
    /// system mDNS responder for port 5353. Non-blocking: call <see cref="Poll"/> from the game loop.
    /// </summary>
    public sealed class CastDiscovery : IDisposable
    {
        /// <summary>How often to ask again; Chromecasts that stop answering drop out after <see cref="ForgetMs"/>.</summary>
        public const long QueryIntervalMs = 10_000;
        public const long ForgetMs = 35_000;

        private readonly Socket _socket;
        private readonly byte[] _buffer = new byte[9000];
        private readonly List<CastDevice> _devices = new List<CastDevice>();
        private readonly IPEndPoint _target;
        private long _nextQueryMs = long.MinValue;

        /// <summary>Asks <paramref name="target"/> (the mDNS group by default; a test passes its own).</summary>
        public CastDiscovery(IPEndPoint target = null)
        {
            _target = target ?? new IPEndPoint(Mdns.Group, Mdns.Port);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        }

        /// <summary>The Chromecasts that have answered lately, in the order they were first found.</summary>
        public IReadOnlyList<CastDevice> Devices => _devices;

        /// <summary>Raised the first time each device answers.</summary>
        public event Action<CastDevice> Found;

        /// <summary>Asks again if it's time, takes in any answers and forgets devices that went quiet.</summary>
        public void Poll(long nowMs)
        {
            if (nowMs >= _nextQueryMs)
            {
                _nextQueryMs = nowMs + QueryIntervalMs;
                try
                {
                    _socket.SendTo(Mdns.Query(), _target);
                }
                catch (SocketException)
                {
                    // No route for multicast right now; try again next time.
                }
            }

            while (_socket.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int received;
                try
                {
                    received = _socket.ReceiveFrom(_buffer, ref from);
                }
                catch (SocketException)
                {
                    break;
                }

                foreach (var device in Mdns.ParseReply(_buffer, received, ((IPEndPoint)from).Address))
                {
                    Observe(device, nowMs);
                }
            }

            _devices.RemoveAll(d => nowMs - d.SeenAtMs > ForgetMs);
        }

        /// <summary>The device <paramref name="wanted"/> names (see <see cref="CastDevice.Matches"/>), or null.</summary>
        public CastDevice Find(string wanted)
        {
            foreach (var device in _devices)
            {
                if (device.Matches(wanted))
                {
                    return device;
                }
            }

            return null;
        }

        private void Observe(CastDevice device, long nowMs)
        {
            device.SeenAtMs = nowMs;
            for (int i = 0; i < _devices.Count; i++)
            {
                if (string.Equals(_devices[i].Id, device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _devices[i] = device;
                    return;
                }
            }

            _devices.Add(device);
            Found?.Invoke(device);
        }

        public void Dispose() => _socket.Dispose();
    }
}
