using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Numerics;
using CarRacer.Core.Platform;
using CarRacer.Core.Track;

namespace CarRacer.Core.Viewer
{
    /// <summary>
    /// Draws a <see cref="ViewerScene"/> as one 1280×720 frame of the TV viewer (design doc §2.4):
    /// a top-down map of the track or arena with every car as a numbered dot, the standings beside
    /// it, a big countdown, and the results; in the lobby, a QR code to join and who's in. Each
    /// track's map is drawn once and kept. Not thread-safe: use one renderer per thread.
    /// </summary>
    public sealed class ViewerRenderer
    {
        public const int Width = 1280;
        public const int Height = 720;

        private const int Margin = 24;
        private const int TopBar = 84;
        private const int MapSize = Height - TopBar - 2 * Margin + 8;
        private const int MapX = Margin;
        private const int MapY = TopBar + Margin - 4;
        private const int PanelX = MapX + MapSize + Margin;
        private const int PanelWidth = Width - PanelX - Margin;
        private const int RowHeight = 62;
        private const int RowGap = 6;
        private const float CarRadius = 13f;

        public static readonly Rgb Background = new Rgb(18, 22, 38);
        public static readonly Rgb Panel = new Rgb(30, 36, 58);
        public static readonly Rgb Accent = new Rgb(245, 197, 24);
        public static readonly Rgb Text = new Rgb(240, 240, 240);
        public static readonly Rgb DimText = new Rgb(138, 144, 166);
        private static readonly Rgb Grass = new Rgb(47, 90, 44);
        private static readonly Rgb Road = new Rgb(92, 94, 101);
        private static readonly Rgb RoadEdge = new Rgb(34, 35, 40);
        private static readonly Rgb Void = new Rgb(14, 26, 51);
        private static readonly Rgb Platform = new Rgb(107, 114, 128);
        private static readonly Rgb BowlFloor = new Rgb(74, 79, 92);
        private static readonly Rgb BowlWall = new Rgb(192, 57, 43);
        private static readonly Rgb Dirt = new Rgb(122, 90, 58);
        private static readonly Rgb DirtWall = new Rgb(58, 42, 26);
        private static readonly Rgb RampColour = new Rgb(170, 130, 85);
        private static readonly Rgb Outline = new Rgb(12, 12, 16);

        private readonly Dictionary<int, RgbImage> _maps = new Dictionary<int, RgbImage>();
        private readonly Dictionary<string, QrCode> _qrCodes = new Dictionary<string, QrCode>();

        /// <summary>Draws <paramref name="scene"/> into <paramref name="frame"/>, which must be <see cref="Width"/> × <see cref="Height"/>.</summary>
        public void Render(ViewerScene scene, RgbImage frame)
        {
            if (frame.Width != Width || frame.Height != Height)
            {
                throw new ArgumentException($"Viewer frames are {Width}×{Height}", nameof(frame));
            }

            frame.Fill(Background);
            DrawTopBar(scene, frame);
            switch (scene.Phase)
            {
                case ViewerPhase.Lobby:
                    DrawJoinCard(scene, frame);
                    DrawRows(scene.Rows, frame, PanelX, PanelWidth, showPlace: false);
                    break;
                case ViewerPhase.Results:
                    DrawRows(scene.Rows, frame, Width / 2 - 420, 840, showPlace: true);
                    break;
                default:
                    DrawMap(scene, frame);
                    DrawRows(scene.Rows, frame, PanelX, PanelWidth, showPlace: scene.Rows.Count > 0 && scene.Rows[0].Place > 0);
                    if (scene.Countdown != null)
                    {
                        DrawCentred(frame, scene.Countdown, MapX + MapSize / 2, MapY + MapSize / 2, 22, Accent);
                    }

                    break;
            }

            if (!string.IsNullOrEmpty(scene.Footer))
            {
                int x = scene.Phase == ViewerPhase.Results ? Width / 2 - 420 : PanelX;
                int width = scene.Phase == ViewerPhase.Results ? 840 : PanelWidth;
                PixelFont.Draw(frame, x, Height - Margin - 21, PixelFont.Fit(scene.Footer, 3, width), 3, DimText);
            }
        }

