using System;
using System.Collections.Generic;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Physics
{
    /// <summary>Another player's car as this phone sees it (its predicted position).</summary>
    public struct OtherCar
    {
        public byte PlayerId;
        public Vec3 Position;
        public Vec3 Velocity;

        public OtherCar(byte playerId, Vec3 position, Vec3 velocity)
        {
            PlayerId = playerId;
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>A bump this phone's car takes: push itself by <see cref="SelfImpulse"/>, and tell the other phone with <see cref="Fallback"/>.</summary>
    public struct Bump
    {
        public byte OtherPlayerId;
        public Vec3 SelfImpulse;
        public HitMessage Fallback;
    }

    /// <summary>
    /// Car-to-car contact for this phone's own car (design doc §2.2). Each physics step it checks
    /// the car against every other car's predicted position. A contact pushes this car by its own
    /// share of the impulse from <see cref="BumpResolver"/>, which is antisymmetric, so the other
    /// phone pushes its car the opposite way when it sees the same contact. A
    /// <see cref="HitMessage"/> carries the other car's share in case its phone missed the contact.
    /// <see cref="HitDeduplicator"/> makes sure each bump moves a car only once either way.
    /// </summary>
    public sealed class BumpMonitor
    {
        /// <summary>Contact radius of a car: a little larger than its physics sphere, so contacts feel generous (design doc §2.2).</summary>
        public const float CarRadius = 1.0f;

        /// <summary>All cars have the same mass: car stats change handling, not mass (design doc §4).</summary>
        public const float CarMass = 1f;

        /// <summary>
        /// Reduced mass of two cars in a bump, which <see cref="BumpResolver"/> clamps per unit of: half a car's, since
        /// all weigh the same.
        /// </summary>
        public const float ReducedMass = CarMass / 2f;

        /// <summary>
        /// How far from this car on the ground plane another car can be, as this phone last saw it, and still have its
        /// fallback bump apply: well past the contact reach, since the two phones' views disagree a little and the cars
        /// part while the hit is on its way, but nowhere near across the track (design doc §2.2).
        /// </summary>
        public const float FallbackReach = 8f;

        /// <summary>
        /// How long after this car takes a fallback bump from another it ignores the next from that one. By then the
        /// hardest bump has worn off: the car has landed from its pop, grip has taken out a sideways push and
        /// <see cref="Driving.CarTuning.OverSpeedDeceleration"/> a forward one, so a modified phone sending it over and
        /// over can't build up speed to fling the car off the map (design doc §2.2).
        /// </summary>
        public const long FallbackCooldownMs = 1500;

        private readonly BumpSettings _settings;
        private readonly HitDeduplicator _dedupe = new HitDeduplicator();

        /// <summary>The other cars within <see cref="FallbackReach"/> at the last <see cref="Step"/>.</summary>
        private readonly HashSet<byte> _withinReach = new HashSet<byte>();

        /// <summary>When this car last took a fallback bump from each other car.</summary>
        private readonly Dictionary<byte, long> _lastFallbackMs = new Dictionary<byte, long>();

        public BumpMonitor(BumpSettings settings = null)
        {
            _settings = settings ?? new BumpSettings();
        }

        /// <summary>
        /// Checks this car against the others, noting which are near enough for <see cref="OnHit"/> to take their
        /// fallback bumps; appends a <see cref="Bump"/> for each new contact.
        /// </summary>
        public void Step(byte selfId, Vec3 selfPosition, Vec3 selfVelocity, IReadOnlyList<OtherCar> others, long nowMs, List<Bump> bumps)
        {
            var self = new CarBody(selfPosition, selfVelocity, CarMass, CarRadius);
            _withinReach.Clear();
            foreach (var otherCar in others)
            {
                if (Vec3.WithinFlat(otherCar.Position, selfPosition, FallbackReach))
                {
                    _withinReach.Add(otherCar.PlayerId);
                }

                var other = new CarBody(otherCar.Position, otherCar.Velocity, CarMass, CarRadius);
                if (!BumpResolver.AreTouching(self, other))
                {
                    continue;
                }

                Vec3 impulse = BumpResolver.ComputeSelfImpulse(self, other, _settings);
                if (impulse == Vec3.Zero || !_dedupe.TryApplyLocalContact(otherCar.PlayerId, nowMs))
                {
                    continue;
                }

                bumps.Add(new Bump
                {
                    OtherPlayerId = otherCar.PlayerId,
                    SelfImpulse = impulse,
                    Fallback = new HitMessage
                    {
                        AttackerId = selfId,
                        TargetId = otherCar.PlayerId,
                        SentAtMs = nowMs,
                        Impulse = BumpResolver.ComputeSelfImpulse(other, self, _settings),
                    },
                });
            }
        }

        /// <summary>
        /// A hit addressed to this car arrived. Returns true (with the impulse to apply) unless its impulse is not
        /// finite, or it is a car-to-car bump that this phone already applied from its own view, from a car that was
        /// not within <see cref="FallbackReach"/> at the last <see cref="Step"/>, or within
        /// <see cref="FallbackCooldownMs"/> of the last one this car took from it. Item hits always apply. A bump's
        /// impulse is clamped to what <see cref="BumpResolver"/> could have produced, because it comes from another
        /// phone.
        /// </summary>
        public bool OnHit(HitMessage hit, byte selfId, long nowMs, out Vec3 impulse)
        {
            impulse = Vec3.Zero;
            if (hit.TargetId != selfId || !hit.Impulse.IsFinite)
            {
                return false;
            }

            if (hit.IsItemHit)
            {
                impulse = hit.Impulse;
                return true;
            }

            // Both checks come before the dedupe window, so a dropped hit doesn't stop this phone's own view of the
            // contact from applying it.
            bool tooSoon = _lastFallbackMs.TryGetValue(hit.AttackerId, out long last)
                && nowMs - last < FallbackCooldownMs;
            if (tooSoon || !_withinReach.Contains(hit.AttackerId) || !_dedupe.TryApplyRemoteHit(hit.AttackerId, nowMs))
            {
                return false;
            }

            _lastFallbackMs[hit.AttackerId] = nowMs;
            impulse = LimitBump(hit.Impulse);
            return true;
        }

        private Vec3 LimitBump(Vec3 impulse)
        {
            float maxHorizontal = _settings.MaxImpulsePerMass * ReducedMass;
            Vec3 horizontal = impulse.Flattened;
            if (horizontal.Length > maxHorizontal)
            {
                horizontal = horizontal.Normalized * maxHorizontal;
            }

            return horizontal + Vec3.Up * Math.Clamp(impulse.Y, 0f, maxHorizontal * _settings.LiftFraction);
        }
    }
}
