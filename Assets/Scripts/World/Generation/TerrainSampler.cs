using World.Generation.Noise;

namespace World.Generation
{
    /// <summary>
    /// Result of sampling the terrain at one (x, z) column.
    /// </summary>
    public struct ColumnSample
    {
        /// <summary>Y of the topmost terrain block (before caves).</summary>
        public int Height;

        public BiomeId Biome;

        /// <summary>Roughly -1 (freezing) .. 1 (hot), already cooled by altitude.</summary>
        public float Temperature;

        /// <summary>Roughly -1 (dry) .. 1 (wet).</summary>
        public float Humidity;

        /// <summary>0..1, how strongly mountains shape this column.</summary>
        public float Mountainness;

        /// <summary>Column lies in a river channel.</summary>
        public bool IsRiver;
    }

    /// <summary>
    /// Pure function from world (x, z) to terrain height and biome.
    ///
    /// The height comes from layered noise maps, roughly the way
    /// large voxel games do it:
    ///   continentalness  - ocean vs. coast vs. inland
    ///   erosion          - flat, worn land vs. rugged land
    ///   ridges           - mountain ranges where erosion is low
    ///   hills + detail   - local bumps
    ///   rivers           - winding channels carved down to sea level
    ///
    /// The biome is picked from temperature and humidity maps and the
    /// final height, but biome never changes the height, so biome
    /// borders cannot create cliffs.
    ///
    /// Immutable and thread-safe.
    /// </summary>
    public sealed class TerrainSampler
    {
        private readonly TerrainConfig _config;

        private readonly FractalNoise _continent;
        private readonly FractalNoise _erosion;
        private readonly FractalNoise _ridges;
        private readonly FractalNoise _hills;
        private readonly FractalNoise _detail;
        private readonly FractalNoise _river;
        private readonly FractalNoise _temperature;
        private readonly FractalNoise _humidity;

        // Voxels per metre; constants below are written in metres.
        private readonly double _metres;

        // Continentalness -> base height offset from sea level.
        private static readonly double[] ContinentKeys =
            { -1.00, -0.55, -0.30, -0.16, -0.08, 0.00, 0.12, 0.35, 1.00 };

        private static readonly double[] ContinentHeights =
            { -42, -32, -16, -6, -1, 2, 5, 12, 26 };

        public TerrainConfig Config => _config;

        public TerrainSampler(TerrainConfig config)
        {
            _config = config;

            long seed = config.Seed;
            double scale = config.ContinentScale;
            double biome = config.BiomeScale;

            _continent = new FractalNoise(SeedHash.Derive(seed, 101), scale, 5);
            _erosion = new FractalNoise(SeedHash.Derive(seed, 102), scale * 0.5, 4);
            _ridges = new FractalNoise(SeedHash.Derive(seed, 103), scale * 0.3, 5);
            double f = config.FeatureScale;
            _metres = f;

            _hills = new FractalNoise(SeedHash.Derive(seed, 104), 150 * f, 4);
            _detail = new FractalNoise(SeedHash.Derive(seed, 105), 36 * f, 3);
            _river = new FractalNoise(SeedHash.Derive(seed, 106), scale * 0.6, 4);
            _temperature = new FractalNoise(SeedHash.Derive(seed, 107), biome, 4);
            _humidity = new FractalNoise(SeedHash.Derive(seed, 108), biome * 0.85, 4);
        }

