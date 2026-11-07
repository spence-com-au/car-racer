using System.Collections.Generic;
using System.IO;
using CarRacer.Core.Cast;
using CarRacer.Core.Discovery;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Viewer;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The TV viewer on a server (design doc §2.4): finds Chromecasts on the network, and while a TV
    /// is wanted (<c>-cast</c>, or <see cref="CastTo"/>) streams the room's event as video with
    /// ffmpeg, serves it over HTTP and casts it, trying again at each countdown if nothing is cast.
    /// Logs what it finds and how each cast goes. Everything slow runs on background threads.
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
            bool casting = _cast != null && !_cast.Finished;
            bool same = casting && _cast.Device.Matches(wanted ?? string.Empty);
            if (casting && !same)
            {
                _cast.Stop();
            }

            _plan.Request(wanted, takeOver, alreadyShowing: same);
            Debug.Log(string.IsNullOrEmpty(wanted) ? "Casting to the first TV found" : $"Casting to \"{wanted}\"");
        }

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
                // The cast thread asks for the URL after connecting; StopStreaming may have cleared _http by then.
                var http = _http;
                _cast = new CastConnection(device, address => http.UrlFor(address), _options.Name, takeOver);
                _loggedState = null;
            }

            LogCastState();
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

        private void OnDestroy()
        {
            _cast?.Dispose();
            StopStreaming();
            _discovery?.Dispose();
        }
    }
}
