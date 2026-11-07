using System;
using CarRacer.Core.Arena;
using CarRacer.Core.Bots;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Core.Track;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// What the next event will be (design doc §11): the mode, laps and track of a single event or
    /// which cup, the rules, and the bots. The host keeps them and sends them to every phone, so the
    /// room leader can change them from a guest phone when the host is a server (design doc §2.3).
    /// </summary>
    public struct LobbySettings : IEquatable<LobbySettings>
    {
        public const int MaxLaps = 9;

        /// <summary>The last mode the lobby offers, shared by <see cref="NextMode"/> and <see cref="Clean"/> so they agree.</summary>
        public const GameMode LastMode = GameMode.Monster;

        /// <summary>The last bot difficulty the lobby offers, shared by <see cref="NextDifficulty"/> and <see cref="Clean"/>.</summary>
        public const BotDifficulty HardestBots = BotDifficulty.Hard;

        /// <summary>The bot fill choices the lobby steps through (design doc §9): off, or fill the grid to 4 or 6 cars.</summary>
        public static readonly int[] BotFills = { 0, 4, 6 };

        public GameMode Mode;
        public int Laps;
        public int Track;
        public EventRules Rules;
        public int BotFill;
        public BotDifficulty BotDifficulty;

        /// <summary>0 is a single event; otherwise <see cref="Cups.All"/>[Cup - 1].</summary>
        public int Cup;

        public bool ReverseGrid;

        public static LobbySettings Default => new LobbySettings
        {
            Mode = GameMode.Race,
            Laps = 3,
            Rules = EventRules.Default,
            BotDifficulty = BotDifficulty.Normal,
            ReverseGrid = true,
        };

        /// <summary>True when the lobby is set up for a cup rather than a single event.</summary>
        public bool CupChosen => Cup > 0;

        /// <summary>The mode after <paramref name="mode"/> in the lobby's mode button, wrapping back to a race.</summary>
        public static GameMode NextMode(GameMode mode) => mode >= LastMode ? GameMode.Race : mode + 1;

        /// <summary>The difficulty after <paramref name="difficulty"/> in the lobby's bot button, wrapping back to easy.</summary>
        public static BotDifficulty NextDifficulty(BotDifficulty difficulty) => difficulty >= HardestBots ? BotDifficulty.Easy : difficulty + 1;

        /// <summary>These settings with anything out of range brought back in, so a bad value from a phone can't break the host.</summary>
        public LobbySettings Clean()
        {
            var clean = this;
            clean.Mode = Mode <= LastMode ? Mode : GameMode.Race;
            clean.Laps = Math.Max(1, Math.Min(MaxLaps, Laps));
            clean.Track = TrackCatalog.Clamp(Track);
            clean.BotFill = Math.Max(0, Math.Min(LobbyRoster.MaxPlayers, BotFill));
            clean.BotDifficulty = BotDifficulty <= HardestBots ? BotDifficulty : BotDifficulty.Normal;
            clean.Cup = Cup >= 0 && Cup <= Cups.All.Count ? Cup : 0;
            return clean;
        }

        public void Write(NetWriter w)
        {
            w.WriteByte((byte)Mode);
            w.WriteByte((byte)Laps);
            w.WriteByte((byte)Track);
            w.WriteByte(Rules.ToByte());
            w.WriteByte((byte)BotFill);
            w.WriteByte((byte)BotDifficulty);
            w.WriteByte((byte)Cup);
            w.WriteBool(ReverseGrid);
        }

        public static LobbySettings Read(NetReader r) => new LobbySettings
        {
            Mode = (GameMode)r.ReadByte(),
            Laps = r.ReadByte(),
            Track = r.ReadByte(),
            Rules = EventRules.FromByte(r.ReadByte()),
            BotFill = r.ReadByte(),
            BotDifficulty = (BotDifficulty)r.ReadByte(),
            Cup = r.ReadByte(),
            ReverseGrid = r.ReadBool(),
        };

        public bool Equals(LobbySettings other) =>
            Mode == other.Mode && Laps == other.Laps && Track == other.Track && Rules.ToByte() == other.Rules.ToByte()
            && BotFill == other.BotFill && BotDifficulty == other.BotDifficulty && Cup == other.Cup && ReverseGrid == other.ReverseGrid;

        public override bool Equals(object obj) => obj is LobbySettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Mode, Laps, Track, Rules.ToByte(), BotFill, (int)BotDifficulty, Cup, ReverseGrid);
    }
}
