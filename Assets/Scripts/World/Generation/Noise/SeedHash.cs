namespace World.Generation.Noise
{
    /// <summary>
    /// Integer hashing helpers. Everything here is pure integer math,
    /// so results are bit-identical on every platform (the Windows
    /// client and the Linux dedicated server generate the same world).
    /// </summary>
    public static class SeedHash
    {
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        public static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        /// <summary>Derives an independent seed for one noise layer or feature.</summary>
        public static long Derive(long seed, int salt)
        {
            return (long)Mix((ulong)seed + Golden * (ulong)(uint)salt);
        }

        public static ulong Hash(long seed, int x, int z, int salt)
        {
            ulong h = (ulong)seed;
            h = Mix(h ^ ((ulong)(uint)x * Golden));
            h = Mix(h ^ ((ulong)(uint)z * 0xC2B2AE3D27D4EB4FUL));
            h = Mix(h ^ (ulong)(uint)salt);
            return h;
        }

        public static ulong Hash(long seed, int x, int y, int z, int salt)
        {
            ulong h = Hash(seed, x, z, salt);
            return Mix(h ^ ((ulong)(uint)y * 0x165667B19E3779F9UL));
        }

        /// <summary>Maps a hash to [0, 1).</summary>
        public static double ToUnit(ulong hash)
        {
            return (hash >> 11) * (1.0 / (1UL << 53));
        }
    }

    /// <summary>
    /// Small deterministic random number generator (SplitMix64).
    /// A struct so per-chunk and per-feature generators cost nothing
    /// to create.
    /// </summary>
    public struct WorldRandom
    {
        private ulong _state;

        public WorldRandom(ulong seed)
        {
            _state = seed;
        }

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            return SeedHash.Mix(_state);
        }

        /// <summary>Random integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>Random double in [0, 1).</summary>
        public double NextDouble()
        {
            return SeedHash.ToUnit(NextULong());
        }

        public bool Chance(double probability)
        {
            return NextDouble() < probability;
        }
    }
}
