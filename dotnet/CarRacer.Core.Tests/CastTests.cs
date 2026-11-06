using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        internal static byte[] Reply(string instance, string name, string model, string id, byte[] address, int port, bool includeAddress)
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
            foreach (string entry in new[] { "id=" + id, "md=" + model, "fn=" + name, "bad" })
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

    public class CastSessionTests
    {
        private const string Url = "http://192.168.1.20:47780/live.m3u8";

        internal static CastMessage Status(params string[] apps) => new CastMessage(CastSession.ReceiverId, CastSession.SenderId, CastMessage.ReceiverNamespace,
            "{\"type\":\"RECEIVER_STATUS\",\"status\":{\"applications\":[" + string.Join(",", apps) + "]}}");

        internal static string App(string appId, string name, bool idle, string session = "s1", string transport = "t1") =>
            $"{{\"appId\":\"{appId}\",\"displayName\":\"{name}\",\"isIdleScreen\":{(idle ? "true" : "false")},\"sessionId\":\"{session}\",\"transportId\":\"{transport}\"}}";

        internal static CastMessage Media(string status) => new CastMessage("t1", CastSession.SenderId, CastMessage.MediaNamespace, "{\"type\":\"MEDIA_STATUS\",\"status\":[" + status + "]}");

        [Fact]
        public void LaunchesTheMediaReceiverAndLoadsTheStream()
        {
            var session = new CastSession("Lounge", takeOver: false);
            var begin = session.Begin(Url, 0);
            Assert.Equal(new[] { "CONNECT", "GET_STATUS" }, begin.Select(m => m.PayloadType));

            var launch = Assert.Single(session.Handle(Status(App("E8C28D3C", "Backdrop", idle: true)), 10));
            Assert.Equal(CastState.Launching, session.State);
            Assert.Contains("\"appId\":\"CC1AD845\"", launch.Payload);

            // A status before the player is up changes nothing.
            Assert.Empty(session.Handle(Status(App("E8C28D3C", "Backdrop", idle: true)), 20));
            var load = session.Handle(Status(App(CastSession.MediaReceiverAppId, "Default Media Receiver", idle: false)), 30);
            Assert.Equal(new[] { "CONNECT", "LOAD" }, load.Select(m => m.PayloadType));
            Assert.All(load, m => Assert.Equal("t1", m.DestinationId));
            var media = Json.Object(Json.TryParseObject(load[1].Payload), "media");
            Assert.Equal(Url, Json.String(media, "contentId"));
            Assert.Equal("LIVE", Json.String(media, "streamType"));
            Assert.Equal("Lounge", Json.String(Json.Object(media, "metadata"), "title"));
            Assert.Equal(CastState.Loading, session.State);

            session.Handle(Media("{\"playerState\":\"BUFFERING\"}"), 40);
            Assert.Equal(CastState.Playing, session.State);
            Assert.False(session.Finished);
        }

        [Fact]
        public void LeavesABusyTvAloneUnlessTakingOver()
        {
            var polite = new CastSession("Lounge", takeOver: false);
            polite.Begin(Url, 0);
            Assert.Empty(polite.Handle(Status(App("233637DE", "YouTube", idle: false)), 1));
            Assert.Equal(CastState.Busy, polite.State);
            Assert.Equal("The TV is showing YouTube", polite.Detail);

            var leader = new CastSession("Lounge", takeOver: true);
            leader.Begin(Url, 0);
            Assert.Equal("LAUNCH", Assert.Single(leader.Handle(Status(App("233637DE", "YouTube", idle: false)), 1)).PayloadType);

            // Nothing running at all counts as idle.
            var empty = new CastSession("Lounge", takeOver: false);
            empty.Begin(Url, 0);
            Assert.Equal("LAUNCH", Assert.Single(empty.Handle(Status(), 1)).PayloadType);
        }

        [Fact]
        public void AnswersHeartbeatsAndSendsItsOwn()
        {
            var session = Playing();
            var pong = Assert.Single(session.Handle(new CastMessage(CastSession.ReceiverId, CastSession.SenderId, CastMessage.HeartbeatNamespace, "{\"type\":\"PING\"}"), 100));
            Assert.Equal("PONG", pong.PayloadType);
            Assert.Equal(CastSession.ReceiverId, pong.DestinationId);
            Assert.Empty(session.Tick(CastSession.HeartbeatMs - 1));
            Assert.Equal("PING", Assert.Single(session.Tick(CastSession.HeartbeatMs)).PayloadType);
            Assert.Empty(session.Tick(CastSession.HeartbeatMs + 10));
        }

        [Fact]
        public void EndsWhenSomethingElseTakesTheTvOrItGoesQuiet()
        {
            var taken = Playing();
            taken.Handle(Status(App("233637DE", "YouTube", idle: false, session: "other")), 50);
            Assert.Equal(CastState.Stopped, taken.State);

            var quiet = Playing();
            quiet.Tick(CastSession.SilenceMs + 100);
            Assert.Equal(CastState.Failed, quiet.State);
            Assert.Equal("The TV stopped answering", quiet.Detail);

            var broken = Playing();
            broken.Handle(Media("{\"playerState\":\"IDLE\",\"idleReason\":\"ERROR\"}"), 50);
            Assert.Equal(CastState.Failed, broken.State);

            var refused = new CastSession("Lounge", false);
            refused.Begin(Url, 0);
            refused.Handle(Status(), 1);
            refused.Handle(new CastMessage(CastSession.ReceiverId, CastSession.SenderId, CastMessage.ReceiverNamespace, "{\"type\":\"LAUNCH_ERROR\",\"reason\":\"NOT_ALLOWED\"}"), 2);
            Assert.Equal(CastState.Failed, refused.State);
            Assert.Contains("NOT_ALLOWED", refused.Detail);

            var slow = new CastSession("Lounge", false);
            slow.Begin(Url, 0);
            slow.Handle(new CastMessage(CastSession.ReceiverId, CastSession.SenderId, CastMessage.HeartbeatNamespace, "{\"type\":\"PING\"}"), CastSession.StepTimeoutMs);
            slow.Tick(CastSession.StepTimeoutMs + 1);
            Assert.Equal(CastState.Failed, slow.State);
        }

        [Fact]
        public void StoppingClosesThePlayerItStarted()
        {
            var session = Playing();
            var stop = Assert.Single(session.Stop());
            Assert.Equal("STOP", stop.PayloadType);
            Assert.Contains("\"sessionId\":\"s1\"", stop.Payload);
            Assert.Equal(CastState.Stopped, session.State);
            Assert.Empty(session.Stop());
            Assert.Empty(session.Handle(Media("{\"playerState\":\"PLAYING\"}"), 99));
            Assert.Equal(CastState.Stopped, session.State);
        }

        internal static CastSession Playing()
        {
            var session = new CastSession("Lounge", takeOver: false);
            session.Begin(Url, 0);
            session.Handle(Status(), 1);
            session.Handle(Status(App(CastSession.MediaReceiverAppId, "Default Media Receiver", idle: false)), 2);
            session.Handle(Media("{\"playerState\":\"PLAYING\"}"), 3);
            Assert.Equal(CastState.Playing, session.State);
            return session;
        }
    }

    public class CastConnectionTests
    {
        [Fact]
        public void CastsToAFakeChromecastOverTls()
        {
            using var cert = SelfSigned();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var seen = new List<string>();
            string loadedUrl = null;
            var server = new Thread(() =>
            {
                using var client = listener.AcceptTcpClient();
                using var tls = new SslStream(client.GetStream());
                tls.AuthenticateAsServer(cert, false, System.Security.Authentication.SslProtocols.Tls12, false);
                void Send(CastMessage m)
                {
                    var f = m.Frame();
                    tls.Write(f, 0, f.Length);
                }

                try
                {
                    while (true)
                    {
                        var m = CastMessage.ReadFrom(tls);
                        if (m == null)
                        {
                            break;
                        }

                        lock (seen)
                        {
                            seen.Add(m.PayloadType);
                        }

                        switch (m.PayloadType)
                        {
                            case "GET_STATUS":
                                Send(CastSessionTests.Status(CastSessionTests.App("E8C28D3C", "Backdrop", idle: true)));
                                break;
                            case "LAUNCH":
                                Send(CastSessionTests.Status(CastSessionTests.App(CastSession.MediaReceiverAppId, "Default Media Receiver", idle: false)));
                                break;
                            case "LOAD":
                                loadedUrl = Json.String(Json.Object(Json.TryParseObject(m.Payload), "media"), "contentId");
                                Send(CastSessionTests.Media("{\"playerState\":\"PLAYING\"}"));
                                break;
                            case "STOP":
                                return;
                        }
                    }
                }
                catch (IOException)
                {
                }
            })
            { IsBackground = true };
            server.Start();

            var device = new CastDevice { Name = "Fake", Address = IPAddress.Loopback, Port = ((IPEndPoint)listener.LocalEndpoint).Port };
            using var connection = new CastConnection(device, local => $"http://{local}:47780/live.m3u8", "Lounge", takeOver: false);
            Assert.True(SpinWait.SpinUntil(() => connection.State == CastState.Playing || connection.Finished, 10_000), connection.Detail);
            Assert.Equal(CastState.Playing, connection.State);
            Assert.Equal("http://127.0.0.1:47780/live.m3u8", loadedUrl);

            connection.Stop();
            Assert.True(SpinWait.SpinUntil(() => connection.Finished, 5_000));
            Assert.Equal(CastState.Stopped, connection.State);
            Assert.True(server.Join(5_000));
            lock (seen)
            {
                Assert.Equal(new[] { "CONNECT", "GET_STATUS", "LAUNCH", "CONNECT", "LOAD", "STOP" }, seen.Where(t => t != "PING"));
            }

            listener.Stop();
        }

        [Fact]
        public void ReportsATvThatCantBeReached()
        {
            // A port nothing listens on.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            using var connection = new CastConnection(new CastDevice { Name = "Gone", Address = IPAddress.Loopback, Port = port }, _ => "http://x/", "Lounge", false);
            Assert.True(SpinWait.SpinUntil(() => connection.Finished, 10_000));
            Assert.Equal(CastState.Failed, connection.State);
        }

        private static X509Certificate2 SelfSigned()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Chromecast", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return new X509Certificate2(cert.Export(X509ContentType.Pfx));
        }
    }
}
