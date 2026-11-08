using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;

namespace CarRacer.Core.Race
{
    public enum RacePhase
    {
        Lobby,
        Countdown,
        Racing,
        Results,
    }

    /// <summary>What the race screen needs, the same on the host and on guests.</summary>
    public interface IRaceView
    {
        RacePhase Phase { get; }
        int LapCount { get; }

        /// <summary>Race, Sumo, Battle or Monster; set by each countdown.</summary>
        GameMode Mode { get; }

        /// <summary>Which track a race is on (an index into the game's track list); set by each countdown.</summary>
        int TrackIndex { get; }

        /// <summary>The host's rules for the current event, as they apply in its mode; set by each countdown.</summary>
        EventRules Rules { get; }

        /// <summary>The start slot (grid slot, or spot round an arena) of <paramref name="playerId"/> in the current event.</summary>
        int GridSlotOf(byte playerId);

        /// <summary>When the race starts (or started), in this phone's clock.</summary>
        long StartAtLocalMs { get; }

        /// <summary>Latest race order from the host; null before the first update of a race.</summary>
        StandingsMessage Standings { get; }

        /// <summary>Final results; null until the race ends.</summary>
        RaceResultsMessage Results { get; }

        /// <summary>Call every frame.</summary>
        void Tick();

        /// <summary>This phone's car crossed checkpoint <paramref name="index"/> (from <see cref="CheckpointDetector"/>).</summary>
        void ReportOwnCheckpoint(int index);

        /// <summary>
        /// Monster Jam (design doc §7.0): this phone's truck landed a jump of <paramref name="airMs"/>
        /// (<see cref="StuntKind.Air"/>) or crushed <paramref name="victimId"/>'s truck (<see cref="StuntKind.Crush"/>).
        /// </summary>
        void ReportOwnStunt(StuntKind kind, long airMs, byte victimId);
    }

    /// <summary>
    /// The host's race referee (design doc §2, §7.1): starts a synced countdown, validates every
    /// player's checkpoints with a <see cref="RaceTracker"/>, announces finishers, broadcasts
    /// standings, ends the race when everyone is home or 30 s after the winner, and reopens the lobby.
    /// In Sumo and Battle (§7.0) it referees with an <see cref="EliminationTracker"/> instead: in
    /// Sumo it puts out any car off the shrinking platform, in Battle each spin-out costs a life,
    /// and the event ends when at most one car is left (or Battle's time runs out). Arena standings
    /// carry each player's lives in <see cref="StandingsEntry.LapsCompleted"/> and, once they are
    /// out, their place in <see cref="StandingsEntry.FinishPosition"/>; results carry how long each
    /// lasted as the race time. In Monster Jam it keeps score with a <see cref="StuntTracker"/>
    /// from the stunts each phone reports, standings and results carry the points, and the event
    /// ends when the time runs out.
    /// </summary>
    public sealed class HostRace : IRaceView
    {
        /// <summary>The 3-2-1 countdown.</summary>
        public const long CountdownMs = 3000;

        /// <summary>Extra time before the countdown so the start message reaches every guest first.</summary>
        public const long StartLeadMs = 500;

        /// <summary>Once someone finishes, the others have this long before the race ends without them.</summary>
        public const long PostFinishMs = 30000;

        public const long StandingsIntervalMs = 500;

        private readonly HostSession _session;
        private readonly IReadOnlyList<int> _checkpointCounts;
        private readonly Func<long> _clockMs;
        private readonly Func<int, float> _fractionToNextCheckpoint;
        private readonly CarPositionLookup _carPositions;
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly List<byte> _gridOrder = new List<byte>();
        private long _firstFinishMs = -1;
        private long _nextStandingsMs;

        /// <param name="checkpointCounts">Each track's checkpoint count, by track index.</param>
        /// <param name="fractionToNextCheckpoint">How far (0..1) a player's car is towards its next checkpoint, from track geometry.</param>
        /// <param name="carPositions">Where the host sees each car, for Sumo's platform edge; not needed for races.</param>
        public HostRace(HostSession session, IReadOnlyList<int> checkpointCounts, Func<long> clockMs, Func<int, float> fractionToNextCheckpoint, CarPositionLookup carPositions = null)
        {
            _session = session;
            _checkpointCounts = checkpointCounts;
            _clockMs = clockMs;
            _fractionToNextCheckpoint = fractionToNextCheckpoint;
            _carPositions = carPositions;
            _session.CheckpointReceived += OnCheckpoint;
            _session.StuntReceived += OnStunt;
        }

