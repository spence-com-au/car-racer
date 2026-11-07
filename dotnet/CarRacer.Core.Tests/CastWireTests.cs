using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CarRacer.Core.Cast;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void ReadsWhatTheCastProtocolSends()
        {
            var obj = Json.TryParseObject("{\"type\":\"RECEIVER_STATUS\",\"requestId\":3,\"status\":{\"applications\":[{\"appId\":\"CC1AD845\",\"isIdleScreen\":false,\"n\":-1.5e2}],\"x\":null,\"s\":\"a\\\"b\\u00e9\\n\"}}");
            Assert.Equal("RECEIVER_STATUS", Json.String(obj, "type"));
            Assert.Equal(3, Json.Long(obj, "requestId"));
            var status = Json.Object(obj, "status");
            var app = (Dictionary<string, object>)Json.Array(status, "applications")[0];
            Assert.Equal("CC1AD845", Json.String(app, "appId"));
            Assert.False(Json.Bool(app, "isIdleScreen"));
            Assert.Equal(-150.0, app["n"]);
            Assert.Null(status["x"]);
            Assert.Equal("a\"bé\n", Json.String(status, "s"));
        }

        [Fact]
        public void WritesAndReadsBack()
        {
            var value = new Dictionary<string, object>
            {
                ["type"] = "LOAD",
                ["autoplay"] = true,
                ["n"] = 0,
                ["list"] = new List<object> { "a", 1.5, null },
                ["title"] = "Quote \" and \\ and\ttab",
            };
            string text = Json.Write(value);
            Assert.Equal("{\"type\":\"LOAD\",\"autoplay\":true,\"n\":0,\"list\":[\"a\",1.5,null],\"title\":\"Quote \\\" and \\\\ and\\ttab\"}", text);
            var back = Json.TryParseObject(text);
            Assert.Equal(value["title"], back["title"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1,]")]
        [InlineData("\"open")]
        [InlineData("{} x")]
        public void RejectsBrokenJson(string text)
        {
            Assert.Throws<FormatException>(() => Json.Parse(text));
            Assert.Null(Json.TryParseObject(text));
        }

        [Fact]
        public void RefusesDeepNesting() => Assert.Throws<FormatException>(() => Json.Parse(new string('[', 100) + new string(']', 100)));
    }

    public class CastMessageTests
    {
        [Fact]
        public void EncodesTheProtobufFields()
        {
            var bytes = new CastMessage("s", "r", "n", "{}").Encode();
            Assert.Equal(new byte[] { 0x08, 0, 0x12, 1, (byte)'s', 0x1A, 1, (byte)'r', 0x22, 1, (byte)'n', 0x28, 0, 0x32, 2, (byte)'{', (byte)'}' }, bytes);
        }

        [Fact]
        public void FramesRoundTripThroughAStream()
        {
            string longPayload = "{\"title\":\"" + new string('x', 300) + "é\"}";
            var stream = new MemoryStream();
            foreach (var m in new[] { new CastMessage("sender-0", "receiver-0", CastMessage.HeartbeatNamespace, "{\"type\":\"PING\"}"), new CastMessage("a", "b", CastMessage.MediaNamespace, longPayload) })
            {
                var frame = m.Frame();
                stream.Write(frame, 0, frame.Length);
            }

            stream.Position = 0;
            var first = CastMessage.ReadFrom(stream);
            Assert.Equal("PING", first.PayloadType);
            Assert.Equal("receiver-0", first.DestinationId);
            var second = CastMessage.ReadFrom(stream);
            Assert.Equal(longPayload, second.Payload);
            Assert.Null(CastMessage.ReadFrom(stream));
        }

        [Fact]
        public void SkipsUnknownFieldsAndRejectsBadOnes()
        {
            // An unknown varint field 9 and binary field 7 are skipped.
            var data = new byte[] { 0x48, 0x96, 0x01, 0x3A, 2, 1, 2, 0x22, 1, (byte)'n' };
            Assert.Equal("n", CastMessage.Decode(data, 0, data.Length).Namespace);
            Assert.Throws<FormatException>(() => CastMessage.Decode(new byte[] { 0x22, 5, (byte)'n' }, 0, 3));
            Assert.Throws<FormatException>(() => CastMessage.ReadFrom(new MemoryStream(new byte[] { 0x7F, 0, 0, 0 })));
            Assert.Throws<EndOfStreamException>(() => CastMessage.ReadFrom(new MemoryStream(new byte[] { 0, 0, 0, 9, 1 })));
        }
    }

    public class MdnsTests
    {
        [Fact]
        public void QueryAsksForTheCastService()
        {
            var q = Mdns.Query();
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, q.Take(12));
            Assert.Equal("\v_googlecast\u0004_tcp\u0005local\0", Encoding.ASCII.GetString(q, 12, q.Length - 16));
            Assert.Equal(new byte[] { 0, 12, 0, 1 }, q.Skip(q.Length - 4));
        }

        [Fact]
        public void ReadsADeviceFromAReply()
        {
            var reply = Reply("Chromecast-abc123", "Living Room TV", "Chromecast", "abc123", new byte[] { 192, 168, 1, 50 }, 8009, includeAddress: true);
            var device = Assert.Single(Mdns.ParseReply(reply, reply.Length, IPAddress.Parse("10.0.0.1")));
            Assert.Equal("abc123", device.Id);
            Assert.Equal("Living Room TV", device.Name);
            Assert.Equal("Chromecast", device.Model);
            Assert.Equal(IPAddress.Parse("192.168.1.50"), device.Address);
            Assert.Equal(8009, device.Port);
            Assert.True(device.Matches(" living room tv "));
            Assert.True(device.Matches("192.168.1.50"));
            Assert.True(device.Matches("ABC123"));
            Assert.False(device.Matches("Kitchen"));
        }

        [Fact]
        public void WithoutAnAddressRecordTheDeviceIsWhereTheReplyCameFrom()
        {
            var reply = Reply("Speaker-x", "Den", "Google TV", "x", null, 8010, includeAddress: false);
            var device = Assert.Single(Mdns.ParseReply(reply, reply.Length, IPAddress.Parse("10.0.0.7")));
            Assert.Equal(IPAddress.Parse("10.0.0.7"), device.Address);
            Assert.Equal(8010, device.Port);
        }

        [Fact]
        public void SpeakersAndGroupsAreNotTvs()
        {
            var speaker = Reply("Google-Home-x", "Kitchen speaker", "Google Home", "x", null, 8009, includeAddress: false, capabilities: "2052");
            Assert.Empty(Mdns.ParseReply(speaker, speaker.Length, IPAddress.Loopback));
            var unknown = Reply("Chromecast-y", "Den", "Chromecast", "y", null, 8009, includeAddress: false, capabilities: null);
            Assert.Single(Mdns.ParseReply(unknown, unknown.Length, IPAddress.Loopback));
        }

        [Fact]
        public void BrokenRepliesGiveNothing()
        {
            var reply = Reply("Chromecast-abc123", "Living Room TV", "Chromecast", "abc123", new byte[] { 192, 168, 1, 50 }, 8009, includeAddress: true);
            for (int length = 0; length < reply.Length; length++)
            {
                // Never throws, whatever is cut off.
                Mdns.ParseReply(reply, length, IPAddress.Loopback);
            }

            // A pointer that loops on itself.
            var loop = new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12, 0, 12, 0, 1, 0, 0, 0, 0, 0, 0 };
            Assert.Empty(Mdns.ParseReply(loop, loop.Length, IPAddress.Loopback));

            // A query isn't a reply.
            var query = Mdns.Query();
            Assert.Empty(Mdns.ParseReply(query, query.Length, IPAddress.Loopback));
        }

        [Fact]
        public void DiscoveryFindsAResponderAndForgetsItWhenQuiet()
        {
            using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var discovery = new CastDiscovery(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)responder.Client.LocalEndPoint).Port));
            var found = new List<CastDevice>();
            discovery.Found += found.Add;
            discovery.Poll(0);
            IPEndPoint asker = null;
            var query = responder.Receive(ref asker);
            Assert.Equal(Mdns.Query(), query);
            var reply = Reply("Chromecast-abc123", "Living Room TV", "Chromecast", "abc123", new byte[] { 127, 0, 0, 1 }, 8009, includeAddress: true);
            responder.Send(reply, reply.Length, asker);
            responder.Send(reply, reply.Length, asker);

            var clock = Stopwatch.StartNew();
            while (discovery.Devices.Count == 0 && clock.ElapsedMilliseconds < 2000)
            {
                Thread.Sleep(10);
                discovery.Poll(1);
            }

            Thread.Sleep(50);
            discovery.Poll(2);
            Assert.Single(found);
            Assert.Same(discovery.Devices[0], discovery.Find("Living Room TV"));
            discovery.Poll(CastDiscovery.ForgetMs + 10);
            Assert.Empty(discovery.Devices);
        }

        /// <summary>A reply as a Chromecast sends it: PTR answer, then SRV, TXT and A records, names compressed.</summary>
        internal static byte[] Reply(string instance, string name, string model, string id, byte[] address, int port, bool includeAddress, string capabilities = "4101")
        {
            var b = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, (byte)(includeAddress ? 3 : 2) };
            int service = b.Count;
            b.AddRange(Name("_googlecast._tcp.local"));
            Header(b, 12);
            int instanceAt = b.Count + 2;
            var ptr = new List<byte>();
            ptr.Add((byte)instance.Length);
            ptr.AddRange(Encoding.ASCII.GetBytes(instance));
            ptr.Add(0xC0);
            ptr.Add((byte)service);
            Data(b, ptr);

            b.Add(0xC0);
            b.Add((byte)instanceAt);
            Header(b, 33);
            var srv = new List<byte> { 0, 0, 0, 0, (byte)(port >> 8), (byte)port };
            srv.AddRange(Name("host.local"));
            int hostAt = b.Count + 2 + 6;
            Data(b, srv);

            b.Add(0xC0);
            b.Add((byte)instanceAt);
            Header(b, 16);
            var txt = new List<byte>();
            var entries = new List<string> { "id=" + id, "md=" + model, "fn=" + name, "bad" };
            if (capabilities != null)
            {
                entries.Add("ca=" + capabilities);
            }

            foreach (string entry in entries)
            {
                txt.Add((byte)Encoding.UTF8.GetByteCount(entry));
                txt.AddRange(Encoding.UTF8.GetBytes(entry));
            }

            Data(b, txt);
            if (includeAddress)
            {
                b.Add(0xC0);
                b.Add((byte)hostAt);
                Header(b, 1);
                Data(b, address.ToList());
            }

            return b.ToArray();
        }

        private static IEnumerable<byte> Name(string name)
        {
            var bytes = new List<byte>();
            foreach (string label in name.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(label));
            }

            bytes.Add(0);
            return bytes;
        }

        private static void Header(List<byte> b, int type) => b.AddRange(new byte[] { 0, (byte)type, 0x80, 1, 0, 0, 0, 120 });

        private static void Data(List<byte> b, List<byte> data)
        {
            b.Add((byte)(data.Count >> 8));
            b.Add((byte)data.Count);
            b.AddRange(data);
        }
    }
}
