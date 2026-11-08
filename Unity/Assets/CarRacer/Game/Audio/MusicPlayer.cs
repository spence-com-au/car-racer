using CarRacer.Core.Audio;
using CarRacer.Game.Platform;
using UnityEngine;

namespace CarRacer.Game.Audio
{
    /// <summary>
    /// The background music (design doc §13): CC0 tunes in <c>Resources/Music</c>, one playing at
    /// a time, looping, fading across when the tune changes, at the remembered music volume. It
    /// outlives screens, so the same tune carries on from the menu into a lobby.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        public const string VolumeKey = "musicVolume";

        private static MusicPlayer _instance;
        private static int? _volumeStep;
        private AudioSource _source;
        private string _playing;
        private string _wanted;

        /// <summary>0 while fading out, rising to 1 once the wanted tune is in and fully up.</summary>
        private float _fade;

        public static int VolumeStep
        {
            get => SoundRules.ClampVolumeStep(Accessibility.Get(ref _volumeStep, VolumeKey, SoundRules.DefaultMusicStep));
            private set => Accessibility.Set(ref _volumeStep, VolumeKey, value);
        }

        public static float Volume => SoundRules.VolumeSteps[VolumeStep];

        /// <summary>For the menu button, e.g. "Music: Medium".</summary>
        public static string Label => $"Music: {SoundRules.VolumeName(VolumeStep)}";

        public static void CycleVolume() => VolumeStep = SoundRules.NextVolumeStep(VolumeStep);

        public static AudioClip Clip(string name) => Resources.Load<AudioClip>("Music/" + name);

        /// <summary>Asks for <paramref name="tune"/> (one of <see cref="MusicChoice.All"/>); does nothing if it is already playing.</summary>
        public static void Play(string tune)
        {
            if (_instance == null)
            {
                var host = new GameObject(nameof(MusicPlayer));
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<MusicPlayer>();
            }

            _instance._wanted = tune;
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
        }

        private void Update()
        {
            float step = Time.unscaledDeltaTime / SoundRules.MusicFadeSeconds;
            if (_wanted != _playing)
            {
                // Fade the old tune out, then swap.
                _fade = Mathf.Max(0f, _fade - step);
                if (_fade <= 0f || !_source.isPlaying)
                {
                    _source.Stop();
                    var clip = _wanted != null ? Clip(_wanted) : null;
                    if (_source.clip != null && _source.clip != clip)
                    {
                        Resources.UnloadAsset(_source.clip);
                    }

                    _source.clip = clip;
                    _playing = _wanted;
                    _fade = 0f;
                    if (clip != null)
                    {
                        _source.Play();
                    }
                }
            }
            else
            {
                _fade = Mathf.Min(1f, _fade + step);
            }

            _source.volume = _fade * Volume;
        }
    }
}
