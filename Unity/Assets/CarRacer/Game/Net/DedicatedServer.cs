using System;
using System.Text;
using CarRacer.Core.Discovery;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Game.Screens;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// Runs the game as a dedicated host on a server (design doc §2.3): hosts with no player of its
    /// own on the track screen's server mode, writes who joins, leaves and leads the room and each
    /// event's start and end to the log (stdout with <c>-logFile -</c>), and exits if hosting fails
    /// so a service manager or Docker can restart it.
    /// </summary>
    public sealed class DedicatedServer : MonoBehaviour
    {
        private TrackScreen _screen;
        private NetworkSession _session;
        private string _lastRoster;
        private RacePhase _lastPhase = RacePhase.Lobby;

        /// <summary>Starts hosting with <paramref name="options"/>; returns null (after logging why) if the options are bad.</summary>
        public static DedicatedServer Run(ServerOptions options)
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            if (options.Error != null)
            {
                Debug.LogError(options.Error);
                Application.Quit(2);
                return null;
            }

            // A steady 60 Hz tick for car states and the referee; nothing is drawn.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;

            var server = new GameObject(nameof(DedicatedServer)).AddComponent<DedicatedServer>();
            server._screen = TrackScreen.CreateServer(() => NetworkSession.StartServer(options.Name, options.Port));
            server._session = server._screen.Session;
            TvViewer.Create(server.gameObject, options, server._session, server._screen.GetComponent<NetworkedCars>());
            Debug.Log($"Car Racer server \"{options.Name}\" on port {options.Port} (protocol {DiscoveryConstants.ProtocolVersion})");
            return server;
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            if (_session.Ended)
            {
                Debug.LogError("Hosting stopped: " + _session.Status);
                _session = null;
                Application.Quit(1);
                return;
            }

            LogRosterChanges();
            LogPhaseChanges();
        }

        private void LogRosterChanges()
        {
            var text = new StringBuilder();
            byte leader = _session.LeaderId;
            foreach (var p in _session.Players)
            {
                text.Append(text.Length > 0 ? ", " : string.Empty).Append(p.Name);
                if (p.IsBot)
                {
                    text.Append(" (bot)");
                }
                else if (p.PlayerId == leader)
                {
                    text.Append(" (leader)");
                }
            }

            string roster = text.Length > 0 ? text.ToString() : "nobody";
            if (roster != _lastRoster)
            {
                Debug.Log("Players: " + roster);
                _lastRoster = roster;
            }
        }

        private void LogPhaseChanges()
        {
            var race = _session.Race;
            if (race == null || race.Phase == _lastPhase)
            {
                return;
            }

            if (race.Phase == RacePhase.Countdown)
            {
                Debug.Log($"Starting {race.Mode} (track {race.TrackIndex}, {race.LapCount} laps)");
            }
            else if (race.Phase == RacePhase.Results && race.Results != null)
            {
                string winner = null;
                int best = int.MaxValue;
                foreach (var result in race.Results.Results)
                {
                    if (result.Position < best)
                    {
                        best = result.Position;
                        winner = result.Name;
                    }
                }

                Debug.Log(winner != null ? "Finished; won by " + winner : "Finished");
            }

            _lastPhase = race.Phase;
        }

        private void OnDestroy()
        {
            if (_screen != null)
            {
                Destroy(_screen.gameObject);
            }
        }

        /// <summary>The process's arguments, for <see cref="ServerOptions.Parse"/>.</summary>
        public static string[] CommandLine()
        {
            try
            {
                return Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
