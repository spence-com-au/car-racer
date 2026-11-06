using System.Collections.Generic;
using System.IO;
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
        private readonly TvCastPlan _plan = new TvCastPlan();
        private ServerOptions _options;
        private NetworkSession _session;
        private NetworkedCars _cars;
        private CastDiscovery _discovery;
        private ViewerHttpServer _http;
        private ViewerStream _stream;
        private CastConnection _cast;
        private CastState? _loggedState;
        private RacePhase _phase = RacePhase.Lobby;
        private string _joinAddress;
        private float _nextFrameAt;
        private bool _streamFailed;

        /// <summary>The Chromecasts found lately.</summary>
        public IReadOnlyList<CastDevice> Devices => _discovery != null ? _discovery.Devices : (IReadOnlyList<CastDevice>)new List<CastDevice>();

        /// <summary>The current or last cast, or null.</summary>
        public CastConnection Cast => _cast;

        /// <summary>Whether streaming can't work here (no ffmpeg, or the port is taken).</summary>
        public bool Unavailable => _streamFailed;

        public static TvViewer Create(GameObject owner, ServerOptions options, NetworkSession session, NetworkedCars cars)
        {
            var viewer = owner.AddComponent<TvViewer>();
            viewer._options = options;
            viewer._session = session;
            viewer._cars = cars;
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
                viewer._discovery.Found += device => Debug.Log("Found a TV: " + device);
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
        public void CastTo(string wanted, bool takeOver)
        {
            if (_cast != null && !_cast.Finished && !_cast.Device.Matches(wanted ?? string.Empty))
            {
                _cast.Stop();
            }

            _plan.Request(wanted, takeOver);
            Debug.Log(string.IsNullOrEmpty(wanted) ? "Casting to the first TV found" : $"Casting to \"{wanted}\"");
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
                    Debug.Log($"The room leader picked \"{command.Device}\"");
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
        public void StopCasting()
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
                Debug.Log($"Casting to {device.Name}{(takeOver ? " (taking over)" : string.Empty)}");
                _cast = new CastConnection(device, address => _http.UrlFor(address), _options.Name, takeOver);
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
                Debug.LogError("TV viewer stopped: " + _stream.Failure);
                StopStreaming();
                _streamFailed = true;
                return;
            }

            if (Time.unscaledTime < _nextFrameAt)
            {
                return;
            }

            _nextFrameAt = Time.unscaledTime + 1f / ViewerStream.FramesPerSecond;
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

        private void StartStreaming()
        {
            string folder = Path.Combine(Path.GetTempPath(), "car-racer-viewer-" + _session.Port);
            try
            {
                _http = new ViewerHttpServer(folder, _options.ViewerPort);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Debug.LogError($"TV viewer off: can't serve on TCP port {_options.ViewerPort} ({e.Message}); choose another with {ServerOptions.ViewerPortFlag}");
                _streamFailed = true;
                return;
            }

            _stream = ViewerStream.Start(_options.FfmpegPath, folder, out string error);
            if (_stream == null)
            {
                Debug.LogError("TV viewer off: " + error);
                _http.Dispose();
                _http = null;
                _streamFailed = true;
                return;
            }

            Debug.Log($"TV viewer streaming on port {_http.Port}");
        }

        private void StopStreaming()
        {
            _stream?.Dispose();
            _stream = null;
            _http?.Dispose();
            _http = null;
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
            switch (state)
            {
                case CastState.Playing:
                    Debug.Log($"On TV: {_cast.Device.Name}");
                    break;
                case CastState.Busy:
                case CastState.Stopped:
                    Debug.Log($"Not casting to {_cast.Device.Name}: {_cast.Detail}");
                    break;
                case CastState.Failed:
                    Debug.LogWarning($"Casting to {_cast.Device.Name} failed: {_cast.Detail}");
                    break;
            }
        }

        /// <summary>What the phones are told: whether casting works here, the TVs found and how the cast is going.</summary>
        private TvStatusMessage Status()
        {
            var status = new TvStatusMessage { CanCast = !_streamFailed && _discovery != null, Music = Music };
            foreach (var device in Devices)
            {
                if (status.Devices.Count < TvStatusMessage.MaxDevices && !status.Devices.Contains(device.Name))
                {
                    status.Devices.Add(device.Name);
                }
            }

            if (_cast != null)
            {
                status.CastingTo = _cast.Device.Name;
                status.Detail = _cast.Detail;
                switch (_cast.State)
                {
                    case CastState.Playing:
                        status.State = TvState.Playing;
                        break;
                    case CastState.Busy:
                        status.State = TvState.Busy;
                        break;
                    case CastState.Failed:
                        status.State = TvState.Failed;
                        break;
                    case CastState.Stopped:
                        status.State = TvState.Off;
                        break;
                    default:
                        status.State = TvState.Connecting;
                        break;
                }
            }

            // Waiting for the stream or the TV to turn up counts as connecting to it.
            if (_plan.Wanted != null && (_cast == null || _cast.Finished) && status.State != TvState.Busy && status.State != TvState.Failed && _plan.Pending)
            {
                status.State = TvState.Connecting;
                status.CastingTo = _plan.Wanted.Length > 0 ? _plan.Wanted : status.CastingTo;
                status.Detail = string.Empty;
            }

            return status;
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
