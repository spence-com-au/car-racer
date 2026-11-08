using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Bots;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class RoomLeaderTests
    {
        private const ushort Version = 3;

        private long _now = 1000;
        private readonly RecordingTransport _transport = new RecordingTransport();
        private readonly HostSession _server;
        private readonly List<LobbyCommand> _commands = new List<LobbyCommand>();

        public RoomLeaderTests()
        {
            _server = HostSession.Dedicated(_transport, Version, () => _now);
            _server.LobbyCommandReceived += _commands.Add;
        }

        private static byte Join(HostSession host, RecordingTransport transport, ulong connection, string name)
        {
            host.OnMessage(connection, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = name });
            var reply = transport.To<JoinReplyMessage>(connection).Last();
            Assert.Equal(JoinResult.Accepted, reply.Result);
            return reply.PlayerId;
        }

        private byte Join(ulong connection, string name) => Join(_server, _transport, connection, name);

        private static LobbySettings Settings(GameMode mode = GameMode.Race, int laps = 3, int track = 0)
        {
            var settings = LobbySettings.Default;
            settings.Mode = mode;
            settings.Laps = laps;
            settings.Track = track;
            return settings;
        }

        private void Command(ulong connection, LobbyCommand command, LobbySettings settings = default) =>
            _server.OnMessage(connection, new LobbyCommandMessage { Command = command, Settings = settings });

        [Fact]
        public void ADedicatedHostHasNoPlayerAndNobodyLeadsAnEmptyRoom()
        {
            Assert.True(_server.IsDedicated);
            Assert.Equal(HostSession.NoPlayer, _server.HostPlayerId);
            Assert.Empty(_server.Roster.Players);
            Assert.Equal(HostSession.NoPlayer, _server.LeaderId);
            Assert.False(_server.CanStart);
        }

        [Fact]
        public void TheFirstGuestInLeadsAndHandsOnToTheNextLongestWhenTheyLeave()
        {
            byte alice = Join(1, "Alice");
            byte bob = Join(2, "Bob");
            byte carol = Join(3, "Carol");
            Assert.Equal(alice, _server.LeaderId);
            Assert.Equal(alice, _transport.To<RosterMessage>(3).Last().LeaderId);

            _server.OnDisconnected(1);
            Assert.Equal(bob, _server.LeaderId);
            Assert.Equal(bob, _transport.To<RosterMessage>(3).Last().LeaderId);

            // Dave gets Alice's old id, the lowest free, but joined last, so Bob still leads, then Carol.
            byte dave = Join(4, "Dave");
            Assert.Equal(alice, dave);
            _server.OnDisconnected(2);
            Assert.Equal(carol, _server.LeaderId);
        }

        [Fact]
        public void ALeaderAwayFromTheGameHandsOverUntilTheyAreBack()
        {
            byte alice = Join(1, "Alice");
            byte bob = Join(2, "Bob");
            _server.EventInProgress = true;

            _now += HostSession.AwayAfterMs / 2;
            _server.OnMessage(2, new ClockPingMessage());
            _now += HostSession.AwayAfterMs / 2;
            _server.Tick();
            Assert.Equal(bob, _server.LeaderId);

            _server.OnMessage(1, new ClockPingMessage());
            Assert.Equal(alice, _server.LeaderId);
        }

        [Fact]
        public void OnlyTheLeadersSettingsCount_AndEveryoneHearsThem()
        {
            Join(1, "Alice");
            Join(2, "Bob");

            Command(2, LobbyCommand.ChangeSettings, Settings(laps: 7));
            Assert.Equal(3, _server.Settings.Laps);

            Command(1, LobbyCommand.ChangeSettings, Settings(GameMode.Sumo, laps: 5, track: 2));
            Assert.Equal(GameMode.Sumo, _server.Settings.Mode);
            Assert.Equal(5, _server.Settings.Laps);
            Assert.Equal(2, _server.Settings.Track);
            Assert.Equal(5, _transport.To<LobbySettingsMessage>(2).Last().Settings.Laps);
        }

        [Fact]
        public void SettingsAreKeptInRangeAndCantChangeMidEvent()
        {
            Join(1, "Alice");
            var wild = Settings(laps: 40, track: 200);
            wild.Mode = (GameMode)9;
            wild.BotFill = 99;
            wild.Cup = 99;
            Command(1, LobbyCommand.ChangeSettings, wild);
            Assert.Equal(GameMode.Race, _server.Settings.Mode);
            Assert.Equal(LobbySettings.MaxLaps, _server.Settings.Laps);
            Assert.Equal(0, _server.Settings.Track);
            Assert.Equal(LobbyRoster.MaxPlayers, _server.Settings.BotFill);
            Assert.Equal(0, _server.Settings.Cup);

            _server.EventInProgress = true;
            Command(1, LobbyCommand.ChangeSettings, Settings(laps: 2));
            Assert.Equal(LobbySettings.MaxLaps, _server.Settings.Laps);
        }

        [Fact]
        public void TheLeadersOtherCommandsGoToTheGame_OthersAreIgnored()
        {
            Join(1, "Alice");
            Join(2, "Bob");
            Command(2, LobbyCommand.Start);
            Assert.Empty(_commands);

            Command(1, LobbyCommand.Start);
            Command(1, LobbyCommand.PauseCup);
            Assert.Equal(new[] { LobbyCommand.Start, LobbyCommand.PauseCup }, _commands);
        }

        [Fact]
        public void AGuestJoiningLateGetsTheSettingsAndTheSavedCup()
        {
            Join(1, "Alice");
            Command(1, LobbyCommand.ChangeSettings, Settings(laps: 6));
            _server.SetSavedCup(2, 3);

            Join(2, "Bob");
            var lobby = _transport.To<LobbySettingsMessage>(2).Last();
            Assert.Equal(6, lobby.Settings.Laps);
            Assert.Equal(2, lobby.SavedCup);
            Assert.Equal(3, lobby.SavedCupDone);
        }

        [Fact]
        public void TheLeaderCanStartAloneOrOnceEveryoneElseIsReady()
        {
            Join(1, "Alice");
            Assert.True(_server.CanStart);

            byte bob = Join(2, "Bob");
            Assert.False(_server.CanStart);
            Assert.False(LobbyRoster.ReadyToStart(_transport.To<RosterMessage>(1).Last()));

            _server.OnMessage(2, new SetReadyMessage { Ready = true });
            Assert.True(_server.CanStart);
            Assert.True(LobbyRoster.ReadyToStart(_transport.To<RosterMessage>(1).Last()));
            Assert.NotEqual(_server.LeaderId, bob);
        }

        [Fact]
        public void OnAPhoneTheHostLeadsAndGuestsCantCommand()
        {
            var transport = new RecordingTransport();
            var host = new HostSession(transport, "Host", Version, () => _now);
            var commands = new List<LobbyCommand>();
            host.LobbyCommandReceived += commands.Add;
            Join(host, transport, 1, "Alice");

            Assert.False(host.IsDedicated);
            Assert.Equal(host.HostPlayerId, host.LeaderId);
            host.OnMessage(1, new LobbyCommandMessage { Command = LobbyCommand.Start });
            host.OnMessage(1, new LobbyCommandMessage { Command = LobbyCommand.ChangeSettings, Settings = Settings(laps: 8) });
            Assert.Empty(commands);
            Assert.Equal(3, host.Settings.Laps);

            // The host's own buttons go through the same path.
            host.RunCommand(LobbyCommand.ChangeSettings, Settings(laps: 8));
            host.RunCommand(LobbyCommand.Start);
            Assert.Equal(8, host.Settings.Laps);
            Assert.Equal(new[] { LobbyCommand.Start }, commands);
        }

        [Fact]
        public void BotSettingsArePartOfTheLobbySettings()
        {
            Join(1, "Alice");
            _server.BotFill = 4;
            _server.BotDifficulty = BotDifficulty.Hard;
            var lobby = _transport.To<LobbySettingsMessage>(1).Last().Settings;
            Assert.Equal(4, lobby.BotFill);
            Assert.Equal(BotDifficulty.Hard, lobby.BotDifficulty);
        }

        [Fact]
        public void AGuestKnowsWhetherItLeadsAndSendsCommandsToTheHost()
        {
            var link = new RecordingTransport();
            var guest = new GuestSession(link, "Alice", Version, () => _now);
            guest.OnConnected();
            guest.OnMessage(new JoinReplyMessage { Result = JoinResult.Accepted, PlayerId = 2 });
            Assert.False(guest.IsLeader);

            guest.OnMessage(new RosterMessage { LeaderId = 2 });
            guest.OnMessage(new LobbySettingsMessage { Settings = Settings(GameMode.Battle), SavedCup = 1 });
            Assert.True(guest.IsLeader);
            Assert.Equal(GameMode.Battle, guest.Lobby.Settings.Mode);
            Assert.Equal(1, guest.Lobby.SavedCup);

            guest.SendLobbyCommand(LobbyCommand.ChangeSettings, Settings(laps: 4));
            var sent = link.To<LobbyCommandMessage>(GuestSession.HostConnection).Single();
            Assert.Equal(LobbyCommand.ChangeSettings, sent.Command);
            Assert.Equal(4, sent.Settings.Laps);
        }

        [Fact]
        public void LobbyMessagesRoundTrip()
        {
            var settings = Settings(GameMode.Battle, laps: 9, track: TrackCatalog.Count - 1);
            settings.Rules = EventRules.Default.WithDamage(false);
            settings.BotFill = 6;
            settings.BotDifficulty = BotDifficulty.Easy;
            settings.Cup = Cups.All.Count;
            settings.ReverseGrid = false;

            var lobby = Assert.IsType<LobbySettingsMessage>(MessageCodec.Decode(MessageCodec.Encode(new LobbySettingsMessage { Settings = settings, SavedCup = 1, SavedCupDone = 4 })));
            Assert.Equal(settings, lobby.Settings);
            Assert.False(lobby.Settings.Rules.Damage);
            Assert.Equal(1, lobby.SavedCup);
            Assert.Equal(4, lobby.SavedCupDone);

            var command = Assert.IsType<LobbyCommandMessage>(MessageCodec.Decode(MessageCodec.Encode(new LobbyCommandMessage { Command = LobbyCommand.LeaveCup, Settings = settings })));
            Assert.Equal(LobbyCommand.LeaveCup, command.Command);
            Assert.Equal(settings, command.Settings);

            var roster = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(new RosterMessage { LeaderId = 3 })));
            Assert.Equal(3, roster.LeaderId);
        }

        [Fact]
        public void AnUnknownLobbyCommandIsMalformed()
        {
            byte[] bytes = MessageCodec.Encode(new LobbyCommandMessage { Command = LobbyCommand.BackToLobby });
            bytes[1] = 200;
            Assert.Throws<NetFormatException>(() => MessageCodec.Decode(bytes));
        }
    }
}
