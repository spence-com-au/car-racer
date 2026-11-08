using System;
using System.Collections.Generic;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class DriverInputTests
    {
        [Fact]
        public void TheStrongerSteerWins()
        {
            var touch = new DriverControls { Steer = -0.3f };
            var pad = new DriverControls { Steer = 0.8f };
            Assert.Equal(0.8f, DriverInput.Merge(touch, pad).Steer);
            Assert.Equal(0.8f, DriverInput.Merge(pad, touch).Steer);
        }

        [Fact]
        public void TheFirstDeviceWinsATiedSteer()
        {
            var touch = new DriverControls { Steer = -0.5f };
            var tilt = new DriverControls { Steer = 0.5f };
            Assert.Equal(-0.5f, DriverInput.Merge(touch, tilt).Steer);
            Assert.Equal(0.5f, DriverInput.Merge(tilt, touch).Steer);
        }

        [Fact]
        public void TheHarderPedalPressWins()
        {
            var keyboard = new DriverControls { Accelerate = 0.3f, Brake = 0.9f };
            var pad = new DriverControls { Accelerate = 0.6f, Brake = 0.2f };
            var merged = DriverInput.Merge(keyboard, pad);
            Assert.Equal(0.6f, merged.Accelerate);
            Assert.Equal(0.9f, merged.Brake);
        }

        [Fact]
        public void ButtonsFromEitherDevice()
        {
            var drift = DriverInput.Merge(default, new DriverControls { Drift = true });
            var item = DriverInput.Merge(new DriverControls { UseItem = true }, default);
            var lookBack = DriverInput.Merge(default, new DriverControls { LookBack = true });
            Assert.True(drift.Drift);
            Assert.True(item.UseItem);
            Assert.True(lookBack.LookBack);
            var none = DriverInput.Merge(default, default);
            Assert.False(none.Drift || none.UseItem || none.LookBack);
        }

        [Fact]
        public void AutoAccelerateDrivesAtFullThrottle()
        {
            Assert.Equal(1f, DriverInput.ToCarInput(default, true).Throttle);
            Assert.Equal(0f, DriverInput.ToCarInput(default, false).Throttle);
        }

        [Fact]
        public void ManualThrottleFollowsTheAccelerator()
        {
            Assert.Equal(0.6f, DriverInput.ToCarInput(new DriverControls { Accelerate = 0.6f }, false).Throttle);
        }

        [Fact]
        public void BrakingOverridesAccelerating()
        {
            var controls = new DriverControls { Accelerate = 1f, Brake = 1f };
            Assert.Equal(-1f, DriverInput.ToCarInput(controls, false).Throttle);
            Assert.Equal(-1f, DriverInput.ToCarInput(controls, true).Throttle);
        }

        [Fact]
        public void ATriggerBarelyPressedDoesNotBrake()
        {
            var pad = new DriverControls { Brake = DriverInput.BrakeThreshold / 2f };
            Assert.Equal(1f, DriverInput.ToCarInput(pad, true).Throttle);
        }

        [Fact]
        public void TheCarInputCarriesSteerAndDrift()
        {
            var input = DriverInput.ToCarInput(new DriverControls { Steer = -0.4f, Drift = true }, true);
            Assert.Equal(-0.4f, input.Steer);
            Assert.True(input.Drift);
        }
    }

    public class TouchControlsTests
    {
        // A 1920×1080 landscape phone at 400 dpi with no notch.
        private static readonly TouchControlLayout Layout = new TouchControlLayout(1920f, 1080f, 0f, 0f, 1920f, 1080f, 400f);

        private readonly TouchControls _controls = new TouchControls();

        private DriverControls Frame(params TouchPoint[] touches) =>
            _controls.Update(new List<TouchPoint>(touches), Layout, showGas: false, showItem: true);

        private static TouchPoint Touch(int id, float x, float y, TouchStage stage = TouchStage.Held) => new TouchPoint(id, x, y, stage);

        private static TouchPoint At(int id, Vec2 p, TouchStage stage = TouchStage.Held) => Touch(id, p.X, p.Y, stage);

        [Fact]
        public void LayoutKeepsButtonsInsideTheSafeAreaOnTheRight()
        {
            var notched = new TouchControlLayout(2400f, 1080f, 100f, 0f, 2300f, 1080f, 400f);
            Assert.True(notched.DriftCentre.X + notched.ButtonRadius <= 2300f);
            Assert.True(notched.BrakeCentre.X > notched.SteerZoneRight);
            Assert.True(notched.GasCentre.Y + notched.ButtonRadius <= 1080f);
            Assert.Equal(0.42f * 400f, notched.ButtonRadius, 3);
        }

        [Fact]
        public void LayoutFallsBackWhenTheDpiIsUnknown()
        {
            var layout = new TouchControlLayout(1920f, 1080f, 0f, 0f, 1920f, 1080f, 0f);
            Assert.Equal(TouchControlLayout.ButtonRadiusInches * 1080f / TouchControlLayout.FallbackScreenHeightInches, layout.ButtonRadius, 2);
        }

        [Fact]
        public void DraggingRightOnTheLeftHalfSteersRight()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began));
            var controls = Frame(Touch(1, 400f + Layout.SteerRange / 2f, 320f));
            Assert.InRange(controls.Steer, 0.4f, 0.5f);
            Assert.True(_controls.Steering);
        }

        [Fact]
        public void TheCentreIsWhereTheThumbLands()
        {
            Assert.Equal(0f, Frame(Touch(1, 700f, 300f, TouchStage.Began)).Steer);
            Assert.Equal(0f, Frame(Touch(1, 702f, 300f)).Steer); // inside the dead zone
        }

        [Fact]
        public void DraggingPastFullLockMovesTheCentre()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began));
            Assert.Equal(-1f, Frame(Touch(1, 400f - 3f * Layout.SteerRange, 300f)).Steer);

            // Coming back by one range from the far point reaches straight ahead, not still full left.
            Assert.Equal(0f, Frame(Touch(1, 400f - 2f * Layout.SteerRange, 300f)).Steer, 3);
        }

        [Fact]
        public void LiftingTheThumbStraightensUp()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began));
            Frame(Touch(1, 400f + Layout.SteerRange, 300f));
            Assert.Equal(0f, Frame(Touch(1, 400f + Layout.SteerRange, 300f, TouchStage.Ended)).Steer);
            Assert.False(_controls.Steering);
        }

        [Fact]
        public void TheSteeringThumbKeepsSteeringWhenItStraysRight()
        {
            Frame(Touch(1, 900f, 300f, TouchStage.Began));
            Assert.Equal(1f, Frame(Touch(1, 1100f + Layout.SteerRange, 300f)).Steer);
        }

        [Fact]
        public void ButtonsWorkAlongsideSteering()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began), At(2, Layout.DriftCentre, TouchStage.Began));
            var controls = Frame(Touch(1, 400f + Layout.SteerRange, 300f), At(2, Layout.DriftCentre));
            Assert.True(controls.Drift);
            Assert.Equal(0f, controls.Brake);
            Assert.Equal(1f, controls.Steer, 3);

            controls = Frame(Touch(1, 400f + Layout.SteerRange, 300f), At(3, Layout.BrakeCentre, TouchStage.Began));
            Assert.False(controls.Drift); // finger 2 vanished without an Ended
            Assert.Equal(1f, controls.Brake);
        }

        [Fact]
        public void AButtonStaysHeldWhenTheFingerSlidesOff()
        {
            Frame(At(1, Layout.DriftCentre, TouchStage.Began));
            Assert.True(Frame(Touch(1, Layout.DriftCentre.X, Layout.DriftCentre.Y + 3f * Layout.ButtonRadius)).Drift);
            Assert.False(Frame(Touch(1, 0f, 0f, TouchStage.Ended)).Drift);
        }

        [Fact]
        public void SlidingOntoAButtonFromEmptyScreenPressesIt()
        {
            Frame(Touch(1, 1000f, 1000f, TouchStage.Began)); // right half, just missed the buttons
            Assert.Equal(1f, Frame(At(1, Layout.BrakeCentre)).Brake);
        }

        [Fact]
        public void AThumbRockingFromBrakeToGasLetsGoOfTheBrake()
        {
            var controls = new TouchControls();
            DriverControls Step(TouchPoint touch) => controls.Update(new List<TouchPoint> { touch }, Layout, showGas: true);

            Assert.Equal(1f, Step(At(1, Layout.BrakeCentre, TouchStage.Began)).Brake);
            var onGas = Step(At(1, Layout.GasCentre));
            Assert.Equal(0f, onGas.Brake);
            Assert.Equal(1f, onGas.Accelerate);
            Assert.Equal(1f, Step(At(1, Layout.BrakeCentre)).Brake);
        }

        [Fact]
        public void WhereTheReachesOverlapTheNearerButtonWins()
        {
            // Drift and Brake are 2.43 radii apart and each reaches 1.25 radii, so their reaches overlap.
            var d = Layout.DriftCentre;
            var b = Layout.BrakeCentre;
            var nearerBrake = new Vec2(d.X + 0.51f * (b.X - d.X), d.Y + 0.51f * (b.Y - d.Y));
            Assert.True(Layout.HitsButton(nearerBrake, d));
            var controls = Frame(At(1, nearerBrake, TouchStage.Began));
            Assert.Equal(1f, controls.Brake);
            Assert.False(controls.Drift);
        }

        [Fact]
        public void TheSteeringThumbNeverPressesButtons()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began));
            var controls = Frame(At(1, Layout.BrakeCentre));
            Assert.Equal(0f, controls.Brake);
            Assert.True(_controls.Steering);
        }

        [Fact]
        public void OnlyTheFirstThumbOnTheLeftSteers()
        {
            Frame(Touch(1, 400f, 300f, TouchStage.Began));
            Frame(Touch(1, 400f, 300f), Touch(2, 200f, 600f, TouchStage.Began));
            Assert.Equal(0f, Frame(Touch(1, 400f, 300f), Touch(2, 200f + Layout.SteerRange, 600f)).Steer);
        }

        [Fact]
        public void AFingerFirstSeenMidPressStillCounts()
        {
            Assert.Equal(1f, Frame(At(1, Layout.BrakeCentre)).Brake);
        }

        [Fact]
        public void TappingItemUsesTheItemOnceAndHoldingDoesNotRepeat()
        {
            Assert.True(Frame(At(1, Layout.ItemCentre, TouchStage.Began)).UseItem);
            Assert.True(_controls.ItemHeld);
            Assert.False(Frame(At(1, Layout.ItemCentre)).UseItem);
            Assert.False(Frame(At(1, Layout.ItemCentre, TouchStage.Ended)).UseItem);
            Assert.True(Frame(At(2, Layout.ItemCentre, TouchStage.Began)).UseItem);
        }

        [Fact]
        public void SlidingOntoItemNeverUsesIt()
        {
            Frame(At(1, Layout.BrakeCentre, TouchStage.Began));
            var controls = Frame(At(1, Layout.ItemCentre));
            Assert.False(controls.UseItem);
            Assert.Equal(1f, controls.Brake); // it keeps its pedal over the Item button
            Frame(At(1, Layout.ItemCentre), Touch(2, 1000f, 1000f, TouchStage.Began)); // plus a stray finger on the right
            Assert.False(Frame(At(1, Layout.ItemCentre), At(2, Layout.ItemCentre)).UseItem);
        }

        [Fact]
        public void AFingerOnItemStaysThereWhenItSlides()
        {
            Frame(At(1, Layout.ItemCentre, TouchStage.Began));
            var controls = Frame(At(1, Layout.BrakeCentre));
            Assert.Equal(0f, controls.Brake);
            Assert.True(_controls.ItemHeld);
        }

        [Fact]
        public void AHiddenItemButtonTakesNoFingers()
        {
            var controls = new TouchControls();
            var touches = new List<TouchPoint> { At(1, Layout.ItemCentre, TouchStage.Began) };
            Assert.False(controls.Update(touches, Layout, showGas: false, showItem: false).UseItem);
            Assert.False(controls.ItemHeld);

            // Nor does it win a finger from Brake where their reaches overlap.
            var b = Layout.BrakeCentre;
            var i = Layout.ItemCentre;
            var nearerItem = new Vec2(b.X + 0.51f * (i.X - b.X), b.Y + 0.51f * (i.Y - b.Y));
            Assert.True(Layout.HitsButton(nearerItem, b));
            var hidden = new TouchControls().Update(new List<TouchPoint> { At(2, nearerItem, TouchStage.Began) }, Layout, showGas: false, showItem: false);
            Assert.Equal(1f, hidden.Brake);
        }

        [Fact]
        public void TheItemButtonSitsAboveBrakeInsideTheScreen()
        {
            Assert.Equal(Layout.BrakeCentre.X, Layout.ItemCentre.X);
            Assert.True(Layout.ItemCentre.Y > Layout.BrakeCentre.Y);
            Assert.True(Layout.ItemCentre.Y + Layout.ButtonRadius <= 1080f);
            var low = new TouchControlLayout(1920f, 600f, 0f, 0f, 1920f, 600f, 400f);
            Assert.True(low.ItemCentre.Y + low.ButtonRadius <= 600f);
        }

        [Fact]
        public void HoldingLookBackLooksBackUntilTheFingerLifts()
        {
            var controls = new TouchControls();
            DriverControls Look(params TouchPoint[] touches) =>
                controls.Update(new List<TouchPoint>(touches), Layout, showGas: false, showItem: true, showLookBack: true);

            Assert.True(Look(At(1, Layout.LookBackCentre, TouchStage.Began)).LookBack);
            Assert.True(Look(Touch(1, Layout.LookBackCentre.X, Layout.LookBackCentre.Y - 400f)).LookBack); // sliding off keeps it
            Assert.False(Look(At(1, Layout.LookBackCentre, TouchStage.Ended)).LookBack);
        }

        [Fact]
        public void LookBackTakesOnlyAFingerThatLandsOnIt()
        {
            var controls = new TouchControls();
            var show = new List<TouchPoint> { Touch(1, Layout.LookBackCentre.X + 300f, Layout.LookBackCentre.Y - 300f, TouchStage.Began) };
            controls.Update(show, Layout, showGas: false, showLookBack: true);
            Assert.True(controls.Steering);

            // A steering thumb straying over it keeps steering.
            var slid = controls.Update(new List<TouchPoint> { At(1, Layout.LookBackCentre) }, Layout, showGas: false, showLookBack: true);
            Assert.False(slid.LookBack);
            Assert.True(controls.Steering);

            // Hidden, it takes no fingers: one landing there steers.
            var hidden = new TouchControls();
            Assert.False(hidden.Update(new List<TouchPoint> { At(2, Layout.LookBackCentre, TouchStage.Began) }, Layout, showGas: false).LookBack);
            Assert.True(hidden.Steering);
        }

        [Fact]
        public void TheLookBackButtonSitsTopLeftBelowTheText()
        {
            Assert.True(Layout.LookBackCentre.X < Layout.SteerZoneRight);
            Assert.True(Layout.LookBackCentre.X - Layout.ButtonRadius >= 0f);
            Assert.True(Layout.LookBackCentre.Y + Layout.ButtonRadius <= 1080f - TouchControlLayout.TopTextInches * 400f + 0.01f);
        }

        [Fact]
        public void TheGasButtonOnlyWorksWhenShown()
        {
            var touches = new List<TouchPoint> { At(1, Layout.GasCentre, TouchStage.Began) };
            Assert.Equal(0f, new TouchControls().Update(touches, Layout, showGas: false).Accelerate);
            Assert.Equal(1f, new TouchControls().Update(touches, Layout, showGas: true).Accelerate);
        }
    }

    public class ChaseCameraRigTests
    {
        private const float Dt = 1f / 60f;
        private static readonly ChaseCameraSettings Settings = new ChaseCameraSettings();

        [Fact]
        public void StartsBehindAndAboveTheCar()
        {
            var cam = ChaseCameraRig.Step(default, new Vec3(10f, 0f, 20f), 0f, 0f, Dt, Settings);
            Assert.True(cam.Position.ApproximatelyEquals(new Vec3(10f, Settings.Height, 20f - Settings.Distance)));
            Assert.True(cam.LookAt.ApproximatelyEquals(new Vec3(10f, Settings.LookHeight, 20f + Settings.LookAhead)));
            Assert.Equal(Settings.FieldOfView, cam.FieldOfView);
        }

        [Fact]
        public void FacesTheSameWayAsTheCar()
        {
            // Heading π/2 faces +X, so the camera sits on the -X side.
            var cam = ChaseCameraRig.Step(default, Vec3.Zero, MathF.PI / 2f, 0f, Dt, Settings);
            Assert.True(cam.Position.X < -Settings.Distance + 0.01f);
            Assert.Equal(0f, cam.Position.Z, 3);
        }

        [Fact]
        public void PullsBackAndWidensWithSpeed()
        {
            var cam = Settle(default, Vec3.Zero, 0f, Settings.TopSpeed);
            Assert.Equal(Settings.FieldOfViewAtTopSpeed, cam.FieldOfView, 1);
            Assert.Equal(-Settings.DistanceAtTopSpeed, cam.Position.Z, 1);
            Assert.Equal(Settings.HeightAtTopSpeed, cam.Position.Y, 1);

            // Boosting past top speed doesn't widen any further.
            Assert.Equal(Settings.FieldOfViewAtTopSpeed, Settle(cam, Vec3.Zero, 0f, Settings.TopSpeed * 1.5f).FieldOfView, 1);
        }

        [Fact]
        public void ReversingFramesLikeDrivingForward()
        {
            var forward = Settle(default, Vec3.Zero, 0f, 8f);
            var reverse = Settle(default, Vec3.Zero, 0f, -8f);
            Assert.True(forward.Position.ApproximatelyEquals(reverse.Position, 1e-3f));
        }

        [Fact]
        public void SwingsRoundAfterTheCarRatherThanSnapping()
        {
            var cam = ChaseCameraRig.Step(default, Vec3.Zero, 0f, 0f, Dt, Settings);
            cam = ChaseCameraRig.Step(cam, Vec3.Zero, 1f, 0f, Dt, Settings);
            Assert.InRange(cam.Heading, 0.01f, 0.2f);

            cam = Settle(cam, Vec3.Zero, 1f, 0f);
            Assert.Equal(1f, cam.Heading, 3);
        }

        [Fact]
        public void TurnsTheShortWayAcrossTheWrap()
        {
            var cam = ChaseCameraRig.Step(default, Vec3.Zero, 3f, 0f, Dt, Settings);
            cam = ChaseCameraRig.Step(cam, Vec3.Zero, -3f, 0f, Dt, Settings);

            // From 3 rad to -3 rad is 0.28 rad onward through π, not 6 rad back round.
            Assert.True(cam.Heading > 3f || cam.Heading < -3f, $"heading {cam.Heading}");
        }

        [Fact]
        public void TheRearViewSitsInFrontOfTheCarLookingBack()
        {
            var chase = Settle(default, Vec3.Zero, 0.3f, 0f);
            var rear = ChaseCameraRig.RearView(chase, new Vec3(10f, 0f, 20f), 0f, 0f, Settings);
            Assert.True(rear.Position.ApproximatelyEquals(new Vec3(10f, Settings.Height, 20f + Settings.Distance)));
            Assert.True(rear.LookAt.ApproximatelyEquals(new Vec3(10f, Settings.LookHeight, 20f - Settings.LookAhead)));
            Assert.Equal(chase.FieldOfView, rear.FieldOfView);
            Assert.Equal(MathF.PI, MathF.Abs(rear.Heading), 3);

            // Square to the car, not to the trailing chase heading, and further out at speed.
            var fast = ChaseCameraRig.RearView(chase, Vec3.Zero, MathF.PI / 2f, Settings.TopSpeed, Settings);
            Assert.Equal(Settings.DistanceAtTopSpeed, fast.Position.X, 3);
            Assert.Equal(0f, fast.Position.Z, 3);
        }

        [Fact]
        public void SmoothingDoesNotDependOnFrameRate()
        {
            var start = ChaseCameraRig.Step(default, Vec3.Zero, 0f, 0f, Dt, Settings);
            var target = new Vec3(5f, 0f, 5f);

            var fast = start;
            for (int i = 0; i < 4; i++)
            {
                fast = ChaseCameraRig.Step(fast, target, 0.5f, 10f, Dt / 4f, Settings);
            }

            var slow = ChaseCameraRig.Step(start, target, 0.5f, 10f, Dt, Settings);
            Assert.Equal(slow.Heading, fast.Heading, 4);
            Assert.Equal(slow.FieldOfView, fast.FieldOfView, 3);
        }

        private static ChaseCameraState Settle(ChaseCameraState cam, Vec3 target, float heading, float speed)
        {
            for (int i = 0; i < 600; i++)
            {
                cam = ChaseCameraRig.Step(cam, target, heading, speed, Dt, Settings);
            }

            return cam;
        }
    }
}
