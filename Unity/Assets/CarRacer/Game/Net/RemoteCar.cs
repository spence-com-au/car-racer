using CarRacer.Core.Net;
using CarRacer.Core.Physics;
using CarRacer.Game.Driving;
using CarRacer.Game.Items;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// Another phone's car, shown where it probably is now (design doc §2): positioned each frame
    /// by <see cref="RemoteCarPredictor"/> from that phone's latest <see cref="CarStateMessage"/>.
    /// Contact with it is resolved by <c>BumpMonitor</c> against that position, not by PhysX.
    /// </summary>
    public sealed class RemoteCar : MonoBehaviour
    {
        private const float HeadingSharpness = 20f;

        private readonly RemoteCarPredictor _predictor = new RemoteCarPredictor();
        private Transform _visual;
        private float _headingDegrees;
        private float _driftYaw;
        private Transform _shieldRing;
        private CarSmoke _smoke;

        public byte PlayerId { get; private set; }
        public Color Colour { get; set; }
        public bool HasState => _predictor.HasState;

        /// <summary>The car's last reported velocity, for bumps.</summary>
        public Vector3 Velocity => _predictor.Velocity.ToUnity();

        /// <summary>The car's last reported heading, in radians.</summary>
        public float HeadingRadians => _predictor.Latest.HeadingRadians;

        public void Initialise(byte playerId, Color colour, Transform visual)
        {
            PlayerId = playerId;
            Colour = colour;
            _visual = visual;
            _visual.gameObject.SetActive(false);
            _shieldRing = ShieldRing.Build(transform);
            _smoke = CarSmoke.Build(transform);
        }

        public void OnState(CarStateMessage state, long nowMs, float latencyMs)
        {
            bool first = !_predictor.HasState;
            if (_predictor.OnState(state, nowMs, latencyMs) && first)
            {
                _headingDegrees = state.HeadingRadians * Mathf.Rad2Deg;
                _visual.gameObject.SetActive(true);
            }
        }

        public void Tick(long nowMs, float latencyMs, float deltaSeconds)
        {
            if (!_predictor.HasState)
            {
                return;
            }

            transform.position = _predictor.UpdateDisplayPosition(nowMs, latencyMs, deltaSeconds).ToUnity();

            var latest = _predictor.Latest;
            float blend = 1f - Mathf.Exp(-HeadingSharpness * deltaSeconds);
            _headingDegrees = Mathf.LerpAngle(_headingDegrees, latest.HeadingRadians * Mathf.Rad2Deg, blend);

            // Drift direction isn't sent; lean the body into the slide by which side the car is sliding towards.
            float targetDrift = 0f;
            if ((latest.Flags & CarStateFlags.Drifting) != 0)
            {
                var forward = new Vector3(Mathf.Sin(latest.HeadingRadians), 0f, Mathf.Cos(latest.HeadingRadians));
                var right = new Vector3(forward.z, 0f, -forward.x);
                targetDrift = -Mathf.Sign(Vector3.Dot(latest.Velocity.ToUnity(), right)) * CarController.DriftVisualYawDegrees;
            }

            ShieldRing.Show(_shieldRing, (latest.Flags & CarStateFlags.Shielded) != 0);
            _smoke.Level = DamageFlags.SmokeLevel(latest.Flags);
            _driftYaw = Mathf.Lerp(_driftYaw, targetDrift, blend);
            _visual.rotation = Quaternion.Euler(0f, _headingDegrees + _driftYaw, 0f);
        }
    }
}
