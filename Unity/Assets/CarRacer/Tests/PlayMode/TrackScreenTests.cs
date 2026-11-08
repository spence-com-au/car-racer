using System.Collections;
using CarRacer.Core.Driving;
using CarRacer.Core.Physics;
using CarRacer.Core.Track;
using CarRacer.Game;
using CarRacer.Game.Driving;
using CarRacer.Game.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CarModels = CarRacer.Core.Art.CarModels;

namespace CarRacer.Tests
{
    /// <summary>
    /// Drives the real Unity car (rigidbody, colliders, generated track) to check the glue between
    /// <see cref="CarModel"/> and the physics engine: driving, collisions, landings and respawns.
    /// The handling itself is tested by `dotnet test`.
    /// </summary>
    public class TrackScreenTests
    {
        private sealed class ScriptedInput : ICarInputSource
        {
            public CarInput Input;

            public CarInput Read() => Input;
        }

        private ScriptedInput _input;
        private TrackScreen _screen;

        private CarController Car => _screen.Car;

        [SetUp]
        public void SetUp()
        {
            _input = new ScriptedInput();
            _screen = TrackScreen.Create(_input);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_screen.gameObject);
        }

        [UnityTest]
        public IEnumerator TheCarSettlesOnTheRoad()
        {
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(Car.Grounded);
            Assert.That(Car.transform.position.y, Is.InRange(0f, 1.5f));
            Assert.That(Car.ForwardSpeed, Is.EqualTo(0f).Within(0.3f));
        }

        [UnityTest]
        public IEnumerator AWreckedCarStopsThenDrivesAgainOnceMended()
        {
            yield return new WaitForSeconds(0.5f);
            _input.Input = new CarInput(0f, 1f);
            yield return new WaitForSeconds(1f);
            Assert.That(Car.ForwardSpeed, Is.GreaterThan(8f));

            // Practice takes damage; four item hits wreck the car.
            Assert.IsTrue(Car.DamageEnabled);
            for (int i = 0; i < 4; i++)
            {
                Car.TakeItemHit();
            }

            Assert.IsTrue(Car.Damage.Wrecked);
            yield return new WaitForSeconds(1.5f);
            Assert.That(Car.ForwardSpeed, Is.LessThan(3f), "a wreck takes no throttle and slides to a stop");

            float giveUp = Time.time + CarDamage.WreckSeconds;
            while (Car.Damage.Wrecked && Time.time < giveUp)
            {
                yield return null;
            }

            Assert.IsFalse(Car.Damage.Wrecked);
            Assert.That(Car.Damage.Value, Is.EqualTo(0f));
            yield return new WaitForSeconds(0.5f);
            Assert.That(Car.ForwardSpeed, Is.GreaterThan(3f), "mended, it pulls away again");
        }

        [UnityTest]
        public IEnumerator FullThrottleDrivesDownTheStraight()
        {
            yield return new WaitForSeconds(0.5f);
            Vector3 start = Car.transform.position;
            Vector3 forward = Car.State.Forward.ToUnity();

            _input.Input = new CarInput(0f, 1f);
            yield return new WaitForSeconds(2f);

            // 16 m/s² for 2 s covers about 32 m with nothing in the way; allow for frame timing.
            float travelled = Vector3.Dot(Car.transform.position - start, forward);
            Assert.That(travelled, Is.GreaterThan(20f));
            Assert.That(Car.ForwardSpeed, Is.GreaterThan(20f));
            Assert.IsTrue(Car.Grounded);
        }

