using CarRacer.Core.Art;
using UnityEngine;

namespace CarRacer.Game.Driving
{
    /// <summary>Car paint for each roster colour index, as Unity colours (<see cref="CarPalette"/>).</summary>
    public static class CarColours
    {
        public static Color ForIndex(int colourIndex)
        {
            var c = CarPalette.ForIndex(colourIndex);
            return new Color32(c.R, c.G, c.B, 255);
        }
    }
}
