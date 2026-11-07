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
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.PostAsync(Url("/live.m3u8"), new StringContent("x"))).StatusCode);
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
            var args = ViewerStream.Arguments("/tmp/v");
            string line = string.Join(" ", args);
            Assert.Contains("-f rawvideo -pix_fmt rgb24 -video_size 1280x720 -framerate 25 -i pipe:0", line);
            Assert.Contains("-g 25", line);
            Assert.Contains("-f hls -hls_time 1", line);
            Assert.EndsWith("live.m3u8\"", line);
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

        /// <summary>Runs the real ffmpeg when there is one (CI may not have it) and checks what it writes.</summary>
        [Fact]
        public void StreamsPlayableHls()
        {
            if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            {
                return;
            }

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

        private static bool HasTool(string name)
        {
            try
            {
                Run(name, "-version");
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
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
