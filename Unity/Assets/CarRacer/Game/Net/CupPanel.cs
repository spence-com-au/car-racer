using System;
using System.Text;
using CarRacer.Core.Audio;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The cup parts of the session screens (design doc §10). For the room leader: the lobby's
    /// choice of a single event or a cup, reverse grid, and resuming the cup saved on the host. On
    /// every phone: the standings table beside the results, with the countdown to the next event,
    /// and the leader's pause, skip and leave. The cup itself runs on the host (<see cref="HostLobby"/>);
    /// these buttons only send it commands. It builds UI Toolkit elements for
    /// <see cref="SessionOverlay"/>'s lobby and results views.
    /// </summary>
    public sealed class CupPanel
    {
        private readonly NetworkSession _session;
        private readonly HostLobby _host;

        /// <param name="host">The host's lobby, on the host; null on a guest.</param>
        public CupPanel(NetworkSession session, HostLobby host)
        {
            _session = session;
            _host = host;
        }

        /// <summary>What the lobby's cup parts show, so the lobby rebuilds only when it changes.</summary>
        public string Signature => $"{_session.Settings.Cup}/{_session.Settings.ReverseGrid}/{_session.SavedCup}/{_session.SavedCupDone}";

        /// <summary>The leader's lobby choices: single event or a cup, and for a cup, reverse grid.</summary>
        public void AddLobbyChoice(VisualElement panel)
        {
            var settings = _session.Settings;
            string label = settings.CupChosen ? "Event: " + Cups.All[settings.Cup - 1].Name : "Event: single";
            panel.Add(UiKit.Button(label, () =>
            {
                var next = _session.Settings;
                next.Cup = (next.Cup + 1) % (Cups.All.Count + 1);
                _session.ChangeSettings(next);
            }));

            if (!settings.CupChosen)
            {
                return;
            }

            var cup = Cups.All[settings.Cup - 1];
            panel.Add(UiKit.Hint($"{cup.Events.Count} events; the next starts {CupRun.AdvanceMs / 1000} s after each finishes."));
            panel.Add(UiKit.Button(settings.ReverseGrid ? "Reverse grid: on" : "Reverse grid: off", () =>
            {
                var next = _session.Settings;
                next.ReverseGrid = !next.ReverseGrid;
                _session.ChangeSettings(next);
            }));
        }

        /// <summary>The leader's start buttons for a cup: start a new one, or resume the one saved on the host.</summary>
        public void AddStartButtons(VisualElement panel, bool canStart)
        {
            int saved = _session.SavedCup;
            if (saved > 0 && saved <= Cups.All.Count)
            {
                var savedCup = Cups.All[saved - 1];
                panel.Add(UiKit.Enabled(UiKit.Button($"Resume {savedCup.Name} ({_session.SavedCupDone} of {savedCup.Events.Count} done)", () =>
                {
                    if (canStart)
                    {
                        _session.Command(LobbyCommand.ResumeSavedCup);
                    }
                }), canStart));
            }

            var cup = Cups.All[_session.Settings.Cup - 1];
            panel.Add(UiKit.Enabled(UiKit.Button(canStart ? "Start " + cup.Name : "Waiting for everyone to be ready", () =>
            {
                if (canStart)
                {
                    _session.Command(LobbyCommand.Start);
                }
            }, primary: true), canStart));
        }

        /// <summary>The cup table for the results screen, or null outside a cup.</summary>
        public CupStandingsMessage CurrentStandings(IRaceView race)
        {
            if (race.Phase != RacePhase.Results)
            {
                return null;
            }

            return _host != null ? _host.Run?.Standings : (race as GuestRace)?.CupStandings;
        }

        /// <summary>What the standings show (not the countdown, which <see cref="UpdateCountdown"/> keeps current), so the results rebuild only when it changes.</summary>
        public string StandingsSignature(CupStandingsMessage table)
        {
            var text = new StringBuilder();
            text.Append(table.CupName).Append(table.EventsCompleted).Append('/').Append(table.TotalEvents).Append(table.IsComplete);
            text.Append(table.IsPaused).Append(_session.LeadsRoom);
            foreach (var entry in table.Standings)
            {
                text.Append('|').Append(entry.Name).Append(entry.Points);
            }

            return text.ToString();
        }

        /// <summary>
        /// The cup table with the countdown to the next event, and for the room leader the pause,
        /// skip and leave buttons, or Back to lobby once the cup is won. <paramref name="countdown"/>
        /// is the label to keep current with <see cref="UpdateCountdown"/>.
        /// </summary>
        public VisualElement BuildStandings(CupStandingsMessage table, string me, out Label countdown)
        {
            var panel = UiKit.Panel(460f);
            string heading = table.IsComplete ? $"{table.CupName}: final" : $"{table.CupName}: {table.EventsCompleted} of {table.TotalEvents}";
            panel.Add(UiKit.Heading(heading));
            for (int i = 0; i < table.Standings.Count; i++)
            {
                var entry = table.Standings[i];
                string you = string.Equals(entry.Name, me, StringComparison.OrdinalIgnoreCase) ? " (you)" : string.Empty;
                var row = UiKit.Row();
                var name = UiKit.Text($"{RaceFormat.Ordinal(i + 1)}  {entry.Name}{you}", 20, Color.white);
                name.style.flexGrow = 1f;
                row.Add(name);
                row.Add(UiKit.Text($"{entry.Points} pts", 20, Color.white));
                panel.Add(row);
            }

            panel.Add(UiKit.Space(12f));
            countdown = null;
            if (table.IsComplete)
            {
                panel.Add(UiKit.Text(table.Standings.Count > 0 ? $"{table.Standings[0].Name} wins the {table.CupName}!" : string.Empty, 20, UiKit.Accent, bold: true));
                if (_session.LeadsRoom)
                {
                    panel.Add(UiKit.Button("Back to lobby", () => _session.Command(LobbyCommand.BackToLobby), primary: true, sound: UiSound.Back));
                }

                return panel;
            }

            countdown = UiKit.Text(string.Empty, 20, Color.white);
            panel.Add(countdown);
            UpdateCountdown(table, countdown);
            if (_session.LeadsRoom)
            {
                bool paused = table.IsPaused;
                panel.Add(UiKit.EvenRow(
                    UiKit.Button(paused ? "Resume" : "Pause", () => _session.Command(paused ? LobbyCommand.UnpauseCup : LobbyCommand.PauseCup)),
                    UiKit.Button("Next event", () => _session.Command(LobbyCommand.NextEvent)),
                    UiKit.Button("Leave cup", () => _session.Command(LobbyCommand.LeaveCup), sound: UiSound.Back)));
            }

            return panel;
        }

        /// <summary>Sets the countdown line to the time left before the next event.</summary>
        public void UpdateCountdown(CupStandingsMessage table, Label countdown)
        {
            long nextAt = table.NextEventAtHostMs;
            if (nextAt > 0 && !_session.IsHost)
            {
                nextAt = _session.GuestSide.Clock.ToLocalTime(nextAt);
            }

            long msToGo = nextAt - NetworkSession.NowMs;
            string text = nextAt > 0 ? $"Next event in {Math.Max(0L, (msToGo + 999) / 1000)} s" : "Next event: paused";
            if (countdown.text != text)
            {
                countdown.text = text;
            }
        }
    }
}
