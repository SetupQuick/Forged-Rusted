using System;
using World.Generation.Noise;

namespace World.Generation
{
    /// <summary>
    /// Decides which underground blocks are hollowed out into caves.
    ///
    /// Two kinds of caves:
    ///   caverns ("cheese") - large open chambers where a 3D noise is high;
    ///   tunnels ("spaghetti") - long winding tubes where two 3D noises
    ///                           are both close to zero.
    ///
    /// 3D noise per block is expensive, so noise is only evaluated on a
    /// coarse world-aligned grid (every <see cref="CellSize"/> blocks)
    /// and interpolated in between. Because the grid is aligned to world
    /// coordinates, the result at any block is a pure function of its
    /// position: a chunk computing it from its cached grid and a
    /// neighbour computing it directly get exactly the same answer.
    ///
    /// Immutable and thread-safe.
    /// </summary>
    public sealed class CaveCarver
    {
        public const int CellSize = 4;

        private const int MinCaveY = 5;

        private readonly TerrainConfig _config;
        private readonly FractalNoise _cavern;
        private readonly GradientNoise _tunnelA;
        private readonly GradientNoise _tunnelB;
        private readonly FractalNoise _entrances;
        private readonly double _metres;

        private readonly double _cavernThreshold;
        private readonly double _tunnelWidth;

        public CaveCarver(TerrainConfig config)
        {
            _config = config;

            long seed = config.Seed;
            _metres = config.FeatureScale;
            _cavern = new FractalNoise(SeedHash.Derive(seed, 201), 70 * _metres, 2);
            _tunnelA = new GradientNoise(SeedHash.Derive(seed, 202));
            _tunnelB = new GradientNoise(SeedHash.Derive(seed, 203));
            _entrances = new FractalNoise(SeedHash.Derive(seed, 204), 140 * _metres, 2);

            double density = config.CaveDensity;
            _cavernThreshold = 0.42 - 0.10 * (density - 1.0);
            _tunnelWidth = 0.055 * Math.Max(0.0, density);
        }

        public bool Enabled => _config.CavesEnabled && _config.CaveDensity > 0;

        /// <summary>Noise values at one grid corner.</summary>
        public struct Corner
        {
            public double Cavern;
            public double Tunnel;
        }

        public Corner SampleCorner(int cornerX, int cornerY, int cornerZ)
        {
            double x = cornerX * CellSize;
            double y = cornerY * CellSize;
            double z = cornerZ * CellSize;

            // Caverns are squashed vertically so they are wider than tall.
            double cavern = _cavern.Sample(x, y * 2.0, z);

            double tunnelFrequency = 1.0 / (90.0 * _metres);
            double a = _tunnelA.Sample(x * tunnelFrequency, y * tunnelFrequency * 1.6, z * tunnelFrequency);
            double b = _tunnelB.Sample(x * tunnelFrequency, y * tunnelFrequency * 1.6, z * tunnelFrequency);

            if (a < 0) a = -a;
            if (b < 0) b = -b;

            return new Corner
            {
                Cavern = cavern,
                Tunnel = a > b ? a : b
            };
        }

        /// <summary>
        /// Final carve decision from interpolated noise values.
        /// </summary>
        public bool ShouldCarve(
            double cavern,
            double tunnel,
            int worldX,
            int y,
            int worldZ,
            int surfaceHeight)
        {
            if (y < MinCaveY || y > surfaceHeight)
                return false;

            // Depth below the surface in metres, so the rules hold at any voxel size.
            double depth = (surfaceHeight - y) / _metres;
            int sea = _config.SeaLevel;

            // Never open caves under oceans, lakes and rivers.
            if (surfaceHeight < sea + 3 * _metres && depth < 10)
                return false;

            // Caverns: keep a solid roof near the surface, and make them
            // a bit more common deep down.
            double threshold = _cavernThreshold;

            if (depth < 12)
                threshold += (12 - depth) * 0.05;

            double heightMetres = y / _metres;
            if (heightMetres < 40)
                threshold -= (40 - heightMetres) * 0.003;

            if (cavern > threshold)
                return true;

            double width = _tunnelWidth;

            if (heightMetres < 24)
                width *= 1.25;

            if (tunnel >= width)
                return false;

            // Tunnels only break the surface in a few spots (cave
            // entrances); elsewhere they stay a few blocks underground.
            if (depth < 6)
                return _entrances.Sample(worldX, worldZ) > 0.28;

            return true;
        }

        /// <summary>
        /// Carve test for a single block, computed from scratch.
        /// Used for positions outside the current chunk.
        /// </summary>
        public bool IsCarved(int worldX, int y, int worldZ, int surfaceHeight)
        {
            if (!Enabled || y < MinCaveY || y > surfaceHeight)
                return false;

            int cx = FloorDiv(worldX, CellSize);
            int cy = FloorDiv(y, CellSize);
            int cz = FloorDiv(worldZ, CellSize);

            Corner c000 = SampleCorner(cx, cy, cz);
            Corner c100 = SampleCorner(cx + 1, cy, cz);
            Corner c010 = SampleCorner(cx, cy + 1, cz);
            Corner c110 = SampleCorner(cx + 1, cy + 1, cz);
            Corner c001 = SampleCorner(cx, cy, cz + 1);
            Corner c101 = SampleCorner(cx + 1, cy, cz + 1);
            Corner c011 = SampleCorner(cx, cy + 1, cz + 1);
            Corner c111 = SampleCorner(cx + 1, cy + 1, cz + 1);

            double fx = (worldX - cx * CellSize) / (double)CellSize;
            double fy = (y - cy * CellSize) / (double)CellSize;
            double fz = (worldZ - cz * CellSize) / (double)CellSize;

            Interpolate(
                c000, c100, c010, c110, c001, c101, c011, c111,
                fx, fy, fz,
                out double cavern, out double tunnel);

            return ShouldCarve(cavern, tunnel, worldX, y, worldZ, surfaceHeight);
        }

