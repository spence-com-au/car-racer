using System;
using CarRacer.Core.Audio;
using CarRacer.Core.Driving;
using CarRacer.Core.Physics;
using CarRacer.Game.Driving;
using CarRacer.Game.Net;
using UnityEngine;

namespace CarRacer.Game.Audio
{
    /// <summary>
    /// A car's sounds (design doc §13): every car's engine, rising with its speed and heard from
    /// where it is; and, for this phone's own car, a crunch scaled by each bump and a whoosh on
    /// each drift boost.
    /// </summary>
    public sealed class CarAudio : MonoBehaviour
    {
        /// <summary>Speed (m/s) at which the engine reaches its top note.</summary>
        private const float TopSpeed = CarTuning.BaseMaxSpeed;

        private readonly BumpSettings _bumpSettings = new BumpSettings();
        private AudioSource _engine;
        private Func<float> _speed;
        private Func<bool> _boosting;
        private CarController _own;
        private float _pitchOffset;

        /// <summary>A car this phone drives: its own (with crunches and boosts, heard up close) or a bot.</summary>
        public static CarAudio ForCar(CarController car, bool own)
        {
            var audio = car.gameObject.AddComponent<CarAudio>();
            audio.Initialise(() => car.ForwardSpeed, () => car.State.BoostSecondsLeft > 0f, own ? car : null);
            return audio;
        }

        /// <summary>Another phone's car, from the speed its updates carry.</summary>
        public static CarAudio ForRemote(RemoteCar car)
        {
            var audio = car.gameObject.AddComponent<CarAudio>();
            audio.Initialise(() => car.Velocity.magnitude, () => false, null);
            return audio;
        }

        private void Initialise(Func<float> speed, Func<bool> boosting, CarController own)
        {
            _speed = speed;
            _boosting = boosting;
            _own = own;

            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = GameAudio.EngineClip();
            _engine.loop = true;
            _engine.playOnAwake = false;

            // This phone's engine is always close; the others fade with distance.
            _engine.spatialBlend = own != null ? 0f : 1f;
            _engine.rolloffMode = AudioRolloffMode.Linear;
            _engine.minDistance = 5f;
            _engine.maxDistance = 70f;
            _engine.dopplerLevel = 0f;

            // A slightly different note per car, so several engines don't drone in unison.
            _engine.time = UnityEngine.Random.Range(0f, _engine.clip.length * 0.99f);
            _pitchOffset = UnityEngine.Random.Range(-0.05f, 0.05f);
            _engine.Play();

            if (_own != null)
            {
                _own.Pushed += OnPushed;
                _own.BoostReleased += OnBoostReleased;
            }
        }

        private void OnDestroy()
        {
            if (_own != null)
            {
                _own.Pushed -= OnPushed;
                _own.BoostReleased -= OnBoostReleased;
            }
        }

        private void Update()
        {
            if (_engine == null)
            {
                return;
            }

            float speed = _speed();
            _engine.pitch = SoundRules.EnginePitch(speed, TopSpeed, _boosting()) + _pitchOffset;
            _engine.volume = SoundRules.EngineVolume(speed, TopSpeed) * GameAudio.Volume * (_own != null ? 0.7f : 1f);
        }

        private void OnPushed(Vector3 impulse)
        {
            var (size, volume) = SoundRules.Crunch(new Vector2(impulse.x, impulse.z).magnitude, BumpMonitor.ReducedMass, _bumpSettings);
            GameAudio.Play(size == CrunchSize.Heavy ? "crunch_heavy" : size == CrunchSize.Medium ? "crunch_medium" : "crunch_light", volume);
        }

        private void OnBoostReleased(int tier) => GameAudio.Play("boost", 0.5f + 0.15f * tier);
    }
}
