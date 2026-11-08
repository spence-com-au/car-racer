using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Bots;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class BotRosterTests
    {
        [Fact]
        public void BotsGetNamesIdsAndColoursAndAreAlwaysReady()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.Add("Alice");
            var first = roster.AddBot();
            var second = roster.AddBot();
            Assert.True(first.IsBot);
            Assert.True(first.Ready);
            Assert.Equal(LobbyRoster.BotNames[0], first.Name);
            Assert.Equal(LobbyRoster.BotNames[1], second.Name);
            Assert.Equal(2, first.PlayerId);
            Assert.NotEqual(first.ColourIndex, second.ColourIndex);
            Assert.False(roster.SetReady(first.PlayerId, false));
            Assert.Equal(2, roster.HumanCount);
        }

        [Fact]
        public void BotsDoNotCountTowardsEveryoneReady()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.AddBot();
            Assert.False(roster.EveryoneReady); // no guests, just a bot
            var alice = roster.Add("Alice");
            Assert.False(roster.EveryoneReady);
            roster.SetReady(alice.PlayerId, true);
            Assert.True(roster.EveryoneReady);
            roster.ResetReady();
            Assert.True(roster.Players.Single(p => p.IsBot).Ready);
        }

        [Fact]
        public void RemovingBotsLeavesThePeople()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.AddBot();
            roster.AddBot();
            Assert.Equal(2, roster.RemoveBots());
            Assert.Single(roster.Players);
        }

        [Fact]
        public void TheRosterMessageSaysWhoIsABot()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.AddBot();
            var decoded = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(roster.ToMessage())));
            Assert.False(decoded.Players[0].IsBot);
            Assert.True(decoded.Players[1].IsBot);
        }
    }

    public class BotSessionTests
    {
        private const ushort Version = 17;

        private sealed class Transport : ISessionTransport
        {
            public readonly List<(ulong To, IGameMessage Message)> Sent = new List<(ulong, IGameMessage)>();

            public void Send(ulong connection, IGameMessage message, bool reliable) => Sent.Add((connection, message));

            public void Disconnect(ulong connection)
            {
            }
        }

        private long _now = 10_000;
        private readonly Transport _transport = new Transport();
        private readonly HostSession _host;
        private readonly HostRace _race;
        private readonly HostItems _items;
        private readonly Dictionary<byte, Vec3> _cars = new Dictionary<byte, Vec3>();
        private readonly List<HitMessage> _botHits = new List<HitMessage>();
        private readonly byte _guest;

        public BotSessionTests()
        {
            _host = new HostSession(_transport, "Host", Version, () => _now);
            _race = new HostRace(_host, new[] { 4 }, () => _now, id => 0f);
            _items = new HostItems(_host, _race, new List<Vec3> { new Vec3(0f, 0.75f, 20f) }, () => _now, () => 0.0, _cars.TryGetValue);
            _host.BotHitReceived += _botHits.Add;
            _host.OnMessage(5, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = "Alice" });
            _guest = _transport.Sent.Select(s => s.Message).OfType<JoinReplyMessage>().Last().PlayerId;
        }

        private List<LobbyPlayer> Bots => _host.Roster.Players.Where(p => p.IsBot).ToList();

        private void StartRacing()
        {
            Assert.True(_race.Start(3));
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            _race.Tick();
            _items.Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);
        }

        [Fact]
        public void NoBotsUnlessTheHostAsks()
        {
            StartRacing();
            Assert.Empty(Bots);
        }

        [Fact]
        public void StartingARaceFillsTheGridWithBotsWhoRace()
        {
            _host.BotFill = 4;
            StartRacing();
            Assert.Equal(2, Bots.Count);
            Assert.Equal(4, _host.Roster.Count);
            Assert.True(Bots.All(b => _race.Tracker.Get(b.PlayerId) != null));

            var lastRoster = _transport.Sent.Where(s => s.To == 5).Select(s => s.Message).OfType<RosterMessage>().Last();
            Assert.Equal(2, lastRoster.Players.Count(p => p.IsBot));
        }

        [Fact]
        public void BotFillNeverGoesPastAFullSession()
        {
            _host.BotFill = 99;
            StartRacing();
            Assert.Equal(LobbyRoster.MaxPlayers, _host.Roster.Count);
        }

        [Fact]
        public void BackToTheLobbyTheBotsLeave()
        {
            _host.BotFill = 4;
            StartRacing();
            _race.Tracker.FinishRemaining(_ => 0f, 0);
            _race.Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Contains(_race.Results.Results, r => r.Name == LobbyRoster.BotNames[0]);
            Assert.True(_race.ReturnToLobby());
            Assert.Empty(Bots);
            Assert.Equal(2, _host.Roster.Count);
        }

        [Fact]
        public void TheHostReportsBotCheckpointsButNotForPeople()
        {
            _host.BotFill = 3;
            StartRacing();
            byte bot = Bots[0].PlayerId;
            _race.ReportBotCheckpoint(bot, 0);
            Assert.Equal(1, _race.Tracker.Get(bot).NextCheckpoint);
            _race.ReportBotCheckpoint(_guest, 0);
            Assert.Equal(0, _race.Tracker.Get(_guest).NextCheckpoint);
        }

        [Fact]
        public void ItemHitsOnBotsStayWithTheHost()
        {
            _host.BotFill = 3;
            StartRacing();
            byte bot = Bots[0].PlayerId;
            int sentBefore = _transport.Sent.Count;
            _host.SendHit(new HitMessage { AttackerId = _guest, TargetId = bot, IsItemHit = true });
            Assert.Equal(bot, Assert.Single(_botHits).TargetId);
            Assert.DoesNotContain(_transport.Sent.Skip(sentBefore), s => s.Message is HitMessage);
        }

        [Fact]
        public void BotsPickUpAndUseItems()
        {
            _host.BotFill = 3;
            StartRacing();
            byte bot = Bots[0].PlayerId;
            Assert.True(_items.ClaimTouchedBoxForBot(bot, new Vec3(0f, 0.6f, 20f)));
            var held = _items.Slots.Held(bot);
            Assert.NotEqual(ItemKind.None, held);
            Assert.Equal(held, _items.UseHeldForBot(bot, Vec3.Zero, Vec3.Zero, 0f));
            Assert.Equal(ItemKind.None, _items.Slots.Held(bot));
            Assert.Contains(_transport.Sent, s => s.Message is ItemUsedMessage u && u.PlayerId == bot);
        }

        [Fact]
        public void TheBotItemCallsAreOnlyForBots()
        {
            StartRacing();
            Assert.False(_items.ClaimTouchedBoxForBot(_guest, new Vec3(0f, 0.6f, 20f)));
            Assert.False(_items.ClaimTouchedBoxForBot(_host.HostPlayerId, new Vec3(0f, 0.6f, 20f)));
            Assert.Equal(ItemKind.None, _items.UseHeldForBot(_guest, Vec3.Zero, Vec3.Zero, 0f));
        }

        [Fact]
        public void LeavingMidRaceHandsTheCarToABot()
        {
            StartRacing();
            var takenOver = new List<byte>();
            var left = new List<byte>();
            _host.PlayerTakenOver += takenOver.Add;
            _host.PlayerLeft += (id, name) => left.Add(id);
            _host.OnDisconnected(5);
            Assert.Equal(new[] { _guest }, takenOver);
            Assert.Empty(left);
            var player = _host.Roster.Find(_guest);
            Assert.True(player.IsBot);
            Assert.Equal("Alice", player.Name);
            Assert.True(_host.IsBot(_guest));
        }

        [Fact]
        public void LeavingTheLobbyStillJustLeaves()
        {
            var left = new List<byte>();
            _host.PlayerLeft += (id, name) => left.Add(id);
            _host.OnDisconnected(5);
            Assert.Equal(new[] { _guest }, left);
            Assert.Null(_host.Roster.Find(_guest));
        }

        [Fact]
        public void DifficultyDefaultsToNormal()
        {
            Assert.Equal(BotDifficulty.Normal, _host.BotDifficulty);
        }
    }
}
