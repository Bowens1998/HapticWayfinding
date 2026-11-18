using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HapticWayfinding.Haptics
{
    /// <summary>
    /// Talks to the bHaptics Player desktop app over its local WebSocket API
    /// (ws://127.0.0.1:15881/v2/feedbacks), the same protocol used by bHaptics' tact-python / tact-js.
    /// No SDK, app registration or pattern files needed: we stream raw per-motor "dot" frames.
    /// Verified with bHaptics Player 3.6.13 + a vest reported as position "Vest".
    ///
    /// Latency design (measured in the lab 2026-11-18):
    ///  - The Player itself replaces frames with the same key and applies turnOff within ~1 ms,
    ///    but every message ends up as Bluetooth traffic to the vest. Sending faster than the BLE link
    ///    can drain makes the delay GROW over time (vibration lagging seconds behind).
    ///  - So we send as little as possible: only keys whose pattern changed (diff against what the Player
    ///    already has), turnOff only for keys that were on, plus a slow full-state refresh to self-heal.
    /// </summary>
    public class BHapticsWebSocketBackend : IDisposable
    {
        const string KeyFront = "hw_VestFront", KeyBack = "hw_VestBack", KeySingle = "hw_Vest";
        static readonly string[] AllKeys = { KeyFront, KeyBack, KeySingle };

        readonly Uri uri;
        readonly VestLayout layout;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        readonly SemaphoreSlim outboxSignal = new SemaphoreSlim(0);
        // What we believe the Player is currently playing per key (dots JSON, "" = off). Main thread only.
        readonly Dictionary<string, string> playing = new Dictionary<string, string>();

        volatile bool connected;
        volatile bool needFullRefresh = true;
        volatile string status = "Not started";
        volatile string lastPlayerMessage = "";
        long messagesSent;

        /// <summary>
        /// false: two keys, positions "VestFront" / "VestBack" (20 motors each, classic Tactot API).
        /// true:  one key, position "Vest" with 40 indices (0-19 front, 20-39 back), as TactSuit X40 reports itself.
        /// </summary>
        public bool singleVestPosition;

        public bool IsConnected => connected;
        public string Status => status;
        public long MessagesSent => Interlocked.Read(ref messagesSent);
        /// <summary>True after (re)connecting: the caller should send a full refresh.</summary>
        public bool NeedsFullRefresh => needFullRefresh;
        /// <summary>Last raw status JSON pushed by the bHaptics Player (shows connected devices).</summary>
        public string LastPlayerMessage => lastPlayerMessage;

        public BHapticsWebSocketBackend(string url, VestLayout layout)
        {
            uri = new Uri(url);
            this.layout = layout;
            foreach (var k in AllKeys) playing[k] = "";
            Task.Run(() => RunAsync(cts.Token));
        }

        /// <summary>
        /// Send the vest state. With fullRefresh=false only differences are sent (nothing at all if unchanged);
        /// with fullRefresh=true every key is re-sent (frames re-armed, silent keys turned off).
        /// </summary>
        public void Send(VestFrame frame, int durationMillis, bool fullRefresh)
        {
            if (!connected) return; // state will be pushed by the full refresh after reconnecting
            if (needFullRefresh) { fullRefresh = true; needFullRefresh = false; }

            var desired = new Dictionary<string, (string position, string dots)>
            {
                [KeyFront] = ("VestFront", ""),
                [KeyBack] = ("VestBack", ""),
                [KeySingle] = ("Vest", "")
            };
            if (singleVestPosition)
            {
                var dots = new StringBuilder(512);
                bool any = false;
                for (int s = 0; s < 2; s++)
                {
                    var side = (VestSide)s;
                    int offset = side == VestSide.Back ? VestLayout.MotorsPerSide : 0;
                    AppendSideDots(dots, ref any, frame, side, offset);
                }
                desired[KeySingle] = ("Vest", dots.ToString());
            }
            else
            {
                for (int s = 0; s < 2; s++)
                {
                    var side = (VestSide)s;
                    var dots = new StringBuilder(256);
                    bool any = false;
                    AppendSideDots(dots, ref any, frame, side, 0);
                    string key = side == VestSide.Front ? KeyFront : KeyBack;
                    desired[key] = (side == VestSide.Front ? "VestFront" : "VestBack", dots.ToString());
                }
            }

            var sb = new StringBuilder(1024);
            int items = 0;
            foreach (var k in AllKeys)
            {
                var (position, dots) = desired[k];
                string was = playing[k];
                if (dots.Length > 0)
                {
                    if (!fullRefresh && dots == was) continue;
                    AppendItem(sb, ref items);
                    sb.Append("{\"Type\":\"frame\",\"Key\":\"").Append(k).Append("\",\"Frame\":{\"position\":\"").Append(position)
                      .Append("\",\"dotPoints\":[").Append(dots).Append("],\"pathPoints\":[],\"durationMillis\":")
                      .Append(durationMillis.ToString(CultureInfo.InvariantCulture)).Append("}}");
                }
                else
                {
                    if (!fullRefresh && was.Length == 0) continue;
                    AppendItem(sb, ref items);
                    sb.Append("{\"Type\":\"turnOff\",\"Key\":\"").Append(k).Append("\"}");
                }
                playing[k] = dots;
            }
            if (items == 0) return;
            Enqueue("{\"Submit\":[" + sb + "]}");
        }

        void AppendSideDots(StringBuilder dots, ref bool any, VestFrame frame, VestSide side, int offset)
        {
            float[] values = frame.Side(side);
            for (int r = 0; r < VestLayout.Rows; r++)
            {
                for (int c = 0; c < VestLayout.Columns; c++)
                {
                    int intensity = (int)Math.Round(values[r * VestLayout.Columns + c] * 100.0);
                    if (intensity <= 0) continue;
                    if (intensity > 100) intensity = 100;
                    if (any) dots.Append(',');
                    dots.Append("{\"index\":").Append((layout.DeviceIndex(side, r, c) + offset).ToString(CultureInfo.InvariantCulture))
                        .Append(",\"intensity\":").Append(intensity.ToString(CultureInfo.InvariantCulture)).Append('}');
                    any = true;
                }
            }
        }

        static void AppendItem(StringBuilder sb, ref int items)
        {
            if (items > 0) sb.Append(',');
            items++;
        }

        public void StopAll()
        {
            foreach (var k in AllKeys) playing[k] = "";
            Enqueue("{\"Submit\":[{\"Type\":\"turnOffAll\",\"Key\":\"\"}]}");
        }

        void Enqueue(string json)
        {
            outbox.Enqueue(json);
            outboxSignal.Release();
        }

        async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        status = "Connecting to bHaptics Player at " + uri + " ...";
                        await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
                        while (outbox.TryDequeue(out _)) { } // drop anything queued while offline
                        needFullRefresh = true;
                        connected = true;
                        status = "Connected to bHaptics Player";
                        var receiveTask = ReceiveLoop(socket, ct);

                        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            await outboxSignal.WaitAsync(500, ct).ConfigureAwait(false);
                            // Messages are diffs, so they must all be sent, in order. Local socket: microseconds each.
                            while (outbox.TryDequeue(out string msg))
                            {
                                var bytes = Encoding.UTF8.GetBytes(msg);
                                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                                Interlocked.Increment(ref messagesSent);
                            }
                        }
                        await receiveTask.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    status = "bHaptics Player not reachable (" + e.GetBaseException().Message + "). Retrying...";
                }
                connected = false;
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            connected = false;
        }

        async Task ReceiveLoop(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        lastPlayerMessage = sb.ToString();
                        sb.Clear();
                    }
                }
            }
            catch (Exception)
            {
                // Connection dropped; RunAsync will reconnect.
            }
        }

        public void Dispose()
        {
            try
            {
                // Best effort: stop motors before closing.
                StopAll();
                Thread.Sleep(30);
            }
            catch { /* ignore */ }
            cts.Cancel();
        }
    }
}
