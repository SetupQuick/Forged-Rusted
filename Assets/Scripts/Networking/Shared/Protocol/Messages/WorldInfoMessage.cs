namespace Networking
{
    /// <summary>
    /// Sent by the server to every client right after it connects
    /// (reliable, ordered, before any object spawns). Tells the client
    /// which world to generate.
    ///
    /// Only the seed is authoritative. The generator version and the
    /// settings fingerprint let a client detect that it was built with
    /// different world-generation code or settings, which would make
    /// its terrain differ from the server's.
    /// </summary>
    public readonly struct WorldInfoMessage
    {
        public const int Size =
            sizeof(int) +
            sizeof(ushort) +
            sizeof(uint);

        public int Seed { get; }

        public ushort GeneratorVersion { get; }

        public uint SettingsFingerprint { get; }

        public WorldInfoMessage(
            int seed,
            ushort generatorVersion,
            uint settingsFingerprint)
        {
            Seed = seed;
            GeneratorVersion = generatorVersion;
            SettingsFingerprint = settingsFingerprint;
        }

        public void Write(PacketWriter writer)
        {
            writer.Write(Seed);
            writer.Write(GeneratorVersion);
            writer.Write(SettingsFingerprint);
        }

        public static bool TryRead(
            PacketReader reader,
            out WorldInfoMessage message)
        {
            message = default;

            if (reader.Remaining != Size)
                return false;

            int seed = reader.ReadInt32();
            ushort version = reader.ReadUInt16();
            uint fingerprint = reader.ReadUInt32();

            message = new WorldInfoMessage(
                seed,
                version,
                fingerprint);

            return true;
        }

        public override string ToString()
        {
            return
                $"seed {Seed}, generator v{GeneratorVersion}, " +
                $"settings {SettingsFingerprint:X8}";
        }
    }
}
