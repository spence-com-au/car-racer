using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;

namespace CarRacer.Core.Arena
{
    /// <summary>What the host's event is (design doc §7). The values travel in network messages.</summary>
    public enum GameMode : byte
    {
        Race = 0,

        /// <summary>A floating platform that shrinks; the last car on it wins.</summary>
        Sumo = 1,

        /// <summary>An item fight in a walled bowl; the last car with lives left wins.</summary>
        Battle = 2,

        /// <summary>Monster trucks in a stunt stadium; the most points for air time and crushes wins (design doc §7.0).</summary>
        Monster = 3,
    }

    /// <summary>
    /// The Sumo platform (design doc §7): a circle that holds its size for a grace period, then
    /// shrinks steadily to a small final size and stays there. A car is out once its centre is
    /// past the edge (it is about to fall) or it has fallen below the platform.
    /// </summary>
    public static class SumoRules
    {
        public const float StartRadius = 30f;
        public const float EndRadius = 10f;
        public const float GraceSeconds = 20f;
        public const float ShrinkSeconds = 90f;

        /// <summary>How far below the platform a car must drop to count as fallen, whatever its position.</summary>
        public const float FallDepth = 3f;

        public static float PlatformRadius(float secondsSinceStart)
        {
            float shrinking = (secondsSinceStart - GraceSeconds) / ShrinkSeconds;
            float t = Math.Max(0f, Math.Min(1f, shrinking));
            return StartRadius + (EndRadius - StartRadius) * t;
        }

        /// <summary>Whether a car at <paramref name="carPosition"/> is out, for a platform centred on <paramref name="centre"/>.</summary>
        public static bool IsOut(Vec3 carPosition, Vec3 centre, float radius)
        {
            return carPosition.Y < centre.Y - FallDepth || (carPosition - centre).Flattened.Length > radius;
        }
    }

    /// <summary>The Battle rules (design doc §7): lives, and the time limit after which the most lives wins.</summary>
    public static class BattleRules
    {
        public const int StartingLives = 3;
        public const long TimeLimitMs = 180_000;

        /// <summary>The walled bowl's radius.</summary>
        public const float BowlRadius = 35f;
    }

    /// <summary>
    /// Monster Jam (design doc §7.0): every car is a monster truck in a walled dirt stadium with
    /// ramps, and scores for time in the air and for crushing other trucks. The most points when
    /// the time runs out wins. Trucks jump higher and hit harder than cars.
    /// </summary>
    public static class MonsterRules
    {
        public const long TimeLimitMs = 120_000;

        /// <summary>The stadium's radius, to the inside of its wall.</summary>
        public const float StadiumRadius = 45f;

        /// <summary>A jump scores once it is this long; shorter hops off bumps don't.</summary>
        public const long MinAirMs = 400;

        /// <summary>One point per this much time in the air, for a jump long enough to score.</summary>
        public const long MsPerAirPoint = 100;

        /// <summary>No single jump scores more than this long in the air (a truck that fell off something).</summary>
        public const long MaxAirMs = 6000;

        /// <summary>Points for crushing another truck: hitting it hard, or landing on it.</summary>
        public const int CrushPoints = 25;

        /// <summary>Trucks meeting at this closing speed (m/s) or more is a crush, for whichever was doing more of the closing.</summary>
        public const float CrushClosingSpeed = 9f;

        /// <summary>Coming down on a truck from this far above it (m) is a crush too, at any speed.</summary>
        public const float CrushDropHeight = 0.6f;

        /// <summary>The same truck can't be crushed by the same attacker again for this long.</summary>
        public const long CrushCooldownMs = 1500;

        /// <summary>The host only believes a crush if it sees the two trucks within this distance.</summary>
        public const float CrushReach = 6f;

        /// <summary>Trucks feel this much less gravity in the air (m/s² upward against the usual pull), for big air.</summary>
        public const float AirLift = 4f;

        /// <summary>Bumps between trucks are this much more dramatic than between cars (heavy hitters).</summary>
        public const float BumpExaggeration = 2.4f;

        /// <summary>The points a jump of <paramref name="airMs"/> scores.</summary>
        public static int AirPoints(long airMs) => airMs < MinAirMs ? 0 : (int)(Math.Min(airMs, MaxAirMs) / MsPerAirPoint);

        /// <summary>
        /// Whether a truck at <paramref name="self"/> moving at <paramref name="selfVelocity"/> crushed
        /// the one it just touched at <paramref name="other"/>: it came down on top of it, or hit it
        /// hard while doing more of the closing than the other truck. Both phones see the same touch;
        /// at most one of them counts it as their crush.
        /// </summary>
        public static bool IsCrush(Vec3 self, Vec3 selfVelocity, Vec3 other, Vec3 otherVelocity)
        {
            if (self.Y - other.Y >= CrushDropHeight && selfVelocity.Y < 0f)
            {
                return true;
            }

            var towards = (other - self).Flattened;
            float apart = towards.Length;
            if (apart < 1e-3f)
            {
                return false;
            }

            towards /= apart;
            float mine = Vec3.Dot(selfVelocity.Flattened, towards);
            float theirs = -Vec3.Dot(otherVelocity.Flattened, towards);
            return mine + theirs >= CrushClosingSpeed && mine > theirs;
        }
    }

