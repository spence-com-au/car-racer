using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Bots;
using CarRacer.Core.Driving;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Physics;
using CarRacer.Core.Race;
using CarRacer.Game.Driving;
using CarRacer.Game.Items;
using CarRacer.Game.Net;
using CarRacer.Game.Screens;
using UnityEngine;
using Vec3 = CarRacer.Core.Numerics.Vec3;

namespace CarRacer.Game.Bots
{
    /// <summary>
    /// The host's bots on the track (design doc §9). Each bot in the roster gets a real car, with
    /// physics and walls like anyone's, driven by a <see cref="BotDriver"/>. The host lines it up
    /// on the grid and holds it through the countdown, reports its checkpoints, picks up and uses
    /// its items, bumps it off other phones' cars, applies the item hits aimed at it, and sends its
    /// state to everyone at 30 Hz with the <see cref="CarStateFlags.BotControlled"/> flag. A bot
    /// that takes over a player who left mid-race (design doc §12) carries on from where their car
    /// was, at its speed and with its laps. In Sumo and Battle an <see cref="ArenaBotDriver"/> drives
    /// it instead, its spin-outs are reported to the referee, and it stops once it is out. In
    /// Monster Jam it is a monster truck driven by a <see cref="MonsterBotDriver"/>, and its jumps
    /// and crushes are reported for its score.
    /// </summary>
    public sealed class BotCars : MonoBehaviour
    {
        private sealed class Bot : ICarInputSource
        {
            public byte PlayerId;
            public Color Colour;
            public CarController Car;
            public BotDriver Driver;
            public ArenaBotDriver ArenaDriver;
            public MonsterBotDriver MonsterDriver;

            /// <summary>Whether the car is drawn as a monster truck now.</summary>
            public bool Truck;
            public bool WasSpunOut;
            public BotSkill Skill;
            public CheckpointDetector Detector;

            /// <summary>The track <see cref="Driver"/> and <see cref="Detector"/> were made for.</summary>
            public int TrackIndex;
            public readonly Shield Shield = new Shield();
            public BumpMonitor Bumps;
            public Transform ShieldRing;
            public Vector3 LastPosition;
            public float HeldSeconds;
            public uint Sequence;
            public BotCars Owner;

            public CarInput Read()
            {
                if (MonsterDriver != null)
                {
                    return MonsterDriver.Drive(Car.State, Car.BodyPosition.ToCore(), Owner.RivalsOf(PlayerId));
                }

                if (ArenaDriver == null)
                {
                    return Driver.Drive(Car.State, Car.BodyPosition.ToCore(), Time.fixedDeltaTime, Owner.RivalsOf(PlayerId));
                }

                var mode = Owner._session.Race.Mode;
                float radius = mode == GameMode.Sumo ? ((HostRace)Owner._session.Race).PlatformRadius : BattleRules.BowlRadius;
                return ArenaDriver.Drive(Car.State, Car.BodyPosition.ToCore(), ArenaLayout.CentreFor(mode), radius, Owner.RivalsOf(PlayerId));
            }
        }

        private readonly Dictionary<byte, Bot> _bots = new Dictionary<byte, Bot>();
        private readonly HashSet<byte> _inRoster = new HashSet<byte>();
        private readonly List<byte> _gone = new List<byte>();
        private readonly List<Vec3> _rivals = new List<Vec3>();
        private readonly List<OtherCar> _others = new List<OtherCar>();
        private readonly List<Bump> _bumps = new List<Bump>();

        /// <summary>Shared by every bot's bump monitor; Monster Jam's trucks hit harder.</summary>
        private readonly BumpSettings _bumpSettings = new BumpSettings();
        private float _carExaggeration;
        private TrackScreen _screen;
        private NetworkSession _session;
        private NetworkedCars _cars;
        private RacePhase _phase = RacePhase.Lobby;
        private float _sendTimer;

        public void Initialise(TrackScreen screen, NetworkSession session, NetworkedCars cars)
        {
            _screen = screen;
            _session = session;
            _cars = cars;
            _carExaggeration = _bumpSettings.Exaggeration;
            _session.HostSide.BotHitReceived += OnBotHit;
            _session.HostSide.PlayerReturned += OnPlayerReturned;
        }

        private void OnDestroy()
        {
            if (_session != null && _session.HostSide != null)
            {
                _session.HostSide.BotHitReceived -= OnBotHit;
                _session.HostSide.PlayerReturned -= OnPlayerReturned;
            }
        }

