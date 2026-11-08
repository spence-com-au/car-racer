using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Track;
using CarRacer.Game.Arenas;
using CarRacer.Game.Audio;
using CarRacer.Game.Driving;
using CarRacer.Game.Net;
using UnityEngine;
using CarModel = CarRacer.Core.Art.CarModel;

namespace CarRacer.Game.Screens
{
    /// <summary>
    /// The driving screen, for practice and for every event: all the built-in tracks (cars are on
    /// one at a time; only that one is drawn), the Sumo and Battle arenas in a session, the chase
    /// camera, touch/keyboard/gamepad controls and the HUD. Alone it holds one car for practice;
    /// given a <see cref="NetworkSession"/> it holds everyone in the session (see
    /// <see cref="NetworkedCars"/>), whose race flow runs the lobby, events and results on it.
    /// Everything is built from code under one root object, so destroying it cleans up the whole
    /// scene; it also destroys the session, which NGO needs on a root object of its own.
    /// </summary>
    public sealed class TrackScreen : MonoBehaviour
    {
        /// <summary>Each track's grass reaches this far beyond its road (and stays clear of the others; see <c>TrackCatalogPlacementTests</c>).</summary>
        private const float GroundMargin = 60f;

        private static readonly Color RoadColour = new Color(0.32f, 0.33f, 0.36f);
        private static readonly Color WallColour = new Color(0.85f, 0.25f, 0.2f);
        private static readonly Color GrassColour = new Color(0.36f, 0.6f, 0.3f);

        private readonly List<Object> _owned = new List<Object>();

        /// <summary>Each track's road, walls and scenery, by catalog index; only the current track's are drawn.</summary>
        private readonly List<Renderer[]> _trackRenderers = new List<Renderer[]>();
        private ICarInputSource _inputSource;
        private PlayerCarInput _playerInput;
        private NetworkSession _session;

        public CarController Car { get; private set; }
        public Color CarColour { get; private set; }
        public ChaseCamera CameraRig { get; private set; }

        /// <summary>The track cars are on: one of <see cref="TrackCatalog.Layouts"/>, all of which are built.</summary>
        public TrackLayout Track => TrackCatalog.Layouts[TrackIndex];

        public int TrackIndex { get; private set; }

        /// <summary>The hosted or joined session, if any.</summary>
        public NetworkSession Session => _session;

        /// <summary>The player's own controls; null when a test drives the car.</summary>
        public PlayerCarInput PlayerInput => _playerInput;

        /// <summary>Shows the Item button (in a race), labelled with the item held, and lets touches press it.</summary>
        public bool ShowItemButton
        {
            get => _playerInput != null && _playerInput.ShowItem;
            set
            {
                if (_playerInput != null)
                {
                    _playerInput.ShowItem = value;
                }
            }
        }

        public string ItemButtonLabel { get; set; } = string.Empty;

        /// <summary>
        /// Builds the screen. Pass <paramref name="inputOverride"/> to drive the car from a test instead
        /// of the player's devices, and <paramref name="startSession"/> to start a hosted or joined
        /// session (destroyed with the screen).
        /// </summary>
        public static TrackScreen Create(ICarInputSource inputOverride = null, System.Func<NetworkSession> startSession = null)
        {
            var screen = new GameObject("Track screen").AddComponent<TrackScreen>();
            screen.Build(inputOverride, startSession, server: false);
            return screen;
        }

        /// <summary>
        /// Builds the screen for a dedicated host (a server, design doc §2.3): the tracks, arenas and
        /// everyone's cars, which the host needs for bots, item hits and standings, but no car,
        /// camera, controls or HUD of its own.
        /// </summary>
        public static TrackScreen CreateServer(System.Func<NetworkSession> startSession)
        {
            var screen = new GameObject("Track screen").AddComponent<TrackScreen>();
            screen.Build(null, startSession, server: true);
            return screen;
        }

