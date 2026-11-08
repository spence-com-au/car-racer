using CarRacer.Core.Art;
using CarRacer.Core.Audio;
using CarRacer.Game.Audio;
using CarRacer.Game.Net;
using UnityEngine;

namespace CarRacer.Game.Driving
{
    /// <summary>
    /// Builds a car from code: the physics sphere plus the player's Kenney body in their colour
    /// (design doc §11, §13), or a placeholder made of primitives if the model can't be loaded.
    /// Each body's handling ratings (<see cref="CarModel.Stats"/>, design doc §4) set the car's
    /// top speed, acceleration and turn rate.
    /// </summary>
    public static class CarFactory
    {
        private static PhysicsMaterial _frictionless;

        /// <param name="haptics">Whether bumps and boosts vibrate the phone and sound up close: yes for this phone's own car, no for bots the host drives.</param>
        /// <param name="body">The garage body (design doc §11); the race car if not given.</param>
        public static CarController Build(string name, Color colour, Transform parent, bool haptics = true, CarModel body = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var sphereBody = root.AddComponent<Rigidbody>();
            sphereBody.mass = 1f;
            sphereBody.linearDamping = 0f;
            sphereBody.angularDamping = 0f;
            sphereBody.constraints = RigidbodyConstraints.FreezeRotation;
            sphereBody.interpolation = RigidbodyInterpolation.Interpolate;
            sphereBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // The car model owns grip, so the sphere itself must slide freely.
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = CarController.SphereRadius;
            sphere.sharedMaterial = Frictionless();

            var visual = new GameObject("Body").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, -CarController.SphereRadius, 0f);
            BuildBody(visual, colour, body);

            var car = root.AddComponent<CarController>();
            car.Initialise(visual);
            car.BodyStats = (body ?? CarModels.Race).Stats;
            car.UseBodyStats(true);
            car.Smoke = CarSmoke.Build(root.transform);
            car.WreckedNow += () => GameAudio.PlayAt(Sfx.Explosion, car.transform.position, 0.8f);
            if (haptics)
            {
                root.AddComponent<CarHaptics>();
            }

            CarAudio.ForCar(car, own: haptics);
            return car;
        }

        /// <summary>
        /// Another phone's car: just the body, placed by its <see cref="RemoteCar"/>. It has no
        /// collider, so it is never a solid wall for this phone's car; contact with it is
        /// resolved by <c>BumpMonitor</c> instead (design doc §2.2).
        /// </summary>
        public static RemoteCar BuildRemote(string name, byte playerId, Color colour, Transform parent, CarModel body = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var visual = new GameObject("Body").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, -CarController.SphereRadius, 0f);
            BuildBody(visual, colour, body);

            var car = root.AddComponent<RemoteCar>();
            car.Initialise(playerId, colour, visual);
            CarAudio.ForRemote(car);
            return car;
        }

        /// <summary>
        /// Swaps the body and paint of a car made here (design doc §11, picking a car in the lobby),
        /// and its handling with them, leaving the car where it is. Takes the car's root object.
        /// <paramref name="monster"/> draws it as a monster truck (Monster Jam, design doc §7.0).
        /// </summary>
        public static void ReplaceBody(GameObject car, Color colour, CarModel body, bool monster = false)
        {
            var visual = car.transform.Find("Body");
            if (visual == null)
            {
                return;
            }

            // Detach first: Destroy waits for the end of the frame, and the new body shouldn't share the old one's parent until then.
            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                var old = visual.GetChild(i);
                old.SetParent(null, false);
                Object.Destroy(old.gameObject);
            }

            BuildBody(visual, colour, body, monster);
            var controller = car.GetComponent<CarController>();
            if (controller != null)
            {
                controller.BodyStats = (body ?? CarModels.Race).Stats;
                controller.UseBodyStats(true);
            }
        }

        private static void BuildBody(Transform visual, Color colour, CarModel body, bool monster = false)
        {
            if (CarBodies.TryBuild(visual, colour, body ?? CarModels.Race, monster))
            {
                return;
            }

            var paint = SimpleMaterials.Get(colour);
            var dark = SimpleMaterials.Get(new Color(0.12f, 0.12f, 0.14f));
            var glass = SimpleMaterials.Get(new Color(0.55f, 0.75f, 0.9f));

            SimpleMaterials.Part(PrimitiveType.Cube, visual, new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.45f, 2.3f), paint);
            SimpleMaterials.Part(PrimitiveType.Cube, visual, new Vector3(0f, 0.85f, -0.2f), new Vector3(1f, 0.4f, 1.1f), glass);
            SimpleMaterials.Part(PrimitiveType.Cube, visual, new Vector3(0f, 0.6f, -1.15f), new Vector3(1.2f, 0.12f, 0.25f), dark);

            foreach (float x in new[] { -0.7f, 0.7f })
            {
                foreach (float z in new[] { -0.75f, 0.8f })
                {
                    var wheel = SimpleMaterials.Part(PrimitiveType.Cylinder, visual, new Vector3(x, 0.32f, z), new Vector3(0.62f, 0.12f, 0.62f), dark);
                    wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
        }

        private static PhysicsMaterial Frictionless()
        {
            if (_frictionless == null)
            {
                _frictionless = new PhysicsMaterial("Car")
                {
                    staticFriction = 0f,
                    dynamicFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            return _frictionless;
        }
    }
}
