using Networking;
using UnityEngine;
using World;
using World.Saving;

namespace Game
{
    /// <summary>
    /// Glue between the world saver and the network server, for hosting
    /// and for dedicated servers:
    ///   1. <see cref="Open"/> before the server starts: loads the named
    ///      world (seed, clock, player edits) or creates it;
    ///   2. <see cref="AttachServer"/> after it started: saved inventories
    ///      go to the server, and the saver reads them back when saving;
    ///   3. saving happens automatically (every few minutes, on leaving,
    ///      on quitting).
    /// </summary>
    public static class HostedWorld
    {
        private static NetworkRuntime _runtime;
        private static WorldSaver _attached;
        public static string Open(string worldName)
        {
            WorldManager world = WorldManager.Active;
            if (world == null)
                return "No world in this scene.";

            WorldSaver.Open(world, worldName, out string message);
            return message;
        }

        public static void AttachServer(NetworkRuntime runtime)
        {
            WorldSaver saver = WorldSaver.Active;
            if (saver == null || runtime == null)
                return;

            ServerGameplayModule gameplay = runtime.ServerGameplay;
            if (gameplay == null)
                return;

            gameplay.ImportPlayers(saver.Loaded?.Players);
            saver.CollectPlayers = () => gameplay.ExportPlayers();
            saver.PlayersVersion = () => gameplay.PlayersVersion;

            // Read inventories while the server still exists.
            Detach();
            _runtime = runtime;
            _attached = saver;
            runtime.BeforeShutdown += saver.CachePlayers;
        }

        /// <summary>Saves and stops saving (leaving a hosted game).</summary>
        public static void Close()
        {
            WorldSaver saver = WorldSaver.Active;
            if (saver == null)
                return;

            saver.Close();
            saver.CollectPlayers = null;
            saver.PlayersVersion = null;
            Detach();
        }

        private static void Detach()
        {
            if (_runtime != null && _attached != null)
                _runtime.BeforeShutdown -= _attached.CachePlayers;

            _runtime = null;
            _attached = null;
        }
    }
}
