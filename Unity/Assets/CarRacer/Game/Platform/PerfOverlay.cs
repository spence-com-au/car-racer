using CarRacer.Core.Platform;
using CarRacer.Game.Ui;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarRacer.Game.Platform
{
    /// <summary>
    /// The performance readout (design doc §14): frame rate, the slowest recent frame, frames that
    /// missed 30 fps, draw calls, memory and this phone's network traffic, in small text at the top
    /// of the screen over every screen (a UI Toolkit layer above the others). On by default in
    /// development builds; Settings turns it on or off (remembered). The draw-call and memory
    /// counters only report in development builds.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        public const string VisibleKey = "perfReadout";

        /// <summary>This phone's session traffic; <c>NetworkSession</c> counts into it.</summary>
        public static readonly TrafficCounter Traffic = new TrafficCounter();

        private static PerfOverlay _instance;
        private static bool? _visible;

        private readonly FrameStats _frames = new FrameStats();
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _memory;
        private TextElement _readout;
        private float _nextText;

        public static bool Visible
        {
            get => Accessibility.Get(ref _visible, VisibleKey, fallback: Debug.isDebugBuild);
            set => Accessibility.Set(ref _visible, VisibleKey, value);
        }

        /// <summary>For the Settings button.</summary>
        public static string Label => Visible ? "Perf stats: on" : "Perf stats: off";

        /// <summary>Makes the overlay, once; it outlives screens. Counting starts at once, so the readout is ready when shown.</summary>
        public static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(PerfOverlay));
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<PerfOverlay>();
            _instance.Build(UiRoot.Create(host.transform, "Perf readout", order: 100));
        }

        /// <summary>Small text just under the item slot at the top centre, on a dark backdrop; it never takes touches.</summary>
        private void Build(UiRoot layer)
        {
            var strip = UiKit.Row();
            strip.style.position = Position.Absolute;
            strip.style.top = 76f;
            strip.style.left = strip.style.right = 0f;
            strip.style.justifyContent = Justify.Center;
            _readout = UiKit.Centred(UiKit.Text(string.Empty, 16, Color.white, wrap: true));

            // A fixed size for a testing tool, as before, so it needn't rebuild when large text changes.
            _readout.style.fontSize = 16;
            _readout.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _readout.style.paddingLeft = _readout.style.paddingRight = 8f;
            _readout.style.paddingTop = _readout.style.paddingBottom = 4f;
            _readout.style.width = 340f;
            strip.Add(_readout);
            layer.Full.Add(strip);
        }

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _memory.Dispose();
        }

        private void Update()
        {
            _frames.Add(Time.unscaledDeltaTime);
            Traffic.Roll(Time.realtimeSinceStartupAsDouble);
            if (Visible && Time.unscaledTime >= _nextText)
            {
                // A few times a second is readable; every frame would flicker and allocate.
                _nextText = Time.unscaledTime + 0.25f;
                int draws = _drawCalls.Valid ? (int)_drawCalls.LastValue : -1;
                int memoryMb = _memory.Valid ? (int)(_memory.LastValue / (1024 * 1024)) : -1;
                _readout.text = PerfReadout.Format(_frames, draws, memoryMb, CarRacer.Game.Net.NetworkSession.Active ? Traffic : null);
            }

            _readout.style.display = Visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