        private static void DrawTopBar(ViewerScene scene, RgbImage frame)
        {
            frame.FillRect(0, 0, Width, TopBar, Panel);
            frame.FillRect(0, TopBar - 4, Width, 4, Accent);
            int clockWidth = PixelFont.Measure(scene.Clock, 6);
            PixelFont.Draw(frame, Width - Margin - clockWidth, 18, scene.Clock, 6, Accent);
            string title = PixelFont.Fit(scene.Title, 6, Width - 3 * Margin - clockWidth);
            PixelFont.Draw(frame, Margin, 18, title, 6, Text);
        }

        private static void DrawRows(List<ViewerRow> rows, RgbImage frame, int x, int width, bool showPlace)
        {
            int y = MapY;
            int maxRows = (Height - MapY - Margin - 36) / (RowHeight + RowGap);
            for (int i = 0; i < rows.Count && i < maxRows; i++)
            {
                var row = rows[i];
                frame.FillRect(x, y, width, RowHeight, Panel);
                int cursor = x + 16;
                if (showPlace)
                {
                    string place = row.Place > 0 ? row.Place.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
                    PixelFont.Draw(frame, cursor, y + (RowHeight - 35) / 2, place, 5, row.Place == 1 ? Accent : Text);
                    cursor += 70;
                }

                frame.FillCircle(cursor + 16, y + RowHeight / 2f, 18f, Outline);
                frame.FillCircle(cursor + 16, y + RowHeight / 2f, 15f, row.Colour);
                cursor += 50;
                var nameColour = row.Dim ? DimText : Text;
                int detailWidth = PixelFont.Measure(row.Detail, 4);
                int detailX = x + width - 16 - detailWidth;
                PixelFont.Draw(frame, detailX, y + (RowHeight - 28) / 2, row.Detail, 4, row.Dim ? DimText : Accent);
                string name = PixelFont.Fit(row.Name, 4, detailX - cursor - 16);
                PixelFont.Draw(frame, cursor, y + (RowHeight - 28) / 2, name, 4, nameColour);
                y += RowHeight + RowGap;
            }
        }

        private void DrawJoinCard(ViewerScene scene, RgbImage frame)
        {
            frame.FillRect(MapX, MapY, MapSize, MapSize, Panel);
            if (string.IsNullOrEmpty(scene.JoinLink))
            {
                DrawCentred(frame, "Find this game", MapX + MapSize / 2, MapY + MapSize / 2 - 30, 4, Text);
                DrawCentred(frame, "under Join Game", MapX + MapSize / 2, MapY + MapSize / 2 + 30, 4, Text);
                return;
            }

            if (!_qrCodes.TryGetValue(scene.JoinLink, out var qr))
            {
                qr = QrCode.Encode(scene.JoinLink);
                _qrCodes[scene.JoinLink] = qr;
            }

            // A white card with the code and its four-module quiet zone.
            int module = Math.Max(1, 400 / (qr.Size + 8));
            int card = module * (qr.Size + 8);
            int left = MapX + (MapSize - card) / 2;
            int top = MapY + 70;
            frame.FillRect(left, top, card, card, new Rgb(255, 255, 255));
            for (int y = 0; y < qr.Size; y++)
            {
                for (int x = 0; x < qr.Size; x++)
                {
                    if (qr.IsDark(x, y))
                    {
                        frame.FillRect(left + (x + 4) * module, top + (y + 4) * module, module, module, new Rgb(0, 0, 0));
                    }
                }
            }

            DrawCentred(frame, "Scan to join", MapX + MapSize / 2, MapY + 38, 4, Accent);
            DrawCentred(frame, PixelFont.Fit(scene.JoinText, 4, MapSize - 32), MapX + MapSize / 2, top + card + 46, 4, Text);
        }

