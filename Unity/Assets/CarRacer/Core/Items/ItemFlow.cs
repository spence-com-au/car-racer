using System;
using System.Collections.Generic;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;

namespace CarRacer.Core.Items
{
    /// <summary>What this phone's game needs from the item rules, the same on the host and on guests (design doc §8).</summary>
    public interface IItemView
    {
        /// <summary>Which boxes are up, in this phone's clock.</summary>
        ItemBoxField Boxes { get; }

        /// <summary>Whether item boxes are on the track: not during an event the host has turned items off for.</summary>
        bool BoxesShown { get; }

        /// <summary>The item this phone's player holds.</summary>
        ItemKind Held { get; }

        /// <summary>Oil slicks and Fake boxes on the track, in this phone's clock.</summary>
        HazardField Hazards { get; }

        /// <summary>Missiles, Seekers and Bombs in flight, where this phone last knew them to be.</summary>
        IReadOnlyList<ProjectileSnapshot> Projectiles { get; }

        /// <summary>A Missile, Seeker or Bomb ended here: it hit a car, blew up or ran out.</summary>
        event Action<ItemKind, Vec3> ProjectileEnded;

        /// <summary>
        /// Someone used an item. This phone's own use is raised at once, so its effects feel instant;
        /// other players' arrive through the host.
        /// </summary>
        event Action<ItemUsedMessage> ItemUsed;

        /// <summary>Call every frame, after the race's own Tick. A new race puts every box back and empties every slot.</summary>
        void Tick();

        /// <summary>Call every physics step with this phone's car position: claims a box the car is touching, if the slot is empty.</summary>
        bool ClaimTouchedBox(Vec3 carPosition);

        /// <summary>
        /// Uses the held item, returning which it was (<see cref="ItemKind.None"/> if nothing was held,
        /// the race isn't on or the player is out of an arena event). The caller applies Boost and
        /// Shield to its own car at once.
        /// </summary>
        ItemKind UseHeld(Vec3 position, Vec3 velocity, float headingRadians);
    }

    /// <summary>
    /// The host's item referee (design doc §8): grants box claims first come first served, rolls
    /// each item with catch-up odds from the latest standings, keeps every player's slot, relays
    /// item uses to everyone once it has checked the player held that item, and decides every item
    /// hit: a Shockwave's or Lightning's victims at once, an oil slick's or Fake box's when a car
    /// drives onto it, a projectile's when it reaches a car.
    /// </summary>
    public sealed class HostItems : IItemView
    {
        private readonly HostSession _session;
        private readonly IRaceView _race;
        private readonly Func<long> _clockMs;
        private readonly Func<double> _random01;
        private readonly CarPositionLookup _carPositions;
        /// <summary>How often the host tells everyone where the projectiles are.</summary>
        public const long ProjectileBroadcastMs = 50;

        private readonly List<Projectile> _flying = new List<Projectile>();
        private readonly List<ProjectileSnapshot> _snapshots = new List<ProjectileSnapshot>();
        private ushort _lastEffectId;
        private long _lastTickMs = long.MinValue;
        private long _nextProjectileBroadcastMs;

        /// <param name="random01">Uniform random numbers in [0, 1) for item rolls.</param>
        /// <param name="carPositions">Where the host sees each player's car, for deciding hits.</param>
        public HostItems(HostSession session, HostRace race, IReadOnlyList<Vec3> boxPositions, Func<long> clockMs, Func<double> random01, CarPositionLookup carPositions)
        {
            _session = session;
            _race = race;
            _clockMs = clockMs;
            _random01 = random01;
            _carPositions = carPositions;
            Boxes = new ItemBoxField(boxPositions);
            _session.ItemMessageReceived += OnMessage;
            _session.PlayerLeft += (playerId, name) => Slots.Remove(playerId);
            race.CountdownStarted += OnCountdownStarted;
        }

