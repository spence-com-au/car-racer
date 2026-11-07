using System.Collections.Generic;

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

        /// <summary>Whether a cast is waiting to start (for the stream to be ready, or the TV to be found).</summary>
        public bool Pending => _pending && Wanted != null;

        /// <summary>The TV wanted: a name, id or address, or empty for the first one found; null for none.</summary>
        public string Wanted { get; private set; }

        /// <summary>
        /// Casts to <paramref name="wanted"/> as soon as it can; <paramref name="takeOver"/> for the
        /// leader's own request. When <paramref name="alreadyShowing"/> (that TV is being cast to
        /// now), it is only remembered: nothing is queued, so a take-over can't fire later, after
        /// someone else has taken the TV.
        /// </summary>
        public void Request(string wanted, bool takeOver, bool alreadyShowing = false)
        {
            Wanted = wanted ?? string.Empty;
            _pending = !alreadyShowing;
            _takeOver = takeOver && !alreadyShowing;
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
        /// the old cast to end (the caller stops it to switch TVs).
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
            _pending = false;
            _takeOver = false;
            return device;
        }
    }
}
