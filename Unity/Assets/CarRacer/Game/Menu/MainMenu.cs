using System.Net.Sockets;
using System.Text;
using CarRacer.Core.Art;
using CarRacer.Core.Audio;
using CarRacer.Core.Discovery;
using CarRacer.Core.Session;
using CarRacer.Game.Audio;
using CarRacer.Game.Driving;
using CarRacer.Game.Platform;
using CarRacer.Game.Screens;
using CarRacer.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Menu
{
    /// <summary>
    /// The menus before a game (design doc §11): your name on first launch, then Host game, Join game,
    /// Practice, Garage and Settings (with its Accessibility page). The Join page lists the games
    /// announced on this Wi-Fi as cards, and takes an IP address for networks that block discovery.
    /// Discovery and the Android multicast lock run only while the Join page is open (design doc §1.1).
    /// Each page is a UI Toolkit element tree, rebuilt whenever something on it changes.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        public const string NameKey = "playerName";
        public const string JoinAddressKey = "joinAddress";

        /// <summary>How often the Join page checks whether the list of games has changed.</summary>
        private const float HostListRefreshSeconds = 0.5f;

        private enum Page
        {
            Name,
            Main,
            Join,
            Settings,
            Garage,
            Accessibility,
        }

        private readonly DiscoveredHostList _hosts = new DiscoveredHostList();
        private readonly AndroidMulticastLock _multicastLock = new AndroidMulticastLock();
        private readonly StringBuilder _hostSignature = new StringBuilder();
        private DiscoveryListener _listener;
        private Page _page;
        private string _name;
        private string _nameDraft;
        private string _joinAddress;
        private string _joinMessage = string.Empty;
        private GaragePreview _preview;
        private UiRoot _layer;
        private VisualElement _hostList;
        private string _shownHosts;
        private float _nextHostRefresh;

        /// <summary>A join link that arrived before the player had a name; joined once they choose one.</summary>
        public static (string Address, ushort Port)? PendingJoin { get; set; }

        /// <summary>Shown on the main page, e.g. why the last session ended.</summary>
        public string Notice { get; set; }

        private static long NowMs => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        private void OnEnable()
        {
            _name = PlayerPrefs.GetString(NameKey, string.Empty);
            _nameDraft = string.IsNullOrEmpty(_name) ? SystemInfo.deviceModel : _name;
            _joinAddress = PlayerPrefs.GetString(JoinAddressKey, string.Empty);
            _page = string.IsNullOrEmpty(_name) ? Page.Name : Page.Main;
            MusicPlayer.Play(MusicChoice.Menu);
        }

        private void Start()
        {
            _layer = UiRoot.Create(transform, "Menu");
            _layer.Restyle += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            StopDiscovery();
        }

        private void Update()
        {
            if (_listener != null)
            {
                long now = NowMs;
                _listener.Poll(_hosts, now);
                _hosts.Expire(now);
            }

            if (_page == Page.Join && Time.unscaledTime >= _nextHostRefresh)
            {
                _nextHostRefresh = Time.unscaledTime + HostListRefreshSeconds;
                FillHostList();
            }
        }

        private void GoTo(Page page)
        {
            // The turntable only exists while the garage is open.
            if (page == Page.Garage && _preview == null)
            {
                _preview = new GameObject(nameof(GaragePreview)).AddComponent<GaragePreview>();
                _preview.transform.SetParent(transform, false);
            }
            else if (page != Page.Garage && _preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }

            if (page == Page.Join && _page != Page.Join)
            {
                StartDiscovery();
            }
            else if (page != Page.Join && _page == Page.Join)
            {
                StopDiscovery();
            }

            _page = page;
            Refresh();
        }

        private void StartDiscovery()
        {
            _multicastLock.Acquire();
            try
            {
                _listener = new DiscoveryListener();
                _joinMessage = string.Empty;
            }
            catch (SocketException e)
            {
                _joinMessage = $"Can't look for games on this network ({e.Message}). You can still join by IP.";
            }
        }

        private void StopDiscovery()
        {
            _listener?.Dispose();
            _listener = null;
            _multicastLock.Release();
        }

        /// <summary>Rebuilds the current page, e.g. after a setting changes its button's label.</summary>
        private void Refresh()
        {
            if (_layer == null)
            {
                return;
            }

            _layer.Safe.Clear();
            _hostList = null;
            _shownHosts = null;
            switch (_page)
            {
                case Page.Name:
                    BuildNamePage();
                    break;
                case Page.Main:
                    BuildMainPage();
                    break;
                case Page.Join:
                    BuildJoinPage();
                    break;
                case Page.Settings:
                    BuildSettingsPage();
                    break;
                case Page.Garage:
                    BuildGaragePage();
                    break;
                case Page.Accessibility:
                    BuildAccessibilityPage();
                    break;
            }
        }

        /// <summary>A panel of <paramref name="width"/>, centred across the safe area, <paramref name="top"/> pixels down.</summary>
        private VisualElement NewPage(float width, float top)
        {
            var column = UiKit.Fill(new VisualElement());
            column.style.alignItems = Align.Center;
            column.style.paddingTop = top;
            column.style.paddingBottom = 16f;
            column.style.paddingLeft = column.style.paddingRight = 16f;
            var panel = UiKit.Panel(width);
            panel.style.flexShrink = 1f;
            column.Add(panel);
            _layer.Safe.Add(column);
            return panel;
        }

        /// <summary>A page's heading with a Back button on the right.</summary>
        private void Header(VisualElement panel, string title, Page back)
        {
            var row = UiKit.Row();
            row.Add(UiKit.Heading(title));
            row.Add(UiKit.Grow());
            var button = UiKit.Button("Back", () => GoTo(back), sound: UiSound.Back);
            button.style.width = 140f;
            row.Add(button);
            panel.Add(row);
        }

        private void BuildNamePage()
        {
            var panel = NewPage(560f, 120f);
            panel.Add(UiKit.Title("Car Racer"));
            panel.Add(UiKit.Space(16f));
            panel.Add(UiKit.Body("What should other players call you?"));
            Button next = null;
            panel.Add(UiKit.Field(_nameDraft ?? string.Empty, LobbyRoster.MaxNameChars, value =>
            {
                _nameDraft = value;
                UiKit.Enabled(next, !string.IsNullOrWhiteSpace(_nameDraft));
            }));
            panel.Add(UiKit.Space(12f));
            next = UiKit.Enabled(UiKit.Button("Continue", ChooseName, primary: true), !string.IsNullOrWhiteSpace(_nameDraft));
            panel.Add(next);
        }

        private void ChooseName()
        {
            if (string.IsNullOrWhiteSpace(_nameDraft))
            {
                return;
            }

            _name = LobbyRoster.CleanName(_nameDraft);
            PlayerPrefs.SetString(NameKey, _name);
            if (PendingJoin.HasValue)
            {
                var join = PendingJoin.Value;
                PendingJoin = null;
                Bootstrap.JoinGame(_name, join.Address, join.Port);
            }
            else
            {
                GoTo(Page.Main);
            }
        }

        private void BuildMainPage()
        {
            var panel = NewPage(480f, 70f);
            panel.Add(UiKit.Title("Car Racer"));
            panel.Add(UiKit.Centred(UiKit.Hint($"Racing as {_name}")));
            if (!string.IsNullOrEmpty(Notice))
            {
                panel.Add(UiKit.Centred(UiKit.Hint(Notice)));
            }

            panel.Add(UiKit.Space(20f));
            panel.Add(UiKit.Button("Host game", () => Bootstrap.HostGame(_name), primary: true));
            panel.Add(UiKit.Button("Join game", () => GoTo(Page.Join)));
            panel.Add(UiKit.Button("Practice", Bootstrap.ShowPractice));
            panel.Add(UiKit.Space(12f));
            panel.Add(UiKit.Button("Change name", () =>
            {
                _nameDraft = _name;
                GoTo(Page.Name);
            }));
            panel.Add(UiKit.EvenRow(UiKit.Button("Garage", () => GoTo(Page.Garage)), UiKit.Button("Settings", () => GoTo(Page.Settings))));
        }

        /// <summary>
        /// The garage (design doc §11): a turning preview of the car, the body to step through and
        /// a swatch per colour. The choice is remembered at once and used when hosting, joining or
        /// practising.
        /// </summary>
        private void BuildGaragePage()
        {
            var panel = NewPage(760f, 24f);
            Header(panel, "Garage", Page.Main);

            var model = Garage.Model;
            var colour = CarColours.ForIndex(Garage.Colour);
            if (_preview != null)
            {
                _preview.Show(model, colour);
                var image = new Image { image = _preview.Texture, scaleMode = ScaleMode.ScaleToFit };
                image.style.height = 230f;
                image.pickingMode = PickingMode.Ignore;
                panel.Add(image);
            }

            var bodies = UiKit.Row();
            var back = UiKit.Button("<", () => ChangeGarage(Garage.Body - 1, Garage.Colour));
            back.style.width = 90f;
            var name = UiKit.Centred(UiKit.Heading(CarModels.DisplayName(model)));
            name.style.flexGrow = 1f;
            var next = UiKit.Button(">", () => ChangeGarage(Garage.Body + 1, Garage.Colour));
            next.style.width = 90f;
            bodies.Add(back);
            bodies.Add(name);
            bodies.Add(next);
            panel.Add(bodies);
            panel.Add(CarStatsView.Build(model.Stats));

            var swatches = UiKit.Row();
            for (int i = 0; i < LobbyRoster.ColourCount; i++)
            {
                int pick = i;
                swatches.Add(UiKit.Swatch(CarColours.ForIndex(i), i == Garage.Colour, unavailable: false, () => ChangeGarage(Garage.Body, pick)));
            }

            panel.Add(swatches);
            panel.Add(UiKit.Hint("Each car trades top speed, steering and acceleration; none is better overall. If someone in the game already has your colour, you get the next free one."));
        }

        private void ChangeGarage(int body, int colour)
        {
            Garage.Body = body;
            Garage.Colour = colour;
            Refresh();
        }

        /// <summary>Every setting in one place (design doc §11); each is remembered as soon as it changes.</summary>
        private void BuildSettingsPage()
        {
            var panel = NewPage(760f, 24f);
            Header(panel, "Settings", Page.Main);
            panel.Add(UiKit.EvenRow(
                Setting(GameAudio.Label, GameAudio.CycleVolume),
                Setting(MusicPlayer.Label, MusicPlayer.CycleVolume)));

            // Graphics is automatic by default (design doc §14); tap to choose a level instead.
            panel.Add(UiKit.EvenRow(
                Setting(GraphicsQuality.Label, GraphicsQuality.Cycle),
                Setting(PerfOverlay.Label, () => PerfOverlay.Visible = !PerfOverlay.Visible)));

            panel.Add(UiKit.EvenRow(
                Setting(Vibration.Label, () => Vibration.Enabled = !Vibration.Enabled),
                Setting(PlayerCarInput.AutoAccelerateLabel, () => PlayerCarInput.AutoAccelerateEnabled = !PlayerCarInput.AutoAccelerateEnabled)));
            panel.Add(UiKit.EvenRow(
                Setting(PlayerCarInput.TiltEnabled ? "Tilt steering: on" : "Tilt steering: off", () => PlayerCarInput.TiltEnabled = !PlayerCarInput.TiltEnabled),
                UiKit.Button("Accessibility", () => GoTo(Page.Accessibility))));
            panel.Add(UiKit.EvenRow(
                Setting(DrivingTipsPanel.Label, () => DrivingTipsPanel.Seen = !DrivingTipsPanel.Seen),
                UiKit.Space(0f)));
            panel.Add(UiKit.Hint("With tilt steering on, turn the phone like a steering wheel; the steering thumb can still steer."));
        }

        /// <summary>The accessibility options (design doc §5, §6, §11); each applies at once and is remembered.</summary>
        private void BuildAccessibilityPage()
        {
            var panel = NewPage(760f, 24f);
            Header(panel, "Accessibility", Page.Settings);
            panel.Add(Setting(Accessibility.LeftHanded ? "Controls: left-handed" : "Controls: right-handed", () => Accessibility.LeftHanded = !Accessibility.LeftHanded));
            panel.Add(UiKit.Hint(Accessibility.LeftHanded ? "Buttons on the left; drag on the right half to steer." : "Buttons on the right; drag on the left half to steer."));
            panel.Add(Setting(Accessibility.LargeControls ? "Control size: large" : "Control size: standard", () => Accessibility.LargeControls = !Accessibility.LargeControls));

            // Large text rebuilds the page through the layer's Restyle, at the new size.
            panel.Add(Setting(Accessibility.LargeText ? "Text size: large" : "Text size: standard", () => Accessibility.LargeText = !Accessibility.LargeText));
            panel.Add(Setting(Accessibility.ReducedMotion ? "Reduced motion: on" : "Reduced motion: off", () => Accessibility.ReducedMotion = !Accessibility.ReducedMotion));
            panel.Add(UiKit.Hint("Reduced motion keeps the camera steady at speed, stops the car leaning and stops warnings flashing."));
        }

        /// <summary>A settings button: tapping it changes the setting, then the page is rebuilt to show the new value.</summary>
        private Button Setting(string label, System.Action change) => UiKit.Button(label, () =>
        {
            change();
            Refresh();
        });

        private void BuildJoinPage()
        {
            var panel = NewPage(760f, 24f);
            panel.style.height = Length.Percent(100);
            Header(panel, "Join a game", Page.Main);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            _hostList = scroll.contentContainer;
            panel.Add(scroll);
            FillHostList();

            // For networks that block discovery: the address is shown in the host's lobby.
            panel.Add(UiKit.Hint("Not listed? Type the address shown in the host's lobby:"));
            var row = UiKit.Row();
            var field = UiKit.Field(_joinAddress ?? string.Empty, 21, value => _joinAddress = value);
            field.style.flexGrow = 1f;
            row.Add(field);
            row.Add(UiKit.Space(12f));
            var join = UiKit.Button("Join", JoinByAddress);
            join.style.width = 160f;
            row.Add(join);
            panel.Add(row);
            panel.Add(UiKit.Hint(_joinMessage));
        }

        private void JoinByAddress()
        {
            if (HostAddress.TryParse(_joinAddress, out string address, out ushort port))
            {
                PlayerPrefs.SetString(JoinAddressKey, _joinAddress.Trim());
                Bootstrap.JoinGame(_name, address, port);
            }
            else
            {
                _joinMessage = "That isn't an address. It looks like 192.168.1.23.";
                Refresh();
            }
        }

        /// <summary>Fills the Join page's list of games, only when what it would show has changed.</summary>
        private void FillHostList()
        {
            if (_hostList == null)
            {
                return;
            }

            var hosts = _hosts.Hosts;
            _hostSignature.Clear();
            foreach (var host in hosts)
            {
                var a = host.Announcement;
                _hostSignature.Append(host.Address).Append(a.HostName).Append(a.PlayerCount).Append(a.MaxPlayers).Append(a.GamePort).Append(host.StatusText).Append(host.CanJoin).Append('|');
            }

            _hostSignature.Append(_listener != null);
            string signature = _hostSignature.ToString();
            if (signature == _shownHosts)
            {
                return;
            }

            _shownHosts = signature;
            _hostList.Clear();
            if (hosts.Count == 0)
            {
                _hostList.Add(UiKit.Hint(_listener != null ? "Looking for games on this Wi-Fi…" : string.Empty));
            }

            foreach (var host in hosts)
            {
                var a = host.Announcement;
                var card = UiKit.Panel();
                card.style.flexDirection = FlexDirection.Row;
                card.style.alignItems = Align.Center;
                card.style.marginBottom = 8f;
                card.style.backgroundColor = UiKit.FieldColour;
                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.pickingMode = PickingMode.Ignore;
                text.Add(UiKit.Body(a.HostName));
                text.Add(UiKit.Hint($"{a.PlayerCount}/{a.MaxPlayers} players · {host.StatusText}"));
                card.Add(text);
                string address = host.Address;
                ushort gamePort = a.GamePort;
                var join = UiKit.Enabled(UiKit.Button("Join", () => Bootstrap.JoinGame(_name, address, gamePort), primary: true), host.CanJoin);
                join.style.width = 160f;
                card.Add(join);
                _hostList.Add(card);
            }
        }
    }
}
