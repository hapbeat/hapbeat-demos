using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Hapbeat.DemoSwitch
{
    internal readonly struct DemoSwitchDatagram
    {
        public DemoSwitchDatagram(byte[] payload, IPEndPoint source)
        {
            Payload = payload;
            Source = source;
        }

        public byte[] Payload { get; }
        public IPEndPoint Source { get; }
    }

    internal sealed class DemoSwitchUdpTransport : IDisposable
    {
        public const int QueueCapacity = 64;
        private readonly object _lifecycle = new object();
        private readonly BoundedDatagramQueue _received = new BoundedDatagramQueue(QueueCapacity);
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;

        public bool IsRunning => _running;
        public BoundedDatagramQueue Inbox => _received;

        public void Start(int port)
        {
            lock (_lifecycle)
            {
                if (_client != null) return;
                var client = new UdpClient(AddressFamily.InterNetwork);
                try
                {
                    client.ExclusiveAddressUse = true;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                    client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    var thread = new Thread(() => ReceiveLoop(client))
                    {
                        IsBackground = true,
                        Name = "Hapbeat Demo Switch UDP"
                    };
                    _client = client;
                    _thread = thread;
                    _running = true;
                    thread.Start();
                }
                catch
                {
                    _running = false;
                    _client = null;
                    _thread = null;
                    client.Dispose();
                    throw;
                }
            }
        }

        public void Send(string json, IPEndPoint endpoint)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            UdpClient client;
            lock (_lifecycle) client = _client;
            if (client != null)
            {
                client.Send(bytes, bytes.Length, endpoint);
                return;
            }

            using (var sender = new UdpClient(AddressFamily.InterNetwork)) sender.Send(bytes, bytes.Length, endpoint);
        }

        public void Stop()
        {
            UdpClient client;
            Thread thread;
            lock (_lifecycle)
            {
                client = _client;
                thread = _thread;
                if (client == null) return;
                _running = false;
            }

            client.Close();
            if (thread != null && thread != Thread.CurrentThread) thread.Join();

            lock (_lifecycle)
            {
                if (!ReferenceEquals(_client, client)) return;
                _client = null;
                _thread = null;
            }
        }

        public void Dispose() => Stop();

        private void ReceiveLoop(UdpClient client)
        {
            while (_running)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var payload = client.Receive(ref endpoint);
                    if (payload.Length <= DemoSwitchProtocol.MaxPayloadBytes)
                        _received.TryEnqueue(new DemoSwitchDatagram(payload, endpoint));
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (!_running) return; }
            }
        }
    }

    internal sealed class BoundedDatagramQueue
    {
        private readonly object _gate = new object();
        private readonly Queue<DemoSwitchDatagram> _queue;
        private readonly int _capacity;
        private int _dropped;

        public BoundedDatagramQueue(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _queue = new Queue<DemoSwitchDatagram>(capacity);
        }

        public int Count { get { lock (_gate) return _queue.Count; } }

        public bool TryEnqueue(DemoSwitchDatagram datagram)
        {
            lock (_gate)
            {
                if (_queue.Count >= _capacity)
                {
                    _dropped++;
                    return false;
                }
                _queue.Enqueue(datagram);
                return true;
            }
        }

        public bool TryDequeue(out DemoSwitchDatagram datagram)
        {
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    datagram = default;
                    return false;
                }
                datagram = _queue.Dequeue();
                return true;
            }
        }

        public int TakeDroppedCount()
        {
            lock (_gate)
            {
                var dropped = _dropped;
                _dropped = 0;
                return dropped;
            }
        }
    }

    internal static class DemoSwitchFrameDrain
    {
        public static int Drain(BoundedDatagramQueue inbox, int maximum, Action<DemoSwitchDatagram> handler)
        {
            var count = 0;
            while (count < maximum && inbox.TryDequeue(out var datagram))
            {
                handler(datagram);
                count++;
            }
            return count;
        }
    }
}
