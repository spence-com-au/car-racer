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
            var announcement = new DiscoveryAnnouncement
            {
                SessionId = 0xDEADBEEF,
                HostName = "Unity",
                GamePort = 51234,
                PlayerCount = 3,
                MaxPlayers = 5,
                State = HostSessionState.InEvent,
            };
            byte[] bytes = announcement.Encode();
            Assert.IsTrue(DiscoveryAnnouncement.TryDecode(bytes, bytes.Length, out var decoded));
            Assert.AreEqual(announcement.ProtocolVersion, decoded.ProtocolVersion);
            Assert.AreEqual(announcement.SessionId, decoded.SessionId);
            Assert.AreEqual(announcement.HostName, decoded.HostName);
            Assert.AreEqual(announcement.GamePort, decoded.GamePort);
            Assert.AreEqual(announcement.PlayerCount, decoded.PlayerCount);
            Assert.AreEqual(announcement.MaxPlayers, decoded.MaxPlayers);
            Assert.AreEqual(announcement.State, decoded.State);
        }

        [Test]
        public void MessagesRoundTripInUnity()
        {
            var state = new CarStateMessage
            {
                PlayerId = 2,
                Sequence = 0xCAFEBABE,
                SentAtMs = 0x123456789ABCL,
                Position = new Vec3(1f, 2f, 3f),
                Velocity = new Vec3(-4f, 5.5f, -6.25f),
                HeadingRadians = 1.5f,
                Flags = CarStateFlags.Drifting | CarStateFlags.Shielded,
            };
            var decoded = (CarStateMessage)MessageCodec.Decode(MessageCodec.Encode(state));
            Assert.AreEqual(state.PlayerId, decoded.PlayerId);
            Assert.AreEqual(state.Sequence, decoded.Sequence);
            Assert.AreEqual(state.SentAtMs, decoded.SentAtMs);
            Assert.AreEqual(state.Position, decoded.Position);
            Assert.AreEqual(state.Velocity, decoded.Velocity);
            Assert.AreEqual(state.HeadingRadians, decoded.HeadingRadians);
            Assert.AreEqual(state.Flags, decoded.Flags);
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