        private void Build(ICarInputSource inputOverride, System.Func<NetworkSession> startSession, bool server)
        {
            // Fixed 50 Hz physics on every device (design doc §4).
            Time.fixedDeltaTime = 0.02f;

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.SetParent(transform, false);
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            foreach (var layout in TrackCatalog.Layouts)
            {
                BuildTrack(layout);
            }

            DrawOnlyCurrentTrack();
            if (server)
            {
                StartSession(startSession);
                return;
            }

            if (inputOverride != null)
            {
                _inputSource = inputOverride;
            }
            else
            {
                _playerInput = gameObject.AddComponent<PlayerCarInput>();
                _inputSource = _playerInput;
            }

            var cameraObject = new GameObject("Chase camera");
            cameraObject.transform.SetParent(transform, false);
            var unityCamera = cameraObject.AddComponent<Camera>();
            unityCamera.tag = "MainCamera";
            unityCamera.clearFlags = CameraClearFlags.SolidColor;
            unityCamera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            unityCamera.nearClipPlane = 0.3f;
            unityCamera.farClipPlane = 600f;
            cameraObject.AddComponent<AudioListener>();
            CameraRig = cameraObject.AddComponent<ChaseCamera>();
            CameraRig.LookBackInput = _playerInput;

            // Until this phone's car exists (a guest waiting to join), look over the track.
            cameraObject.transform.position = new Vector3(45f, 70f, -70f);
            cameraObject.transform.LookAt(new Vector3(45f, 0f, 30f));

            gameObject.AddComponent<RaceHud>().Initialise(this);
            DrivingTipsPanel.ShowIfNew(this, _playerInput);
            gameObject.AddComponent<MusicDirector>().Initialise(this);
            if (startSession == null)
            {
                // Practice: the car from the garage, which takes damage as in a race (design doc §4).
                SpawnLocalCar(0, CarColours.ForIndex(Garage.Colour), Garage.Model);
                Car.DamageEnabled = true;
            }
            else
            {
                StartSession(startSession);
            }
        }

        private void StartSession(System.Func<NetworkSession> startSession)
        {
            _session = startSession();
            ArenaVenues.Build(transform, _session);
            gameObject.AddComponent<NetworkedCars>().Initialise(this, _session);
        }

        /// <summary>Builds a track's road, walls and its own patch of grass, reaching <see cref="GroundMargin"/> beyond the road.</summary>
        private void BuildTrack(TrackLayout layout)
        {
            string name = layout.Definition.DisplayName;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var sample in layout.Spline.SampleEvenly(4f))
            {
                minX = Mathf.Min(minX, sample.Position.X);
                maxX = Mathf.Max(maxX, sample.Position.X);
                minZ = Mathf.Min(minZ, sample.Position.Z);
                maxZ = Mathf.Max(maxZ, sample.Position.Z);
            }

            // Unity's plane is 10 m square.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = name + " ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localPosition = new Vector3((minX + maxX) / 2f, -0.05f, (minZ + maxZ) / 2f);
            ground.transform.localScale = new Vector3((maxX - minX + 2f * GroundMargin) / 10f, 1f, (maxZ - minZ + 2f * GroundMargin) / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = SimpleMaterials.Get(GrassColour);

            // The road, walls and scenery go under one object so they can be hidden together; the
            // grass stays drawn so the world doesn't end at this track's edge.
            var drawn = new GameObject(name).transform;
            drawn.SetParent(transform, false);
            var meshes = RoadMeshBuilder.Build(layout.Spline, layout.Definition.WallHeight);
            AddSurface(drawn, name + " road", meshes.Road, RoadColour);
            AddSurface(drawn, name + " left wall", meshes.LeftWall, WallColour);
            AddSurface(drawn, name + " right wall", meshes.RightWall, WallColour);
            var scenery = new List<Mesh>();
            TrackDressing.Build(drawn, layout, name, scenery);
            _owned.AddRange(scenery);
            _trackRenderers.Add(drawn.GetComponentsInChildren<Renderer>());
        }

        /// <summary>
        /// Every track is built, but with a 600 m view the others would all be drawn too (design doc
        /// §14). Only the track cars are on is drawn; the others keep their colliders, so nothing that
        /// is still on one falls through.
        /// </summary>
        private void DrawOnlyCurrentTrack()
        {
            for (int i = 0; i < _trackRenderers.Count; i++)
            {
                foreach (var renderer in _trackRenderers[i])
                {
                    renderer.enabled = i == TrackIndex;
                }
            }
        }

