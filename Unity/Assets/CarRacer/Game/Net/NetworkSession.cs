using System;
using System.Collections.Generic;
using CarRacer.Core.Discovery;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Game.Driving;
using CarRacer.Game.Platform;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CarRacer.Game.Net
{
    /// <summary>
    /// A hosted or joined LAN session (design doc §1, §2.1). Owns an NGO NetworkManager with Unity
    /// Transport, created from code, and carries the Core session protocol over one NGO custom
    /// message. The protocol itself lives in <see cref="HostSession"/> and <see cref="GuestSession"/>;
    /// this class only moves bytes, tracks connection events and, while hosting, announces the
    /// session on the LAN. It lives on its own root object (NGO expects the NetworkManager at the
    /// root of the hierarchy); destroying that object shuts the session down.
    /// </summary>
    public sealed class NetworkSession : MonoBehaviour, ISessionTransport
    {
        public const string MessageName = "car-racer";

        /// <summary>A refused guest is disconnected after this long, so the reply explaining why reaches it first.</summary>
        public const float RefusalDisconnectDelaySeconds = 0.5f;

        /// <summary>Assumed one-way latency to guests on a LAN; the host has no clock samples of its own.</summary>
        public const float HostLatencyGuessMs = 15f;

        private static int _live;

        private readonly List<(ulong Connection, float AtTime)> _pendingDisconnects = new List<(ulong, float)>();
        private NetworkManager _network;
        private DiscoveryAnnouncer _announcer;
        private DiscoveryAnnouncement _announcement;

        public bool IsHost { get; private set; }

        /// <summary>True on a dedicated host (a server, design doc §2.3): hosting with no player, car or screens of its own.</summary>
        public bool IsDedicated => IsHost && HostSide.IsDedicated;

        public HostSession HostSide { get; private set; }
        public GuestSession GuestSide { get; private set; }

        /// <summary>A short line for the HUD: connecting, joined, refused, disconnected…</summary>
        public string Status { get; private set; } = string.Empty;

        /// <summary>True once the session can't continue (failed to start, refused, or lost the host).</summary>
        public bool Ended { get; private set; }

        /// <summary>The port the host listens on (for the join link in its QR code); 0 for a guest.</summary>
        public ushort Port { get; private set; }

        /// <summary>When the session ended, in <see cref="Time.realtimeSinceStartup"/>.</summary>
        public float EndedAt { get; private set; }

        /// <summary>A guest that hasn't heard from the host for a moment (design doc §12): the HUD says "Reconnecting…".</summary>
        public bool Reconnecting => !Ended && GuestSide != null && GuestSide.HostQuiet;

        public event Action<CarStateMessage> CarStateReceived;

        /// <summary>A hit addressed to this phone's car: another car's fallback bump, or an item hit from the host.</summary>
        public event Action<HitMessage> HitReceived;

        /// <summary>The race referee (host) or follower (guest), once <see cref="CreateRace"/> has been called.</summary>
        public IRaceView Race { get; private set; }

        /// <summary>The item referee (host) or follower (guest), created with the race.</summary>
        public IItemView Items { get; private set; }

        /// <summary>True while a session exists on this phone, for the performance readout's traffic line.</summary>
        public static bool Active => _live > 0;

        public static long NowMs => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        /// <summary>This phone's player id, once it has one; a dedicated host never has one.</summary>
        public bool TryGetLocalPlayerId(out byte playerId)
        {
            if (IsHost)
            {
                playerId = HostSide.HostPlayerId;
                return !HostSide.IsDedicated;
            }

            playerId = GuestSide?.PlayerId ?? 0;
            return GuestSide != null && GuestSide.State == GuestState.Joined;
        }

        /// <summary>
        /// Everyone in the session. On the host this is the shared <see cref="HostSession.RosterMessage"/>'s list,
        /// built once per roster change; a new list means the roster changed.
        /// </summary>
        public IReadOnlyList<RosterEntry> Players => IsHost ? HostSide.RosterMessage().Players : GuestSide.Roster.Players;

        /// <summary>True when this phone runs the room (design doc §2.3): it hosts, or a dedicated host made it the leader.</summary>
        public bool LeadsRoom => IsHost ? !HostSide.IsDedicated : GuestSide.IsLeader;

        /// <summary>The next event's settings (design doc §11), as the host keeps them.</summary>
        public LobbySettings Settings => IsHost ? HostSide.Settings : GuestSide.Lobby.Settings;

        /// <summary>Who runs the room, or <see cref="HostSession.NoPlayer"/>.</summary>
        public byte LeaderId => IsHost ? HostSide.LeaderId : GuestSide.Roster.LeaderId;

        /// <summary>The cup saved on the host, as <see cref="LobbySettings.Cup"/> (0 for none), and how many of its events are done.</summary>
        public int SavedCup => IsHost ? HostSide.SavedCup : GuestSide.Lobby.SavedCup;

        public int SavedCupDone => IsHost ? HostSide.SavedCupDone : GuestSide.Lobby.SavedCupDone;

        /// <summary>True when the room leader may start the event (everyone else is ready).</summary>
        public bool CanStart => IsHost ? HostSide.CanStart : LobbyRoster.ReadyToStart(GuestSide.Roster);

        /// <summary>The host's TV viewer (design doc §2.4): whether it can cast, the TVs it found and how the cast is going.</summary>
        public TvStatusMessage TvStatus => IsHost ? HostSide.TvStatus : GuestSide.TvStatus;

        /// <summary>The room leader's TV command (cast to a TV, stop, music on or off); only a guest leading a server's room can.</summary>
        public void Cast(CastCommand command, string device = null)
        {
            if (!IsHost && LeadsRoom)
            {
                GuestSide.SendCastCommand(command, device);
            }
        }

        /// <summary>Changes the next event's settings, if this phone runs the room; the host tells every phone.</summary>
        public void ChangeSettings(LobbySettings settings) => Command(LobbyCommand.ChangeSettings, settings);

        /// <summary>A lobby or results button pressed by the room leader: carried out on the host, or sent to it.</summary>
        public void Command(LobbyCommand command, LobbySettings settings = default)
        {
            if (!LeadsRoom)
            {
                return;
            }

            if (IsHost)
            {
                HostSide.RunCommand(command, settings);
            }
            else
            {
                GuestSide.SendLobbyCommand(command, settings);
            }
        }

        /// <summary>One-way latency estimate for showing <paramref name="playerId"/>'s car (guest cars reach other guests via the host).</summary>
        public float LatencyMsFor(byte playerId)
        {
            if (IsHost)
            {
                return HostLatencyGuessMs;
            }

            return GuestSide.CarLatencyMs(playerId, HostLatencyGuessMs);
        }

        /// <summary>
        /// Sets up racing on tracks with <paramref name="checkpointCounts"/> checkpoints (by track
        /// index) and item boxes at <paramref name="itemBoxes"/>. The host needs
        /// <paramref name="fractionToNextCheckpoint"/> (how far each player's car is towards its next
        /// checkpoint) for standings, and <paramref name="carPositions"/> (where it sees each car)
        /// for item hits and Sumo's platform edge.
        /// </summary>
        public void CreateRace(IReadOnlyList<int> checkpointCounts, Func<int, float> fractionToNextCheckpoint, IReadOnlyList<Vec3> itemBoxes, CarPositionLookup carPositions)
        {
            if (IsHost)
            {
                var random = new System.Random();
                var race = new HostRace(HostSide, checkpointCounts, () => NowMs, fractionToNextCheckpoint, carPositions);
                Race = race;
                Items = new HostItems(HostSide, race, itemBoxes, () => NowMs, random.NextDouble, carPositions);
            }
            else
            {
                Race = new GuestRace(GuestSide, () => NowMs);
                Items = new GuestItems(GuestSide, Race, itemBoxes, () => NowMs);
            }
        }

        /// <summary>Sends a fallback bump (or, on the host, an item hit) to the car it hit.</summary>
        public void SendHit(HitMessage hit)
        {
            if (IsHost)
            {
                HostSide.SendHit(hit);
            }
            else
            {
                GuestSide.SendToHost(hit, reliable: true);
            }
        }

        /// <summary>Asks for another car in the lobby (design doc §11) and remembers it in the garage too; the roster says what was granted.</summary>
        public void ChangeCar(CarChoice car)
        {
            Garage.Body = car.Body;
            Garage.Colour = car.Colour;
            if (IsHost)
            {
                HostSide.ChangeHostCar(car);
            }
            else
            {
                GuestSide.ChangeCar(car);
            }
        }

        /// <summary>A guest's vote for the next track (design doc §11); the host decides, so it has no vote of its own.</summary>
        public void VoteTrack(byte track)
        {
            if (!IsHost)
            {
                GuestSide.VoteTrack(track);
            }
        }

        public void SendCarState(CarStateMessage state)
        {
            if (IsHost)
            {
                HostSide.BroadcastCarState(state);
            }
            else
            {
                GuestSide.SendCarState(state);
            }
        }

        public static NetworkSession StartHosting(string playerName, ushort port = DiscoveryConstants.DefaultGamePort)
        {
            var session = Create();
            session.HostSide = new HostSession(session, playerName, DiscoveryConstants.ProtocolVersion, () => NowMs, Garage.Choice);
            return session.Host(playerName, port);
        }

        /// <summary>Hosts with no player of its own (design doc §2.3), announced as <paramref name="serverName"/>; the first guest in runs the room.</summary>
        public static NetworkSession StartServer(string serverName, ushort port = DiscoveryConstants.DefaultGamePort)
        {
            var session = Create();
            session.HostSide = HostSession.Dedicated(session, DiscoveryConstants.ProtocolVersion, () => NowMs);
            return session.Host(serverName, port);
        }

        private NetworkSession Host(string name, ushort port)
        {
            IsHost = true;
            HostSide.CarStateReceived += RaiseCarState;
            HostSide.HitReceived += RaiseHit;

            Port = port;
            var transport = GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            bool started = HostSide.IsDedicated ? _network.StartServer() : _network.StartHost();
            if (!started)
            {
                Fail($"Couldn't host on port {port}");
                return this;
            }

            RegisterHandler();
            _announcement = new DiscoveryAnnouncement
            {
                SessionId = (uint)new System.Random().Next(1, int.MaxValue),
                HostName = LobbyRoster.CleanName(name),
                GamePort = port,
                MaxPlayers = LobbyRoster.MaxPlayers,
            };
            _announcer = new DiscoveryAnnouncer(DiscoveryAnnouncer.DefaultTargets());
            Status = "Hosting";
            return this;
        }

        public static NetworkSession Join(string playerName, string address, ushort port)
        {
            var session = Create();
            session.GuestSide = new GuestSession(session, playerName, DiscoveryConstants.ProtocolVersion, () => NowMs, Garage.Choice);
            session.GuestSide.CarStateReceived += session.RaiseCarState;
            session.GuestSide.HitReceived += session.RaiseHit;

            var transport = session.GetComponent<UnityTransport>();
            transport.SetConnectionData(address, port);
            if (!session._network.StartClient())
            {
                session.Fail($"Couldn't connect to {address}:{port}");
                return session;
            }

            session.RegisterHandler();
            session.Status = $"Connecting to {address}…";
            return session;
        }

        private static NetworkSession Create()
        {
            // Configure everything before NetworkManager.Awake runs.
            var go = new GameObject("Network session");
            go.SetActive(false);
            var transport = go.AddComponent<UnityTransport>();
            var network = go.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = false,
                EnableSceneManagement = false,
            };

            var session = go.AddComponent<NetworkSession>();
            _live++;
            session._network = network;
            network.OnClientConnectedCallback += session.OnClientConnected;
            network.OnClientDisconnectCallback += session.OnClientDisconnected;
            go.SetActive(true);
            return session;
        }

        // ISessionTransport: what HostSession and GuestSession use to talk.
        void ISessionTransport.Send(ulong connection, IGameMessage message, bool reliable)
        {
            if (_network == null || !_network.IsListening)
            {
                return;
            }

            byte[] bytes = MessageCodec.Encode(message);
            using (var writer = new FastBufferWriter(bytes.Length + sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(bytes.Length);
                writer.WriteBytesSafe(bytes);

                // Fragmented, because NGO throws on an unfragmented message over its `NonFragmentedMessageMaxSize`
                // (about 1.3 KB) and a TV status can be twice that. Unity Transport sends both kinds down the same
                // reliable pipeline.
                var delivery = reliable ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.Unreliable;
                _network.CustomMessagingManager.SendNamedMessage(MessageName, connection, writer, delivery);
            }

            PerfOverlay.Traffic.Sent(bytes.Length + sizeof(int), Time.realtimeSinceStartupAsDouble);
        }

        void ISessionTransport.Disconnect(ulong connection)
        {
            _pendingDisconnects.Add((connection, Time.realtimeSinceStartup + RefusalDisconnectDelaySeconds));
        }

        private void RegisterHandler()
        {
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnNamedMessage);
        }

        private void OnNamedMessage(ulong sender, FastBufferReader reader)
        {
            IGameMessage message;
            try
            {
                reader.ReadValueSafe(out int length);

                // Checked before `new byte[length]`, so a bad length can't allocate more than the message carries.
                int received = reader.Length - reader.Position;
                if (length <= 0 || length > received)
                {
                    Debug.LogWarning($"Dropped a message from connection {sender} claiming {length} bytes when {received} arrived.");
                    return;
                }

                PerfOverlay.Traffic.Received(length + sizeof(int), Time.realtimeSinceStartupAsDouble);
                var bytes = new byte[length];
                reader.ReadBytesSafe(ref bytes, length);
                message = MessageCodec.Decode(bytes);
            }
            catch (Exception e) when (e is NetFormatException || e is OverflowException || e is IndexOutOfRangeException)
            {
                Debug.LogWarning($"Dropped a malformed message from client {sender}: {e.Message}");
                return;
            }

            if (IsHost)
            {
                HostSide.OnMessage(sender, message);
            }
            else
            {
                GuestSide.OnMessage(message);
                if (GuestSide.State == GuestState.Refused)
                {
                    Fail(RefusalText(GuestSide.RefusalReason));
                }
                else if (GuestSide.State == GuestState.Joined)
                {
                    Status = "Joined";
                }
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsHost && clientId == _network.LocalClientId)
            {
                Status = "Joining…";
                GuestSide.OnConnected();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (IsHost)
            {
                HostSide.OnDisconnected(clientId);
            }
            else if (!Ended)
            {
                Fail(GuestSide.State == GuestState.Joined ? "The host left" : "Couldn't reach the host");
            }
        }

        private void RaiseCarState(CarStateMessage state) => CarStateReceived?.Invoke(state);

        private void RaiseHit(HitMessage hit) => HitReceived?.Invoke(hit);

        /// <summary>Time in the background doesn't count as the host going quiet; the host's own takeover rules cover it (§12).</summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                GuestSide?.ForgiveSilence();
            }
        }

        private void Update()
        {
            long now = NowMs;
            if (!Ended && GuestSide != null && GuestSide.HostLost)
            {
                // The transport's own timeout is long so a guest can come back from the background; Wi-Fi drops end sooner.
                Fail("Lost the connection to the host");
                if (_network != null && _network.IsListening)
                {
                    _network.Shutdown();
                }
            }

            GuestSide?.Tick();
            HostSide?.Tick();
            if (!Ended)
            {
                Race?.Tick();
                Items?.Tick();
            }

            if (_announcer != null)
            {
                _announcement.PlayerCount = (byte)HostSide.Roster.Count;
                _announcement.State = HostSide.EventInProgress ? HostSessionState.InEvent : HostSessionState.Lobby;
                _announcer.Tick(_announcement, now);
            }

            for (int i = _pendingDisconnects.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup >= _pendingDisconnects[i].AtTime)
                {
                    if (_network != null && _network.IsServer)
                    {
                        _network.DisconnectClient(_pendingDisconnects[i].Connection);
                    }

                    _pendingDisconnects.RemoveAt(i);
                }
            }
        }

        private void OnDestroy()
        {
            _live--;
            _announcer?.Dispose();
            _announcer = null;
            if (_network != null)
            {
                _network.OnClientConnectedCallback -= OnClientConnected;
                _network.OnClientDisconnectCallback -= OnClientDisconnected;
                if (_network.IsListening)
                {
                    _network.Shutdown();
                }
            }
        }

        private void Fail(string reason)
        {
            Ended = true;
            EndedAt = Time.realtimeSinceStartup;
            Status = reason;
            _announcer?.Dispose();
            _announcer = null;
        }

        private static string RefusalText(JoinResult result)
        {
            switch (result)
            {
                case JoinResult.SessionFull:
                    return "That game is full";
                case JoinResult.VersionMismatch:
                    return "That game needs a different version of Car Racer";
                case JoinResult.EventInProgress:
                    return "That game has already started";
                default:
                    return "Couldn't join";
            }
        }
    }
}
