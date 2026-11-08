using CarRacer.Core.Arena;

namespace CarRacer.Core.Race
{
    /// <summary>
    /// The host's rules for an event (design doc §11, host controls): whether cars take damage,
    /// whether item boxes give items, and whether each body keeps its handling ratings or every car
    /// drives the same. They go out in each countdown, so every phone plays the event by the same rules.
    /// </summary>
    public readonly struct EventRules
    {
        private const byte NoDamage = 1 << 0;
        private const byte NoItems = 1 << 1;
        private const byte NoCarStats = 1 << 2;

        public EventRules(bool damage, bool items, bool carStats)
        {
            Damage = damage;
            Items = items;
            CarStats = carStats;
        }

        /// <summary>Everything on, as the game plays without host controls.</summary>
        public static EventRules Default => new EventRules(true, true, true);

        /// <summary>Crashes and item hits damage cars (races only; the arenas have their own rules either way).</summary>
        public bool Damage { get; }

        /// <summary>Item boxes give items. Battle is all about items, so it always has them.</summary>
        public bool Items { get; }

        /// <summary>Each body keeps its top speed, steering and acceleration ratings; off, every car drives the same.</summary>
        public bool CarStats { get; }

        /// <summary>
        /// Whether cars take damage in <paramref name="phase"/> of a <paramref name="mode"/> event (design doc §4,
        /// §11): only while racing in a race, and only if the host left damage on. The arenas have their own rules,
        /// and the lobby is for messing about.
        /// </summary>
        public bool DamageIn(GameMode mode, RacePhase phase) => phase == RacePhase.Racing && mode == GameMode.Race && Damage;

        /// <summary>Whether each body keeps its handling ratings in <paramref name="phase"/>: always in the lobby, as the host set otherwise.</summary>
        public bool CarStatsIn(RacePhase phase) => phase == RacePhase.Lobby || CarStats;

        /// <summary>The rules as they apply in <paramref name="mode"/>: Battle always has items.</summary>
        public EventRules For(GameMode mode) => mode == GameMode.Battle ? new EventRules(Damage, true, CarStats) : this;

        public EventRules WithDamage(bool on) => new EventRules(on, Items, CarStats);

        public EventRules WithItems(bool on) => new EventRules(Damage, on, CarStats);

        public EventRules WithCarStats(bool on) => new EventRules(Damage, Items, on);

        /// <summary>A byte for the wire; zero is everything on, so a zero byte means the default rules.</summary>
        public byte ToByte() => (byte)((Damage ? 0 : NoDamage) | (Items ? 0 : NoItems) | (CarStats ? 0 : NoCarStats));

        public static EventRules FromByte(byte value) =>
            new EventRules((value & NoDamage) == 0, (value & NoItems) == 0, (value & NoCarStats) == 0);
    }
}
