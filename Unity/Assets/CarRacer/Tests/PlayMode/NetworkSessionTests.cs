using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarRacer.Core.Driving;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Game;
using CarRacer.Game.Bots;
using CarRacer.Game.Driving;
using CarRacer.Game.Net;
using CarRacer.Game.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarRacer.Tests
{
    /// <summary>
    /// Starts a real NGO host from code (one process can't also join it). The session protocol and the framing of its
    /// messages (`MessageCodec.TryDecodeFrame`, which reads everything a guest sends) are tested by `dotnet test`; this
    /// checks the NGO and Unity Transport setup, and what a session does with the frames it is handed: with no peer to
    /// send them, the tests pass them to `NetworkSession.Receive`.
    /// </summary>
    public class NetworkSessionTests
    {
        private const ushort TestPort = 47790;

        // Nothing listens on this one, so a guest that joins it stays connecting.
        private const ushort NobodyPort = TestPort + 6;

        private const ushort ServerPort = TestPort + 7;

        private static readonly string[] SettingKeys =
        {
            HostLobby.BotFillKey,
            HostLobby.BotDifficultyKey,
            HostLobby.ModeKey,
            HostLobby.LapsKey,
            HostLobby.TrackKey,
            HostLobby.RulesKey,
            HostLobby.CupKey,
            HostLobby.ReverseGridKey,
        };

        // Each is something anyone on the LAN could send, and every one has to be dropped without a word to the game.
        private static readonly byte[][] MalformedFrames =
        {
            // Not even a length.
            new byte[0],
            // A length of nothing.
            new byte[] { 0, 0, 0, 0 },
            // A length of 2049 bytes, one past the cap.
            new byte[] { 0x01, 0x08, 0, 0 },
            // Five bytes of message promised and one sent.
            new byte[] { 5, 0, 0, 0, 1 },
            // A message type nobody has.
            new byte[] { 1, 0, 0, 0, 200 },
        };

        private readonly Dictionary<string, int> _savedSettings = new Dictionary<string, int>();
        private string _savedCup;
        private TrackScreen _screen;
        private NetworkSession _session;

        // Hosting saves the lobby settings to `PlayerPrefs` and loads them back on the next host, so each test starts
        // from the defaults and leaves the developer's own saved settings as it found them.
        [SetUp]
        public void SetUp()
        {
            _savedSettings.Clear();
            foreach (string key in SettingKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    _savedSettings[key] = PlayerPrefs.GetInt(key);
                }

                PlayerPrefs.DeleteKey(key);
            }

            _savedCup = PlayerPrefs.HasKey(HostLobby.SaveKey) ? PlayerPrefs.GetString(HostLobby.SaveKey) : null;
            PlayerPrefs.DeleteKey(HostLobby.SaveKey);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string key in SettingKeys)
            {
                if (_savedSettings.TryGetValue(key, out int value))
                {
                    PlayerPrefs.SetInt(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            if (_savedCup != null)
            {
                PlayerPrefs.SetString(HostLobby.SaveKey, _savedCup);
            }
            else
            {
                PlayerPrefs.DeleteKey(HostLobby.SaveKey);
            }
        }

        [UnityTearDown]
        public IEnumerator DestroyTheScreenAndSession()
        {
            if (_screen != null)
            {
                Object.Destroy(_screen.gameObject);
            }

            if (_session != null)
            {
                Object.Destroy(_session.gameObject);
            }

            // Let NGO shut down before anything else starts a NetworkManager. This runs even when an assertion failed.
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private sealed class NoInput : ICarInputSource
        {
            public CarInput Read() => default;
        }

        [UnityTest]
        public IEnumerator HostingPutsTheHostsCarOnTheGrid()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Test host", TestPort));
            yield return null;
            yield return null;

            var session = _screen.Session;
            Assert.IsFalse(session.Ended, session.Status);
            Assert.IsTrue(session.IsHost);
            Assert.IsTrue(session.TryGetLocalPlayerId(out byte me));
            Assert.AreEqual(0, me);
            Assert.AreEqual(1, session.Players.Count);
            Assert.AreEqual("Test host", session.Players[0].Name);
            Assert.IsNotNull(_screen.Car, "the host's car should be spawned");

            int slotIndex = session.Race.GridSlotOf(me);
            var expected = _screen.Track.StartGrid(slotIndex + 1)[slotIndex].Position.ToUnity();
            var actual = _screen.Car.BodyPosition;
            Assert.That(Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(expected.x, expected.z)), Is.LessThan(0.5f), "the host's car should stand on its grid slot");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(2f), "on the track, not under or above it");
        }

        [UnityTest]
        public IEnumerator ASoloRaceHoldsTheCarOnTheGridUntilGo()
        {
            var input = new FullThrottle();
            _screen = TrackScreen.Create(input, () => NetworkSession.StartHosting("Solo", TestPort + 1));
            yield return null;
            yield return null;
            var race = (HostRace)_screen.Session.Race;
            Assert.IsTrue(race.Start(laps: 1));

            yield return new WaitForSeconds(1f);
            Assert.AreEqual(RacePhase.Countdown, race.Phase);
            Assert.IsTrue(_screen.Car.InputLocked);
            Assert.That(_screen.Car.ForwardSpeed, Is.EqualTo(0f).Within(0.5f), "held on the grid despite full throttle");

            yield return new WaitForSeconds((HostRace.StartLeadMs + HostRace.CountdownMs) / 1000f);
            Assert.AreEqual(RacePhase.Racing, race.Phase);
            Assert.IsFalse(_screen.Car.InputLocked);

            // Driving off the grid crosses the start line, checkpoint 0.
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(1, race.Tracker.Get(0).CheckpointsPassed);
        }

        [UnityTest]
        public IEnumerator AServerHostsWithNoCarOfItsOwn()
        {
            _screen = TrackScreen.CreateServer(() => NetworkSession.StartServer("Test server", TestPort + 2));
            yield return null;
            yield return null;

            var session = _screen.Session;
            Assert.IsFalse(session.Ended, session.Status);
            Assert.IsTrue(session.IsHost);
            Assert.IsTrue(session.IsDedicated);
            Assert.IsFalse(session.TryGetLocalPlayerId(out _));
            Assert.AreEqual(0, session.Players.Count);
            Assert.IsNull(_screen.Car, "a server has no car");
            Assert.IsFalse(session.LeadsRoom, "nobody runs an empty room");
            Assert.IsFalse(session.CanStart);
            Assert.IsNotNull(_screen.GetComponent<HostLobby>());
            Assert.IsNull(_screen.GetComponent<SessionOverlay>(), "nothing is drawn on a server");
        }

        [UnityTest]
        public IEnumerator TheStartCommandStartsTheEventTheSettingsSay()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 3));
            yield return null;
            yield return null;

            var session = _screen.Session;
            var settings = LobbySettings.Default;
            settings.Laps = 2;
            settings.Track = 1;
            session.ChangeSettings(settings);
            session.Command(LobbyCommand.Start);

            var race = session.Race;
            Assert.AreEqual(RacePhase.Countdown, race.Phase);
            Assert.AreEqual(2, race.LapCount);
            Assert.AreEqual(1, race.TrackIndex);
        }

        [UnityTest]
        public IEnumerator MonsterJamPutsTheCarInTheStadiumAsATruck()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 4));
            yield return null;
            yield return null;

            var session = _screen.Session;
            var settings = LobbySettings.Default;
            settings.Mode = CarRacer.Core.Arena.GameMode.Monster;
            session.ChangeSettings(settings);
            session.Command(LobbyCommand.Start);
            yield return null;
            yield return null;

            Assert.AreEqual(CarRacer.Core.Arena.GameMode.Monster, session.Race.Mode);
            Assert.IsTrue(_screen.Car.MonsterTruck, "the car handles as a monster truck");
            var centre = CarRacer.Core.Arena.ArenaLayout.MonsterCentre.ToUnity();
            Assert.Less(Vector3.Distance(_screen.Car.BodyPosition, centre), CarRacer.Core.Arena.MonsterRules.StadiumRadius);
            Assert.IsNotNull(GameObject.Find("Ramp 0"), "the stadium has its ramps");
            Assert.IsNull(_screen.CameraRig.ArenaCentre, "Monster Jam keeps the chase camera");
        }

        [UnityTest]
        public IEnumerator BotsFillTheGridAreHeldUntilGoThenDriveOffAndReportCheckpoints()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 5));
            yield return null;
            yield return null;

            var session = _screen.Session;
            var settings = LobbySettings.Default;
            settings.BotFill = 3;
            session.ChangeSettings(settings);
            session.Command(LobbyCommand.Start);
            yield return null;
            yield return null;

            var botIds = new System.Collections.Generic.List<byte>();
            foreach (var player in session.Players)
            {
                if (player.IsBot)
                {
                    botIds.Add(player.PlayerId);
                }
            }

            Assert.AreEqual(2, botIds.Count, "the bots fill the grid up to the setting");
            var bots = _screen.GetComponent<BotCars>();
            var race = (HostRace)session.Race;
            var grid = new Vector3[botIds.Count];
            for (int i = 0; i < botIds.Count; i++)
            {
                Assert.IsTrue(bots.TryGetPosition(botIds[i], out grid[i]), "every bot in the roster has a car");
            }

            Assert.AreEqual(RacePhase.Countdown, race.Phase);
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < botIds.Count; i++)
            {
                bots.TryGetPosition(botIds[i], out var now);
                Assert.Less(Vector3.Distance(grid[i], now), 0.5f, "a bot is held on the grid through the countdown");
            }

            yield return new WaitForSeconds((HostRace.StartLeadMs + HostRace.CountdownMs) / 1000f);
            Assert.AreEqual(RacePhase.Racing, race.Phase);

            // Driving off the grid crosses the start line, checkpoint 0.
            yield return new WaitForSeconds(3f);
            foreach (byte id in botIds)
            {
                Assert.GreaterOrEqual(race.Tracker.Get(id).CheckpointsPassed, 1, "a bot's checkpoints are reported to the referee");
            }
        }

        private static byte[] AcceptedFrame() =>
            MessageCodec.EncodeFrame(new JoinReplyMessage { Result = JoinResult.Accepted, PlayerId = 3 });

        private NetworkSession JoinNobody() => _session = NetworkSession.Join("Guest", "127.0.0.1", NobodyPort);

        [UnityTest]
        public IEnumerator AHostThatCannotBindItsPortEndsWithWhy()
        {
            // The transport logs an error when another socket holds the port.
            LogAssert.ignoreFailingMessages = true;
            using (new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, ServerPort)))
            {
                _session = NetworkSession.StartServer("Test server", ServerPort);
                yield return null;
            }

            Assert.IsTrue(_session.IsHost);
            Assert.IsTrue(_session.Ended);
            Assert.AreEqual($"Couldn't host on port {ServerPort}", _session.Status);
        }

        [UnityTest]
        public IEnumerator AGuestWaitsToBeLetIn()
        {
            JoinNobody();
            yield return null;

            Assert.IsFalse(_session.IsHost);
            Assert.IsFalse(_session.Ended, _session.Status);
            Assert.AreEqual(GuestState.Connecting, _session.GuestSide.State);
            StringAssert.StartsWith("Connecting to 127.0.0.1", _session.Status);
            Assert.IsFalse(_session.TryGetLocalPlayerId(out _));
        }

        [UnityTest]
        public IEnumerator AnAcceptedGuestIsJoined()
        {
            JoinNobody();
            yield return null;

            _session.Receive(GuestSession.HostConnection, AcceptedFrame());

            Assert.IsFalse(_session.Ended, _session.Status);
            Assert.AreEqual("Joined", _session.Status);
            Assert.IsTrue(_session.TryGetLocalPlayerId(out byte me));
            Assert.AreEqual(3, me);
        }

        [UnityTest]
        public IEnumerator ARefusedGuestEndsWithWhy()
        {
            JoinNobody();
            yield return null;

            _session.Receive(
                GuestSession.HostConnection,
                MessageCodec.EncodeFrame(new JoinReplyMessage { Result = JoinResult.EventInProgress }));

            Assert.IsTrue(_session.Ended);
            Assert.AreEqual(GuestSession.RefusalText(JoinResult.EventInProgress), _session.Status);
            Assert.IsFalse(_session.TryGetLocalPlayerId(out _));
        }

        [UnityTest]
        public IEnumerator AGuestGivesUpOnAHostThatGoesQuiet()
        {
            // This guest joins a host that isn't there and pings it all the same, which may make the transport log
            // errors; what's checked is that the guest gives up.
            LogAssert.ignoreFailingMessages = true;
            JoinNobody();
            yield return null;
            _session.Receive(GuestSession.HostConnection, AcceptedFrame());
            Assert.IsFalse(_session.Ended, _session.Status);

            yield return new WaitForSecondsRealtime(GuestSession.GiveUpAfterMs / 1000f + 1f);

            Assert.IsTrue(_session.Ended);
            Assert.AreEqual("Lost the connection to the host", _session.Status);
        }

        [UnityTest]
        public IEnumerator AGuestDropsMalformedMessagesAndCarriesOn()
        {
            JoinNobody();
            yield return null;

            foreach (byte[] frame in MalformedFrames)
            {
                LogAssert.Expect(LogType.Warning, new Regex("Dropped a malformed message from client 0"));
                _session.Receive(GuestSession.HostConnection, frame);
            }

            Assert.IsFalse(_session.Ended, _session.Status);
            Assert.AreEqual(GuestState.Connecting, _session.GuestSide.State);
            _session.Receive(GuestSession.HostConnection, AcceptedFrame());
            Assert.AreEqual("Joined", _session.Status);
        }

        [UnityTest]
        public IEnumerator AHostDropsMalformedMessagesAndCarriesOn()
        {
            _session = NetworkSession.StartServer("Test server", ServerPort);
            yield return null;

            foreach (byte[] frame in MalformedFrames)
            {
                LogAssert.Expect(LogType.Warning, new Regex("Dropped a malformed message from client 9"));
                _session.Receive(9, frame);
            }

            Assert.IsFalse(_session.Ended, _session.Status);
            Assert.AreEqual(0, _session.Players.Count);
        }

        private sealed class FullThrottle : ICarInputSource
        {
            public CarInput Read() => new CarInput(0f, 1f);
        }
    }
}
