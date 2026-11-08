using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;

namespace CarRacer.Core.Cast
{
    /// <summary>
    /// Runs a <see cref="CastSession"/> against a real device (design doc §2.4): a TLS connection to
    /// its Cast port on a background thread, with a second thread reading frames. The game thread
    /// only reads <see cref="State"/> and <see cref="Detail"/> and may call <see cref="Stop"/>.
    /// Chromecasts present self-signed certificates, so the certificate isn't checked; the stream
    /// is the same public picture anyone on the LAN can fetch.
    /// </summary>
    public sealed class CastConnection : IDisposable
    {
        /// <summary>How long connecting, and then the TLS handshake, may each take.</summary>
        public const int ConnectTimeoutMs = 5_000;

        private readonly object _lock = new object();
        private readonly Queue<CastMessage> _inbox = new Queue<CastMessage>();
        private readonly CastSession _session;
        private readonly Func<IPAddress, string> _mediaUrlFor;
        private readonly Thread _thread;
        private volatile bool _stopRequested;
        private volatile bool _readerDone;
        private CastState _state = CastState.Connecting;
        private string _detail = string.Empty;
        private TcpClient _client;

        /// <summary>
        /// Casts to <paramref name="device"/>. <paramref name="mediaUrlFor"/> gives the stream's URL
        /// for this machine's address on the network the device was reached over.
        /// </summary>
        public CastConnection(CastDevice device, Func<IPAddress, string> mediaUrlFor, string title, bool takeOver)
        {
            Device = device;
            _mediaUrlFor = mediaUrlFor;
            _session = new CastSession(title, takeOver);
            _thread = new Thread(Run) { IsBackground = true, Name = "Cast to " + device.Name };
            _thread.Start();
        }

        public CastDevice Device { get; }

        public CastState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        public string Detail
        {
            get
            {
                lock (_lock)
                {
                    return _detail;
                }
            }
        }

        public bool Finished => State.IsFinished();

        /// <summary>Ends the cast, closing the media player on the TV; returns at once.</summary>
        public void Stop() => _stopRequested = true;

        private void Run()
        {
            var clock = Stopwatch.StartNew();
            try
            {
                _client = new TcpClient(AddressFamily.InterNetwork);
                if (!_client.ConnectAsync(Device.Address, Device.Port).Wait(ConnectTimeoutMs))
                {
                    Publish(CastState.Failed, "Couldn't reach the TV");
                    return;
                }

                var local = ((IPEndPoint)_client.Client.LocalEndPoint).Address;
                using (var tls = new SslStream(_client.GetStream(), false, (sender, certificate, chain, errors) => true))
                {
                    // A device that takes the connection but never finishes the handshake fails like one that never answers.
                    _client.ReceiveTimeout = _client.SendTimeout = ConnectTimeoutMs;
                    tls.AuthenticateAsClient(Device.Address.ToString(), null, SslProtocols.Tls12, false);
                    _client.ReceiveTimeout = _client.SendTimeout = 0;
                    var reader = new Thread(() => Read(tls)) { IsBackground = true, Name = "Cast reader" };
                    reader.Start();
                    Send(tls, _session.Begin(_mediaUrlFor(local), clock.ElapsedMilliseconds));
                    while (true)
                    {
                        if (_stopRequested)
                        {
                            Send(tls, _session.Stop());
                        }

                        foreach (var message in Take())
                        {
                            Send(tls, _session.Handle(message, clock.ElapsedMilliseconds));
                        }

                        Send(tls, _session.Tick(clock.ElapsedMilliseconds));
                        if (_readerDone && !_session.Finished)
                        {
                            Publish(CastState.Failed, "The TV dropped the connection");
                            return;
                        }

                        Publish(_session.State, _session.Detail);
                        if (_session.Finished)
                        {
                            return;
                        }

                        lock (_inbox)
                        {
                            if (_inbox.Count == 0 && !_readerDone)
                            {
                                Monitor.Wait(_inbox, 200);
                            }
                        }
                    }
                }
            }
            catch (Exception e) when (IsConnectionLoss(e))
            {
                Publish(CastState.Failed, "Lost the TV: " + e.GetBaseException().Message);
            }
            finally
            {
                _client?.Dispose();
            }
        }

        private void Read(Stream stream)
        {
            try
            {
                while (true)
                {
                    var message = CastMessage.ReadFrom(stream);
                    if (message == null)
                    {
                        break;
                    }

                    lock (_inbox)
                    {
                        _inbox.Enqueue(message);
                        Monitor.Pulse(_inbox);
                    }
                }
            }
            catch (Exception e) when (IsConnectionLoss(e))
            {
                // Including the end of every cast, when Run disposes the stream this is blocked reading; the main thread notices through _readerDone.
            }
            finally
            {
                _readerDone = true;
                lock (_inbox)
                {
                    Monitor.Pulse(_inbox);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="e"/> means the connection to the TV is gone. Mono's <see cref="SslStream"/>
        /// wraps a failed read or write, even of a disposed stream, in an <see cref="AggregateException"/>.
        /// </summary>
        private static bool IsConnectionLoss(Exception e) =>
            e is IOException || e is SocketException || e is AuthenticationException || e is AggregateException || e is ObjectDisposedException || e is FormatException || e is InvalidOperationException;

        private List<CastMessage> Take()
        {
            lock (_inbox)
            {
                var messages = new List<CastMessage>(_inbox);
                _inbox.Clear();
                return messages;
            }
        }

        private static void Send(Stream stream, List<CastMessage> messages)
        {
            foreach (var message in messages)
            {
                byte[] frame = message.Frame();
                stream.Write(frame, 0, frame.Length);
            }

            if (messages.Count > 0)
            {
                stream.Flush();
            }
        }

        private void Publish(CastState state, string detail)
        {
            lock (_lock)
            {
                _state = state;
                _detail = detail ?? string.Empty;
            }
        }

        public void Dispose()
        {
            _stopRequested = true;
            if (!_thread.Join(1_000))
            {
                _client?.Dispose();
            }
        }
    }
}