        public RacePhase Phase { get; private set; } = RacePhase.Lobby;
        public int LapCount { get; private set; }
        public long StartAtLocalMs { get; private set; }
        public StandingsMessage Standings { get; private set; }
        public RaceResultsMessage Results { get; private set; }
        public GameMode Mode { get; private set; }
        public int TrackIndex { get; private set; }

        public EventRules Rules { get; private set; } = EventRules.Default;

        /// <summary>Player ids from pole back, for the current event.</summary>
        public IReadOnlyList<byte> GridOrder => _gridOrder;

        public int GridSlotOf(byte playerId) => RaceGrid.SlotOf(_gridOrder, playerId);

        /// <summary>The race referee; null in arena modes.</summary>
        public RaceTracker Tracker { get; private set; }

        /// <summary>The arena referee in Sumo and Battle; null otherwise.</summary>
        public EliminationTracker Arena { get; private set; }

        /// <summary>The Monster Jam scorer; null otherwise.</summary>
        public StuntTracker Stunts { get; private set; }

        /// <summary>
        /// Starts the countdown for everyone in the roster, after filling the grid with bots. A race
        /// is on track <paramref name="trackIndex"/>. Players start in order of
        /// <paramref name="gridRank"/> by name (lowest on pole; design doc §10's reverse grid), or by
        /// player id without one. Returns false outside the lobby or for a track that doesn't exist.
        /// </summary>
        public bool Start(int laps, GameMode mode = GameMode.Race, int trackIndex = 0, Func<string, int> gridRank = null)
        {
            if (mode != GameMode.Race)
            {
                trackIndex = 0;
            }

            if (Phase != RacePhase.Lobby || laps < 1 || laps > byte.MaxValue || trackIndex < 0 || trackIndex >= _checkpointCounts.Count)
            {
                return false;
            }

            _session.AddBotsForEvent();
            var ids = new int[_session.Roster.Count];
            _names.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                var player = _session.Roster.Players[i];
                ids[i] = player.PlayerId;
                _names[player.PlayerId] = player.Name;
            }

            Mode = mode;
            TrackIndex = trackIndex;
            _gridOrder.Clear();
            foreach (var player in _session.Roster.Players)
            {
                _gridOrder.Add(player.PlayerId);
            }

            if (gridRank != null)
            {
                // A stable sort by rank, ties by player id.
                var ranked = new List<(int Rank, byte Id)>();
                foreach (var player in _session.Roster.Players)
                {
                    ranked.Add((gridRank(player.Name), player.PlayerId));
                }

                ranked.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Id.CompareTo(b.Id));
                _gridOrder.Clear();
                foreach (var (_, id) in ranked)
                {
                    _gridOrder.Add(id);
                }
            }
            else
            {
                _gridOrder.Sort();
            }

