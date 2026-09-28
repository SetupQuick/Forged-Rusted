using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace Networking
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkWorld))]
    public sealed class NetworkRuntime : MonoBehaviour
    {
        private readonly List<INetworkModule> _modules =
            new List<INetworkModule>();

        [Header("Startup")]
        [SerializeField]
        private bool startAutomatically = true;

        [Header("Execution")]
        [SerializeField]
        private bool runInBackground = true;

        [SerializeField]
        private NetworkMode launchMode =
            NetworkMode.ListenServer;

        [Header("Connection")]
        [SerializeField]
        private string serverAddress = "127.0.0.1";

        [SerializeField]
        private ushort port = 25565;

        [Header("World")]
        [SerializeField]
        private NetworkWorld world;

        [Header("Diagnostics")]
        [SerializeField]
        private bool showNetworkOverlay = true;

        [SerializeField]
        [Min(1)]
        private int pendingInputWarningThreshold = 12;

        [SerializeField]
        [Min(0f)]
        private float correctionLogThreshold = 0.1f;

        [Header("Artificial network conditions")]
        [SerializeField]
        private NetworkSimulationSettings networkSimulation =
            new NetworkSimulationSettings();

        private GameServer _server;
        private GameClient _client;

        private double _previousTime;
        private bool _shuttingDown;

        public static NetworkRuntime Current
        {
            get;
            private set;
        }

        public NetworkMode Mode
        {
            get;
            private set;
        } = NetworkMode.Stopped;

        public NetworkWorld World => world;

        public GameServer Server => _server;

        public GameClient Client => _client;

        public NetworkDiagnostics Diagnostics
        {
            get;
            private set;
        }

        public NetworkSimulationSettings NetworkSimulation =>
            networkSimulation;

        public int PendingInputWarningThreshold =>
            pendingInputWarningThreshold;

        public uint LocalPeerId
        {
            get;
            private set;
        }

        public bool IsRunning =>
            Mode != NetworkMode.Stopped;

        public bool RunsServer =>
            Mode == NetworkMode.DedicatedServer ||
            Mode == NetworkMode.ListenServer;

        public bool RunsClient =>
            Mode == NetworkMode.Client ||
            Mode == NetworkMode.ListenServer;

        public event Action<Peer> PeerConnected;

        public event Action<uint> PeerDisconnected;

        public event Action<ClientConnectionState>
            ClientStateChanged;

        public event Action<string> Error;

        private void Awake()
        {
            if (Current != null &&
                Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;

            Application.runInBackground = runInBackground;

            if (world == null)
                world = GetComponent<NetworkWorld>();

            if (networkSimulation == null)
            {
                networkSimulation =
                    new NetworkSimulationSettings();
            }

            Diagnostics = new NetworkDiagnostics(
                pendingInputWarningThreshold,
                correctionLogThreshold);

            NetworkDebugOverlay overlay =
                GetComponent<NetworkDebugOverlay>();

            if (overlay == null)
                overlay = gameObject.AddComponent<NetworkDebugOverlay>();

            overlay.Initialize(this, showNetworkOverlay);

            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (Current != this ||
                !startAutomatically)
            {
                return;
            }

            StartRuntime(
                launchMode,
                serverAddress,
                port);
        }

        private void Update()
        {
            if (!IsRunning)
                return;

            double now = GetTime();
            double deltaTime =
                now - _previousTime;

            _previousTime = now;

            for (int index = 0;
                 index < _modules.Count;
                 index++)
            {
                _modules[index].Tick(
                    now,
                    deltaTime);
            }
        }

        public void StartRuntime(
            NetworkMode mode,
            string address,
            ushort serverPort)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException(
                    "Network runtime is already running.");
            }

            if (mode == NetworkMode.Stopped)
            {
                throw new ArgumentException(
                    "Cannot start in Stopped mode.",
                    nameof(mode));
            }

            if (world == null)
            {
                throw new InvalidOperationException(
                    "Assign a NetworkWorld " +
                    "to NetworkRuntime.");
            }

            Mode = mode;

            Diagnostics = new NetworkDiagnostics(
                pendingInputWarningThreshold,
                correctionLogThreshold);

            try
            {
                world.Initialize(this);

                switch (mode)
                {
                    case NetworkMode.Client:
                        InstallRemoteClient(
                            address,
                            serverPort);
                        break;

                    case NetworkMode.DedicatedServer:
                        InstallDedicatedServer(
                            serverPort);
                        break;

                    case NetworkMode.ListenServer:
                        InstallListenServer(
                            serverPort);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(mode));
                }

                SortModules();

                _previousTime = GetTime();
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        public void Shutdown()
        {
            if (_shuttingDown)
                return;

            _shuttingDown = true;

            // Reverse order matters for a listen server:
            // the local client disconnects before the server.
            _modules.Sort(
                (left, right) =>
                    left.DisposeOrder.CompareTo(
                        right.DisposeOrder));

            for (int index = 0;
                index < _modules.Count;
                index++)
            {
                try
                {
                    _modules[index].Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception);
                }
            }

            _modules.Clear();

            UnsubscribeClient();
            UnsubscribeServer();

            _client = null;
            _server = null;

            LocalPeerId = 0;

            world?.Shutdown();

            Mode = NetworkMode.Stopped;
            _shuttingDown = false;
        }

        private void OnDestroy()
        {
            if (Current != this)
                return;

            Shutdown();
            Current = null;
        }

        private void InstallRemoteClient(
            string address,
            ushort serverPort)
        {
            _client =
                new GameClient(
                    new DeliveryTransport(
                        CreateSimulatedTransport(
                            new UdpTransport())),
                    Diagnostics);

            SubscribeClient();

            var router =
                new ClientMessageRouter();

            var worldReplication =
                new ClientWorldReplication(
                    router,
                    world);

            var playerMovement =
                new ClientPlayerMovement(
                    _client,
                    router,
                    world);

            _modules.Add(
                new ClientNetworkModule(
                    _client,
                    router,
                    worldReplication,
                    playerMovement));

            _client.Connect(
                address,
                serverPort,
                GetTime());
        }

        private void InstallDedicatedServer(
            ushort serverPort)
        {
            _server =
                new GameServer(
                    new DeliveryTransport(
                        CreateSimulatedTransport(
                            new UdpTransport())));

            SubscribeServer();

            var router =
                new ServerMessageRouter();

            var playerMovement =
                new ServerPlayerMovement(
                    _server,
                    router,
                    world);

            var interestManager =
                new FullInterestManager(
                    world);

            var worldReplication =
                new ServerWorldReplication(
                    _server,
                    world,
                    interestManager);

            _modules.Add(
                new ServerNetworkModule(
                    _server,
                    router,
                    playerMovement,
                    worldReplication,
                    Diagnostics));

            _server.Start(
                serverPort);

            Debug.Log(
                $"Dedicated server started on " +
                $"UDP port {serverPort}.");
        }


        private void InstallListenServer(
            ushort serverPort)
        {
            LoopbackTransport.CreatePair(
                out LoopbackTransport serverLoopback,
                out LoopbackTransport clientLoopback);

            var serverTransport =
                CreateSimulatedTransport(
                    new CompositeServerTransport(
                        new UdpTransport(),
                        serverLoopback));

            _server =
                new GameServer(
                    new DeliveryTransport(
                        serverTransport));

            SubscribeServer();

            var serverRouter =
                new ServerMessageRouter();

            var serverPlayerMovement =
                new ServerPlayerMovement(
                    _server,
                    serverRouter,
                    world);

            var interestManager =
                new FullInterestManager(
                    world);

            var serverWorldReplication =
                new ServerWorldReplication(
                    _server,
                    world,
                    interestManager);

            _modules.Add(
                new ServerNetworkModule(
                    _server,
                    serverRouter,
                    serverPlayerMovement,
                    serverWorldReplication,
                    Diagnostics));

            _server.Start(
                serverPort);

            // ------------------------
            // Local loopback client
            // ------------------------

            _client =
                new GameClient(
                    new DeliveryTransport(
                        CreateSimulatedTransport(
                            clientLoopback)),
                    Diagnostics);

            SubscribeClient();

            var clientRouter =
                new ClientMessageRouter();

            var clientWorldReplication =
                new ClientWorldReplication(
                    clientRouter,
                    world);

            var clientPlayerMovement =
                new ClientPlayerMovement(
                    _client,
                    clientRouter,
                    world);

            _modules.Add(
                new ClientNetworkModule(
                    _client,
                    clientRouter,
                    clientWorldReplication,
                    clientPlayerMovement));

            _client.Connect(
                "loopback",
                0,
                GetTime());

            Debug.Log(
                $"Listen server started on " +
                $"UDP port {serverPort}.");
        }
        private INetworkTransport CreateSimulatedTransport(
            INetworkTransport transport)
        {
            return new SimulatedNetworkTransport(
                transport,
                networkSimulation,
                Diagnostics);
        }

        private void SortModules()
        {
            _modules.Sort(
                (left, right) =>
                    left.TickOrder.CompareTo(
                        right.TickOrder));
        }

        private void SubscribeServer()
        {
            _server.PeerConnected +=
                OnPeerConnected;

            _server.PeerDisconnected +=
                OnPeerDisconnected;

            _server.Error +=
                OnServerError;
        }

        private void UnsubscribeServer()
        {
            if (_server == null)
                return;

            _server.PeerConnected -=
                OnPeerConnected;

            _server.PeerDisconnected -=
                OnPeerDisconnected;

            _server.Error -=
                OnServerError;
        }

        private void SubscribeClient()
        {
            _client.StateChanged +=
                OnClientStateChanged;

            _client.Error +=
                OnClientError;
        }

        private void UnsubscribeClient()
        {
            if (_client == null)
                return;

            _client.StateChanged -=
                OnClientStateChanged;

            _client.Error -=
                OnClientError;
        }

        private void OnPeerConnected(Peer peer)
        {
            Debug.Log(
                $"Peer {peer.Id} connected.");

            PeerConnected?.Invoke(peer);
        }

        private void OnPeerDisconnected(
            uint peerId)
        {
            Debug.Log(
                $"Peer {peerId} disconnected.");

            PeerDisconnected?.Invoke(peerId);
        }

        private void OnClientStateChanged(
            ClientConnectionState state)
        {
            if (state ==
                ClientConnectionState.Connected)
            {
                LocalPeerId = _client.PeerId;
            }
            else if (
                state ==
                    ClientConnectionState.Stopped ||
                state ==
                    ClientConnectionState.TimedOut)
            {
                LocalPeerId = 0;
            }

            Debug.Log(
                $"Client connection state: {state}");

            ClientStateChanged?.Invoke(state);
        }

        private void OnServerError(
            string message)
        {
            string fullMessage =
                $"Server: {message}";

            Debug.LogWarning(fullMessage);
            Error?.Invoke(fullMessage);
        }

        private void OnClientError(
            string message)
        {
            string fullMessage =
                $"Client: {message}";

            Debug.LogWarning(fullMessage);
            Error?.Invoke(fullMessage);
        }

        private static double GetTime()
        {
            return
                (double)Stopwatch.GetTimestamp() /
                Stopwatch.Frequency;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType
                .SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Current = null;
        }
    }
}
