using System;
using System.Collections.Generic;
using CarRacer.Core.Net;

namespace CarRacer.Core.Session
{
    /// <summary>The car a player picked in the garage (design doc §11): a body and a colour.</summary>
    public struct CarChoice
    {
        public byte Body;
        public byte Colour;

        public CarChoice(byte body, byte colour)
        {
            Body = body;
            Colour = colour;
        }
    }

    /// <summary>One person in the session, as the host tracks them.</summary>
    public sealed class LobbyPlayer
    {
        public byte PlayerId;
        public string Name = string.Empty;
        public byte ColourIndex;
        public byte BodyIndex;
        public bool Ready;
        public bool IsHost;
        public bool IsBot;

        /// <summary>The track this player voted for, or <see cref="VoteTrackMessage.NoVote"/>.</summary>
        public byte TrackVote = VoteTrackMessage.NoVote;
    }

    /// <summary>
    /// The host's list of players (design doc §11). Hands out the lowest free player id, which is
    /// also the grid slot until race rules decide otherwise. Each player gets the colour they
    /// picked in the garage unless someone already has it, in which case the later joiner gets
    /// the next free one along, so no two players share one; bots get the lowest free colour and
    /// a body each in turn. Names are unique too, ignoring case, since a cup scores players by
    /// name (§10): a later player whose name is taken gets a number after it. The host and bots
    /// are always ready; guests tap Ready.
    /// </summary>
    public sealed class LobbyRoster
    {
        public const int MaxPlayers = 6;
        public const int MaxNameChars = 24;

        /// <summary>Number of distinct car colours; at least <see cref="MaxPlayers"/>.</summary>
        public const int ColourCount = 8;

        /// <summary>Number of car bodies to pick from (<c>CarModels.Bodies</c>).</summary>
        public const int BodyCount = 12;

        private readonly List<LobbyPlayer> _players = new List<LobbyPlayer>();

        public IReadOnlyList<LobbyPlayer> Players => _players;
        public int Count => _players.Count;
        public bool IsFull => _players.Count >= MaxPlayers;

        /// <summary>Bot names, handed out in order.</summary>
        public static readonly string[] BotNames = { "Turbo", "Sprocket", "Nitro", "Gizmo", "Blitz", "Zippy" };

        /// <summary>True when there is at least one guest and every guest is ready. Bots don't count.</summary>
        public bool EveryoneReady
        {
            get
            {
                bool anyGuest = false;
                foreach (var p in _players)
                {
                    if (!p.IsHost && !p.IsBot)
                    {
                        anyGuest = true;
                        if (!p.Ready)
                        {
                            return false;
                        }
                    }
                }

                return anyGuest;
            }
        }

