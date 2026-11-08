using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using CarRacer.Core.Cast;
using CarRacer.Core.Viewer;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class TvCastPlanTests
    {
        private static readonly List<CastDevice> Tvs = new List<CastDevice>
        {
            new CastDevice { Id = "a", Name = "Kitchen", Address = IPAddress.Parse("10.0.0.2") },
            new CastDevice { Id = "b", Name = "Living Room TV", Address = IPAddress.Parse("10.0.0.3") },
        };

        [Fact]
        public void WaitsForTheStreamAndTheTvThenCastsOnce()
        {
            var plan = new TvCastPlan();
            Assert.Null(plan.Next(Tvs, false, true, out _));
            plan.Request("Living Room TV", takeOver: false);
            Assert.Null(plan.Next(Tvs, false, streamReady: false, out _));
            Assert.Null(plan.Next(new List<CastDevice>(), false, true, out _));
            var tv = plan.Next(Tvs, false, true, out bool takeOver);
            Assert.Equal("b", tv.Id);
            Assert.False(takeOver);
            Assert.Null(plan.Next(Tvs, false, true, out _));
        }

        [Fact]
        public void NoNameMeansTheFirstTvFound()
        {
            var plan = new TvCastPlan();
            plan.Request(string.Empty, takeOver: false);
            Assert.Equal("a", plan.Next(Tvs, false, true, out _).Id);
        }

        [Fact]
        public void CountdownsTryAgainPolitelyOnlyWhenNothingIsCast()
        {
            var plan = new TvCastPlan();
            plan.Request("Kitchen", takeOver: true);
            Assert.True(plan.Next(Tvs, false, true, out bool first) != null && first);

            plan.EventStarting(casting: true);
            Assert.Null(plan.Next(Tvs, casting: false, true, out _));

            plan.EventStarting(casting: false);
            var again = plan.Next(Tvs, false, true, out bool takeOver);
            Assert.Equal("a", again.Id);
            Assert.False(takeOver);

            plan.Forget();
            plan.EventStarting(casting: false);
            Assert.Null(plan.Next(Tvs, false, true, out _));
        }

        [Fact]
        public void ALeadersRequestWaitsForTheOldCastToEnd()
        {
            var plan = new TvCastPlan();
            plan.Request("Living Room TV", takeOver: true);
            Assert.Null(plan.Next(Tvs, casting: true, true, out _));
            Assert.Equal("b", plan.Next(Tvs, casting: false, true, out bool takeOver).Id);
            Assert.True(takeOver);

            // The server's own try is dropped while something is cast.
            plan.Request("Kitchen", takeOver: false);
            Assert.Null(plan.Next(Tvs, casting: true, true, out _));
            Assert.Null(plan.Next(Tvs, casting: false, true, out _));
        }
    }

    public class TvCastPlanAlreadyShowingTests
    {
        [Fact]
        public void RequestingTheTvAlreadyShowingQueuesNoTakeOver()
        {
            var tvs = new List<CastDevice> { new CastDevice { Id = "b", Name = "Living Room TV", Address = IPAddress.Parse("10.0.0.3") } };
            var plan = new TvCastPlan();
            plan.Request("Living Room TV", takeOver: true, alreadyShowing: true);
            Assert.Equal("Living Room TV", plan.Wanted);

            // Someone else takes the TV and our cast ends: no take-over follows on its own.
            Assert.Null(plan.Next(tvs, casting: false, true, out _));

            // The next countdown still tries again, politely.
            plan.EventStarting(casting: false);
            Assert.NotNull(plan.Next(tvs, casting: false, true, out bool takeOver));
            Assert.False(takeOver);
        }
    }

    public class ViewerHttpServerTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "viewer-http-" + Guid.NewGuid().ToString("N"));
        private readonly ViewerHttpServer _server;
        private readonly HttpClient _http = new HttpClient();

        public ViewerHttpServerTests()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "live.m3u8"), "#EXTM3U\n");
            File.WriteAllBytes(Path.Combine(_folder, "seg00001.ts"), new byte[] { 0x47, 1, 2 });
            File.WriteAllText(Path.Combine(_folder, ".secret"), "no");
            _server = new ViewerHttpServer(_folder, 0);
        }

        private string Url(string path) => $"http://127.0.0.1:{_server.Port}{path}";

        [Fact]
        public async System.Threading.Tasks.Task ServesThePlaylistAndSegmentsWithCors()
        {
            var playlist = await _http.GetAsync(Url("/live.m3u8?t=1"));
            Assert.Equal(HttpStatusCode.OK, playlist.StatusCode);
            Assert.Equal("application/vnd.apple.mpegurl", playlist.Content.Headers.ContentType.MediaType);
            Assert.Equal("*", playlist.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal("#EXTM3U\n", await playlist.Content.ReadAsStringAsync());

            var segment = await _http.GetAsync(Url("/seg00001.ts"));
            Assert.Equal("video/mp2t", segment.Content.Headers.ContentType.MediaType);
            Assert.Equal(new byte[] { 0x47, 1, 2 }, await segment.Content.ReadAsByteArrayAsync());

            var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url("/live.m3u8")));
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Equal(8, head.Content.Headers.ContentLength);

            var preflight = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Options, Url("/live.m3u8")));
            Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
            Assert.Equal($"http://10.0.0.5:{_server.Port}/live.m3u8", _server.UrlFor(IPAddress.Parse("10.0.0.5")));
        }

        [Theory]
        [InlineData("/missing.ts")]
        [InlineData("/.secret")]
        [InlineData("/../etc/passwd")]
        [InlineData("/%2e%2e/x")]
        [InlineData("/")]
        public async System.Threading.Tasks.Task RefusesAnythingElse(string path)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(Url(path))).StatusCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task OnlyBareFileNamesAreServed()
        {
            Assert.Equal("live.m3u8", ViewerHttpServer.FileFor("/live.m3u8?x=1"));
            Assert.Null(ViewerHttpServer.FileFor("/sub/live.m3u8"));
            Assert.Null(ViewerHttpServer.FileFor("/a\\b"));
            Assert.Null(ViewerHttpServer.FileFor("live.m3u8"));
            Assert.Null(ViewerHttpServer.FileFor("/.."));
            Assert.Null(ViewerHttpServer.FileFor("/../etc/passwd"));
            Assert.Null(ViewerHttpServer.FileFor("/%2e%2e/x"));
            Assert.Null(ViewerHttpServer.FileFor("/a..b"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.PostAsync(Url("/live.m3u8"), new StringContent("x"))).StatusCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task StalledClientsCannotTakeEveryRequestThread()
        {
            var stalled = new List<System.Net.Sockets.TcpClient>();
            for (int i = 0; i < ViewerHttpServer.MaxConcurrentRequests; i++)
            {
                stalled.Add(new System.Net.Sockets.TcpClient("127.0.0.1", _server.Port));
            }

            await Assert.ThrowsAsync<HttpRequestException>(() => _http.GetAsync(Url("/live.m3u8")));

            stalled.ForEach(c => c.Dispose());
            var deadline = Stopwatch.StartNew();
            HttpResponseMessage reply = null;
            while (reply == null && deadline.ElapsedMilliseconds < 5_000)
            {
                try
                {
                    reply = await _http.GetAsync(Url("/live.m3u8"));
                }
                catch (HttpRequestException)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                }
            }

            Assert.Equal(HttpStatusCode.OK, reply?.StatusCode);
        }

        [Fact]
        public void AClientTricklingItsRequestIsCutOff()
        {
            using var client = new System.Net.Sockets.TcpClient("127.0.0.1", _server.Port);
            var stream = client.GetStream();
            var elapsed = Stopwatch.StartNew();
            try
            {
                while (elapsed.ElapsedMilliseconds < 3 * ViewerHttpServer.ClientTimeoutMs)
                {
                    stream.WriteByte((byte)'G');
                    Thread.Sleep(250);
                }
            }
            catch (IOException)
            {
                // The server hung up.
            }

            Assert.InRange(elapsed.ElapsedMilliseconds, ViewerHttpServer.ClientTimeoutMs, 2 * ViewerHttpServer.ClientTimeoutMs);
        }

        public void Dispose()
        {
            _server.Dispose();
            _http.Dispose();
            Directory.Delete(_folder, recursive: true);
        }
    }

    public class ViewerStreamTests
    {
        [Fact]
        public void ArgumentsMakeLiveHlsFromRawFrames()
        {
            var args = ViewerStream.Arguments("/tmp/v", 40000);
            string line = string.Join(" ", args);
            Assert.Contains("-f s16le -ar 48000 -ac 2 -i tcp://127.0.0.1:40000", line);
            Assert.Contains("-f rawvideo -pix_fmt rgb24 -video_size 1280x720 -framerate 25 -i pipe:0", line);
            Assert.Contains("-g 25", line);
            Assert.Contains("-f hls -hls_time 1", line);
            Assert.EndsWith("live.m3u8\"", line);
        }

        [Fact]
        public void OnlyALiveConnectionFromTheSameUserIsTakenForFfmpegs()
        {
            var us = new IPEndPoint(IPAddress.Loopback, 40000);
            var them = new IPEndPoint(IPAddress.Loopback, 50000);
            static string Row(string local, string remote, string state, int uid, int inode) =>
                $"   1: {local} {remote} {state} 00000000:00000000 00:00000000 00000000 {uid,5} 0 {inode} 1 0000000000000000 20 4 30 10 -1";
            static string Ours(string state, int uid) => Row("0100007F:9C40", "0100007F:C350", state, uid, 101694263);
            static string Theirs(string state, int uid, int inode) => Row("0100007F:C350", "0100007F:9C40", state, uid, inode);
            const string header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";

            Assert.True(ViewerStream.ConnectedBySameUser(new[] { header, Ours("01", 1000), Theirs("01", 1000, 101864147) }, us, them));
            Assert.False(ViewerStream.ConnectedBySameUser(new[] { header, Ours("01", 1000), Theirs("01", 1001, 101864147) }, us, them));

            // Closed before it was taken, the far end lingers in FIN_WAIT2, which the kernel lists as root's: a server
            // run as root would otherwise take it for ffmpeg's.
            Assert.False(ViewerStream.ConnectedBySameUser(new[] { header, Ours("08", 0), Theirs("05", 0, 0) }, us, them));

            // A connection from an IPv6 socket is listed in /proc/net/tcp6 instead.
            Assert.False(ViewerStream.ConnectedBySameUser(new[] { header, Ours("01", 1000) }, us, them));
        }

        [Fact]
        public void AnFfmpegThatHangsIsGivenUpOn()
        {
            if (!File.Exists("/bin/sh"))
            {
                return;
            }

            string script = Path.Combine(Path.GetTempPath(), "hanging-ffmpeg-" + Guid.NewGuid().ToString("N") + ".sh");
            File.WriteAllText(script, "#!/bin/sh\nsleep 30\n");
            Run("chmod", "+x \"" + script + "\"");
            var clock = Stopwatch.StartNew();
            Assert.False(ViewerStream.IsAvailable(script, out string error, timeoutMs: 500));
            Assert.True(clock.ElapsedMilliseconds < 5_000, clock.ElapsedMilliseconds + " ms");
            Assert.Contains("didn't answer", error);
            File.Delete(script);
        }

        [Fact]
        public void AMissingFfmpegIsReported()
        {
            string folder = Path.Combine(Path.GetTempPath(), "viewer-missing-" + Guid.NewGuid().ToString("N"));
            Assert.Null(ViewerStream.Start("/nonexistent/ffmpeg", folder, out string error));
            Assert.Contains("ffmpeg not found", error);
            Directory.Delete(folder, recursive: true);
        }

        /// <summary>Runs the real ffmpeg, which the Test workflow installs, and checks what it writes.</summary>
        [Fact]
        public void StreamsPlayableHls()
        {
            string folder = Path.Combine(Path.GetTempPath(), "viewer-stream-" + Guid.NewGuid().ToString("N"));
            using (var stream = ViewerStream.Start("ffmpeg", folder, out string error))
            {
                Assert.True(stream != null, error);
                var race = new FakeRaceView { Standings = new Net.StandingsMessage() };
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 20_000 && Directory.GetFiles(folder, "*.ts").Length < 2)
                {
                    stream.Show(ViewerScene.Build(ViewerSceneTests.Racing(race, clock.ElapsedMilliseconds)));
                    Thread.Sleep(40);
                }

                Assert.Null(stream.Failure);
                Assert.True(stream.Ready);
                string playlist = File.ReadAllText(Path.Combine(folder, ViewerStream.PlaylistName));
                Assert.Contains("#EXTINF", playlist);
                Assert.DoesNotContain("#EXT-X-ENDLIST", playlist);
                string segment = Directory.GetFiles(folder, "*.ts").OrderBy(f => f).First();
                string probe = Run("ffprobe", $"-v error -show_entries stream=codec_name,width,height -of csv=p=0 \"{segment}\"");
                Assert.Contains("h264,1280,720", probe);
                Assert.Contains("aac", probe);
            }

            Directory.Delete(folder, recursive: true);
        }

        [Fact]
        public void MusicCrossfadesAndAppliesGain()
        {
            var a = new short[] { 1000, -1000, 1000, -1000 };
            var b = new short[] { 0, 0, 30000, 30000 };
            var output = new short[4];
            MusicFeed.Mix(a, null, output, 2, 0, 10);
            Assert.Equal(new short[] { 800, -800, 800, -800 }, output);

            // Halfway through a fade to silence, half as loud.
            MusicFeed.Mix(a, null, output, 1, 5, 10);
            Assert.Equal(400, output[0]);

            // Fully faded in, only the new tune.
            MusicFeed.Mix(a, b, output, 2, 10, 10);
            Assert.Equal(new short[] { 0, 0, 24000, 24000 }, output);

            var loud = new short[] { short.MaxValue, short.MinValue };
            MusicFeed.Mix(loud, loud, output, 1, 5, 10);
            Assert.Equal((short)Math.Round(short.MaxValue * MusicFeed.Gain), output[0]);
        }

        [Fact]
        public void MissingMusicIsSilence()
        {
            using var feed = new MusicFeed("ffmpeg");
            feed.Play("/nonexistent/tune.ogg");
            var pcm = new short[960 * 2];
            for (int i = 0; i < 40; i++)
            {
                feed.Fill(pcm, 960);
                Assert.All(pcm, s => Assert.Equal(0, s));
            }

            Assert.Contains("No music file", feed.Error);
        }

        /// <summary>With the real ffmpeg, a tune asked for is heard in the stream.</summary>
        [Fact]
        public void StreamsTheMusic()
        {
            string folder = Path.Combine(Path.GetTempPath(), "viewer-music-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string tune = Path.Combine(folder, "tune.ogg");
            Run("ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:duration=2 -ac 2 -c:a libvorbis \"{tune}\"");
            Assert.True(File.Exists(tune));

            string stream = Path.Combine(folder, "hls");
            using (var viewer = ViewerStream.Start("ffmpeg", stream, out string error))
            {
                Assert.True(viewer != null, error);
                viewer.Music.Play(tune);
                var race = new FakeRaceView { Standings = new Net.StandingsMessage() };
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 20_000 && Directory.GetFiles(stream, "*.ts").Length < 3)
                {
                    viewer.Show(ViewerScene.Build(ViewerSceneTests.Racing(race, clock.ElapsedMilliseconds)));
                    Thread.Sleep(40);
                }

                Assert.Null(viewer.Failure);
                Assert.Null(viewer.Music.Error);

                // A segment after the fade-in is loud; volumedetect reports a mean far above silence (-91 dB).
                string segment = Directory.GetFiles(stream, "*.ts").OrderBy(f => f).Last();
                string volume = RunErr("ffmpeg", $"-hide_banner -i \"{segment}\" -af volumedetect -vn -f null -");
                string mean = volume.Split('\n').First(l => l.Contains("mean_volume")).Split(':')[1].Trim().Split(' ')[0];
                Assert.True(double.Parse(mean, System.Globalization.CultureInfo.InvariantCulture) > -40, volume);
            }

            Directory.Delete(folder, recursive: true);
        }

        private static string RunErr(string file, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardError = true, UseShellExecute = false });
            string output = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        private static string Run(string file, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, UseShellExecute = false });
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}
