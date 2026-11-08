using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CarRacer.Core.Session;

namespace CarRacer.Core.Viewer
{
    /// <summary>
    /// Turns the TV viewer into a live video stream (design doc §2.4): a background thread draws the
    /// latest <see cref="ViewerScene"/> 25 times a second with a <see cref="ViewerRenderer"/> and
    /// pipes the frames to <c>ffmpeg</c>, which writes HLS (<see cref="PlaylistName"/> and its
    /// segments) into <see cref="Folder"/> for <see cref="ViewerHttpServer"/> to serve. The game
    /// thread only calls <see cref="Show"/>. If drawing falls behind, the last frame is sent again so
    /// the stream keeps time with the race. A second thread feeds ffmpeg the sound over a loopback
    /// TCP connection, in step with the clock: the music <see cref="Music"/> plays, or silence.
    /// </summary>
    public sealed class ViewerStream : IDisposable
    {
        public const int FramesPerSecond = 25;
        public const string PlaylistName = "live.m3u8";

        /// <summary>Behind by more than this, frames are repeated rather than drawn, to catch up.</summary>
        private const long MaxLagMs = 200;

        private readonly object _lock = new object();
        private readonly Process _ffmpeg;
        private readonly Thread _thread;
        private readonly Thread _audioThread;
        private readonly TcpListener _audioListener;
        private readonly Queue<string> _errors = new Queue<string>();
        private ViewerScene _scene;
        private volatile bool _stopping;
        private volatile string _failure;

