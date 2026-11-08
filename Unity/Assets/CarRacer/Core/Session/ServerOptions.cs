using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CarRacer.Core.Discovery;

namespace CarRacer.Core.Session
{
    /// <summary>
    /// The command line of a dedicated host (design doc §2.3): <c>-server</c> to host with no
    /// player (the Linux server build always does), <c>-port</c> for the game port, and
    /// <c>-name</c> for the name phones list it under. Unity's own arguments are ignored. For the TV
    /// viewer (design doc §2.4): <c>-cast [TV]</c> casts to a Chromecast by itself (the first found
    /// if no TV is named), <c>-ffmpeg</c> says where ffmpeg is, and <c>-viewer-port</c> is the TCP
    /// port the stream is served on, and <c>-tv-music</c> plays the music on the TV. <c>-help</c> (or
    /// <c>--help</c>) prints them all instead of hosting.
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
        public const string HelpFlag = "-help";
        public const string DefaultName = "Car Racer server";
        public const string DefaultFfmpegPath = "ffmpeg";
        public const ushort DefaultViewerPort = 47780;

        public bool IsServer { get; private set; }
        public ushort Port { get; private set; } = DiscoveryConstants.DefaultGamePort;
        public string Name { get; private set; } = DefaultName;

        /// <summary>The TV to cast to by itself: a name, id or address; empty for the first one found; null for none.</summary>
        public string CastTo { get; private set; }

        /// <summary>The ffmpeg to stream with; "ffmpeg" looks on the PATH.</summary>
        public string FfmpegPath { get; private set; } = DefaultFfmpegPath;

        /// <summary>Whether the TV plays the music to begin with; the room leader can switch it.</summary>
        public bool TvMusic { get; private set; }

        /// <summary>The TCP port the TV viewer's stream is served on.</summary>
        public ushort ViewerPort { get; private set; } = DefaultViewerPort;

        /// <summary>Whether to print <see cref="Usage"/> and exit rather than host.</summary>
        public bool Help { get; private set; }

        /// <summary>Why the command line couldn't be used, or null if it could.</summary>
        public string Error { get; private set; }

        /// <summary>What <see cref="HelpFlag"/> prints: each option <c>run-server.sh</c> passes on, with its default.</summary>
        public static string Usage => string.Join("\n",
            "Usage: run-server.sh [option...]",
            Option(NameFlag + " <name>", $"What phones list the game as, up to {LobbyRoster.MaxNameChars} characters (default \"{DefaultName}\")"),
            Option(PortFlag + " <port>", $"The game port guests connect to, over UDP (default {DiscoveryConstants.DefaultGamePort})"),
            Option(CastFlag + " [TV]", "Cast the room to a Chromecast by itself: a TV's name, id or address, or none for the first found"),
            Option(FfmpegFlag + " <path>", $"The ffmpeg the TV stream is made with (default \"{DefaultFfmpegPath}\", from the PATH)"),
            Option(ViewerPortFlag + " <port>", $"The TCP port the TV fetches the stream from (default {DefaultViewerPort})"),
            Option(TvMusicFlag, "Play the music on the TV, and phones fade theirs; the room leader can switch it"),
            Option(HelpFlag, "Print this and exit"));

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
                else if (string.Equals(arg, HelpFlag, StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-" + HelpFlag, StringComparison.OrdinalIgnoreCase))
                {
                    options.Help = true;
                }
                else if (string.Equals(arg, PortFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = NextValue(args, ref i);
                    if (!HostAddress.TryParsePort(value, out ushort port) || port == DiscoveryConstants.DiscoveryPort)
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
                    string value = NextValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        options.Error = $"{NameFlag} needs a name";
                    }
                    else
                    {
                        options.Name = LobbyRoster.CleanName(value);
                    }
                }
                else if (string.Equals(arg, CastFlag, StringComparison.OrdinalIgnoreCase))
                {
                    // The TV is optional: "-cast -name X" casts to the first one found.
                    options.CastTo = NextValue(args, ref i)?.Trim() ?? string.Empty;
                }
                else if (string.Equals(arg, TvMusicFlag, StringComparison.OrdinalIgnoreCase))
                {
                    options.TvMusic = true;
                }
                else if (string.Equals(arg, FfmpegFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = NextValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(value))
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
                    string value = NextValue(args, ref i);
                    if (!HostAddress.TryParsePort(value, out ushort port))
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

        private static string Option(string flag, string description)
        {
            return "  " + flag.PadRight(22) + description;
        }

        /// <summary>
        /// Takes the value after the flag at <paramref name="i"/>, or returns null without taking anything if nothing
        /// or another flag follows, so a flag missing its value never swallows the next one.
        /// </summary>
        private static string NextValue(IReadOnlyList<string> args, ref int i)
        {
            if (i + 1 < args.Count && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                return args[++i];
            }

            return null;
        }
    }

    /// <summary>The dedicated server's log (design doc §2.3), which shows text that other devices choose.</summary>
    public static class ServerLog
    {
        /// <summary>
        /// <paramref name="text"/> with each control character written as <c>\uXXXX</c>, so text from another device (a
        /// player's or TV's name, the TV the room leader picks or what a TV reports) can't start a log line of its own
        /// or send the operator's terminal escape sequences.
        /// </summary>
        public static string Escape(string text)
        {
            var escaped = new StringBuilder();
            foreach (char c in text ?? string.Empty)
            {
                if (char.IsControl(c))
                {
                    escaped.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    escaped.Append(c);
                }
            }

            return escaped.ToString();
        }
    }
}
