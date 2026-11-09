using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using World.Blocks;
using World.Chunks;

namespace World.Saving
{
    /// <summary>A player's saved state, keyed by their player id.</summary>
    public sealed class PlayerSaveData
    {
        public string Name = string.Empty;
        public int SelectedSlot;

        /// <summary>One entry per inventory slot: item id (null = empty) and count.</summary>
        public List<(string ItemId, int Count)> Slots = new List<(string, int)>();
    }

    /// <summary>Everything a world save holds.</summary>
    public sealed class WorldSaveData
    {
        public int Seed;
        public uint GeneratorVersion;
        public uint SettingsFingerprint;
        public double WorldSeconds;
        public float DayLengthSeconds = 1200f;
        public float StartTimeOfDay = 0.3f;
        public DateTime SavedAtUtc;

        /// <summary>Player edits per chunk; key = local block index (see WorldManager).</summary>
        public Dictionary<ChunkCoord, Dictionary<int, BlockId>> Edits =
            new Dictionary<ChunkCoord, Dictionary<int, BlockId>>();

        public Dictionary<string, PlayerSaveData> Players =
            new Dictionary<string, PlayerSaveData>();

        public int EditCount
        {
            get
            {
                int count = 0;
                foreach (Dictionary<int, BlockId> chunk in Edits.Values)
                    count += chunk.Count;
                return count;
            }
        }
    }

    /// <summary>
    /// Reads and writes world saves.
    ///
    /// Only what players changed is saved: the terrain itself is
    /// regenerated from the seed, so a save stays tiny (a few bytes per
    /// edited block) and loads instantly, however far people explored.
    ///
    /// Format: "FRSV" + format version, then a Deflate-compressed body
    /// (header, clock, edits grouped by chunk, players). Writing goes to a
    /// temporary file first and then replaces the old save, keeping the
    /// previous one as ".bak", so a crash or power cut during saving can
    /// never leave a broken world. Pure code - safe on a worker thread.
    /// </summary>
    public static class WorldSaveFile
    {
        public const string FileName = "world.frs";
        private const uint Magic = 0x56535246; // "FRSV"
        public const ushort FormatVersion = 1;

        public static void Write(string path, WorldSaveData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporary = path + ".tmp";

            using (FileStream file = File.Create(temporary))
            {
                using (var header = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
                {
                    header.Write(Magic);
                    header.Write(FormatVersion);
                }

                using (var deflate = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true))
                using (var writer = new BinaryWriter(deflate, Encoding.UTF8))
                    WriteBody(writer, data);

                file.Flush(true);
            }

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);
            }

