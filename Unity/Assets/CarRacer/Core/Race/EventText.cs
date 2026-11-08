using System;
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
                    ? $"Race, {settings.Laps} {(settings.Laps == 1 ? "lap" : "laps")}, {EventName(GameMode.Race, settings.Track)}"
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

        /// <summary>How a player reads in the phones' lobby: "host", "bot", "runs the room", "ready" or "not ready".</summary>
        public static string LobbyState(RosterEntry player, byte leaderId)
        {
            if (player.IsHost)
            {
                return "host";
            }

            if (player.IsBot)
            {
                return "bot";
            }

            return player.PlayerId == leaderId ? "runs the room" : player.Ready ? "ready" : "not ready";
        }

        /// <summary>
        /// The three lines in the corner of the phone's HUD for <paramref name="playerId"/>'s car (design doc §7.0), each
        /// empty while there is nothing to show. In a race, its place (or "Finished 2nd" once home), its lap and the
        /// rules note. In Monster Jam, its place, its points and the time left. In Sumo and Battle, its lives ("Still
        /// on" in Sumo) or the place it went out in, how many cars are left and, in Battle, the time left.
        /// <paramref name="lapsCompleted"/> is this car's own count, which is known before the host's standings catch up;
        /// <paramref name="sinceStartMs"/> is the time since the start, negative before it.
        /// </summary>
        public static (string Corner1, string Corner2, string Corner3) PhoneStatus(IRaceView race, byte playerId, int lapsCompleted, long sinceStartMs)
        {
            var standings = race.Standings;
            int count = standings?.Order.Count ?? 0;
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                if (standings.Order[i].PlayerId == playerId)
                {
                    index = i;
                }
            }

            string place = index < 0 ? string.Empty : $"{RaceFormat.Ordinal(index + 1)} / {count}";
            string timeLeft = race.Phase == RacePhase.Racing && EventClock.TimeLimitMs(race.Mode) > 0
                ? RaceFormat.Time(EventClock.TimeLeftMs(race.Mode, sinceStartMs))
                : string.Empty;
            switch (race.Mode)
            {
                case GameMode.Race:
                    bool finished = index >= 0 && standings.Order[index].FinishPosition > 0;
                    int lap = finished ? race.LapCount : Math.Min(lapsCompleted + 1, race.LapCount);
                    string position = finished ? $"Finished {RaceFormat.Ordinal(standings.Order[index].FinishPosition)}" : place;
                    return (position, $"Lap {lap} / {race.LapCount}", RulesNote(race.Rules));
                case GameMode.Monster:
                    return (place, index < 0 ? string.Empty : $"{standings.Order[index].Points} pts", timeLeft);
                default:
                    int left = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (!standings.Order[i].IsOut(race.Mode))
                        {
                            left++;
                        }
                    }

                    string status = string.Empty;
                    if (index >= 0)
                    {
                        var entry = standings.Order[index];
                        status = entry.IsOut(race.Mode)
                            ? $"Out: {RaceFormat.Ordinal(entry.FinishPosition)}"
                            : race.Mode == GameMode.Battle ? $"Lives {entry.Lives}" : "Still on";
                    }

                    return (status, count > 0 ? $"{left} / {count} left" : string.Empty, timeLeft);
            }
        }

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
