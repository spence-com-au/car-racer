namespace CarRacer.Core.Track
{
    /// <summary>
    /// The built-in tracks (design doc §13): an easy oval, a technical track with a jump, a
    /// figure-eight, fast seaside sweepers, a hill climb with switchbacks, a desert run with two
    /// jumps, a banked carousel with infield esses, and a long ridge with a high hairpin.
    /// </summary>
    public static class SampleTracks
    {
        /// <summary>Every built-in track, in the order the lobby offers them.</summary>
        public static TrackDefinition[] All() => new[] { Oval(), Canyon(), FigureEight(), Sweepers(), Switchbacks(), Dunes(), Carousel(), Ridge() };

        /// <summary>A gentle rounded-rectangle circuit (about 386 m), driven clockwise when viewed from above.</summary>
        public static TrackDefinition Oval() => new TrackDefinition
        {
            Id = "oval",
            DisplayName = "Pebble Oval",
            CheckpointCount = 8,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.3f, 0.75f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f),
                new TrackPoint(0f, 0f, 60f),
                new TrackPoint(15f, 0f, 90f, 16f, 8f),
                new TrackPoint(45f, 0f, 100f, 16f, 8f),
                new TrackPoint(75f, 0f, 90f, 16f, 8f),
                new TrackPoint(90f, 0f, 60f),
                new TrackPoint(90f, 0f, 0f),
                new TrackPoint(75f, 0f, -30f, 16f, 8f),
                new TrackPoint(45f, 0f, -40f, 16f, 8f),
                new TrackPoint(15f, 0f, -30f, 16f, 8f),
            },
        };

        /// <summary>
        /// A technical circuit (about 590 m): a jump over a crest on the opening straight, a tight
        /// right-hand hairpin, a chicane, a fast sweeper and a second hairpin back to the line.
        /// </summary>
        public static TrackDefinition Canyon() => new TrackDefinition
        {
            Id = "canyon",
            DisplayName = "Canyon Hairpins",
            CheckpointCount = 10,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.35f, 0.7f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f),
                new TrackPoint(0f, 0f, 40f),
                new TrackPoint(0f, 2.6f, 62f, 12f),
                new TrackPoint(0f, 0f, 82f, 12f),
                new TrackPoint(0f, 0f, 106f),
                new TrackPoint(15f, 0f, 130f, 14f, 6f),
                new TrackPoint(45f, 0f, 138f, 14f, 8f),
                new TrackPoint(68f, 0f, 118f, 14f, 8f),
                new TrackPoint(70f, 0f, 90f),
                new TrackPoint(88f, 0f, 66f, 12f),
                new TrackPoint(90f, 0f, 40f, 12f),
                new TrackPoint(110f, 0f, 20f),
                new TrackPoint(140f, 0f, 0f, 16f, 6f),
                new TrackPoint(150f, 0f, -35f, 16f, 6f),
                new TrackPoint(130f, 0f, -65f, 14f, 8f),
                new TrackPoint(95f, 0f, -65f, 14f, 8f),
                new TrackPoint(60f, 0f, -45f),
                new TrackPoint(30f, 0f, -48f, 14f, 6f),
                new TrackPoint(6f, 0f, -32f, 14f, 6f),
            },
        };

        /// <summary>
        /// A figure-eight (about 700 m) with a crossover: the line crosses the middle on the ground,
        /// then the road climbs round the right-hand loop and crosses back over on a bridge 8 m up.
        /// </summary>
        public static TrackDefinition FigureEight() => new TrackDefinition
        {
            Id = "figure-eight",
            DisplayName = "Crossover Eight",
            CheckpointCount = 10,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.2f, 0.7f },
            Points = new[]
            {
                new TrackPoint(-35f, 0f, -35f),
                new TrackPoint(35f, 0f, 35f),
                new TrackPoint(75f, 0f, 55f, 16f, 6f),
                new TrackPoint(120f, 2f, 20f, 16f, 6f),
                new TrackPoint(115f, 5f, -30f, 16f, 6f),
                new TrackPoint(75f, 7.5f, -55f, 16f, 6f),
                new TrackPoint(35f, 8f, -35f),
                new TrackPoint(-35f, 8f, 35f),
                new TrackPoint(-75f, 7.5f, 55f, 16f, -6f),
                new TrackPoint(-115f, 5f, 30f, 16f, -6f),
                new TrackPoint(-120f, 2f, -20f, 16f, -6f),
                new TrackPoint(-75f, 0f, -55f, 16f, -6f),
            },
        };

        /// <summary>
        /// A fast, flowing circuit (about 500 m): a long front straight into a banked right-hander,
        /// a run of left-right sweepers along the back, and a wide hairpin home. Wide and flat, so
        /// it rewards holding a drift through the bends.
        /// </summary>
        public static TrackDefinition Sweepers() => new TrackDefinition
        {
            Id = "sweepers",
            DisplayName = "Seaside Sweepers",
            CheckpointCount = 10,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.3f, 0.65f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f, 16f),
                new TrackPoint(0f, 0f, 70f, 16f),
                new TrackPoint(12f, 0f, 105f, 16f, 8f),
                new TrackPoint(45f, 0f, 120f, 16f, 8f),
                new TrackPoint(78f, 0f, 108f, 16f, 6f),
                new TrackPoint(95f, 0f, 80f, 15f, -4f),
                new TrackPoint(120f, 0f, 58f, 15f, 6f),
                new TrackPoint(128f, 0f, 30f, 15f, 4f),
                new TrackPoint(112f, 0f, 5f, 15f, -6f),
                new TrackPoint(118f, 0f, -22f, 15f, 4f),
                new TrackPoint(100f, 0f, -48f, 16f, 6f),
                new TrackPoint(65f, 0f, -55f, 16f, 8f),
                new TrackPoint(30f, 0f, -48f, 16f, 8f),
                new TrackPoint(6f, 0f, -28f, 16f, 6f),
            },
        };

        /// <summary>
        /// A hill climb (about 480 m): up a long ramp, round a tight switchback near the top,
        /// along the ridge 9 m up, and down the far side through a long, sweeping bend.
        /// </summary>
        public static TrackDefinition Switchbacks() => new TrackDefinition
        {
            Id = "switchbacks",
            DisplayName = "Switchback Hill",
            CheckpointCount = 10,
            WallHeight = 1.4f,
            ItemBoxRows = new[] { 0.25f, 0.7f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f, 14f),
                new TrackPoint(0f, 0f, 40f, 14f),
                new TrackPoint(0f, 3f, 70f, 13f),
                new TrackPoint(0f, 6f, 100f, 13f),
                new TrackPoint(14f, 7.5f, 124f, 13f, 6f),
                new TrackPoint(40f, 8f, 128f, 13f, 8f),
                new TrackPoint(55f, 8.5f, 108f, 13f, 6f),
                new TrackPoint(48f, 9f, 82f, 13f, -6f),
                new TrackPoint(54f, 9f, 60f, 13f, -6f),
                new TrackPoint(80f, 9f, 46f, 13f, 6f),
                new TrackPoint(100f, 7f, 30f, 13f, 6f),
                new TrackPoint(100f, 4f, 0f, 13f),
                new TrackPoint(95f, 1f, -28f, 13f, 6f),
                new TrackPoint(72f, 0f, -45f, 14f, 8f),
                new TrackPoint(40f, 0f, -48f, 14f, 8f),
                new TrackPoint(12f, 0f, -32f, 14f, 6f),
            },
        };

        /// <summary>
        /// A wide desert run (about 500 m): a crest to jump on each long straight, joined by two
        /// big, open bends that are made for drifting.
        /// </summary>
        public static TrackDefinition Dunes() => new TrackDefinition
        {
            Id = "dunes",
            DisplayName = "Dune Jumps",
            CheckpointCount = 10,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.3f, 0.7f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f, 16f),
                new TrackPoint(0f, 0f, 40f, 16f),
                new TrackPoint(0f, 2.6f, 62f, 14f),
                new TrackPoint(0f, 0f, 84f, 14f),
                new TrackPoint(0f, 0f, 110f, 16f),
                new TrackPoint(15f, 0f, 142f, 17f, 8f),
                new TrackPoint(50f, 0f, 156f, 17f, 8f),
                new TrackPoint(85f, 0f, 142f, 17f, 8f),
                new TrackPoint(100f, 0f, 110f, 16f),
                new TrackPoint(100f, 0f, 84f, 14f),
                new TrackPoint(100f, 2.6f, 62f, 14f),
                new TrackPoint(100f, 0f, 40f, 14f),
                new TrackPoint(100f, 0f, 10f, 16f),
                new TrackPoint(85f, 0f, -22f, 17f, 8f),
                new TrackPoint(50f, 0f, -36f, 17f, 8f),
                new TrackPoint(15f, 0f, -22f, 17f, 8f),
            },
        };

        /// <summary>
        /// A banked carousel (about 440 m): a long, steeply banked loop to hold flat out, then back
        /// through the infield on a run of tighter left-right esses.
        /// </summary>
        public static TrackDefinition Carousel() => new TrackDefinition
        {
            Id = "carousel",
            DisplayName = "Carousel Ring",
            CheckpointCount = 10,
            WallHeight = 1.2f,
            ItemBoxRows = new[] { 0.25f, 0.7f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f, 15f),
                new TrackPoint(0f, 0f, 45f, 15f),
                new TrackPoint(8f, 0f, 76f, 15f, 8f),
                new TrackPoint(30f, 0f, 97f, 16f, 12f),
                new TrackPoint(62f, 0f, 104f, 16f, 12f),
                new TrackPoint(93f, 0f, 93f, 16f, 12f),
                new TrackPoint(111f, 0f, 68f, 16f, 12f),
                new TrackPoint(113f, 0f, 38f, 15f, 8f),
                new TrackPoint(99f, 0f, 14f, 14f, -5f),
                new TrackPoint(104f, 0f, -12f, 14f, 5f),
                new TrackPoint(88f, 0f, -36f, 14f, 6f),
                new TrackPoint(56f, 0f, -47f, 15f, 8f),
                new TrackPoint(26f, 0f, -42f, 15f, 8f),
                new TrackPoint(6f, 0f, -24f, 15f, 6f),
            },
        };

        /// <summary>
        /// A long ridge (about 565 m): a steady climb to a tight hairpin 9 m up, along the top, then
        /// down a run of esses to a fast sweeping finish.
        /// </summary>
        public static TrackDefinition Ridge() => new TrackDefinition
        {
            Id = "ridge",
            DisplayName = "Ridge Run",
            CheckpointCount = 12,
            WallHeight = 1.4f,
            ItemBoxRows = new[] { 0.3f, 0.75f },
            Points = new[]
            {
                new TrackPoint(0f, 0f, 0f, 14f),
                new TrackPoint(0f, 0f, 40f, 14f),
                new TrackPoint(0f, 3f, 70f, 13f),
                new TrackPoint(0f, 6f, 100f, 13f),
                new TrackPoint(0f, 8f, 125f, 13f),
                new TrackPoint(12f, 9f, 150f, 13f, 6f),
                new TrackPoint(40f, 9f, 160f, 13f, 8f),
                new TrackPoint(65f, 9f, 145f, 13f, 6f),
                new TrackPoint(70f, 9f, 118f, 13f),
                new TrackPoint(70f, 8f, 90f, 13f),
                new TrackPoint(85f, 7f, 62f, 13f, -5f),
                new TrackPoint(80f, 6f, 35f, 13f, 5f),
                new TrackPoint(95f, 4f, 10f, 13f, -5f),
                new TrackPoint(110f, 2f, -15f, 14f, 6f),
                new TrackPoint(105f, 0f, -45f, 14f, 8f),
                new TrackPoint(80f, 0f, -62f, 14f, 8f),
                new TrackPoint(50f, 0f, -62f, 14f, 6f),
                new TrackPoint(25f, 0f, -48f, 14f, 6f),
                new TrackPoint(6f, 0f, -26f, 14f, 6f),
            },
        };
    }
}
