using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Discovery;
using CarRacer.Core.Driving;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
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
    /// Starts a real NGO host from code (one process can't also join it). The session protocol
    /// itself is tested by `dotnet test`; this checks the NGO and Unity Transport setup. A guest is
    /// played by feeding `HostSide.OnMessage` what its phone would send, which exercises the Game
    /// glue that follows the roster (remote cars, a bot taking a car over and handing it back).
    /// </summary>
    public class NetworkSessionTests
    {
        private const ushort TestPort = 47790;

        /// <summary>
        /// The connection a simulated guest speaks from. NGO knows no client by it and drops what the host sends it, so
        /// each test says what the guest sends next.
        /// </summary>
        private const ulong GuestConnection = 100;

        private readonly Dictionary<string, int> _savedSettings = new Dictionary<string, int>();
        private string _savedCup;
        private TrackScreen _screen;

        // Hosting saves the lobby settings to `PlayerPrefs` and loads them back on the next host, so each test starts
        // from the defaults and leaves the developer's own saved settings as it found them.
        [SetUp]
        public void SetUp()
        {
            _savedSettings.Clear();
            foreach (string key in HostLobby.SettingKeys)
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
            foreach (string key in HostLobby.SettingKeys)
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
        public IEnumerator DestroyTheScreen()
        {
            if (_screen != null)
            {
                Object.Destroy(_screen.gameObject);
            }

            // Let NGO shut down before anything else starts a NetworkManager. This runs even when an assertion failed.
            yield return null;
            yield return null;
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
        public IEnumerator HostingAgainBringsBackTheSettingsTheLastHostSaved()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 6));
            yield return null;
            yield return null;

            // Each setting differs from its default, and from every other setting, so a swapped key shows.
            var changed = new LobbySettings
            {
                Mode = CarRacer.Core.Arena.GameMode.Monster,
                Laps = 7,
                Track = 6,
                Rules = new EventRules(damage: false, items: true, carStats: false),
                BotFill = 4,
                BotDifficulty = CarRacer.Core.Bots.BotDifficulty.Hard,
                Cup = 1,
                ReverseGrid = false,
            };
            _screen.Session.ChangeSettings(changed);

            Object.Destroy(_screen.gameObject);
            yield return null;
            yield return null;

            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 7));
            yield return null;
            yield return null;

            var loaded = _screen.Session.Settings;
            Assert.AreEqual(changed.Mode, loaded.Mode);
            Assert.AreEqual(changed.Laps, loaded.Laps);
            Assert.AreEqual(changed.Track, loaded.Track);
            Assert.AreEqual(changed.Rules.ToByte(), loaded.Rules.ToByte());
            Assert.AreEqual(changed.BotFill, loaded.BotFill);
            Assert.AreEqual(changed.BotDifficulty, loaded.BotDifficulty);
            Assert.AreEqual(changed.Cup, loaded.Cup);
            Assert.AreEqual(changed.ReverseGrid, loaded.ReverseGrid);
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

        [UnityTest]
        public IEnumerator AGuestsCarIsShownWhereItReportsItself()
        {
            byte guest = HostWithAGuest(TestPort + 8);
            yield return null;
            yield return null;

            var session = _screen.Session;
            var cars = _screen.GetComponent<NetworkedCars>();
            var remote = cars.RemoteCars.Single();
            Assert.AreEqual(guest, remote.PlayerId);
            Assert.IsFalse(remote.HasState, "nothing is shown until the guest reports");
            Assert.IsFalse(cars.TryGetCarPosition(guest, out _));

            var reported = OnTheRoad(40f);
            GuestReports(session, guest, 1, reported, heading: 1f);
            yield return null;
            yield return null;

            Assert.IsTrue(remote.HasState);
            Assert.That(Vector3.Distance(remote.transform.position, reported), Is.LessThan(0.01f));
            Assert.That(remote.HeadingRadians, Is.EqualTo(1f).Within(1e-4f));
            Assert.IsTrue(cars.TryGetCarPosition(guest, out var seen), "the host sees the car for its item hits");
            Assert.That(Vector3.Distance(seen.ToUnity(), reported), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator AGuestWhoPicksAnotherCarIsRestyledInPlace()
        {
            byte guest = HostWithAGuest(TestPort + 9);
            yield return null;
            yield return null;

            var session = _screen.Session;
            var cars = _screen.GetComponent<NetworkedCars>();
            var remote = cars.RemoteCars.Single();
            int guestBody = session.Players.Single(p => p.PlayerId == guest).BodyIndex;
            byte body = (byte)((guestBody + 1) % LobbyRoster.BodyCount);
            byte colour = FreeColour(session);
            var change = new ChangeCarMessage { BodyIndex = body, ColourIndex = colour };
            session.HostSide.OnMessage(GuestConnection, change);
            yield return null;
            yield return null;

            var entry = session.Players.Single(p => p.PlayerId == guest);
            Assert.AreEqual(body, entry.BodyIndex);
            Assert.AreEqual(colour, entry.ColourIndex);
            Assert.AreEqual(CarColours.ForIndex(colour), remote.Colour);
            Assert.AreSame(remote, cars.RemoteCars.Single(), "the car is repainted, not replaced");
        }

        [UnityTest]
        public IEnumerator AGuestWhoLeavesTheLobbyTakesTheirCarWithThem()
        {
            HostWithAGuest(TestPort + 10);
            yield return null;
            yield return null;

            var cars = _screen.GetComponent<NetworkedCars>();
            var remote = cars.RemoteCars.Single();
            _screen.Session.HostSide.OnDisconnected(GuestConnection);
            yield return null;
            yield return null;

            Assert.AreEqual(1, _screen.Session.Players.Count);
            Assert.IsEmpty(cars.RemoteCars);
            Assert.IsTrue(remote == null, "the guest's car is destroyed");
        }

        [UnityTest]
        public IEnumerator AGuestsBumpReportPushesTheHostsCar()
        {
            byte guest = HostWithAGuest(TestPort + 11);
            yield return null;
            yield return null;

            var session = _screen.Session;
            Assert.IsTrue(session.TryGetLocalPlayerId(out byte me));
            var pushes = new List<Vector3>();
            _screen.Car.Pushed += pushes.Add;

            // The host takes a guest's bump report only from a car it saw within reach of its own car at its last
            // physics step: past touching (2 m) but well inside `BumpMonitor.FallbackReach` (8 m).
            GuestReports(session, guest, 1, _screen.Car.BodyPosition + new Vector3(3f, 0f, 0f));
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsEmpty(pushes, "a car 3 m away is no contact");

            session.HostSide.OnMessage(GuestConnection, new HitMessage
            {
                AttackerId = guest,
                TargetId = me,
                SentAtMs = NetworkSession.NowMs,
                Impulse = new Vec3(2f, 0f, 0f),
            });

            Assert.AreEqual(1, pushes.Count);
            Assert.That(pushes[0].x, Is.EqualTo(2f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator AnItemHitSpinsTheHostsCarOutUnlessAShieldSoaksItUp()
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", TestPort + 12));
            yield return null;
            yield return null;

            var session = _screen.Session;
            Assert.IsTrue(session.TryGetLocalPlayerId(out byte me));
            var car = _screen.Car;
            var hit = new HitMessage
            {
                AttackerId = 1,
                TargetId = me,
                SentAtMs = NetworkSession.NowMs,
                Impulse = new Vec3(0f, 0f, 3f),
                IsItemHit = true,
            };

            var shield = new Shield();
            shield.Raise();
            NetworkedCars.TakeHit(car, shield, new BumpMonitor(), me, hit);
            Assert.IsFalse(car.State.SpunOut, "a shield soaks up the hit");
            Assert.IsFalse(shield.IsUp, "and drops");

            // The way the host's item referee delivers one aimed at the host's own car.
            session.SendHit(hit);
            Assert.IsTrue(car.State.SpunOut);
        }

        [UnityTest]
        public IEnumerator TheHostsCarIsPushedOffAGuestsCarItRunsInto()
        {
            byte guest = HostWithAGuest(TestPort + 13);
            yield return null;
            yield return null;

            var session = _screen.Session;
            var pushes = new List<Vector3>();
            _screen.Car.Pushed += pushes.Add;
            var at = _screen.Car.BodyPosition;

            // A shown car moves to its latest report on the next frame, until when a contact is judged against where
            // it was seen before, so report it far off first.
            GuestReports(session, guest, 1, at + new Vector3(30f, 0f, 0f));
            yield return null;
            yield return null;
            Assert.IsEmpty(pushes, "a car 30 m away is no bump");

            // Closing on the host's car from its +x side. The host shows a car where it will probably be by now, at
            // most 250 ms on, so it gets no nearer than 1 m and stays in contact on the same side.
            GuestReports(session, guest, 2, at + new Vector3(1.5f, 0f, 0f), new Vector3(-2f, 0f, 0f));
            float deadline = Time.realtimeSinceStartup + 3f;
            while (pushes.Count == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsNotEmpty(pushes, "the host's car is bumped");
            Assert.Less(pushes[0].x, 0f, "away from the guest's car");
        }

        [UnityTest]
        public IEnumerator ABotTakesOverTheCarOfAGuestWhoLeavesMidRaceFromWhereItWas()
        {
            byte guest = HostWithAGuest(TestPort + 14);
            yield return null;
            yield return null;

            var session = _screen.Session;
            var cars = _screen.GetComponent<NetworkedCars>();
            var bots = _screen.GetComponent<BotCars>();
            Assert.IsTrue(((HostRace)session.Race).Start(laps: 1));
            yield return null;
            yield return null;

            // Well past the grid, where a bot that started the event would line up instead.
            var seenAt = OnTheRoad(40f);
            GuestReports(session, guest, 1, seenAt);
            yield return null;
            yield return null;
            var remote = cars.RemoteCars.Single();
            Assert.IsTrue(remote.HasState);

            session.HostSide.OnDisconnected(GuestConnection);
            yield return null;
            yield return null;

            Assert.IsTrue(session.HostSide.IsBot(guest));
            Assert.IsTrue(bots.TryGetPosition(guest, out var botAt), "a bot drives the car");
            Assert.That(FlatDistance(botAt, seenAt), Is.LessThan(0.5f), "from where the guest's car was seen");
            Assert.IsEmpty(cars.RemoteCars);
            Assert.IsTrue(remote == null, "the bot replaces the guest's remote car");
        }

        [UnityTest]
        public IEnumerator AGuestWhoGoesQuietIsReplacedByABotUntilTheyAreBack()
        {
            byte guest = HostWithAGuest(TestPort + 15);
            yield return null;
            yield return null;

            var session = _screen.Session;
            var cars = _screen.GetComponent<NetworkedCars>();
            var bots = _screen.GetComponent<BotCars>();
            Assert.IsTrue(((HostRace)session.Race).Start(laps: 1));
            yield return null;
            yield return null;
            GuestReports(session, guest, 1, OnTheRoad(40f));
            yield return null;
            yield return null;
            var remote = cars.RemoteCars.Single();

            // The host's clock is real time, so wait for it.
            float deadline = Time.realtimeSinceStartup + HostSession.AwayAfterMs / 1000f + 5f;
            while (!session.HostSide.IsAway(guest) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(session.HostSide.IsAway(guest), "the host gives up on a silent guest");
            yield return null;
            yield return null;
            Assert.IsTrue(bots.TryGetPosition(guest, out _), "a bot drives the car");
            Assert.IsEmpty(cars.RemoteCars);
            Assert.IsTrue(remote == null, "the bot replaces the guest's remote car");

            // Anything from the guest is a sign of life.
            GuestReports(session, guest, 2, OnTheRoad(40f));
            Assert.IsFalse(session.HostSide.IsBot(guest));
            Assert.IsFalse(bots.TryGetPosition(guest, out _), "the bot gives the car back");
            yield return null;
            yield return null;

            var back = cars.RemoteCars.Single();
            Assert.AreEqual(guest, back.PlayerId);
            Assert.IsFalse(back.HasState, "the host ignores the guest's reports until it echoes where its car was handed back");

            // As the phone does on taking the car back, which lets its reports through.
            session.HostSide.OnMessage(GuestConnection, new ControlReturnedMessage { PlayerId = guest });
            GuestReports(session, guest, 3, OnTheRoad(60f));
            Assert.IsTrue(back.HasState);
        }

        /// <summary>
        /// Hosts, and lets in a guest that has no phone behind it (see <see cref="GuestConnection"/>). Both cars appear
        /// in the next frame.
        /// </summary>
        private byte HostWithAGuest(ushort port)
        {
            _screen = TrackScreen.Create(new NoInput(), () => NetworkSession.StartHosting("Host", port));
            var session = _screen.Session;
            session.HostSide.OnMessage(GuestConnection, new JoinRequestMessage
            {
                ProtocolVersion = DiscoveryConstants.ProtocolVersion,
                PlayerName = "Guest",
            });

            Assert.AreEqual(2, session.Players.Count, "the guest is let in");
            return session.Players.Single(p => !p.IsHost).PlayerId;
        }

        /// <summary>The guest's phone reports its car, as it does 30 times a second.</summary>
        private static void GuestReports(
            NetworkSession session,
            byte guest,
            uint sequence,
            Vector3 position,
            Vector3 velocity = default,
            float heading = 0f)
        {
            session.HostSide.OnMessage(GuestConnection, new CarStateMessage
            {
                PlayerId = guest,
                Sequence = sequence,
                SentAtMs = NetworkSession.NowMs,
                Position = position.ToCore(),
                Velocity = velocity.ToCore(),
                HeadingRadians = heading,
            });
        }

        /// <summary>Where a car sits on the track's centre line, <paramref name="distance"/> m past the start line.</summary>
        private Vector3 OnTheRoad(float distance)
        {
            var centre = _screen.Track.Spline.SampleAtDistance(distance).Position.ToUnity();
            return centre + Vector3.up * (CarController.SphereRadius + 0.1f);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        /// <summary>A car colour nobody in the session has, to change to.</summary>
        private static byte FreeColour(NetworkSession session)
        {
            var colours = Enumerable.Range(0, LobbyRoster.ColourCount);
            return (byte)colours.First(colour => session.Players.All(p => p.ColourIndex != colour));
        }

        private sealed class FullThrottle : ICarInputSource
        {
            public CarInput Read() => new CarInput(0f, 1f);
        }
    }
}
