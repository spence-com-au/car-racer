using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class TvCastTests
    {
        private const ushort Version = 3;

        private sealed class FakeTransport : ISessionTransport
        {
            public readonly List<(ulong To, IGameMessage Message)> Sent = new List<(ulong, IGameMessage)>();

            public void Send(ulong connection, IGameMessage message, bool reliable) => Sent.Add((connection, message));

            public void Disconnect(ulong connection)
            {
            }

            public List<T> To<T>(ulong connection) => Sent.Where(s => s.To == connection).Select(s => s.Message).OfType<T>().ToList();
        }

        private readonly FakeTransport _transport = new FakeTransport();
        private readonly HostSession _server;
        private readonly List<CastCommandMessage> _commands = new List<CastCommandMessage>();

        public TvCastTests()
        {
            _server = HostSession.Dedicated(_transport, Version, () => 1000);
            _server.CastCommandReceived += _commands.Add;
        }

        private void Join(ulong connection, string name) =>
            _server.OnMessage(connection, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = name });

        private static TvStatusMessage Playing(string tv = "Den") => new TvStatusMessage
        {
            CanCast = true,
            Devices = { "Den", "Kitchen" },
            CastingTo = tv,
            State = TvState.Playing,
        };

        [Fact]
        public void MessagesRoundTrip()
        {
            var status = Assert.IsType<TvStatusMessage>(MessageCodec.Decode(MessageCodec.Encode(new TvStatusMessage
            {
                CanCast = true,
                Devices = { "Living Room TV", "Zoë's room" },
                CastingTo = "Living Room TV",
                State = TvState.Busy,
                Detail = "The TV is showing YouTube",
                Music = true,
            })));
            Assert.True(status.Music);
            Assert.True(status.CanCast);
            Assert.Equal(new[] { "Living Room TV", "Zoë's room" }, status.Devices);
            Assert.Equal(TvState.Busy, status.State);
            Assert.Equal("The TV is showing YouTube", status.Detail);

            foreach (var command in new[] { CastCommand.Cast, CastCommand.Stop, CastCommand.MusicOn, CastCommand.MusicOff })
            {
                var back = Assert.IsType<CastCommandMessage>(MessageCodec.Decode(MessageCodec.Encode(new CastCommandMessage { Command = command, Device = "Den" })));
                Assert.Equal(command, back.Command);
                Assert.Equal("Den", back.Device);
            }

            var bad = MessageCodec.Encode(new CastCommandMessage { Command = CastCommand.MusicOff });
            bad[System.Array.IndexOf(bad, (byte)CastCommand.MusicOff, 1)] = 9;
            Assert.ThrowsAny<System.Exception>(() => MessageCodec.Decode(bad));
        }

        [Fact]
        public void OnlyTheFirstEightTvsAreSent()
        {
            var many = new TvStatusMessage();
            for (int i = 0; i < 12; i++)
            {
                many.Devices.Add("TV " + i);
            }

            var back = (TvStatusMessage)MessageCodec.Decode(MessageCodec.Encode(many));
            Assert.Equal(TvStatusMessage.MaxDevices, back.Devices.Count);
        }

        [Fact]
        public void LongNamesAndReasonsAreCutToFitInsteadOfThrowing()
        {
            string longName = new string('x', 300);
            string longEmoji = string.Concat(Enumerable.Repeat("📺", 100));
            var back = (TvStatusMessage)MessageCodec.Decode(MessageCodec.Encode(new TvStatusMessage
            {
                CanCast = true,
                Devices = { longName, longEmoji },
                CastingTo = longName,
                Detail = longEmoji,
            }));
            Assert.Equal(NetWriter.MaxStringBytes, back.Devices[0].Length);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(back.Detail) <= NetWriter.MaxStringBytes);
            Assert.False(char.IsHighSurrogate(back.Detail[back.Detail.Length - 1]));
            Assert.Equal(back.Detail, back.Devices[1]);
            Assert.Equal("Den", TvStatusMessage.Fit("Den"));
            Assert.Equal(string.Empty, TvStatusMessage.Fit(null));
        }

        [Fact]
        public void OnlyTheLeadersCastCommandsReachTheGame()
        {
            Join(1, "Alice");
            Join(2, "Bob");
            _server.OnMessage(2, new CastCommandMessage { Device = "Den" });
            Assert.Empty(_commands);
            _server.OnMessage(1, new CastCommandMessage { Command = CastCommand.Cast, Device = "Den" });
            Assert.Equal("Den", Assert.Single(_commands).Device);

            // Before joining, nobody's commands count.
            _server.OnMessage(9, new CastCommandMessage { Command = CastCommand.Stop });
            Assert.Single(_commands);
        }

        [Fact]
        public void StatusGoesToEveryoneWhenItChangesAndToEachNewGuest()
        {
            Join(1, "Alice");
            _server.SetTvStatus(Playing());
            _server.SetTvStatus(Playing());
            Assert.Single(_transport.To<TvStatusMessage>(1));
            _server.SetTvStatus(Playing("Kitchen"));
            Assert.Equal(2, _transport.To<TvStatusMessage>(1).Count);

            Join(2, "Bob");
            Assert.Equal("Kitchen", Assert.Single(_transport.To<TvStatusMessage>(2)).CastingTo);
        }

        [Fact]
        public void AHostThatCantCastSendsNewGuestsNothing()
        {
            Join(1, "Alice");
            Assert.Empty(_transport.To<TvStatusMessage>(1));
        }

        [Fact]
        public void TheGuestKeepsTheLatestStatusAndSendsCommands()
        {
            var toHost = new FakeTransport();
            var guest = new GuestSession(toHost, "Alice", Version, () => 0);
            guest.OnMessage(new JoinReplyMessage { Result = JoinResult.Accepted, PlayerId = 1 });
            Assert.Equal(GuestState.Joined, guest.State);
            guest.OnMessage(Playing());
            Assert.Equal("Den", guest.TvStatus.CastingTo);
            guest.SendCastCommand(CastCommand.Cast, "Kitchen");
            guest.SendCastCommand(CastCommand.MusicOn);
            var sent = toHost.Sent.Select(s => s.Message).OfType<CastCommandMessage>().ToList();
            Assert.Equal("Kitchen", sent[0].Device);
            Assert.Equal(CastCommand.MusicOn, sent[1].Command);
            Assert.Equal(string.Empty, sent[1].Device);
        }

        [Fact]
        public void TextReadsForEachState()
        {
            var status = new TvStatusMessage { CanCast = true, Devices = { "Den" } };
            Assert.Equal("Cast to TV", TvText.ButtonLabel(status));
            Assert.Equal("Den", TvText.QuickCast(status));
            Assert.Null(TvText.OnTvLine(status));

            status.State = TvState.Connecting;
            status.CastingTo = "Den";
            Assert.Equal("Casting to Den...", TvText.ButtonLabel(status));
            Assert.Null(TvText.QuickCast(status));

            status.State = TvState.Playing;
            Assert.Equal("On TV: Den", TvText.ButtonLabel(status));
            Assert.Equal("On TV: Den", TvText.OnTvLine(status));

            status.State = TvState.Busy;
            Assert.Equal("TV busy: Den", TvText.ButtonLabel(status));
            status.State = TvState.Failed;
            Assert.Equal("Can't reach Den", TvText.ButtonLabel(status));
            Assert.Equal("Den", TvText.QuickCast(status));

            status.Devices.Add("Kitchen");
            Assert.Null(TvText.QuickCast(status));
            Assert.NotEqual(TvText.Signature(status), TvText.Signature(Playing()));

            // A new reason alone rebuilds the lobby, since it's shown under the button.
            var why = new TvStatusMessage { State = TvState.Busy, Detail = "The TV is showing YouTube" };
            Assert.NotEqual(TvText.Signature(why), TvText.Signature(new TvStatusMessage { State = TvState.Busy, Detail = "The TV is showing Netflix" }));
        }

        [Fact]
        public void TheProtocolVersionChangedForTheTvMessages() => Assert.Equal(26, Discovery.DiscoveryConstants.ProtocolVersion);
    }
}
