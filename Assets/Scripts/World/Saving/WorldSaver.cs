using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace World.Saving
{
    /// <summary>
    /// Keeps the hosted world saved:
    ///   - every <see cref="AutosaveMinutes"/> minutes if anything changed,
    ///   - when the host leaves the game, quits, or the server stops.
    ///
    /// The data is copied on the main thread (a few milliseconds even with
    /// tens of thousands of edits) and written on a worker thread, so
    /// autosaves never stutter the game. Only the machine that runs the
    /// server saves; clients have nothing to save.
    ///
    /// Player inventories come from <see cref="CollectPlayers"/>, which the
    /// game sets to the server's gameplay module.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSaver : MonoBehaviour
    {
        /// <summary>Where worlds live: one folder per world.</summary>
        public static string SavesRoot => Path.Combine(Application.persistentDataPath, "worlds");

        public static float AutosaveMinutes = 5f;

        private WorldManager _world;
        private Task _writing;
        private float _nextAutosave;
        private long _savedChangeCount;
        private int _savedPlayersVersion = -1;
        private Dictionary<string, PlayerSaveData> _lastPlayers;
        private Dictionary<Vector3Int, List<(string ItemId, int Count)>> _lastChests;

        public string WorldName { get; private set; }
        public string FilePath { get; private set; }

        /// <summary>Returns the players to save (id -> data). Set by the game.</summary>
        public Func<Dictionary<string, PlayerSaveData>> CollectPlayers { get; set; }

        /// <summary>Returns the chests to save. Set by the game.</summary>
        public Func<Dictionary<Vector3Int, List<(string ItemId, int Count)>>> CollectChests { get; set; }

        /// <summary>Changes whenever player data changed (for "is there anything to save").</summary>
        public Func<int> PlayersVersion { get; set; }

        /// <summary>The save that was loaded when the world started (null for a new world).</summary>
        public WorldSaveData Loaded { get; private set; }

        public DateTime LastSavedUtc { get; private set; }

        public static WorldSaver Active { get; private set; }

        /// <summary>
        /// Loads the named world into the WorldManager (or starts a new
        /// one) and keeps it saved from now on.
        /// </summary>
        public static WorldSaver Open(WorldManager world, string worldName, out string message)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            WorldSaver saver = world.GetComponent<WorldSaver>();
            if (saver == null)
                saver = world.gameObject.AddComponent<WorldSaver>();

            saver.OpenInternal(world, worldName, out message);
            return saver;
        }

        private void OpenInternal(WorldManager world, string worldName, out string message)
        {
            FinishWriting();

            _world = world;
            WorldName = WorldSaveFile.FolderName(worldName);
            FilePath = WorldSaveFile.PathFor(SavesRoot, WorldName);
            Active = this;

            if (WorldSaveFile.TryRead(FilePath, out WorldSaveData data, out string error))
            {
                Loaded = data;
                _lastPlayers = data.Players;
                _lastChests = data.Chests;
                world.LoadSave(data);
                message = $"Loaded world \"{WorldName}\" ({data.EditCount} changed blocks, {data.Players.Count} players)" +
                          (error != null ? $" - {error}" : string.Empty);
            }
            else
            {
                Loaded = null;
                _lastPlayers = null;
                _lastChests = null;
                world.StartNewWorld();

                message = File.Exists(FilePath)
                    ? $"Could not load \"{WorldName}\" ({error}); started a new world. The old file was kept."
                    : $"New world \"{WorldName}\" (seed {world.Seed})";

                if (File.Exists(FilePath))
                {
                    // Keep the damaged file for recovery; the new world saves next to it.
                    string broken = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Move(FilePath, broken); } catch (IOException) { }
                }
            }

            _savedChangeCount = world.ChangeCount;
            _savedPlayersVersion = PlayersVersion?.Invoke() ?? -1;
            _nextAutosave = Time.unscaledTime + AutosaveMinutes * 60f;

            Debug.Log(message);

            // A new world exists on disk from the start.
            if (Loaded == null)
                Save(blocking: false);
        }

        private void Update()
        {
            if (_world == null || Time.unscaledTime < _nextAutosave)
                return;

            _nextAutosave = Time.unscaledTime + AutosaveMinutes * 60f;

            if (HasUnsavedChanges)
                Save(blocking: false);
        }

        public bool HasUnsavedChanges =>
            _world != null &&
            (_world.ChangeCount != _savedChangeCount ||
             (PlayersVersion != null && PlayersVersion() != _savedPlayersVersion));

        /// <summary>
        /// Saves now. blocking = wait until the file is written (quitting);
        /// otherwise the file is written in the background.
        /// </summary>
        public void Save(bool blocking)
        {
            if (_world == null || !_world.IsReady || string.IsNullOrEmpty(FilePath))
                return;

            // One write at a time; the newest data wins.
            FinishWriting();

            WorldSaveData data = _world.CaptureSave();

            CachePlayers();

            if (_lastPlayers != null)
                data.Players = _lastPlayers;
            else if (Loaded != null)
                data.Players = Loaded.Players; // keep players we cannot see right now

            if (_lastChests != null)
                data.Chests = _lastChests;
            else if (Loaded != null)
                data.Chests = Loaded.Chests;

            _savedChangeCount = _world.ChangeCount;
            _savedPlayersVersion = PlayersVersion?.Invoke() ?? -1;
            string path = FilePath;

            _writing = Task.Run(() =>
            {
                try
                {
                    WorldSaveFile.Write(path, data);
                    LastSavedUtc = DateTime.UtcNow;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Saving the world failed: {exception.Message}");
                    _savedChangeCount = -1; // try again next time
                }
            });

            if (blocking)
                FinishWriting();
        }

        /// <summary>
        /// Reads the players now (call before the server shuts down, so a
        /// save afterwards still has everyone's latest inventory).
        /// </summary>
        public void CachePlayers()
        {
            try
            {
                Dictionary<string, PlayerSaveData> players = CollectPlayers?.Invoke();
                if (players != null)
                    _lastPlayers = players;

                Dictionary<Vector3Int, List<(string ItemId, int Count)>> chests = CollectChests?.Invoke();
                if (chests != null)
                    _lastChests = chests;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Saves (blocking) and stops saving this world.</summary>
        public void Close()
        {
            Save(blocking: true);
            _world = null;
            FilePath = null;

            if (Active == this)
                Active = null;
        }

        private void FinishWriting()
        {
            try
            {
                _writing?.Wait();
            }
            catch (AggregateException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
            }

            _writing = null;
        }

        private void OnApplicationQuit()
        {
            if (_world != null)
                Close();
        }

        private void OnDestroy()
        {
            if (_world != null)
                Close();
            else
                FinishWriting();
        }
    }
}
