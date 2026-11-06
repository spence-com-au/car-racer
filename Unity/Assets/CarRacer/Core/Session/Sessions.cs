using System;
using System.Collections.Generic;
using CarRacer.Core.Bots;
using CarRacer.Core.Net;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// What a session needs from the network layer (NGO in the game, a fake in tests). Connections
    /// are identified by the transport's client id; the host is always <see cref="GuestSession.HostConnection"/>.
    /// </summary>
    public interface ISessionTransport
    {
        void Send(ulong connection, IGameMessage message, bool reliable);

        void Disconnect(ulong connection);
    }

    /// <summary>
    /// The host's side of the session protocol (design doc §2.1): admits or refuses guests,
    /// keeps the roster and the next event's settings, answers clock pings, and relays car states
    /// between guests (NGO guests can only reach the host). On a phone the host is a player too;
    /// a dedicated host (a server, design doc §2.3) has no player of its own, and the guest who
    /// has been there longest runs the room. Independent of NGO so it can be unit tested.
    /// </summary>
    public sealed class HostSession
    {
        /// <summary>No player: a dedicated host's <see cref="HostPlayerId"/>, or the leader of an empty room.</summary>
        public const byte NoPlayer = byte.MaxValue;

        private readonly Dictionary<byte, long> _lastHeardMs = new Dictionary<byte, long>();
        private readonly List<byte> _joinOrder = new List<byte>();
        private LobbySettings _settings = LobbySettings.Default;
        private byte _savedCup;
        private byte _savedCupDone;
        private readonly HashSet<byte> _away = new HashSet<byte>();
        private readonly ISessionTransport _transport;
        private readonly Func<long> _clockMs;
        private readonly ushort _protocolVersion;
        private readonly Dictionary<ulong, byte> _playerByConnection = new Dictionary<ulong, byte>();

        public HostSession(ISessionTransport transport, string hostName, ushort protocolVersion, Func<long> clockMs, CarChoice car = default)
        {
            _transport = transport;
            _protocolVersion = protocolVersion;
            _clockMs = clockMs;
            HostPlayerId = hostName == null ? NoPlayer : Roster.Add(hostName, isHost: true, car).PlayerId;
        }

        /// <summary>A host with no player of its own, on a server (design doc §2.3); the room leader runs the lobby.</summary>
        public static HostSession Dedicated(ISessionTransport transport, ushort protocolVersion, Func<long> clockMs) =>
            new HostSession(transport, null, protocolVersion, clockMs);

        public LobbyRoster Roster { get; } = new LobbyRoster();

        /// <summary>The host's own player, or <see cref="NoPlayer"/> on a dedicated host.</summary>
        public byte HostPlayerId { get; }

        public bool IsDedicated => HostPlayerId == NoPlayer;

        /// <summary>
        /// Who runs the room (design doc §2.3): the host's own player, or on a dedicated host the
        /// guest who joined first and is still driving their own car (one away with a bot at the
        /// wheel hands over until they're back); <see cref="NoPlayer"/> when there's nobody.
        /// </summary>
        public byte LeaderId
        {
            get
            {
                if (!IsDedicated)
                {
                    return HostPlayerId;
                }

                foreach (byte playerId in _joinOrder)
                {
                    var player = Roster.Find(playerId);
                    if (player != null && !player.IsBot)
                    {
                        return playerId;
                    }
                }

                return NoPlayer;
            }
        }

        /// <summary>The next event's settings (design doc §11), as every phone sees them.</summary>
        public LobbySettings Settings => _settings;

        /// <summary>The cup saved on the host, as <see cref="LobbySettings.Cup"/> (0 for none), and how many of its events are done.</summary>
        public int SavedCup => _savedCup;

        public int SavedCupDone => _savedCupDone;

        /// <summary>True when the leader may start: everyone else is ready (the leader starting counts as their Ready).</summary>
        public bool CanStart => LobbyRoster.ReadyToStart(RosterMessage());

        /// <summary>The TV viewer's state (design doc §2.4), as last set with <see cref="SetTvStatus"/>.</summary>
        public TvStatusMessage TvStatus { get; private set; } = new TvStatusMessage();

        /// <summary>The room leader picked a TV or stopped casting.</summary>
        public event Action<CastCommandMessage> CastCommandReceived;

        /// <summary>Tells every phone how the TV viewer is doing, if that changed.</summary>
        public void SetTvStatus(TvStatusMessage status)
        {
            if (status == null || status.Equals(TvStatus))
            {
                return;
            }

            TvStatus = status;
            Broadcast(status, reliable: true);
        }

        /// <summary>A command from the room leader (or the host's own lobby, <see cref="RunCommand"/>) other than changing settings, for the game to carry out.</summary>
        public event Action<LobbyCommand> LobbyCommandReceived;

        /// <summary>While true, new guests are refused (no joining mid-event, design doc §12).</summary>
        public bool EventInProgress { get; set; }

        /// <summary>The host's bot fill (design doc §9): bots are added at the start of an event until there are this many cars; 0 for none.</summary>
        public int BotFill
        {
            get => _settings.BotFill;
            set
            {
                var next = _settings;
                next.BotFill = value;
                ChangeSettings(next);
            }
        }

        public BotDifficulty BotDifficulty
        {
            get => _settings.BotDifficulty;
            set
            {
                var next = _settings;
                next.BotDifficulty = value;
                ChangeSettings(next);
            }
        }

        /// <summary>Changes the next event's settings (ignored mid-event) and tells everyone. Returns true if they changed.</summary>
        public bool ChangeSettings(LobbySettings settings)
        {
            settings = settings.Clean();
            if (EventInProgress || settings.Equals(_settings))
            {
                return false;
            }

            _settings = settings;
            BroadcastSettings();
            return true;
        }

        /// <summary>Tells everyone which cup is saved on the host (as <see cref="LobbySettings.Cup"/>, 0 for none) and how far it got.</summary>
        public void SetSavedCup(int cup, int eventsDone)
        {
            if (cup == _savedCup && eventsDone == _savedCupDone)
            {
                return;
            }

            _savedCup = (byte)cup;
            _savedCupDone = (byte)eventsDone;
            BroadcastSettings();
        }

        /// <summary>Carries out a lobby command from the host's own screen, the same way as one from the room leader.</summary>
        public void RunCommand(LobbyCommand command, LobbySettings settings = default)
        {
            if (command == LobbyCommand.ChangeSettings)
            {
                ChangeSettings(settings);
            }
            else
            {
                LobbyCommandReceived?.Invoke(command);
            }
        }

        /// <summary>An item hit aimed at a bot, which the host drives (see <see cref="SendHit"/>).</summary>
        public event Action<HitMessage> BotHitReceived;

        /// <summary>Raised after anyone joins, leaves or changes Ready.</summary>
        public event Action RosterChanged;

        /// <summary>A guest's car state arrived (already relayed to the other guests).</summary>
        public event Action<CarStateMessage> CarStateReceived;

        /// <summary>A joined guest reported a checkpoint for its own car (see <c>HostRace</c>).</summary>
        public event Action<CheckpointReachedMessage> CheckpointReceived;

        /// <summary>A joined guest reported a Monster Jam stunt by its own truck (see <c>HostRace</c>).</summary>
        public event Action<StuntMessage> StuntReceived;

        /// <summary>A joined guest left (disconnected), with the name they had. Raised before the roster updates.</summary>
        public event Action<byte, string> PlayerLeft;

        /// <summary>
        /// A guest left mid-event and a bot took over their car (design doc §12): same id, name and
        /// colour, now marked <see cref="LobbyPlayer.IsBot"/>. Raised instead of <see cref="PlayerLeft"/>.
        /// </summary>
        public event Action<byte> PlayerTakenOver;

        /// <summary>
        /// A guest whose car a bot took over while they were away (their app in the background,
        /// design doc §12) is back: they're a player again and get their car back. The game sends
        /// them where it got to with <see cref="SendControlReturned"/>.
        /// </summary>
        public event Action<byte> PlayerReturned;

        /// <summary>A guest silent this long during an event (app in the background) is taken over by a bot until they're heard from again.</summary>
        public const long AwayAfterMs = 10_000;

        /// <summary>A guest's car bumped the host's car (fallback hit, see <c>BumpMonitor</c>).</summary>
        public event Action<HitMessage> HitReceived;

        /// <summary>A joined guest claimed an item box or used its item, for its own car (see <c>HostItems</c>).</summary>
        public event Action<IGameMessage> ItemMessageReceived;

        public IEnumerable<ulong> GuestConnections => _playerByConnection.Keys;

        public bool TryGetPlayerId(ulong connection, out byte playerId) => _playerByConnection.TryGetValue(connection, out playerId);

        public bool IsBot(byte playerId) => Roster.Find(playerId)?.IsBot ?? false;

        /// <summary>Adds bots until there are <see cref="BotFill"/> cars (or the session is full), and tells everyone.</summary>
        public void AddBotsForEvent()
        {
            int target = Math.Min(BotFill, LobbyRoster.MaxPlayers);
            bool added = false;
            while (Roster.Count < target && Roster.AddBot() != null)
            {
                added = true;
            }

            if (added)
            {
                BroadcastRoster();
            }
        }

        /// <summary>Removes the bots when the lobby reopens, freeing their places for people.</summary>
        public void RemoveBots()
        {
            // A guest still away when the event ends is a player again in the lobby, not one of the bots.
            foreach (byte playerId in _away)
            {
                var player = Roster.Find(playerId);
                if (player != null)
                {
                    player.IsBot = false;
                    player.Ready = false;
                }
            }

            _away.Clear();
            if (Roster.RemoveBots() > 0)
            {
                BroadcastRoster();
            }
        }

        /// <summary>Call every frame on the host: a guest silent for <see cref="AwayAfterMs"/> during an event is taken over by a bot.</summary>
        public void Tick()
        {
            if (!EventInProgress)
            {
                return;
            }

            long now = _clockMs();
            bool changed = false;
            foreach (byte playerId in _playerByConnection.Values)
            {
                var player = Roster.Find(playerId);
                if (player == null || player.IsBot || _away.Contains(playerId) || !_lastHeardMs.TryGetValue(playerId, out long heard) || now - heard < AwayAfterMs)
                {
                    continue;
                }

                // The car carries on under a bot until the guest is back (OnMessage), like a takeover.
                _away.Add(playerId);
                player.IsBot = true;
                PlayerTakenOver?.Invoke(playerId);
                changed = true;
            }

            if (changed)
            {
                BroadcastRoster();
            }
        }

        /// <summary>Whether <paramref name="playerId"/> is a guest away from the game, their car driven by a bot.</summary>
        public bool IsAway(byte playerId) => _away.Contains(playerId);

        /// <summary>Tells a returning guest where their car got to under the bot, so they carry on from there.</summary>
        public void SendControlReturned(ControlReturnedMessage message)
        {
            foreach (var pair in _playerByConnection)
            {
                if (pair.Value == message.PlayerId)
                {
                    _transport.Send(pair.Key, message, reliable: true);
                    return;
                }
            }
        }

        public void OnDisconnected(ulong connection)
        {
            if (_playerByConnection.TryGetValue(connection, out byte playerId))
            {
                _playerByConnection.Remove(connection);
                _lastHeardMs.Remove(playerId);
                _joinOrder.Remove(playerId);

                // Gone for good: an away player's bot keeps the car for the rest of the event.
                bool wasAway = _away.Remove(playerId);
                var player = Roster.Find(playerId);
                if (EventInProgress && player != null)
                {
                    // A bot takes the car over for the rest of the event, and leaves with the other bots.
                    player.IsBot = true;
                    player.Ready = true;
                    if (!wasAway)
                    {
                        PlayerTakenOver?.Invoke(playerId);
                    }
                }
                else
                {
                    PlayerLeft?.Invoke(playerId, player?.Name ?? string.Empty);
                    Roster.Remove(playerId);
                }

                BroadcastRoster();
            }
        }

        public void OnMessage(ulong connection, IGameMessage message)
        {
            bool joined = _playerByConnection.TryGetValue(connection, out byte playerId);
            if (joined)
            {
                _lastHeardMs[playerId] = _clockMs();
                if (_away.Remove(playerId))
                {
                    Return(playerId);
                }
            }

            switch (message)
            {
                case JoinRequestMessage join when !joined:
                    HandleJoin(connection, join);
                    break;
                case ClockPingMessage ping:
                    _transport.Send(connection, new ClockPongMessage { GuestSentMs = ping.GuestSentMs, HostMs = _clockMs() }, reliable: false);
                    break;
                case SetReadyMessage ready when joined:
                    if (Roster.SetReady(playerId, ready.Ready))
                    {
                        BroadcastRoster();
                    }

                    break;
                case ChangeCarMessage change when joined && !EventInProgress:
                    if (Roster.ChangeCar(playerId, new CarChoice(change.BodyIndex, change.ColourIndex)))
                    {
                        BroadcastRoster();
                    }

                    break;
                case VoteTrackMessage vote when joined && !EventInProgress:
                    if (Roster.Vote(playerId, vote.TrackIndex))
                    {
                        BroadcastRoster();
                    }

                    break;
                case CarStateMessage state when joined && state.PlayerId == playerId:
                    // A guest may only speak for its own car.
                    foreach (ulong other in _playerByConnection.Keys)
                    {
                        if (other != connection)
                        {
                            _transport.Send(other, state, reliable: false);
                        }
                    }

                    CarStateReceived?.Invoke(state);
                    break;
                case CheckpointReachedMessage checkpoint when joined && checkpoint.PlayerId == playerId:
                    CheckpointReceived?.Invoke(checkpoint);
                    break;
                case StuntMessage stunt when joined && stunt.PlayerId == playerId:
                    StuntReceived?.Invoke(stunt);
                    break;
                case HitMessage hit when joined && hit.AttackerId == playerId && !hit.IsItemHit:
                    // Item hits come only from the host; a guest may only report bumps by its own car.
                    SendHit(hit);
                    break;
                case ItemBoxClaimMessage claim when joined && claim.PlayerId == playerId:
                    ItemMessageReceived?.Invoke(claim);
                    break;
                case ItemUsedMessage used when joined && used.PlayerId == playerId:
                    ItemMessageReceived?.Invoke(used);
                    break;
                case LobbyCommandMessage command when joined && playerId == LeaderId:
                    RunCommand(command.Command, command.Settings);
                    break;
                case CastCommandMessage cast when joined && playerId == LeaderId:
                    CastCommandReceived?.Invoke(cast);
                    break;
            }
        }

        /// <summary>Sends the host's own car (or a bot's) to every guest.</summary>
        public void BroadcastCarState(CarStateMessage state) => Broadcast(state, reliable: false);

        /// <summary>Clears every guest's Ready and track vote (the lobby reopening after an event) and tells everyone.</summary>
        public void ResetReady()
        {
            Roster.ResetReady();
            Roster.ClearVotes();
            BroadcastRoster();
        }

        /// <summary>The host picked another car in the lobby; ignored mid-event. Returns true if it changed.</summary>
        public bool ChangeHostCar(CarChoice car)
        {
            if (IsDedicated || EventInProgress || !Roster.ChangeCar(HostPlayerId, car))
            {
                return false;
            }

            BroadcastRoster();
            return true;
        }

        public void SetHostName(string name)
        {
            if (IsDedicated)
            {
                return;
            }

            Roster.Find(HostPlayerId).Name = LobbyRoster.CleanName(name);
            BroadcastRoster();
        }

        private void HandleJoin(ulong connection, JoinRequestMessage join)
        {
            var result = Roster.CheckJoin(join.ProtocolVersion, _protocolVersion, EventInProgress);
            if (result != JoinResult.Accepted)
            {
                _transport.Send(connection, new JoinReplyMessage { Result = result }, reliable: true);
                _transport.Disconnect(connection);
                return;
            }

            var player = Roster.Add(join.PlayerName, car: new CarChoice(join.BodyIndex, join.ColourIndex));
            _playerByConnection[connection] = player.PlayerId;
            _joinOrder.Add(player.PlayerId);
            _lastHeardMs[player.PlayerId] = _clockMs();
            _transport.Send(connection, new JoinReplyMessage { Result = JoinResult.Accepted, PlayerId = player.PlayerId }, reliable: true);
            _transport.Send(connection, SettingsMessage(), reliable: true);
            if (TvStatus.CanCast)
            {
                _transport.Send(connection, TvStatus, reliable: true);
            }

            BroadcastRoster();
        }

        private void Return(byte playerId)
        {
            var player = Roster.Find(playerId);
            if (player == null)
            {
                return;
            }

            player.IsBot = false;
            PlayerReturned?.Invoke(playerId);
            BroadcastRoster();
        }

        private void BroadcastRoster()
        {
            Broadcast(RosterMessage(), reliable: true);
            RosterChanged?.Invoke();
        }

        /// <summary>The roster as the guests get it, with the room leader.</summary>
        public RosterMessage RosterMessage()
        {
            var message = Roster.ToMessage();
            message.LeaderId = LeaderId;
            return message;
        }

        private LobbySettingsMessage SettingsMessage() => new LobbySettingsMessage { Settings = _settings, SavedCup = _savedCup, SavedCupDone = _savedCupDone };

        private void BroadcastSettings()
        {
            Broadcast(SettingsMessage(), reliable: true);
            SettingsChanged?.Invoke();
        }

        /// <summary>Raised after the settings or the saved cup change, whoever changed them.</summary>
        public event Action SettingsChanged;

        /// <summary>Delivers a hit to its target: the host's own car, a bot the host drives, or the guest that owns the target car.</summary>
        public void SendHit(HitMessage hit)
        {
            if (hit.TargetId == HostPlayerId)
            {
                HitReceived?.Invoke(hit);
                return;
            }

            if (IsBot(hit.TargetId))
            {
                BotHitReceived?.Invoke(hit);
                return;
            }

            foreach (var pair in _playerByConnection)
            {
                if (pair.Value == hit.TargetId)
                {
                    _transport.Send(pair.Key, hit, reliable: true);
                    return;
                }
            }
        }

        /// <summary>Sends a message to every joined guest.</summary>
        public void Broadcast(IGameMessage message, bool reliable)
        {
            foreach (ulong connection in _playerByConnection.Keys)
            {
                _transport.Send(connection, message, reliable);
            }
        }
    }

    public enum GuestState
    {
        Connecting,
        Joined,
        Refused,
    }

    /// <summary>
    /// A guest's side of the session protocol: asks to join, tracks the roster, keeps the clock
    /// offset fresh, and passes on other players' car states.
    /// </summary>
    public sealed class GuestSession
    {
        /// <summary>NGO's server client id.</summary>
        public const ulong HostConnection = 0;

        /// <summary>Clock pings go out quickly at first for a good estimate before a race starts, then once a second.</summary>
        public const long FastPingIntervalMs = 200;
        public const long PingIntervalMs = 1000;
        public const int FastPingCount = 5;

        private readonly ISessionTransport _transport;
        private readonly Func<long> _clockMs;
        private readonly string _playerName;
        private CarChoice _car;
        private readonly ushort _protocolVersion;
        private long _nextPingMs;
        private int _pingsSent;
        private long _lastHeardMs;

        /// <summary>
        /// Once joined, the host is heard from many times a second (its car, clock replies). After
        /// this long without a word the guest shows "Reconnecting…" (design doc §12)…
        /// </summary>
        public const long ReconnectingAfterMs = 1500;

        /// <summary>…and after this long it gives up and goes back to the menu.</summary>
        public const long GiveUpAfterMs = 10_000;

        public GuestSession(ISessionTransport transport, string playerName, ushort protocolVersion, Func<long> clockMs, CarChoice car = default)
        {
            _transport = transport;
            _playerName = playerName;
            _car = car;
            _protocolVersion = protocolVersion;
            _clockMs = clockMs;
        }

        public GuestState State { get; private set; } = GuestState.Connecting;
        public JoinResult RefusalReason { get; private set; }
        public byte PlayerId { get; private set; }
        public RosterMessage Roster { get; private set; } = new RosterMessage();
        public ClockSync Clock { get; } = new ClockSync();
        public bool Ready { get; private set; }

        /// <summary>The host's settings for the next event and its saved cup (design doc §11).</summary>
        public LobbySettingsMessage Lobby { get; private set; } = new LobbySettingsMessage();

        /// <summary>The host's TV viewer (design doc §2.4): whether it can cast, the TVs it found and how the cast is going.</summary>
        public TvStatusMessage TvStatus { get; private set; } = new TvStatusMessage();

        /// <summary>True when this phone runs the room (design doc §2.3): a dedicated host made it the leader.</summary>
        public bool IsLeader => State == GuestState.Joined && Roster.LeaderId == PlayerId;

        public event Action RosterChanged;

        /// <summary>The host's settings or saved cup changed.</summary>
        public event Action LobbyChanged;
        public event Action<CarStateMessage> CarStateReceived;

        /// <summary>Race messages from the host (countdown, standings, finishes, results, back to lobby); see <c>GuestRace</c>.</summary>
        public event Action<IGameMessage> RaceMessageReceived;

        /// <summary>Another car bumped this guest's car, or the host scored an item hit on it.</summary>
        public event Action<HitMessage> HitReceived;

        /// <summary>Item boxes taken and items used, from the host (see <c>GuestItems</c>).</summary>
        public event Action<IGameMessage> ItemMessageReceived;

        /// <summary>The host has handed this phone's car back after a bot drove it while the app was away (design doc §12).</summary>
        public event Action<ControlReturnedMessage> ControlReturned;

        /// <summary>Call once the transport has connected to the host.</summary>
        public void OnConnected()
        {
            _lastHeardMs = _clockMs();
            _transport.Send(HostConnection, new JoinRequestMessage { ProtocolVersion = _protocolVersion, PlayerName = LobbyRoster.CleanName(_playerName), BodyIndex = _car.Body, ColourIndex = _car.Colour }, reliable: true);
        }

        /// <summary>Joined, but nothing from the host for <see cref="ReconnectingAfterMs"/>: the Wi-Fi may have dropped.</summary>
        public bool HostQuiet => State == GuestState.Joined && _clockMs() - _lastHeardMs >= ReconnectingAfterMs;

        /// <summary>Joined, but nothing from the host for <see cref="GiveUpAfterMs"/>: time to give up.</summary>
        public bool HostLost => State == GuestState.Joined && _clockMs() - _lastHeardMs >= GiveUpAfterMs;

        /// <summary>The app is back from the background: time spent there doesn't count as the host going quiet.</summary>
        public void ForgiveSilence() => _lastHeardMs = _clockMs();

        public void OnMessage(IGameMessage message)
        {
            _lastHeardMs = _clockMs();
            switch (message)
            {
                case JoinReplyMessage reply when State == GuestState.Connecting:
                    if (reply.Result == JoinResult.Accepted)
                    {
                        State = GuestState.Joined;
                        PlayerId = reply.PlayerId;
                        _nextPingMs = _clockMs();
                    }
                    else
                    {
                        State = GuestState.Refused;
                        RefusalReason = reply.Result;
                    }

                    break;
                case RosterMessage roster:
                    Roster = roster;
                    RosterChanged?.Invoke();
                    break;
                case LobbySettingsMessage lobby:
                    Lobby = lobby;
                    LobbyChanged?.Invoke();
                    break;
                case TvStatusMessage tv:
                    TvStatus = tv;
                    LobbyChanged?.Invoke();
                    break;
                case ClockPongMessage pong:
                    Clock.AddSample(pong.GuestSentMs, pong.HostMs, _clockMs());
                    break;
                case CarStateMessage state when state.PlayerId != PlayerId:
                    CarStateReceived?.Invoke(state);
                    break;
                case HitMessage hit when State == GuestState.Joined && hit.TargetId == PlayerId:
                    HitReceived?.Invoke(hit);
                    break;
                case ControlReturnedMessage returned when State == GuestState.Joined && returned.PlayerId == PlayerId:
                    ControlReturned?.Invoke(returned);
                    break;
                case RaceCountdownMessage _:
                case StandingsMessage _:
                case PlayerFinishedMessage _:
                case RaceResultsMessage _:
                case ReturnToLobbyMessage _:
                case CupStandingsMessage _:
                    if (State == GuestState.Joined)
                    {
                        RaceMessageReceived?.Invoke(message);
                    }

                    break;
                case ItemBoxTakenMessage _:
                case ItemUsedMessage _:
                case HazardGoneMessage _:
                case ProjectilesMessage _:
                    if (State == GuestState.Joined)
                    {
                        ItemMessageReceived?.Invoke(message);
                    }

                    break;
            }
        }

        /// <summary>Call every frame: sends clock pings when due.</summary>
        public void Tick()
        {
            long now = _clockMs();
            if (State != GuestState.Joined || now < _nextPingMs)
            {
                return;
            }

            _transport.Send(HostConnection, new ClockPingMessage { GuestSentMs = now }, reliable: false);
            _pingsSent++;
            _nextPingMs = now + (_pingsSent < FastPingCount ? FastPingIntervalMs : PingIntervalMs);
        }

        public void SetReady(bool ready)
        {
            Ready = ready;
            if (State == GuestState.Joined)
            {
                _transport.Send(HostConnection, new SetReadyMessage { Ready = ready }, reliable: true);
            }
        }

        /// <summary>Asks the host for another car (design doc §11); the roster that comes back says what was granted.</summary>
        public void ChangeCar(CarChoice car)
        {
            _car = car;
            SendToHost(new ChangeCarMessage { BodyIndex = car.Body, ColourIndex = car.Colour }, reliable: true);
        }

        /// <summary>Votes for the next track (design doc §11), or withdraws the vote with <see cref="VoteTrackMessage.NoVote"/>.</summary>
        public void VoteTrack(byte track) => SendToHost(new VoteTrackMessage { TrackIndex = track }, reliable: true);

        public void SendCarState(CarStateMessage state) => SendToHost(state, reliable: false);

        /// <summary>The room leader's command to the host (design doc §2.3); the host ignores it from anyone else.</summary>
        /// <summary>The room leader's TV command (design doc §2.4): a TV to cast to, Stop, or TV music on or off.</summary>
        public void SendCastCommand(CastCommand command, string device = null) =>
            SendToHost(new CastCommandMessage { Command = command, Device = device ?? string.Empty }, reliable: true);

        public void SendLobbyCommand(LobbyCommand command, LobbySettings settings = default) =>
            SendToHost(new LobbyCommandMessage { Command = command, Settings = settings }, reliable: true);

        public void SendToHost(IGameMessage message, bool reliable)
        {
            if (State == GuestState.Joined)
            {
                _transport.Send(HostConnection, message, reliable);
            }
        }
    }
}
