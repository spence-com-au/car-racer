using System.Text;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Audio;
using CarRacer.Core.Bots;
using CarRacer.Core.Discovery;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using CarRacer.Game.Driving;
using CarRacer.Game.Menu;
using CarRacer.Game.Platform;
using CarRacer.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The session screens drawn over the track (design doc §6, §11), on a UI Toolkit layer: the
    /// lobby (players, Ready, votes and the car picker, and for the room leader mode, track, laps,
    /// bots, rules and Start, which go to the host as commands, design doc §2.3), the
    /// countdown, the race HUD (position and lap, or lives and who is left in an arena, and the
    /// item slot) and the results with a rematch or the cup table. The lobby and results are
    /// rebuilt only when what they show changes; the race HUD's text is updated every frame.
    /// </summary>
    public sealed class SessionOverlay : MonoBehaviour
    {
        /// <summary>How long a Monster Jam score ("Big air! +12") stays up.</summary>
        public const float StuntPopupSeconds = 1.5f;

        /// <summary>How much lower the race status sits in the top-left corner, clear of the status line.</summary>
        public const float LeftCornerDrop = 16f;

        /// <summary>A session that has ended (host gone, connection lost) shows why, then goes back to the menu after this long.</summary>
        public const float EndedReturnSeconds = 4f;

        /// <summary>How often the host's own addresses are re-read, in case Wi-Fi or the hotspot comes up (or changes) after the lobby opens.</summary>
        private const float AddressRefreshSeconds = 3f;

        private static string[] _trackNames;

        private readonly StringBuilder _signature = new StringBuilder();
        private NetworkSession _session;
        private NetworkedCars _cars;
        private CupPanel _cup;
        private string _hostAddresses = string.Empty;
        private string _joinLink;
        private Texture2D _joinQr;
        private float _nextAddressRefresh;

        private UiRoot _layer;
        private Label _status;
        private Label _banner;
        private Label _stunt;
        private string _stuntText;
        private float _stuntUntil;
        private Label _countdown;
        private VisualElement _view;
        private ScrollView _lobbyScroll;
        private VisualElement _raceHud;
        private Label _corner1;
        private Label _corner2;
        private Label _corner3;
        private Label _itemSlot;
        private Label _cupCountdown;
        private string _shownView;
        private bool _hudLeftHanded;

        /// <param name="host">The host's lobby, on the host; null on a guest.</param>
        public void Initialise(NetworkSession session, NetworkedCars cars, HostLobby host)
        {
            _session = session;
            _cars = cars;
            _cup = new CupPanel(session, host);
            cars.StuntScored += text =>
            {
                _stuntText = text;
                _stuntUntil = Time.unscaledTime + StuntPopupSeconds;
            };

            _layer = UiRoot.Create(transform, "Session", order: 10);
            _layer.Restyle += Build;
            Build();
        }

        /// <summary>Makes the layer's lasting elements; the lobby and results views are filled in as they change.</summary>
        private void Build()
        {
            _layer.Safe.Clear();
            _layer.Full.Clear();
            _layer.Full.Add(_layer.Safe);

            _status = UiKit.Text(string.Empty, 20, Color.white);
            _status.style.position = Position.Absolute;
            _status.style.left = 24f;
            _status.style.top = 64f;
            _layer.Safe.Add(_status);

            _view = UiKit.Fill(new VisualElement { name = "view" });
            _layer.Safe.Add(_view);
            _shownView = null;

            BuildRaceHud();

            _countdown = Big(96, 200f, 140f);
            _layer.Full.Add(_countdown);
            _banner = Big(56, 260f, 80f);
            _layer.Full.Add(_banner);
            _stunt = Big(44, 360f, 60f);
            _stunt.style.color = UiKit.Accent;
            _layer.Full.Add(_stunt);
        }

        /// <summary>A large centred line across the screen, <paramref name="top"/> pixels down; hidden until given text.</summary>
        private static Label Big(int size, float top, float height)
        {
            var label = UiKit.Centred(UiKit.Text(string.Empty, size, Color.white, bold: true));
            label.style.position = Position.Absolute;
            label.style.left = label.style.right = 0f;
            label.style.top = top;
            label.style.height = height;
            label.style.display = DisplayStyle.None;
            return label;
        }

        /// <summary>
        /// The race HUD: up to three lines of status in the top corner opposite Look back (the top
        /// right, or the top left with left-handed controls, design doc §5) and the item slot at the
        /// top centre (§6).
        /// </summary>
        private void BuildRaceHud()
        {
            _hudLeftHanded = Accessibility.LeftHanded;
            _raceHud = UiKit.Fill(new VisualElement { name = "race HUD" });
            var corner = new VisualElement();
            corner.style.position = Position.Absolute;
            corner.style.top = _hudLeftHanded ? 84f + LeftCornerDrop : 84f;
            corner.style.width = 400f;
            if (_hudLeftHanded)
            {
                corner.style.left = 24f;
            }
            else
            {
                corner.style.right = 24f;
            }

            corner.pickingMode = PickingMode.Ignore;
            _corner1 = CornerLine();
            _corner2 = CornerLine();
            _corner3 = CornerLine();
            corner.Add(_corner1);
            corner.Add(_corner2);
            corner.Add(_corner3);
            _raceHud.Add(corner);

            var slot = UiKit.Panel();
            slot.style.position = Position.Absolute;
            slot.style.top = 16f;
            slot.style.left = Length.Percent(50);
            slot.style.marginLeft = -110f;
            slot.style.width = 220f;
            slot.style.height = 56f;
            slot.style.paddingTop = slot.style.paddingBottom = 0f;
            slot.style.justifyContent = Justify.Center;
            slot.pickingMode = PickingMode.Ignore;
            _itemSlot = UiKit.Centred(UiKit.Text("—", 34, UiKit.Muted, bold: true));
            slot.Add(_itemSlot);
            _raceHud.Add(slot);
            _raceHud.style.display = DisplayStyle.None;
            _layer.Safe.Add(_raceHud);
        }

        private Label CornerLine()
        {
            var line = UiKit.Text(string.Empty, 34, Color.white, bold: true);
            line.style.height = 44f;
            line.style.unityTextAlign = _hudLeftHanded ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            return line;
        }

        private void Update()
        {
            if (_session != null && _session.Ended && Time.realtimeSinceStartup - _session.EndedAt >= EndedReturnSeconds)
            {
                Bootstrap.ShowMainMenu(_session.Status);
                return;
            }

            if (_session == null || _layer == null)
            {
                return;
            }

            if (_hudLeftHanded != Accessibility.LeftHanded)
            {
                Build();
            }

            Set(_status, _session.Status);

            // Design doc §12: "Reconnecting…" while the host is quiet; why it ended, before going back to the menu.
            Show(_banner, _session.Ended ? _session.Status : _session.Reconnecting ? "Reconnecting…" : null);

            bool joined = _session.TryGetLocalPlayerId(out byte me);
            if (_session.Ended || !joined || _session.Race == null)
            {
                ShowView(string.Empty, null);
                _raceHud.style.display = DisplayStyle.None;
                Show(_countdown, null);
                Show(_stunt, null);
                return;
            }

            var race = _session.Race;
            bool racing = race.Phase == RacePhase.Countdown || race.Phase == RacePhase.Racing;
            _raceHud.style.display = racing ? DisplayStyle.Flex : DisplayStyle.None;
            switch (race.Phase)
            {
                case RacePhase.Lobby:
                    Show(_countdown, null);
                    Show(_stunt, null);
                    if (_session.IsHost && Time.unscaledTime >= _nextAddressRefresh)
                    {
                        RefreshHostAddresses();
                    }

                    ShowView(LobbySignature(me), () => BuildLobby(me));
                    break;
                case RacePhase.Countdown:
                case RacePhase.Racing:
                    ShowView("race", null);
                    UpdateRace(race, me);
                    break;
                case RacePhase.Results:
                    Show(_countdown, null);
                    Show(_stunt, null);
                    var table = _cup.CurrentStandings(race);
                    ShowView(ResultsSignature(race, table), () => BuildResults(race, me, table));
                    if (table != null && _cupCountdown != null)
                    {
                        _cup.UpdateCountdown(table, _cupCountdown);
                    }

                    break;
            }
        }

        /// <summary>Rebuilds the lobby or results view with <paramref name="build"/> when its <paramref name="signature"/> changes.</summary>
        private void ShowView(string signature, System.Action build)
        {
            if (signature == _shownView)
            {
                return;
            }

            _shownView = signature;
            _view.Clear();
            _cupCountdown = null;
            build?.Invoke();
        }

        /// <summary>
        /// The lobby rebuilds whenever anyone readies up, changes car or votes; carry the scroll
        /// position over so the host isn't thrown back to the top, away from Start. The offset is
        /// applied once the new list has been laid out, as until then there is nothing to scroll.
        /// </summary>
        private void KeepScroll(ScrollView scroll)
        {
            float offset = _lobbyScroll != null ? _lobbyScroll.scrollOffset.y : 0f;
            _lobbyScroll = scroll;
            if (offset <= 0f)
            {
                return;
            }

            EventCallback<GeometryChangedEvent> restore = null;
            restore = _ =>
            {
                scroll.contentContainer.UnregisterCallback(restore);
                scroll.scrollOffset = new Vector2(0f, offset);
            };
            scroll.contentContainer.RegisterCallback(restore);
        }

        /// <summary>Forces the current view to rebuild next frame, after a button changed what it shows.</summary>
        private void Changed() => _shownView = null;

        private static void Set(Label label, string text)
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }

        private static void Show(Label label, string text)
        {
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            if (!string.IsNullOrEmpty(text))
            {
                Set(label, text);
            }
        }

        private void RefreshHostAddresses()
        {
            // For guests whose network blocks discovery (design doc §1.1).
            var addresses = HostAddress.LocalIPv4Addresses();
            _hostAddresses = addresses.Count > 0 ? "Join by IP: " + string.Join(" or ", addresses) : string.Empty;
            _nextAddressRefresh = Time.unscaledTime + AddressRefreshSeconds;
            UpdateJoinQr(addresses.Count > 0 ? JoinLink.Make(addresses[0], _session.Port) : null);
        }

        /// <summary>Everything the lobby shows, so it rebuilds only when one of these changes.</summary>
        private string LobbySignature(byte me)
        {
            _signature.Clear().Append("lobby/").Append(me).Append('/');
            foreach (var p in _session.Players)
            {
                _signature.Append(p.PlayerId).Append(p.Name).Append(p.ColourIndex).Append(p.BodyIndex)
                    .Append(p.Ready).Append(p.IsHost).Append(p.IsBot).Append(p.TrackVote).Append('|');
            }

            var settings = _session.Settings;
            _signature.Append(_session.LeaderId).Append('/').Append(settings.Mode).Append(settings.Laps).Append(settings.Track)
                .Append(settings.BotFill).Append(settings.BotDifficulty).Append(settings.Rules.ToByte()).Append(_cup.Signature);
            if (_session.LeadsRoom)
            {
                _signature.Append(_hostAddresses).Append(_joinLink).Append(_session.CanStart);
            }
            else
            {
                _signature.Append(_session.GuestSide.Ready);
            }

            return _signature.ToString();
        }

        private void BuildLobby(byte me)
        {
            // The lobby list and settings on the left: as tall as they need, scrolling if that's
            // more than the space below the status line. The column marks out that space; the
            // panel shrinks to fit inside it.
            var column = new VisualElement { name = "lobby column", pickingMode = PickingMode.Ignore };
            column.style.position = Position.Absolute;
            column.style.left = 24f;
            column.style.top = 100f;
            column.style.bottom = 16f;
            column.style.width = 540f;
            column.style.alignItems = Align.FlexStart;
            var panel = UiKit.Panel(540f);
            panel.style.maxHeight = Length.Percent(100);
            panel.style.flexShrink = 1f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 0f;
            scroll.style.flexShrink = 1f;
            KeepScroll(scroll);
            var content = scroll.contentContainer;
            panel.Add(scroll);
            column.Add(panel);
            _view.Add(column);

            content.Add(UiKit.Heading("Lobby"));
            byte leader = _session.LeaderId;
            foreach (var player in _session.Players)
            {
                string state = player.IsHost ? "host" : player.IsBot ? "bot" : player.PlayerId == leader ? "runs the room" : player.Ready ? "ready" : "not ready";
                string you = player.PlayerId == me ? " (you)" : string.Empty;
                var row = UiKit.Row();
                row.Add(Dot(player.ColourIndex));
                row.Add(UiKit.Space(8f));
                row.Add(UiKit.Body(player.Name + you));
                row.Add(UiKit.Grow());
                row.Add(UiKit.Hint(state));
                content.Add(row);
            }

            content.Add(UiKit.Space(8f));
            var settings = _session.Settings;
            if (_session.LeadsRoom)
            {
                if (_session.IsHost && _hostAddresses.Length > 0)
                {
                    content.Add(UiKit.Hint(_hostAddresses));
                }

                _cup.AddLobbyChoice(content);
                if (!settings.CupChosen)
                {
                    AddSingleEventSettings(content);
                }

                AddBotSettings(content);
                AddRules(content);

                // The room leader decides (design doc §11, §2.3); alone, it's a practice race.
                bool canStart = _session.CanStart;
                if (settings.CupChosen)
                {
                    _cup.AddStartButtons(content, canStart);
                }
                else
                {
                    content.Add(UiKit.Enabled(UiKit.Button(canStart ? $"Start {EventText.ModeName(settings.Mode)}" : "Waiting for everyone to be ready", () =>
                    {
                        if (_session.CanStart)
                        {
                            _session.Command(LobbyCommand.Start);
                        }
                    }, primary: true), canStart));
                }
            }
            else
            {
                bool ready = _session.GuestSide.Ready;
                content.Add(UiKit.Button(ready ? "Not ready" : "Ready", () =>
                {
                    _session.GuestSide.SetReady(!_session.GuestSide.Ready);
                    Changed();
                }, primary: !ready));
                content.Add(UiKit.Hint(ready ? "Waiting for the race to start." : "Drive around, then tap Ready."));
                content.Add(UiKit.Hint("Next: " + EventText.SettingsSummary(settings)));
                AddTrackVote(content, me);
            }

            // On the right: the host's QR code (scanned with a guest's camera app, it opens the game
            // straight into joining, design doc §1.1), then this phone's car picker.
            var side = new VisualElement();
            side.style.position = Position.Absolute;
            side.style.left = 584f;
            side.style.top = 100f;
            side.style.width = 248f;
            side.pickingMode = PickingMode.Ignore;
            if (_session.IsHost && _joinQr != null)
            {
                var qr = new Image { image = _joinQr, scaleMode = ScaleMode.ScaleToFit };
                qr.style.width = qr.style.height = 200f;
                qr.pickingMode = PickingMode.Ignore;
                side.Add(qr);
                side.Add(UiKit.Hint("Scan to join"));
                side.Add(UiKit.Space(12f));
            }

            AddCarPicker(side, me);
            _view.Add(side);
        }

        private static VisualElement Dot(int colourIndex)
        {
            var dot = new VisualElement();
            dot.style.width = dot.style.height = 18f;
            dot.style.flexShrink = 0f;
            dot.style.backgroundColor = CarColours.ForIndex(colourIndex);
            dot.pickingMode = PickingMode.Ignore;
            UiKit.Round(dot, 4f);
            return dot;
        }

        /// <summary>
        /// Changing car without leaving the lobby (design doc §11): arrows through the bodies and a
        /// swatch per colour. Colours other players have are drawn small and can't be picked. The
        /// car changes once the host's roster comes back with it.
        /// </summary>
        private void AddCarPicker(VisualElement side, byte me)
        {
            if (!TryFind(me, out var mine))
            {
                return;
            }

            const int perRow = 4;
            var panel = UiKit.Panel();
            panel.Add(UiKit.Hint("Your car"));
            var body = CarModels.ForIndex(mine.BodyIndex);
            panel.Add(UiKit.Text(CarModels.DisplayName(body), 20, Color.white));
            panel.Add(CarStatsView.Build(body.Stats, compact: true));
            panel.Add(UiKit.EvenRow(
                UiKit.Button("<", () => _session.ChangeCar(new CarChoice((byte)((mine.BodyIndex + LobbyRoster.BodyCount - 1) % LobbyRoster.BodyCount), mine.ColourIndex))),
                UiKit.Button(">", () => _session.ChangeCar(new CarChoice((byte)((mine.BodyIndex + 1) % LobbyRoster.BodyCount), mine.ColourIndex)))));
            for (int row = 0; row * perRow < LobbyRoster.ColourCount; row++)
            {
                var swatches = UiKit.Row();
                for (int i = row * perRow; i < Mathf.Min(LobbyRoster.ColourCount, (row + 1) * perRow); i++)
                {
                    byte colour = (byte)i;
                    bool chosen = i == mine.ColourIndex;
                    bool taken = !chosen && ColourTaken(colour, me);
                    swatches.Add(UiKit.Swatch(CarColours.ForIndex(i), chosen, taken, () => _session.ChangeCar(new CarChoice(mine.BodyIndex, colour)), size: 44f));
                }

                panel.Add(swatches);
            }

            side.Add(panel);
        }

        private bool ColourTaken(byte colour, byte me)
        {
            foreach (var p in _session.Players)
            {
                if (p.PlayerId != me && p.ColourIndex == colour)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The leader's settings for a single event: mode, laps and track, and the guests' track votes.</summary>
        private void AddSingleEventSettings(VisualElement panel)
        {
            var settings = _session.Settings;
            void Change(System.Func<LobbySettings, LobbySettings> edit) => _session.ChangeSettings(edit(_session.Settings));

            var row = UiKit.Row();
            var mode = UiKit.Button(EventText.ModeName(settings.Mode), () => Change(s =>
            {
                s.Mode = (GameMode)(((int)s.Mode + 1) % 4);
                return s;
            }));
            mode.style.width = 190f;
            row.Add(mode);
            row.Add(UiKit.Space(12f));
            if (settings.Mode == GameMode.Race)
            {
                var laps = UiKit.Body($"Laps: {settings.Laps}");
                laps.style.width = 130f;
                row.Add(laps);
                var fewer = UiKit.Button("-", () => Change(s =>
                {
                    s.Laps = Mathf.Max(1, s.Laps - 1);
                    return s;
                }));
                fewer.style.width = 80f;
                row.Add(fewer);
                row.Add(UiKit.Space(8f));
                var more = UiKit.Button("+", () => Change(s =>
                {
                    s.Laps = Mathf.Min(LobbySettings.MaxLaps, s.Laps + 1);
                    return s;
                }));
                more.style.width = 80f;
                row.Add(more);
            }
            else
            {
                var hint = UiKit.Hint(
                    settings.Mode == GameMode.Sumo ? "Last car on the platform wins"
                    : settings.Mode == GameMode.Monster ? "Monster trucks: big air and crushes score"
                    : $"{BattleRules.StartingLives} lives, items only");
                hint.style.flexShrink = 1f;
                row.Add(hint);
            }

            panel.Add(row);
            if (settings.Mode != GameMode.Race)
            {
                return;
            }

            // The leader picks the track (design doc §11); everyone moves to it at the countdown.
            panel.Add(UiKit.Button("Track: " + TrackCatalog.Layouts[settings.Track].Definition.DisplayName, () => Change(s =>
            {
                s.Track = (s.Track + 1) % TrackCatalog.Count;
                return s;
            })));

            // Guests' votes only suggest; the leader can go with them in one tap, or not.
            var votes = TrackVotes.Count(_session.Players, TrackCatalog.Count);
            string summary = TrackVotes.Summary(votes, TrackNames());
            if (summary.Length == 0)
            {
                return;
            }

            var voteRow = UiKit.Row();
            var count = UiKit.Hint("Votes: " + summary);
            count.style.flexShrink = 1f;
            count.style.flexGrow = 1f;
            voteRow.Add(count);
            int leader = TrackVotes.Leader(votes, settings.Track);
            if (leader != settings.Track)
            {
                var go = UiKit.Button("Go with it", () => Change(s =>
                {
                    s.Track = leader;
                    return s;
                }));
                go.style.width = 180f;
                voteRow.Add(go);
            }

            panel.Add(voteRow);
        }

        /// <summary>
        /// A guest's vote for the next race's track (design doc §11): tap to step through the tracks
        /// and back to no vote. The host sees the count and still decides.
        /// </summary>
        private void AddTrackVote(VisualElement panel, byte me)
        {
            if (!TryFind(me, out var mine))
            {
                return;
            }

            bool voted = mine.TrackVote < TrackCatalog.Count;
            string label = voted ? "My vote: " + TrackCatalog.Layouts[mine.TrackVote].Definition.DisplayName : "Vote for a track";
            panel.Add(UiKit.Button(label, () =>
            {
                int next = voted ? mine.TrackVote + 1 : 0;
                _session.VoteTrack(next < TrackCatalog.Count ? (byte)next : VoteTrackMessage.NoVote);
            }));

            string summary = TrackVotes.Summary(TrackVotes.Count(_session.Players, TrackCatalog.Count), TrackNames());
            panel.Add(UiKit.Hint(summary.Length > 0 ? "Votes: " + summary : "The host picks the track; votes help them decide."));
        }

        /// <summary>The bot fill and difficulty (design doc §9), remembered on the host between sessions.</summary>
        private void AddBotSettings(VisualElement panel)
        {
            var settings = _session.Settings;
            var fill = UiKit.Button(settings.BotFill == 0 ? "Bots: off" : $"Bots: fill to {settings.BotFill}", () =>
            {
                var next = _session.Settings;
                next.BotFill = LobbySettings.BotFills[(System.Array.IndexOf(LobbySettings.BotFills, next.BotFill) + 1) % LobbySettings.BotFills.Length];
                _session.ChangeSettings(next);
            });
            var difficulty = UiKit.Enabled(UiKit.Button(settings.BotDifficulty.ToString(), () =>
            {
                var next = _session.Settings;
                if (next.BotFill == 0)
                {
                    return;
                }

                next.BotDifficulty = (BotDifficulty)(((int)next.BotDifficulty + 1) % 3);
                _session.ChangeSettings(next);
            }), settings.BotFill > 0);
            panel.Add(UiKit.EvenRow(fill, difficulty));
        }

        /// <summary>
        /// The rules for the next event (design doc §11, host controls): damage, items and car stats,
        /// each a toggle, remembered on the host between sessions. Battle always has items, so its
        /// button says so and can't be changed.
        /// </summary>
        private void AddRules(VisualElement panel)
        {
            var settings = _session.Settings;
            var rules = settings.Rules;
            void Choose(EventRules next)
            {
                var changed = _session.Settings;
                changed.Rules = next;
                _session.ChangeSettings(changed);
            }

            bool battle = !settings.CupChosen && settings.Mode == GameMode.Battle;
            var damage = UiKit.Button(rules.Damage ? "Damage: on" : "Damage: off", () => Choose(rules.WithDamage(!rules.Damage)));
            var items = UiKit.Enabled(UiKit.Button(battle ? "Items: always" : rules.Items ? "Items: on" : "Items: off", () =>
            {
                if (!battle)
                {
                    Choose(rules.WithItems(!rules.Items));
                }
            }), !battle);
            var stats = UiKit.Button(rules.CarStats ? "Car stats: on" : "Car stats: off", () => Choose(rules.WithCarStats(!rules.CarStats)));

            // Three to a row in the lobby panel, so smaller text than a full-width button.
            foreach (var button in new[] { damage, items, stats })
            {
                button.style.fontSize = Accessibility.Text(21);
                button.style.paddingLeft = button.style.paddingRight = 6f;
            }

            panel.Add(UiKit.EvenRow(damage, items, stats));
            panel.Add(UiKit.Hint("Car stats off makes every car drive the same. Damage only counts in races."));
        }

        private static string[] TrackNames()
        {
            if (_trackNames == null)
            {
                _trackNames = new string[TrackCatalog.Count];
                for (int i = 0; i < _trackNames.Length; i++)
                {
                    _trackNames[i] = TrackCatalog.Layouts[i].Definition.DisplayName;
                }
            }

            return _trackNames;
        }

        private void UpdateJoinQr(string link)
        {
            if (link == _joinLink)
            {
                return;
            }

            _joinLink = link;
            if (_joinQr != null)
            {
                Destroy(_joinQr);
                _joinQr = null;
            }

            if (link != null)
            {
                _joinQr = QrTexture.Build(link);
            }
        }

        private void OnDestroy()
        {
            if (_joinQr != null)
            {
                Destroy(_joinQr);
            }
        }

        /// <summary>The countdown, then the race or arena status and the item slot, refreshed every frame.</summary>
        private void UpdateRace(IRaceView race, byte me)
        {
            long msToGo = race.StartAtLocalMs - NetworkSession.NowMs;
            Show(_countdown, EventText.Countdown(race.Phase, msToGo));
            Show(_stunt, Time.unscaledTime < _stuntUntil ? _stuntText : null);
            if (race.Mode == GameMode.Monster)
            {
                UpdateMonsterStatus(race, me, msToGo);
            }
            else if (race.Mode != GameMode.Race)
            {
                UpdateArenaStatus(race, me, msToGo);
            }
            else
            {
                UpdateRaceStatus(race, me);
            }

            var held = _session.Items?.Held ?? ItemKind.None;
            Set(_itemSlot, held == ItemKind.None ? "—" : ItemNames.Label(held));
            _itemSlot.style.color = held == ItemKind.None ? UiKit.Muted : UiKit.Accent;
        }

        private void UpdateRaceStatus(IRaceView race, byte me)
        {
            int lap = Mathf.Min(_cars.Detector.LapsCompleted + 1, race.LapCount);
            string position = string.Empty;
            var standings = race.Standings;
            if (standings != null)
            {
                for (int i = 0; i < standings.Order.Count; i++)
                {
                    if (standings.Order[i].PlayerId == me)
                    {
                        var entry = standings.Order[i];
                        position = entry.FinishPosition > 0
                            ? $"Finished {RaceFormat.Ordinal(entry.FinishPosition)}"
                            : $"{RaceFormat.Ordinal(i + 1)} / {standings.Order.Count}";
                        if (entry.FinishPosition > 0)
                        {
                            lap = race.LapCount;
                        }
                    }
                }
            }

            Set(_corner1, position);
            Set(_corner2, $"Lap {lap} / {race.LapCount}");
            Set(_corner3, EventText.RulesNote(race.Rules));
        }

        /// <summary>The Monster Jam HUD (design doc §7.0): this phone's place and points, and the time left.</summary>
        private void UpdateMonsterStatus(IRaceView race, byte me, long msToGo)
        {
            string place = string.Empty;
            string points = string.Empty;
            var standings = race.Standings;
            if (standings != null)
            {
                for (int i = 0; i < standings.Order.Count; i++)
                {
                    if (standings.Order[i].PlayerId == me)
                    {
                        place = $"{RaceFormat.Ordinal(i + 1)} / {standings.Order.Count}";
                        points = $"{standings.Order[i].Points} pts";
                    }
                }
            }

            Set(_corner1, place);
            Set(_corner2, points);
            bool clock = race.Phase == RacePhase.Racing;
            Set(_corner3, clock ? RaceFormat.Time(System.Math.Max(0L, MonsterRules.TimeLimitMs + msToGo)) : string.Empty);
        }

        /// <summary>The arena HUD (design doc §7.0): this phone's lives (Battle) or place once out, how many are left, and Battle's clock.</summary>
        private void UpdateArenaStatus(IRaceView race, byte me, long msToGo)
        {
            int left = 0;
            int total = 0;
            string status = string.Empty;
            var standings = race.Standings;
            if (standings != null)
            {
                foreach (var entry in standings.Order)
                {
                    total++;
                    if (entry.FinishPosition == 0)
                    {
                        left++;
                    }

                    if (entry.PlayerId == me)
                    {
                        status = entry.FinishPosition > 0
                            ? $"Out: {RaceFormat.Ordinal(entry.FinishPosition)}"
                            : race.Mode == GameMode.Battle ? $"Lives {entry.LapsCompleted}" : "Still on";
                    }
                }
            }

            Set(_corner1, status);
            Set(_corner2, total > 0 ? $"{left} / {total} left" : string.Empty);
            bool clock = race.Mode == GameMode.Battle && race.Phase == RacePhase.Racing;
            Set(_corner3, clock ? RaceFormat.Time(System.Math.Max(0L, BattleRules.TimeLimitMs + msToGo)) : string.Empty);
        }

        /// <summary>Everything the results show, so they rebuild only when one of these changes.</summary>
        private string ResultsSignature(IRaceView race, CupStandingsMessage table)
        {
            _signature.Clear().Append("results/");
            foreach (var r in race.Results.Results)
            {
                _signature.Append(r.PlayerId).Append(r.Position).Append(r.RaceTimeMs).Append(r.DidNotFinish).Append(r.Points).Append('|');
            }

            _signature.Append(_session.Players.Count).Append(_session.LeadsRoom).Append(_session.LeaderId);
            if (table != null)
            {
                _signature.Append(_cup.StandingsSignature(table));
            }

            return _signature.ToString();
        }

        private void BuildResults(IRaceView race, byte me, CupStandingsMessage table)
        {
            // The results, with the cup table beside them in a cup; its buttons replace the lobby button.
            var row = UiKit.Row();
            row.style.position = Position.Absolute;
            row.style.top = 120f;
            row.style.left = row.style.right = 0f;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.FlexStart;

            var panel = UiKit.Panel(560f);
            panel.Add(UiKit.Heading("Results"));
            foreach (var r in race.Results.Results)
            {
                bool stillHere = TryFind(r.PlayerId, out var player);

                // In an arena the time is how long the player lasted; Monster Jam shows the points instead.
                string time = EventText.ResultDetail(race.Mode, r);
                string tag = r.PlayerId == me ? " (you)" : !stillHere ? " (left)" : player.IsBot ? " (bot)" : string.Empty;
                var line = UiKit.Row();
                line.Add(Dot(stillHere ? player.ColourIndex : r.PlayerId));
                line.Add(UiKit.Space(10f));
                var name = UiKit.Text($"{RaceFormat.Ordinal(r.Position)}  {r.Name}{tag}", 20, Color.white);
                name.style.flexGrow = 1f;
                line.Add(name);
                line.Add(UiKit.Text(time, 20, Color.white));
                panel.Add(line);
            }

            panel.Add(UiKit.Space(16f));
            if (_session.LeadsRoom && table == null)
            {
                panel.Add(UiKit.Button("Back to lobby", () => _session.Command(LobbyCommand.BackToLobby), primary: true, sound: UiSound.Back));
            }
            else if (table == null)
            {
                // On a server the room leader moves things on; on a phone host, the host does.
                bool leaderIsGuest = TryFind(_session.LeaderId, out var leader) && !leader.IsHost;
                panel.Add(UiKit.Text(leaderIsGuest ? "Waiting for the room leader..." : "Waiting for the host...", 20, UiKit.Muted));
            }

            row.Add(panel);
            if (table != null)
            {
                row.Add(UiKit.Space(40f));
                string self = TryFind(me, out var mine) ? mine.Name : string.Empty;
                row.Add(_cup.BuildStandings(table, self, out _cupCountdown));
            }

            _view.Add(row);
        }

        /// <summary>False if the player has left the session since the race began.</summary>
        private bool TryFind(byte playerId, out RosterEntry player)
        {
            foreach (var p in _session.Players)
            {
                if (p.PlayerId == playerId)
                {
                    player = p;
                    return true;
                }
            }

            player = default;
            return false;
        }
    }
}