        public ItemBoxField Boxes { get; }
        public bool BoxesShown => _race.Phase == RacePhase.Lobby || _race.Rules.Items;
        public ItemSlots Slots { get; } = new ItemSlots();
        public ItemKind Held => Slots.Held(_session.HostPlayerId);
        public HazardField Hazards { get; } = new HazardField();
        public IReadOnlyList<ProjectileSnapshot> Projectiles => _snapshots;

        public event Action<ItemUsedMessage> ItemUsed;
        public event Action<ItemKind, Vec3> ProjectileEnded;

        public void Tick()
        {
            var phase = _race.Phase;
            long now = _clockMs();
            float dt = _lastTickMs == long.MinValue ? 0f : Math.Min(0.1f, (now - _lastTickMs) / 1000f);
            _lastTickMs = now;
            Hazards.Expire(now);
            if (phase == RacePhase.Racing)
            {
                CatchCarsOnHazards(now);
                FlyProjectiles(now, dt);
            }
            else
            {
                // Nothing flies them outside a race, so they would hang in mid-air.
                _flying.Clear();
                _snapshots.Clear();
            }
        }

        public bool ClaimTouchedBox(Vec3 carPosition) => ClaimFor(_session.HostPlayerId, carPosition);

        public ItemKind UseHeld(Vec3 position, Vec3 velocity, float headingRadians) => UseFor(_session.HostPlayerId, position, velocity, headingRadians);

        /// <summary>A bot's car (which the host drives) claims a box it is touching.</summary>
        public bool ClaimTouchedBoxForBot(byte botId, Vec3 carPosition) => _session.IsBot(botId) && ClaimFor(botId, carPosition);

        /// <summary>A bot uses the item it holds; see <see cref="UseHeld"/>.</summary>
        public ItemKind UseHeldForBot(byte botId, Vec3 position, Vec3 velocity, float headingRadians) =>
            _session.IsBot(botId) ? UseFor(botId, position, velocity, headingRadians) : ItemKind.None;

        private bool ClaimFor(byte playerId, Vec3 carPosition)
        {
            int box = Boxes.FindTouched(carPosition, _clockMs());
            return box >= 0 && Grant(playerId, box);
        }

        private ItemKind UseFor(byte playerId, Vec3 position, Vec3 velocity, float headingRadians)
        {
            var used = new ItemUsedMessage
            {
                PlayerId = playerId,
                Item = Slots.Held(playerId),
                Position = position,
                Velocity = velocity,
                HeadingRadians = headingRadians,
            };

            return Accept(used) ? used.Item : ItemKind.None;
        }

        private void OnMessage(IGameMessage message)
        {
            switch (message)
            {
                case ItemBoxClaimMessage claim:
                    Grant(claim.PlayerId, claim.BoxIndex);
                    break;
                case ItemUsedMessage used:
                    Accept(used);
                    break;
            }
        }

        /// <summary>
        /// Empties every slot and restores every box as an event counts down. This resets on the countdown itself
        /// rather than on a phase change seen in <see cref="Tick"/>, because a host that stalls (in the background, or
        /// on a long hitch) through the whole countdown goes straight to Racing.
        /// </summary>
        private void OnCountdownStarted()
        {
            Boxes.Reset();
            Slots.Clear();
            Hazards.Clear();
        }

        private bool Grant(byte playerId, int box)
        {
            long now = _clockMs();
            if (_race.Phase != RacePhase.Racing || !_race.Rules.Items || ArenaStandings.IsOut(_race, playerId) || !Slots.CanPickUp(playerId) || !Boxes.TryTake(box, now, out long respawnAt))
            {
                return false;
            }

            RacePosition(playerId, out int position, out int racers);
            var item = ItemRoll.Roll(position, racers, _random01());
            Slots.Give(playerId, item);
            _session.Broadcast(new ItemBoxTakenMessage { BoxIndex = (ushort)box, PlayerId = playerId, Item = item, RespawnAtHostMs = respawnAt }, reliable: true);
            return true;
        }

