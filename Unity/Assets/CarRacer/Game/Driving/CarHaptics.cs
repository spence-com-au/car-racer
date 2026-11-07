using CarRacer.Core.Driving;
using CarRacer.Core.Physics;
using CarRacer.Game.Platform;
using UnityEngine;

namespace CarRacer.Game.Driving
{
    /// <summary>Vibrates the phone when this phone's car is bumped or releases a drift boost (design doc §5).</summary>
    [RequireComponent(typeof(CarController))]
    public sealed class CarHaptics : MonoBehaviour
    {
        private readonly HapticLimiter _limiter = new HapticLimiter();
        private readonly BumpSettings _bumpSettings = new BumpSettings();
        private CarController _car;

        private static long NowMs => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        private void OnEnable()
        {
            _car = GetComponent<CarController>();
            _car.Pushed += OnPushed;
            _car.BoostReleased += OnBoostReleased;
        }

        private void OnDisable()
        {
            _car.Pushed -= OnPushed;
            _car.BoostReleased -= OnBoostReleased;
        }

        private void OnPushed(Vector3 impulse)
        {
            float horizontal = new Vector2(impulse.x, impulse.z).magnitude;
            float reducedMass = BumpMonitor.CarMass / 2f;
            Play(HapticPatterns.ForBump(horizontal, reducedMass, _bumpSettings));
        }

        private void OnBoostReleased(int tier) => Play(HapticPatterns.ForBoost(tier));

        private void Play(HapticPulse pulse)
        {
            if (_limiter.TryPlay(NowMs))
            {
                Vibration.Play(pulse);
            }
        }
    }
}
