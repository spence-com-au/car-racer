using System.Collections.Generic;

namespace CarRacer.Core.Cast
{
    /// <summary>Where a cast has got to.</summary>
    public enum CastState
    {
        Connecting,
        Launching,
        Loading,

        /// <summary>The TV is showing the stream.</summary>
        Playing,

        /// <summary>The TV was showing something else and this cast wasn't allowed to take it over.</summary>
        Busy,

        /// <summary>The cast ended: stopped from here, or something else took the TV.</summary>
        Stopped,

        /// <summary>The TV couldn't be reached, refused, or couldn't play the stream.</summary>
        Failed,
    }

    public static class CastStates
    {
        /// <summary>Whether the cast is over and its connection can close.</summary>
        public static bool IsFinished(this CastState state) => state == CastState.Busy || state == CastState.Stopped || state == CastState.Failed;
    }

    /// <summary>
    /// One cast of the viewer stream to one Chromecast (design doc §2.4), as the Cast v2 protocol
    /// between this sender and the device: connect, look at what the TV is showing, launch
    /// Google's Default Media Receiver (no Cast app of our own needs registering), and load the
    /// live HLS stream into it, then answer heartbeats and notice when the cast ends. It only
    /// turns messages into messages; <see cref="CastConnection"/> carries them over TLS.
    /// </summary>
    public sealed class CastSession
    {
        public const string SenderId = "sender-0";
        public const string ReceiverId = "receiver-0";

        /// <summary>Google's Default Media Receiver, on every Chromecast.</summary>
        public const string MediaReceiverAppId = "CC1AD845";

        public const long HeartbeatMs = 5_000;

        /// <summary>A TV that says nothing for this long (it answers each heartbeat) is gone.</summary>
        public const long SilenceMs = 20_000;

        /// <summary>Connecting, launching or loading that takes longer than this has failed.</summary>
        public const long StepTimeoutMs = 20_000;

        private readonly string _title;
        private readonly bool _takeOver;
        private string _mediaUrl;
        private string _sessionId;
        private string _transportId;
        private int _requestId;
        private long _lastHeardMs;
        private long _lastPingMs;
        private long _stepStartedMs;

        /// <summary>Stop was asked for while the player was launching; it is closed once the TV says it's up.</summary>
        private bool _stopPending;

        /// <summary>
        /// A cast titled <paramref name="title"/>. With <paramref name="takeOver"/> it replaces
        /// whatever the TV is showing (the room leader asked); otherwise it only uses an idle TV.
        /// </summary>
        public CastSession(string title, bool takeOver)
        {
            _title = title;
            _takeOver = takeOver;
        }

        public CastState State { get; private set; } = CastState.Connecting;

        /// <summary>Why it is busy, stopped or failed, for the log and the leader's phone; empty otherwise.</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>Whether the cast is over and its connection can close.</summary>
        public bool Finished => State.IsFinished();

        /// <summary>Starts talking to the device once connected; <paramref name="mediaUrl"/> is the stream as the TV can reach it.</summary>
        public List<CastMessage> Begin(string mediaUrl, long nowMs)
        {
            _mediaUrl = mediaUrl;
            _lastHeardMs = _lastPingMs = _stepStartedMs = nowMs;
            State = CastState.Connecting;
            return new List<CastMessage>
            {
                Message(ReceiverId, CastMessage.ConnectionNamespace, new Dictionary<string, object> { ["type"] = "CONNECT" }),
                Message(ReceiverId, CastMessage.ReceiverNamespace, new Dictionary<string, object> { ["type"] = "GET_STATUS", ["requestId"] = ++_requestId }),
            };
        }

        /// <summary>Takes in one message from the device; returns what to send back.</summary>
        public List<CastMessage> Handle(CastMessage message, long nowMs)
        {
            var replies = new List<CastMessage>();
            _lastHeardMs = nowMs;
            if (Finished)
            {
                return replies;
            }

            var payload = Json.TryParseObject(message.Payload ?? string.Empty);
            string type = Json.String(payload, "type");
            switch (message.Namespace)
            {
                case CastMessage.HeartbeatNamespace:
                    if (type == "PING")
                    {
                        replies.Add(new CastMessage(SenderId, message.SourceId, CastMessage.HeartbeatNamespace, Json.Write(new Dictionary<string, object> { ["type"] = "PONG" })));
                    }

                    break;
                case CastMessage.ConnectionNamespace:
                    if (type == "CLOSE")
                    {
                        if (message.SourceId == ReceiverId)
                        {
                            End(CastState.Failed, "The TV closed the connection");
                        }
                        else if (message.SourceId == _transportId)
                        {
                            End(CastState.Stopped, "The TV stopped showing the race");
                        }
                    }

                    break;
                case CastMessage.ReceiverNamespace:
                    OnReceiverMessage(type, payload, replies, nowMs);
                    break;
                case CastMessage.MediaNamespace:
                    OnMediaMessage(type, payload);
                    break;
            }

            return replies;
        }

        /// <summary>Call often: sends heartbeats and notices a device that has gone quiet or a step that is taking too long.</summary>
        public List<CastMessage> Tick(long nowMs)
        {
            var messages = new List<CastMessage>();
            if (Finished)
            {
                return messages;
            }

            if (nowMs - _lastHeardMs > SilenceMs)
            {
                End(CastState.Failed, "The TV stopped answering");
                return messages;
            }

            if (State != CastState.Playing && nowMs - _stepStartedMs > StepTimeoutMs)
            {
                if (_stopPending)
                {
                    End(CastState.Stopped, "Stopped casting");
                    return messages;
                }

                End(CastState.Failed, State == CastState.Loading ? "The TV didn't start the stream" : "The TV didn't answer in time");
                return messages;
            }

            if (nowMs - _lastPingMs >= HeartbeatMs)
            {
                _lastPingMs = nowMs;
                messages.Add(Message(ReceiverId, CastMessage.HeartbeatNamespace, new Dictionary<string, object> { ["type"] = "PING" }));
            }

            return messages;
        }