        private void DrawMap(ViewerScene scene, RgbImage frame)
        {
            var map = MapFor(scene.Mode, scene.TrackIndex);
            for (int y = 0; y < MapSize; y++)
            {
                Buffer.BlockCopy(map.Pixels, y * MapSize * 3, frame.Pixels, ((MapY + y) * Width + MapX) * 3, MapSize * 3);
            }

            var view = ViewFor(scene.Mode, scene.TrackIndex);
            if (scene.Mode == GameMode.Sumo)
            {
                var (cx, cy) = view.ToPixel(ArenaLayout.SumoCentre);
                float r = scene.PlatformRadius * view.Scale;
                frame.FillCircle(MapX + cx, MapY + cy, r + 4f, Accent);
                frame.FillCircle(MapX + cx, MapY + cy, r, Platform);
            }

            // Back markers first, so the leader's dot is on top.
            var cars = new List<ViewerCar>(scene.Cars);
            cars.Sort((a, b) => (b.Place == 0 ? int.MaxValue : b.Place).CompareTo(a.Place == 0 ? int.MaxValue : a.Place));
            foreach (var car in cars)
            {
                var (px, py) = view.ToPixel(car.Position);
                if (px < -CarRadius || py < -CarRadius || px > MapSize + CarRadius || py > MapSize + CarRadius)
                {
                    continue;
                }

                float x = MapX + Math.Max(0f, Math.Min(MapSize, px));
                float y = MapY + Math.Max(0f, Math.Min(MapSize, py));
                frame.FillCircle(x, y, CarRadius + 3f, Outline);
                frame.FillCircle(x, y, CarRadius, car.Colour);
                if (car.Place > 0 && car.Place < 10)
                {
                    var label = car.Colour.Luma > 0.5f ? Outline : Text;
                    PixelFont.Draw(frame, (int)Math.Round(x) - 5, (int)Math.Round(y) - 7, car.Place.ToString(System.Globalization.CultureInfo.InvariantCulture), 2, label);
                }
            }
        }

        /// <summary>The map of a track or arena, without cars (and without Sumo's platform, which shrinks).</summary>
        private RgbImage MapFor(GameMode mode, int trackIndex)
        {
            int key = mode == GameMode.Race ? trackIndex : -1 - (int)mode;
            if (_maps.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var map = new RgbImage(MapSize, MapSize);
            var view = ViewFor(mode, trackIndex);
            switch (mode)
            {
                case GameMode.Sumo:
                    map.Fill(Void);
                    break;
                case GameMode.Battle:
                    DrawArenaFloor(map, view, ArenaLayout.BattleCentre, BattleRules.BowlRadius, BowlFloor, BowlWall);
                    break;
                case GameMode.Monster:
                    DrawArenaFloor(map, view, ArenaLayout.MonsterCentre, MonsterRules.StadiumRadius, Dirt, DirtWall);
                    foreach (var ramp in ArenaLayout.MonsterRamps(ArenaLayout.MonsterCentre))
                    {
                        var (ax, ay) = view.ToPixel(ramp.Foot);
                        var (bx, by) = view.ToPixel(ramp.Foot + ramp.Forward * ramp.Length);
                        map.DrawLine(ax, ay, bx, by, ramp.Width * view.Scale, RampColour);
                    }

                    break;
                default:
                    DrawTrack(map, view, TrackCatalog.Layouts[TrackCatalog.Clamp(trackIndex)]);
                    break;
            }

            _maps[key] = map;
            return map;
        }

        private static void DrawArenaFloor(RgbImage map, MapView view, Vec3 centre, float radius, Rgb floor, Rgb wall)
        {
            map.Fill(Grass);
            var (cx, cy) = view.ToPixel(centre);
            map.FillCircle(cx, cy, radius * view.Scale + 6f, wall);
            map.FillCircle(cx, cy, radius * view.Scale, floor);
        }

        /// <summary>
        /// The road from above, lowest stretches first, so a bridge is drawn over the road it
        /// crosses. A stretch's dark edge is only drawn over grass or road well below it, so the
        /// road doesn't show seams where it climbs.
        /// </summary>
        private static void DrawTrack(RgbImage map, MapView view, TrackLayout layout)
        {
            map.Fill(Grass);
            var heights = new float[MapSize * MapSize];
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = float.NegativeInfinity;
            }

            var samples = layout.Spline.SampleEvenly(1.5f);
            samples.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));
            float edge = Math.Max(2f, 0.9f * view.Scale);
            foreach (var sample in samples)
            {
                var (x, y) = view.ToPixel(sample.Position);
                float radius = Math.Max(2f, sample.Width / 2f * view.Scale);
                Stamp(map, heights, x, y, radius + edge, RoadEdge, sample.Position.Y, edgeOnly: true);
                Stamp(map, heights, x, y, radius, Road, sample.Position.Y, edgeOnly: false);
            }

