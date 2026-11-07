using CarRacer.Core.Discovery;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;
using CarRacer.Game;
using NUnit.Framework;
using UnityEngine;

namespace CarRacer.Tests
{
    /// <summary>
    /// The core logic is tested thoroughly by `dotnet test`; these check it compiles and
    /// behaves the same inside the Unity Editor's runtime (Mono, not IL2CPP: JsonUtility, meshes).
    /// </summary>
    public class CoreIntegrationTests
    {
        [Test]
        public void DiscoveryAnnouncementRoundTripsInUnity()
        {
            var announcement = new DiscoveryAnnouncement { SessionId = 7, HostName = "Unity" };
            byte[] bytes = announcement.Encode();
            Assert.IsTrue(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            Assert.AreEqual("Unity", decoded.HostName);
        }

        [Test]
        public void MessagesRoundTripInUnity()
        {
            var state = new CarStateMessage { PlayerId = 2, Sequence = 9, Position = new Vec3(1f, 2f, 3f) };
            var decoded = (CarStateMessage)MessageCodec.Decode(MessageCodec.Encode(state));
            Assert.AreEqual(new Vec3(1f, 2f, 3f), decoded.Position);
        }

        [Test]
        public void TrackDefinitionLoadsFromJson()
        {
            string json = JsonUtility.ToJson(SampleTracks.Oval());
            var loaded = JsonUtility.FromJson<TrackDefinition>(json);
            Assert.AreEqual("oval", loaded.Id);
            Assert.AreEqual(10, loaded.Points.Length);
            Assert.AreEqual(16f, loaded.Points[2].Width);
            CollectionAssert.IsEmpty(loaded.Validate());
        }

        [Test]
        public void GeneratedRoadBecomesAUnityMesh()
        {
            var layout = new TrackLayout(SampleTracks.Oval());
            var trackMesh = RoadMeshBuilder.Build(layout.Spline, layout.Definition.WallHeight);
            Mesh mesh = trackMesh.Road.ToUnityMesh("Road");
            try
            {
                Assert.AreEqual(trackMesh.Road.Vertices.Count, mesh.vertexCount);
                Assert.AreEqual(trackMesh.Road.Triangles.Count, mesh.triangles.Length);
                Assert.Greater(mesh.bounds.size.x, 50f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
