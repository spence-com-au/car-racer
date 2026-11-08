using System;
using System.IO;
using System.Linq;
using CarRacer.Core.Art;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ObjModelTests
    {
        private const string Quad = @"# a unit square facing up (anticlockwise seen from above, as OBJ winds front faces), in two groups
v 0 0 0
v 1 0 0
v 1 0 1
v 0 0 1
vt 0 0
vt 1 0
vt 1 1
vt 0 1
vn 0 1 0
g floor
f 1/1/1 4/4/1 3/3/1 2/2/1
g extra
f -4/-4/-1 -3/-3/-1 -2/-2/-1
";

        [Fact]
        public void ReadsGroupsAndSplitsQuadsIntoTriangles()
        {
            var model = ObjModel.Parse(Quad);
            Assert.Equal(new[] { "floor", "extra" }, model.Parts.Select(p => p.Name));
            Assert.Equal(6, model.Parts[0].Triangles.Count);
            Assert.Equal(6, model.Parts[0].Positions.Count);
            Assert.Equal(3, model.TriangleCount);
        }

        [Fact]
        public void MirrorsXForUnityAndKeepsUvsAndNormals()
        {
            var model = ObjModel.Parse(Quad);
            var floor = model.Parts[0];
            // The first triangle is corners 1, 4, 3 rewound to 1, 3, 4: vertex 3 at (1, 0, 1) is mirrored.
            Assert.Equal(new Vec3(-1f, 0f, 1f), floor.Positions[1]);
            Assert.Equal(new Vec2(1f, 1f), floor.Uvs[1]);
            Assert.All(floor.Normals, n => Assert.Equal(Vec3.Up, n));
            Assert.Equal(new Vec3(-1f, 0f, 0f), model.Min);
            Assert.Equal(new Vec3(0f, 0f, 1f), model.Max);
        }

        [Fact]
        public void RewindsTrianglesSoTheyStillFaceOutward()
        {
            // Seen from above (+y) in Unity's left-handed axes, a front face winds clockwise.
            var floor = ObjModel.Parse(Quad).Parts[0];
            var a = floor.Positions[0];
            var b = floor.Positions[1];
            var c = floor.Positions[2];
            var ab = b - a;
            var ac = c - a;

            // Left-handed cross product's y: positive means clockwise seen from above.
            float y = ab.Z * ac.X - ab.X * ac.Z;
            Assert.True(y > 0f);
        }

        [Fact]
        public void NegativeIndicesCountBackFromTheEnd()
        {
            var extra = ObjModel.Parse(Quad).Parts[1];
            Assert.Equal(3, extra.Positions.Count);
            Assert.Contains(new Vec3(0f, 0f, 0f), extra.Positions);
            Assert.Contains(new Vec3(-1f, 0f, 1f), extra.Positions);
        }

        [Fact]
        public void RepeatedGroupDrawsItsFacesOnce()
        {
            // As Kenney's Racing Kit writes it: the group again, with no new vertices and the same faces.
            var model = ObjModel.Parse(Quad + "g floor\nf 1/1/1 4/4/1 3/3/1 2/2/1\n");
            Assert.Equal(new[] { "floor", "extra" }, model.Parts.Select(p => p.Name));
            Assert.Equal(3, model.TriangleCount);
        }
    }

    public class PaintRecolourTests
    {
        [Fact]
        public void CellsCountFromTheTopLeft()
        {
            var strip = UvRect.Cells(16, 13, 4, 1, 4);
            Assert.Equal(13f / 16f, strip.UMin);
            Assert.Equal(14f / 16f, strip.UMax);
            Assert.Equal(0.5f, strip.VMin);
            Assert.Equal(0.75f, strip.VMax);
        }

        [Fact]
        public void RecoloursOnlyTheAreaAndKeepsItsShading()
        {
            // A 2x2 texture: the bottom row is a dark and a light red, the top row stays green.
            byte[] rgba =
            {
                100, 0, 0, 255, 200, 0, 0, 255,
                0, 200, 0, 255, 0, 200, 0, 255,
            };
            PaintRecolour.Recolour(rgba, 2, 2, new UvRect(0f, 0f, 1f, 0.5f), 0, 0, 120);

            // Dark stays darker than light, both now blue, averaging the asked-for blue.
            Assert.Equal(0, rgba[0]);
            Assert.Equal(80, rgba[2]);
            Assert.Equal(160, rgba[6]);
            Assert.Equal(new byte[] { 0, 200, 0, 255, 0, 200, 0, 255 }, rgba.Skip(8).ToArray());
        }

        [Theory]
        [InlineData(0.5f, 0f, 0.5f, 1f)] // no width
        [InlineData(0f, 0.5f, 1f, 0.5f)] // no height
        [InlineData(1f, 0f, 0f, 1f)] // back to front
        [InlineData(2f, 0f, 3f, 1f)] // right of the texture
        [InlineData(-1f, 0f, 0f, 1f)] // left of the texture
        [InlineData(0f, 2f, 1f, 3f)] // above the texture
        [InlineData(0f, -1f, 1f, 0f)] // below the texture
        public void LeavesTheTextureAloneForAnAreaWithNoPixelsInIt(float uMin, float vMin, float uMax, float vMax)
        {
            byte[] rgba = { 100, 0, 0, 255, 200, 0, 0, 255, 0, 200, 0, 255, 0, 200, 0, 255 };
            byte[] original = (byte[])rgba.Clone();

            PaintRecolour.Recolour(rgba, 2, 2, new UvRect(uMin, vMin, uMax, vMax), 0, 0, 120);

            Assert.Equal(original, rgba);
        }

        [Fact]
        public void ClipsAnAreaHangingOffTheTextureToTheEdge()
        {
            byte[] rgba = { 100, 0, 0, 255, 200, 0, 0, 255, 0, 200, 0, 255, 0, 200, 0, 255 };

            PaintRecolour.Recolour(rgba, 2, 2, new UvRect(-0.5f, -0.5f, 0.5f, 0.5f), 0, 0, 120);

            // Only the bottom-left pixel is inside the texture and in the area, so it takes the whole colour.
            Assert.Equal(new byte[] { 0, 0, 120, 255, 200, 0, 0, 255, 0, 200, 0, 255, 0, 200, 0, 255 }, rgba);
        }

        [Fact]
        public void PaintsAnAllBlackAreaBlackRatherThanDividingByZero()
        {
            // A mean of 0 would make every shade NaN, and casting NaN to a byte is unspecified.
            byte[] rgba = { 0, 0, 0, 255, 0, 0, 0, 255 };

            PaintRecolour.Recolour(rgba, 2, 1, new UvRect(0f, 0f, 1f, 1f), 200, 100, 50);

            Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255 }, rgba);
        }

        [Fact]
        public void ClampsAChannelBrighterThanTheColourCanReach()
        {
            // The mean grey is 50, so the 200 pixel is four times as light as average and 100 * 4 would wrap a byte.
            byte[] rgba = { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 200, 200, 200, 255 };

            PaintRecolour.Recolour(rgba, 4, 1, new UvRect(0f, 0f, 1f, 1f), 100, 50, 0);

            Assert.Equal(new byte[] { 255, 200, 0, 255 }, rgba.Skip(12).ToArray());
        }
    }

    public class CarModelAssetTests
    {
        [Fact]
        public void EveryBodyIsInTheCatalogue()
        {
            Assert.Equal(LobbyRoster.BodyCount, CarModels.Bodies.Length);
            Assert.Equal(CarModels.Bodies.Length, CarModels.Bodies.Select(b => b.Name).Distinct().Count());
            Assert.Same(CarModels.Bodies[1], CarModels.ForIndex(1 + CarModels.Bodies.Length));
            Assert.Equal("Race future", CarModels.DisplayName(CarModels.Bodies[1]));
            Assert.Equal(CarModels.Bodies.Length, CarModels.Bodies.Select(CarModels.DisplayName).Distinct().Count());
            Assert.Contains("SUV", CarModels.Bodies.Select(CarModels.DisplayName));
            Assert.Contains("Kart", CarModels.Bodies.Select(CarModels.DisplayName));
        }

        [Fact]
        public void EveryBodyLoadsDrawnTheSameLengthWithItsPaintInTheStrip()
        {
            foreach (var body in CarModels.Bodies)
            {
                var model = ObjModel.Parse(File.ReadAllText(ModelFiles.Resolve(body.Name + ".obj.bytes")));
                Assert.Equal(CarModels.Length, (model.Max.Z - model.Min.Z) * body.Scale, 1);
                Assert.Equal(4, model.Parts.Count(p => p.Name.StartsWith("wheel-", StringComparison.Ordinal) && p.Name.Split('-').Length == 3));

                // Some of the bodywork is painted from the recoloured strip.
                var paint = body.Paint;
                bool painted = model.Parts.Where(p => !p.Name.StartsWith("wheel", StringComparison.Ordinal))
                    .SelectMany(p => p.Uvs)
                    .Any(uv => uv.X >= paint.UMin && uv.X < paint.UMax && uv.Y >= paint.VMin && uv.Y < paint.VMax);
                Assert.True(painted, body.Name);
            }
        }

        [Fact]
        public void TheRaceCarHasABodyAndFourWheelsFacingForward()
        {
            var model = ObjModel.Parse(File.ReadAllText(ModelFiles.Resolve(CarModels.Race.Name + ".obj.bytes")));
            Assert.Contains(model.Parts, p => p.Name == "body");
            Assert.Equal(4, model.Parts.FindAll(p => p.Name.StartsWith("wheel-", StringComparison.Ordinal)).Count);

            // About a car's size, sitting on the ground.
            Assert.Equal(0f, model.Min.Y, 2);
            Assert.InRange(model.Max.Z - model.Min.Z, 2.3f, 2.8f);
            Assert.InRange((model.Max.Z - model.Min.Z) * CarModels.Race.Scale, 2.3f, 2.5f);

            // Front wheels ahead (+z), and the left ones on the left (-x) once mirrored for Unity.
            var frontLeft = model.Parts.Find(p => p.Name == "wheel-front-left");
            float z = 0f;
            float x = 0f;
            foreach (var position in frontLeft.Positions)
            {
                z += position.Z;
                x += position.X;
            }

            Assert.True(z > 0f);
            Assert.True(x < 0f);
        }

        [Fact]
        public void ThePaletteIsThere()
        {
            byte[] png = File.ReadAllBytes(ModelFiles.Resolve(CarModels.Palette + ".png.bytes"));
            Assert.Equal(0x89, png[0]);
            Assert.Equal((byte)'P', png[1]);
        }
    }
}
