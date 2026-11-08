using CarRacer.Core.Arena;
using CarRacer.Core.Bots;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// The host's side of the lobby (design doc §2.3, §10, §11), on a phone host and on a server
    /// alike: remembers the next event's settings and the cup auto-save between sessions, and hands
    /// the room leader's commands to <see cref="HostLobbyRules"/>, which carries them out. The
    /// screens only send commands (<see cref="NetworkSession.Command"/>), so the same buttons work
    /// on the host's own phone and on a guest phone leading a server's room.
    /// </summary>
    public sealed class HostLobby : MonoBehaviour
    {
        public const string BotFillKey = "botFill";
        public const string BotDifficultyKey = "botDifficulty";
        public const string ModeKey = "mode";
        public const string LapsKey = "laps";
        public const string TrackKey = "track";
        public const string RulesKey = "eventRules";
        public const string CupKey = "cupChoice";
        public const string ReverseGridKey = "reverseGrid";
        public const string SaveKey = "cupSave";

        private NetworkSession _session;
        private HostLobbyRules _rules;

        /// <summary>The cup in progress, or null.</summary>
        public CupRun Run => _rules?.Run;

        public void Initialise(NetworkSession session)
        {
            _session = session;
            var host = session.HostSide;
            host.ChangeSettings(Load());
            host.SettingsChanged += Save;
            _rules = new HostLobbyRules(host, (HostRace)session.Race, () => NetworkSession.NowMs, new PlayerPrefsCupSave());
            host.LobbyCommandReceived += _rules.RunCommand;
        }

        private void OnDestroy()
        {
            if (_session != null && _session.HostSide != null)
            {
                _session.HostSide.SettingsChanged -= Save;
                if (_rules != null)
                {
                    _session.HostSide.LobbyCommandReceived -= _rules.RunCommand;
                }
            }
        }

        private void Update()
        {
            if (!_session.Ended)
            {
                _rules.Tick();
            }
        }

        private static LobbySettings Load()
        {
            var settings = LobbySettings.Default;
            settings.Mode = (GameMode)PlayerPrefs.GetInt(ModeKey, (int)settings.Mode);
            settings.Laps = PlayerPrefs.GetInt(LapsKey, settings.Laps);
            settings.Track = PlayerPrefs.GetInt(TrackKey, settings.Track);
            settings.Rules = EventRules.FromByte((byte)PlayerPrefs.GetInt(RulesKey, settings.Rules.ToByte()));
            settings.BotFill = PlayerPrefs.GetInt(BotFillKey, settings.BotFill);
            settings.BotDifficulty = (BotDifficulty)PlayerPrefs.GetInt(BotDifficultyKey, (int)settings.BotDifficulty);
            settings.Cup = PlayerPrefs.GetInt(CupKey, settings.Cup);
            settings.ReverseGrid = PlayerPrefs.GetInt(ReverseGridKey, settings.ReverseGrid ? 1 : 0) == 1;
            return settings.Clean();
        }

        private void Save()
        {
            var settings = _session.Settings;
            PlayerPrefs.SetInt(ModeKey, (int)settings.Mode);
            PlayerPrefs.SetInt(LapsKey, settings.Laps);
            PlayerPrefs.SetInt(TrackKey, TrackCatalog.Clamp(settings.Track));
            PlayerPrefs.SetInt(RulesKey, settings.Rules.ToByte());
            PlayerPrefs.SetInt(BotFillKey, settings.BotFill);
            PlayerPrefs.SetInt(BotDifficultyKey, (int)settings.BotDifficulty);
            PlayerPrefs.SetInt(CupKey, settings.Cup);
            PlayerPrefs.SetInt(ReverseGridKey, settings.ReverseGrid ? 1 : 0);
        }

        private sealed class PlayerPrefsCupSave : ICupSaveStore
        {
            public string Load() => PlayerPrefs.GetString(SaveKey, string.Empty);

            public void Save(string save)
            {
                PlayerPrefs.SetString(SaveKey, save);
                PlayerPrefs.Save();
            }

            public void Delete() => PlayerPrefs.DeleteKey(SaveKey);
        }
    }
}
