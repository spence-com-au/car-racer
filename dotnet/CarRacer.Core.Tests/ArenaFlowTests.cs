using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ArenaFlowTests
    {
        private const ushort Version = 17;
        private const ulong GuestConnection = 7;

        private sealed class HostLink : ISessionTransport
        {
            public readonly List<IGameMessage> Sent = new List<IGameMessage>();
            public GuestSession Guest;

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                Sent.Add(message);
                Guest?.OnMessage(message);
            }

            public void Disconnect(ulong connection)
            {
            }
        }

        private sealed class GuestLink : ISessionTransport
        {
            public HostSession Host;

            public void Send(ulong connection, IGameMessage message, bool reliable) => Host.OnMessage(GuestConnection, message);

            public void Disconnect(ulong connection)
            {
            }
        }

        private long _now = 10_000;
        private readonly HostLink _hostLink = new HostLink();
        private readonly HostSession _host;
        private readonly HostRace _race;
        private readonly GuestSession _guest;
        private readonly GuestRace _guestRace;
        private readonly Dictionary<byte, Vec3> _cars = new Dictionary<byte, Vec3>();

        public ArenaFlowTests()
        {
            _host = new HostSession(_hostLink, "Host", Version, () => _now);
            _race = new HostRace(_host, new[] { 4 }, () => _now, id => 0f, _cars.TryGetValue);
            var guestLink = new GuestLink { Host = _host };
            _guest = new GuestSession(guestLink, "Guest", Version, () => _now);
            _hostLink.Guest = _guest;
            _guest.OnConnected();
            _guest.OnMessage(new ClockPongMessage { GuestSentMs = _now - 20, HostMs = _now - 10 });
            _guestRace = new GuestRace(_guest, () => _now);
        }

        private byte HostId => _host.HostPlayerId;
        private byte GuestId => _guest.PlayerId;

        private void Start(GameMode mode)
        {
            Assert.True(_race.Start(1, mode));
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);
        }

        private void Tick()
        {
            _race.Tick();
            _guestRace.Tick();
        }

        [Fact]
        public void TheCountdownTellsGuestsTheMode()
        {
            Start(GameMode.Sumo);
            Assert.Equal(GameMode.Sumo, _guestRace.Mode);
            Assert.Null(_race.Tracker);
            Assert.NotNull(_race.Arena);
            var countdown = Assert.IsType<RaceCountdownMessage>(MessageCodec.Decode(MessageCodec.Encode(new RaceCountdownMessage { Mode = GameMode.Battle })));
            Assert.Equal(GameMode.Battle, countdown.Mode);
        }

        [Fact]
        public void RacesStillDefaultToRacing()
        {
            Start(GameMode.Race);
            Assert.Equal(GameMode.Race, _guestRace.Mode);
            Assert.NotNull(_race.Tracker);
            Assert.Null(_race.Arena);
        }

        [Fact]
        public void ABotTakesOverAGuestWhoLeavesAnArena()
        {
            Start(GameMode.Battle);
            _now += 5_000;
            _host.OnDisconnected(GuestConnection);
            Tick();

            // The guest's car carries on under a bot (design doc §12): still in, and it can still lose lives.
            Assert.Equal(RacePhase.Racing, _race.Phase);
            Assert.True(_host.IsBot(GuestId));
            Assert.False(_race.Arena.IsOut(GuestId));
            _race.ReportSpinOut(GuestId);
            Assert.Equal(BattleRules.StartingLives - 1, _race.Arena.Lives(GuestId));
        }

        [Fact]
        public void InSumoACarOffThePlatformIsOutAndTheLastOneWins()
        {
            _cars[HostId] = ArenaLayout.SumoCentre + new Vec3(5f, 0.6f, 0f);
            _cars[GuestId] = ArenaLayout.SumoCentre + new Vec3(-5f, 0.6f, 0f);
            Start(GameMode.Sumo);
            _now += 10_000;
            Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);

            _cars[GuestId] = ArenaLayout.SumoCentre + new Vec3(SumoRules.StartRadius + 1f, 0.6f, 0f);
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal(RacePhase.Results, _guestRace.Phase);
            var results = _guestRace.Results.Results;
            Assert.Equal(new[] { HostId, GuestId }, results.Select(r => r.PlayerId));
            Assert.Equal(10_000, results[1].RaceTimeMs);
            Assert.Equal("Guest", results[1].Name);
        }

        [Fact]
        public void TheShrinkingPlatformCatchesACarThatStaysPut()
        {
            _cars[HostId] = ArenaLayout.SumoCentre;
            _cars[GuestId] = ArenaLayout.SumoCentre + new Vec3(SumoRules.EndRadius + 5f, 0.6f, 0f);
            Start(GameMode.Sumo);
            _now += (long)(SumoRules.GraceSeconds * 1000f);
            Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);
            _now += (long)(SumoRules.ShrinkSeconds * 1000f);
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal(HostId, _race.Results.Results[0].PlayerId);
        }

        [Fact]
        public void ACarThatFallsIsOut()
        {
            _cars[HostId] = ArenaLayout.SumoCentre;
            _cars[GuestId] = ArenaLayout.SumoCentre + new Vec3(3f, 0.6f, 0f);
            Start(GameMode.Sumo);
            _cars[GuestId] = ArenaLayout.SumoCentre + new Vec3(3f, -SumoRules.FallDepth - 1f, 0f);
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
        }

        [Fact]
        public void InBattleSpinOutsCostLivesAndStandingsShowThem()
        {
            Start(GameMode.Battle);
            _race.ReportSpinOut(GuestId);
            var guestStanding = _guestRace.Standings.Order.Single(e => e.PlayerId == GuestId);
            Assert.Equal(BattleRules.StartingLives - 1, guestStanding.LapsCompleted);
            Assert.Equal(0, guestStanding.FinishPosition);

            _race.ReportSpinOut(GuestId);
            _race.ReportSpinOut(GuestId);
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal(HostId, _race.Results.Results[0].PlayerId);
            Assert.Equal(2, _race.Standings.Order.Single(e => e.PlayerId == GuestId).FinishPosition);
        }

        [Fact]
        public void BattleEndsWhenTimeRunsOutWithTheMostLivesWinning()
        {
            Start(GameMode.Battle);
            _race.ReportSpinOut(HostId);
            _now += BattleRules.TimeLimitMs;
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal(new[] { GuestId, HostId }, _race.Results.Results.Select(r => r.PlayerId));
        }

        [Fact]
        public void SpinOutsDoNotCountOutsideBattle()
        {
            Start(GameMode.Sumo);
            _race.ReportSpinOut(GuestId);
            Assert.Equal(1, _race.Arena.Lives(GuestId));
        }

        [Fact]
        public void CheckpointsDoNotCountInArenas()
        {
            _cars[HostId] = ArenaLayout.SumoCentre;
            _cars[GuestId] = ArenaLayout.SumoCentre;
            Start(GameMode.Sumo);
            _race.ReportOwnCheckpoint(0); // nothing to report to: no race tracker
            _host.OnMessage(GuestConnection, new CheckpointReachedMessage { PlayerId = GuestId, CheckpointIndex = 0 });
            Assert.Equal(RacePhase.Racing, _race.Phase);
        }

        [Fact]
        public void BackToTheLobbyClearsTheArena()
        {
            Start(GameMode.Battle);
            _now += BattleRules.TimeLimitMs;
            Tick();
            Assert.True(_race.ReturnToLobby());
            Assert.Null(_race.Arena);
            Start(GameMode.Race);
            Assert.NotNull(_race.Tracker);
        }
    }
}
