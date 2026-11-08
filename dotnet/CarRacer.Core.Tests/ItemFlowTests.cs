using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ItemMessageTests
    {
        [Fact]
        public void ItemMessagesRoundTrip()
        {
            var claim = Assert.IsType<ItemBoxClaimMessage>(MessageCodec.Decode(MessageCodec.Encode(new ItemBoxClaimMessage { PlayerId = 3, BoxIndex = 513 })));
            Assert.Equal(3, claim.PlayerId);
            Assert.Equal(513, claim.BoxIndex);

            var taken = Assert.IsType<ItemBoxTakenMessage>(MessageCodec.Decode(MessageCodec.Encode(new ItemBoxTakenMessage
            {
                BoxIndex = 7, PlayerId = 2, Item = ItemKind.Bomb, RespawnAtHostMs = 123_456_789_012,
            })));
            Assert.Equal(7, taken.BoxIndex);
            Assert.Equal(2, taken.PlayerId);
            Assert.Equal(ItemKind.Bomb, taken.Item);
            Assert.Equal(123_456_789_012, taken.RespawnAtHostMs);

            var used = Assert.IsType<ItemUsedMessage>(MessageCodec.Decode(MessageCodec.Encode(new ItemUsedMessage
            {
                PlayerId = 1, Item = ItemKind.Missile, Position = new Vec3(1f, 2f, 3f), Velocity = new Vec3(-4f, 0f, 5.5f), HeadingRadians = 1.25f, UsedAtHostMs = 99, EffectId = 40000,
            })));
            Assert.Equal(ItemKind.Missile, used.Item);
            Assert.Equal(new Vec3(1f, 2f, 3f), used.Position);
            Assert.Equal(new Vec3(-4f, 0f, 5.5f), used.Velocity);
            Assert.Equal(1.25f, used.HeadingRadians);
            Assert.Equal(99, used.UsedAtHostMs);
            Assert.Equal(40000, used.EffectId);

            var gone = Assert.IsType<HazardGoneMessage>(MessageCodec.Decode(MessageCodec.Encode(new HazardGoneMessage
            {
                EffectId = 1234, Item = ItemKind.Bomb, Position = new Vec3(4f, 0.5f, -6f),
            })));
            Assert.Equal(1234, gone.EffectId);
            Assert.Equal(ItemKind.Bomb, gone.Item);
            Assert.Equal(new Vec3(4f, 0.5f, -6f), gone.Position);

            var flying = new ProjectilesMessage();
            flying.Projectiles.Add(new ProjectileSnapshot { EffectId = 5, Kind = ItemKind.Bomb, Position = new Vec3(1f, 2f, 3f) });
            flying.Projectiles.Add(new ProjectileSnapshot { EffectId = 6, Kind = ItemKind.Missile, Position = new Vec3(-1f, 0f, 9f), TargetId = 3 });
            var decoded = Assert.IsType<ProjectilesMessage>(MessageCodec.Decode(MessageCodec.Encode(flying)));
            Assert.Equal(2, decoded.Projectiles.Count);
            Assert.Equal(ItemKind.Bomb, decoded.Projectiles[0].Kind);
            Assert.Equal(new Vec3(-1f, 0f, 9f), decoded.Projectiles[1].Position);
            Assert.Equal(3, decoded.Projectiles[1].TargetId);
        }
    }

    public class ItemFlowTests
    {
        private const ushort Version = 18;
        private const ulong GuestConnection = 7;
        private const long GuestClockBehindMs = 1_000_000;

        private static readonly List<Vec3> BoxPositions = new List<Vec3> { new Vec3(0f, 0.75f, 20f), new Vec3(3f, 0.75f, 20f) };

        /// <summary>Delivers the host's messages to the guest, unless held back to simulate latency.</summary>
        private sealed class HostLink : ISessionTransport
        {
            public readonly List<IGameMessage> Sent = new List<IGameMessage>();
            public readonly List<IGameMessage> Held = new List<IGameMessage>();
            public GuestSession Guest;
            public bool HoldBack;

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                Sent.Add(message);
                if (HoldBack)
                {
                    Held.Add(message);
                }
                else
                {
                    Guest?.OnMessage(message);
                }
            }

            public void Release()
            {
                HoldBack = false;
                foreach (var m in Held)
                {
                    Guest.OnMessage(m);
                }

                Held.Clear();
            }

            public void Disconnect(ulong connection)
            {
            }
        }

        /// <summary>Delivers the guest's messages to the host, unless dropped.</summary>
        private sealed class GuestLink : ISessionTransport
        {
            public readonly List<IGameMessage> Sent = new List<IGameMessage>();
            public HostSession Host;
            public bool Drop;

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                Sent.Add(message);
                if (!Drop)
                {
                    Host.OnMessage(GuestConnection, message);
                }
            }

            public void Disconnect(ulong connection)
            {
            }
        }

        private long _now = 10_000;
        private double _nextRandom = 0.0;
        private readonly HostLink _hostLink = new HostLink();
        private readonly GuestLink _guestLink = new GuestLink();
        private readonly HostSession _host;
        private readonly HostRace _hostRace;
        private readonly HostItems _hostItems;
        private readonly GuestSession _guest;
        private readonly GuestRace _guestRace;
        private readonly GuestItems _guestItems;
        private readonly List<ItemUsedMessage> _hostSaw = new List<ItemUsedMessage>();
        private readonly List<ItemUsedMessage> _guestSaw = new List<ItemUsedMessage>();
        private readonly Dictionary<byte, Vec3> _cars = new Dictionary<byte, Vec3>();
        private readonly List<HitMessage> _guestHits = new List<HitMessage>();
        private readonly List<HitMessage> _hostHits = new List<HitMessage>();

        public ItemFlowTests()
        {
            _host = new HostSession(_hostLink, "Host", Version, () => _now);
            _hostRace = new HostRace(_host, new[] { 4 }, () => _now, id => 0f);
            _hostItems = new HostItems(_host, _hostRace, BoxPositions, () => _now, () => _nextRandom, _cars.TryGetValue);
            _host.HitReceived += _hostHits.Add;
            _hostItems.ItemUsed += _hostSaw.Add;

            _guestLink.Host = _host;
            _guest = new GuestSession(_guestLink, "Guest", Version, () => _now - GuestClockBehindMs);
            _hostLink.Guest = _guest;
            _guest.OnConnected();
            _guest.OnMessage(new ClockPongMessage { GuestSentMs = _now - GuestClockBehindMs - 20, HostMs = _now - 10 }); // 20 ms round trip
            _guestRace = new GuestRace(_guest, () => _now - GuestClockBehindMs);
            _guestItems = new GuestItems(_guest, _guestRace, BoxPositions, () => _now - GuestClockBehindMs);
            _guestItems.ItemUsed += _guestSaw.Add;
            _guest.HitReceived += _guestHits.Add;
        }

        private byte GuestId => _guest.PlayerId;

        private void Tick()
        {
            _hostRace.Tick();
            _guestRace.Tick();
            _hostItems.Tick();
            _guestItems.Tick();
        }

        private void StartRacing()
        {
            Assert.True(_hostRace.Start(3));
            Tick();
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            Assert.Equal(RacePhase.Racing, _hostRace.Phase);
            Assert.Equal(RacePhase.Racing, _guestRace.Phase);
        }

        [Fact]
        public void TheHostsRulesReachEveryPhoneInTheCountdown()
        {
            var settings = _host.Settings;
            settings.Rules = new EventRules(damage: false, items: true, carStats: false);
            _host.ChangeSettings(settings);
            StartRacing();
            Assert.False(_hostRace.Rules.Damage);
            Assert.False(_guestRace.Rules.Damage);
            Assert.False(_guestRace.Rules.CarStats);
            Assert.True(_guestRace.Rules.Items);
        }

        [Fact]
        public void WithItemsOffBoxesGiveNothingAndAreHidden()
        {
            Assert.True(_hostItems.BoxesShown, "boxes show in the lobby");
            var settings = _host.Settings;
            settings.Rules = EventRules.Default.WithItems(false);
            _host.ChangeSettings(settings);
            StartRacing();
            Assert.False(_hostItems.BoxesShown);
            Assert.False(_guestItems.BoxesShown);

            Assert.False(_hostItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.False(_guestItems.ClaimTouchedBox(BoxPositions[1]));

            // Even a claim that gets through is refused.
            _host.OnMessage(GuestConnection, new ItemBoxClaimMessage { PlayerId = GuestId, BoxIndex = 1 });
            Assert.Empty(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
        }

        [Fact]
        public void NoClaimsOutsideARace()
        {
            Assert.False(_hostItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.False(_guestItems.ClaimTouchedBox(BoxPositions[1]));
            Assert.Empty(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
        }

        [Fact]
        public void AGuestDrivingThroughABoxGetsAnItemAndEveryoneSeesTheBoxGo()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[1] + new Vec3(0.5f, -0.5f, 0f)));

            var taken = Assert.Single(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
            Assert.Equal(1, taken.BoxIndex);
            Assert.Equal(GuestId, taken.PlayerId);
            Assert.Equal(_now + ItemBoxField.RespawnMs, taken.RespawnAtHostMs);
            Assert.NotEqual(ItemKind.None, taken.Item);
            Assert.Equal(taken.Item, _guestItems.Held);
            Assert.Equal(taken.Item, _hostItems.Slots.Held(GuestId));
            Assert.Equal(ItemKind.None, _hostItems.Held);

            // Down on both phones until it comes back, in each phone's own clock.
            Assert.False(_hostItems.Boxes.IsUp(1, _now));
            Assert.False(_guestItems.Boxes.IsUp(1, _now - GuestClockBehindMs));
            Assert.True(_hostItems.Boxes.IsUp(1, taken.RespawnAtHostMs));
            Assert.True(_guestItems.Boxes.IsUp(1, _guest.Clock.ToLocalTime(taken.RespawnAtHostMs)));
        }

        [Fact]
        public void TheFirstToArriveGetsTheBox()
        {
            StartRacing();
            _hostLink.HoldBack = true; // the guest hasn't heard yet that the host got there first
            Assert.True(_hostItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            _hostLink.Release();

            var taken = Assert.Single(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
            Assert.Equal(_host.HostPlayerId, taken.PlayerId);
            Assert.NotEqual(ItemKind.None, _hostItems.Held);
            Assert.Equal(ItemKind.None, _guestItems.Held);

            // The host's answer about that box ends the guest's wait, so it can claim the next one.
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[1]));
        }

        [Fact]
        public void APlayerHoldingAnItemLeavesBoxesForOthers()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.False(_guestItems.ClaimTouchedBox(BoxPositions[1]));
            Assert.True(_guestItems.Boxes.IsUp(1, _now - GuestClockBehindMs));

            // Even if a claim gets through, the host refuses a player whose slot is full.
            _host.OnMessage(GuestConnection, new ItemBoxClaimMessage { PlayerId = GuestId, BoxIndex = 1 });
            Assert.Single(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
            Assert.True(_hostItems.Boxes.IsUp(1, _now));
        }

        [Fact]
        public void ALostClaimStopsBlockingAfterATimeout()
        {
            StartRacing();
            _guestLink.Drop = true;
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            _guestLink.Drop = false;
            Assert.False(_guestItems.ClaimTouchedBox(BoxPositions[1]));
            _now += GuestItems.ClaimTimeoutMs;
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[1]));
            Assert.NotEqual(ItemKind.None, _guestItems.Held);
        }

        [Fact]
        public void RollsUseTheLatestStandings()
        {
            StartRacing();
            _now += HostRace.StandingsIntervalMs;
            Tick();
            var order = _hostRace.Standings.Order;
            Assert.Equal(2, order.Count);
            int guestPosition = order.FindIndex(e => e.PlayerId == GuestId) + 1;

            // A number that gives last place a Missile and the leader something else.
            double r = 0.0;
            while (ItemRoll.Roll(2, 2, r) != ItemKind.Missile)
            {
                r += 0.01;
            }

            Assert.NotEqual(ItemKind.Missile, ItemRoll.Roll(1, 2, r));
            _nextRandom = r;
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.True(_hostItems.ClaimTouchedBox(BoxPositions[1]));
            Assert.Equal(ItemRoll.Roll(guestPosition, 2, r), _guestItems.Held);
            Assert.Equal(ItemRoll.Roll(3 - guestPosition, 2, r), _hostItems.Held);
            Assert.Contains(ItemKind.Missile, new[] { _guestItems.Held, _hostItems.Held });
        }

        [Fact]
        public void UsingAnItemTellsEveryoneAndEmptiesTheSlot()
        {
            StartRacing();
            _nextRandom = 0.0; // Boost
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.Equal(ItemKind.Boost, _guestItems.Held);

            Assert.Equal(ItemKind.Boost, _guestItems.UseHeld(new Vec3(1f, 0f, 2f), new Vec3(0f, 0f, 20f), 0.5f));
            Assert.Equal(ItemKind.None, _guestItems.Held);
            Assert.Equal(ItemKind.None, _hostItems.Slots.Held(GuestId));

            // The guest saw its own use at once and doesn't see the host's echo again.
            var own = Assert.Single(_guestSaw);
            Assert.Equal(GuestId, own.PlayerId);
            var relayed = Assert.Single(_hostSaw);
            Assert.Equal(ItemKind.Boost, relayed.Item);
            Assert.Equal(new Vec3(1f, 0f, 2f), relayed.Position);
            Assert.Equal(_now, relayed.UsedAtHostMs);
            Assert.Single(_hostLink.Sent.OfType<ItemUsedMessage>());

            Assert.Equal(ItemKind.None, _guestItems.UseHeld(Vec3.Zero, Vec3.Zero, 0f));
        }

        [Fact]
        public void TheHostsOwnUseReachesTheGuest()
        {
            StartRacing();
            Assert.True(_hostItems.ClaimTouchedBox(BoxPositions[0]));
            var item = _hostItems.Held;
            Assert.Equal(item, _hostItems.UseHeld(Vec3.Zero, Vec3.Zero, 0f));
            Assert.Equal(item, Assert.Single(_guestSaw).Item);
            Assert.Equal(item, Assert.Single(_hostSaw).Item);
            Assert.Equal(ItemKind.None, _hostItems.Held);
        }

        [Fact]
        public void TheHostRefusesAnItemThePlayerDoesNotHold()
        {
            StartRacing();
            _host.OnMessage(GuestConnection, new ItemUsedMessage { PlayerId = GuestId, Item = ItemKind.Missile });
            Assert.Empty(_hostSaw);
            Assert.Empty(_hostLink.Sent.OfType<ItemUsedMessage>());
        }

        [Fact]
        public void AGuestCannotSpeakForAnotherPlayer()
        {
            StartRacing();
            _host.OnMessage(GuestConnection, new ItemBoxClaimMessage { PlayerId = _host.HostPlayerId, BoxIndex = 0 });
            Assert.Empty(_hostLink.Sent.OfType<ItemBoxTakenMessage>());
        }

        [Fact]
        public void ANewRaceRestoresBoxesAndEmptiesSlots()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.True(_hostItems.ClaimTouchedBox(BoxPositions[1]));

            _hostRace.Tracker.FinishRemaining(_ => 0f, 0);
            Tick();
            Assert.Equal(RacePhase.Results, _hostRace.Phase);
            Assert.True(_hostRace.ReturnToLobby());
            StartRacing();

            Assert.Equal(ItemKind.None, _guestItems.Held);
            Assert.Equal(ItemKind.None, _hostItems.Held);
            Assert.True(_hostItems.Boxes.IsUp(0, _now - 1));
            Assert.True(_guestItems.Boxes.IsUp(1, _now - GuestClockBehindMs - 1));
        }

        [Fact]
        public void TheBotTakingOverAPlayerKeepsTheirItem()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            var held = _hostItems.Slots.Held(GuestId);
            _host.OnDisconnected(GuestConnection);
            Assert.Equal(held, _hostItems.Slots.Held(GuestId));
            Assert.Equal(held, _hostItems.UseHeldForBot(GuestId, Vec3.Zero, Vec3.Zero, 0f));
        }

        [Fact]
        public void AnItemTheBotUsedForAnAwayGuestIsGoneWhenTheyComeBack()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            var held = _guestItems.Held;
            _now += HostSession.AwayAfterMs;
            _host.Tick();
            Assert.True(_host.IsAway(GuestId));
            Assert.Equal(held, _hostItems.UseHeldForBot(GuestId, Vec3.Zero, Vec3.Zero, 0f));

            Assert.Equal(ItemKind.None, _guestItems.Held);
            Assert.Equal(ItemKind.None, _guestItems.UseHeld(Vec3.Zero, Vec3.Zero, 0f));
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[1]));
        }

        [Fact]
        public void AGuestThatMissesTheCountdownStillStartsTheNextRaceEmptyHanded()
        {
            StartRacing();
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
            Assert.NotEqual(ItemKind.None, _guestItems.Held);

            // The guest hears nothing from the end of this race until the next one is under way.
            _hostLink.HoldBack = true;
            _hostRace.Tracker.Retire(GuestId);
            _hostRace.Tracker.Retire(_host.HostPlayerId);
            Tick();
            Assert.True(_hostRace.ReturnToLobby());
            Assert.True(_hostRace.Start(3));
            Tick();
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            Assert.Equal(RacePhase.Racing, _guestRace.Phase);
            _hostLink.Release();
            Tick();

            Assert.Equal(RacePhase.Racing, _guestRace.Phase);
            Assert.Equal(ItemKind.None, _hostItems.Slots.Held(GuestId));
            Assert.Equal(ItemKind.None, _guestItems.Held);
            Assert.True(_guestItems.ClaimTouchedBox(BoxPositions[0]));
        }

        [Fact]
        public void APlayerWhoLeavesTheLobbyLosesTheirSlot()
        {
            _hostItems.Slots.Give(GuestId, ItemKind.Bomb);
            _host.OnDisconnected(GuestConnection);
            Assert.Equal(ItemKind.None, _hostItems.Slots.Held(GuestId));
        }

        /// <summary>Gives <paramref name="player"/> <paramref name="item"/> through a box, then uses it at <paramref name="position"/> facing +Z.</summary>
        private void GiveAndUse(byte player, ItemKind item, Vec3 position)
        {
            _hostItems.Slots.Give(player, item);
            if (player == _host.HostPlayerId)
            {
                Assert.Equal(item, _hostItems.UseHeld(position, Vec3.Zero, 0f));
            }
            else
            {
                _host.OnMessage(GuestConnection, new ItemUsedMessage { PlayerId = player, Item = item, Position = position });
            }
        }

        [Fact]
        public void ItemUsesGetAnEffectIdFromTheHost()
        {
            StartRacing();
            GiveAndUse(_host.HostPlayerId, ItemKind.Boost, Vec3.Zero);
            GiveAndUse(GuestId, ItemKind.Shield, Vec3.Zero);
            var ids = _hostLink.Sent.OfType<ItemUsedMessage>().Select(u => u.EffectId).ToList();
            Assert.Equal(2, ids.Count);
            Assert.NotEqual(ids[0], ids[1]);
            Assert.DoesNotContain((ushort)0, ids);
        }

        [Fact]
        public void AShockwaveHitsCarsInRangeAndPushesThemAway()
        {
            StartRacing();
            var user = new Vec3(0f, 0.6f, 0f);
            _cars[_host.HostPlayerId] = user;
            _cars[GuestId] = new Vec3(ItemEffects.ShockwaveRadius - 1f, 0.6f, 0f);
            GiveAndUse(_host.HostPlayerId, ItemKind.Shockwave, user);

            var hit = Assert.Single(_guestHits);
            Assert.True(hit.IsItemHit);
            Assert.Equal(_host.HostPlayerId, hit.AttackerId);
            Assert.True(hit.Impulse.X > 0f, "pushed away from the user");
            Assert.Equal(ItemEffects.HitPop, hit.Impulse.Y, 3);
            Assert.Empty(_hostHits); // never the user
        }

        [Fact]
        public void AShockwaveMissesCarsOutOfRange()
        {
            StartRacing();
            _cars[GuestId] = new Vec3(ItemEffects.ShockwaveRadius + 1f, 0.6f, 0f);
            GiveAndUse(_host.HostPlayerId, ItemKind.Shockwave, new Vec3(0f, 0.6f, 0f));
            Assert.Empty(_guestHits);
        }

        [Fact]
        public void AGuestsShockwaveHitsTheHost()
        {
            StartRacing();
            _cars[_host.HostPlayerId] = new Vec3(0f, 0.6f, -3f);
            GiveAndUse(GuestId, ItemKind.Shockwave, new Vec3(0f, 0.6f, 0f));
            var hit = Assert.Single(_hostHits);
            Assert.Equal(GuestId, hit.AttackerId);
            Assert.True(hit.Impulse.Z < 0f);
        }

        [Fact]
        public void AnOilSlickLandsBehindAndBothPhonesSeeIt()
        {
            StartRacing();
            GiveAndUse(GuestId, ItemKind.OilSlick, new Vec3(5f, 0.6f, 10f));

            var expected = new Vec3(5f, 0.6f, 10f - ItemEffects.OilDropBehind);
            var onHost = Assert.Single(_hostItems.Hazards.Active);
            var onGuest = Assert.Single(_guestItems.Hazards.Active);
            Assert.True(onHost.Position.ApproximatelyEquals(expected));
            Assert.True(onGuest.Position.ApproximatelyEquals(expected));
            Assert.Equal(onHost.EffectId, onGuest.EffectId);
            Assert.Equal(_now + ItemEffects.OilLifetimeMs, onHost.ExpiresAtMs);
            Assert.Equal(_guest.Clock.ToLocalTime(_now + ItemEffects.OilLifetimeMs), onGuest.ExpiresAtMs);
        }

        [Fact]
        public void AnOilSlickCatchesTheFirstCarOnceArmedAndIsUsedUp()
        {
            StartRacing();
            var drop = new Vec3(0f, 0.6f, 10f);
            GiveAndUse(GuestId, ItemKind.OilSlick, drop);
            var oil = ItemEffects.OilPosition(drop, 0f);

            // Not yet armed: the host drives straight over it.
            _cars[_host.HostPlayerId] = oil;
            Tick();
            Assert.Empty(_hostHits);

            _now += ItemEffects.OilArmMs;
            Tick();
            var hit = Assert.Single(_hostHits);
            Assert.True(hit.IsItemHit);
            Assert.Equal(GuestId, hit.AttackerId);
            Assert.Empty(_hostItems.Hazards.Active);
            Assert.Empty(_guestItems.Hazards.Active);
            Assert.Single(_hostLink.Sent.OfType<HazardGoneMessage>());

            // Used up: the guest drives over the same spot without sliding.
            _cars[GuestId] = oil;
            Tick();
            Assert.Empty(_guestHits);
        }

        [Fact]
        public void TheDropperCanSlideOnTheirOwnOil()
        {
            StartRacing();
            var drop = new Vec3(0f, 0.6f, 10f);
            GiveAndUse(GuestId, ItemKind.OilSlick, drop);
            _cars[GuestId] = ItemEffects.OilPosition(drop, 0f);
            _now += ItemEffects.OilArmMs;
            Tick();
            Assert.Single(_guestHits);
        }

        [Fact]
        public void OilSlicksTimeOutOnEveryPhone()
        {
            StartRacing();
            GiveAndUse(GuestId, ItemKind.OilSlick, new Vec3(0f, 0.6f, 10f));
            _now += ItemEffects.OilLifetimeMs;
            Tick();
            Assert.Empty(_hostItems.Hazards.Active);
            Assert.Empty(_guestItems.Hazards.Active);
            Assert.Empty(_hostLink.Sent.OfType<HazardGoneMessage>());
        }

        [Fact]
        public void ANewRaceClearsTheOil()
        {
            StartRacing();
            GiveAndUse(GuestId, ItemKind.OilSlick, new Vec3(0f, 0.6f, 10f));
            _hostRace.Tracker.FinishRemaining(_ => 0f, 0);
            Tick();
            Assert.True(_hostRace.ReturnToLobby());
            StartRacing();
            Assert.Empty(_hostItems.Hazards.Active);
            Assert.Empty(_guestItems.Hazards.Active);
        }

        private void Advance(long ms)
        {
            for (long t = 0; t < ms; t += 20)
            {
                _now += 20;
                Tick();
            }
        }

        /// <summary>Ticks until the standings list both racers; returns (leader, second).</summary>
        private (byte Leader, byte Second) Standings()
        {
            _now += HostRace.StandingsIntervalMs;
            Tick();
            var order = _hostRace.Standings.Order;
            return (order[0].PlayerId, order[1].PlayerId);
        }

        [Fact]
        public void AMissileChasesTheCarAheadAndHitsIt()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[second] = new Vec3(0f, 0.6f, 0f);
            _cars[leader] = new Vec3(10f, 0.6f, 30f);
            GiveAndUse(second, ItemKind.Missile, _cars[second]);
            Assert.Single(_hostItems.Projectiles);

            // The guest learns whom it chases, for the target's warning.
            Advance(100);
            Assert.Equal(leader, Assert.Single(_guestItems.Projectiles).TargetId);

            Advance(2000);
            var hits = leader == _host.HostPlayerId ? _hostHits : _guestHits;
            var hit = Assert.Single(hits);
            Assert.Equal(second, hit.AttackerId);
            Assert.True(hit.IsItemHit);
            Assert.Empty(_hostItems.Projectiles);
            Assert.Empty(_guestItems.Projectiles);
            Assert.Single(_hostLink.Sent.OfType<HazardGoneMessage>());
        }

        [Fact]
        public void AMissileHitsTheFirstCarInItsPathNotJustItsTarget()
        {
            StartRacing();
            var (leader, second) = Standings();
            var bystander = _host.Roster.AddBot().PlayerId;
            var botHits = new List<HitMessage>();
            _host.BotHitReceived += botHits.Add;
            _cars[second] = new Vec3(0f, 0.6f, 0f);
            _cars[bystander] = new Vec3(0f, 0.6f, 15f);
            _cars[leader] = new Vec3(0f, 0.6f, 60f);
            GiveAndUse(second, ItemKind.Missile, _cars[second]);

            Advance(2000);

            var hit = Assert.Single(botHits);
            Assert.Equal(bystander, hit.TargetId);
            Assert.Equal(second, hit.AttackerId);
            Assert.True(hit.IsItemHit);
            Assert.Empty(_hostHits);
            Assert.Empty(_guestHits);
            Assert.Empty(_hostItems.Projectiles);
        }

        [Fact]
        public void TheLeadersMissileFliesStraightAndRunsOut()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[leader] = new Vec3(0f, 0.6f, 0f);
            _cars[second] = new Vec3(30f, 0.6f, 0f); // off to the side, never in its path
            GiveAndUse(leader, ItemKind.Missile, _cars[leader]);
            Advance(ProjectileRules.MissileLifetimeMs + 100);
            Assert.Empty(_hostHits);
            Assert.Empty(_guestHits);
            Assert.Empty(_hostItems.Projectiles);
            Assert.Single(_hostLink.Sent.OfType<HazardGoneMessage>());
        }

        [Fact]
        public void LightningStrikesEveryCarAheadWhereverItIs()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[leader] = new Vec3(0f, 0.6f, 500f); // nowhere near
            _cars[second] = Vec3.Zero;
            GiveAndUse(second, ItemKind.Lightning, _cars[second]);

            var hit = Assert.Single(leader == _host.HostPlayerId ? _hostHits : _guestHits);
            Assert.True(hit.IsItemHit);
            Assert.Equal(second, hit.AttackerId);
            Assert.Equal(ItemEffects.LightningImpulse, hit.Impulse);
            Assert.Empty(second == _host.HostPlayerId ? _hostHits : _guestHits); // never the user
        }

        [Fact]
        public void TheLeadersLightningStrikesNobody()
        {
            StartRacing();
            var (leader, _) = Standings();
            GiveAndUse(leader, ItemKind.Lightning, Vec3.Zero);
            Assert.Empty(_hostHits);
            Assert.Empty(_guestHits);
        }

        [Fact]
        public void LightningAndSeekersLeaveCarsThatHaveFinishedAlone()
        {
            StartRacing();
            var (leader, second) = Standings();
            var tracker = _hostRace.Tracker;
            for (int i = 0; !tracker.Get(leader).IsFinished && i < 1000; i++)
            {
                tracker.ReportCheckpoint(leader, tracker.Get(leader).NextCheckpoint, 1000 + i);
            }

            _now += HostRace.StandingsIntervalMs;
            Tick();
            Assert.NotEqual(0, _hostRace.Standings.Order.Find(e => e.PlayerId == leader).FinishPosition);

            GiveAndUse(second, ItemKind.Lightning, Vec3.Zero);
            Assert.Empty(_hostHits);
            Assert.Empty(_guestHits);

            GiveAndUse(second, ItemKind.Seeker, Vec3.Zero);
            Advance(100);
            Assert.Equal(ProjectileRules.NoTarget, Assert.Single(_guestItems.Projectiles).TargetId);
        }

        [Fact]
        public void AFakeBoxSitsBehindOnBothPhonesAndCatchesTheFirstCarOnceArmed()
        {
            StartRacing();
            var drop = new Vec3(0f, 0.6f, 10f);
            GiveAndUse(GuestId, ItemKind.FakeBox, drop);
            var onHost = Assert.Single(_hostItems.Hazards.Active);
            var onGuest = Assert.Single(_guestItems.Hazards.Active);
            Assert.Equal(ItemKind.FakeBox, onHost.Item);
            Assert.Equal(ItemKind.FakeBox, onGuest.Item);

            _cars[_host.HostPlayerId] = onHost.Position;
            _now += ItemEffects.OilArmMs;
            Tick();
            var hit = Assert.Single(_hostHits);
            Assert.Equal(GuestId, hit.AttackerId);
            Assert.Equal(ItemEffects.FakeBoxImpulse, hit.Impulse);
            Assert.Empty(_hostItems.Hazards.Active);
            Assert.Empty(_guestItems.Hazards.Active);
        }

        [Fact]
        public void ASeekerPassesOtherCarsAndBlowsUpOnTheLeader()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[second] = new Vec3(0f, 0.6f, 0f);
            _cars[leader] = new Vec3(0f, 0.6f, 120f);
            GiveAndUse(second, ItemKind.Seeker, _cars[second]);
            Advance(100);
            Assert.Equal(leader, Assert.Single(_guestItems.Projectiles).TargetId);

            Advance(3000);
            var hit = Assert.Single(leader == _host.HostPlayerId ? _hostHits : _guestHits);
            Assert.Equal(second, hit.AttackerId);
            Assert.True(hit.IsItemHit);
            Assert.Empty(second == _host.HostPlayerId ? _hostHits : _guestHits);
            Assert.Empty(_hostItems.Projectiles);
            Assert.Empty(_guestItems.Projectiles);
        }

        [Fact]
        public void ASeekersBlastCatchesEveryCarNearTheLeaderButNotItsOwner()
        {
            StartRacing();
            var (leader, second) = Standings();
            var near = _host.Roster.AddBot().PlayerId;
            var far = _host.Roster.AddBot().PlayerId;
            var botHits = new List<HitMessage>();
            _host.BotHitReceived += botHits.Add;
            _cars[second] = new Vec3(0f, 0.6f, 0f);
            _cars[leader] = new Vec3(0f, 0.6f, 200f);
            _cars[near] = _cars[leader] + new Vec3(ProjectileRules.SeekerBlastRadius - 1f, 0f, 0f);
            _cars[far] = _cars[leader] + new Vec3(ProjectileRules.SeekerBlastRadius + 1f, 0f, 0f);
            GiveAndUse(second, ItemKind.Seeker, _cars[second]);

            // The owner drives up beside the leader before the Seeker lands: inside the blast, but never hit by its own Seeker.
            Advance(100);
            _cars[second] = _cars[leader] - new Vec3(0f, 0f, 2f);
            Advance(5000);

            Assert.Single(leader == _host.HostPlayerId ? _hostHits : _guestHits);
            Assert.Empty(second == _host.HostPlayerId ? _hostHits : _guestHits);
            var hit = Assert.Single(botHits);
            Assert.Equal(near, hit.TargetId);
            Assert.Equal(second, hit.AttackerId);
            Assert.True(hit.IsItemHit);
            Assert.Empty(_hostItems.Projectiles);
        }

        [Fact]
        public void TheLeadersSeekerGoesForSecondPlace()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[leader] = new Vec3(0f, 0.6f, 0f);
            _cars[second] = new Vec3(0f, 0.6f, -40f); // behind the leader
            GiveAndUse(leader, ItemKind.Seeker, _cars[leader]);
            Advance(100);
            Assert.Equal(second, Assert.Single(_guestItems.Projectiles).TargetId);
            Advance(4000);
            Assert.Single(second == _host.HostPlayerId ? _hostHits : _guestHits);
        }

        [Fact]
        public void TheGuestSeesWhereProjectilesAre()
        {
            StartRacing();
            var (leader, second) = Standings();
            _cars[leader] = new Vec3(0f, 0.6f, 0f);
            GiveAndUse(leader, ItemKind.Missile, _cars[leader]);
            Advance(200);
            var seen = Assert.Single(_guestItems.Projectiles);
            Assert.Equal(ItemKind.Missile, seen.Kind);
            Assert.True(seen.Position.Z > ProjectileRules.MissileLaunchAhead);
            Assert.True(_hostLink.Sent.OfType<ProjectilesMessage>().Count() >= 3);
        }

        [Fact]
        public void APositionUpdateArrivingAfterTheProjectileIsGoneIsIgnored()
        {
            StartRacing();
            _guest.OnMessage(new HazardGoneMessage { EffectId = 77 });
            var late = new ProjectilesMessage();
            late.Projectiles.Add(new ProjectileSnapshot { EffectId = 77, Kind = ItemKind.Missile });
            _guest.OnMessage(late);
            Assert.Empty(_guestItems.Projectiles);
        }

        [Fact]
        public void ABombGoesOffOnContactAndHitsEveryoneNearIncludingTheThrower()
        {
            StartRacing();
            var thrower = _host.HostPlayerId;
            _cars[thrower] = new Vec3(0f, 0.6f, 0f);
            _cars[GuestId] = new Vec3(50f, 0.6f, 0f); // well clear of where the bomb lands
            var guestExplosions = new List<ItemKind>();
            _guestItems.ProjectileEnded += (kind, position) => guestExplosions.Add(kind);
            GiveAndUse(thrower, ItemKind.Bomb, _cars[thrower]);

            // The thrower catches up with the bomb after its safe moment: it blows up on them, and the guest is close enough too.
            Advance(ProjectileRules.BombSafeMs);
            Assert.Empty(_hostHits);
            _cars[thrower] = _hostItems.Projectiles[0].Position;
            _cars[GuestId] = _cars[thrower] + new Vec3(ProjectileRules.BombBlastRadius - 1f, 0f, 0f);
            Advance(20);

            Assert.Single(_hostHits);
            Assert.Single(_guestHits);
            Assert.Empty(_hostItems.Projectiles);
            Assert.Equal(ItemKind.Bomb, Assert.Single(guestExplosions));
        }

        [Theory]
        [InlineData(ItemKind.Bomb, 1f)]
        [InlineData(ItemKind.Missile, 3f)]
        public void TheGuestSeesAProjectileThatEndsBeforeAnyPositionUpdate(ItemKind item, float guestAhead)
        {
            StartRacing();
            var thrower = _host.HostPlayerId;
            _cars[thrower] = new Vec3(0f, 0.6f, 0f);
            _cars[GuestId] = new Vec3(0f, 0.6f, guestAhead); // close enough that the projectile meets them on its first step
            var hostEnds = new List<(ItemKind, Vec3)>();
            var guestEnds = new List<(ItemKind, Vec3)>();
            _hostItems.ProjectileEnded += (kind, position) => hostEnds.Add((kind, position));
            _guestItems.ProjectileEnded += (kind, position) => guestEnds.Add((kind, position));
            GiveAndUse(thrower, item, _cars[thrower]);
            Advance(20);

            Assert.Empty(_hostLink.Sent.OfType<ProjectilesMessage>());
            Assert.Single(_guestHits);
            Assert.Equal(item, Assert.Single(guestEnds).Item1);
            Assert.Equal(hostEnds, guestEnds);
            Assert.Empty(_guestItems.Projectiles);
        }

        [Fact]
        public void ABombGoesOffWhenItsFuseRunsOut()
        {
            StartRacing();
            _cars[GuestId] = new Vec3(0f, 0.6f, 0f);
            _cars[_host.HostPlayerId] = new Vec3(100f, 0.6f, 0f);
            var explosions = new List<Vec3>();
            _hostItems.ProjectileEnded += (kind, position) => explosions.Add(position);
            GiveAndUse(GuestId, ItemKind.Bomb, _cars[GuestId]);
            Advance(ProjectileRules.BombFuseMs - 40);
            Assert.Empty(explosions);
            Advance(60);
            Assert.Single(explosions);
            Assert.Empty(_hostHits); // too far away
        }

        [Fact]
        public void ANewRaceClearsProjectiles()
        {
            StartRacing();
            _cars[GuestId] = Vec3.Zero;
            GiveAndUse(GuestId, ItemKind.Bomb, Vec3.Zero);
            Advance(100);
            _hostRace.Tracker.FinishRemaining(_ => 0f, 0);
            Tick();
            Assert.True(_hostRace.ReturnToLobby());
            StartRacing();
            Assert.Empty(_hostItems.Projectiles);
            Assert.Empty(_guestItems.Projectiles);
        }
    }
}