        /// <summary>
        /// Shared trilinear interpolation, so cached and direct paths
        /// produce bit-identical values.
        /// </summary>
        public static void Interpolate(
            Corner c000, Corner c100, Corner c010, Corner c110,
            Corner c001, Corner c101, Corner c011, Corner c111,
            double fx, double fy, double fz,
            out double cavern, out double tunnel)
        {
            cavern = Tri(
                c000.Cavern, c100.Cavern, c010.Cavern, c110.Cavern,
                c001.Cavern, c101.Cavern, c011.Cavern, c111.Cavern,
                fx, fy, fz);

            tunnel = Tri(
                c000.Tunnel, c100.Tunnel, c010.Tunnel, c110.Tunnel,
                c001.Tunnel, c101.Tunnel, c011.Tunnel, c111.Tunnel,
                fx, fy, fz);
        }

        private static double Tri(
            double v000, double v100, double v010, double v110,
            double v001, double v101, double v011, double v111,
            double fx, double fy, double fz)
        {
            double x00 = v000 + (v100 - v000) * fx;
            double x10 = v010 + (v110 - v010) * fx;
            double x01 = v001 + (v101 - v001) * fx;
            double x11 = v011 + (v111 - v011) * fx;

            double y0 = x00 + (x10 - x00) * fy;
            double y1 = x01 + (x11 - x01) * fy;

            return y0 + (y1 - y0) * fz;
        }

        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
                quotient--;
            return quotient;
        }
    }

    /// <summary>
    /// Cave noise grid cached for one chunk. Not thread-safe; create
    /// one per chunk being generated.
    /// </summary>
    public sealed class ChunkCaveField
    {
        private readonly CaveCarver _carver;
        private readonly CaveCarver.Corner[] _corners;
        private readonly int _originX;
        private readonly int _originZ;
        private readonly int _countX;
        private readonly int _countY;
        private readonly int _countZ;

        public ChunkCaveField(
            CaveCarver carver,
            int minWorldX,
            int minWorldZ,
            int sizeX,
            int sizeZ,
            int maxY)
        {
            _carver = carver;

            const int cell = CaveCarver.CellSize;

            _originX = CaveCarver.FloorDiv(minWorldX, cell);
            _originZ = CaveCarver.FloorDiv(minWorldZ, cell);

            int lastX = CaveCarver.FloorDiv(minWorldX + sizeX - 1, cell) + 1;
            int lastZ = CaveCarver.FloorDiv(minWorldZ + sizeZ - 1, cell) + 1;
            int lastY = CaveCarver.FloorDiv(Math.Max(0, maxY), cell) + 1;

            _countX = lastX - _originX + 1;
            _countZ = lastZ - _originZ + 1;
            _countY = lastY + 1;

            _corners = new CaveCarver.Corner[_countX * _countY * _countZ];

            for (int cy = 0; cy < _countY; cy++)
            for (int cz = 0; cz < _countZ; cz++)
            for (int cx = 0; cx < _countX; cx++)
            {
                _corners[Index(cx, cy, cz)] =
                    carver.SampleCorner(_originX + cx, cy, _originZ + cz);
            }
        }

        public bool IsCarved(int worldX, int y, int worldZ, int surfaceHeight)
        {
            if (y < 0 || y > surfaceHeight)
                return false;

            const int cell = CaveCarver.CellSize;

            int gx = CaveCarver.FloorDiv(worldX, cell);
            int gy = CaveCarver.FloorDiv(y, cell);
            int gz = CaveCarver.FloorDiv(worldZ, cell);

            int lx = gx - _originX;
            int lz = gz - _originZ;

            if (lx < 0 || lz < 0 || gy < 0 ||
                lx + 1 >= _countX || lz + 1 >= _countZ || gy + 1 >= _countY)
            {
                return _carver.IsCarved(worldX, y, worldZ, surfaceHeight);
            }

            double fx = (worldX - gx * cell) / (double)cell;
            double fy = (y - gy * cell) / (double)cell;
            double fz = (worldZ - gz * cell) / (double)cell;

            CaveCarver.Interpolate(
                _corners[Index(lx, gy, lz)],
                _corners[Index(lx + 1, gy, lz)],
                _corners[Index(lx, gy + 1, lz)],
                _corners[Index(lx + 1, gy + 1, lz)],
                _corners[Index(lx, gy, lz + 1)],
                _corners[Index(lx + 1, gy, lz + 1)],
                _corners[Index(lx, gy + 1, lz + 1)],
                _corners[Index(lx + 1, gy + 1, lz + 1)],
                fx, fy, fz,
                out double cavern, out double tunnel);

            return _carver.ShouldCarve(cavern, tunnel, worldX, y, worldZ, surfaceHeight);
        }

        private int Index(int x, int y, int z)
        {
            return x + _countX * (z + _countZ * y);
        }
    }
}
