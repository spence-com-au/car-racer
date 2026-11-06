using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CarRacer.Core.Viewer
{
    /// <summary>
    /// Serves the TV viewer's HLS playlist and segments over HTTP on the LAN (design doc §2.4), for
    /// a Chromecast (or VLC) to fetch. It only answers GET, HEAD and OPTIONS for plain file names in
    /// one folder, adds the CORS headers a Cast receiver needs, and closes each connection after
    /// one reply. Runs on background threads.
    /// </summary>
    public sealed class ViewerHttpServer : IDisposable
    {
        public const ushort DefaultPort = 47780;
        private const int MaxRequestBytes = 8 * 1024;
        private const int ClientTimeoutMs = 5_000;

        private readonly TcpListener _listener;
        private readonly string _folder;
        private readonly Thread _thread;
        private volatile bool _stopping;

        /// <summary>Serves <paramref name="folder"/> on <paramref name="port"/> (0 for any free port); throws <see cref="SocketException"/> if the port is taken.</summary>
        public ViewerHttpServer(string folder, int port)
        {
            _folder = Path.GetFullPath(folder);
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Accept) { IsBackground = true, Name = "TV viewer HTTP" };
            _thread.Start();
        }

        public int Port { get; }

        /// <summary>The stream's URL for a device that reaches this machine at <paramref name="address"/>.</summary>
        public string UrlFor(IPAddress address) => $"http://{address}:{Port}/{ViewerStream.PlaylistName}";

        private void Accept()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception e) when (e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    return;
                }

                new Thread(() => Serve(client)) { IsBackground = true, Name = "TV viewer request" }.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = client.SendTimeout = ClientTimeoutMs;
                    var stream = client.GetStream();
                    string request = ReadRequestHead(stream);
                    if (request == null)
                    {
                        return;
                    }

                    string[] line = request.Split('\r', '\n')[0].Split(' ');
                    string method = line.Length >= 2 ? line[0] : string.Empty;
                    string path = line.Length >= 2 ? line[1] : string.Empty;
                    if (method == "OPTIONS")
                    {
                        Reply(stream, "204 No Content", null, null, includeBody: false);
                        return;
                    }

                    if (method != "GET" && method != "HEAD")
                    {
                        Reply(stream, "405 Method Not Allowed", "text/plain", Encoding.ASCII.GetBytes("Method not allowed\n"), includeBody: true);
                        return;
                    }

                    string file = FileFor(path);
                    byte[] body = null;
                    if (file != null)
                    {
                        try
                        {
                            body = File.ReadAllBytes(Path.Combine(_folder, file));
                        }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                        {
                            body = null;
                        }
                    }

                    if (body == null)
                    {
                        Reply(stream, "404 Not Found", "text/plain", Encoding.ASCII.GetBytes("Not found\n"), includeBody: method == "GET");
                        return;
                    }

                    Reply(stream, "200 OK", ContentType(file), body, includeBody: method == "GET");
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                {
                    // The client went away.
                }
            }
        }

        /// <summary>The file a request path names: a bare name of letters, digits, '.', '-' and '_', and no "..". Null for anything else.</summary>
        public static string FileFor(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/')
            {
                return null;
            }

            int query = path.IndexOf('?');
            string name = (query >= 0 ? path.Substring(1, query - 1) : path.Substring(1));
            if (name.Length == 0 || name.Length > 64 || name.Contains("..") || name[0] == '.')
            {
                return null;
            }

            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '-' && c != '_')
                {
                    return null;
                }
            }

            return name;
        }

        public static string ContentType(string file)
        {
            if (file.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                return "application/vnd.apple.mpegurl";
            }

            return file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? "video/mp2t" : "application/octet-stream";
        }

        private static string ReadRequestHead(NetworkStream stream)
        {
            var head = new StringBuilder();
            var buffer = new byte[1024];
            while (head.Length < MaxRequestBytes)
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    return null;
                }

                head.Append(Encoding.ASCII.GetString(buffer, 0, n));
                if (head.ToString().Contains("\r\n\r\n"))
                {
                    return head.ToString();
                }
            }

            return null;
        }

        private static void Reply(NetworkStream stream, string status, string contentType, byte[] body, bool includeBody)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            head.Append("Access-Control-Allow-Origin: *\r\n");
            head.Append("Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n");
            head.Append("Access-Control-Allow-Headers: *\r\n");
            head.Append("Cache-Control: no-cache\r\n");
            head.Append("Connection: close\r\n");
            if (contentType != null)
            {
                head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            }

            head.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            if (includeBody && body != null)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        public void Dispose()
        {
            _stopping = true;
            _listener.Stop();
            _thread.Join(1_000);
        }
    }
}
