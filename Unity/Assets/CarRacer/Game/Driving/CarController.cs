using CarRacer.Core.Arena;
using CarRacer.Core.Driving;
using CarRacer.Core.Physics;
using CarRacer.Game.Platform;
using UnityEngine;

namespace CarRacer.Game.Driving
{
    /// <summary>Supplies the driver's input each physics step (the local player, a bot, or a test).</summary>
    public interface ICarInputSource
    {
        CarInput Read();
    }

    /// <summary>
    /// The sphere-based car (design doc §4). A frictionless, non-rotating sphere rigidbody carries
    /// the car; every fixed step its horizontal velocity goes through <see cref="CarModel"/> and
    /// back, while gravity and collisions stay with the physics engine. The visible body is a child
    /// that turns to the car's heading and leans for show. Where damage is on (a race or practice,
    /// design doc §4), bumps, item hits and hard wall hits add to <see cref="Damage"/>, which costs
    /// top speed and acceleration; a wrecked car stops, takes no input, and comes back mended.
    /// In Monster Jam (design doc §7.0) it handles as a monster truck: it feels less gravity in the
    /// air and hits other trucks harder, and it reports its landings and the cars it touches, for
    /// the stunt scores.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        public const float SphereRadius = 0.6f;

        /// <summary>Extra downward pull on top of normal gravity, so the car feels planted and lands quickly.</summary>
        public const float ExtraGravity = 12f;

        /// <summary>How far below the sphere the ground can be and still count as touching it.</summary>
        public const float GroundProbe = 0.25f;

        /// <summary>How quickly a wreck slides to a stop (per second, exponential).</summary>
        private const float WreckDrag = 4f;

        /// <summary>A contact whose normal points more upward than this is the ground, not a wall.</summary>
        private const float WallNormalMaxY = 0.5f;

        private static readonly BumpSettings BumpScale = new BumpSettings();

        /// <summary>A car that falls below this height is put back where it started.</summary>
        public const float FallLimit = -20f;

        /// <summary>How far the body swings into a drift; <c>RemoteCar</c> swings other phones' cars as far.</summary>
        public const float DriftVisualYawDegrees = 22f;

        private const float MaxLeanDegrees = 5f;
        private const float VisualSharpness = 10f;

        public CarTuning Tuning = new CarTuning();
        public ICarInputSource InputSource;

        /// <summary>This car's damage; see <see cref="DamageEnabled"/>.</summary>
        public readonly CarDamage Damage = new CarDamage();

        /// <summary>The smoke that shows this car's damage, if it has any.</summary>
        public CarSmoke Smoke;

        /// <summary>Its body's handling ratings (design doc §4), applied unless the host turns car stats off.</summary>
        public CarStats BodyStats = CarStats.Balanced;

        /// <summary>Drives with <see cref="BodyStats"/>, or, with car stats off for the event (design doc §11), like every other car.</summary>
        public void UseBodyStats(bool on) => (on ? BodyStats : CarStats.Balanced).ApplyTo(Tuning);

        /// <summary>
        /// Whether hits damage the car: on in a race and in practice, off in the lobby and the arenas,
        /// which have their own rules. Turning it off mends the car.
        /// </summary>
        public bool DamageEnabled
        {
            get => _damageEnabled;
            set
            {
                if (!value)
                {
                    Damage.Reset();
                }

                _damageEnabled = value;
            }
        }

        /// <summary>While true the driver's input is ignored, e.g. on the grid before the start.</summary>
        public bool InputLocked;

        /// <summary>Whose car this is (set by whoever builds it in a session), so another car touching it knows who it hit.</summary>
        public byte PlayerId = byte.MaxValue;

        /// <summary>Monster truck handling for Monster Jam (design doc §7.0): big air and heavy hits.</summary>
        public bool MonsterTruck;

        private Rigidbody _body;
        private Transform _visual;
        private Vector3 _pendingImpulse;
        private CarState _state;
        private Vector3 _spawnPosition;
        private float _spawnHeading;
        private float _visualDriftYaw;
        private float _visualLean;
        private bool _damageEnabled;
        private float _airSeconds;

        public CarState State => _state;

        /// <summary>The physics sphere's centre and velocity, as sent to other phones.</summary>
        public Vector3 BodyPosition => _body.position;
        public Vector3 BodyVelocity => _body.linearVelocity;

