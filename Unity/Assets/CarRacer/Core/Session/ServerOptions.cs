using System;
using System.Collections.Generic;
using System.Globalization;
using CarRacer.Core.Discovery;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// The command line of a dedicated host (design doc §2.3): <c>-server</c> to host with no
    /// player (the Linux server build always does), <c>-port</c> for the game port, and
    /// <c>-name</c> for the name phones list it under. Unity's own arguments are ignored. For the TV
    /// viewer (design doc §2.4): <c>-cast [TV]</c> casts to a Chromecast by itself (the first found
    /// if no TV is named), <c>-ffmpeg</c> says where ffmpeg is, and <c>-viewer-port</c> is the TCP
    /// port the stream is served on, and <c>-tv-music</c> plays the music on the TV.
    /// </summary>
    public sealed class ServerOptions
    {
        public const string ServerFlag = "-server";
        public const string PortFlag = "-port";
        public const string NameFlag = "-name";
        public const string CastFlag = "-cast";
        public const string FfmpegFlag = "-ffmpeg";
        public const string ViewerPortFlag = "-viewer-port";
        public const string TvMusicFlag = "-tv-music";
        public const string DefaultName = "Car Racer server";
        public const ushort DefaultViewerPort = 47780;

        public bool IsServer { get; private set; }
        public ushort Port { get; private set; } = DiscoveryConstants.DefaultGamePort;
        public string Name { get; private set; } = DefaultName;

        /// <summary>The TV to cast to by itself: a name, id or address; empty for the first one found; null for none.</summary>
        public string CastTo { get; private set; }

        /// <summary>The ffmpeg to stream with; "ffmpeg" looks on the PATH.</summary>
        public string FfmpegPath { get; private set; } = "ffmpeg";

        /// <summary>Whether the TV plays the music to begin with; the room leader can switch it.</summary>
        public bool TvMusic { get; private set; }

        /// <summary>The TCP port the TV viewer's stream is served on.</summary>
        public ushort ViewerPort { get; private set; } = DefaultViewerPort;

        /// <summary>Why the command line couldn't be used, or null if it could.</summary>
        public string Error { get; private set; }

        public static ServerOptions Parse(IReadOnlyList<string> args)
        {
            var options = new ServerOptions();
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, ServerFlag, StringComparison.OrdinalIgnoreCase))
                {
                    options.IsServer = true;
                }
                else if (string.Equals(arg, PortFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = i + 1 < args.Count ? args[++i] : string.Empty;
                    if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0 || port == DiscoveryConstants.DiscoveryPort)
                    {
                        options.Error = $"{PortFlag} needs a port from 1 to 65535 other than {DiscoveryConstants.DiscoveryPort} (used for discovery), not \"{value}\"";
                    }
                    else
                    {
                        options.Port = port;
                    }
                }
                else if (string.Equals(arg, NameFlag, StringComparison.OrdinalIgnoreCase))
                {
                    options.Name = LobbyRoster.CleanName(i + 1 < args.Count ? args[++i] : string.Empty);
                }
                else if (string.Equals(arg, CastFlag, StringComparison.OrdinalIgnoreCase))
                {
                    // The TV is optional: "-cast -name X" casts to the first one found.
                    bool named = i + 1 < args.Count && !args[i + 1].StartsWith("-", StringComparison.Ordinal);
                    options.CastTo = named ? args[++i].Trim() : string.Empty;
                }
                else if (string.Equals(arg, TvMusicFlag, StringComparison.OrdinalIgnoreCase))
                {
                    options.TvMusic = true;
                }
                else if (string.Equals(arg, FfmpegFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = i + 1 < args.Count ? args[++i] : string.Empty;
                    if (value.Trim().Length == 0)
                    {
                        options.Error = $"{FfmpegFlag} needs the path to ffmpeg";
                    }
                    else
                    {
                        options.FfmpegPath = value;
                    }
                }
                else if (string.Equals(arg, ViewerPortFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = i + 1 < args.Count ? args[++i] : string.Empty;
                    if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0)
                    {
                        options.Error = $"{ViewerPortFlag} needs a port from 1 to 65535, not \"{value}\"";
                    }
                    else
                    {
                        options.ViewerPort = port;
                    }
                }
            }

            return options;
        }
    }
}
