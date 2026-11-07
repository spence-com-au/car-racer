using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;

namespace CarRacer.Core.Viewer
{
    /// <summary>Which screen the TV viewer shows.</summary>
    public enum ViewerPhase
    {
        Lobby,
        Countdown,
        Racing,
        Results,
    }

    /// <summary>A car on the viewer's map.</summary>
    public struct ViewerCar
    {
        public byte PlayerId;
        public Rgb Colour;
        public Vec3 Position;

        /// <summary>Its place in the standings (1 for the leader), or 0 if it has none yet.</summary>
        public int Place;
    }

    /// <summary>A line of the viewer's table: the lobby list, the standings or the results.</summary>
    public struct ViewerRow
    {
        /// <summary>1-based place, or 0 to show none (the lobby).</summary>
        public int Place;
        public string Name;
        public Rgb Colour;

        /// <summary>What's shown at the right of the row: "Lap 2/3", "Ready", "1:23.45", "Out"…</summary>
        public string Detail;

        /// <summary>Drawn dimmer: a car that is out, or a bot in the lobby.</summary>
        public bool Dim;
    }

    /// <summary>
    /// Everything one frame of the TV viewer shows (design doc §2.4), made by <see cref="Build"/>
    /// from the host's session and drawn by <see cref="ViewerRenderer"/>. Once built it isn't changed,
    /// so it can be handed to the thread that draws and encodes frames.
    /// </summary>
    public sealed class ViewerScene
    {
        public ViewerPhase Phase;
        public GameMode Mode;
        public int TrackIndex;

        /// <summary>Sumo: the platform's radius now.</summary>
        public float PlatformRadius = SumoRules.StartRadius;

        /// <summary>The headline: the server's name in the lobby, the event under way, or "Results".</summary>
        public string Title = string.Empty;

        /// <summary>Top right: the race clock, the time left, or the player count in the lobby.</summary>
        public string Clock = string.Empty;

        /// <summary>The bottom line: what's next, or the rules.</summary>
        public string Footer = string.Empty;

        /// <summary>"3", "2", "1" or "GO!" over the map, or null.</summary>
        public string Countdown;

        /// <summary>Lobby: the link a phone's camera opens to join (design doc §1.1), drawn as a QR code; null for none.</summary>
        public string JoinLink;

        /// <summary>Lobby: how to join by hand ("Join 192.168.1.20").</summary>
        public string JoinText = string.Empty;

        public List<ViewerRow> Rows = new List<ViewerRow>();
        public List<ViewerCar> Cars = new List<ViewerCar>();

        /// <summary>The host's state the scene is made from.</summary>
        public struct Inputs
        {
            public string ServerName;
            public IReadOnlyList<RosterEntry> Players;
            public byte LeaderId;
            public LobbySettings Settings;

            /// <summary>The host's race; null before it exists.</summary>
            public IRaceView Race;

            /// <summary>Where each car is now; null for no map.</summary>
            public CarPositionLookup Positions;

            /// <summary>Now in the host's clock, the clock the race's start time is in.</summary>
            public long NowMs;

            /// <summary>An address phones can join at, for the lobby's QR code; null for none.</summary>
            public string JoinAddress;
            public ushort Port;
        }

