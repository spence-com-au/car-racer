using CarRacer.Core.Arena;

namespace CarRacer.Core.Audio
{
    /// <summary>
    /// Which tune plays (design doc §13): the menu tune in the menus and a session's lobby, each
    /// track's own tune while racing on it (and practising), and one tune for every arena.
    /// </summary>
    public static class MusicChoice
    {
        public const string Menu = "menu";
        public const string Arena = "arena";

        /// <summary>The track tunes, in <see cref="Track.TrackCatalog"/> order; any further track reuses them in turn.</summary>
        public static readonly string[] TrackTunes = { "oval", "canyon", "figure_eight", "sweepers", "switchbacks", "dunes", "carousel", "ridge" };

        /// <summary>Every tune in <c>Resources/Music</c>, so a test can check they all load.</summary>
        public static readonly string[] All = { Menu, Arena, "oval", "canyon", "figure_eight", "sweepers", "switchbacks", "dunes", "carousel", "ridge" };

        /// <param name="inEvent">An event is under way (or the player is practising), rather than waiting in a lobby.</param>
        public static string For(bool inEvent, GameMode mode, int trackIndex)
        {
            if (!inEvent)
            {
                return Menu;
            }

            if (mode != GameMode.Race)
            {
                return Arena;
            }

            int count = TrackTunes.Length;
            return TrackTunes[((trackIndex % count) + count) % count];
        }
    }
}
