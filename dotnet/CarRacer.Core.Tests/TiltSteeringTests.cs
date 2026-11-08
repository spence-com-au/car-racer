using System;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class TiltSteeringTests
    {
        /// <summary>Gravity in screen axes with the phone turned clockwise by <paramref name="degrees"/>.</summary>
        private static Vec2 Turned(float degrees)
        {
            float r = degrees * MathF.PI / 180f;
            return new Vec2(MathF.Sin(r), -MathF.Cos(r));
        }

        [Fact]
        public void HeldLevelDrivesStraight()
        {
            Assert.Equal(0f, TiltSteering.Steer(new Vec2(0f, -1f)));
            Assert.Equal(0f, TiltSteering.Steer(Turned(TiltSteering.DeadZoneDegrees - 0.5f)));
        }

        [Fact]
        public void TurningClockwiseSteersRight()
        {
            Assert.True(TiltSteering.Steer(Turned(15f)) > 0f);
            Assert.True(TiltSteering.Steer(Turned(-15f)) < 0f);
            Assert.Equal(-TiltSteering.Steer(Turned(15f)), TiltSteering.Steer(Turned(-15f)), 5);
        }

        [Theory]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        public void SteeringRisesLinearlyFromTheDeadZoneToFullLock(float fraction)
        {
            float span = TiltSteering.FullLockDegrees - TiltSteering.DeadZoneDegrees;
            float degrees = TiltSteering.DeadZoneDegrees + fraction * span;
            Assert.Equal(fraction, TiltSteering.Steer(Turned(degrees)), 4);
            Assert.Equal(-fraction, TiltSteering.Steer(Turned(-degrees)), 4);
        }

        [Fact]
        public void FullLockAtThirtyDegreesAndNoFurther()
        {
            Assert.Equal(1f, TiltSteering.Steer(Turned(TiltSteering.FullLockDegrees)), 4);
            Assert.Equal(1f, TiltSteering.Steer(Turned(80f)));
            Assert.Equal(-1f, TiltSteering.Steer(Turned(-80f)));
        }

        [Fact]
        public void LyingFlatSteersStraight()
        {
            Assert.Equal(0f, TiltSteering.Steer(new Vec2(0.2f, -0.1f)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EitherLandscapeWayRoundReadsTheSame(bool landscapeLeft)
        {
            // At rest gravity points down the screen: in device axes that is -x when turned
            // anticlockwise (landscape left) and +x when turned clockwise.
            float restX = landscapeLeft ? -1f : 1f;
            var level = TiltSteering.ToScreen(restX, 0f, landscapeLeft);
            Assert.Equal(0f, level.X, 5);
            Assert.Equal(-1f, level.Y, 5);

            // Turning the phone clockwise by 20°: gravity turns 20° anticlockwise in the device's axes.
            float r = 20f * MathF.PI / 180f;
            float x = restX * MathF.Cos(r);
            float y = restX * MathF.Sin(r);
            Assert.Equal(TiltSteering.Steer(Turned(20f)), TiltSteering.Steer(TiltSteering.ToScreen(x, y, landscapeLeft)), 4);
        }
    }
}
