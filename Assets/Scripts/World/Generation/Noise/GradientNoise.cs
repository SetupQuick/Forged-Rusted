namespace World.Generation.Noise
{
    /// <summary>
    /// Seeded Perlin-style gradient noise in 2D and 3D.
    ///
    /// Unity's Mathf.PerlinNoise has no seed (the old generator faked
    /// one by shifting coordinates, so nearby seeds produced nearly the
    /// same world). This implementation shuffles its own permutation
    /// table from the seed. It is immutable after construction and
    /// therefore safe to share between worker threads.
    ///
    /// Only +, -, * and floor are used, in double precision, so the
    /// output is identical on every x64 platform.
    /// </summary>
    public sealed class GradientNoise
    {
        private readonly int[] _perm = new int[512];

        // 12 cube-edge gradients used by both 2D (x,z part) and 3D.
        private static readonly double[] GradX =
            { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };

        private static readonly double[] GradY =
            { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1 };

        private static readonly double[] GradZ =
            { 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1 };

        // 8 evenly spread 2D gradients.
        private const double Diagonal = 0.7071067811865476;

        private static readonly double[] Grad2X =
            { 1, -1, 0, 0, Diagonal, -Diagonal, Diagonal, -Diagonal };

        private static readonly double[] Grad2Y =
            { 0, 0, 1, -1, Diagonal, Diagonal, -Diagonal, -Diagonal };

        public GradientNoise(long seed)
        {
            var random = new WorldRandom((ulong)seed);
            var source = new int[256];

            for (int i = 0; i < 256; i++)
                source[i] = i;

            for (int i = 255; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                (source[i], source[j]) = (source[j], source[i]);
            }

            for (int i = 0; i < 512; i++)
                _perm[i] = source[i & 255];
        }

        /// <summary>2D noise, roughly in [-1, 1].</summary>
        public double Sample(double x, double y)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);

            double xf = x - xi;
            double yf = y - yi;

            int X = xi & 255;
            int Y = yi & 255;

            double u = Fade(xf);
            double v = Fade(yf);

            int aa = _perm[_perm[X] + Y];
            int ab = _perm[_perm[X] + Y + 1];
            int ba = _perm[_perm[X + 1] + Y];
            int bb = _perm[_perm[X + 1] + Y + 1];

            double x1 = Lerp(Grad2(aa, xf, yf), Grad2(ba, xf - 1, yf), u);
            double x2 = Lerp(Grad2(ab, xf, yf - 1), Grad2(bb, xf - 1, yf - 1), u);

            // Scale so the output uses most of [-1, 1].
            return Lerp(x1, x2, v) * 1.4142135623730951;
        }

        /// <summary>3D noise, roughly in [-1, 1].</summary>
        public double Sample(double x, double y, double z)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            int zi = FastFloor(z);

            double xf = x - xi;
            double yf = y - yi;
            double zf = z - zi;

            int X = xi & 255;
            int Y = yi & 255;
            int Z = zi & 255;

            double u = Fade(xf);
            double v = Fade(yf);
            double w = Fade(zf);

            int a = _perm[X] + Y;
            int aa = _perm[a] + Z;
            int ab = _perm[a + 1] + Z;
            int b = _perm[X + 1] + Y;
            int ba = _perm[b] + Z;
            int bb = _perm[b + 1] + Z;

            double x1 = Lerp(
                Grad3(_perm[aa], xf, yf, zf),
                Grad3(_perm[ba], xf - 1, yf, zf), u);

            double x2 = Lerp(
                Grad3(_perm[ab], xf, yf - 1, zf),
                Grad3(_perm[bb], xf - 1, yf - 1, zf), u);

            double y1 = Lerp(x1, x2, v);

            x1 = Lerp(
                Grad3(_perm[aa + 1], xf, yf, zf - 1),
                Grad3(_perm[ba + 1], xf - 1, yf, zf - 1), u);

            x2 = Lerp(
                Grad3(_perm[ab + 1], xf, yf - 1, zf - 1),
                Grad3(_perm[bb + 1], xf - 1, yf - 1, zf - 1), u);

            double y2 = Lerp(x1, x2, v);

            return Lerp(y1, y2, w);
        }

        public static int FastFloor(double value)
        {
            int truncated = (int)value;
            return value < truncated ? truncated - 1 : truncated;
        }

        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static double Grad2(int hash, double x, double y)
        {
            int h = hash & 7;
            return Grad2X[h] * x + Grad2Y[h] * y;
        }

        private static double Grad3(int hash, double x, double y, double z)
        {
            int h = hash % 12;
            return GradX[h] * x + GradY[h] * y + GradZ[h] * z;
        }
    }
}