        [UnityTest]
        public IEnumerator BrakingBringsTheCarToAStop()
        {
            _input.Input = new CarInput(0f, 1f);
            yield return new WaitForSeconds(1.5f);
            Assert.That(Car.ForwardSpeed, Is.GreaterThan(10f));

            // Hold the brake until the car stops (holding it longer would start reversing).
            float startSpeed = Car.ForwardSpeed;
            float startTime = Time.fixedTime;
            _input.Input = new CarInput(0f, -1f);
            while (Car.ForwardSpeed > 0.5f && Time.fixedTime - startTime < 3f)
            {
                yield return new WaitForFixedUpdate();
            }

            _input.Input = default;
            float expected = startSpeed / Car.Tuning.Braking;
            Assert.That(Time.fixedTime - startTime, Is.LessThan(expected + 0.2f), $"braking from {startSpeed:0.0} m/s");

            yield return new WaitForSeconds(0.3f);
            Assert.That(Car.ForwardSpeed, Is.EqualTo(0f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator SteeringRightTurnsClockwise()
        {
            _input.Input = new CarInput(0f, 1f);
            yield return new WaitForSeconds(0.8f);
            float heading = Car.State.Heading;

            _input.Input = new CarInput(1f, 1f);
            yield return new WaitForSeconds(0.3f);
            Assert.That(Mathf.DeltaAngle(heading * Mathf.Rad2Deg, Car.State.Heading * Mathf.Rad2Deg), Is.GreaterThan(10f));
        }

        [UnityTest]
        public IEnumerator ALandingReportsTheTimeInTheAir()
        {
            yield return new WaitForSeconds(0.5f);
            float? airSeconds = null;
            Car.Landed += seconds => airSeconds = seconds;

            Lift(Car, 4f);
            yield return WaitUntil(() => airSeconds.HasValue, 5f);

            Assert.IsNotNull(airSeconds, "the car landed");
            Assert.That(airSeconds.Value, Is.InRange(0.2f, 2f));
        }

        [UnityTest]
        public IEnumerator AMonsterTruckHangsInTheAirLongerThanACar()
        {
            float car = 0f;
            yield return MeasureDrop(seconds => car = seconds);

            Car.MonsterTruck = true;
            float truck = 0f;
            yield return MeasureDrop(seconds => truck = seconds);

            Assert.That(car, Is.GreaterThan(0f));
            Assert.That(truck, Is.GreaterThan(car), "a truck feels less gravity in the air");
        }

        [UnityTest]
        public IEnumerator ACarThatFallsOffTheWorldIsPutBackAtTheStart()
        {
            yield return new WaitForSeconds(0.5f);
            Vector3 start = Car.transform.position;

            var body = Car.GetComponent<Rigidbody>();
            body.position = start + Vector3.down * (start.y - CarController.FallLimit + 5f);
            yield return new WaitForSeconds(0.2f);

            Assert.That(Car.transform.position.y, Is.GreaterThan(CarController.FallLimit));
            Vector3 offset = Car.transform.position - start;
            offset.y = 0f;
            Assert.That(offset.magnitude, Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator AHardHitOnAWallDamagesTheCar()
        {
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(Car.DamageEnabled);
            Assert.That(Car.Damage.Value, Is.EqualTo(0f));

            Vector3 forward = Car.State.Forward.ToUnity();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(_screen.transform, true);
            wall.transform.position = Car.transform.position + forward * 15f;
            wall.transform.rotation = Quaternion.LookRotation(forward);
            wall.transform.localScale = new Vector3(30f, 6f, 1f);

            _input.Input = new CarInput(0f, 1f);
            yield return WaitUntil(() => Car.Damage.Value > 0f, 4f);

            Assert.That(Car.Damage.Value, Is.GreaterThan(0f), "ran into the wall at speed");
        }

        [UnityTest]
        public IEnumerator HittingAnotherCarReportsTheTouch()
        {
            yield return new WaitForSeconds(0.5f);
            CarController other = null;
            CarController touched = null;
            Car.TouchedCar += car => touched = car;
            yield return DriveIntoACarAhead(car => other = car);

            Assert.That(other, Is.Not.Null);
            Assert.That(touched, Is.SameAs(other));
        }

        [UnityTest]
        public IEnumerator AMonsterTruckPushesWhatItHits()
        {
            yield return new WaitForSeconds(0.5f);
            Car.MonsterTruck = true;
            Vector3? push = null;
            Car.Pushed += impulse => push = impulse;
            yield return DriveIntoACarAhead(_ => { });

            Assert.IsNotNull(push, "the truck was pushed by its own hit");
            Assert.That(push.Value.y, Is.GreaterThan(0f), "with some lift");
        }

        [UnityTest]
        public IEnumerator ABumpFromAnotherPhoneDamagesTheCarByItsHorizontalPush()
        {
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(Car.DamageEnabled);
            Assert.That(Car.Damage.Value, Is.EqualTo(0f));

            // The car has a mass of 1, so the damage is sized by the horizontal impulse over half of that;
            // the upward pop in the impulse must not count.
            Car.TakeBump(new Vector3(0f, 100f, 6f));

            float expected = BumpDamage(6f / 0.5f);
            Assert.That(expected, Is.GreaterThan(CarDamage.BumpMinDamage).And.LessThan(CarDamage.BumpMaxDamage));
            Assert.That(Car.Damage.Value, Is.EqualTo(expected).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator ABumpDoesNothingWhereDamageIsOff()
        {
            yield return new WaitForSeconds(0.5f);
            Car.DamageEnabled = false;

            Car.TakeBump(new Vector3(0f, 0f, 20f));

            Assert.That(Car.Damage.Value, Is.EqualTo(0f));
        }

        [Test]
        public void OnlyTheTrackCarsAreOnIsDrawnButEveryTrackStaysSolid()
        {
            AssertOnlyTrackDrawn(0);

            Assert.IsTrue(_screen.UseTrack(3));
            Assert.AreEqual(3, _screen.TrackIndex);
            AssertOnlyTrackDrawn(3);

            Assert.IsFalse(_screen.UseTrack(3), "already there");
            AssertOnlyTrackDrawn(3);

            Assert.IsTrue(_screen.UseTrack(TrackCatalog.Count), "an unknown track falls back to the first");
            Assert.AreEqual(0, _screen.TrackIndex);
            AssertOnlyTrackDrawn(0);
        }

        [Test]
        public void ReplacingABodySwapsTheModelAndTheHandlingWithIt()
        {
            var car = CarFactory.Build("Spare car", Color.red, _screen.transform, haptics: false);
            var visual = car.transform.Find("Body");
            Assert.AreEqual(CarModels.Race.Name, visual.GetChild(0).name);
            float raceSpeed = car.Tuning.MaxSpeed;

            var hatchback = CarModels.Bodies[3];
            CarFactory.ReplaceBody(car.gameObject, Color.blue, hatchback);

            Assert.AreEqual(1, visual.childCount, "the old body is detached, not left under the new one");
            Assert.AreEqual(hatchback.Name, visual.GetChild(0).name);
            Assert.AreNotEqual(raceSpeed, car.Tuning.MaxSpeed);
            Assert.AreEqual(hatchback.Stats.Tuning().MaxSpeed, car.Tuning.MaxSpeed);
        }

        /// <summary>The damage a single bump of <paramref name="impulsePerMass"/> does to an undamaged car.</summary>
        private static float BumpDamage(float impulsePerMass)
        {
            var damage = new CarDamage();
            damage.AddBump(impulsePerMass, new BumpSettings());
            return damage.Value;
        }

        /// <summary>
        /// Asserts that only track <paramref name="drawn"/> has its road, walls and scenery drawn, that every
        /// track's grass is drawn, and that every track's road and walls still collide.
        /// </summary>
        private void AssertOnlyTrackDrawn(int drawn)
        {
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                string name = TrackCatalog.Layouts[i].Definition.DisplayName;
                var track = _screen.transform.Find(name);
                Assert.IsNotNull(track, name);

                var renderers = track.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, name);
                foreach (var renderer in renderers)
                {
                    Assert.AreEqual(i == drawn, IsDrawn(renderer), $"{renderer.name} with track {drawn} drawn");
                }

                var colliders = track.GetComponentsInChildren<Collider>(true);
                Assert.That(colliders, Is.Not.Empty, name);
                foreach (var collider in colliders)
                {
                    bool solid = collider.enabled && collider.gameObject.activeInHierarchy;
                    Assert.IsTrue(solid, $"{collider.name} collides");
                }

                var grass = _screen.transform.Find(name + " ground").GetComponent<Renderer>();
                Assert.IsTrue(IsDrawn(grass), $"{name} grass is drawn");
            }
        }

        private static bool IsDrawn(Renderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy;

        /// <summary>Waits until <paramref name="done"/> or <paramref name="timeout"/> seconds have passed.</summary>
        private static IEnumerator WaitUntil(System.Func<bool> done, float timeout)
        {
            float giveUp = Time.time + timeout;
            while (!done() && Time.time < giveUp)
            {
                yield return null;
            }
        }

        /// <summary>Raises <paramref name="car"/> by <paramref name="height"/> metres, at rest, so it drops back down.</summary>
        private static void Lift(CarController car, float height)
        {
            var body = car.GetComponent<Rigidbody>();
            body.position += Vector3.up * height;
            body.linearVelocity = Vector3.zero;
        }

        /// <summary>Puts the car back at the start, drops it from a height and reports how long it was in the air.</summary>
        private IEnumerator MeasureDrop(System.Action<float> report)
        {
            Car.Place(Car.transform.position, Car.State.Heading);
            yield return new WaitForSeconds(0.5f);

            float? airSeconds = null;
            System.Action<float> onLanded = seconds => airSeconds = seconds;
            Car.Landed += onLanded;
            Lift(Car, 10f);
            yield return WaitUntil(() => airSeconds.HasValue, 5f);
            Car.Landed -= onLanded;

            Assert.IsNotNull(airSeconds, "the car landed");
            report(airSeconds.Value);
        }

        /// <summary>Parks a second car 6 m ahead of this one and drives into it at full throttle for a moment.</summary>
        private IEnumerator DriveIntoACarAhead(System.Action<CarController> parked)
        {
            Vector3 forward = Car.State.Forward.ToUnity();
            var other = CarFactory.Build("Parked car", Color.red, _screen.transform, haptics: false);
            other.Place(Car.transform.position + forward * 6f, Car.State.Heading);
            parked(other);
            yield return new WaitForSeconds(0.3f);

            _input.Input = new CarInput(0f, 1f);
            yield return new WaitForSeconds(1.5f);
        }
    }
}
