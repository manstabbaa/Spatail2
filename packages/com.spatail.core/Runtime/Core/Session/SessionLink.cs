using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Spatail.Core
{
    /// <summary>
    /// A small LAN link between Spatail devices: the headset hosts, the dev tool (or another headset) joins by
    /// address. Socket work happens on background threads; call <see cref="Pump"/> from the main loop to receive
    /// events on the caller's thread.
    /// </summary>
    public sealed class SessionLink : IDisposable
    {
        public const int DefaultPort = 7777;

        sealed class Connection
        {
            public TcpClient client;
            public StreamWriter writer;
            public Peer peer = new Peer();
            public bool saidHello;
            public readonly object writeLock = new object();
        }

        readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        readonly List<Connection> _connections = new List<Connection>();
        readonly object _lock = new object();
        TcpListener _listener;
        Thread _acceptThread;
        volatile bool _running;

        public string LocalName = "Spatail";
        public string LocalRole = "";
        public string LocalDevice = "";

        public bool IsHost { get; private set; }
        public bool IsActive => _running;
        public int Port { get; private set; }

        /// <summary>A peer finished its handshake.</summary>
        public event Action<Peer> PeerJoined;
        public event Action<Peer> PeerLeft;
        public event Action<Peer> PeerMoved;
        /// <summary>A plan arrived from a peer and should be applied locally.</summary>
        public event Action<PlanResult, Peer> PlanReceived;
        /// <summary>Host only: a peer asked for a prompt to be planned.</summary>
        public event Action<string, Peer> PromptReceived;
        /// <summary>Client only: the host's status line for a prompt.</summary>
        public event Action<string, string, string> ReplyReceived;
        /// <summary>Human-readable connection status changes ("Hosting on 192.168.1.20:7777", "Disconnected").</summary>
        public event Action<string> StatusChanged;

        public List<Peer> Peers
        {
            get
            {
                var list = new List<Peer>();
                lock (_lock)
                    foreach (Connection c in _connections)
                        if (c.saidHello) list.Add(c.peer);
                return list;
            }
        }

        // ---- lifecycle ---------------------------------------------------------------------

        public void Host(int port = DefaultPort)
        {
            Stop();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            IsHost = true;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Spatail session accept" };
            _acceptThread.Start();
            Post(() => Status("Hosting on " + LocalAddress() + ":" + Port));
        }

        public void Join(string host, int port = DefaultPort)
        {
            Stop();
            IsHost = false;
            _running = true;
            Port = port;
            var thread = new Thread(() =>
            {
                try
                {
                    var client = new TcpClient { NoDelay = true };
                    var connect = client.BeginConnect(host, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        client.Close();
                        throw new TimeoutException("No answer from " + host + ":" + port);
                    }
                    client.EndConnect(connect);
                    Connection c = Register(client);
                    Post(() => Status("Connected to " + host + ":" + port));
                    ReadLoop(c);
                }
                catch (Exception ex)
                {
                    _running = false;
                    Post(() => Status("Could not connect: " + ex.Message));
                }
            }) { IsBackground = true, Name = "Spatail session client" };
            thread.Start();
        }

        public void Stop()
        {
            if (!_running && _listener == null) return;
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            _listener = null;
            List<Connection> copy;
            lock (_lock) { copy = new List<Connection>(_connections); _connections.Clear(); }
            foreach (Connection c in copy)
            {
                TrySend(c, SessionMessage.MakeBye(LocalName));
                try { c.client.Close(); } catch (Exception) { }
            }
            Post(() => Status("Disconnected"));
        }

        public void Dispose() => Stop();

        /// <summary>Delivers queued events. Call once per frame from the main thread.</summary>
        public void Pump()
        {
            Action action;
            while (_mainThread.TryDequeue(out action))
            {
                try { action(); }
                catch (Exception ex) { if (StatusChanged != null) StatusChanged("Session handler error: " + ex.Message); }
            }
        }

        // ---- sending -----------------------------------------------------------------------

        public void SendPlan(PlanResult plan) => Broadcast(SessionMessage.MakePlan(plan), null);

        public void SendPrompt(string text) => Broadcast(SessionMessage.MakePrompt(text, LocalName), null);

        public void SendPresence(Vec3 stationPosition, Vec3 stationForward) =>
            Broadcast(SessionMessage.MakePresence(LocalName, stationPosition, stationForward), null);

        public void SendReply(string text, string chip, string planner) => Broadcast(SessionMessage.MakeReply(text, chip, planner), null);

        void Broadcast(string line, Connection except)
        {
            List<Connection> copy;
            lock (_lock) copy = new List<Connection>(_connections);
            foreach (Connection c in copy)
                if (c != except) TrySend(c, line);
        }

        static void TrySend(Connection c, string line)
        {
            try
            {
                lock (c.writeLock)
                {
                    c.writer.Write(line);
                    c.writer.Write('\n');
                    c.writer.Flush();
                }
            }
            catch (Exception) { /* the read loop notices the broken socket and cleans up */ }
        }

        // ---- receiving ---------------------------------------------------------------------

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    Connection c = Register(client);
                    var thread = new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "Spatail session peer" };
                    thread.Start();
                }
                catch (Exception)
                {
                    if (_running) Thread.Sleep(100);
                }
            }
        }

        Connection Register(TcpClient client)
        {
            var c = new Connection
            {
                client = client,
                writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)),
            };
            lock (_lock) _connections.Add(c);
            TrySend(c, SessionMessage.MakeHello(LocalName, LocalRole, LocalDevice));
            return c;
        }

        void ReadLoop(Connection c)
        {
            try
            {
                using (var reader = new StreamReader(c.client.GetStream(), new UTF8Encoding(false)))
                {
                    string line;
                    while (_running && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        Handle(c, line);
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                bool removed;
                lock (_lock) removed = _connections.Remove(c);
                try { c.client.Close(); } catch (Exception) { }
                if (removed && c.saidHello) Post(() => { if (PeerLeft != null) PeerLeft(c.peer); });
                if (removed && !IsHost)
                {
                    _running = false;
                    Post(() => Status("Disconnected"));
                }
            }
        }

        void Handle(Connection c, string line)
        {
            object parsed;
            if (!Json.TryParse(line, out parsed)) return;
            var msg = parsed as Dictionary<string, object>;
            if (msg == null) return;

            switch (msg.Str("type", ""))
            {
                case SessionMessage.Hello:
                    c.peer.name = msg.Str("name", "Guest");
                    c.peer.role = msg.Str("role", "");
                    c.peer.device = msg.Str("device", "");
                    c.saidHello = true;
                    Post(() => { if (PeerJoined != null) PeerJoined(c.peer); });
                    break;

                case SessionMessage.Presence:
                    c.peer.position = msg.Vec("position");
                    c.peer.forward = msg.Vec("forward", Vec3.Forward);
                    c.peer.hasPose = true;
                    Post(() => { if (PeerMoved != null) PeerMoved(c.peer); });
                    // The host relays presence so every participant sees every other one.
                    if (IsHost) Broadcast(line, c);
                    break;

                case SessionMessage.Plan:
                {
                    var planJson = msg.Obj("plan");
                    if (planJson == null) break;
                    PlanResult plan = SpecJson.PlanFromJson(planJson);
                    Post(() => { if (PlanReceived != null) PlanReceived(plan, c.peer); });
                    if (IsHost) Broadcast(line, c);
                    break;
                }

                case SessionMessage.Prompt:
                {
                    string text = msg.Str("text", "");
                    if (text.Length > 0) Post(() => { if (PromptReceived != null) PromptReceived(text, c.peer); });
                    break;
                }

                case SessionMessage.Reply:
                {
                    string text = msg.Str("text", ""), chip = msg.Str("chip", ""), planner = msg.Str("planner", "");
                    Post(() => { if (ReplyReceived != null) ReplyReceived(text, chip, planner); });
                    break;
                }

                case SessionMessage.Bye:
                    try { c.client.Close(); } catch (Exception) { }
                    break;
            }
        }

        void Post(Action a) => _mainThread.Enqueue(a);

        void Status(string s)
        {
            if (StatusChanged != null) StatusChanged(s);
        }

        /// <summary>Best guess at this device's LAN address, for showing to someone who wants to join.</summary>
        public static string LocalAddress()
        {
            try
            {
                // Connecting a UDP socket sends nothing; it just makes the OS pick the outbound interface.
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("10.255.255.255", 9);
                    var ep = socket.LocalEndPoint as IPEndPoint;
                    if (ep != null) return ep.Address.ToString();
                }
            }
            catch (Exception) { }
            return "127.0.0.1";
        }
    }
}