        private bool Accept(ItemUsedMessage used)
        {
            if (_race.Phase != RacePhase.Racing || ArenaStandings.IsOut(_race, used.PlayerId) || !Slots.TryUse(used.PlayerId, used.Item))
            {
                return false;
            }

            used.UsedAtHostMs = _clockMs();
            used.EffectId = ++_lastEffectId;
            _session.Broadcast(used, reliable: true);
            ItemUsed?.Invoke(used);

            switch (used.Item)
            {
                case ItemKind.Shockwave:
                    HitEveryoneNear(used);
                    break;
                case ItemKind.OilSlick:
                case ItemKind.FakeBox:
                    Hazards.AddDropped(used, used.UsedAtHostMs);
                    break;
                case ItemKind.Lightning:
                    StrikeEveryoneAhead(used.PlayerId);
                    break;
                case ItemKind.Missile:
                    _flying.Add(ProjectileRules.LaunchMissile(used, CarAhead(used.PlayerId), used.UsedAtHostMs));
                    TakeSnapshots();
                    break;
                case ItemKind.Seeker:
                    _flying.Add(ProjectileRules.LaunchSeeker(used, Leader(used.PlayerId), used.UsedAtHostMs));
                    TakeSnapshots();
                    break;
                case ItemKind.Bomb:
                    _flying.Add(ProjectileRules.LaunchBomb(used, used.UsedAtHostMs));
                    TakeSnapshots();
                    break;
            }

            return true;
        }

        /// <summary>The car one place ahead of <paramref name="playerId"/> in the latest standings: what a Missile chases.</summary>
        private byte CarAhead(byte playerId)
        {
            var standings = _race.Standings;
            if (standings != null)
            {
                for (int i = 1; i < standings.Order.Count; i++)
                {
                    if (standings.Order[i].PlayerId == playerId)
                    {
                        return standings.Order[i - 1].PlayerId;
                    }
                }
            }

            return ProjectileRules.NoTarget;
        }

        /// <summary>
        /// The leader among the cars still racing (not finished, or not out in an arena): what a
        /// Seeker hunts. The leader's own Seeker goes for the next car instead; with nobody else
        /// racing it flies straight.
        /// </summary>
        private byte Leader(byte playerId)
        {
            var standings = _race.Standings;
            if (standings != null)
            {
                foreach (var entry in standings.Order)
                {
                    if (entry.FinishPosition == 0 && entry.PlayerId != playerId)
                    {
                        return entry.PlayerId;
                    }
                }
            }

            return ProjectileRules.NoTarget;
        }

        /// <summary>
        /// Lightning: every car ahead of <paramref name="playerId"/> in the latest standings that is
        /// still racing is struck, wherever it is. Cars that have finished (or are out) are left be.
        /// </summary>
        private void StrikeEveryoneAhead(byte playerId)
        {
            var standings = _race.Standings;
            if (standings == null)
            {
                return;
            }

            foreach (byte target in standings.RacingAhead(playerId))
            {
                Hit(playerId, target, ItemEffects.LightningImpulse);
            }
        }

        /// <summary>Moves every Missile, Seeker and Bomb, decides what each hits, and tells everyone where the rest are.</summary>
        private void FlyProjectiles(long now, float dt)
        {
            if (_flying.Count == 0)
            {
                return;
            }

            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var p = _flying[i];
                var targetPosition = default(Vec3);
                bool hasTarget = p.TargetId != ProjectileRules.NoTarget && _carPositions(p.TargetId, out targetPosition);

                p = ProjectileRules.Step(p, dt, hasTarget, targetPosition);
                _flying[i] = p;
                bool done = p.Kind == ItemKind.Missile ? MissileDone(p, now)
                    : p.Kind == ItemKind.Seeker ? SeekerDone(p, now)
                    : BombDone(p, now);
                if (done)
                {
                    _flying.RemoveAt(i);
                    _session.Broadcast(new HazardGoneMessage { EffectId = p.EffectId, Item = p.Kind, Position = p.Position }, reliable: true);
                    ProjectileEnded?.Invoke(p.Kind, p.Position);
                }
            }

