using System.Text.RegularExpressions;

namespace CarRacer.Core.Audio
{
    /// <summary>A clip in <c>Resources/Audio</c>, loaded from the file named for it in snake case: <c>BombThrow</c> from <c>bomb_throw.ogg</c>.</summary>
    public enum Sfx
    {
        CrunchLight,
        CrunchMedium,
        CrunchHeavy,
        Boost,
        ItemBox,
        Missile,
        BombThrow,
        Shockwave,
        Shield,
        Oil,
        Explosion,
    }

    public static class SfxFiles
    {
        /// <summary>The name of the file in <c>Resources/Audio</c>, without its extension, that <paramref name="sfx"/> is loaded from.</summary>
        public static string FileName(this Sfx sfx) => Regex.Replace(sfx.ToString(), "(?<=.)(?=[A-Z])", "_").ToLowerInvariant();
    }
}
