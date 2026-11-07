using CarRacer.Core.Arena;
using CarRacer.Core.Audio;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Game.Screens;
using UnityEngine;

namespace CarRacer.Game.Audio
{
    /// <summary>
    /// Picks the tune for the driving screen (<see cref="MusicChoice"/>): the menu tune in a lobby,
    /// the track's or the arena's in an event or practice. While the room is on a TV that plays the
    /// music (design doc §2.4), the phone's own fades out, so the two don't play out of step.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        private TrackScreen _screen;

        public void Initialise(TrackScreen screen) => _screen = screen;

        private void Update()
        {
            if (_screen == null)
            {
                return;
            }

            // Practice has no session: it is always under way, on the track.
            var race = _screen.Session != null ? _screen.Session.Race : null;
            bool inEvent = _screen.Session == null || (race != null && race.Phase != RacePhase.Lobby);
            var mode = race != null ? race.Mode : GameMode.Race;
            var tv = _screen.Session != null ? _screen.Session.TvStatus : null;
            bool tvPlaysMusic = tv != null && tv.Music && tv.State == TvState.Playing;
            MusicPlayer.Play(tvPlaysMusic ? null : MusicChoice.For(inEvent, mode, _screen.TrackIndex));
        }
    }
}