        public ColumnSample Sample(int worldX, int worldZ)
        {
            double x = worldX;
            double z = worldZ;
            int sea = _config.SeaLevel;

            // Domain warp so coastlines and ranges are not blobby.
            double warpX = x + _detail.Sample(x * 0.05 + 311, z * 0.05) * 60 * _metres;
            double warpZ = z + _detail.Sample(x * 0.05, z * 0.05 + 977) * 60 * _metres;

            // Slight positive bias: a bit more land than ocean.
            double continent = Clamp(_continent.Sample(warpX, warpZ) * 1.8 + 0.08, -1, 1);
            double erosion = Clamp(_erosion.Sample(warpX, warpZ) * 1.8, -1, 1);
            double ridges = _ridges.SampleRidged(warpX, warpZ);

            double landFactor = SmoothStep(-0.14, 0.04, continent);

            double height = sea + Spline(continent, ContinentKeys, ContinentHeights) * _metres;

            // Rolling hills, stronger inland and on rugged (low erosion) land.
            double hilliness = 0.35 + 0.65 * SmoothStep(0.5, -0.5, erosion);
            height += _hills.Sample(x, z) * 2.0 * _config.HillHeight * hilliness * landFactor;

            // Mountain ranges: inland, low erosion, along ridge lines.
            double mountainness =
                SmoothStep(0.02, 0.45, continent) *
                SmoothStep(0.35, -0.30, erosion);

            double peak = ridges * ridges * (0.6 + 0.4 * ridges);
            height += mountainness * peak * _config.MountainHeight;

            // Small surface detail everywhere.
            height += _detail.Sample(x, z) * 2.5 * _metres;

            bool isRiver = false;

            if (_config.RiversEnabled && continent > -0.10)
            {
                double river = _river.Sample(x, z);
                if (river < 0) river = -river;

                // Rivers fade out in high mountains instead of cutting canyons.
                double riverStrength =
                    SmoothStep(-0.10, 0.02, continent) *
                    (1.0 - SmoothStep(0.25, 0.65, mountainness));

                if (riverStrength > 0)
                {
                    const double channel = 0.012;
                    const double valley = 0.075;

                    double bed = sea - 4 * _metres + _detail.Sample(x * 2, z * 2) * _metres;
                    double t = SmoothStep(channel, valley, river);
                    double carved = bed + (height - bed) * t;

                    if (carved < height)
                        height = height + (carved - height) * riverStrength;

                    isRiver = river < channel * 1.8 && height < sea;
                }
            }

            int finalHeight = (int)System.Math.Floor(height);
            finalHeight = ClampInt(finalHeight, 6, _config.WorldHeight - (int)(20 * _metres));

            // Climate. Higher ground is colder.
            double temperature = Clamp(_temperature.Sample(x, z) * 1.9, -1, 1);
            double humidity = Clamp(_humidity.Sample(x, z) * 1.9, -1, 1);

            double altitude = finalHeight - sea;
            if (altitude > 0)
                temperature -= altitude / (200.0 * _metres) * 0.5;

            var sample = new ColumnSample
            {
                Height = finalHeight,
                Temperature = (float)temperature,
                Humidity = (float)humidity,
                Mountainness = (float)mountainness,
                IsRiver = isRiver
            };

            sample.Biome = PickBiome(sample, continent);
            return sample;
        }

        private BiomeId PickBiome(ColumnSample sample, double continent)
        {
            int sea = _config.SeaLevel;
            int height = sample.Height;
            double t = sample.Temperature;
            double h = sample.Humidity;

            if (height >= _config.SnowLine)
                return BiomeId.SnowyPeaks;

            if (height < sea - 1 && !sample.IsRiver && continent < -0.02)
                return t < -0.45 ? BiomeId.FrozenOcean : BiomeId.Ocean;

            if (height <= sea + 2 * _metres && continent < 0.06 && !sample.IsRiver)
                return t < -0.45 ? BiomeId.Tundra : BiomeId.Beach;

            if (sample.Mountainness > 0.45 && height > sea + 45 * _metres)
                return BiomeId.Mountains;

            if (t < -0.35)
                return h > -0.05 ? BiomeId.Taiga : BiomeId.Tundra;

            if (t > 0.35)
            {
                if (h < -0.15) return BiomeId.Desert;
                if (h < 0.30) return BiomeId.Savanna;
                return BiomeId.Forest;
            }

            if (h > 0.45 && height <= sea + 6 * _metres)
                return BiomeId.Swamp;

            return h > 0.05 ? BiomeId.Forest : BiomeId.Plains;
        }

        private static double Spline(double value, double[] keys, double[] values)
        {
            if (value <= keys[0])
                return values[0];

            for (int i = 1; i < keys.Length; i++)
            {
                if (value <= keys[i])
                {
                    double t = (value - keys[i - 1]) / (keys[i] - keys[i - 1]);
                    t = t * t * (3 - 2 * t);
                    return values[i - 1] + (values[i] - values[i - 1]) * t;
                }
            }

            return values[values.Length - 1];
        }

        public static double SmoothStep(double edge0, double edge1, double value)
        {
            double t = (value - edge0) / (edge1 - edge0);
            t = Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        public static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static int ClampInt(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