            TakeSnapshots();
            if (_snapshots.Count > 0 && now >= _nextProjectileBroadcastMs)
            {
                _nextProjectileBroadcastMs = now + ProjectileBroadcastMs;
                var message = new ProjectilesMessage();
                message.Projectiles.AddRange(_snapshots);
                _session.Broadcast(message, reliable: false);
            }
        }

        private void TakeSnapshots()
        {
            _snapshots.Clear();
            foreach (var p in _flying)
            {
                _snapshots.Add(new ProjectileSnapshot { EffectId = p.EffectId, Kind = p.Kind, Position = p.Position, TargetId = p.TargetId });
            }
        }

        /// <summary>A Missile hits the first car it reaches (any but its owner's) or runs out of time.</summary>
        private bool MissileDone(Projectile missile, long now)
        {
            foreach (var player in _session.Roster.Players)
            {
                if (player.PlayerId != missile.OwnerId && _carPositions(player.PlayerId, out var position)
                    && (position - missile.Position).Flattened.LengthSquared <= ProjectileRules.HitRadius * ProjectileRules.HitRadius)
                {
                    Hit(missile.OwnerId, player.PlayerId, ProjectileRules.MissileImpulse(missile));
                    return true;
                }
            }

            return now >= missile.ExpiresAtMs;
        }

        /// <summary>
        /// A Seeker flies past everyone but its target; on reaching it, it blows up, catching every car
        /// near the target but its owner's. It runs out after its lifetime.
        /// </summary>
        private bool SeekerDone(Projectile seeker, long now)
        {
            if (seeker.TargetId != ProjectileRules.NoTarget && _carPositions(seeker.TargetId, out var target)
                && (target - seeker.Position).Flattened.LengthSquared <= ProjectileRules.HitRadius * ProjectileRules.HitRadius)
            {
                foreach (var player in _session.Roster.Players)
                {
                    if (player.PlayerId != seeker.OwnerId && _carPositions(player.PlayerId, out var position)
                        && (position - target).Flattened.LengthSquared <= ProjectileRules.SeekerBlastRadius * ProjectileRules.SeekerBlastRadius)
                    {
                        Hit(seeker.OwnerId, player.PlayerId, ProjectileRules.SeekerImpulse(seeker.Position, position));
                    }
                }

                return true;
            }

            return now >= seeker.ExpiresAtMs;
        }

        /// <summary>A Bomb goes off when it meets a car (its thrower only after a moment) or its fuse runs out, hitting everyone near.</summary>
        private bool BombDone(Projectile bomb, long now)
        {
            bool explode = now >= bomb.ExpiresAtMs;
            foreach (var player in _session.Roster.Players)
            {
                if (explode)
                {
                    break;
                }

                bool safe = player.PlayerId == bomb.OwnerId && now < bomb.LaunchedAtMs + ProjectileRules.BombSafeMs;
                explode = !safe && _carPositions(player.PlayerId, out var position)
                    && (position - bomb.Position).Flattened.LengthSquared <= ProjectileRules.HitRadius * ProjectileRules.HitRadius;
            }

            if (!explode)
            {
                return false;
            }

            foreach (var player in _session.Roster.Players)
            {
                if (_carPositions(player.PlayerId, out var position)
                    && (position - bomb.Position).Flattened.LengthSquared <= ProjectileRules.BombBlastRadius * ProjectileRules.BombBlastRadius)
                {
                    Hit(bomb.OwnerId, player.PlayerId, ProjectileRules.BlastImpulse(bomb.Position, position));
                }
            }

            return true;
        }

        private void HitEveryoneNear(ItemUsedMessage used)
        {
            foreach (var player in _session.Roster.Players)
            {
                if (player.PlayerId != used.PlayerId && _carPositions(player.PlayerId, out var position)
                    && (position - used.Position).Flattened.LengthSquared <= ItemEffects.ShockwaveRadius * ItemEffects.ShockwaveRadius)
                {
                    Hit(used.PlayerId, player.PlayerId, ItemEffects.ShockwaveImpulse(used.Position, position));
                }
            }
        }

