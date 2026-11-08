using System;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Physics
{
    [Serializable]
    public sealed class PredictionSettings
    {
        /// <summary>Never extrapolate further than this past the last state (covers packet loss without wild overshoot).</summary>
        public float MaxExtrapolationMs = 250f;

        /// <summary>Time constant for blending away correction error after a new state arrives.</summary>
        public float SmoothingSeconds = 0.08f;

        /// <summary>Errors larger than this snap immediately (respawn, teleport) instead of sliding.</summary>
        public float SnapDistance = 5f;
    }

    /// <summary>
    /// Shows a remote car where it most likely is <em>now</em> (extrapolated from its last
    /// state by velocity × age), rather than interpolating in the past. Being close to the
    /// truth matters more than smoothness here, because local collisions are resolved
    /// against this position. When a new state corrects the prediction, the error is
    /// blended out over <see cref="PredictionSettings.SmoothingSeconds"/>.
    /// </summary>
    public sealed class RemoteCarPredictor
    {
        private readonly PredictionSettings _settings;
        private CarStateMessage _latest;
        private long _latestReceivedMs;
        private Vec3 _errorOffset;

        public RemoteCarPredictor(PredictionSettings settings = null)
        {
            _settings = settings ?? new PredictionSettings();
        }

        public bool HasState => _latest != null;
        public CarStateMessage Latest => _latest;

        /// <summary>
        /// Accepts a state whose numbers are all finite if it's newer than the current one (sequence
        /// numbers wrap safely), or if it comes from a different driver. <paramref name="oneWayLatencyMs"/>
        /// is the current estimate, typically RTT / 2.
        /// </summary>
        public bool OnState(CarStateMessage state, long receivedAtMs, float oneWayLatencyMs)
        {
            // The state comes from another phone. A NaN would stick in the error offset, freezing the car on
            // screen, and reach this car's physics through bumps.
            if (!state.Position.IsFinite || !state.Velocity.IsFinite || !float.IsFinite(state.HeadingRadians))
            {
                return false;
            }

            // A bot taking over a player's car, or handing it back (design doc §12), sends from its
            // own sequence counter, so the old driver's sequence says nothing about the new one's.
            bool sameDriver = _latest != null
                && (state.Flags & CarStateFlags.BotControlled) == (_latest.Flags & CarStateFlags.BotControlled);
            if (sameDriver && unchecked((int)(state.Sequence - _latest.Sequence)) <= 0)
            {
                return false;
            }

            if (_latest == null)
            {
                _latest = state;
                _latestReceivedMs = receivedAtMs;
                _errorOffset = Vec3.Zero;
                return true;
            }

            Vec3 shownBefore = PredictRaw(receivedAtMs, oneWayLatencyMs) + _errorOffset;
            _latest = state;
            _latestReceivedMs = receivedAtMs;
            Vec3 predictedAfter = PredictRaw(receivedAtMs, oneWayLatencyMs);

            _errorOffset = shownBefore - predictedAfter;

            // Extrapolating a huge but finite state can overflow to infinity, and infinity minus infinity is a NaN
            // that no distance exceeds, so it would stick.
            if (!_errorOffset.IsFinite || _errorOffset.Length > _settings.SnapDistance)
            {
                _errorOffset = Vec3.Zero;
            }

            return true;
        }

        /// <summary>Best estimate of the car's true current position, without smoothing.</summary>
        public Vec3 PredictRaw(long nowMs, float oneWayLatencyMs)
        {
            if (_latest == null)
            {
                return Vec3.Zero;
            }

            float aheadMs = (nowMs - _latestReceivedMs) + oneWayLatencyMs;
            aheadMs = Math.Max(0f, Math.Min(aheadMs, _settings.MaxExtrapolationMs));
            return _latest.Position + _latest.Velocity * (aheadMs / 1000f);
        }

        /// <summary>Position to render the remote car at and to check bumps against. Call once per frame.</summary>
        public Vec3 UpdateDisplayPosition(long nowMs, float oneWayLatencyMs, float deltaSeconds)
        {
            if (_settings.SmoothingSeconds > 0f)
            {
                _errorOffset *= MathF.Exp(-deltaSeconds / _settings.SmoothingSeconds);
            }
            else
            {
                _errorOffset = Vec3.Zero;
            }

            return PredictRaw(nowMs, oneWayLatencyMs) + _errorOffset;
        }

        /// <summary>The remote car's velocity, for bump resolution.</summary>
        public Vec3 Velocity => _latest?.Velocity ?? Vec3.Zero;
    }
}
