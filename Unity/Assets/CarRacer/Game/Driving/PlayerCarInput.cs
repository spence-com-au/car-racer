using System.Collections.Generic;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using CarRacer.Game.Platform;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CarRacer.Game.Driving
{
    /// <summary>
    /// The local player's controls (design doc §5): on-screen touch controls, keyboard, gamepad and
    /// (if turned on) tilt, all usable at once. Reads devices every frame; the car samples <see cref="Read"/> each physics step.
    /// Uses the Input System when it is the active input handler, otherwise the legacy Input Manager.
    /// </summary>
    public sealed class PlayerCarInput : MonoBehaviour, ICarInputSource
    {
        private const string AutoAccelerateKey = "autoAccelerate";

        private static bool? _autoAccelerateEnabled;
        private readonly TouchControls _touchControls = new TouchControls();
        private readonly List<TouchPoint> _touches = new List<TouchPoint>();
        private CarInput _current;
        private bool _useTilt;
        private bool _itemPressed;
        private Rect _safeArea;

        public TouchControls TouchState => _touchControls;

        /// <summary>Whether the Item button is on screen (in a race); while it isn't, its spot takes no fingers.</summary>
        public bool ShowItem { get; set; }

        /// <summary>Whether the Look back button is on screen (with the chase camera); while it isn't, its spot takes no fingers.</summary>
        public bool ShowLookBack { get; set; }

        /// <summary>The rear-view glance is held: the Look back button, Q, or a gamepad's left stick click.</summary>
        public bool LookingBack { get; private set; }
        public TouchControlLayout Layout { get; private set; }

        /// <summary>Auto-accelerate (design doc §5): on by default; the choice is remembered.</summary>
        public static bool AutoAccelerateEnabled
        {
            get => Accessibility.Get(ref _autoAccelerateEnabled, AutoAccelerateKey, fallback: true);
            set => Accessibility.Set(ref _autoAccelerateEnabled, AutoAccelerateKey, value);
        }

        public static string AutoAccelerateLabel => AutoAccelerateEnabled ? "Auto-accelerate: on" : "Auto-accelerate: off";

        /// <summary>Tilt steering (design doc §5): off by default; the choice is remembered.</summary>
        public static bool TiltEnabled
        {
            get => PlayerPrefs.GetInt(TiltSteering.PreferenceKey, 0) != 0;
            set => PlayerPrefs.SetInt(TiltSteering.PreferenceKey, value ? 1 : 0);
        }

        public CarInput Read() => _current;

        /// <summary>True once per press of the use-item control (Item button, E, or a gamepad's Y/X), then false until the next press.</summary>
        public bool ConsumeItemPress()
        {
            bool pressed = _itemPressed;
            _itemPressed = false;
            return pressed;
        }

        private void Awake()
        {
            _useTilt = TiltEnabled;
        }

        private void Update()
        {
            var safe = Screen.safeArea;
            float buttonScale = Accessibility.LargeControls ? TouchControlLayout.LargeButtonScale : 1f;
            if (Layout == null || Layout.ScreenWidth != Screen.width || Layout.ScreenHeight != Screen.height || safe != _safeArea
                || Layout.LeftHanded != Accessibility.LeftHanded || Layout.ButtonScale != buttonScale)
            {
                _safeArea = safe;
                Layout = new TouchControlLayout(Screen.width, Screen.height, safe.xMin, safe.yMin, safe.xMax, safe.yMax, Screen.dpi, Accessibility.LeftHanded, buttonScale);
            }

            ReadTouches(_touches);
            var touch = _touchControls.Update(_touches, Layout, showGas: !AutoAccelerateEnabled, showItem: ShowItem, showLookBack: ShowLookBack);
            var devices = ReadDevices();
            if (_useTilt)
            {
                // Tilt is one more device: the strongest steer wins, so a thumb can still correct it.
                devices = DriverInput.Merge(devices, new DriverControls { Steer = ReadTilt() });
            }

            var controls = DriverInput.Merge(touch, devices);
            _current = DriverInput.ToCarInput(controls, AutoAccelerateEnabled);
            _itemPressed |= controls.UseItem;
            LookingBack = controls.LookBack;
        }

#if ENABLE_INPUT_SYSTEM
        private static void ReadTouches(List<TouchPoint> touches)
        {
            touches.Clear();
            var screen = Touchscreen.current;
            if (screen == null)
            {
                return;
            }

            foreach (var touch in screen.touches)
            {
                var phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None)
                {
                    continue;
                }

                var stage = phase == UnityEngine.InputSystem.TouchPhase.Began ? TouchStage.Began
                    : phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled ? TouchStage.Ended
                    : TouchStage.Held;
                var position = touch.position.ReadValue();
                touches.Add(new TouchPoint(touch.touchId.ReadValue(), position.x, position.y, stage));
            }
        }

        private static float ReadTilt()
        {
            var accelerometer = Accelerometer.current;
            if (accelerometer == null)
            {
                return 0f;
            }

            if (!accelerometer.enabled)
            {
                InputSystem.EnableDevice(accelerometer);
            }

            // With orientation compensation on, the Input System already reports in screen axes.
            var a = accelerometer.acceleration.ReadValue();
            var screen = InputSystem.settings.compensateForScreenOrientation
                ? new Vec2(a.x, a.y)
                : TiltSteering.ToScreen(a.x, a.y, Screen.orientation == ScreenOrientation.LandscapeLeft);
            return TiltSteering.Steer(screen);
        }

        private static DriverControls ReadDevices()
        {
            var controls = new DriverControls();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool left = keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed;
                bool right = keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed;
                controls.Steer = (right ? 1f : 0f) - (left ? 1f : 0f);
                controls.Accelerate = keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed ? 1f : 0f;
                controls.Brake = keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed ? 1f : 0f;
                controls.Drift = keyboard.spaceKey.isPressed || keyboard.leftShiftKey.isPressed;
                controls.UseItem = keyboard.eKey.wasPressedThisFrame || keyboard.leftCtrlKey.wasPressedThisFrame;
                controls.LookBack = keyboard.qKey.isPressed;
            }

            // Left stick steers; A or the right trigger accelerates; B or the left trigger brakes; either shoulder drifts; Y or X uses the item;
            // clicking the left stick looks back.
            var pad = Gamepad.current;
            if (pad != null)
            {
                controls = DriverInput.Merge(controls, new DriverControls
                {
                    Steer = pad.leftStick.x.ReadValue(),
                    Accelerate = Mathf.Max(pad.rightTrigger.ReadValue(), pad.buttonSouth.isPressed ? 1f : 0f),
                    Brake = Mathf.Max(pad.leftTrigger.ReadValue(), pad.buttonEast.isPressed ? 1f : 0f),
                    Drift = pad.rightShoulder.isPressed || pad.leftShoulder.isPressed,
                    UseItem = pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame,
                    LookBack = pad.leftStickButton.isPressed,
                });
            }

            return controls;
        }