        /// <summary>Each armed oil slick or Fake box catches the first car on it (its dropper included, once armed) and is used up.</summary>
        private void CatchCarsOnHazards(long now)
        {
            if (Hazards.Active.Count == 0)
            {
                return;
            }

            foreach (var player in _session.Roster.Players)
            {
                if (_carPositions(player.PlayerId, out var position) && Hazards.TryFindTouched(position, now, out var hazard))
                {
                    Hazards.Remove(hazard.EffectId);
                    _session.Broadcast(new HazardGoneMessage { EffectId = hazard.EffectId, Item = hazard.Item, Position = hazard.Position }, reliable: true);
                    Hit(hazard.OwnerId, player.PlayerId, hazard.Item == ItemKind.FakeBox ? ItemEffects.FakeBoxImpulse : ItemEffects.OilImpulse);
                }
            }
        }

        private void Hit(byte attackerId, byte targetId, Vec3 impulse)
        {
            _session.SendHit(new HitMessage { AttackerId = attackerId, TargetId = targetId, SentAtMs = _clockMs(), Impulse = impulse, IsItemHit = true });
        }

        /// <summary>The player's place in the latest standings; mid-pack before the first standings of a race.</summary>
        private void RacePosition(byte playerId, out int position, out int racers)
        {
            var standings = _race.Standings;
            if (standings != null)
            {
                for (int i = 0; i < standings.Order.Count; i++)
                {
                    if (standings.Order[i].PlayerId == playerId)
                    {
                        position = i + 1;
                        racers = standings.Order.Count;
                        return;
                    }
                }
            }

            racers = Math.Max(1, _session.Roster.Count);
            position = (racers + 1) / 2;
        }
    }

    /// <summary>
    /// A guest's side of the items: claims boxes its car drives through (hiding them at once), learns
    /// its item from the host, tells the host when it uses it, and mirrors the hazards on the track.
    /// </summary>
    public sealed class GuestItems : IItemView
    {
        /// <summary>A claim the host never answered (a lost message) stops blocking new claims after this long.</summary>
        public const long ClaimTimeoutMs = 1000;

        private readonly GuestSession _session;
        private readonly IRaceView _race;
        private readonly Func<long> _clockMs;
        private int _pendingBox = -1;
        private long _pendingSinceMs;
        private readonly List<ProjectileSnapshot> _snapshots = new List<ProjectileSnapshot>();

        /// <summary>Projectiles the host has said are gone, so a late position update can't bring one back.</summary>
        private readonly HashSet<ushort> _gone = new HashSet<ushort>();

        public GuestItems(GuestSession session, IRaceView race, IReadOnlyList<Vec3> boxPositions, Func<long> clockMs)
        {
            _session = session;
            _race = race;
            _clockMs = clockMs;
            Boxes = new ItemBoxField(boxPositions);
            _session.ItemMessageReceived += OnMessage;
            _session.RaceMessageReceived += OnRaceMessage;
        }

        public ItemBoxField Boxes { get; }
        public bool BoxesShown => _race.Phase == RacePhase.Lobby || _race.Rules.Items;
        public ItemKind Held { get; private set; }
        public HazardField Hazards { get; } = new HazardField();
        public IReadOnlyList<ProjectileSnapshot> Projectiles => _snapshots;

        public event Action<ItemUsedMessage> ItemUsed;
        public event Action<ItemKind, Vec3> ProjectileEnded;

        public void Tick()
        {
            // The host stops flying projectiles when the race ends, so drop the last positions it sent.
            if (_race.Phase != RacePhase.Racing)
            {
                _snapshots.Clear();
            }

            Hazards.Expire(_clockMs());
        }

        public bool ClaimTouchedBox(Vec3 carPosition)
        {
            long now = _clockMs();
            if (_pendingBox >= 0 && now - _pendingSinceMs >= ClaimTimeoutMs)
            {
                _pendingBox = -1;
            }

            if (_race.Phase != RacePhase.Racing || !_race.Rules.Items || ArenaStandings.IsOut(_race, _session.PlayerId) || Held != ItemKind.None || _pendingBox >= 0)
            {
                return false;
            }

            int box = Boxes.FindTouched(carPosition, now);
            if (box < 0)
            {
                return false;
            }

            // Hide it at once; the host's answer corrects the respawn time.
            Boxes.MarkTaken(box, now + ItemBoxField.RespawnMs);
            _pendingBox = box;
            _pendingSinceMs = now;
            _session.SendToHost(new ItemBoxClaimMessage { PlayerId = _session.PlayerId, BoxIndex = (ushort)box }, reliable: true);
            return true;
        }

        public ItemKind UseHeld(Vec3 position, Vec3 velocity, float headingRadians)
        {
            var item = Held;
            if (_race.Phase != RacePhase.Racing || ArenaStandings.IsOut(_race, _session.PlayerId) || item == ItemKind.None)
            {
                return ItemKind.None;
            }

            Held = ItemKind.None;
            var used = new ItemUsedMessage
            {
                PlayerId = _session.PlayerId,
                Item = item,
                Position = position,
                Velocity = velocity,
                HeadingRadians = headingRadians,
            };
            _session.SendToHost(used, reliable: true);
            ItemUsed?.Invoke(used);
            return item;
        }

        private void OnMessage(IGameMessage message)
        {
            switch (message)
            {
                case ItemBoxTakenMessage taken:
                    Boxes.MarkTaken(taken.BoxIndex, _session.Clock.ToLocalTime(taken.RespawnAtHostMs));
                    if (taken.PlayerId == _session.PlayerId)
                    {
                        Held = taken.Item;
                    }

                    if (taken.BoxIndex == _pendingBox)
                    {
                        _pendingBox = -1;
                    }

                    break;
                case ItemUsedMessage used:
                    // The host's relay carries the effect id, so hazards come from it even for this phone's own use.
                    if (ItemEffects.IsDropped(used.Item))
                    {
                        Hazards.AddDropped(used, _session.Clock.ToLocalTime(used.UsedAtHostMs));
                    }

                    if (used.PlayerId != _session.PlayerId)
                    {
                        ItemUsed?.Invoke(used);
                    }
                    else
                    {
                        // A bot may have used it while this app was away. The host handles a use before any
                        // later claim, so this relay always arrives before the next item's ItemBoxTaken.
                        Held = ItemKind.None;
                    }

                    break;
                case HazardGoneMessage gone:
                    Hazards.Remove(gone.EffectId);
                    _gone.Add(gone.EffectId);
                    _snapshots.RemoveAll(p => p.EffectId == gone.EffectId);
                    if (!ItemEffects.IsDropped(gone.Item))
                    {
                        ProjectileEnded?.Invoke(gone.Item, gone.Position);
                    }

                    break;
                case ProjectilesMessage flying:
                    _snapshots.Clear();
                    foreach (var p in flying.Projectiles)
                    {
                        if (!_gone.Contains(p.EffectId))
                        {
                            _snapshots.Add(p);
                        }
                    }

                    break;
            }
        }

        /// <summary>
        /// The host empties every slot and restores every box when an event counts down. This resets on the
        /// countdown itself rather than on a phase change seen in <see cref="Tick"/>, because a phone back from the
        /// background can take a whole event's messages in one frame and a late countdown goes straight to Racing.
        /// </summary>
        private void OnRaceMessage(IGameMessage message)
        {
            if (message is RaceCountdownMessage)
            {
                Boxes.Reset();
                Hazards.Clear();
                _snapshots.Clear();
                _gone.Clear();
                Held = ItemKind.None;
                _pendingBox = -1;
            }
        }
    }
}
