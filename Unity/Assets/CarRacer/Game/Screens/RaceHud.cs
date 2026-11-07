using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Audio;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using CarRacer.Core.Platform;
using CarRacer.Core.Race;
using CarRacer.Core.Track;
using CarRacer.Game.Bots;
using CarRacer.Game.Driving;
using CarRacer.Game.Net;
using CarRacer.Game.Platform;
using CarRacer.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Screens
{
    /// <summary>
    /// The driving HUD (design doc §5, §6), on a UI Toolkit layer under the session screens: the
    /// touch controls, a drift charge meter, the "Wrong way!" warning, a mini-map with a dot for
    /// every car, arrows at the screen's edge for cars behind or beside you, a warning when a
    /// Missile is chasing you, and a top bar with the speed, Back and quick toggles. Shapes are
    /// drawn with the vector painter on one full-screen canvas, repainted every frame; text is
    /// labels. Works alone and in a session.
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {
        private const float MapSize = 160f;
        private const float MeterWidth = 300f;
        private const float ArrowMin = 26f;
        private const float ArrowMax = 64f;
        private const float ArrowInset = 12f;

        private static readonly Color MissileColour = new Color(1f, 0.2f, 0.15f);
        private static readonly Color WrongWayColour = new Color(1f, 0.25f, 0.2f);

        private static readonly Color[] TierColours =
        {
            new Color(0.6f, 0.6f, 0.6f),  // charging, no boost yet
            new Color(0.35f, 0.7f, 1f),   // tier 1
            new Color(1f, 0.6f, 0.15f),   // tier 2
            new Color(0.9f, 0.35f, 1f),   // tier 3
        };

        private readonly List<Vec3> _rivalPositions = new List<Vec3>();
        private readonly List<Color> _rivalColours = new List<Color>();
        private readonly List<EdgeIndicator> _arrows = new List<EdgeIndicator>();
        private readonly List<(Vector2 At, float Size, float Bearing, Color Colour)> _arrowShapes = new List<(Vector2, float, float, Color)>();
        private readonly List<(Vector2 At, float Radius, bool Held)> _buttonShapes = new List<(Vector2, float, bool)>();
        private readonly List<Label> _buttonLabels = new List<Label>();

        private TrackScreen _screen;
        private TrackProgress _progress;
        private MiniMap _map;
        private TrackLayout _mapTrack;
        private NetworkedCars _cars;
        private BotCars _bots;

        private UiRoot _layer;
        private VisualElement _canvas;
        private Label _driftLabel;
        private Label _wrongWay;
        private Label _missile;
        private Label _wrecked;
        private Label _speed;
        private Button _auto;
        private Button _haptics;

        public void Initialise(TrackScreen screen)
        {
            _screen = screen;
            FollowTrack();
            _layer = UiRoot.Create(transform, "HUD", order: 5);
            _layer.Restyle += Build;
            Build();
        }

        /// <summary>Rebuilds the wrong-way detector and mini-map when racing moves to another track.</summary>
        private void FollowTrack()
        {
            if (_mapTrack == _screen.Track)
            {
                return;
            }

            _mapTrack = _screen.Track;
            _progress = new TrackProgress(_mapTrack.Spline);
            _map = new MiniMap(_mapTrack.Spline);
        }

        private void Build()
        {
            _layer.Safe.Clear();
            _layer.Full.Clear();

            _canvas = UiKit.Fill(new VisualElement { name = "HUD canvas" });
            _canvas.generateVisualContent += Draw;
            _layer.Full.Add(_canvas);
            _layer.Full.Add(_layer.Safe);

            _buttonLabels.Clear();
            for (int i = 0; i < 5; i++)
            {
                var label = UiKit.Centred(UiKit.Text(string.Empty, 20, Color.white, bold: true));
                label.style.position = Position.Absolute;
                label.style.display = DisplayStyle.None;
                _buttonLabels.Add(label);
                _layer.Full.Add(label);
            }

            _driftLabel = UiKit.Centred(UiKit.Text(string.Empty, 20, Color.white, bold: true));
            _driftLabel.style.position = Position.Absolute;
            _driftLabel.style.unityTextAlign = TextAnchor.LowerCenter;
            _layer.Full.Add(_driftLabel);
            _wrongWay = Warning("Wrong way!", WrongWayColour, 340f);
            _missile = Warning("Missile!", MissileColour, 140f);
            _wrecked = Warning(string.Empty, WrongWayColour, 260f);

            // The top bar: the speed on the left; quick toggles and Back on the right.
            _speed = UiKit.Text(string.Empty, 26, Color.white);
            _speed.style.position = Position.Absolute;
            _speed.style.left = 24f;
            _speed.style.top = 20f;
            _layer.Safe.Add(_speed);

            var bar = UiKit.Row();
            bar.style.position = Position.Absolute;
            bar.style.top = 12f;
            bar.style.right = 24f;
            var input = _screen.PlayerInput;
            if (input != null)
            {
                _haptics = TopButton(string.Empty, 168f, () => Vibration.Enabled = !Vibration.Enabled);
                bar.Add(_haptics);
                bar.Add(UiKit.Space(12f));
                _auto = TopButton(string.Empty, 248f, () => input.AutoAccelerate = !input.AutoAccelerate);
                bar.Add(_auto);
                bar.Add(UiKit.Space(12f));
            }

            bar.Add(TopButton("Back", 136f, () => Bootstrap.ShowMainMenu(), UiSound.Back));
            _layer.Safe.Add(bar);
        }

        private Label Warning(string text, Color colour, float top)
        {
            var label = UiKit.Centred(UiKit.Text(text, 56, colour, bold: true));
            label.style.position = Position.Absolute;
            label.style.left = label.style.right = 0f;
            label.style.top = top;
            label.style.height = 80f;
            label.style.display = DisplayStyle.None;
            _layer.Full.Add(label);
            return label;
        }

        private static Button TopButton(string text, float width, System.Action onClick, UiSound sound = UiSound.Tap)
        {
            var button = UiKit.Button(text, onClick, sound: sound);
            button.style.width = width;
            button.style.height = 52f;
            button.style.fontSize = Accessibility.Text(22);
            return button;
        }

        /// <summary>True when the car is on a track, not in an arena or the Monster Jam stadium, so the map and wrong-way warning mean something.</summary>
        private bool OnTrack
        {
            get
            {
                var race = _screen.Session != null ? _screen.Session.Race : null;
                bool stadium = race != null && race.Mode == GameMode.Monster && race.Phase != RacePhase.Lobby;
                return !stadium && !_screen.CameraRig.ArenaCentre.HasValue;
            }
        }

        private void Update()
        {
            FollowTrack();
            var car = _screen.Car;
            if (car != null)
            {
                _progress.Update(car.BodyPosition.ToCore(), car.State.Heading, car.ForwardSpeed, Time.deltaTime);
            }

            if (_layer == null)
            {
                return;
            }

            _arrowShapes.Clear();
            _buttonShapes.Clear();
            float width = UiMetrics.Width(Screen.width, Screen.height);
            bool inArena = _screen.CameraRig.ArenaCentre.HasValue;
            UpdateTouchControls();
            UpdateTopBar(car);
            if (car == null)
            {
                Hide(_driftLabel);
                Hide(_wrongWay);
                Hide(_missile);
                Hide(_wrecked);
                _canvas.MarkDirtyRepaint();
                return;
            }

            // The overhead camera shows every car in an arena; a chase camera needs the arrows and the warnings.
            if (!inArena)
            {
                CollectRivalArrows(width);
                CollectMissileWarning(width);
            }
            else
            {
                Hide(_missile);
            }

            UpdateDriftLabel(car, width);
            UpdateWrecked(car);
            _wrongWay.style.display = _progress.IsWrongWay && OnTrack && !car.Damage.Wrecked ? DisplayStyle.Flex : DisplayStyle.None;
            _canvas.MarkDirtyRepaint();
        }

        private static void Hide(VisualElement element) => element.style.display = DisplayStyle.None;

        /// <summary>"Wrecked!" with the seconds until the car is mended (design doc §4).</summary>
        private void UpdateWrecked(CarController car)
        {
            if (!car.Damage.Wrecked)
            {
                Hide(_wrecked);
                return;
            }

            _wrecked.style.display = DisplayStyle.Flex;
            Set(_wrecked, $"Wrecked! {Mathf.CeilToInt(car.Damage.WreckSecondsLeft)}");
        }

        private static void Set(Label label, string text)
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }

        private void UpdateTopBar(CarController car)
        {
            string speed = string.Empty;
            if (car != null)
            {
                var state = car.State;
                int tier = state.DriftTier(car.Tuning);
                string drift = state.Drifting ? $"Drift {tier}/{car.Tuning.DriftTierSeconds.Length}" : string.Empty;
                string boost = state.BoostSecondsLeft > 0f ? $"BOOST +{state.BoostSpeed:0}" : string.Empty;
                // Damage shows once it's worth knowing about (design doc §4).
                float damage = car.Damage.Value;
                string hurt = car.DamageEnabled && damage >= 0.01f ? $"Damage {damage * 100f:0}%" : string.Empty;
                speed = $"{Mathf.Abs(car.ForwardSpeed) * 3.6f:0} km/h  {hurt}  {drift}  {boost}";
            }

            if (_speed.text != speed)
            {
                _speed.text = speed;
            }

            var input = _screen.PlayerInput;
            if (input != null && _auto != null)
            {
                _auto.text = input.AutoAccelerate ? "Auto-accelerate: on" : "Auto-accelerate: off";
                _haptics.text = Vibration.Enabled ? "Haptics: on" : "Haptics: off";
            }
        }

        /// <summary>The on-screen buttons (design doc §5): where <see cref="TouchControlLayout"/> puts them, lit while pressed.</summary>
        private void UpdateTouchControls()
        {
            int used = 0;
            var input = _screen.PlayerInput;
            var layout = input != null ? input.Layout : null;
            if (layout != null)
            {
                var touch = input.TouchState;
                AddButton(layout, layout.DriftCentre, "DRIFT", touch.DriftHeld, ref used);
                AddButton(layout, layout.BrakeCentre, "BRAKE", touch.BrakeHeld, ref used);
                if (!input.AutoAccelerate)
                {
                    AddButton(layout, layout.GasCentre, "GAS", touch.GasHeld, ref used);
                }

                if (_screen.ShowItemButton)
                {
                    AddButton(layout, layout.ItemCentre, _screen.ItemButtonLabel, touch.ItemHeld, ref used);
                }

                if (input.ShowLookBack)
                {
                    AddButton(layout, layout.LookBackCentre, "LOOK BACK", touch.LookBackHeld, ref used);
                }
            }

            for (int i = used; i < _buttonLabels.Count; i++)
            {
                Hide(_buttonLabels[i]);
            }
        }

        private void AddButton(TouchControlLayout layout, Vec2 centre, string text, bool held, ref int used)
        {
            // Touch coordinates are device pixels from the bottom-left; the HUD is reference pixels from the top-left.
            float scale = UiMetrics.Scale(Screen.height);
            var at = new Vector2(centre.X / scale, (Screen.height - centre.Y) / scale);
            float radius = layout.ButtonRadius / scale;
            _buttonShapes.Add((at, radius, held));

            var label = _buttonLabels[used++];
            label.style.display = DisplayStyle.Flex;
            label.style.left = at.x - radius;
            label.style.top = at.y - radius;
            label.style.width = label.style.height = 2f * radius;
            label.style.fontSize = Mathf.RoundToInt(radius * 0.32f);
            if (label.text != text)
            {
                label.text = text;
            }
        }

        /// <summary>A coloured arrow at the screen's edge for each nearby car the camera can't show, growing as it closes in.</summary>
        private void CollectRivalArrows(float width)
        {
            _rivalPositions.Clear();
            _rivalColours.Clear();
            _cars = _cars != null ? _cars : GetComponent<NetworkedCars>();
            if (_cars != null)
            {
                foreach (var remote in _cars.RemoteCars)
                {
                    if (remote.HasState)
                    {
                        _rivalPositions.Add(remote.transform.position.ToCore());
                        _rivalColours.Add(remote.Colour);
                    }
                }
            }

            _bots = _bots != null ? _bots : GetComponent<BotCars>();
            if (_bots != null)
            {
                foreach (var (position, colour) in _bots.Dots)
                {
                    _rivalPositions.Add(position.ToCore());
                    _rivalColours.Add(colour);
                }
            }

            var car = _screen.Car;
            HudIndicators.Rivals(car.BodyPosition.ToCore(), ViewHeading(), _rivalPositions, _arrows);
            foreach (var arrow in _arrows)
            {
                AddArrow(arrow.Bearing, Mathf.Lerp(ArrowMin, ArrowMax, arrow.Closeness), _rivalColours[arrow.Index], width);
            }
        }

        /// <summary>A flashing "Missile!" (steady with reduced motion) and a red arrow towards it while a Missile is chasing this phone's car.</summary>
        private void CollectMissileWarning(float width)
        {
            var session = _screen.Session;
            if (session == null || session.Items == null || !session.TryGetLocalPlayerId(out byte me))
            {
                Hide(_missile);
                return;
            }

            var position = _screen.Car.BodyPosition.ToCore();
            if (!HudIndicators.TryIncomingMissile(session.Items.Projectiles, me, position, out var missile, out _))
            {
                Hide(_missile);
                return;
            }

            // Reduced motion (design doc §6) keeps the warning up instead of flashing it.
            bool lit = Accessibility.ReducedMotion || Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.6f;
            AddArrow(HudIndicators.Bearing(position, ViewHeading(), missile.Position), ArrowMax, MissileColour, width);
            _missile.style.display = lit ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The way the camera faces: the car's heading, or the opposite during the rear-view glance, so arrows match the screen.</summary>
        private float ViewHeading() => _screen.Car.State.Heading + (_screen.CameraRig.LookingBack ? Mathf.PI : 0f);

        private void AddArrow(float bearing, float size, Color colour, float width)
        {
            Vec2 at = HudIndicators.EdgePoint(bearing, width, UiMetrics.ReferenceHeight, size / 2f + ArrowInset);
            _arrowShapes.Add((new Vector2(at.X, at.Y), size, bearing, colour));
        }

        /// <summary>The drift tier in words above the meter, so the colour isn't the only way to tell (design doc §6).</summary>
        private void UpdateDriftLabel(CarController car, float width)
        {
            var state = car.State;
            if (!state.Drifting)
            {
                Hide(_driftLabel);
                return;
            }

            int tier = state.DriftTier(car.Tuning);
            _driftLabel.style.display = DisplayStyle.Flex;
            _driftLabel.style.left = (width - MeterWidth) / 2f;
            _driftLabel.style.top = UiMetrics.ReferenceHeight - 64f - 40f;
            _driftLabel.style.width = MeterWidth;
            _driftLabel.style.height = 34f;
            string text = tier > 0 ? $"BOOST {tier}" : "DRIFT";
            if (_driftLabel.text != text)
            {
                _driftLabel.text = text;
            }
        }

        /// <summary>Draws this frame's shapes: touch buttons, the mini-map, the drift meter and the edge arrows.</summary>
        private void Draw(MeshGenerationContext context)
        {
            var paint = context.painter2D;
            foreach (var (at, radius, held) in _buttonShapes)
            {
                paint.fillColor = held ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.25f);
                paint.BeginPath();
                paint.Arc(at, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
                paint.Fill();
            }

            var car = _screen != null ? _screen.Car : null;
            if (car == null)
            {
                return;
            }

            float width = UiMetrics.Width(Screen.width, Screen.height);

            // The track's map means nothing in an arena, which has the overhead camera instead, or in the Monster Jam stadium.
            if (OnTrack)
            {
                // Opposite Look back, which moves top-right with left-handed controls (design doc §5).
                bool left = Accessibility.LeftHanded;
                DrawMiniMap(paint, new Rect(left ? 24f : width - MapSize - 24f, left ? 184f + SessionOverlay.LeftCornerDrop : 184f, MapSize, MapSize));
            }

            DrawDriftMeter(paint, car, width);
            foreach (var (at, size, bearing, colour) in _arrowShapes)
            {
                DrawArrow(paint, at, size, bearing, colour);
            }
        }

        private void DrawDriftMeter(Painter2D paint, CarController car, float width)
        {
            var state = car.State;
            if (!state.Drifting)
            {
                return;
            }

            var tuning = car.Tuning;
            float charge = state.DriftCharge(tuning);
            int tier = state.DriftTier(tuning);
            var bar = new Rect((width - MeterWidth) / 2f, UiMetrics.ReferenceHeight - 64f, MeterWidth, 18f);
            Box(paint, bar, new Color(0f, 0f, 0f, 0.5f));
            Box(paint, new Rect(bar.x, bar.y, bar.width * charge, bar.height), TierColours[Mathf.Clamp(tier, 0, TierColours.Length - 1)]);

            // A tick at each tier's threshold.
            float full = tuning.DriftTierSeconds[tuning.DriftTierSeconds.Length - 1];
            foreach (float seconds in tuning.DriftTierSeconds)
            {
                float x = bar.x + bar.width * (seconds / full);
                Box(paint, new Rect(x - 1.5f, bar.y - 4f, 3f, bar.height + 8f), Color.white);
            }
        }

        private void DrawMiniMap(Painter2D paint, Rect area)
        {
            Box(paint, area, new Color(0f, 0f, 0f, 0.35f));
            var outline = new Color(1f, 1f, 1f, 0.8f);
            foreach (var point in _map.Outline)
            {
                Dot(paint, area, point, 3f, outline);
            }

            _cars = _cars != null ? _cars : GetComponent<NetworkedCars>();
            if (_cars != null)
            {
                foreach (var remote in _cars.RemoteCars)
                {
                    if (remote.HasState)
                    {
                        Dot(paint, area, _map.ToMap(remote.transform.position.ToCore()), 10f, remote.Colour);
                    }
                }
            }

            _bots = _bots != null ? _bots : GetComponent<BotCars>();
            if (_bots != null)
            {
                foreach (var (position, colour) in _bots.Dots)
                {
                    Dot(paint, area, _map.ToMap(position.ToCore()), 10f, colour);
                }
            }

            // This phone's car on top, with a white ring so it stands out.
            Vec2 me = _map.ToMap(_screen.Car.BodyPosition.ToCore());
            Dot(paint, area, me, 14f, Color.white);
            Dot(paint, area, me, 10f, _screen.CarColour);
        }

        /// <summary>A triangle pointing at <paramref name="bearing"/> (0 is up the screen), centred on <paramref name="at"/>.</summary>
        private static void DrawArrow(Painter2D paint, Vector2 at, float size, float bearing, Color colour)
        {
            float sin = Mathf.Sin(bearing);
            float cos = Mathf.Cos(bearing);
            Vector2 Turn(float x, float y) => at + new Vector2(x * cos - y * sin, x * sin + y * cos);
            float half = size / 2f;
            paint.fillColor = colour;
            paint.BeginPath();
            paint.MoveTo(Turn(0f, -half));
            paint.LineTo(Turn(half, half));
            paint.LineTo(Turn(-half, half));
            paint.ClosePath();
            paint.Fill();
        }

        /// <summary>Map points run 0..1 from the bottom-left; the HUD runs from the top-left.</summary>
        private static void Dot(Painter2D paint, Rect area, Vec2 point, float size, Color colour)
        {
            float x = area.x + point.X * area.width;
            float y = area.y + (1f - point.Y) * area.height;
            Box(paint, new Rect(x - size / 2f, y - size / 2f, size, size), colour);
        }

        private static void Box(Painter2D paint, Rect rect, Color colour)
        {
            paint.fillColor = colour;
            paint.BeginPath();
            paint.MoveTo(new Vector2(rect.xMin, rect.yMin));
            paint.LineTo(new Vector2(rect.xMax, rect.yMin));
            paint.LineTo(new Vector2(rect.xMax, rect.yMax));
            paint.LineTo(new Vector2(rect.xMin, rect.yMax));
            paint.ClosePath();
            paint.Fill();
        }
    }
}
