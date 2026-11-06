using CarRacer.Core.Session;
using CarRacer.Core.Viewer;

namespace CarRacer.Core.Art
{
    /// <summary>
    /// Car paint for each roster colour index (<see cref="LobbyRoster.ColourCount"/> of them),
    /// chosen to stay distinct for colour-blind players (Okabe–Ito based, design doc §11). The game
    /// paints cars with it and the TV viewer draws their dots with it (§2.4).
    /// </summary>
    public static class CarPalette
    {
        private static readonly Rgb[] Colours =
        {
            Rgb.FromFloats(0.95f, 0.75f, 0.10f), // yellow
            Rgb.FromFloats(0.00f, 0.45f, 0.70f), // blue
            Rgb.FromFloats(0.84f, 0.37f, 0.00f), // vermilion
            Rgb.FromFloats(0.00f, 0.62f, 0.45f), // bluish green
            Rgb.FromFloats(0.80f, 0.47f, 0.65f), // reddish purple
            Rgb.FromFloats(0.34f, 0.71f, 0.91f), // sky blue
            Rgb.FromFloats(0.95f, 0.95f, 0.95f), // white
            Rgb.FromFloats(0.20f, 0.20f, 0.22f), // black
        };

        public static int Count => Colours.Length;

        public static Rgb ForIndex(int colourIndex) => Colours[((colourIndex % Colours.Length) + Colours.Length) % Colours.Length];
    }
}