        private ViewerStream(Process ffmpeg, string folder, TcpListener audioListener, MusicFeed music)
        {
            _ffmpeg = ffmpeg;
            Folder = folder;
            _audioListener = audioListener;
            Music = music;
            _ffmpeg.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (_errors)
                    {
                        _errors.Enqueue(e.Data);
                        while (_errors.Count > 5)
                        {
                            _errors.Dequeue();
                        }
                    }
                }
            };
            _ffmpeg.BeginErrorReadLine();
            _thread = new Thread(Run) { IsBackground = true, Name = "TV viewer" };
            _thread.Start();
            _audioThread = new Thread(RunAudio) { IsBackground = true, Name = "TV viewer sound" };
            _audioThread.Start();
        }

        /// <summary>The TV's music: <see cref="MusicFeed.Play"/> a tune, or null for silence.</summary>
        public MusicFeed Music { get; }

        /// <summary>Where the playlist and segments are written.</summary>
        public string Folder { get; }

        /// <summary>Whether ffmpeg has written the playlist, so a TV can start playing it.</summary>
        public bool Ready => File.Exists(Path.Combine(Folder, PlaylistName));

        /// <summary>Why the stream stopped (ffmpeg exited or the pipe broke), or null while it runs.</summary>
        public string Failure => _failure;

        /// <summary>
        /// Starts ffmpeg (<paramref name="ffmpegPath"/>, "ffmpeg" to look on the PATH) writing into a
        /// fresh <paramref name="folder"/>, which it creates and the caller removes. Returns null with
        /// <paramref name="error"/> set if it can't run.
        /// </summary>
        public static ViewerStream Start(string ffmpegPath, string folder, out string error)
        {
            error = null;
            TcpListener listener = null;
            try
            {
                Directory.CreateDirectory(folder);
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int audioPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                var info = new ProcessStartInfo(ffmpegPath, string.Join(" ", Arguments(folder, audioPort)))
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false,
                    CreateNoWindow = true,
                };
                var process = Process.Start(info);
                if (process == null)
                {
                    error = "ffmpeg didn't start";
                    listener.Stop();
                    return null;
                }

                return new ViewerStream(process, folder, listener, new MusicFeed(ffmpegPath));
            }
            catch (Win32Exception)
            {
                error = NotFound(ffmpegPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is SocketException)
            {
                error = "Couldn't start ffmpeg: " + e.Message;
            }

            listener?.Stop();
            return null;
        }

        /// <summary>
        /// Whether <paramref name="ffmpegPath"/> runs (<c>-version</c>, within <paramref name="timeoutMs"/>),
        /// so the server only offers casting when it can (design doc §2.4). A program that doesn't
        /// answer in time is killed and counts as unavailable, so a wrong <c>-ffmpeg</c> can't hang start-up.
        /// </summary>
        public static bool IsAvailable(string ffmpegPath, out string error, int timeoutMs = 5_000)
        {
            error = null;
            try
            {
                var info = new ProcessStartInfo(ffmpegPath, "-hide_banner -version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        error = "ffmpeg didn't start";
                        return false;
                    }

                    // Drain the output as it comes, so the wait below is what bounds the check.
                    process.OutputDataReceived += (sender, e) => { };
                    process.BeginOutputReadLine();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // It ended just now.
                        }

                        error = $"ffmpeg didn't answer (\"{ffmpegPath} -version\" took over {timeoutMs / 1000} s)";
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        error = "ffmpeg didn't run (\"" + ffmpegPath + " -version\" failed)";
                        return false;
                    }
                }

                return true;
            }
            catch (Win32Exception)
            {
                error = NotFound(ffmpegPath);
            }
            catch (InvalidOperationException e)
            {
                error = "Couldn't run ffmpeg: " + e.Message;
            }

            return false;
        }

        /// <summary>
        /// ffmpeg's arguments: raw RGB frames on stdin and raw stereo PCM from a loopback TCP
        /// connection to <paramref name="audioPort"/>, encoded as H.264 and AAC into 1-second HLS
        /// segments, each starting with a keyframe, keeping the last six.
        /// </summary>
        public static IReadOnlyList<string> Arguments(string folder, int audioPort)
        {
            string size = ViewerRenderer.Width.ToString(CultureInfo.InvariantCulture) + "x" + ViewerRenderer.Height.ToString(CultureInfo.InvariantCulture);
            string fps = FramesPerSecond.ToString(CultureInfo.InvariantCulture);
            return new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", size, "-framerate", fps, "-i", "pipe:0",
                "-f", "s16le", "-ar", MusicFeed.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", MusicFeed.Channels.ToString(CultureInfo.InvariantCulture),
                "-i", "tcp://127.0.0.1:" + audioPort.ToString(CultureInfo.InvariantCulture),
                "-map", "0:v", "-map", "1:a",
                "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-pix_fmt", "yuv420p", "-profile:v", "main",
                "-g", fps, "-keyint_min", fps, "-sc_threshold", "0", "-b:v", "2500k", "-maxrate", "2500k", "-bufsize", "5000k",
                "-c:a", "aac", "-b:a", "64k",
                "-f", "hls", "-hls_time", "1", "-hls_list_size", "6",
                "-hls_flags", "delete_segments+independent_segments+temp_file+omit_endlist",
                "-hls_segment_filename", Quote(Path.Combine(folder, "seg%05d.ts")),
                Quote(Path.Combine(folder, PlaylistName)),
            };
        }

        /// <summary>What the TV shows from the next frame on.</summary>
        public void Show(ViewerScene scene)
        {
            lock (_lock)
            {
                _scene = scene;
            }
        }

        /// <summary>The last few lines ffmpeg wrote to its error output, for the log.</summary>
        public string RecentErrors()
        {
            lock (_errors)
            {
                return string.Join(" / ", _errors);
            }
        }

        private void Run()
        {
            var renderer = new ViewerRenderer();
            var frame = new RgbImage(ViewerRenderer.Width, ViewerRenderer.Height);
            var clock = Stopwatch.StartNew();
            var stdin = _ffmpeg.StandardInput.BaseStream;
            long frameIndex = 0;
            ViewerScene drawn = null;
            try
            {
                while (!_stopping)
                {
                    long due = frameIndex * 1000 / FramesPerSecond;
                    long wait = due - clock.ElapsedMilliseconds;
                    if (wait > 0)
                    {
                        Thread.Sleep((int)wait);
                        continue;
                    }

                    ViewerScene scene;
                    lock (_lock)
                    {
                        scene = _scene;
                    }

                    // Draw only when something changed and there's time; otherwise the last frame repeats.
                    if (scene != null && (scene != drawn || frameIndex == 0) && -wait <= MaxLagMs)
                    {
                        renderer.Render(scene, frame);
                        drawn = scene;
                    }

                    stdin.Write(frame.Pixels, 0, frame.Pixels.Length);
                    frameIndex++;
                }
            }
            catch (IOException e)
            {
                _failure = "ffmpeg stopped: " + (RecentErrors().Length > 0 ? RecentErrors() : e.Message);
            }
            catch (ObjectDisposedException)
            {
                // Stopped from Dispose.
            }
        }

        /// <summary>Feeds ffmpeg the sound 20 ms at a time, keeping pace with the clock.</summary>
        private void RunAudio()
        {
            const int chunkFrames = MusicFeed.SampleRate / 50;
            var pcm = new short[chunkFrames * MusicFeed.Channels];
            var bytes = new byte[pcm.Length * 2];
            try
            {
                using (var client = AcceptFfmpeg())
                {
                    _audioListener.Stop();
                    var stream = client.GetStream();
                    var clock = Stopwatch.StartNew();
                    long written = 0;
                    while (!_stopping)
                    {
                        long due = clock.ElapsedMilliseconds * MusicFeed.SampleRate / 1000;
                        if (written >= due)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        Music.Fill(pcm, chunkFrames);
                        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                        stream.Write(bytes, 0, bytes.Length);
                        written += chunkFrames;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                if (!_stopping)
                {
                    _failure = "ffmpeg stopped taking the sound: " + e.Message;
                }
            }
        }

        /// <summary>
        /// ffmpeg's connection for the sound. Every user on the host can find the port and reach it, so a connection
        /// that isn't a live one from this server's own user is hung up on, and the wait for ffmpeg's goes on.
        /// </summary>
        private TcpClient AcceptFfmpeg()
        {
            while (true)
            {
                var client = _audioListener.AcceptTcpClient();
                if (FromThisUser(client))
                {
                    return client;
                }

                client.Dispose();
            }
        }

        private static bool FromThisUser(TcpClient client)
        {
            try
            {
                return ConnectedBySameUser(File.ReadLines("/proc/net/tcp"), (IPEndPoint)client.Client.LocalEndPoint, (IPEndPoint)client.Client.RemoteEndPoint);
            }
            catch (SocketException)
            {
                // Hung up already, which ffmpeg doesn't.
                return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // No table to read, as off Linux, so there's no telling whose it is.
                return true;
            }
        }

        /// <summary>
        /// Whether the far end of the loopback connection from <paramref name="remote"/> to <paramref name="local"/>
        /// is still connected and belongs to the user who owns this end, going by <paramref name="table"/>, the lines
        /// of Linux's <c>/proc/net/tcp</c>. An end it doesn't list, such as an IPv6 socket's, is a stranger's. So is
        /// one already closed: the kernel lists it as root's whoever opened it, which would pass for ours on a server
        /// run as root.
        /// </summary>
        public static bool ConnectedBySameUser(IEnumerable<string> table, IPEndPoint local, IPEndPoint remote)
        {
            const string established = "01";
            string ours = TcpTableEndPoint(local);
            string theirs = TcpTableEndPoint(remote);
            string ourUser = null;
            string theirUser = null;
            foreach (string line in table)
            {
                // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid timeout inode ...
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 8)
                {
                    continue;
                }

                if (fields[1] == ours && fields[2] == theirs)
                {
                    ourUser = fields[7];
                }
                else if (fields[1] == theirs && fields[2] == ours && fields[3] == established)
                {
                    theirUser = fields[7];
                }
            }

            return ourUser != null && ourUser == theirUser;
        }

        /// <summary>An endpoint as <c>/proc/net/tcp</c> writes it: the address as a native-endian number, then the port, in hex.</summary>
        private static string TcpTableEndPoint(IPEndPoint endPoint) =>
            BitConverter.ToUInt32(endPoint.Address.GetAddressBytes(), 0).ToString("X8", CultureInfo.InvariantCulture) + ":" + endPoint.Port.ToString("X4", CultureInfo.InvariantCulture);

        public void Dispose()
        {
            _stopping = true;
            _audioListener.Stop();
            try
            {
                _ffmpeg.StandardInput.Close();
            }
            catch (IOException)
            {
                // Already gone.
            }

            _thread.Join(2_000);
            _audioThread.Join(2_000);
            Music.Dispose();
            try
            {
                if (!_ffmpeg.WaitForExit(3_000))
                {
                    _ffmpeg.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Never started or already reaped.
            }

            _ffmpeg.Dispose();
        }

        /// <summary>A path as one ffmpeg argument, for a command line joined with spaces.</summary>
        internal static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

        private static string NotFound(string ffmpegPath) => $"ffmpeg not found (\"{ffmpegPath}\"); install it, or point {ServerOptions.FfmpegFlag} at it";
    }
}
