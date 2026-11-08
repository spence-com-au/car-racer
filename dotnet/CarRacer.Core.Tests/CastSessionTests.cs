using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using CarRacer.Core.Cast;
using Xunit;

namespace CarRacer.Core.Tests
{
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

        [Fact]
        public void StoppingWhileLaunchingClosesThePlayerOnceItIsUp()
        {
            var session = new CastSession("Lounge", takeOver: false);
            session.Begin(Url, 0);
            session.Handle(Status(), 1);
            Assert.Equal(CastState.Launching, session.State);

            // Nothing to close yet, so it waits for the TV to report the player.
            Assert.Empty(session.Stop());
            Assert.Empty(session.Stop());
            Assert.False(session.Finished);

            var stop = Assert.Single(session.Handle(Status(App(CastSession.MediaReceiverAppId, "Default Media Receiver", idle: false, session: "s9")), 2));
            Assert.Equal("STOP", stop.PayloadType);
            Assert.Contains("\"sessionId\":\"s9\"", stop.Payload);
            Assert.Equal(CastState.Stopped, session.State);
        }

        [Fact]
        public void StoppingWhileLaunchingEndsAnywayIfThePlayerNeverAppears()
        {
            var session = new CastSession("Lounge", takeOver: false);
            session.Begin(Url, 0);
            session.Handle(Status(), 1);
            session.Stop();
            session.Handle(new CastMessage(CastSession.ReceiverId, CastSession.SenderId, CastMessage.HeartbeatNamespace, "{\"type\":\"PING\"}"), CastSession.StepTimeoutMs);
            session.Tick(CastSession.StepTimeoutMs + 2);
            Assert.Equal(CastState.Stopped, session.State);
        }

        [Fact]
        public void StoppingBeforeLaunchingSendsNothing()
        {
            var session = new CastSession("Lounge", takeOver: false);
            session.Begin(Url, 0);
            Assert.Empty(session.Stop());
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
            var server = StartFakeChromecast(listener, cert, hangUpAfterLoad: false, seen, m => loadedUrl = Json.String(Json.Object(Json.TryParseObject(m.Payload), "media"), "contentId"));

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

        [Fact]
        public void ATvThatNeverFinishesTheHandshakeFails()
        {
            using var tv = new MuteChromecast();
            using var connection = new CastConnection(tv.Device, _ => "http://x/", "Lounge", takeOver: false);
            Assert.True(SpinWait.SpinUntil(() => connection.Finished, CastConnection.ConnectTimeoutMs + 5_000));
            Assert.Equal(CastState.Failed, connection.State);
        }

        [Fact]
        public void ReportsATvThatDropsTheConnectionMidCast()
        {
            using var cert = SelfSigned();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = StartFakeChromecast(listener, cert, hangUpAfterLoad: true);

            var device = new CastDevice { Name = "Flaky", Address = IPAddress.Loopback, Port = ((IPEndPoint)listener.LocalEndpoint).Port };
            using var connection = new CastConnection(device, _ => "http://x/", "Lounge", takeOver: false);

            // The fake closes the socket right after reporting PLAYING, so the cast starts and is then cut off.
            Assert.True(SpinWait.SpinUntil(() => connection.Finished, 10_000), connection.Detail);
            Assert.Equal(CastState.Failed, connection.State);
            Assert.Equal("The TV dropped the connection", connection.Detail);
            Assert.True(server.Join(5_000));
            listener.Stop();
        }

        [Fact]
        public void DisposingAbandonsAHandshakeThatNeverFinishes()
        {
            using var tv = new MuteChromecast();
            var connection = new CastConnection(tv.Device, _ => "http://x/", "Lounge", takeOver: false);
            Assert.True(tv.Accepted(5_000));

            // Stopping can't interrupt the handshake, so Dispose gives up waiting and closes the socket instead.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            connection.Dispose();
            Assert.True(clock.ElapsedMilliseconds < CastConnection.ConnectTimeoutMs, $"Dispose took {clock.ElapsedMilliseconds} ms");
            Assert.True(SpinWait.SpinUntil(() => connection.Finished, 2_000));
            Assert.Equal(CastState.Failed, connection.State);
        }

        // Plays the TV's side of a cast over TLS: answers the status request, the launch and the load, and hangs up on
        // a STOP, or straight after reporting PLAYING when `hangUpAfterLoad` is set.
        private static Thread StartFakeChromecast(TcpListener listener, X509Certificate2 cert, bool hangUpAfterLoad, List<string> seen = null, Action<CastMessage> onLoad = null)
        {
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

                        if (seen != null)
                        {
                            lock (seen)
                            {
                                seen.Add(m.PayloadType);
                            }
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
                                onLoad?.Invoke(m);
                                Send(CastSessionTests.Media("{\"playerState\":\"PLAYING\"}"));
                                if (hangUpAfterLoad)
                                {
                                    return;
                                }

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
            return server;
        }

        private static X509Certificate2 SelfSigned()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Chromecast", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return new X509Certificate2(cert.Export(X509ContentType.Pfx));
        }

        // A TV that accepts the TCP connection, then says nothing.
        private sealed class MuteChromecast : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Thread _accept;
            private TcpClient _held;

            public MuteChromecast()
            {
                _listener.Start();
                _accept = new Thread(() => _held = _listener.AcceptTcpClient()) { IsBackground = true };
                _accept.Start();
                Device = new CastDevice { Name = "Mute", Address = IPAddress.Loopback, Port = ((IPEndPoint)_listener.LocalEndpoint).Port };
            }

            public CastDevice Device { get; }

            public bool Accepted(int timeoutMs) => _accept.Join(timeoutMs);

            public void Dispose()
            {
                _held?.Dispose();
                _listener.Stop();
            }
        }
    }
}