        /// <summary>Where a bot's car is, for item hits, standings and the mini-map.</summary>
        public bool TryGetPosition(byte playerId, out Vector3 position)
        {
            if (_bots.TryGetValue(playerId, out var bot))
            {
                position = bot.Car.BodyPosition;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>Where every bot is and its colour, for the mini-map.</summary>
        public IEnumerable<(Vector3 Position, Color Colour)> Dots
        {
            get
            {
                foreach (var bot in _bots.Values)
                {
                    yield return (bot.Car.BodyPosition, bot.Colour);
                }
            }
        }

        private void Update()
        {
            if (_session.Ended || _session.Race == null)
            {
                return;
            }

            SyncRoster();
            FollowRacePhase();
            foreach (var bot in _bots.Values)
            {
                bot.Shield.Tick(Time.deltaTime);
                ShieldRing.Show(bot.ShieldRing, bot.Shield.IsUp);
            }

            SendStates();
        }

        private void FixedUpdate()
        {
            if (_session.Ended || _phase != RacePhase.Racing)
            {
                return;
            }

            var race = (HostRace)_session.Race;
            var items = (HostItems)_session.Items;
            foreach (var bot in _bots.Values)
            {
                ResolveBumps(bot);
                FollowArenaState(bot, race);

                Vector3 position = bot.Car.BodyPosition;
                int crossed = bot.Detector.Update(bot.LastPosition.ToCore(), position.ToCore());
                bot.LastPosition = position;
                if (crossed >= 0)
                {
                    race.ReportBotCheckpoint(bot.PlayerId, crossed);
                }

                items.ClaimTouchedBoxForBot(bot.PlayerId, position.ToCore());
                UseItem(bot, items, race);
            }
        }

        /// <summary>Reports a bot's spin-outs (they cost a Battle life) and takes its controls away once it is out of an arena event.</summary>
        private static void FollowArenaState(Bot bot, HostRace race)
        {
            bool spun = bot.Car.State.SpunOut;
            if (spun && !bot.WasSpunOut)
            {
                race.ReportSpinOut(bot.PlayerId);
            }

            bot.WasSpunOut = spun;
            if (race.Mode != GameMode.Race)
            {
                bot.Car.InputLocked = NetworkedCars.IsOut(race, bot.PlayerId);
            }
        }

        private void UseItem(Bot bot, HostItems items, HostRace race)
        {
            var held = items.Slots.Held(bot.PlayerId);
            if (held == ItemKind.None)
            {
                bot.HeldSeconds = 0f;
                return;
            }

            bot.HeldSeconds += Time.fixedDeltaTime;
            if (bot.Car.Damage.Wrecked || !BotItems.ShouldUse(held, bot.HeldSeconds, bot.Skill, Situation(bot, race)))
            {
                return;
            }

            var used = items.UseHeldForBot(bot.PlayerId, bot.Car.BodyPosition.ToCore(), bot.Car.BodyVelocity.ToCore(), bot.Car.State.Heading);
            if (used == ItemKind.Boost)
            {
                bot.Car.Boost(bot.Car.Tuning.ItemBoostSpeed, bot.Car.Tuning.ItemBoostDuration);
            }
            else if (used == ItemKind.Shield)
            {
                bot.Shield.Raise();
            }
        }

        /// <summary>What the bot can see for its item decision: the nearest rivals in front and behind, the bend ahead, and whether anyone leads it.</summary>
        private BotItemSituation Situation(Bot bot, HostRace race)
        {
            return BotItems.Observe(
                bot.Car.BodyPosition.ToCore(),
                bot.Car.State.Forward,
                RivalsOf(bot.PlayerId),
                bot.ArenaDriver != null ? 0f : bot.Driver.CurvatureAhead,
                race.Standings,
                bot.PlayerId);
        }

        /// <summary>Every other car still in the event: this phone's own car, the other bots and the other phones' cars.</summary>
        private List<Vec3> RivalsOf(byte playerId)
        {
            _rivals.Clear();
            var race = _session.Race;
            if (_screen.Car != null && _session.TryGetLocalPlayerId(out byte me) && !NetworkedCars.IsOut(race, me))
            {
                _rivals.Add(_screen.Car.BodyPosition.ToCore());
            }

            foreach (var pair in _bots)
            {
                if (pair.Key != playerId && !NetworkedCars.IsOut(race, pair.Key))
                {
                    _rivals.Add(pair.Value.Car.BodyPosition.ToCore());
                }
            }

            foreach (var remote in _cars.RemoteCars)
            {
                if (remote.HasState && !NetworkedCars.IsOut(race, remote.PlayerId))
                {
                    _rivals.Add(remote.transform.position.ToCore());
                }
            }

            return _rivals;
        }

        /// <summary>
        /// Bumps a bot off other phones' cars, which have no collider here (design doc §2.2), and tells
        /// those phones in case they missed it. This phone's own car and the other bots are real
        /// physics bodies, so the physics engine handles those.
        /// </summary>
        private void ResolveBumps(Bot bot)
        {
            _others.Clear();
            foreach (var remote in _cars.RemoteCars)
            {
                if (remote.HasState)
                {
                    _others.Add(new OtherCar(remote.PlayerId, remote.transform.position.ToCore(), remote.Velocity.ToCore()));
                }
            }

            _bumps.Clear();
            bot.Bumps.Step(bot.PlayerId, bot.Car.BodyPosition.ToCore(), bot.Car.BodyVelocity.ToCore(), _others, NetworkSession.NowMs, _bumps);
            foreach (var bump in _bumps)
            {
                bot.Car.AddImpulse(bump.SelfImpulse.ToUnity());
                bot.Car.TakeBump(bump.SelfImpulse.ToUnity());
                _session.SendHit(bump.Fallback);
                foreach (var other in _others)
                {
                    if (other.PlayerId == bump.OtherPlayerId)
                    {
                        ReportCrush(bot, bot.Car.BodyVelocity.ToCore(), other.PlayerId, other.Position, other.Velocity);
                    }
                }
            }
        }

        /// <summary>Monster Jam: a bot's truck landed a jump.</summary>
        private void OnBotLanded(Bot bot, float seconds)
        {
            if (_session.Race is HostRace race && race.Mode == GameMode.Monster)
            {
                race.ReportBotStunt(bot.PlayerId, StuntKind.Air, (long)(seconds * 1000f), 0);
            }
        }

        /// <summary>Monster Jam: a bot's truck, moving at <paramref name="velocity"/>, touched another car; a crush if it came down on it or did the hitting.</summary>
        private void ReportCrush(Bot bot, Vec3 velocity, byte victimId, Vec3 victimPosition, Vec3 victimVelocity)
        {
            if (_session.Race is HostRace race && race.Mode == GameMode.Monster
                && MonsterRules.IsCrush(bot.Car.BodyPosition.ToCore(), velocity, victimPosition, victimVelocity))
            {
                race.ReportBotStunt(bot.PlayerId, StuntKind.Crush, 0, victimId);
            }
        }

        private void OnBotHit(HitMessage hit)
        {
            if (!_bots.TryGetValue(hit.TargetId, out var bot))
            {
                return;
            }

            switch (ItemHitResponse.Resolve(hit, bot.Shield))
            {
                case ItemHitOutcome.Blocked:
                    break;
                case ItemHitOutcome.SpinOut:
                    bot.Car.AddImpulse(hit.Impulse.ToUnity());
                    bot.Car.SpinOut();
                    bot.Car.TakeItemHit();
                    break;
                default:
                    if (bot.Bumps.OnHit(hit, bot.PlayerId, NetworkSession.NowMs, out var impulse))
                    {
                        bot.Car.AddImpulse(impulse.ToUnity());
                        bot.Car.TakeBump(impulse.ToUnity());
                    }

                    break;
            }
        }

        private void FollowRacePhase()
        {
            var phase = _session.Race.Phase;
            if (phase == _phase)
            {
                return;
            }

            bool lineUp = phase == RacePhase.Countdown || (phase == RacePhase.Racing && _phase != RacePhase.Countdown);
            if (lineUp)
            {
                _screen.FollowEventTrack(_session.Race);
            }

            bool trucks = phase != RacePhase.Lobby && _session.Race.Mode == GameMode.Monster;
            _bumpSettings.Exaggeration = trucks ? MonsterRules.BumpExaggeration : _carExaggeration;
            foreach (var bot in _bots.Values)
            {
                if (lineUp)
                {
                    var mode = _session.Race.Mode;
                    bot.ArenaDriver = ArenaDriverFor(bot, mode);
                    bot.MonsterDriver = MonsterDriverFor(bot, mode);
                    LineUp(bot, mode);
                }

                // Monster Jam's trucks for the event, back to cars for the lobby (this resets the handling, so it comes first).
                DressAsTruck(bot, trucks);
                bot.Car.InputLocked = phase != RacePhase.Racing;
                var rules = _session.Race.Rules;
                bot.Car.DamageEnabled = phase == RacePhase.Racing && _session.Race.Mode == GameMode.Race && rules.Damage;
                bot.Car.UseBodyStats(phase == RacePhase.Lobby || rules.CarStats);
            }

            _phase = phase;
        }

        /// <summary>
        /// A guest whose car a bot drove while their app was away is back (design doc §12): the bot
        /// goes, and the guest is told where the car got to and how many checkpoints it has crossed.
        /// </summary>
        private void OnPlayerReturned(byte playerId)
        {
            if (!_bots.TryGetValue(playerId, out var bot))
            {
                return;
            }

            var racer = (_session.Race as HostRace)?.Tracker?.Get(playerId);
            _session.HostSide.SendControlReturned(new ControlReturnedMessage
            {
                PlayerId = playerId,
                Position = bot.Car.BodyPosition.ToCore(),
                Velocity = bot.Car.BodyVelocity.ToCore(),
                HeadingRadians = bot.Car.State.Heading,
                CheckpointsPassed = (ushort)(racer != null ? racer.CheckpointsPassed : 0),
            });
            Destroy(bot.Car.gameObject);
            _bots.Remove(playerId);
        }

        /// <summary>Puts a bot in a departed player's car: where it was, moving as it was, with its race progress.</summary>
        private void TakeOver(Bot bot, Vector3 position, float headingRadians, Vector3 velocity)
        {
            bot.Car.Place(position, headingRadians);
            bot.Car.AddImpulse(velocity * bot.Car.GetComponent<Rigidbody>().mass);
            bot.LastPosition = position;
            var racer = (_session.Race as HostRace)?.Tracker?.Get(bot.PlayerId);
            bot.Detector.Resume(racer != null ? racer.CheckpointsPassed : 0);
            bot.ArenaDriver = ArenaDriverFor(bot, CurrentMode);
            bot.MonsterDriver = MonsterDriverFor(bot, CurrentMode);
            DressAsTruck(bot, CurrentMode == GameMode.Monster);
            bot.Car.InputLocked = _session.Race.Phase != RacePhase.Racing;
            var rules = _session.Race.Rules;
            bot.Car.DamageEnabled = _session.Race.Phase == RacePhase.Racing && _session.Race.Mode == GameMode.Race && rules.Damage;
            bot.Car.UseBodyStats(_session.Race.Phase == RacePhase.Lobby || rules.CarStats);
        }

        /// <summary>The mode of the event under way, or Race in the lobby.</summary>
        private GameMode CurrentMode => _session.Race.Phase == RacePhase.Lobby ? GameMode.Race : _session.Race.Mode;

        private static ArenaBotDriver ArenaDriverFor(Bot bot, GameMode mode) =>
            mode == GameMode.Sumo || mode == GameMode.Battle ? new ArenaBotDriver(bot.Skill, bot.Car.Tuning, mode, bot.PlayerId) : null;

        private static MonsterBotDriver MonsterDriverFor(Bot bot, GameMode mode) =>
            mode == GameMode.Monster
                ? new MonsterBotDriver(bot.Skill, bot.Car.Tuning, ArenaLayout.MonsterRamps(ArenaLayout.MonsterCentre), ArenaLayout.MonsterCentre, MonsterRules.StadiumRadius, bot.PlayerId)
                : null;

        /// <summary>Draws the bot's car as a monster truck (or back as a car) and gives it the truck's handling.</summary>
        private void DressAsTruck(Bot bot, bool truck)
        {
            bot.Car.MonsterTruck = truck;
            if (bot.Truck == truck)
            {
                return;
            }

            var player = FindPlayer(bot.PlayerId);
            CarFactory.ReplaceBody(bot.Car.gameObject, bot.Colour, CarModels.ForIndex(player.BodyIndex), truck);
            bot.Truck = truck;
        }

        private RosterEntry FindPlayer(byte playerId)
        {
            foreach (var player in _session.Players)
            {
                if (player.PlayerId == playerId)
                {
                    return player;
                }
            }

            return default;
        }

        private void LineUp(Bot bot, GameMode mode)
        {
            if (bot.TrackIndex != _screen.TrackIndex)
            {
                bot.Driver = new BotDriver(_screen.Track.Spline, bot.Skill, bot.Car.Tuning, bot.PlayerId);
                bot.Detector = new CheckpointDetector(_screen.Track.CheckpointGates());
                bot.TrackIndex = _screen.TrackIndex;
            }

            _screen.PlaceAtStart(bot.Car, _session.Race.GridSlotOf(bot.PlayerId), mode);
            bot.WasSpunOut = false;
            bot.Detector.Reset();
            bot.Driver.Reset();
            bot.Shield.Clear();
            bot.HeldSeconds = 0f;
            bot.LastPosition = bot.Car.BodyPosition;
        }

        /// <summary>Builds a car for each new bot in the roster and removes the cars of bots that have gone.</summary>
        private void SyncRoster()
        {
            _inRoster.Clear();
            foreach (var player in _session.Players)
            {
                if (!player.IsBot)
                {
                    continue;
                }

                _inRoster.Add(player.PlayerId);
                if (!_bots.ContainsKey(player.PlayerId))
                {
                    var colour = CarColours.ForIndex(player.ColourIndex);
                    var car = CarFactory.Build($"Bot {player.Name}", colour, _screen.transform, haptics: false, body: CarModels.ForIndex(player.BodyIndex));
                    var skill = BotSkill.For(_session.HostSide.BotDifficulty);
                    var bot = new Bot
                    {
                        PlayerId = player.PlayerId,
                        Colour = colour,
                        Car = car,
                        Skill = skill,
                        Driver = new BotDriver(_screen.Track.Spline, skill, car.Tuning, player.PlayerId),
                        Detector = new CheckpointDetector(_screen.Track.CheckpointGates()),
                        TrackIndex = _screen.TrackIndex,
                        Owner = this,
                        Bumps = new BumpMonitor(_bumpSettings),
                    };
                    bot.ShieldRing = ShieldRing.Build(car.transform);
                    car.InputSource = bot;
                    car.PlayerId = player.PlayerId;
                    car.Landed += seconds => OnBotLanded(bot, seconds);
                    // A physics contact has already been resolved by now, so judge it on the speeds both cars hit at.
                    car.TouchedCar += other => ReportCrush(bot, car.PreImpactVelocity.ToCore(), other.PlayerId, other.BodyPosition.ToCore(), other.PreImpactVelocity.ToCore());
                    if (_cars.TryTakeOverRemote(player.PlayerId, out var at, out float heading, out var velocity))
                    {
                        TakeOver(bot, at, heading, velocity);
                    }
                    else
                    {
                        car.InputLocked = true;
                        bot.ArenaDriver = ArenaDriverFor(bot, CurrentMode);
                        bot.MonsterDriver = MonsterDriverFor(bot, CurrentMode);
                        DressAsTruck(bot, CurrentMode == GameMode.Monster);
                        LineUp(bot, CurrentMode);
                    }

                    _bots[player.PlayerId] = bot;
                }
            }

            _gone.Clear();
            foreach (byte id in _bots.Keys)
            {
                if (!_inRoster.Contains(id))
                {
                    _gone.Add(id);
                }
            }

            foreach (byte id in _gone)
            {
                Destroy(_bots[id].Car.gameObject);
                _bots.Remove(id);
            }
        }

        private void SendStates()
        {
            _sendTimer += Time.deltaTime;
            if (_sendTimer < NetworkedCars.SendIntervalSeconds)
            {
                return;
            }

            _sendTimer = Mathf.Min(_sendTimer - NetworkedCars.SendIntervalSeconds, NetworkedCars.SendIntervalSeconds);
            long now = NetworkSession.NowMs;
            foreach (var bot in _bots.Values)
            {
                var state = bot.Car.State;
                var flags = CarStateFlags.BotControlled;
                if (state.Drifting)
                {
                    flags |= CarStateFlags.Drifting;
                }

                if (state.BoostSecondsLeft > 0f)
                {
                    flags |= CarStateFlags.Boosting;
                }

                if (bot.Shield.IsUp)
                {
                    flags |= CarStateFlags.Shielded;
                }

                if (state.SpunOut)
                {
                    flags |= CarStateFlags.SpunOut;
                }

                flags |= DamageFlags.For(bot.Car.Damage.SmokeLevel, bot.Car.Damage.Wrecked);
                _session.SendCarState(new CarStateMessage
                {
                    PlayerId = bot.PlayerId,
                    Sequence = ++bot.Sequence,
                    SentAtMs = now,
                    Position = bot.Car.BodyPosition.ToCore(),
                    Velocity = bot.Car.BodyVelocity.ToCore(),
                    HeadingRadians = state.Heading,
                    Flags = flags,
                });
            }
        }
    }
}
