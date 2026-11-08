using CarRacer.Core.Arena;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class RampMeshBuilderTests
    {
        private static readonly Ramp Kicker = new Ramp
        {
            Foot = new Vec3(5f, 0f, -3f),
            Forward = new Vec3(1f, 0f, 1f).Normalized,
            Length = 8f,
            Width = 6f,
            Height = 2f,
        };

        [Fact]
        public void TheWedgeIsFourFacesOfSixTrianglesOverFourteenFlatShadedVertices()
        {
            MeshData mesh = RampMeshBuilder.Build(Kicker);

            Assert.Equal(14, mesh.Vertices.Count);
            Assert.Equal(14, mesh.Normals.Count);
            Assert.Equal(14, mesh.Uvs.Count);
            Assert.Equal(6, mesh.TriangleCount);
            Assert.All(mesh.Triangles, index => Assert.InRange(index, 0, mesh.Vertices.Count - 1));
        }

        [Fact]
        public void TheSlopeRisesFromTheFootToTheLipAcrossTheWidth()
        {
            MeshData mesh = RampMeshBuilder.Build(Kicker);
            Vec3 across = Vec3.Cross(Vec3.Up, Kicker.Forward) * (Kicker.Width / 2f);

            Assert.True(mesh.Vertices[0].ApproximatelyEquals(Kicker.Foot - across));
            Assert.True(mesh.Vertices[1].ApproximatelyEquals(Kicker.Lip - across));
            Assert.True(mesh.Vertices[2].ApproximatelyEquals(Kicker.Lip + across));
            Assert.True(mesh.Vertices[3].ApproximatelyEquals(Kicker.Foot + across));
            Assert.Equal(Kicker.Height, mesh.Vertices[1].Y - mesh.Vertices[0].Y, 3);
        }

        [Fact]
        public void EveryTriangleIsClockwiseSeenFromOutsideAndMatchesItsNormals()
        {
            MeshData mesh = RampMeshBuilder.Build(Kicker);
            Vec3 centre = Vec3.Zero;
            foreach (Vec3 vertex in mesh.Vertices)
            {
                centre += vertex / mesh.Vertices.Count;
            }

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                Vec3 a = mesh.Vertices[mesh.Triangles[3 * t]];
                Vec3 b = mesh.Vertices[mesh.Triangles[3 * t + 1]];
                Vec3 c = mesh.Vertices[mesh.Triangles[3 * t + 2]];

                // Unity's front face is clockwise, so its normal is Cross(b - a, c - a).
                Vec3 normal = Vec3.Cross(b - a, c - a).Normalized;
                Assert.True(Vec3.Dot(normal, a - centre) > 0f, $"triangle {t} faces inwards");
                for (int corner = 0; corner < 3; corner++)
                {
                    Vec3 stored = mesh.Normals[mesh.Triangles[3 * t + corner]];
                    Assert.True(stored.ApproximatelyEquals(normal, 1e-3f), $"triangle {t} corner {corner} normal");
                }
            }
        }

        [Fact]
        public void TheSlopeFacesUpAndBackDownTheRamp()
        {
            MeshData mesh = RampMeshBuilder.Build(Kicker);
            Vec3 slope = mesh.Normals[0];

            Assert.True(slope.Y > 0f);
            Assert.True(Vec3.Dot(slope, Kicker.Forward) < 0f);
            Assert.Equal(0f, Vec3.Dot(slope, Vec3.Cross(Vec3.Up, Kicker.Forward)), 3);
        }
    }
}
