using System;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;

namespace CarRacer.Core.Tests
{
    /// <summary>Records everything sent through it and delivers nothing.</summary>
    internal sealed class RecordingTransport : ISessionTransport
    {
        public readonly List<(ulong To, IGameMessage Message, bool Reliable)> Sent = new List<(ulong, IGameMessage, bool)>();
        public readonly List<ulong> Disconnected = new List<ulong>();

        public void Send(ulong connection, IGameMessage message, bool reliable) => Sent.Add((connection, message, reliable));

        public void Disconnect(ulong connection) => Disconnected.Add(connection);

        public List<T> To<T>(ulong connection) => Sent.Where(s => s.To == connection).Select(s => s.Message).OfType<T>().ToList();
    }

    /// <summary>The host's end of a loopback to one guest: records each message and hands it straight to the guest.</summary>
    internal sealed class HostLink : ISessionTransport
    {
        public readonly List<IGameMessage> Sent = new List<IGameMessage>();
        public GuestSession Guest;

        public void Send(ulong connection, IGameMessage message, bool reliable)
        {
            Sent.Add(message);
            Guest?.OnMessage(message);
        }

        public void Disconnect(ulong connection)
        {
        }

        /// <summary>
        /// Joins a guest to <paramref name="host"/> over this link and syncs its clock, which shares the host's, over a
        /// 20 ms round trip.
        /// </summary>
        public GuestSession Join(HostSession host, string name, ushort version, Func<long> now)
        {
            Guest = new GuestSession(new GuestLink { Host = host }, name, version, now);
            Guest.OnConnected();
            Guest.OnMessage(new ClockPongMessage { GuestSentMs = now() - 20, HostMs = now() - 10 });
            return Guest;
        }
    }

    /// <summary>A guest's end of a loopback: hands each message straight to the host, from <see cref="Connection"/>.</summary>
    internal sealed class GuestLink : ISessionTransport
    {
        public const ulong Connection = 7;

        public HostSession Host;

        public void Send(ulong connection, IGameMessage message, bool reliable) => Host.OnMessage(Connection, message);

        public void Disconnect(ulong connection)
        {
        }
    }
}