            File.Move(temporary, path);
        }

        /// <summary>Reads a save; falls back to the backup if the main file is damaged.</summary>
        public static bool TryRead(string path, out WorldSaveData data, out string error)
        {
            if (TryReadFile(path, out data, out error))
                return true;

            string backup = path + ".bak";
            if (File.Exists(backup) && TryReadFile(backup, out data, out string backupError))
            {
                error = $"{error} - loaded the backup instead";
                return true;
            }

            return false;
        }

        private static bool TryReadFile(string path, out WorldSaveData data, out string error)
        {
            data = null;
            error = null;

            if (!File.Exists(path))
            {
                error = "no save file";
                return false;
            }

            try
            {
                using (FileStream file = File.OpenRead(path))
                {
                    var header = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

                    if (header.ReadUInt32() != Magic)
                    {
                        error = "not a world save";
                        return false;
                    }

                    ushort version = header.ReadUInt16();
                    if (version > FormatVersion)
                    {
                        error = $"saved by a newer version of the game (format {version})";
                        return false;
                    }

                    using (var deflate = new DeflateStream(file, CompressionMode.Decompress))
                    using (var reader = new BinaryReader(deflate, Encoding.UTF8))
                        data = ReadBody(reader, version);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is EndOfStreamException || exception is UnauthorizedAccessException)
            {
                error = $"damaged save ({exception.Message})";
                data = null;
                return false;
            }
        }

        private static void WriteBody(BinaryWriter writer, WorldSaveData data)
        {
            writer.Write(data.Seed);
            writer.Write(data.GeneratorVersion);
            writer.Write(data.SettingsFingerprint);
            writer.Write(data.WorldSeconds);
            writer.Write(data.DayLengthSeconds);
            writer.Write(data.StartTimeOfDay);
            writer.Write(DateTime.UtcNow.Ticks);

            writer.Write(data.Edits.Count);
            foreach (KeyValuePair<ChunkCoord, Dictionary<int, BlockId>> chunk in data.Edits)
            {
                writer.Write(chunk.Key.X);
                writer.Write(chunk.Key.Z);
                writer.Write(chunk.Value.Count);

                foreach (KeyValuePair<int, BlockId> edit in chunk.Value)
                {
                    writer.Write(edit.Key);
                    writer.Write((byte)edit.Value);
                }
            }

            writer.Write(data.Players.Count);
            foreach (KeyValuePair<string, PlayerSaveData> player in data.Players)
            {
                writer.Write(player.Key);
                writer.Write(player.Value.Name ?? string.Empty);
                writer.Write(player.Value.SelectedSlot);
                writer.Write(player.Value.Slots.Count);

                foreach ((string itemId, int count) in player.Value.Slots)
                {
                    writer.Write(itemId ?? string.Empty);
                    writer.Write(count);
                }
            }
        }

        private static WorldSaveData ReadBody(BinaryReader reader, ushort version)
        {
            var data = new WorldSaveData
            {
                Seed = reader.ReadInt32(),
                GeneratorVersion = reader.ReadUInt32(),
                SettingsFingerprint = reader.ReadUInt32(),
                WorldSeconds = reader.ReadDouble(),
                DayLengthSeconds = reader.ReadSingle(),
                StartTimeOfDay = reader.ReadSingle(),
                SavedAtUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc)
            };

            int chunkCount = Count(reader.ReadInt32());
            for (int c = 0; c < chunkCount; c++)
            {
                var coord = new ChunkCoord(reader.ReadInt32(), reader.ReadInt32());
                int editCount = Count(reader.ReadInt32());
                var edits = new Dictionary<int, BlockId>(editCount);

                for (int e = 0; e < editCount; e++)
                    edits[reader.ReadInt32()] = (BlockId)reader.ReadByte();

                data.Edits[coord] = edits;
            }

            int playerCount = Count(reader.ReadInt32());
            for (int p = 0; p < playerCount; p++)
            {
                string id = reader.ReadString();
                var player = new PlayerSaveData
                {
                    Name = reader.ReadString(),
                    SelectedSlot = reader.ReadInt32()
                };

                int slotCount = Count(reader.ReadInt32());
                for (int s = 0; s < slotCount; s++)
                {
                    string itemId = reader.ReadString();
                    int count = reader.ReadInt32();
                    player.Slots.Add((itemId.Length == 0 ? null : itemId, count));
                }

                data.Players[id] = player;
            }

            return data;
        }

        private static int Count(int value)
        {
            if (value < 0 || value > 50_000_000)
                throw new InvalidDataException($"bad count {value}");
            return value;
        }

        // ------------------------------------------------------------
        // World folders
        // ------------------------------------------------------------

        /// <summary>Turns a world name into a safe folder name.</summary>
        public static string FolderName(string worldName)
        {
            if (string.IsNullOrWhiteSpace(worldName))
                worldName = "World";

            var name = new StringBuilder();
            foreach (char ch in worldName.Trim())
                name.Append(char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' ? ch : '_');

            string result = name.ToString().Trim();
            return result.Length == 0 ? "World" : (result.Length > 48 ? result.Substring(0, 48) : result);
        }

        public static string PathFor(string savesRoot, string worldName) =>
            Path.Combine(savesRoot, FolderName(worldName), FileName);

        /// <summary>Names of the saved worlds in a folder, newest first.</summary>
        public static List<string> ListWorlds(string savesRoot)
        {
            var worlds = new List<(string Name, DateTime Time)>();

            if (Directory.Exists(savesRoot))
            {
                foreach (string directory in Directory.GetDirectories(savesRoot))
                {
                    string file = Path.Combine(directory, FileName);
                    if (File.Exists(file) || File.Exists(file + ".bak"))
                        worlds.Add((Path.GetFileName(directory), File.GetLastWriteTimeUtc(File.Exists(file) ? file : file + ".bak")));
                }
            }

            worlds.Sort((a, b) => b.Time.CompareTo(a.Time));
            return worlds.ConvertAll(w => w.Name);
        }
    }
}
