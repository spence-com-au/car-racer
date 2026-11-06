using CarRacer.Core.Audio;
using CarRacer.Core.Net;
using CarRacer.Game.Ui;
using UnityEngine.UIElements;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The lobby's TV controls (design doc §2.4): for the room leader of a server that can cast, a
    /// Cast to TV button that opens the list of Chromecasts the server found, with Stop casting;
    /// for everyone, "On TV: …" while the room is on a TV.
    /// </summary>
    public sealed class TvCastPanel
    {
        private readonly NetworkSession _session;
        private bool _open;

        public TvCastPanel(NetworkSession session)
        {
            _session = session;
        }

        /// <summary>Changes whenever what the panel shows would change, so the lobby is rebuilt.</summary>
        public string Signature => TvText.Signature(_session.TvStatus) + "|" + _open + "|" + _session.LeadsRoom;

        /// <summary>Adds the controls for this phone to the lobby's <paramref name="content"/>.</summary>
        public void AddTo(VisualElement content)
        {
            var status = _session.TvStatus;
            bool leads = _session.LeadsRoom && !_session.IsHost && status.CanCast;
            if (!leads)
            {
                _open = false;
                string line = TvText.OnTvLine(status);
                if (line != null)
                {
                    content.Add(UiKit.Hint(line));
                }

                return;
            }

            content.Add(UiKit.Button(TvText.ButtonLabel(status), () =>
            {
                string quick = _open ? null : TvText.QuickCast(status);
                if (quick != null)
                {
                    _session.CastTo(quick);
                }
                else
                {
                    _open = !_open;
                }

            }));

            if (!_open)
            {
                if (status.State == TvState.Busy || status.State == TvState.Failed)
                {
                    content.Add(UiKit.Hint(status.Detail));
                }

                return;
            }

            if (status.Devices.Count == 0)
            {
                content.Add(UiKit.Hint("Looking for TVs on this network..."));
            }

            foreach (string device in status.Devices)
            {
                bool current = device == status.CastingTo && status.State == TvState.Playing;
                content.Add(UiKit.Enabled(UiKit.Button(current ? device + " (on now)" : device, () =>
                {
                    _session.CastTo(device);
                    _open = false;
                }), !current));
            }

            if (status.State != TvState.Off)
            {
                content.Add(UiKit.Button("Stop casting", () =>
                {
                    _session.CastTo(null);
                    _open = false;
                }, sound: UiSound.Back));
            }

            content.Add(UiKit.Hint("Casting takes over the TV, even if something else is on it."));
        }
    }
}
