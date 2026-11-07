using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CarRacer.Core.Audio;

namespace CarRacer.Core.Viewer
{
    /// <summary>
    /// The TV viewer's optional music (design doc §2.4) as 48 kHz stereo 16-bit PCM: the tune asked
    /// for, looping, decoded by its own <c>ffmpeg</c>. When it changes, the old and new tunes
    /// overlap, one fading out as the other fades in over <see cref="SoundRules.MusicFadeSeconds"/>
    /// (the phones' fade length; they fade out first and then start the new tune). Silence when
    /// none is asked for or it can't be decoded. <see cref="Play"/> may be called from any thread;
    /// <see cref="Fill"/> from one.
    /// </summary>
    public sealed class MusicFeed : IDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;

        /// <summary>The music sits under the TV's volume, a little quieter than full scale.</summary>
        public const float Gain = 0.8f;

        private static readonly int FadeFrames = (int)(SoundRules.MusicFadeSeconds * SampleRate);

        private readonly object _lock = new object();
        private readonly string _ffmpegPath;
        private string _wanted;
        private string _playing;
        private Decoder _current;
        private Decoder _next;
        private int _fadeFrame;
        private short[] _a = new short[0];
        private short[] _b = new short[0];

        public MusicFeed(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;
        }

        /// <summary>The last decoding error, for the log, or null.</summary>
        public string Error { get; private set; }

        /// <summary>Plays the tune in <paramref name="file"/> (an OGG or anything ffmpeg reads), or silence for null.</summary>
        public void Play(string file)
        {
            lock (_lock)
            {
                _wanted = file;
            }
        }

        /// <summary>Writes the next <paramref name="frames"/> frames (two samples each) into <paramref name="pcm"/>.</summary>
        public void Fill(short[] pcm, int frames)
        {
            string wanted;
            lock (_lock)
            {
                wanted = _wanted;
            }

            if (wanted != _playing && _next == null && _fadeFrame == 0)
            {
                // Start fading towards the new tune (or towards silence).
                _playing = wanted;
                string error = null;
                _next = wanted != null ? Decoder.Start(_ffmpegPath, wanted, out error) : null;
                Error = error ?? Error;
                _fadeFrame = 1;
            }

            int samples = frames * Channels;
            if (_a.Length < samples)
            {
                _a = new short[samples];
                _b = new short[samples];
            }

            Read(_current, _a, frames);
            if (_fadeFrame == 0)
            {
                Mix(_a, null, pcm, frames, 0, 1);
                return;
            }

            Read(_next, _b, frames);
            Mix(_a, _b, pcm, frames, _fadeFrame, FadeFrames);
            _fadeFrame += frames;
            if (_fadeFrame >= FadeFrames)
            {
                _current?.Dispose();
                _current = _next;
                _next = null;
                _fadeFrame = 0;
            }
        }

        /// <summary>
        /// Mixes <paramref name="from"/> fading out with <paramref name="to"/> fading in (null for
        /// silence) into <paramref name="output"/>, <paramref name="fadeFrame"/> frames into a fade of
        /// <paramref name="fadeLength"/>; outside a fade (<paramref name="fadeFrame"/> 0) it is just
        /// <paramref name="from"/>. Applies <see cref="Gain"/> and clips.
        /// </summary>
        public static void Mix(short[] from, short[] to, short[] output, int frames, int fadeFrame, int fadeLength)
        {
            for (int f = 0; f < frames; f++)
            {
                float t = fadeFrame == 0 ? 0f : Math.Min(1f, (fadeFrame + f) / (float)fadeLength);
                for (int c = 0; c < Channels; c++)
                {
                    int i = f * Channels + c;
                    float sample = from[i] * (1f - t) + (to != null ? to[i] * t : 0f);
                    output[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(sample * Gain)));
                }
            }
        }

        private void Read(Decoder decoder, short[] buffer, int frames)
        {
            if (decoder == null || !decoder.Read(buffer, frames))
            {
                Array.Clear(buffer, 0, frames * Channels);
                if (decoder != null && decoder.Error != null)
                {
                    Error = decoder.Error;
                }
            }
        }

        public void Dispose()
        {
            _current?.Dispose();
            _next?.Dispose();
            _current = _next = null;
        }

        /// <summary>One tune decoded by ffmpeg to raw PCM, looping for ever.</summary>
        private sealed class Decoder : IDisposable
        {
            private readonly Process _process;
            private readonly Stream _output;
            private readonly byte[] _bytes = new byte[SampleRate / 10 * Channels * 2];

            private Decoder(Process process)
            {
                _process = process;
                _output = process.StandardOutput.BaseStream;
            }

            public string Error { get; private set; }

            public static Decoder Start(string ffmpegPath, string file, out string error)
            {
                error = null;
                if (!File.Exists(file))
                {
                    error = "No music file " + file;
                    return null;
                }

                try
                {
                    var info = new ProcessStartInfo(ffmpegPath, $"-hide_banner -loglevel error -nostdin -stream_loop -1 -i {ViewerStream.Quote(file)} -f s16le -ar {SampleRate} -ac {Channels} pipe:1")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    };
                    var process = Process.Start(info);
                    if (process != null)
                    {
                        return new Decoder(process);
                    }

                    error = "ffmpeg didn't start to decode " + file;
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    error = "Couldn't decode " + file + ": " + e.Message;
                }

                return null;
            }

            /// <summary>Fills <paramref name="buffer"/> with the next frames; false (and silence) once the decoder has ended.</summary>
            public bool Read(short[] buffer, int frames)
            {
                int wanted = frames * Channels * 2;
                int read = 0;
                try
                {
                    while (read < wanted)
                    {
                        int n = _output.Read(_bytes, 0, Math.Min(_bytes.Length, wanted - read));
                        if (n <= 0)
                        {
                            Error = "The music decoder stopped";
                            return false;
                        }

                        Buffer.BlockCopy(_bytes, 0, buffer, read, n);
                        read += n;
                    }
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException)
                {
                    Error = "The music decoder stopped: " + e.Message;
                    return false;
                }

                return true;
            }

            public void Dispose()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                _process.Dispose();
            }
        }
    }
}
