using System;
using System.Collections.Generic;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Bots
{
    /// <summary>What a bot can see when deciding whether to use its item.</summary>
    public struct BotItemSituation
    {
        /// <summary>Distance to the nearest rival in front, or <see cref="float.PositiveInfinity"/>.</summary>
        public float NearestAhead;

        /// <summary>Distance to the nearest rival behind, or <see cref="float.PositiveInfinity"/>.</summary>
        public float NearestBehind;

        /// <summary>The track just ahead is a straight (see <see cref="BotDriver.CurvatureAhead"/>).</summary>
        public bool OnStraight;

        /// <summary>
        /// A car still racing is ahead in the standings, so a Missile, Lightning or Seeker has a target.
        /// </summary>
        public bool HasCarAhead;
    }

    /// <summary>
    /// When a bot uses its item (design doc §9: "use items opportunistically"). Each item waits for
    /// its moment: Boost on a straight, Shockwave with a rival close by, Missile when there is a car
    /// ahead, Oil or a Fake box with a rival behind, Bomb with a rival in front, Lightning and Seeker
    /// whenever anyone still racing is ahead in the standings. Nothing is used before the bot's
    /// skill delay, and anything held too long is used anyway.
    /// </summary>
    public static class BotItems
    {
        public const float ShockwaveRange = 7f;
        public const float OilRange = 20f;
        public const float BombRange = 25f;
        public const float MissileRange = 60f;

        /// <summary>Curvature (see <see cref="BotDriver.CurvatureAhead"/>) below which the track counts as a straight.</summary>
        public const float StraightCurvature = 0.01f;

        /// <summary>A bot that has held an item this long uses it whatever the situation.</summary>
        public const float GiveUpSeconds = 10f;

        /// <summary>
        /// What a bot at <paramref name="position"/> facing <paramref name="forward"/> can see: the nearest of
        /// <paramref name="rivals"/> in front (on or ahead of the line through the bot at right angles to its heading)
        /// and behind, flattened to the ground plane, whether the bend ahead is straight (a driver with no track, such
        /// as an arena driver, passes 0) and whether anyone still racing leads it in <paramref name="standings"/>.
        /// </summary>
        public static BotItemSituation Observe(
            Vec3 position,
            Vec3 forward,
            IReadOnlyList<Vec3> rivals,
            float curvatureAhead,
            StandingsMessage standings,
            byte playerId)
        {
            var situation = new BotItemSituation
            {
                NearestAhead = float.PositiveInfinity,
                NearestBehind = float.PositiveInfinity,
                OnStraight = curvatureAhead < StraightCurvature,
            };

            // Only a car still racing counts, as the item rules leave finished (or out) cars be.
            for (int i = 0; standings != null && i < standings.Order.Count && standings.Order[i].PlayerId != playerId; i++)
            {
                if (standings.Order[i].FinishPosition == 0)
                {
                    situation.HasCarAhead = true;
                    break;
                }
            }

            // Indexed rather than `foreach`, which would box the list's enumerator on every call.
            for (int i = 0; i < rivals.Count; i++)
            {
                var offset = (rivals[i] - position).Flattened;
                float distance = offset.Length;
                if (Vec3.Dot(offset, forward) >= 0f)
                {
                    situation.NearestAhead = Math.Min(situation.NearestAhead, distance);
                }
                else
                {
                    situation.NearestBehind = Math.Min(situation.NearestBehind, distance);
                }
            }

            return situation;
        }

        public static bool ShouldUse(ItemKind held, float heldSeconds, BotSkill skill, BotItemSituation situation)
        {
            if (held == ItemKind.None || heldSeconds < skill.ItemDelaySeconds)
            {
                return false;
            }

            if (heldSeconds >= GiveUpSeconds)
            {
                return true;
            }

            switch (held)
            {
                case ItemKind.Boost:
                    return situation.OnStraight;
                case ItemKind.Shield:
                    return true;
                case ItemKind.Shockwave:
                    return situation.NearestAhead <= ShockwaveRange || situation.NearestBehind <= ShockwaveRange;
                case ItemKind.Missile:
                    return situation.HasCarAhead && situation.NearestAhead <= MissileRange;
                case ItemKind.OilSlick:
                    return situation.NearestBehind <= OilRange;
                case ItemKind.Bomb:
                    return situation.NearestAhead <= BombRange;
                case ItemKind.FakeBox:
                    return situation.NearestBehind <= OilRange;
                case ItemKind.Lightning:
                case ItemKind.Seeker:
                    return situation.HasCarAhead;
                default:
                    return false;
            }
        }
    }
}