        /// <summary>
        /// Whether the room leader may start (design doc §2.3, §11): there is a leader and every
        /// other person (not the host's own player, not a bot) is ready. A leader on their own can
        /// start, for a practice race.
        /// </summary>
        public static bool ReadyToStart(RosterMessage roster)
        {
            if (roster.LeaderId == HostSession.NoPlayer)
            {
                return false;
            }

            foreach (var p in roster.Players)
            {
                if (p.PlayerId != roster.LeaderId && !p.IsHost && !p.IsBot && !p.Ready)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether the host should accept a guest's <see cref="JoinRequestMessage"/>.</summary>
        public JoinResult CheckJoin(ushort guestProtocolVersion, ushort hostProtocolVersion, bool eventInProgress)
        {
            if (guestProtocolVersion != hostProtocolVersion)
            {
                return JoinResult.VersionMismatch;
            }

            if (eventInProgress)
            {
                return JoinResult.EventInProgress;
            }

            return IsFull ? JoinResult.SessionFull : JoinResult.Accepted;
        }

        /// <summary>Adds a player with the car they picked (the first body and colour if not given), or returns null if the session is full.</summary>
        public LobbyPlayer Add(string name, bool isHost = false, CarChoice car = default)
        {
            if (IsFull)
            {
                return null;
            }

            var player = new LobbyPlayer
            {
                PlayerId = LowestFree(p => p.PlayerId),
                Name = UniqueName(name, null),
                ColourIndex = FreeColourFrom(car.Colour),
                BodyIndex = (byte)(car.Body % BodyCount),
                Ready = isHost,
                IsHost = isHost,
            };

            _players.Add(player);
            _players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            return player;
        }

        /// <summary>Adds a bot with the next free bot name, or returns null if the session is full.</summary>
        public LobbyPlayer AddBot()
        {
            var bot = Add(NextBotName());
            if (bot != null)
            {
                // Every other body, so a full grid of bots shows off the whole garage, old cars and new.
                bot.BodyIndex = (byte)((bot.PlayerId * 2 + 1) % BodyCount);
                bot.IsBot = true;
                bot.Ready = true;
            }

            return bot;
        }

        /// <summary>Renames a player, numbering the name if someone else already has it.</summary>
        public void Rename(byte playerId, string name)
        {
            var player = Find(playerId);
            if (player != null)
            {
                player.Name = UniqueName(name, player);
            }
        }

        /// <summary>Removes every bot, returning how many there were.</summary>
        public int RemoveBots() => _players.RemoveAll(p => p.IsBot);

        public int HumanCount => _players.FindAll(p => !p.IsBot).Count;

        public bool Remove(byte playerId) => _players.RemoveAll(p => p.PlayerId == playerId) > 0;

        public LobbyPlayer Find(byte playerId) => _players.Find(p => p.PlayerId == playerId);

        public bool SetReady(byte playerId, bool ready)
        {
            var player = Find(playerId);
            if (player == null || player.IsHost || player.IsBot)
            {
                return false;
            }

            player.Ready = ready;
            return true;
        }

        /// <summary>
        /// Changes a person's car in the lobby (design doc §11): any body, and the colour unless
        /// someone else has it, in which case they keep theirs. Returns true if anything changed.
        /// Bots keep the cars they were given.
        /// </summary>
        public bool ChangeCar(byte playerId, CarChoice car)
        {
            var player = Find(playerId);
            if (player == null || player.IsBot)
            {
                return false;
            }

            byte body = (byte)(car.Body % BodyCount);
            byte colour = (byte)(car.Colour % ColourCount);
            if (_players.Exists(p => p != player && p.ColourIndex == colour))
            {
                colour = player.ColourIndex;
            }

            if (body == player.BodyIndex && colour == player.ColourIndex)
            {
                return false;
            }

            player.BodyIndex = body;
            player.ColourIndex = colour;
            return true;
        }

        /// <summary>True if someone other than <paramref name="playerId"/> has colour <paramref name="colour"/>.</summary>
        public bool ColourTaken(byte colour, byte playerId) => _players.Exists(p => p.PlayerId != playerId && p.ColourIndex == colour);

        /// <summary>Records a guest's track vote (design doc §11); the host decides and bots don't vote. Returns true if it changed.</summary>
        public bool Vote(byte playerId, byte track)
        {
            var player = Find(playerId);
            if (player == null || player.IsHost || player.IsBot || player.TrackVote == track)
            {
                return false;
            }

            player.TrackVote = track;
            return true;
        }

        /// <summary>Withdraws every vote, e.g. when the lobby reopens after an event. Returns true if there were any.</summary>
        public bool ClearVotes()
        {
            bool any = false;
            foreach (var p in _players)
            {
                any |= p.TrackVote != VoteTrackMessage.NoVote;
                p.TrackVote = VoteTrackMessage.NoVote;
            }

            return any;
        }

        /// <summary>Clears every guest's Ready, e.g. when an event ends and the lobby reopens.</summary>
        public void ResetReady()
        {
            foreach (var p in _players)
            {
                p.Ready = p.IsHost || p.IsBot;
            }
        }

        public RosterMessage ToMessage()
        {
            var message = new RosterMessage();
            foreach (var p in _players)
            {
                message.Players.Add(new RosterEntry
                {
                    PlayerId = p.PlayerId,
                    Name = p.Name,
                    ColourIndex = p.ColourIndex,
                    BodyIndex = p.BodyIndex,
                    TrackVote = p.TrackVote,
                    Ready = p.Ready,
                    IsHost = p.IsHost,
                    IsBot = p.IsBot,
                });
            }

            return message;
        }

        /// <summary>Trims the name and limits it to <see cref="MaxNameChars"/>; a blank name becomes "Player".</summary>
        public static string CleanName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return "Player";
            }

            return trimmed.Length > MaxNameChars ? trimmed.Substring(0, MaxNameChars).TrimEnd() : trimmed;
        }

        private string NextBotName()
        {
            foreach (string name in BotNames)
            {
                if (!NameTaken(name, null))
                {
                    return name;
                }
            }

            return "Bot";
        }

        /// <summary>The cleaned name, or with " 2", " 3"… after it if someone other than <paramref name="self"/> has it.</summary>
        private string UniqueName(string name, LobbyPlayer self)
        {
            string clean = CleanName(name);
            string unique = clean;
            for (int n = 2; NameTaken(unique, self); n++)
            {
                string suffix = " " + n;
                unique = clean.Substring(0, Math.Min(clean.Length, MaxNameChars - suffix.Length)).TrimEnd() + suffix;
            }

            return unique;
        }

        /// <summary>Whether someone other than <paramref name="self"/> has <paramref name="name"/>, compared the way a cup tells players apart.</summary>
        private bool NameTaken(string name, LobbyPlayer self) =>
            _players.Exists(p => p != self && p.Name.ToLowerInvariant() == name.ToLowerInvariant());

        /// <summary><paramref name="wanted"/> if nobody has it, otherwise the next free colour after it (wrapping round).</summary>
        private byte FreeColourFrom(byte wanted)
        {
            for (int i = 0; i < ColourCount; i++)
            {
                byte candidate = (byte)((wanted + i) % ColourCount);
                if (!_players.Exists(p => p.ColourIndex == candidate))
                {
                    return candidate;
                }
            }

            return LowestFree(p => p.ColourIndex);
        }

        private byte LowestFree(Func<LobbyPlayer, byte> taken)
        {
            for (int candidate = 0; candidate < byte.MaxValue; candidate++)
            {
                if (!_players.Exists(p => taken(p) == candidate))
                {
                    return (byte)candidate;
                }
            }

            throw new InvalidOperationException("No free value.");
        }
    }
}