            // The start line, chequered across the road.
            var start = layout.Spline.SampleAtDistance(0f);
            int squares = 8;
            for (int i = 0; i < squares; i++)
            {
                var a = start.Position + start.Right * (start.Width * (i / (float)squares - 0.5f));
                var b = start.Position + start.Right * (start.Width * ((i + 1) / (float)squares - 0.5f));
                var (ax, ay) = view.ToPixel(a);
                var (bx, by) = view.ToPixel(b);
                map.DrawLine(ax, ay, bx, by, Math.Max(3f, 1.2f * view.Scale), i % 2 == 0 ? new Rgb(250, 250, 250) : Outline);
            }
        }

        private static void Stamp(RgbImage map, float[] heights, float cx, float cy, float radius, Rgb colour, float height, bool edgeOnly)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius - 1f)), x1 = Math.Min(MapSize - 1, (int)Math.Ceiling(cx + radius + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius - 1f)), y1 = Math.Min(MapSize - 1, (int)Math.Ceiling(cy + radius + 1f));
            for (int y = y0; y <= y1; y++)
            {
                float dy = y + 0.5f - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float coverage = radius + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy);
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    int i = y * MapSize + x;
                    if (edgeOnly)
                    {
                        if (heights[i] < height - 2.5f)
                        {
                            map.Blend(x, y, colour, coverage);
                        }
                    }
                    else
                    {
                        map.Blend(x, y, colour, coverage);
                        if (coverage >= 0.5f)
                        {
                            heights[i] = height;
                        }
                    }
                }
            }
        }

        private static MapView ViewFor(GameMode mode, int trackIndex)
        {
            if (mode != GameMode.Race)
            {
                float radius = ArenaLayout.StartRadiusFor(mode);
                var centre = ArenaLayout.CentreFor(mode);
                return MapView.Fit(centre.X - radius, centre.X + radius, centre.Z - radius, centre.Z + radius);
            }

            var layout = TrackCatalog.Layouts[TrackCatalog.Clamp(trackIndex)];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in layout.Spline.SampleEvenly(4f))
            {
                float half = s.Width / 2f;
                minX = Math.Min(minX, s.Position.X - half);
                maxX = Math.Max(maxX, s.Position.X + half);
                minZ = Math.Min(minZ, s.Position.Z - half);
                maxZ = Math.Max(maxZ, s.Position.Z + half);
            }

            return MapView.Fit(minX, maxX, minZ, maxZ);
        }

        private static void DrawCentred(RgbImage frame, string text, int cx, int cy, int scale, Rgb colour)
        {
            int width = PixelFont.Measure(text, scale);
            int x = cx - width / 2;
            int y = cy - PixelFont.GlyphHeight * scale / 2;
            int shadow = Math.Max(1, scale / 3);
            PixelFont.Draw(frame, x + shadow, y + shadow, text, scale, Outline);
            PixelFont.Draw(frame, x, y, text, scale, colour);
        }

        /// <summary>World X right and Z up, onto the square map with a margin, the same scale both ways.</summary>
        private readonly struct MapView
        {
            private const float Border = 24f;

            private readonly float _minX;
            private readonly float _maxZ;
            private readonly float _offsetX;
            private readonly float _offsetY;

            private MapView(float minX, float maxZ, float scale, float offsetX, float offsetY)
            {
                _minX = minX;
                _maxZ = maxZ;
                Scale = scale;
                _offsetX = offsetX;
                _offsetY = offsetY;
            }

            /// <summary>Pixels per metre.</summary>
            public float Scale { get; }

            public static MapView Fit(float minX, float maxX, float minZ, float maxZ)
            {
                float span = Math.Max(Math.Max(maxX - minX, maxZ - minZ), 1f);
                float scale = (MapSize - 2f * Border) / span;
                return new MapView(minX, maxZ, scale, Border + (span - (maxX - minX)) * scale / 2f, Border + (span - (maxZ - minZ)) * scale / 2f);
            }

            public (float X, float Y) ToPixel(Vec3 world) => (_offsetX + (world.X - _minX) * Scale, _offsetY + (_maxZ - world.Z) * Scale);
        }
    }
}
