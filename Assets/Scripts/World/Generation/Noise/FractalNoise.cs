namespace World.Generation.Noise
{
    /// <summary>
    /// Several octaves of <see cref="GradientNoise"/> stacked together
    /// (fractal Brownian motion). Each octave has its own permutation
    /// table so octaves do not line up with each other.
    /// Immutable and thread-safe.
    /// </summary>
    public sealed class FractalNoise
    {
        private readonly GradientNoise[] _octaves;
        private readonly double _frequency;
        private readonly double _lacunarity;
        private readonly double _gain;
        private readonly double _normalisation;

        /// <param name="seed">Seed for this layer.</param>
        /// <param name="scale">Size of the largest features, in blocks.</param>
        /// <param name="octaves">Number of detail layers.</param>
        /// <param name="lacunarity">Frequency multiplier per octave.</param>
        /// <param name="gain">Amplitude multiplier per octave.</param>
        public FractalNoise(
            long seed,
            double scale,
            int octaves,
            double lacunarity = 2.0,
            double gain = 0.5)
        {
            if (octaves < 1)
                octaves = 1;

            _octaves = new GradientNoise[octaves];

            for (int i = 0; i < octaves; i++)
                _octaves[i] = new GradientNoise(SeedHash.Derive(seed, i + 1));

            _frequency = 1.0 / (scale <= 0 ? 1.0 : scale);
            _lacunarity = lacunarity;
            _gain = gain;

            double amplitude = 1;
            double sum = 0;

            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude;
                amplitude *= gain;
            }

            _normalisation = 1.0 / sum;
        }

        /// <summary>Fractal noise, roughly in [-1, 1] (mostly within ±0.6).</summary>
        public double Sample(double x, double z)
        {
            double frequency = _frequency;
            double amplitude = 1;
            double sum = 0;

            for (int i = 0; i < _octaves.Length; i++)
            {
                sum += _octaves[i].Sample(x * frequency, z * frequency) * amplitude;
                frequency *= _lacunarity;
                amplitude *= _gain;
            }

            return sum * _normalisation;
        }

        public double Sample(double x, double y, double z)
        {
            double frequency = _frequency;
            double amplitude = 1;
            double sum = 0;

            for (int i = 0; i < _octaves.Length; i++)
            {
                sum += _octaves[i].Sample(
                    x * frequency,
                    y * frequency,
                    z * frequency) * amplitude;

                frequency *= _lacunarity;
                amplitude *= _gain;
            }

            return sum * _normalisation;
        }

        /// <summary>
        /// Ridged noise in [0, 1]: sharp crests where the base noise
        /// crosses zero. Used for mountain ridges.
        /// </summary>
        public double SampleRidged(double x, double z)
        {
            double frequency = _frequency;
            double amplitude = 1;
            double sum = 0;
            double weight = 1;

            for (int i = 0; i < _octaves.Length; i++)
            {
                double n = _octaves[i].Sample(x * frequency, z * frequency);
                n = 1.0 - (n < 0 ? -n : n);
                n *= n;
                n *= weight;

                weight = n * 2.0;
                if (weight > 1) weight = 1;
                if (weight < 0) weight = 0;

                sum += n * amplitude;
                frequency *= _lacunarity;
                amplitude *= _gain;
            }

            return sum * _normalisation;
        }
    }
}