#else
        private static void ReadTouches(List<TouchPoint> touches)
        {
            touches.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                var stage = touch.phase == UnityEngine.TouchPhase.Began ? TouchStage.Began
                    : touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled ? TouchStage.Ended
                    : TouchStage.Held;
                touches.Add(new TouchPoint(touch.fingerId, touch.position.x, touch.position.y, stage));
            }
        }

        private static float ReadTilt()
        {
            // The Input Manager reports in the device's own axes, whatever way round the screen is.
            var a = Input.acceleration;
            return TiltSteering.Steer(TiltSteering.ToScreen(a.x, a.y, Screen.orientation == ScreenOrientation.LandscapeLeft));
        }

        private static DriverControls ReadDevices()
        {
            // The default Input Manager axes cover the arrow keys, WASD and a gamepad's left stick.
            return new DriverControls
            {
                Steer = Input.GetAxisRaw("Horizontal"),
                Accelerate = Mathf.Max(0f, Input.GetAxisRaw("Vertical")),
                Brake = Mathf.Max(0f, -Input.GetAxisRaw("Vertical")),
                Drift = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton5) || Input.GetKey(KeyCode.JoystickButton4),
                UseItem = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.JoystickButton3) || Input.GetKeyDown(KeyCode.JoystickButton2),
                LookBack = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.JoystickButton8),
            };
        }
#endif
    }
}
