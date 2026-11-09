using System;
using Networking;
using UnityEngine;
using World;
using World.Streaming;

namespace Game
{
    /// <summary>
    /// Runs when a scene starts (no scene setup needed):
    ///   1. loads the player's settings and starts applying them;
    ///   2. completes the scene: adds the network runtime, world and chunk
    ///      streamer if they are missing (from assets in Resources), so even
    ///      an empty scene is playable;
    ///   3. reads command-line options (dedicated server, quick host/join);
    ///   4. otherwise shows the main menu instead of starting a game.
    ///
    /// Command line:
    ///   -server [-port 25565]      dedicated server, no menu
    ///   -host [-port 25565]        host straight away
    ///   -connect 1.2.3.4[:port]    join straight away
    ///   -benchmark [-quit]         run the graphics benchmark (and quit when done)
    ///   -nomenu                    old behaviour: NetworkRuntime starts by itself
    /// </summary>
    public static class GameBootstrap
    {
        public const ushort DefaultPort = 25565;

        /// <summary>Messages about scene problems, shown in the main menu.</summary>
        public static string SceneProblems { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            GameSettings.EnsureLoaded();

            string[] args = Environment.GetCommandLineArgs();
            bool dedicated = Application.isBatchMode || HasArg(args, "-server");

            if (HasArg(args, "-nomenu"))
            {
                CreatePersistentObjects(withMenu: false);
                return;
            }

            // The runtime's own Start() runs after this; keep it from
            // starting a game by itself - the menu (or the command line)
            // decides.
            NetworkRuntime.SuppressAutoStart = true;

            CompleteScene(dedicated);

            ushort port = ParsePort(args);

            if (dedicated)
            {
                CreatePersistentObjects(withMenu: false);
                StartWhenReady(NetworkMode.DedicatedServer, string.Empty, port);
                return;
            }

            GameFlow flow = CreatePersistentObjects(withMenu: true);

            string connect = GetArgValue(args, "-connect");

            if (!string.IsNullOrEmpty(connect))
            {
                string address = connect;
                int colon = connect.LastIndexOf(':');

                if (colon > 0 && ushort.TryParse(connect.Substring(colon + 1), out ushort p))
                {
                    address = connect.Substring(0, colon);
                    port = p;
                }

                flow.Join(address, port);
            }
            else if (HasArg(args, "-host"))
            {
                flow.Host(port);
            }
            else if (HasArg(args, "-benchmark"))
            {
                flow.StartBenchmark(quitWhenDone: HasArg(args, "-quit"));
            }
        }

        /// <summary>Adds whatever the game needs and the scene lacks.</summary>
        public static void CompleteScene(bool dedicatedServer)
        {
            SceneProblems = string.Empty;

            if (NetworkRuntime.Current == null && UnityEngine.Object.FindFirstObjectByType<NetworkRuntime>() == null)
                CreateNetworkRuntime();

            WorldManager world = UnityEngine.Object.FindFirstObjectByType<WorldManager>();

            if (world == null)
            {
                var settings = Resources.Load<WorldSettings>("World/DefaultWorldSettings");

                if (settings == null)
                    Problem("World settings missing (Resources/World/DefaultWorldSettings).");
                else
                    world = WorldManager.Create(settings);
            }

            if (world != null && UnityEngine.Object.FindFirstObjectByType<ChunkStreamer>() == null)
                world.gameObject.AddComponent<ChunkStreamer>();
        }

        private static void CreateNetworkRuntime()
        {
            var registry = Resources.Load<NetPrefabRegistry>("Network/DefaultNetPrefabRegistry");
            var playerPrefab = Resources.Load<GameObject>("Network/NetworkPlayer");

            if (registry == null || playerPrefab == null)
            {
                Problem("No NetworkRuntime in the scene and the default network assets are missing (Resources/Network).");
                return;
            }

            var go = new GameObject("Network");
            go.SetActive(false);

            var networkWorld = go.AddComponent<NetworkWorld>();
            networkWorld.SetPrefabRegistry(registry);
            go.AddComponent<NetworkRuntime>();

            var spawner = go.AddComponent<PeerSpawnExample>();
            spawner.PlayerPrefab = playerPrefab.GetComponent<NetObject>();

            go.SetActive(true);

            Debug.Log("GameBootstrap: the scene had no NetworkRuntime, created one from Resources/Network.");
        }

        private static GameFlow CreatePersistentObjects(bool withMenu)
        {
            var go = new GameObject("Game");
            UnityEngine.Object.DontDestroyOnLoad(go);

            go.AddComponent<GameSettingsApplier>();

            if (UnityEngine.Object.FindFirstObjectByType<FpsCounter>() == null)
                go.AddComponent<FpsCounter>();

            return withMenu ? go.AddComponent<GameFlow>() : null;
        }

        private static void StartWhenReady(NetworkMode mode, string address, ushort port)
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            if (runtime == null)
            {
                Debug.LogError("GameBootstrap: no NetworkRuntime, cannot start a " + mode + ".");
                return;
            }

            if (!runtime.IsRunning)
                runtime.StartRuntime(mode, address, port);

            Debug.Log($"Started {mode} on port {port}.");
        }

        private static void Problem(string message)
        {
            Debug.LogError("GameBootstrap: " + message);
            SceneProblems += message + "\n";
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static ushort ParsePort(string[] args)
        {
            string value = GetArgValue(args, "-port");
            return value != null && ushort.TryParse(value, out ushort port) ? port : DefaultPort;
        }
    }
}