        /// <summary>
        /// Moves racing to track <paramref name="index"/> (clamped to the catalog). Cars aren't moved
        /// here; they go to the new track's grid when they line up. Returns true if the track changed.
        /// </summary>
        public bool UseTrack(int index)
        {
            index = TrackCatalog.Clamp(index);
            if (index == TrackIndex)
            {
                return false;
            }

            TrackIndex = index;
            DrawOnlyCurrentTrack();
            return true;
        }

        /// <summary>Switches to the track of the race under way, if any; called by everything that lines cars up, so whichever runs first wins.</summary>
        public void FollowEventTrack(IRaceView race)
        {
            if (race != null && race.Phase != RacePhase.Lobby && race.Mode == GameMode.Race)
            {
                UseTrack(race.TrackIndex);
            }
        }

        /// <summary>Creates this phone's car (<paramref name="body"/>, or the race car) on grid slot <paramref name="slot"/> and points the camera at it.</summary>
        public void SpawnLocalCar(int slot, Color colour, CarModel body = null)
        {
            if (Car != null)
            {
                Destroy(Car.gameObject);
            }

            Car = CarFactory.Build("Car", colour, transform, body: body);
            CarColour = colour;
            Car.InputSource = _inputSource;
            CameraRig.Target = Car;
            PlaceLocalCarOnGrid(slot);
        }

        /// <summary>Gives this phone's car another body or colour where it stands (picking a car in the lobby), or makes it a monster truck (Monster Jam).</summary>
        public void ReplaceLocalCarBody(Color colour, CarModel body, bool monster = false)
        {
            if (Car == null)
            {
                return;
            }

            CarFactory.ReplaceBody(Car.gameObject, colour, body, monster);
            CarColour = colour;
        }

        /// <summary>Puts this phone's car back at rest on grid slot <paramref name="slot"/>, e.g. for a race start.</summary>
        public void PlaceLocalCarOnGrid(int slot) => PlaceLocalCarAtStart(slot, GameMode.Race);

        /// <summary>
        /// Puts this phone's car at rest on its starting spot for <paramref name="mode"/>: grid slot
        /// <paramref name="slot"/> on the track, or spot <paramref name="slot"/> round an arena, and
        /// switches to the overhead camera in Sumo and Battle (design doc §6). Monster Jam keeps the
        /// chase camera, to see the jumps.
        /// </summary>
        public void PlaceLocalCarAtStart(int slot, GameMode mode)
        {
            PlaceAtStart(Car, slot, mode);
            CameraRig.ArenaCentre = mode == GameMode.Race || mode == GameMode.Monster ? (Vector3?)null : ArenaLayout.CentreFor(mode).ToUnity();
            CameraRig.Snap();
        }

        /// <summary>Puts <paramref name="car"/> at rest on starting spot <paramref name="slot"/> for <paramref name="mode"/>.</summary>
        public void PlaceAtStart(CarController car, int slot, GameMode mode)
        {
            Vec3 position;
            Vec3 forward;
            if (mode == GameMode.Race)
            {
                var grid = Track.StartGrid(slot + 1)[slot];
                position = grid.Position;
                forward = grid.Forward;
            }
            else
            {
                var spot = ArenaLayout.StartSlotFor(mode, slot);
                position = spot.Position;
                forward = spot.Forward;
            }

            car.Place(position.ToUnity() + Vector3.up * (CarController.SphereRadius + 0.1f), Mathf.Atan2(forward.X, forward.Z));
        }

        private void AddSurface(Transform parent, string name, MeshData data, Color colour)
        {
            var mesh = data.ToUnityMesh(name);
            _owned.Add(mesh);
            var surface = new GameObject(name);
            surface.transform.SetParent(parent, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial = SimpleMaterials.Get(colour);
            surface.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            foreach (var owned in _owned)
            {
                Destroy(owned);
            }

            if (_session != null)
            {
                Destroy(_session.gameObject);
            }
        }
    }
}