        /// <summary>
        /// The velocity the car went into the latest physics step with: by the time a collision
        /// reports, the engine has already resolved it, so this is the speed it hit at.
        /// </summary>
        public Vector3 PreImpactVelocity { get; private set; }
        public bool Grounded { get; private set; }
        public CarInput LastInput { get; private set; }

        /// <summary>The car was pushed by <see cref="AddImpulse"/> (a bump or a hit).</summary>
        public event System.Action<Vector3> Pushed;

        /// <summary>A drift released a boost of this tier (1..3).</summary>
        public event System.Action<int> BoostReleased;

        /// <summary>The car was just wrecked by damage.</summary>
        public event System.Action WreckedNow;

        /// <summary>The car landed after this many seconds off the ground.</summary>
        public event System.Action<float> Landed;

        /// <summary>The car ran into another car with a physics body here (a bot, or the host's own car); both cars' <see cref="PreImpactVelocity"/> say how they met.</summary>
        public event System.Action<CarController> TouchedCar;

        /// <summary>Speed along the car's heading in m/s; negative when reversing.</summary>
        public float ForwardSpeed => _body == null ? 0f : Vector3.Dot(_body.linearVelocity, _state.Forward.ToUnity());

        public void Initialise(Transform visual)
        {
            _body = GetComponent<Rigidbody>();
            _visual = visual;
        }

        /// <summary>
        /// Puts the car at <paramref name="position"/> facing <paramref name="heading"/> (radians, 0 = +Z), moving at
        /// <paramref name="velocity"/>. Setting the speed is no bump, so unlike <see cref="AddImpulse"/> this raises no
        /// <see cref="Pushed"/>.
        /// </summary>
        public void Place(Vector3 position, float heading, Vector3 velocity = default)
        {
            _spawnPosition = position;
            _spawnHeading = heading;
            Respawn();
            _body.linearVelocity = velocity;
        }

        /// <summary>
        /// Pushes the car (mass × Δv), e.g. a car-to-car bump. Applied at the start of the next
        /// physics step, before the handling model, so grip then straightens the car up.
        /// </summary>
        public void AddImpulse(Vector3 impulse)
        {
            _pendingImpulse += impulse;
            Pushed?.Invoke(impulse);
        }

        /// <summary>Starts a boost (the Boost item) of <paramref name="speed"/> extra top speed for <paramref name="seconds"/>.</summary>
        public void Boost(float speed, float seconds)
        {
            _state = CarModel.Boost(_state, speed, seconds);
        }

        /// <summary>Spins the car out (an item hit): no control for a moment.</summary>
        public void SpinOut()
        {
            _state = CarModel.SpinOut(_state, Tuning);
        }

        /// <summary>
        /// Damage from a bump with another car that pushed this one by <paramref name="impulse"/>
        /// (mass × Δv), as <c>BumpMonitor</c> resolved it. <c>BumpResolver</c> sizes and clamps bumps
        /// per unit of reduced mass, which for two cars of equal mass is half a car's, so the impulse
        /// is measured the same way here; that puts it on the same scale as a closing speed (see
        /// <see cref="OnCollisionEnter"/>) and lets the hardest bump do the full damage.
        /// </summary>
        public void TakeBump(Vector3 impulse)
        {
            impulse.y = 0f;
            Damaged(_damageEnabled && Damage.AddBump(impulse.magnitude / BumpMonitor.ReducedMass, BumpScale));
        }

        /// <summary>Damage from an item hit that landed (not one a Shield soaked up).</summary>
        public void TakeItemHit() => Damaged(_damageEnabled && Damage.AddItemHit());

        private void Damaged(bool wrecked)
        {
            if (wrecked)
            {
                _state = CarModel.Wreck(_state, Tuning);
                WreckedNow?.Invoke();
            }
        }

        /// <summary>
        /// Contacts the physics engine resolves: a hard hit on a wall damages the car by how fast it
        /// was going straight into it, and a car with a body here (on the host, its own car and the
        /// bots) by how fast the two closed, as a bump. Ground contacts don't count. Other phones'
        /// cars have no collider here; <c>BumpMonitor</c> resolves those, and they call <see cref="TakeBump"/>.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0)
            {
                return;
            }

