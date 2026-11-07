using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Physics;
using CarRacer.Core.Race;
using CarRacer.Core.Track;
using CarRacer.Game.Audio;
using CarRacer.Game.Bots;
using CarRacer.Game.Driving;
using CarRacer.Game.Items;
using CarRacer.Game.Screens;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// Puts everyone in a <see cref="NetworkSession"/> on the track (design doc §2) and runs this
    /// phone's part of a race (§7.2): spawns its car on its grid slot once it has a player id,
    /// sends its state at 30 Hz, shows every other player as a <see cref="RemoteCar"/> that
    /// follows the roster, bumps its car off the others (§2.2), holds the car on the grid through
    /// the countdown, and reports the checkpoints it crosses. In a race it also picks up item boxes
    /// and uses the held item (design doc §8), applying Boost and Shield to its own car at once,
    /// and takes item hits from the host (a shield soaks one up; otherwise the car spins out).
    /// In Sumo and Battle (§7.0) the cars start round the arena instead, a car that is out can't be
    /// driven any more, and the host tells its <see cref="HostRace"/> whenever a car spins out. In
    /// Monster Jam every car is drawn as a monster truck, this phone's truck handles like one, and
    /// its jumps and crushes go to the host for its score.
    /// </summary>
    public sealed class NetworkedCars : MonoBehaviour
    {
        public const float SendIntervalSeconds = 1f / 30f;

        private readonly Dictionary<byte, RemoteCar> _remotes = new Dictionary<byte, RemoteCar>();

        /// <summary>The body and colour each car was last built with, and whether as a truck, to notice a change of car or of event.</summary>
        private readonly Dictionary<byte, (byte Body, byte Colour, bool Truck)> _looks = new Dictionary<byte, (byte, byte, bool)>();
        private readonly HashSet<byte> _inRoster = new HashSet<byte>();
        private readonly List<byte> _gone = new List<byte>();
        private readonly BumpSettings _bumpSettings = new BumpSettings();
        private BumpMonitor _bumpMonitor;
        private float _carExaggeration;
        private readonly List<OtherCar> _others = new List<OtherCar>();
        private readonly List<Bump> _bumps = new List<Bump>();
        private readonly HashSet<byte> _spunOut = new HashSet<byte>();
        private readonly Shield _shield = new Shield();

        // Monster Jam: when this phone last showed a crush on each victim, so the popup keeps to the host's cooldown.
        private readonly Dictionary<byte, long> _lastCrushPopupMs = new Dictionary<byte, long>();
        private Transform _shieldRing;
        private BotCars _botCars;
        private TrackScreen _screen;
        private NetworkSession _session;
        private CheckpointDetector _detector;
        private int _detectorTrack;
        private RacePhase _phase = RacePhase.Lobby;

        /// <summary>The event under way took this phone's car off the track (an arena or the Monster Jam stadium), so the lobby brings it back.</summary>
        private bool _offTrack;
        private Vector3 _lastPosition;
        private bool _spawned;
        private bool _ownSpunOut;
        private byte _me;
        private uint _sequence;
        private float _sendTimer;

        /// <summary>This phone's own lap count and next checkpoint, for the HUD.</summary>
        public CheckpointDetector Detector => _detector;

        /// <summary>Every other player's car, for the mini-map.</summary>
        public IEnumerable<RemoteCar> RemoteCars => _remotes.Values;

        /// <summary>Monster Jam: this phone's truck scored, with a line to show ("Big air! +12").</summary>
        public event System.Action<string> StuntScored;


        public void Initialise(TrackScreen screen, NetworkSession session)
        {
            _screen = screen;
            _session = session;
            _bumpMonitor = new BumpMonitor(_bumpSettings);
            _carExaggeration = _bumpSettings.Exaggeration;
            _session.CarStateReceived += OnCarState;
            _session.HitReceived += OnHit;
            if (!session.IsHost)
            {
                session.GuestSide.ControlReturned += OnControlReturned;
            }

            _detector = new CheckpointDetector(screen.Track.CheckpointGates());
            _detectorTrack = screen.TrackIndex;

            // Every track's boxes and the Battle bowl's share one list: they're far apart, so each event only reaches its own.
            var boxes = new List<CarRacer.Core.Numerics.Vec3>();
            var checkpointCounts = new List<int>();
            foreach (var track in TrackCatalog.Layouts)
            {
                boxes.AddRange(track.ItemBoxPositions());
                checkpointCounts.Add(track.CheckpointCount);
            }

            boxes.AddRange(ArenaLayout.BattleItemBoxes(ArenaLayout.BattleCentre, BattleRules.BowlRadius));
            _session.CreateRace(checkpointCounts, FractionToNextCheckpoint, boxes, TryGetCarPosition);
            gameObject.AddComponent<ItemBoxes>().Initialise(_session.Items);
            gameObject.AddComponent<ItemEffectViews>().Initialise(_session.Items);
            HostLobby host = null;
            if (session.IsHost)
            {
                host = gameObject.AddComponent<HostLobby>();
                host.Initialise(session);
            }

            // A dedicated host (a server, design doc §2.3) has nobody looking at it.
            if (!session.IsDedicated)
            {
                gameObject.AddComponent<SessionOverlay>().Initialise(session, this, host);
            }

            if (session.IsHost)
            {
                _botCars = gameObject.AddComponent<BotCars>();
                _botCars.Initialise(screen, session, this);
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.CarStateReceived -= OnCarState;
                _session.HitReceived -= OnHit;
                if (_session.GuestSide != null)
                {
                    _session.GuestSide.ControlReturned -= OnControlReturned;
                }
            }
        }

        private void OnCarState(CarStateMessage state)
        {
            // The host referees Battle lives: a spin-out (an item hit) costs one, counted on the rising edge.
            if (_session.Race is HostRace race && (state.Flags & CarStateFlags.BotControlled) == 0)
            {
                bool spun = (state.Flags & CarStateFlags.SpunOut) != 0;
                if (spun && _spunOut.Add(state.PlayerId))
                {
                    race.ReportSpinOut(state.PlayerId);
                }
                else if (!spun)
                {
                    _spunOut.Remove(state.PlayerId);
                }
            }

            if (_remotes.TryGetValue(state.PlayerId, out var car))
            {
                car.OnState(state, NetworkSession.NowMs, _session.LatencyMsFor(state.PlayerId));
            }
        }

        private void Update()
        {
            // A dedicated host has no car of its own (its id is NoPlayer) but still shows every car, for item hits and standings.
            if (_session.Ended || (!_session.TryGetLocalPlayerId(out _me) && !_session.IsDedicated))
            {
                return;
            }

            SyncRoster();
            FollowRacePhase();

            long now = NetworkSession.NowMs;
            foreach (var pair in _remotes)
            {
                pair.Value.Tick(now, _session.LatencyMsFor(pair.Key), Time.deltaTime);
            }

            UseItems();
            FollowOwnArenaState();
            SendOwnState(now);
        }

        /// <summary>On the host, reports this phone's own spin-outs; in an arena, takes the controls away once this phone's car is out.</summary>
        private void FollowOwnArenaState()
        {
            var car = _screen.Car;
            if (car == null)
            {
                return;
            }

            bool spun = car.State.SpunOut;
            if (spun && !_ownSpunOut && _session.Race is HostRace race)
            {
                race.ReportSpinOut(_me);
            }

            _ownSpunOut = spun;
            if (_phase == RacePhase.Racing && _session.Race.Mode != GameMode.Race)
            {
                car.InputLocked = IsOut(_session.Race, _me);
            }
        }

        /// <summary>Whether <paramref name="playerId"/> is out of the arena event, from the latest standings.</summary>
        public static bool IsOut(IRaceView race, byte playerId)
        {
            var standings = race.Standings;
            if (race.Mode == GameMode.Race || standings == null)
            {
                return false;
            }

            foreach (var entry in standings.Order)
            {
                if (entry.PlayerId == playerId)
                {
                    return entry.FinishPosition > 0;
                }
            }

            return false;
        }

        /// <summary>Uses the held item when the player presses Item, and keeps the Item button and shield ring up to date.</summary>
        private void UseItems()
        {
            var car = _screen.Car;
            var input = _screen.PlayerInput;
            bool pressed = input != null && input.ConsumeItemPress();
            _shield.Tick(Time.deltaTime);
            // A wreck can't use its item (design doc §4): a Boost would drive it, and the item keeps for later.
            if (pressed && car != null && _phase == RacePhase.Racing && !car.Damage.Wrecked)
            {
                var item = _session.Items.UseHeld(car.BodyPosition.ToCore(), car.BodyVelocity.ToCore(), car.State.Heading);
                if (item == ItemKind.Boost)
                {
                    car.Boost(car.Tuning.ItemBoostSpeed, car.Tuning.ItemBoostDuration);
                }
                else if (item == ItemKind.Shield)
                {
                    _shield.Raise();
                }
            }

            _screen.ShowItemButton = _phase == RacePhase.Racing;
            _screen.ItemButtonLabel = ItemNames.Label(_session.Items.Held);
            if (car != null && _shieldRing == null)
            {
                _shieldRing = ShieldRing.Build(car.transform);
            }

            ShieldRing.Show(_shieldRing, _shield.IsUp);
        }

        /// <summary>Back from the background after a bot drove this phone's car (design doc §12): carry on from where it got to.</summary>
        private void OnControlReturned(ControlReturnedMessage returned)
        {
            var car = _screen.Car;
            if (car == null)
            {
                return;
            }

            car.Place(returned.Position.ToUnity(), returned.HeadingRadians, returned.Velocity.ToUnity());
            _detector.Resume(returned.CheckpointsPassed);
            _lastPosition = car.BodyPosition;
        }

        private void OnHit(HitMessage hit)
        {
            var car = _screen.Car;
            if (car == null)
            {
                return;
            }

            switch (ItemHitResponse.Resolve(hit, _shield))
            {
                case ItemHitOutcome.Blocked:
                    break;
                case ItemHitOutcome.SpinOut:
                    car.AddImpulse(hit.Impulse.ToUnity());
                    car.SpinOut();
                    car.TakeItemHit();
                    break;
                default:
                    if (_bumpMonitor.OnHit(hit, _me, NetworkSession.NowMs, out var impulse))
                    {
                        car.AddImpulse(impulse.ToUnity());
                        car.TakeBump(impulse.ToUnity());
                    }

                    break;
            }
        }

        /// <summary>
        /// Host only, when a bot takes over a player who left mid-race (design doc §12): hands over
        /// where that player's car was last seen, and removes the remote car so the bot's car replaces it.
        /// </summary>
        public bool TryTakeOverRemote(byte playerId, out Vector3 position, out float headingRadians, out Vector3 velocity)
        {
            if (!_remotes.TryGetValue(playerId, out var remote) || !remote.HasState)
            {
                position = default;
                headingRadians = 0f;
                velocity = default;
                return false;
            }

            position = remote.transform.position;
            headingRadians = remote.HeadingRadians;
            velocity = remote.Velocity;
            Destroy(remote.gameObject);
            _remotes.Remove(playerId);
            return true;
        }

        /// <summary>Where this phone sees a player's car: for the host's item hits, and the TV viewer's map (design doc §2.4).</summary>
        public bool TryGetCarPosition(byte playerId, out CarRacer.Core.Numerics.Vec3 position)
        {
            if (playerId == _me && _screen.Car != null)
            {
                position = _screen.Car.BodyPosition.ToCore();
                return true;
            }

            if (_remotes.TryGetValue(playerId, out var remote) && remote.HasState)
            {
                position = remote.transform.position.ToCore();
                return true;
            }

            if (_botCars != null && _botCars.TryGetPosition(playerId, out var bot))
            {
                position = bot.ToCore();
                return true;
            }

            position = default;
            return false;
        }

        private void FixedUpdate()
        {
            var car = _screen.Car;
            if (car == null || _session.Ended)
            {
                return;
            }

            ResolveBumps(car);
            if (_phase != RacePhase.Racing)
            {
                return;
            }

            Vector3 position = car.BodyPosition;
            int crossed = _detector.Update(_lastPosition.ToCore(), position.ToCore());
            _lastPosition = position;
            if (crossed >= 0)
            {
                _session.Race.ReportOwnCheckpoint(crossed);
            }

            if (_session.Items.ClaimTouchedBox(position.ToCore()))
            {
                GameAudio.Play("item_box");
            }
        }

        /// <summary>Pushes this phone's car off any car it touches and tells that car's phone, in case it missed the contact.</summary>
        private void ResolveBumps(CarController car)
        {
            _others.Clear();
            foreach (var pair in _remotes)
            {
                if (pair.Value.HasState)
                {
                    _others.Add(new OtherCar(pair.Key, pair.Value.transform.position.ToCore(), pair.Value.Velocity.ToCore()));
                }
            }

            _bumps.Clear();
            _bumpMonitor.Step(_me, car.BodyPosition.ToCore(), car.BodyVelocity.ToCore(), _others, NetworkSession.NowMs, _bumps);
            foreach (var bump in _bumps)
            {
                car.AddImpulse(bump.SelfImpulse.ToUnity());
                car.TakeBump(bump.SelfImpulse.ToUnity());
                _session.SendHit(bump.Fallback);
                foreach (var other in _others)
                {
                    if (other.PlayerId == bump.OtherPlayerId)
                    {
                        ReportCrush(car.BodyVelocity.ToCore(), other.PlayerId, other.Position, other.Velocity);
                    }
                }
            }
        }

        private bool ScoringStunts => _session.Race != null && _session.Race.Mode == GameMode.Monster && _session.Race.Phase == RacePhase.Racing;

        /// <summary>Monster Jam: this phone's truck landed a jump; the host scores it, and a long one shows here straight away.</summary>
        private void OnLanded(float seconds)
        {
            if (!ScoringStunts)
            {
                return;
            }

            long airMs = (long)(seconds * 1000f);
            _session.Race.ReportOwnStunt(StuntKind.Air, airMs, 0);
            int points = MonsterRules.AirPoints(airMs);
            if (points > 0)
            {
                StuntScored?.Invoke($"Big air! +{points}");
            }
        }

        /// <summary>A physics contact has already been resolved by now, so judge it on the speeds both cars hit at.</summary>
        private void OnTouchedCar(CarController other)
        {
            if (_screen.Car != null)
            {
                ReportCrush(_screen.Car.PreImpactVelocity.ToCore(), other.PlayerId, other.BodyPosition.ToCore(), other.PreImpactVelocity.ToCore());
            }
        }

        /// <summary>Monster Jam: this phone's truck, moving at <paramref name="velocity"/>, touched another; a crush if it came down on it or did the hitting.</summary>
        private void ReportCrush(CarRacer.Core.Numerics.Vec3 velocity, byte victimId, CarRacer.Core.Numerics.Vec3 victimPosition, CarRacer.Core.Numerics.Vec3 victimVelocity)
        {
            var car = _screen.Car;
            if (!ScoringStunts || car == null || !MonsterRules.IsCrush(car.BodyPosition.ToCore(), velocity, victimPosition, victimVelocity))
            {
                return;
            }

            _session.Race.ReportOwnStunt(StuntKind.Crush, 0, victimId);

            // The host scores nothing for a repeat crush on the same truck within the cooldown, so don't show one.
            long now = NetworkSession.NowMs;
            if (_lastCrushPopupMs.TryGetValue(victimId, out long last) && now - last < MonsterRules.CrushCooldownMs)
            {
                return;
            }

            _lastCrushPopupMs[victimId] = now;
            StuntScored?.Invoke($"Crush! +{MonsterRules.CrushPoints}");
        }

        private void FollowRacePhase()
        {
            var phase = _session.Race.Phase;
            if (phase == _phase)
            {
                return;
            }

            // Line up for the countdown (or straight away if GO came before the countdown was seen),
            // and come back to the track from an arena or the stadium for the lobby. With no car of its own yet (or
            // ever, on a dedicated host), the screen still moves to the event's track, which standings use.
            bool lineUp = phase == RacePhase.Countdown || (phase == RacePhase.Racing && _phase != RacePhase.Countdown);
            if (lineUp)
            {
                _screen.FollowEventTrack(_session.Race);
            }

            if (_screen.Car == null)
            {
                return;
            }

            if (lineUp)
            {
                if (_detectorTrack != _screen.TrackIndex)
                {
                    _detector = new CheckpointDetector(_screen.Track.CheckpointGates());
                    _detectorTrack = _screen.TrackIndex;
                }

                _screen.PlaceLocalCarAtStart(_session.Race.GridSlotOf(_me), _session.Race.Mode);
                _offTrack = _session.Race.Mode != GameMode.Race;
                _detector.Reset();
                _shield.Clear();
                _lastPosition = _screen.Car.BodyPosition;
            }
            else if (phase == RacePhase.Lobby && _offTrack)
            {
                _screen.PlaceLocalCarOnGrid(_me);
                _offTrack = false;
                _lastPosition = _screen.Car.BodyPosition;
            }

            _screen.Car.InputLocked = phase == RacePhase.Countdown;

            // Monster Jam: truck handling and heavier bumps for the event (the look follows the roster sync).
            bool trucks = phase != RacePhase.Lobby && _session.Race.Mode == GameMode.Monster;
            _screen.Car.MonsterTruck = trucks;
            _bumpSettings.Exaggeration = trucks ? MonsterRules.BumpExaggeration : _carExaggeration;

            // Damage counts in a race unless the host turned it off (design doc §4, §11); the arenas have
            // their own rules, and the lobby is for messing about. Car stats follow the host's rules too.
            var rules = _session.Race.Rules;
            _screen.Car.DamageEnabled = phase == RacePhase.Racing && _session.Race.Mode == GameMode.Race && rules.Damage;
            _screen.Car.UseBodyStats(phase == RacePhase.Lobby || rules.CarStats);
            _phase = phase;
        }

        private void SendOwnState(long now)
        {
            var car = _screen.Car;
            if (car == null)
            {
                return;
            }

            _sendTimer += Time.deltaTime;
            if (_sendTimer < SendIntervalSeconds)
            {
                return;
            }

            _sendTimer = Mathf.Min(_sendTimer - SendIntervalSeconds, SendIntervalSeconds);
            var state = car.State;
            var flags = CarStateFlags.None;
            if (state.Drifting)
            {
                flags |= CarStateFlags.Drifting;
            }

            if (state.BoostSecondsLeft > 0f)
            {
                flags |= CarStateFlags.Boosting;
            }

            if (_shield.IsUp)
            {
                flags |= CarStateFlags.Shielded;
            }

            if (state.SpunOut)
            {
                flags |= CarStateFlags.SpunOut;
            }

            flags |= DamageFlags.For(car.Damage.SmokeLevel, car.Damage.Wrecked);
            _session.SendCarState(new CarStateMessage
            {
                PlayerId = _me,
                Sequence = ++_sequence,
                SentAtMs = now,
                Position = car.BodyPosition.ToCore(),
                Velocity = car.BodyVelocity.ToCore(),
                HeadingRadians = state.Heading,
                Flags = flags,
            });
        }

        /// <summary>Host only, for standings: how far a player's car is towards its next checkpoint.</summary>
        private float FractionToNextCheckpoint(int playerId)
        {
            Vector3 position;
            if (playerId == _me && _screen.Car != null)
            {
                position = _screen.Car.BodyPosition;
            }
            else if (_remotes.TryGetValue((byte)playerId, out var remote) && remote.HasState)
            {
                position = remote.transform.position;
            }
            else if (_botCars == null || !_botCars.TryGetPosition((byte)playerId, out position))
            {
                return 0f;
            }

            var tracker = (_session.Race as HostRace)?.Tracker;
            if (tracker == null)
            {
                return 0f;
            }

            float distance = _screen.Track.Spline.FindNearestDistance(position.ToCore());
            return _screen.Track.FractionToNextCheckpoint(distance, tracker.Get(playerId).NextCheckpoint);
        }

        /// <summary>Spawns this phone's car once its roster entry (and colour) is known, and adds or removes remote cars to match the roster.</summary>
        private void SyncRoster()
        {
            _inRoster.Clear();
            var race = _session.Race;
            bool truck = race != null && race.Mode == GameMode.Monster && race.Phase != RacePhase.Lobby;
            foreach (var player in _session.Players)
            {
                _inRoster.Add(player.PlayerId);
                var look = (player.BodyIndex, player.ColourIndex, truck);
                var colour = CarColours.ForIndex(player.ColourIndex);
                var body = CarModels.ForIndex(player.BodyIndex);
                bool restyled = _looks.TryGetValue(player.PlayerId, out var had) && had != look;
                if (player.PlayerId == _me)
                {
                    if (!_spawned)
                    {
                        _screen.SpawnLocalCar(player.PlayerId, colour, body);
                        _screen.Car.PlayerId = player.PlayerId;
                        _screen.Car.Landed += OnLanded;
                        _screen.Car.TouchedCar += OnTouchedCar;
                        if (truck)
                        {
                            _screen.ReplaceLocalCarBody(colour, body, true);
                        }

                        _spawned = true;
                    }
                    else if (restyled)
                    {
                        _screen.ReplaceLocalCarBody(colour, body, truck);
                    }
                }
                else if (_session.IsHost && player.IsBot)
                {
                    // The host drives its bots itself (BotCars); they aren't remote cars here.
                }
                else if (!_remotes.TryGetValue(player.PlayerId, out var remote))
                {
                    var built = CarFactory.BuildRemote($"Remote car {player.PlayerId}", player.PlayerId, colour, _screen.transform, body);
                    if (truck)
                    {
                        CarFactory.ReplaceBody(built.gameObject, colour, body, true);
                    }

                    _remotes[player.PlayerId] = built;
                }
                else if (restyled)
                {
                    CarFactory.ReplaceBody(remote.gameObject, colour, body, truck);
                    remote.Colour = colour;
                }

                _looks[player.PlayerId] = look;
            }

            _gone.Clear();
            foreach (byte id in _remotes.Keys)
            {
                if (!_inRoster.Contains(id))
                {
                    _gone.Add(id);
                }
            }

            foreach (byte id in _gone)
            {
                Destroy(_remotes[id].gameObject);
                _remotes.Remove(id);
                _looks.Remove(id);
            }
        }
    }
}
