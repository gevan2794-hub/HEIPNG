using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CarX.Telemetry.Receiver
{
    /// <summary>A parsed frame, ready to be published as SimHub properties.</summary>
    public sealed class Snapshot
    {
        public readonly Dictionary<string, double> Numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);
        public DateTime ReceivedUtc = DateTime.UtcNow;
        public long Sequence;
    }

    /// <summary>
    /// Receives telemetry datagrams on a background thread and hands the newest parsed
    /// frame to the SimHub update loop. Nothing blocks SimHub's thread: it only ever
    /// reads a reference that this class swaps atomically.
    /// </summary>
    public sealed class UdpJsonReceiver : IDisposable
    {
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private Snapshot _latest;

        public int Port { get; private set; }

        /// <summary>Datagrams accepted since Start.</summary>
        public long PacketsReceived { get; private set; }

        /// <summary>Datagrams that arrived but did not parse as a telemetry frame.</summary>
        public long PacketsRejected { get; private set; }

        /// <summary>Gaps detected in the sequence counter -- packets the network dropped.</summary>
        public long PacketsLost { get; private set; }

        public string LastError { get; private set; }

        public Snapshot Latest => _latest;

        public bool IsConnected =>
            _latest != null && (DateTime.UtcNow - _latest.ReceivedUtc) < TimeSpan.FromSeconds(2);

        public void Start(int port)
        {
            Stop();

            Port = port;
            PacketsReceived = 0;
            PacketsRejected = 0;
            PacketsLost = 0;
            LastError = null;

            try
            {
                _client = new UdpClient();
                // Let another tool (a second dashboard, a debug sniffer) share the port.
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _client.Client.ReceiveTimeout = 500;
            }
            catch (Exception ex)
            {
                LastError = $"could not listen on UDP {port}: {ex.Message}";
                _client?.Close();
                _client = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "CarXTelemetryReceiver"
            };
            _thread.Start();
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            long expectedSequence = -1;

            while (_running)
            {
                byte[] datagram;
                try
                {
                    datagram = _client.Receive(ref remote);
                }
                catch (SocketException ex)
                {
                    // The 500ms receive timeout is the loop's cancellation check, not an error.
                    if (ex.SocketErrorCode == SocketError.TimedOut) continue;
                    if (!_running) return;
                    LastError = ex.Message;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var snapshot = new Snapshot();
                string text;
                try
                {
                    text = Encoding.UTF8.GetString(datagram);
                }
                catch
                {
                    PacketsRejected++;
                    continue;
                }

                if (!FlatJson.TryRead(text, snapshot.Numbers, snapshot.Strings))
                {
                    PacketsRejected++;
                    continue;
                }

                if (snapshot.Numbers.TryGetValue("Sequence", out var sequence))
                {
                    snapshot.Sequence = (long)sequence;

                    // A backwards jump means the game restarted, not a loss.
                    if (expectedSequence >= 0 && snapshot.Sequence > expectedSequence)
                        PacketsLost += snapshot.Sequence - expectedSequence;

                    expectedSequence = snapshot.Sequence + 1;
                }

                PacketsReceived++;
                _latest = snapshot;
            }
        }

        public void Stop()
        {
            _running = false;

            try { _client?.Close(); } catch { /* already gone */ }
            _client = null;

            if (_thread != null && _thread.IsAlive)
            {
                if (!_thread.Join(TimeSpan.FromSeconds(2)))
                    LastError = "receiver thread did not stop cleanly";
            }

            _thread = null;
            _latest = null;
        }

        public void Dispose() => Stop();
    }
}
