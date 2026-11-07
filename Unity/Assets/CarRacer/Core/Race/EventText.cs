using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Scoring;
using CarRacer.Core.Session;
using CarRacer.Core.Track;

namespace CarRacer.Core.Race
{
    /// <summary>How events and their rules read on screen, the same on the phones' lobby and the TV viewer.</summary>
    public static class EventText
    {
        /// <summary>"Race", "Sumo", "Battle" or "Monster Jam".</summary>
        public static string ModeName(GameMode mode) => mode switch
        {
            GameMode.Sumo => "Sumo",
            GameMode.Battle => "Battle",
            GameMode.Monster => "Monster Jam",
            _ => "Race",
        };

        /// <summary>The track's name for a race ("Canyon Hairpins"), or the arena's.</summary>
        public static string EventName(GameMode mode, int trackIndex) =>
            mode == GameMode.Race ? TrackCatalog.Layouts[TrackCatalog.Clamp(trackIndex)].Definition.DisplayName : ModeName(mode);

        /// <summary>What the next event will be: "Race, 3 laps, Canyon Hairpins" or the cup, and any rules turned off.</summary>
        public static string SettingsSummary(LobbySettings settings)
        {
            settings = settings.Clean();
            string what = settings.CupChosen
                ? Cups.All[settings.Cup - 1].Name
                : settings.Mode == GameMode.Race
                    ? $"Race, {settings.Laps} laps, {EventName(GameMode.Race, settings.Track)}"
                    : ModeName(settings.Mode);
            string rules = RulesNote(settings.Rules);
            return rules.Length == 0 ? what : what + ". " + rules;
        }

        /// <summary>
        /// The big countdown, the same on the phones and the TV: "3", "2", "1" in the last
        /// <see cref="HostRace.CountdownMs"/> before the start, then "GO!" for a second; otherwise
        /// null. <paramref name="msToGo"/> is the time until the start (negative once it has passed).
        /// </summary>
        public static string Countdown(RacePhase phase, long msToGo)
        {
            if (msToGo > 0)
            {
                return phase == RacePhase.Countdown && msToGo <= HostRace.CountdownMs
                    ? ((msToGo + 999) / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            }

            return (phase == RacePhase.Countdown || phase == RacePhase.Racing) && msToGo > -1000 ? "GO!" : null;
        }

        /// <summary>A result's time on the results screen: the race time ("1:23.45"), "DNF", or Monster Jam's points ("37 pts").</summary>
        public static string ResultDetail(GameMode mode, RaceResultEntry result) =>
            mode == GameMode.Monster ? result.Points + " pts" : result.DidNotFinish ? "DNF" : RaceFormat.Time(result.RaceTimeMs);

        /// <summary>Only the rules that are off: "Rules: no damage, equal cars", or nothing.</summary>
        public static string RulesNote(EventRules rules)
        {
            var off = new List<string>();
            if (!rules.Damage)
            {
                off.Add("no damage");
            }

            if (!rules.Items)
            {
                off.Add("no items");
            }

            if (!rules.CarStats)
            {
                off.Add("equal cars");
            }

            return off.Count == 0 ? string.Empty : "Rules: " + string.Join(", ", off);
        }
    }
}
