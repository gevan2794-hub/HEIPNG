using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CarX.Telemetry
{
    /// <summary>
    /// Fire-and-forget UDP sender. One frame per datagram, UTF-8 JSON, no framing needed.
    /// Sends are non-blocking and failures are swallowed: telemetry must never be able to
    /// stall or crash the game's main thread, and a dropped frame is 16ms of nothing.
    /// </summary>
    public sealed class UdpJsonSender : IDisposable
    {
        private UdpClient _client;
        private IPEndPoint _endpoint;
        private int _consecutiveFailures;

        public string Host { get; private set; }
        public int Port { get; private set; }

        /// <summary>Frames handed to the socket since the last <see cref="Reconfigure"/>.</summary>
        public long Sent { get; private set; }

        /// <summary>Frames dropped because the socket refused them.</summary>
        public long Dropped { get; private set; }

        /// <summary>Set when the sender gave up after repeated failures.</summary>
        public bool Faulted { get; private set; }

        public string LastError { get; private set; }

        public UdpJsonSender(string host, int port) => Reconfigure(host, port);

        public void Reconfigure(string host, int port)
        {
            Close();

            Host = host;
            Port = port;
            Sent = 0;
            Dropped = 0;
            _consecutiveFailures = 0;
            Faulted = false;
            LastError = null;

            try
            {
                if (!IPAddress.TryParse(host, out var address))
                {
                    var entry = Dns.GetHostEntry(host);
                    if (entry.AddressList.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
                    address = entry.AddressList[0];
                }

                _endpoint = new IPEndPoint(address, port);
                _client = new UdpClient(address.AddressFamily) { Client = { Blocking = false } };
                if (IPAddress.Broadcast.Equals(address)) _client.EnableBroadcast = true;
            }
            catch (Exception ex)
            {
                Faulted = true;
                LastError = ex.Message;
                Close();
            }
        }

        public void Send(TelemetryFrame frame)
        {
            if (Faulted || _client == null) return;

            byte[] payload;
            try
            {
                payload = Encoding.UTF8.GetBytes(FlatJson.Write(frame));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Dropped++;
                return;
            }

            try
            {
                _client.Send(payload, payload.Length, _endpoint);
                Sent++;
                _consecutiveFailures = 0;
            }
            catch (SocketException ex)
            {
                Dropped++;
                LastError = ex.Message;

                // WouldBlock just means the send buffer is momentarily full. Anything else,
                // repeated, means the socket is broken (adapter went away, address changed);
                // stop hammering it and surface the fault so the UI can say so.
                if (ex.SocketErrorCode == SocketError.WouldBlock) return;
                if (++_consecutiveFailures >= 120) Faulted = true;
            }
            catch (ObjectDisposedException)
            {
                Faulted = true;
            }
        }

        private void Close()
        {
            try { _client?.Close(); } catch { /* closing a dead socket is not interesting */ }
            _client = null;
        }

        public void Dispose() => Close();
    }
}
