using System;
using System.Collections.Generic;

namespace CarRacer.Core.Net
{
    /// <summary>How the TV viewer's cast is going (design doc §2.4), as every phone sees it.</summary>
    public enum TvState : byte
    {
        /// <summary>Nothing is being cast.</summary>
        Off = 0,
        Connecting = 1,
        Playing = 2,

        /// <summary>The TV is showing something else.</summary>
        Busy = 3,
        Failed = 4,
    }

    /// <summary>
    /// Host → all, when it changes and to each guest as it joins: whether the host can cast the
    /// room to a TV, the Chromecasts it has found, and how the cast is going (design doc §2.4).
    /// Only a server with ffmpeg sends <see cref="CanCast"/>; a phone host never casts.
    /// </summary>
    public sealed class TvStatusMessage : IGameMessage, IEquatable<TvStatusMessage>
    {
        public const int MaxDevices = 8;

        public bool CanCast;

        /// <summary>The TVs found, by name; at most <see cref="MaxDevices"/>.</summary>
        public List<string> Devices = new List<string>();

        /// <summary>The TV being cast to (or last tried), or empty.</summary>
        public string CastingTo = string.Empty;
        public TvState State;

        /// <summary>Why it is busy or failed, or empty.</summary>
        public string Detail = string.Empty;

        public MessageType Type => MessageType.TvStatus;

        public void Write(NetWriter w)
        {
            w.WriteBool(CanCast);
            int count = Math.Min(Devices.Count, MaxDevices);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                w.WriteString(Devices[i]);
            }

            w.WriteString(CastingTo ?? string.Empty);
            w.WriteByte((byte)State);
            w.WriteString(Detail ?? string.Empty);
        }

        public static TvStatusMessage Read(NetReader r)
        {
            var message = new TvStatusMessage { CanCast = r.ReadBool() };
            int count = r.ReadByte();
            if (count > MaxDevices)
            {
                throw new NetFormatException($"{count} TVs is more than {MaxDevices}.");
            }

            for (int i = 0; i < count; i++)
            {
                message.Devices.Add(r.ReadString());
            }

            message.CastingTo = r.ReadString();
            byte state = r.ReadByte();
            if (state > (byte)TvState.Failed)
            {
                throw new NetFormatException($"Unknown TV state {state}.");
            }

            message.State = (TvState)state;
            message.Detail = r.ReadString();
            return message;
        }

        public bool Equals(TvStatusMessage other) =>
            other != null && CanCast == other.CanCast && State == other.State && CastingTo == other.CastingTo
            && Detail == other.Detail && Devices.Count == other.Devices.Count && Devices.TrueForAll(other.Devices.Contains);

        public override bool Equals(object obj) => Equals(obj as TvStatusMessage);

        public override int GetHashCode() => HashCode.Combine(CanCast, State, CastingTo, Detail, Devices.Count);
    }

    /// <summary>Guest → host: the room leader picked a TV to cast to, or stopped casting; the host ignores anyone else.</summary>
    public sealed class CastCommandMessage : IGameMessage
    {
        /// <summary>Stop casting; otherwise cast to <see cref="Device"/>.</summary>
        public bool Stop;
        public string Device = string.Empty;

        public MessageType Type => MessageType.CastCommand;

        public void Write(NetWriter w)
        {
            w.WriteBool(Stop);
            w.WriteString(Device ?? string.Empty);
        }

        public static CastCommandMessage Read(NetReader r) => new CastCommandMessage { Stop = r.ReadBool(), Device = r.ReadString() };
    }

    /// <summary>How the TV viewer's state reads in the lobby (design doc §2.4).</summary>
    public static class TvText
    {
        /// <summary>The room leader's button: "Cast to TV", "Casting to Den…", "On TV: Den", "TV busy: Den" or "Can't reach Den".</summary>
        public static string ButtonLabel(TvStatusMessage status)
        {
            string tv = string.IsNullOrEmpty(status.CastingTo) ? "the TV" : status.CastingTo;
            switch (status.State)
            {
                case TvState.Connecting:
                    return "Casting to " + tv + "...";
                case TvState.Playing:
                    return "On TV: " + tv;
                case TvState.Busy:
                    return "TV busy: " + tv;
                case TvState.Failed:
                    return "Can't reach " + tv;
                default:
                    return "Cast to TV";
            }
        }

        /// <summary>The line everyone's lobby shows while the room is on a TV ("On TV: Den"), or null.</summary>
        public static string OnTvLine(TvStatusMessage status) =>
            status.State == TvState.Playing && !string.IsNullOrEmpty(status.CastingTo) ? "On TV: " + status.CastingTo : null;

        /// <summary>
        /// What a tap on the leader's button does: with the list closed, nothing cast and exactly one
        /// TV found, it casts straight to it (returned); otherwise null, and the list opens.
        /// </summary>
        public static string QuickCast(TvStatusMessage status) =>
            status.Devices.Count == 1 && (status.State == TvState.Off || status.State == TvState.Failed) ? status.Devices[0] : null;

        /// <summary>A short summary for rebuilding the lobby only when it changes.</summary>
        public static string Signature(TvStatusMessage status) =>
            status.CanCast + "|" + status.State + "|" + status.CastingTo + "|" + string.Join(",", status.Devices);
    }
}
