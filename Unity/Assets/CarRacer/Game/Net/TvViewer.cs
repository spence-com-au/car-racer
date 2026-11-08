using System.Collections.Generic;
using System.IO;
using CarRacer.Core.Arena;
using CarRacer.Core.Audio;
using CarRacer.Core.Cast;
using CarRacer.Core.Discovery;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Viewer;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The TV viewer on a server (design doc §2.4): finds Chromecasts on the network, and while a TV
    /// is wanted (<c>-cast</c>, or the room leader's Cast to TV) streams the room's event as video
    /// with ffmpeg, serves it over HTTP and casts it, trying again at each countdown if nothing is
    /// cast. It tells every phone what it found and how the cast is going (<see cref="TvStatusMessage"/>),
    /// and logs the same. Everything slow runs on background threads.
    /// </summary>
    public sealed class TvViewer : MonoBehaviour
    {
        /// <summary>The folder beside the server that the build copies the tunes into, for ffmpeg to play.</summary>
        public const string MusicFolder = "Music";

        private readonly TvCastPlan _plan = new TvCastPlan();
        private ServerOptions _options;
        private NetworkSession _session;
        private NetworkedCars _cars;
        private CastDiscovery _discovery;
        private ViewerHttpServer _http;
        private ViewerStream _stream;
        private string _streamFolder;
        private CastConnection _cast;
        private CastState? _loggedState;
        private RacePhase _phase = RacePhase.Lobby;
        private string _joinAddress;
        private float _nextFrameAt;
        private bool _streamFailed;
        private string _musicFolder;
        private string _musicError;

        /// <summary>The Chromecasts found lately.</summary>
        public IReadOnlyList<CastDevice> Devices => _discovery != null ? _discovery.Devices : (IReadOnlyList<CastDevice>)new List<CastDevice>();

        public static TvViewer Create(GameObject owner, ServerOptions options, NetworkSession session, NetworkedCars cars)
        {
            var viewer = owner.AddComponent<TvViewer>();
            viewer._options = options;
            viewer._session = session;
            viewer._cars = cars;
            viewer.Music = options.TvMusic;
            viewer._musicFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", MusicFolder));
            if (!ViewerStream.IsAvailable(options.FfmpegPath, out string ffmpegError))
            {
                Debug.LogWarning("TV viewer off: " + ffmpegError);
                viewer._streamFailed = true;
            }

            session.HostSide.CastCommandReceived += viewer.OnCastCommand;
            var addresses = HostAddress.LocalIPv4Addresses();
            viewer._joinAddress = addresses.Count > 0 ? addresses[0] : null;
            try
            {
                viewer._discovery = new CastDiscovery();
                viewer._discovery.Found += device => Debug.Log("Found a TV: " + ServerLog.Escape(device.ToString()));
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Debug.LogWarning("Can't look for TVs: " + e.Message);
            }

            if (options.CastTo != null)
            {
                viewer.CastTo(options.CastTo, takeOver: false);
            }

            return viewer;
        }

        /// <summary>
        /// Casts to <paramref name="wanted"/> (a name, id or address; empty for the first TV found),
        /// switching from any TV being cast to. <paramref name="takeOver"/> replaces whatever the TV shows.
        /// </summary>
        private void CastTo(string wanted, bool takeOver)
        {
            if (_plan.Request(wanted, takeOver, _cast?.Device, _cast?.State))
            {
                _cast.Stop();
            }

            Debug.Log(string.IsNullOrEmpty(wanted) ? "Casting to the first TV found" : $"Casting to \"{ServerLog.Escape(wanted)}\"");
        }

        private void OnCastCommand(CastCommandMessage command)
        {
            switch (command.Command)
            {
                case CastCommand.Stop:
                    Debug.Log("The room leader stopped casting");
                    StopCasting();
                    break;
                case CastCommand.Cast when !_streamFailed:
                    Debug.Log($"The room leader picked \"{ServerLog.Escape(command.Device)}\"");
                    CastTo(command.Device, takeOver: true);
                    break;
                case CastCommand.MusicOn:
                case CastCommand.MusicOff:
                    Music = command.Command == CastCommand.MusicOn;
                    Debug.Log(Music ? "TV music on" : "TV music off");
                    break;
            }
        }

        /// <summary>Whether the TV plays the music (design doc §2.4).</summary>
        public bool Music { get; set; }

        /// <summary>Stops casting and streaming until asked again.</summary>
        private void StopCasting()
        {
            _plan.Forget();
            _cast?.Stop();
        }

        private void Update()
        {
            long now = NetworkSession.NowMs;
            _discovery?.Poll(now);
            bool casting = _cast != null && !_cast.Finished;
            if (_plan.Wanted != null && _stream == null && !_streamFailed)
            {
                StartStreaming();
            }

            var race = _session.Race;
            var phase = race != null ? race.Phase : RacePhase.Lobby;
            if (phase == RacePhase.Countdown && _phase != RacePhase.Countdown)
            {
                _plan.EventStarting(casting);
            }

            _phase = phase;
            if (_stream != null)
            {
                FeedStream(now);
            }

            var device = _plan.Next(Devices, casting, _stream != null && _stream.Ready, out bool takeOver);
            if (device != null)
            {
                _cast?.Dispose();
                Debug.Log($"Casting to {ServerLog.Escape(device.Name)}{(takeOver ? " (taking over)" : string.Empty)}");
                // The cast thread asks for the URL after connecting; StopStreaming may have cleared _http by then.
                var http = _http;
                _cast = new CastConnection(device, address => http.UrlFor(address), _options.Name, takeOver);
                _loggedState = null;
            }

            LogCastState();
            _session.HostSide.SetTvStatus(Status());
            if (_stream != null && _plan.Wanted == null && (_cast == null || _cast.Finished))
            {
                StopStreaming();
            }
        }

        private void FeedStream(long now)
        {
            if (_stream.Failure != null)
            {
                GiveUp("TV viewer stopped: " + _stream.Failure);
                return;
            }

            if (Time.unscaledTime < _nextFrameAt)
            {
                return;
            }

            _nextFrameAt = Time.unscaledTime + 1f / ViewerStream.FramesPerSecond;
            PlayMusic();
            _stream.Show(ViewerScene.Build(new ViewerScene.Inputs
            {
                ServerName = _options.Name,
                Players = _session.Players,
                LeaderId = _session.LeaderId,
                Settings = _session.Settings,
                Race = _session.Race,
                Positions = _cars.TryGetCarPosition,
                NowMs = now,
                JoinAddress = _joinAddress,
                Port = _session.Port,
            }));
        }

        /// <summary>The tune the phones would play (<see cref="MusicChoice"/>), on the TV while its music is on.</summary>
        private void PlayMusic()
        {
            var race = _session.Race;
            bool inEvent = race != null && race.Phase != RacePhase.Lobby;
            string tune = MusicChoice.For(inEvent, race != null ? race.Mode : GameMode.Race, race != null ? race.TrackIndex : 0);
            _stream.Music.Play(Music ? Path.Combine(_musicFolder, tune + ".ogg") : null);
            string error = _stream.Music.Error;
            if (error != null && error != _musicError)
            {
                Debug.LogWarning("TV music: " + error);
                _musicError = error;
            }
        }

        private void StartStreaming()
        {
            // A name no one can guess, so another user on this host can't create the folder in the shared temp
            // directory first and either block the stream or serve the TV their own playlist from it.
            _streamFolder = Path.Combine(Path.GetTempPath(), "car-racer-viewer-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                _http = new ViewerHttpServer(_streamFolder, _options.ViewerPort);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                GiveUp($"TV viewer off: can't serve on TCP port {_options.ViewerPort} ({e.Message}); choose another with {ServerOptions.ViewerPortFlag}");
                return;
            }

            _stream = ViewerStream.Start(_options.FfmpegPath, _streamFolder, out string error);
            if (_stream == null)
            {
                GiveUp("TV viewer off: " + error);
                return;
            }

            Debug.Log($"TV viewer streaming on port {_http.Port}");
        }

        /// <summary>
        /// Streaming can't work here any more: stops it, and any cast with it, and forgets the TV,
        /// so the phones go back to no TV rather than a cast nobody can stop (the leader's button
        /// goes once casting is off).
        /// </summary>
        private void GiveUp(string reason)
        {
            Debug.LogError(reason);
            _streamFailed = true;
            _plan.Forget();
            _cast?.Stop();
            StopStreaming();
        }

        private void StopStreaming()
        {
            _stream?.Dispose();
            _stream = null;
            _http?.Dispose();
            _http = null;
            if (_streamFolder != null && Directory.Exists(_streamFolder))
            {
                try
                {
                    Directory.Delete(_streamFolder, recursive: true);
                }
                catch (IOException e)
                {
                    Debug.LogWarning($"Couldn't remove the TV stream's folder {_streamFolder}: {e.Message}");
                }
            }

            _streamFolder = null;
        }

        private void LogCastState()
        {
            if (_cast == null)
            {
                return;
            }

            var state = _cast.State;
            if (state == _loggedState)
            {
                return;
            }

            _loggedState = state;
            string tv = ServerLog.Escape(_cast.Device.Name);
            string detail = ServerLog.Escape(_cast.Detail);
            switch (state)
            {
                case CastState.Playing:
                    Debug.Log($"On TV: {tv}");
                    break;
                case CastState.Busy:
                case CastState.Stopped:
                    Debug.Log($"Not casting to {tv}: {detail}");
                    break;
                case CastState.Failed:
                    Debug.LogWarning($"Casting to {tv} failed: {detail}");
                    break;
            }
        }

        /// <summary>What the phones are told: whether casting works here, the TVs found and how the cast is going.</summary>
        private TvStatusMessage Status()
        {
            var names = new List<string>();
            foreach (var device in Devices)
            {
                names.Add(device.Name);
            }

            return _plan.Status(!_streamFailed && _discovery != null, Music, names, _cast?.State, _cast?.Device.Name, _cast?.Detail);
        }

        private void OnDestroy()
        {
            if (_session != null && _session.HostSide != null)
            {
                _session.HostSide.CastCommandReceived -= OnCastCommand;
            }

            _cast?.Dispose();
            StopStreaming();
            _discovery?.Dispose();
        }
    }
}