        /// <summary>Makes the scene for the session as it is now.</summary>
        public static ViewerScene Build(Inputs inputs)
        {
            var scene = new ViewerScene();
            var players = inputs.Players ?? Array.Empty<RosterEntry>();
            var race = inputs.Race;
            var phase = race == null ? RacePhase.Lobby : race.Phase;
            if (phase == RacePhase.Lobby)
            {
                BuildLobby(scene, inputs, players);
                return scene;
            }

            scene.Mode = race.Mode;
            scene.TrackIndex = TrackCatalog.Clamp(race.TrackIndex);
            long sinceStartMs = inputs.NowMs - race.StartAtLocalMs;
            scene.PlatformRadius = SumoRules.PlatformRadius(Math.Max(0, sinceStartMs) / 1000f);
            scene.Footer = EventText.RulesNote(race.Rules);
            var names = new Dictionary<byte, RosterEntry>();
            foreach (var p in players)
            {
                names[p.PlayerId] = p;
            }

            if (phase == RacePhase.Results && race.Results != null)
            {
                scene.Phase = ViewerPhase.Results;
                scene.Title = "Results: " + EventText.EventName(race.Mode, scene.TrackIndex);
                foreach (var result in race.Results.Results)
                {
                    scene.Rows.Add(new ViewerRow
                    {
                        Place = result.Position,
                        Name = result.Name,
                        Colour = ColourOf(names, result.PlayerId),
                        Detail = EventText.ResultDetail(race.Mode, result),
                        Dim = result.DidNotFinish,
                    });
                }

                return scene;
            }

            scene.Phase = phase == RacePhase.Countdown ? ViewerPhase.Countdown : ViewerPhase.Racing;
            scene.Title = EventText.EventName(race.Mode, scene.TrackIndex);
            scene.Clock = ClockText(race.Mode, sinceStartMs);
            scene.Countdown = EventText.Countdown(phase, -sinceStartMs);

            var places = new Dictionary<byte, int>();
            if (race.Standings != null && race.Standings.Order.Count > 0)
            {
                int place = 1;
                foreach (var entry in race.Standings.Order)
                {
                    places[entry.PlayerId] = place;
                    names.TryGetValue(entry.PlayerId, out var player);
                    scene.Rows.Add(new ViewerRow
                    {
                        Place = place++,
                        Name = player.Name ?? "?",
                        Colour = CarPalette.ForIndex(player.ColourIndex),
                        Detail = StandingDetail(race.Mode, race.LapCount, entry),
                        Dim = race.Mode != GameMode.Race && race.Mode != GameMode.Monster && entry.FinishPosition > 0,
                    });
                }
            }
            else
            {
                // Before the first standings (the countdown): everyone in grid order.
                var grid = new List<RosterEntry>(players);
                grid.Sort((a, b) => race.GridSlotOf(a.PlayerId).CompareTo(race.GridSlotOf(b.PlayerId)));
                foreach (var p in grid)
                {
                    scene.Rows.Add(new ViewerRow { Name = p.Name, Colour = CarPalette.ForIndex(p.ColourIndex), Detail = p.IsBot ? "bot" : string.Empty });
                }
            }

            if (inputs.Positions != null)
            {
                foreach (var p in players)
                {
                    if (inputs.Positions(p.PlayerId, out var position))
                    {
                        places.TryGetValue(p.PlayerId, out int place);
                        scene.Cars.Add(new ViewerCar { PlayerId = p.PlayerId, Colour = CarPalette.ForIndex(p.ColourIndex), Position = position, Place = place });
                    }
                }
            }

            return scene;
        }

        private static void BuildLobby(ViewerScene scene, Inputs inputs, IReadOnlyList<RosterEntry> players)
        {
            scene.Phase = ViewerPhase.Lobby;
            scene.Mode = inputs.Settings.Mode;
            scene.TrackIndex = TrackCatalog.Clamp(inputs.Settings.Track);
            scene.Title = string.IsNullOrEmpty(inputs.ServerName) ? "Car Racer" : inputs.ServerName;
            int humans = 0;
            foreach (var p in players)
            {
                if (p.IsBot)
                {
                    continue;
                }

                humans++;
                string detail = p.PlayerId == inputs.LeaderId ? "runs the room" : p.Ready ? "Ready" : string.Empty;
                scene.Rows.Add(new ViewerRow { Name = p.Name, Colour = CarPalette.ForIndex(p.ColourIndex), Detail = detail });
            }

            scene.Clock = humans == 1 ? "1 player" : humans + " players";
            scene.Footer = "Next: " + EventText.SettingsSummary(inputs.Settings);
            if (!string.IsNullOrEmpty(inputs.JoinAddress))
            {
                scene.JoinLink = Discovery.JoinLink.Make(inputs.JoinAddress, inputs.Port);
                scene.JoinText = inputs.Port == Discovery.DiscoveryConstants.DefaultGamePort
                    ? "Join " + inputs.JoinAddress
                    : $"Join {inputs.JoinAddress}:{inputs.Port}";
            }
        }

        /// <summary>The time since the start, or in Battle and Monster Jam the time left.</summary>
        public static string ClockText(GameMode mode, long sinceStartMs)
        {
            long elapsed = Math.Max(0, sinceStartMs);
            long limit = mode == GameMode.Battle ? BattleRules.TimeLimitMs : mode == GameMode.Monster ? MonsterRules.TimeLimitMs : 0;
            return RaceFormat.Clock(limit > 0 ? Math.Max(0, limit - elapsed) : elapsed);
        }

        private static string StandingDetail(GameMode mode, int lapCount, StandingsEntry entry)
        {
            switch (mode)
            {
                case GameMode.Monster:
                    return entry.Points + " pts";
                case GameMode.Sumo:
                    return entry.FinishPosition > 0 ? "Out" : string.Empty;
                case GameMode.Battle:
                    return entry.FinishPosition > 0 ? "Out" : entry.LapsCompleted == 1 ? "1 life" : entry.LapsCompleted + " lives";
                default:
                    return entry.FinishPosition > 0 ? "Finished" : $"Lap {Math.Min(entry.LapsCompleted + 1, Math.Max(1, lapCount))}/{lapCount}";
            }
        }

        private static Rgb ColourOf(Dictionary<byte, RosterEntry> players, byte playerId) =>
            players.TryGetValue(playerId, out var p) ? CarPalette.ForIndex(p.ColourIndex) : new Rgb(128, 128, 128);
    }
}
