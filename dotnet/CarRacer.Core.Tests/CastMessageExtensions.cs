using CarRacer.Core.Cast;

namespace CarRacer.Core.Tests
{
    internal static class CastMessageExtensions
    {
        /// <summary>The payload's "type" ("PING", "RECEIVER_STATUS"…), or null.</summary>
        public static string PayloadType(this CastMessage message) =>
            Json.String(Json.TryParseObject(message.Payload ?? string.Empty), "type");
    }
}
