using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>How a refused guest is told why, and how long the host waits before disconnecting it.</summary>
    public class RefusalTests
    {
        [Theory]
        [InlineData(JoinResult.SessionFull, "That game is full")]
        [InlineData(JoinResult.VersionMismatch, "That game needs a different version of Car Racer")]
        [InlineData(JoinResult.EventInProgress, "That game has already started")]
        public void EachRefusalHasItsOwnText(JoinResult result, string text)
        {
            Assert.Equal(text, GuestSession.RefusalText(result));
        }

        [Fact]
        public void ARefusalThisBuildDoesNotKnowIsStillExplained()
        {
            // A newer host may have more reasons than this build knows.
            Assert.Equal("Couldn't join", GuestSession.RefusalText((JoinResult)200));
        }

        [Fact]
        public void ARefusalReachesTheGuestAcrossTheWire()
        {
            const ushort hostVersion = 2;
            var hostSide = new RecordingTransport();
            var host = new HostSession(hostSide, "Host", hostVersion, () => 0);
            var guest = new GuestSession(new RecordingTransport(), "Old", hostVersion - 1, () => 0);
            host.OnMessage(3, new JoinRequestMessage { ProtocolVersion = hostVersion - 1, PlayerName = "Old" });

            byte[] frame = MessageCodec.EncodeFrame(hostSide.To<JoinReplyMessage>(3).Single());
            Assert.True(MessageCodec.TryDecodeFrame(frame, out var reply, out string error), error);
            guest.OnMessage(reply);

            Assert.Equal(GuestState.Refused, guest.State);
            Assert.Equal("That game needs a different version of Car Racer", GuestSession.RefusalText(guest.RefusalReason));
            Assert.Equal(new ulong[] { 3 }, hostSide.Disconnected);
        }

        [Fact]
        public void NothingIsDueBeforeItsTime()
        {
            var disconnects = new DelayedDisconnects();
            disconnects.Add(7, atTime: 10.5f);

            Assert.False(disconnects.TryTakeDue(10.4f, out _));
        }

        [Fact]
        public void AConnectionIsDueFromItsTimeAndOnlyOnce()
        {
            var disconnects = new DelayedDisconnects();
            disconnects.Add(7, atTime: 10.5f);

            Assert.True(disconnects.TryTakeDue(10.5f, out ulong connection));
            Assert.Equal(7UL, connection);
            Assert.False(disconnects.TryTakeDue(99f, out _));
        }

        [Fact]
        public void OnlyTheConnectionsWhoseTimeHasComeAreTaken()
        {
            var disconnects = new DelayedDisconnects();
            disconnects.Add(1, atTime: 10f);
            disconnects.Add(2, atTime: 12f);
            disconnects.Add(3, atTime: 11f);

            var due = new List<ulong>();
            while (disconnects.TryTakeDue(11f, out ulong connection))
            {
                due.Add(connection);
            }

            Assert.Equal(new ulong[] { 1, 3 }, due.OrderBy(c => c));
            Assert.True(disconnects.TryTakeDue(12f, out ulong last));
            Assert.Equal(2UL, last);
        }

        [Fact]
        public void ASoonerConnectionIsNotHeldUpByOneAddedBeforeIt()
        {
            var disconnects = new DelayedDisconnects();
            disconnects.Add(1, atTime: 20f);
            disconnects.Add(2, atTime: 10f);

            Assert.True(disconnects.TryTakeDue(10f, out ulong connection));
            Assert.Equal(2UL, connection);
            Assert.False(disconnects.TryTakeDue(10f, out _));
        }
    }
}
