using System.IO;
using System.Linq;
using CarRacer.Core.Audio;
using CarRacer.Game.Audio;
using NUnit.Framework;
using UnityEngine;

namespace CarRacer.Tests
{
    /// <summary>The sound effects import as audio, and every clip the game asks for is there.</summary>
    public class AudioTests
    {
        private static readonly Sfx[] AllClips = (Sfx[])System.Enum.GetValues(typeof(Sfx));

        [Test]
        public void EveryClipLoads()
        {
            foreach (var sfx in AllClips)
            {
                var clip = GameAudio.Clip(sfx);
                Assert.IsNotNull(clip, sfx.ToString());
                Assert.Greater(clip.length, 0f, sfx.ToString());
            }
        }

        [Test]
        public void EveryFileInTheFolderIsAKnownClip()
        {
            var files = Directory.GetFiles(Path.Combine(Application.dataPath, "CarRacer/Resources/Audio"), "*.ogg")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n);
            CollectionAssert.AreEqual(AllClips.Select(GameAudio.FileName).OrderBy(n => n), files);
        }

        [Test]
        public void EveryTuneLoadsAndStreams()
        {
            foreach (string name in MusicChoice.All)
            {
                var clip = MusicPlayer.Clip(name);
                Assert.IsNotNull(clip, name);
                Assert.AreEqual(AudioClipLoadType.Streaming, clip.loadType, name);
                Assert.Greater(clip.length, 20f, name);
            }

            var files = Directory.GetFiles(Path.Combine(Application.dataPath, "CarRacer/Resources/Music"), "*.ogg")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n);
            CollectionAssert.AreEqual(MusicChoice.All.OrderBy(n => n), files);
        }

        [Test]
        public void TheEngineNoteIsMadeInCode()
        {
            var clip = GameAudio.EngineClip();
            Assert.AreEqual(1, clip.channels);
            Assert.Greater(clip.samples, 0);
        }

        [Test]
        public void EveryMenuTickIsMadeInCode()
        {
            foreach (UiSound sound in System.Enum.GetValues(typeof(UiSound)))
            {
                var clip = GameAudio.UiClip(sound);
                Assert.AreEqual(1, clip.channels, sound.ToString());
                Assert.AreEqual(UiTone.Make(sound).Length, clip.samples, sound.ToString());
            }
        }
    }
}
