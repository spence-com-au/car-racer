using System;
using System.Collections.Generic;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ItemRollTests
    {
        /// <summary>Every item the game defines, so a new <see cref="ItemKind"/> is checked without editing a table.</summary>
        private static readonly ItemKind[] AllItems = Array.FindAll(
            (ItemKind[])Enum.GetValues(typeof(ItemKind)),
            kind => kind != ItemKind.None);

        private static float OddsOf(ItemKind kind, float backness) =>
            ItemRoll.Odds(backness)[System.Array.IndexOf(ItemRoll.Kinds, kind)];

        [Theory]
        [InlineData(1, 6, 0f)]
        [InlineData(6, 6, 1f)]
        [InlineData(2, 3, 0.5f)]
        [InlineData(1, 1, 0.5f)]
        [InlineData(9, 4, 1f)]
        [InlineData(0, 4, 0f)]
        public void BacknessRunsFromLeaderToLast(int position, int racers, float expected)
        {
            Assert.Equal(expected, ItemRoll.Backness(position, racers), 4);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(1f)]
        public void OddsSumToOne(float backness)
        {
            float total = 0f;
            foreach (float p in ItemRoll.Odds(backness))
            {
                Assert.True(p >= 0f);
                total += p;
            }

            Assert.Equal(1f, total, 4);
        }

        [Fact]
        public void TheLeaderNeverGetsAMissileAndLastNeverGetsOil()
        {
            Assert.Equal(0f, OddsOf(ItemKind.Missile, 0f));
            Assert.Equal(0f, OddsOf(ItemKind.OilSlick, 1f));
        }

        [Fact]
        public void OnlyTheBackOfThePackGetsLightningOrASeekerAndOnlyTheFrontAFakeBox()
        {
            Assert.Equal(0f, OddsOf(ItemKind.Lightning, 0f));
            Assert.Equal(0f, OddsOf(ItemKind.Seeker, 0f));
            Assert.Equal(0f, OddsOf(ItemKind.FakeBox, 1f));
            Assert.True(OddsOf(ItemKind.Lightning, 1f) > 0f);
            Assert.True(OddsOf(ItemKind.Seeker, 1f) > 0f);
            Assert.True(OddsOf(ItemKind.FakeBox, 0f) > 0f);

            // Every item can turn up for someone in the middle of the pack.
            foreach (var kind in ItemRoll.Kinds)
            {
                Assert.True(OddsOf(kind, 0.5f) > 0f, kind.ToString());
            }
        }

        [Fact]
        public void EveryItemKindIsRolledOnceWithAWeightInEachTable()
        {
            var rolled = (ItemKind[])ItemRoll.Kinds.Clone();
            Array.Sort(rolled);

            Assert.Equal(AllItems, rolled);
            Assert.Equal(ItemRoll.Kinds.Length, ItemRoll.FrontWeights.Length);
            Assert.Equal(ItemRoll.Kinds.Length, ItemRoll.BackWeights.Length);
        }

        [Fact]
        public void EveryItemHasItsOwnLabel()
        {
            var labels = new HashSet<string>();
            foreach (var kind in AllItems)
            {
                string label = ItemNames.Label(kind);
                Assert.NotEqual("ITEM", label);
                Assert.True(labels.Add(label), label);
            }
        }

        [Fact]
        public void CatchUpItemsGetLikelierFurtherBack()
        {
            foreach (var kind in new[] { ItemKind.Boost, ItemKind.Missile, ItemKind.Shockwave, ItemKind.Lightning, ItemKind.Seeker })
            {
                Assert.True(OddsOf(kind, 1f) > OddsOf(kind, 0.5f), kind.ToString());
                Assert.True(OddsOf(kind, 0.5f) > OddsOf(kind, 0f), kind.ToString());
            }

            foreach (var kind in new[] { ItemKind.Shield, ItemKind.OilSlick, ItemKind.FakeBox })
            {
                Assert.True(OddsOf(kind, 0f) > OddsOf(kind, 1f), kind.ToString());
            }
        }

        [Fact]
        public void RollsFollowTheOdds()
        {
            // Sweeping random01 evenly across [0, 1) hands out each item in proportion to its odds.
            const int samples = 10000;
            var counts = new Dictionary<ItemKind, int>();
            for (int i = 0; i < samples; i++)
            {
                var kind = ItemRoll.Roll(3, 5, (i + 0.5) / samples);
                counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
            }

            var odds = ItemRoll.Odds(0.5f);
            for (int i = 0; i < ItemRoll.Kinds.Length; i++)
            {
                counts.TryGetValue(ItemRoll.Kinds[i], out int n);
                Assert.Equal(odds[i], n / (float)samples, 2);
            }
        }

        [Fact]
        public void TheEdgesOfTheRangeStillGiveAnItemWithOdds()
        {
            // The leader's last item with odds is the Fake box; last place's is the Seeker.
            Assert.Equal(ItemKind.Boost, ItemRoll.Roll(1, 6, 0.0));
            Assert.Equal(ItemKind.FakeBox, ItemRoll.Roll(1, 6, 0.9999999));
            Assert.NotEqual(ItemKind.OilSlick, ItemRoll.Roll(6, 6, 0.9999999));
            Assert.Equal(ItemKind.Seeker, ItemRoll.Roll(6, 6, 1.0));
        }

        [Fact]
        public void TheLeaderNeverRollsAMissile()
        {
            for (int i = 0; i < 1000; i++)
            {
                Assert.NotEqual(ItemKind.Missile, ItemRoll.Roll(1, 4, i / 1000.0));
            }
        }
    }

    public class ItemBoxFieldTests
    {
        private static ItemBoxField Field() => new ItemBoxField(new List<Vec3>
        {
            new Vec3(0f, 0.75f, 10f),
            new Vec3(3f, 0.75f, 10f),
        });

        [Fact]
        public void ACarTouchesTheNearestBoxInReach()
        {
            var field = Field();
            Assert.Equal(0, field.FindTouched(new Vec3(0.5f, 0.5f, 10f), 0));
            Assert.Equal(1, field.FindTouched(new Vec3(2.0f, 0.5f, 10f), 0));
            Assert.Equal(-1, field.FindTouched(new Vec3(0f, 0.5f, 5f), 0));
        }

        [Fact]
        public void TheFirstClaimWinsAndTheBoxComesBack()
        {
            var field = Field();
            Assert.True(field.TryTake(0, 1000, out long respawnAt));
            Assert.Equal(1000 + ItemBoxField.RespawnMs, respawnAt);
            Assert.False(field.TryTake(0, 1050, out _));
            Assert.False(field.IsUp(0, respawnAt - 1));
            Assert.Equal(-1, field.FindTouched(field.Position(0), respawnAt - 1));
            Assert.True(field.IsUp(0, respawnAt));
            Assert.True(field.TryTake(0, respawnAt, out _));
        }

        [Fact]
        public void ADownBoxDoesNotHideOneNextToIt()
        {
            var field = Field();
            field.MarkTaken(0, 5000);
            Assert.Equal(1, field.FindTouched(new Vec3(1.5f, 0.5f, 10f), 0));
        }

        [Fact]
        public void ClaimsOnBoxesThatDoNotExistAreRefused()
        {
            var field = Field();
            Assert.False(field.TryTake(-1, 0, out _));
            Assert.False(field.TryTake(2, 0, out _));
        }

        [Fact]
        public void MirroringNeverBringsABoxBackEarly()
        {
            var field = Field();
            field.MarkTaken(0, 5000); // hidden locally on pickup
            field.MarkTaken(0, 4000); // an older report from the host
            Assert.False(field.IsUp(0, 4500));
        }

        [Fact]
        public void BoxesAreUpWhateverTheClockReads()
        {
            var field = Field();
            Assert.True(field.IsUp(0, -1_000_000));
            Assert.Equal(0, field.FindTouched(field.Position(0), long.MinValue + 1));
            Assert.True(field.TryTake(0, -5000, out long respawnAt));
            Assert.Equal(-5000 + ItemBoxField.RespawnMs, respawnAt);
        }

        [Fact]
        public void ResetPutsEveryBoxBack()
        {
            var field = Field();
            field.TryTake(0, 0, out _);
            field.TryTake(1, 0, out _);
            field.Reset();
            Assert.True(field.IsUp(0, 0));
            Assert.True(field.IsUp(1, 0));
        }
    }

    public class ItemSlotsTests
    {
        [Fact]
        public void APlayerHoldsOneItemAndCanOnlyUseThatOne()
        {
            var slots = new ItemSlots();
            Assert.True(slots.CanPickUp(1));
            slots.Give(1, ItemKind.Missile);
            Assert.False(slots.CanPickUp(1));
            Assert.Equal(ItemKind.Missile, slots.Held(1));
            Assert.False(slots.TryUse(1, ItemKind.Boost));
            Assert.True(slots.TryUse(1, ItemKind.Missile));
            Assert.False(slots.TryUse(1, ItemKind.Missile));
            Assert.True(slots.CanPickUp(1));
        }

        [Fact]
        public void SlotsArePerPlayerAndClearForANewRace()
        {
            var slots = new ItemSlots();
            slots.Give(1, ItemKind.Shield);
            slots.Give(2, ItemKind.Bomb);
            slots.Remove(1);
            Assert.Equal(ItemKind.None, slots.Held(1));
            Assert.Equal(ItemKind.Bomb, slots.Held(2));
            slots.Clear();
            Assert.Equal(ItemKind.None, slots.Held(2));
            Assert.False(slots.TryUse(2, ItemKind.None));
        }
    }

    public class ShieldTests
    {
        [Fact]
        public void TheShieldLastsItsTimeThenDrops()
        {
            var shield = new Shield();
            Assert.False(shield.IsUp);
            shield.Raise();
            shield.Tick(Shield.Seconds - 0.1f);
            Assert.True(shield.IsUp);
            shield.Tick(0.2f);
            Assert.False(shield.IsUp);
            Assert.Equal(0f, shield.SecondsLeft);
        }

        [Fact]
        public void TheShieldSoaksUpOneHit()
        {
            var shield = new Shield();
            Assert.False(shield.Absorb());
            shield.Raise();
            Assert.True(shield.Absorb());
            Assert.False(shield.IsUp);
            Assert.False(shield.Absorb());
        }

        [Fact]
        public void ClearDropsTheShield()
        {
            var shield = new Shield();
            shield.Raise();
            shield.Clear();
            Assert.False(shield.IsUp);
        }
    }

    public class ItemHitTests
    {
        [Fact]
        public void AShieldBlocksOneItemHitThenTheNextSpinsTheCarOut()
        {
            var shield = new Shield();
            shield.Raise();
            var hit = new HitMessage { IsItemHit = true };
            Assert.Equal(ItemHitOutcome.Blocked, ItemHitResponse.Resolve(hit, shield));
            Assert.Equal(ItemHitOutcome.SpinOut, ItemHitResponse.Resolve(hit, shield));
        }

        [Fact]
        public void BumpsAreNotItemHitsAndLeaveTheShieldUp()
        {
            var shield = new Shield();
            shield.Raise();
            Assert.Equal(ItemHitOutcome.NotAnItemHit, ItemHitResponse.Resolve(new HitMessage(), shield));
            Assert.True(shield.IsUp);
        }

        [Fact]
        public void AShockwavePushesStraightAwayWithAPop()
        {
            var impulse = ItemEffects.ShockwaveImpulse(new Vec3(0f, 0f, 0f), new Vec3(3f, 5f, 4f));
            Assert.Equal(ItemEffects.ShockwavePush * 0.6f, impulse.X, 3);
            Assert.Equal(ItemEffects.ShockwavePush * 0.8f, impulse.Z, 3);
            Assert.Equal(ItemEffects.HitPop, impulse.Y, 3);

            // A car right on top of the user still gets pushed somewhere.
            Assert.Equal(ItemEffects.ShockwavePush, ItemEffects.ShockwaveImpulse(Vec3.Zero, Vec3.Zero).Flattened.Length, 3);
        }

        [Fact]
        public void OilLandsBehindTheCar()
        {
            var behind = ItemEffects.OilPosition(new Vec3(10f, 0.6f, 0f), MathF.PI / 2f); // facing +X
            Assert.True(behind.ApproximatelyEquals(new Vec3(10f - ItemEffects.OilDropBehind, 0.6f, 0f)));
        }

        [Fact]
        public void HazardsArmTouchAndExpire()
        {
            var field = new HazardField();
            field.AddDropped(new ItemUsedMessage { EffectId = 3, PlayerId = 1, Position = new Vec3(0f, 0.6f, 10f) }, 1000);
            var oil = field.Active[0].Position;
            Assert.False(field.TryFindTouched(oil, 1000 + ItemEffects.OilArmMs - 1, out _));
            Assert.True(field.TryFindTouched(oil + new Vec3(ItemEffects.OilRadius - 0.1f, 0f, 0f), 1000 + ItemEffects.OilArmMs, out var hazard));
            Assert.Equal(3, hazard.EffectId);
            Assert.False(field.TryFindTouched(oil + new Vec3(ItemEffects.OilRadius + 0.1f, 0f, 0f), 1000 + ItemEffects.OilArmMs, out _));
            field.Expire(1000 + ItemEffects.OilLifetimeMs);
            Assert.Empty(field.Active);
        }

        [Fact]
        public void AFakeBoxSitsBehindTheCarAndCatchesAtABoxsReach()
        {
            var field = new HazardField();
            field.AddDropped(new ItemUsedMessage { EffectId = 4, PlayerId = 1, Item = ItemKind.FakeBox, Position = new Vec3(0f, 0.6f, 10f) }, 0);
            var fake = Assert.Single(field.Active);
            Assert.Equal(ItemKind.FakeBox, fake.Item);
            Assert.True(fake.Position.ApproximatelyEquals(ItemEffects.OilPosition(new Vec3(0f, 0.6f, 10f), 0f)));

            long armed = ItemEffects.OilArmMs;
            Assert.True(field.TryFindTouched(fake.Position + new Vec3(ItemEffects.FakeBoxRadius - 0.1f, 0f, 0f), armed, out _));
            Assert.False(field.TryFindTouched(fake.Position + new Vec3(ItemEffects.FakeBoxRadius + 0.1f, 0f, 0f), armed, out _));
            Assert.True(ItemEffects.IsDropped(ItemKind.FakeBox));
            Assert.True(ItemEffects.IsDropped(ItemKind.OilSlick));
            Assert.False(ItemEffects.IsDropped(ItemKind.Bomb));
        }

        [Fact]
        public void AddingTheSameEffectTwiceKeepsOneHazard()
        {
            var field = new HazardField();
            var used = new ItemUsedMessage { EffectId = 3 };
            field.AddDropped(used, 0);
            field.AddDropped(used, 10);
            Assert.Single(field.Active);
            Assert.True(field.Remove(3));
            Assert.False(field.Remove(3));
        }
    }

    public class ProjectileRulesTests
    {
        private const float Dt = 0.02f;

        private static ItemUsedMessage Used(ItemKind kind, float heading = 0f, Vec3 velocity = default) =>
            new ItemUsedMessage { EffectId = 9, PlayerId = 2, Item = kind, Position = new Vec3(0f, 0.6f, 0f), HeadingRadians = heading, Velocity = velocity };

        [Fact]
        public void AMissileWithNoTargetFliesStraightAtItsHeight()
        {
            var m = ProjectileRules.LaunchMissile(Used(ItemKind.Missile), ProjectileRules.NoTarget, 1000);
            Assert.Equal(new Vec3(0f, 0.6f, ProjectileRules.MissileLaunchAhead), m.Position);
            Assert.Equal(1000 + ProjectileRules.MissileLifetimeMs, m.ExpiresAtMs);
            for (int i = 0; i < 50; i++)
            {
                m = ProjectileRules.Step(m, Dt, false, default);
            }

            Assert.Equal(0f, m.Position.X, 3);
            Assert.Equal(0.6f, m.Position.Y, 3);
            Assert.Equal(ProjectileRules.MissileLaunchAhead + ProjectileRules.MissileSpeed * 1f, m.Position.Z, 2);
        }

        [Fact]
        public void AMissileTurnsTowardsItsTargetNoFasterThanItsTurnRate()
        {
            var m = ProjectileRules.LaunchMissile(Used(ItemKind.Missile), 1, 0);
            var target = new Vec3(-50f, 0.6f, m.Position.Z); // hard left
            m = ProjectileRules.Step(m, Dt, true, target);
            float turned = MathF.Atan2(m.Velocity.X, m.Velocity.Z) * 180f / MathF.PI;
            Assert.Equal(-ProjectileRules.MissileTurnDegreesPerSecond * Dt, turned, 2);
            Assert.Equal(ProjectileRules.MissileSpeed, m.Velocity.Length, 2);
        }

        [Fact]
        public void AMissileCatchesATargetAhead()
        {
            var m = ProjectileRules.LaunchMissile(Used(ItemKind.Missile), 1, 0);
            var target = new Vec3(15f, 0.6f, 40f);
            for (int i = 0; i < 200; i++)
            {
                m = ProjectileRules.Step(m, Dt, true, target);
                if ((m.Position - target).Flattened.Length <= ProjectileRules.HitRadius)
                {
                    return;
                }
            }

            Assert.Fail("the missile never reached its target");
        }

        [Fact]
        public void ASeekerIsFasterNimblerAndLongerLivedThanAMissile()
        {
            var s = ProjectileRules.LaunchSeeker(Used(ItemKind.Seeker), 1, 1000);
            Assert.Equal(ItemKind.Seeker, s.Kind);
            Assert.Equal(1000 + ProjectileRules.SeekerLifetimeMs, s.ExpiresAtMs);
            Assert.True(ProjectileRules.SeekerSpeed > ProjectileRules.MissileSpeed);
            Assert.True(ProjectileRules.SeekerLifetimeMs > ProjectileRules.MissileLifetimeMs);

            var target = new Vec3(-50f, 0.6f, s.Position.Z); // hard left
            s = ProjectileRules.Step(s, Dt, true, target);
            float turned = MathF.Atan2(s.Velocity.X, s.Velocity.Z) * 180f / MathF.PI;
            Assert.Equal(-ProjectileRules.SeekerTurnDegreesPerSecond * Dt, turned, 2);
            Assert.Equal(ProjectileRules.SeekerSpeed, s.Velocity.Length, 2);
            Assert.True(ProjectileRules.IsHoming(ItemKind.Seeker));
            Assert.False(ProjectileRules.IsHoming(ItemKind.Bomb));
        }

        [Fact]
        public void ABombArcsLandsAndBounces()
        {
            var b = ProjectileRules.LaunchBomb(Used(ItemKind.Bomb, velocity: new Vec3(0f, 0f, 10f)), 0);
            Assert.Equal(10f + ProjectileRules.BombThrowSpeed, b.Velocity.Z, 3);
            float highest = 0f;
            bool bounced = false;
            for (int i = 0; i < 100; i++)
            {
                float vyBefore = b.Velocity.Y;
                b = ProjectileRules.Step(b, Dt, false, default);
                highest = Math.Max(highest, b.Position.Y);
                Assert.True(b.Position.Y >= b.GroundY - 1e-4f);
                bounced |= vyBefore < 0f && b.Velocity.Y > 0f;
            }

            Assert.True(highest > b.GroundY + 1f, "it is lobbed");
            Assert.True(bounced, "it bounces when it lands");
            Assert.True(b.Position.Z > 10f, "it travels forwards");
        }

        [Fact]
        public void HitsPushAlongTheMissileAndAwayFromTheBlast()
        {
            var m = ProjectileRules.LaunchMissile(Used(ItemKind.Missile, MathF.PI / 2f), 1, 0);
            var push = ProjectileRules.MissileImpulse(m);
            Assert.Equal(ProjectileRules.MissilePush, push.X, 3);
            Assert.Equal(ItemEffects.HitPop, push.Y, 3);

            var blast = ProjectileRules.BlastImpulse(Vec3.Zero, new Vec3(0f, 0f, -2f));
            Assert.Equal(-ProjectileRules.BombPush, blast.Z, 3);
        }
    }
}