        /// <summary>Ends the cast from here, closing the Default Media Receiver if this cast started it.</summary>
        public List<CastMessage> Stop()
        {
            var messages = new List<CastMessage>();
            if (Finished)
            {
                return messages;
            }

            if (_sessionId != null)
            {
                messages.Add(StopMessage());
            }
            else if (State == CastState.Launching)
            {
                // The player may still come up; close it once the TV reports its session (StepTimeoutMs bounds the wait).
                _stopPending = true;
                return messages;
            }

            End(CastState.Stopped, "Stopped casting");
            return messages;
        }

        private CastMessage StopMessage() =>
            Message(ReceiverId, CastMessage.ReceiverNamespace, new Dictionary<string, object> { ["type"] = "STOP", ["sessionId"] = _sessionId, ["requestId"] = ++_requestId });

        private void OnReceiverMessage(string type, Dictionary<string, object> payload, List<CastMessage> replies, long nowMs)
        {
            if (type == "LAUNCH_ERROR" || type == "INVALID_REQUEST")
            {
                End(CastState.Failed, "The TV wouldn't start the media player (" + (Json.String(payload, "reason") ?? type) + ")");
                return;
            }

            if (type != "RECEIVER_STATUS")
            {
                return;
            }

            var applications = Json.Array(Json.Object(payload, "status"), "applications");
            switch (State)
            {
                case CastState.Connecting:
                    var showing = applications.Count > 0 ? applications[0] as Dictionary<string, object> : null;
                    if (showing != null && !Json.Bool(showing, "isIdleScreen") && !_takeOver)
                    {
                        End(CastState.Busy, "The TV is showing " + (Json.String(showing, "displayName") ?? "something else"));
                        return;
                    }

                    State = CastState.Launching;
                    _stepStartedMs = nowMs;
                    replies.Add(Message(ReceiverId, CastMessage.ReceiverNamespace, new Dictionary<string, object> { ["type"] = "LAUNCH", ["appId"] = MediaReceiverAppId, ["requestId"] = ++_requestId }));
                    break;
                case CastState.Launching:
                    foreach (var item in applications)
                    {
                        var app = item as Dictionary<string, object>;
                        if (Json.String(app, "appId") == MediaReceiverAppId && Json.String(app, "transportId") != null)
                        {
                            _sessionId = Json.String(app, "sessionId");
                            _transportId = Json.String(app, "transportId");
                            if (_stopPending)
                            {
                                replies.Add(StopMessage());
                                End(CastState.Stopped, "Stopped casting");
                                return;
                            }

                            State = CastState.Loading;
                            _stepStartedMs = nowMs;
                            replies.Add(Message(_transportId, CastMessage.ConnectionNamespace, new Dictionary<string, object> { ["type"] = "CONNECT" }));
                            replies.Add(Message(_transportId, CastMessage.MediaNamespace, Load()));
                            return;
                        }
                    }

                    break;
                default:
                    // Loading or playing: something else taking the TV ends this cast.
                    bool ours = false;
                    foreach (var item in applications)
                    {
                        ours |= Json.String(item as Dictionary<string, object>, "sessionId") == _sessionId;
                    }

                    if (!ours)
                    {
                        End(CastState.Stopped, "Something else is on the TV now");
                    }

                    break;
            }
        }

        private void OnMediaMessage(string type, Dictionary<string, object> payload)
        {
            if (type == "LOAD_FAILED" || type == "LOAD_CANCELLED" || type == "INVALID_REQUEST")
            {
                End(CastState.Failed, "The TV couldn't load the stream (" + type + ")");
                return;
            }

            if (type != "MEDIA_STATUS")
            {
                return;
            }

            foreach (var item in Json.Array(payload, "status"))
            {
                var status = item as Dictionary<string, object>;
                string player = Json.String(status, "playerState");
                string idleReason = Json.String(status, "idleReason");
                if (player == "PLAYING" || player == "BUFFERING" || player == "PAUSED")
                {
                    State = CastState.Playing;
                }
                else if (player == "IDLE" && idleReason == "ERROR")
                {
                    End(CastState.Failed, "The TV couldn't play the stream");
                }
                else if (player == "IDLE" && idleReason != null && State == CastState.Playing)
                {
                    End(CastState.Stopped, "The TV stopped showing the race");
                }
            }
        }

        private Dictionary<string, object> Load() => new Dictionary<string, object>
        {
            ["type"] = "LOAD",
            ["requestId"] = ++_requestId,
            ["sessionId"] = _sessionId,
            ["autoplay"] = true,
            ["currentTime"] = 0,
            ["media"] = new Dictionary<string, object>
            {
                ["contentId"] = _mediaUrl,
                ["contentUrl"] = _mediaUrl,
                ["streamType"] = "LIVE",
                ["contentType"] = "application/x-mpegurl",
                ["hlsSegmentFormat"] = "ts",
                ["hlsVideoSegmentFormat"] = "mpeg2_ts",
                ["metadata"] = new Dictionary<string, object> { ["metadataType"] = 0, ["title"] = _title },
            },
        };

        private void End(CastState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        private static CastMessage Message(string destination, string ns, Dictionary<string, object> payload) =>
            new CastMessage(SenderId, destination, ns, Json.Write(payload));
    }
}