            var normal = collision.GetContact(0).normal;
            var other = collision.rigidbody != null ? collision.rigidbody.GetComponent<CarController>() : null;
            if (other != null)
            {
                TouchedCar?.Invoke(other);
                if (MonsterTruck)
                {
                    // Heavy hitters: the physics engine's push, made as much more dramatic as a truck's bumps.
                    float closingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
                    var push = normal * closingSpeed * (MonsterRules.BumpExaggeration - BumpScale.Exaggeration) * _body.mass;
                    AddImpulse(push + Vector3.up * push.magnitude * BumpScale.LiftFraction);
                }
            }

            if (!_damageEnabled)
            {
                return;
            }

            if (normal.y > WallNormalMaxY)
            {
                return;
            }

            float closing = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            Damaged(collision.rigidbody != null ? Damage.AddBump(closing, BumpScale) : Damage.AddWallHit(closing));
        }

        private void Respawn()
        {
            _pendingImpulse = Vector3.zero;
            _airSeconds = 0f;
            _body.position = _spawnPosition;
            transform.position = _spawnPosition;
            _body.linearVelocity = Vector3.zero;
            _state = new CarState { Heading = _spawnHeading };
            Damage.Reset();
            _visualDriftYaw = 0f;
            _visualLean = 0f;
            UpdateVisual(0f);
        }

        private void FixedUpdate()
        {
            bool wasGrounded = Grounded;
            Grounded = Physics.Raycast(_body.position, Vector3.down, SphereRadius + GroundProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (!Grounded)
            {
                _airSeconds += Time.fixedDeltaTime;
            }
            else if (!wasGrounded && _airSeconds > 0f)
            {
                Landed?.Invoke(_airSeconds);
                _airSeconds = 0f;
            }
            Damage.Tick(Time.fixedDeltaTime);
            bool wrecked = Damage.Wrecked;
            LastInput = InputLocked || wrecked ? default : InputSource?.Read() ?? default;

            Vector3 velocity = _body.linearVelocity + _pendingImpulse / _body.mass;
            _pendingImpulse = Vector3.zero;
            _state.Velocity = velocity.ToCore();
            var before = _state;
            _state = CarModel.Step(_state, LastInput, Grounded, Time.fixedDeltaTime, Tuning, Damage.PerformanceFactor);
            if (wrecked)
            {
                _state.Velocity *= Mathf.Exp(-WreckDrag * Time.fixedDeltaTime);
            }

            int boostTier = HapticPatterns.ReleasedBoostTier(before, _state, Time.fixedDeltaTime, Tuning);
            if (boostTier > 0)
            {
                BoostReleased?.Invoke(boostTier);
            }

            _body.linearVelocity = new Vector3(_state.Velocity.X, velocity.y, _state.Velocity.Z);
            _body.AddForce(Vector3.down * ExtraGravity, ForceMode.Acceleration);
            if (MonsterTruck && !Grounded)
            {
                _body.AddForce(Vector3.up * MonsterRules.AirLift, ForceMode.Acceleration);
            }

            PreImpactVelocity = _body.linearVelocity;

            if (_body.position.y < FallLimit)
            {
                Respawn();
            }
        }

        private void Update()
        {
            UpdateVisual(Time.deltaTime);
            if (Smoke != null)
            {
                Smoke.Level = Damage.SmokeLevel;
            }
        }

        private void UpdateVisual(float dt)
        {
            if (_visual == null)
            {
                return;
            }

            // Kart-style drift: the body swings out into the slide. It also leans out of turns.
            float blend = dt > 0f ? 1f - Mathf.Exp(-VisualSharpness * dt) : 1f;
            float targetDriftYaw = _state.Drifting ? _state.DriftDirection * DriftVisualYawDegrees : 0f;
            float speedFraction = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Tuning.MaxSpeed);
            // Reduced motion (design doc §6) keeps the drift swing, which shows the slide, but not the lean.
            float targetLean = Accessibility.ReducedMotion ? 0f : -LastInput.Steer * speedFraction * MaxLeanDegrees;
            _visualDriftYaw = Mathf.Lerp(_visualDriftYaw, targetDriftYaw, blend);
            _visualLean = Mathf.Lerp(_visualLean, targetLean, blend);

            _visual.rotation = Quaternion.Euler(0f, _state.Heading * Mathf.Rad2Deg + _visualDriftYaw, _visualLean);
        }
    }
}