    /// <summary>A ramp in the Monster Jam stadium: a wedge rising from its foot towards its forward direction.</summary>
    public struct Ramp
    {
        /// <summary>The middle of the ramp's low edge, on the floor.</summary>
        public Vec3 Foot;

        /// <summary>The way up the ramp, flat.</summary>
        public Vec3 Forward;

        public float Length;
        public float Width;
        public float Height;

        /// <summary>The middle of the ramp's high edge.</summary>
        public Vec3 Lip => Foot + Forward * Length + new Vec3(0f, Height, 0f);
    }

    /// <summary>One player's place when an arena event ends.</summary>
    public struct ArenaPlacing
    {
        public int PlayerId;

        /// <summary>1 for the winner.</summary>
        public int Position;

        /// <summary>How long the player lasted, in ms from the start; the event's length for survivors.</summary>
        public long SurvivedMs;

        /// <summary>Lives left (Battle; 0 in Sumo once out, 1 for a Sumo survivor).</summary>
        public int Lives;
    }

    /// <summary>
    /// Who is still in an arena event and who went out when (host side, design doc §7). Players
    /// start with some lives; losing the last puts them out. The event is over when at most one
    /// player is left. Placings rank survivors first (most lives first), then the rest in
    /// reverse order of going out, so the last out is next best.
    /// </summary>
    public sealed class EliminationTracker
    {
        private readonly Dictionary<int, int> _lives = new Dictionary<int, int>();
        private readonly List<(int PlayerId, long AtMs)> _outOrder = new List<(int, long)>();
        private readonly List<int> _order = new List<int>();

        public EliminationTracker(IEnumerable<int> playerIds, int lives = 1)
        {
            foreach (int id in playerIds)
            {
                _lives[id] = lives;
                _order.Add(id);
            }
        }

        public IReadOnlyList<int> PlayerIds => _order;

        public int Lives(int playerId) => _lives.TryGetValue(playerId, out int lives) ? lives : 0;

        public bool IsOut(int playerId) => Lives(playerId) <= 0;

