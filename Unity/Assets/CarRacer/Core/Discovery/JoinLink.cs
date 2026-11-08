using System;

namespace CarRacer.Core.Discovery
{
    /// <summary>
    /// The link in the host's QR code (design doc §1.1): <c>carracer://join?host=192.168.1.20&amp;port=7777</c>.
    /// A guest's camera app opens it, which launches the game straight into joining that host, so
    /// the game needs no QR scanner of its own.
    /// </summary>
    public static class JoinLink
    {
        public const string Scheme = "carracer";
        public const string Host = "join";

        public static string Make(string address, ushort port) =>
            $"{Scheme}://{Host}?host={Uri.EscapeDataString(address ?? string.Empty)}&port={port}";

        /// <summary>
        /// Reads a join link; false for anything else (another link, a missing or bad host or port). Any page or app
        /// can open a link, so the host must be an IPv4 address that <see cref="HostAddress.TryParse"/> accepts,
        /// as if the guest had typed it.
        /// </summary>
        public static bool TryParse(string link, out string address, out ushort port)
        {
            address = null;
            port = 0;
            string host = null;
            string prefix = $"{Scheme}://{Host}?";
            if (string.IsNullOrEmpty(link) || !link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string pair in link.Substring(prefix.Length).Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string key = pair.Substring(0, equals);
                string value = Uri.UnescapeDataString(pair.Substring(equals + 1));
                if (key == "host")
                {
                    host = value;
                }
                else if (key == "port" && ushort.TryParse(value, out ushort parsed))
                {
                    port = parsed;
                }
            }

            // The port has its own field, so one inside the host value makes the link bad rather than a second port.
            return port != 0 && host != null && host.IndexOf(':') < 0 && HostAddress.TryParse(host, out address, out _);
        }
    }
}