            Tracker = mode == GameMode.Race ? new RaceTracker(_checkpointCounts[trackIndex], laps, ids) : null;
            Arena = mode == GameMode.Sumo || mode == GameMode.Battle ? new EliminationTracker(ids, mode == GameMode.Battle ? BattleRules.StartingLives : 1) : null;
            Stunts = mode == GameMode.Monster ? new StuntTracker(ids) : null;
            LapCount = laps;
            StartAtLocalMs = _clockMs() + StartLeadMs + CountdownMs;
            Standings = null;
            Results = null;
            _firstFinishMs = -1;
            _nextStandingsMs = StartAtLocalMs;
            _session.EventInProgress = true;
            Phase = RacePhase.Countdown;
            Rules = _session.Settings.Rules.For(mode);
            _session.Broadcast(new RaceCountdownMessage { StartAtHostMs = StartAtLocalMs, LapCount = (byte)laps, Mode = mode, TrackIndex = (byte)trackIndex, GridOrder = new List<byte>(_gridOrder), Rules = Rules.ToByte() }, reliable: true);
            return true;
        }

        public void Tick()
        {
            long now = _clockMs();
            if (Phase == RacePhase.Countdown && now >= StartAtLocalMs)
            {
                Phase = RacePhase.Racing;
            }

            if (Phase != RacePhase.Racing)
            {
                return;
            }

            if (Mode == GameMode.Monster)
            {
                TickMonster(now);
                return;
            }

            if (Mode != GameMode.Race)
            {
                TickArena(now);
                return;
            }

            if (now >= _nextStandingsMs)
            {
                _nextStandingsMs = now + StandingsIntervalMs;
                Standings = BuildStandings();
                _session.Broadcast(Standings, reliable: false);
            }

            if (Tracker.AllFinished || (_firstFinishMs >= 0 && now >= _firstFinishMs + PostFinishMs))
            {
                End(now);
            }
        }

        public void ReportOwnCheckpoint(int index) => Report(_session.HostPlayerId, index, _clockMs() - StartAtLocalMs);

        /// <summary>Battle: a player's car spun out (an item hit that a shield didn't stop), which costs a life.</summary>
        public void ReportSpinOut(byte playerId)
        {
            if (Phase != RacePhase.Racing || Mode != GameMode.Battle)
            {
                return;
            }

            Arena.LoseLife(playerId, _clockMs() - StartAtLocalMs);
            SendStandingsNow();
        }

        public void ReportOwnStunt(StuntKind kind, long airMs, byte victimId) => Score(_session.HostPlayerId, kind, airMs, victimId);

        /// <summary>Monster Jam: a bot's truck (which the host drives) landed a jump or crushed another truck.</summary>
        public void ReportBotStunt(byte botId, StuntKind kind, long airMs, byte victimId)
        {
            if (_session.IsBot(botId))
            {
                Score(botId, kind, airMs, victimId);
            }
        }

        /// <summary>Sumo's platform radius now (design doc §7.0).</summary>
        public float PlatformRadius => SumoRules.PlatformRadius(Math.Max(0, _clockMs() - StartAtLocalMs) / 1000f);

        /// <summary>A bot's car (which the host drives) crossed checkpoint <paramref name="index"/>.</summary>
        public void ReportBotCheckpoint(byte botId, int index)
        {
            if (_session.IsBot(botId))
            {
                Report(botId, index, _clockMs() - StartAtLocalMs);
            }
        }

        /// <summary>From the results screen: everyone back to the lobby, Ready cleared, joining reopened.</summary>
        public bool ReturnToLobby()
        {
            if (Phase != RacePhase.Results)
            {
                return false;
            }

            Phase = RacePhase.Lobby;
            Arena = null;
            Stunts = null;
            _session.EventInProgress = false;
            _session.Broadcast(new ReturnToLobbyMessage(), reliable: true);
            _session.RemoveBots();
            _session.ResetReady();
            return true;
        }

        private void OnCheckpoint(CheckpointReachedMessage message)
        {
            // Trust the guest's race time only within what has actually elapsed on the host.
            long elapsed = _clockMs() - StartAtLocalMs;
            Report(message.PlayerId, message.CheckpointIndex, Math.Max(0, Math.Min(message.RaceTimeMs, elapsed)));
        }

        private void OnStunt(StuntMessage stunt) => Score(stunt.PlayerId, stunt.Kind, stunt.AirMs, stunt.VictimId);

        private void Score(byte playerId, StuntKind kind, long airMs, byte victimId)
        {
            if (Phase != RacePhase.Racing || Mode != GameMode.Monster)
            {
                return;
            }

            long elapsed = _clockMs() - StartAtLocalMs;
            int points;
            if (kind == StuntKind.Air)
            {
                points = Stunts.AddAir(playerId, airMs, elapsed);
            }
            else
            {
                // Only a crush the host can see: both trucks where it has them, close together.
                float distance = float.MaxValue;
                if (_carPositions != null && _carPositions(playerId, out Vec3 attacker) && _carPositions(victimId, out Vec3 victim))
                {
                    distance = (attacker - victim).Length;
                }

                points = Stunts.AddCrush(playerId, victimId, elapsed, distance);
            }

            if (points > 0)
            {
                SendStandingsNow();
            }
        }

        private void TickMonster(long now)
        {
            long elapsed = now - StartAtLocalMs;
            if (now >= _nextStandingsMs)
            {
                SendStandingsNow();
            }

            if (elapsed < MonsterRules.TimeLimitMs)
            {
                return;
            }

            Results = new RaceResultsMessage();
            foreach (var placing in Stunts.Placings())
            {
                Results.Results.Add(new RaceResultEntry
                {
                    PlayerId = (byte)placing.PlayerId,
                    Name = _names.TryGetValue(placing.PlayerId, out string name) ? name : string.Empty,
                    Position = (byte)placing.Position,
                    RaceTimeMs = MonsterRules.TimeLimitMs,
                    Points = (ushort)Math.Min(placing.Points, ushort.MaxValue),
                });
            }

            Standings = BuildStandings();
            Phase = RacePhase.Results;
            _session.Broadcast(Results, reliable: true);
        }

        private void TickArena(long now)
        {
            long elapsed = now - StartAtLocalMs;
            if (Mode == GameMode.Sumo && _carPositions != null)
            {
                float radius = PlatformRadius;
                foreach (int id in Arena.PlayerIds)
                {
                    if (!Arena.IsOut(id) && _carPositions((byte)id, out Vec3 position) && SumoRules.IsOut(position, ArenaLayout.SumoCentre, radius))
                    {
                        Arena.Eliminate(id, elapsed);
                        SendStandingsNow();
                    }
                }
            }

            if (now >= _nextStandingsMs)
            {
                SendStandingsNow();
            }

            if (Arena.IsOver || (Mode == GameMode.Battle && elapsed >= BattleRules.TimeLimitMs))
            {
                EndArena(elapsed);
            }
        }

        private void SendStandingsNow()
        {
            _nextStandingsMs = _clockMs() + StandingsIntervalMs;
            Standings = BuildStandings();
            _session.Broadcast(Standings, reliable: false);
        }

        private void EndArena(long elapsed)
        {
            Results = new RaceResultsMessage();
            foreach (var placing in Arena.Placings(elapsed))
            {
                Results.Results.Add(new RaceResultEntry
                {
                    PlayerId = (byte)placing.PlayerId,
                    Name = _names.TryGetValue(placing.PlayerId, out string name) ? name : string.Empty,
                    Position = (byte)placing.Position,
                    RaceTimeMs = placing.SurvivedMs,
                });
            }

            Standings = BuildStandings();
            Phase = RacePhase.Results;
            _session.Broadcast(Results, reliable: true);
        }

        private void Report(byte playerId, int index, long raceTimeMs)
        {
            if (Phase != RacePhase.Racing || Mode != GameMode.Race)
            {
                return;
            }

            if (Tracker.ReportCheckpoint(playerId, index, raceTimeMs) != CheckpointResult.Finished)
            {
                return;
            }

            if (_firstFinishMs < 0)
            {
                _firstFinishMs = _clockMs();
            }

            var racer = Tracker.Get(playerId);
            _session.Broadcast(new PlayerFinishedMessage { PlayerId = playerId, FinishPosition = (byte)racer.FinishPosition, RaceTimeMs = racer.FinishTimeMs }, reliable: true);
        }

        private void End(long now)
        {
            Tracker.FinishRemaining(_fractionToNextCheckpoint, now - StartAtLocalMs);
            Standings = BuildStandings();
            Results = new RaceResultsMessage();
            foreach (int id in Tracker.FinishOrder)
            {
                var racer = Tracker.Get(id);
                Results.Results.Add(new RaceResultEntry
                {
                    PlayerId = (byte)id,
                    Name = _names.TryGetValue(id, out string name) ? name : string.Empty,
                    Position = (byte)racer.FinishPosition,
                    RaceTimeMs = racer.FinishTimeMs,
                    DidNotFinish = racer.DidNotFinish,
                });
            }

            Phase = RacePhase.Results;
            _session.Broadcast(Results, reliable: true);
        }

        private StandingsMessage BuildStandings()
        {
            var message = new StandingsMessage();
            if (Stunts != null)
            {
                foreach (var placing in Stunts.Placings())
                {
                    message.Order.Add(new StandingsEntry { PlayerId = (byte)placing.PlayerId, Points = (ushort)Math.Min(placing.Points, ushort.MaxValue) });
                }

                return message;
            }

            if (Arena != null)
            {
                foreach (var placing in Arena.Placings(_clockMs() - StartAtLocalMs))
                {
                    message.Order.Add(new StandingsEntry
                    {
                        PlayerId = (byte)placing.PlayerId,
                        LapsCompleted = (byte)placing.Lives,
                        FinishPosition = Arena.IsOut(placing.PlayerId) ? (byte)placing.Position : (byte)0,
                    });
                }

                return message;
            }

            foreach (int id in Tracker.ComputeStandings(_fractionToNextCheckpoint))
            {
                var racer = Tracker.Get(id);
                message.Order.Add(new StandingsEntry
                {
                    PlayerId = (byte)id,
                    LapsCompleted = (byte)Math.Min(racer.LapsCompleted, byte.MaxValue),
                    FinishPosition = (byte)racer.FinishPosition,
                });
            }

            return message;
        }
    }

    /// <summary>A guest's view of the race: follows the host's messages and reports its own checkpoints.</summary>
    public sealed class GuestRace : IRaceView
    {
        private readonly GuestSession _session;
        private readonly Func<long> _clockMs;
        private List<byte> _gridOrder = new List<byte>();

        public GuestRace(GuestSession session, Func<long> clockMs)
        {
            _session = session;
            _clockMs = clockMs;
            _session.RaceMessageReceived += OnMessage;
        }

        public RacePhase Phase { get; private set; } = RacePhase.Lobby;
        public int LapCount { get; private set; }
        public GameMode Mode { get; private set; }
        public int TrackIndex { get; private set; }
        public EventRules Rules { get; private set; } = EventRules.Default;
        public long StartAtLocalMs { get; private set; }
        public StandingsMessage Standings { get; private set; }
        public RaceResultsMessage Results { get; private set; }

        public int GridSlotOf(byte playerId) => RaceGrid.SlotOf(_gridOrder, playerId);

        /// <summary>
        /// The cup table after the latest event of a cup (design doc §10); cleared when everyone
        /// goes back to the lobby or the next event counts down.
        /// </summary>
        public CupStandingsMessage CupStandings { get; private set; }

        public void Tick()
        {
            if (Phase == RacePhase.Countdown && _clockMs() >= StartAtLocalMs)
            {
                Phase = RacePhase.Racing;
            }
        }

        public void ReportOwnCheckpoint(int index)
        {
            if (Phase != RacePhase.Racing)
            {
                return;
            }

            _session.SendToHost(new CheckpointReachedMessage
            {
                PlayerId = _session.PlayerId,
                CheckpointIndex = (ushort)index,
                RaceTimeMs = _clockMs() - StartAtLocalMs,
            }, reliable: true);
        }

        public void ReportOwnStunt(StuntKind kind, long airMs, byte victimId)
        {
            if (Phase != RacePhase.Racing || Mode != GameMode.Monster)
            {
                return;
            }

            _session.SendToHost(new StuntMessage
            {
                PlayerId = _session.PlayerId,
                Kind = kind,
                AirMs = (ushort)Math.Max(0L, Math.Min(airMs, ushort.MaxValue)),
                VictimId = victimId,
            }, reliable: true);
        }

        private void OnMessage(IGameMessage message)
        {
            switch (message)
            {
                case RaceCountdownMessage countdown:
                    Phase = RacePhase.Countdown;
                    LapCount = countdown.LapCount;
                    Mode = countdown.Mode;
                    TrackIndex = countdown.TrackIndex;
                    Rules = EventRules.FromByte(countdown.Rules).For(countdown.Mode);
                    _gridOrder = countdown.GridOrder;
                    CupStandings = null;
                    StartAtLocalMs = _session.Clock.ToLocalTime(countdown.StartAtHostMs);
                    Standings = null;
                    Results = null;
                    Tick();
                    break;
                case StandingsMessage standings when Phase == RacePhase.Racing || Phase == RacePhase.Countdown:
                    Standings = standings;
                    break;
                case RaceResultsMessage results:
                    Results = results;
                    Phase = RacePhase.Results;
                    break;
                case ReturnToLobbyMessage _:
                    Phase = RacePhase.Lobby;
                    CupStandings = null;
                    break;
                case CupStandingsMessage cup:
                    CupStandings = cup;
                    break;
            }
        }
    }

    /// <summary>Start slots from a countdown's grid order.</summary>
    public static class RaceGrid
    {
        /// <summary>
        /// <paramref name="playerId"/>'s place in <paramref name="gridOrder"/> (0 is pole). Someone
        /// missing from it (it was empty, or they joined late) takes the slot after everyone listed,
        /// offset by their id so two late joiners don't share a slot.
        /// </summary>
        public static int SlotOf(IReadOnlyList<byte> gridOrder, byte playerId)
        {
            for (int i = 0; i < gridOrder.Count; i++)
            {
                if (gridOrder[i] == playerId)
                {
                    return i;
                }
            }

            return gridOrder.Count == 0 ? playerId : gridOrder.Count + playerId;
        }
    }

    /// <summary>Who is out of an arena event (Sumo or Battle, design doc §7.0), as every phone sees it.</summary>
    public static class ArenaStandings
    {
        /// <summary>Whether <paramref name="playerId"/> is out of the arena event, from the latest standings.</summary>
        public static bool IsOut(IRaceView race, byte playerId)
        {
            var standings = race.Standings;
            if (standings == null)
            {
                return false;
            }

            foreach (var entry in standings.Order)
            {
                if (entry.PlayerId == playerId)
                {
                    return entry.IsOut(race.Mode);
                }
            }

            return false;
        }
    }
}