        public int Remaining
        {
            get
            {
                int count = 0;
                foreach (int lives in _lives.Values)
                {
                    if (lives > 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>At most one player left (or nobody, if the last two went out together).</summary>
        public bool IsOver => _lives.Count > 1 ? Remaining <= 1 : Remaining == 0;

        /// <summary>Takes a life from <paramref name="playerId"/>; returns true if that put them out.</summary>
        public bool LoseLife(int playerId, long atMs)
        {
            if (!_lives.TryGetValue(playerId, out int lives) || lives <= 0)
            {
                return false;
            }

            _lives[playerId] = lives - 1;
            if (lives - 1 > 0)
            {
                return false;
            }

            _outOrder.Add((playerId, atMs));
            return true;
        }

        /// <summary>Puts a player straight out, whatever lives they have (fell off the arena).</summary>
        public bool Eliminate(int playerId, long atMs)
        {
            if (!_lives.TryGetValue(playerId, out int lives) || lives <= 0)
            {
                return false;
            }

            _lives[playerId] = 1;
            return LoseLife(playerId, atMs);
        }

        /// <summary>Final placings at <paramref name="endMs"/>: survivors by lives, then the eliminated, last out first.</summary>
        public List<ArenaPlacing> Placings(long endMs)
        {
            var survivors = new List<int>();
            foreach (int id in _order)
            {
                if (!IsOut(id))
                {
                    survivors.Add(id);
                }
            }

            survivors.Sort((a, b) =>
            {
                int byLives = Lives(b).CompareTo(Lives(a));
                return byLives != 0 ? byLives : _order.IndexOf(a).CompareTo(_order.IndexOf(b));
            });
            var placings = new List<ArenaPlacing>();
            foreach (int id in survivors)
            {
                placings.Add(new ArenaPlacing { PlayerId = id, Position = placings.Count + 1, SurvivedMs = endMs, Lives = Lives(id) });
            }

            for (int i = _outOrder.Count - 1; i >= 0; i--)
            {
                placings.Add(new ArenaPlacing { PlayerId = _outOrder[i].PlayerId, Position = placings.Count + 1, SurvivedMs = _outOrder[i].AtMs, Lives = 0 });
            }

            return placings;
        }
    }

    /// <summary>A starting spot in an arena.</summary>
    public struct ArenaSlot
    {
        public Vec3 Position;
        public Vec3 Forward;
    }

    /// <summary>Where cars start in an arena and, in Battle, where the item boxes sit (design doc §13: generated, not hand-placed).</summary>
    public static class ArenaLayout
    {
        /// <summary>
        /// The Sumo platform's top centre: floating off the end of the track scene, clear of its
        /// ground, so a car that falls off has nothing to land on until the catch floor below.
        /// </summary>
        public static readonly Vec3 SumoCentre = new Vec3(45f, 40f, -260f);

        /// <summary>The Battle bowl's floor centre, off the other end of the track scene.</summary>
        public static readonly Vec3 BattleCentre = new Vec3(45f, 0f, 300f);

        /// <summary>The Monster Jam stadium's floor centre, south-east of the track scene.</summary>
        public static readonly Vec3 MonsterCentre = new Vec3(600f, 0f, -380f);

        /// <summary>The arena's centre in <paramref name="mode"/> (Sumo, Battle or Monster).</summary>
        public static Vec3 CentreFor(GameMode mode) => mode == GameMode.Battle ? BattleCentre : mode == GameMode.Monster ? MonsterCentre : SumoCentre;

        /// <summary>The arena's radius at the start in <paramref name="mode"/> (Sumo, Battle or Monster).</summary>
        public static float StartRadiusFor(GameMode mode) =>
            mode == GameMode.Battle ? BattleRules.BowlRadius : mode == GameMode.Monster ? MonsterRules.StadiumRadius : SumoRules.StartRadius;

        /// <summary>
        /// The Monster Jam stadium's ramps: four kickers round a ring halfway out, each facing along
        /// the ring so a truck lapping the stadium hits them in turn, and a spine in the middle (two
        /// ramps back to back) to launch over from either side.
        /// </summary>
        public static List<Ramp> MonsterRamps(Vec3 centre)
        {
            var ramps = new List<Ramp>();
            float ring = MonsterRules.StadiumRadius * 0.55f;
            for (int i = 0; i < 4; i++)
            {
                float angle = 2f * MathF.PI * (i + 0.5f) / 4f;
                var outward = new Vec3(MathF.Sin(angle), 0f, MathF.Cos(angle));
                var along = new Vec3(outward.Z, 0f, -outward.X);
                const float length = 8f;
                ramps.Add(new Ramp { Foot = centre + outward * ring - along * (length / 2f), Forward = along, Length = length, Width = 7f, Height = 2.4f });
            }

            const float spine = 7f;
            ramps.Add(new Ramp { Foot = centre + new Vec3(0f, 0f, -spine), Forward = new Vec3(0f, 0f, 1f), Length = spine, Width = 9f, Height = 3f });
            ramps.Add(new Ramp { Foot = centre + new Vec3(0f, 0f, spine), Forward = new Vec3(0f, 0f, -1f), Length = spine, Width = 9f, Height = 3f });
            return ramps;
        }

        /// <summary>A player's starting spot: one of <see cref="LobbyRoster.MaxPlayers"/> spots round the ring, by player id.</summary>
        public static ArenaSlot StartSlotFor(GameMode mode, int playerId)
        {
            var slots = StartSlots(CentreFor(mode), StartRadiusFor(mode), LobbyRoster.MaxPlayers);
            return slots[((playerId % slots.Count) + slots.Count) % slots.Count];
        }

        /// <summary>Starting spots are on a circle at this share of the arena's radius.</summary>
        public const float StartRingFraction = 0.7f;

        /// <summary><paramref name="count"/> spots evenly round a circle, each facing the centre.</summary>
        public static List<ArenaSlot> StartSlots(Vec3 centre, float arenaRadius, int count)
        {
            var slots = new List<ArenaSlot>(count);
            float ring = arenaRadius * StartRingFraction;
            for (int i = 0; i < count; i++)
            {
                float angle = 2f * MathF.PI * i / Math.Max(1, count);
                var outward = new Vec3(MathF.Sin(angle), 0f, MathF.Cos(angle));
                slots.Add(new ArenaSlot { Position = centre + outward * ring, Forward = -outward });
            }

            return slots;
        }

        /// <summary>Battle's item boxes: a ring of eight halfway out, and four round the centre.</summary>
        public static List<Vec3> BattleItemBoxes(Vec3 centre, float bowlRadius)
        {
            var boxes = new List<Vec3>();
            AddRing(boxes, centre, bowlRadius * 0.5f, 8);
            AddRing(boxes, centre, 3f, 4);
            return boxes;
        }

        private static void AddRing(List<Vec3> boxes, Vec3 centre, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = 2f * MathF.PI * (i + 0.5f) / count;
                boxes.Add(centre + new Vec3(MathF.Sin(angle) * radius, 0.75f, MathF.Cos(angle) * radius));
            }
        }
    }
}
