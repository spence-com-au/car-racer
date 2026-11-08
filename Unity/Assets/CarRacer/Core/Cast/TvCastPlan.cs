using System.Collections.Generic;
using CarRacer.Core.Net;

namespace CarRacer.Core.Cast
{
    /// <summary>
    /// When the server casts the TV viewer, and to which TV (design doc §2.4). A TV is wanted from
    /// <c>-cast</c> or by the room leader; the cast starts once the stream is ready and the TV has
    /// been found. A leader's request may take over a busy TV; the server's own tries, at start-up
    /// and at each countdown while nothing is being cast, only use an idle one, so it never takes
    /// over something someone else is watching. Stopping forgets the TV.
    /// </summary>
    public sealed class TvCastPlan
    {
        private bool _pending;
        private bool _takeOver;

        /// <summary>Whether the latest cast <see cref="Next"/> handed out takes over its TV.</summary>
        private bool _castTakesOver;

        /// <summary>Whether a cast is waiting to start (for the stream to be ready, or the TV to be found).</summary>
        public bool Pending => _pending && Wanted != null;

        /// <summary>The TV wanted: a name, id or address, or empty for the first one found; null for none.</summary>
        public string Wanted { get; private set; }

        /// <summary>
        /// Casts to <paramref name="wanted"/> as soon as it can; <paramref name="takeOver"/> for the
        /// leader's own request. <paramref name="castDevice"/> and <paramref name="cast"/> are the
        /// latest cast's TV and state, or null if there hasn't been one; returns whether that cast
        /// must stop to make way. A cast running to the same TV that the TV can no longer turn away
        /// (past connecting, or itself a take-over) is kept and the request only remembered: nothing
        /// is queued, so a take-over can't fire later, after someone else has taken the TV. A polite
        /// cast still connecting may yet find the TV busy, so a take-over replaces it.
        /// </summary>
        public bool Request(string wanted, bool takeOver, CastDevice castDevice = null, CastState? cast = null)
        {
            Wanted = wanted ?? string.Empty;
            bool casting = cast.HasValue && !Finished(cast);
            bool showing = casting && castDevice.Matches(Wanted) && (!takeOver || _castTakesOver || cast != CastState.Connecting);
            _pending = !showing;
            _takeOver = takeOver && !showing;
            return casting && !showing;
        }

        /// <summary>An event's countdown began: casts again to the wanted TV if nothing is being cast, politely.</summary>
        public void EventStarting(bool casting)
        {
            if (Wanted != null && !casting)
            {
                _pending = true;
            }
        }

        /// <summary>No more casting until asked again.</summary>
        public void Forget()
        {
            Wanted = null;
            _pending = false;
            _takeOver = false;
        }

        /// <summary>
        /// The TV to cast to now, and whether to take it over, or null to wait. While something is
        /// being cast, the server's own polite tries are dropped, but a leader's request waits for
        /// the old cast to end (the caller stops it when <see cref="Request"/> says to).
        /// </summary>
        public CastDevice Next(IReadOnlyList<CastDevice> devices, bool casting, bool streamReady, out bool takeOver)
        {
            takeOver = false;
            if (casting)
            {
                _pending &= _takeOver;
                return null;
            }

            if (!_pending || Wanted == null || !streamReady)
            {
                return null;
            }

            CastDevice device = null;
            foreach (var candidate in devices)
            {
                if (Wanted.Length == 0 || candidate.Matches(Wanted))
                {
                    device = candidate;
                    break;
                }
            }

            if (device == null)
            {
                return null;
            }

            takeOver = _takeOver;
            _castTakesOver = takeOver;
            _pending = false;
            _takeOver = false;
            return device;
        }

        /// <summary>
        /// What every phone is told about the TV (design doc §2.4): whether casting works here, the
        /// TVs found (by name, at most <see cref="TvStatusMessage.MaxDevices"/>, no repeats), and how
        /// the cast is going. <paramref name="cast"/> is the latest cast's state, or null if there
        /// hasn't been one. A cast that has ended is shown as off once no TV is wanted, so Stop
        /// clears a busy or failed TV; while a cast is waiting to start, it shows as connecting.
        /// </summary>
        public TvStatusMessage Status(bool canCast, bool music, IEnumerable<string> devices, CastState? cast, string castDevice, string castDetail)
        {
            var status = new TvStatusMessage { CanCast = canCast, Music = music };
            foreach (string name in devices)
            {
                if (status.Devices.Count < TvStatusMessage.MaxDevices && !status.Devices.Contains(name))
                {
                    status.Devices.Add(name);
                }
            }

            bool finished = Finished(cast);
            if (cast.HasValue && !(finished && Wanted == null))
            {
                status.CastingTo = castDevice ?? string.Empty;
                status.Detail = castDetail ?? string.Empty;
                status.State = cast switch
                {
                    CastState.Playing => TvState.Playing,
                    CastState.Busy => TvState.Busy,
                    CastState.Failed => TvState.Failed,
                    CastState.Stopped => TvState.Off,
                    _ => TvState.Connecting,
                };
            }

            // Waiting for the stream or the TV to turn up counts as connecting to it.
            if (Pending && (!cast.HasValue || finished) && status.State != TvState.Busy && status.State != TvState.Failed)
            {
                status.State = TvState.Connecting;
                status.CastingTo = Wanted.Length > 0 ? Wanted : status.CastingTo;
                status.Detail = string.Empty;
            }

            return status;
        }

        private static bool Finished(CastState? cast) => cast == CastState.Busy || cast == CastState.Stopped || cast == CastState.Failed;
    }
}
