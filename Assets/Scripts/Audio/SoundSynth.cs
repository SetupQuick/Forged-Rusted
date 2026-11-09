using System;
using World.Blocks;

namespace GameAudio
{
    /// <summary>What a block sounds like when stepped on, broken or placed.</summary>
    public enum SoundMaterial
    {
        Grass,
        Dirt,
        Stone,
        Sand,
        Snow,
        Wood,
        Leaves,
        Water,
        Glass
    }

    /// <summary>
    /// Generates short sound effects from noise and tones, so the game has
    /// sounds without any audio files. Pure C#; returns raw samples.
    /// Replace with recorded sounds whenever the team has them.
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 44100;

        public static SoundMaterial MaterialOf(BlockId block)
        {
            switch (block)
            {
                case BlockId.Grass:
                case BlockId.TallGrass:
                case BlockId.DryGrass:
                case BlockId.FlowerRed:
                case BlockId.FlowerYellow:
                case BlockId.FlowerBlue:
                case BlockId.FlowerWhite:
                    return SoundMaterial.Grass;

                case BlockId.Dirt:
                case BlockId.Clay:
                    return SoundMaterial.Dirt;

                case BlockId.Sand:
                case BlockId.Gravel:
                    return SoundMaterial.Sand;

                case BlockId.Snow:
                    return SoundMaterial.Snow;

                case BlockId.Log:
                case BlockId.PineLog:
                case BlockId.Cactus:
                    return SoundMaterial.Wood;

                case BlockId.Leaves:
                case BlockId.PineLeaves:
                case BlockId.AutumnLeaves:
                case BlockId.RedLeaves:
                    return SoundMaterial.Leaves;

                case BlockId.Water:
                    return SoundMaterial.Water;

                case BlockId.Ice:
                    return SoundMaterial.Glass;

                default:
                    return SoundMaterial.Stone;
            }
        }

        /// <summary>A footstep on a material (variant picks a different take).</summary>
        public static float[] Footstep(SoundMaterial material, int variant)
        {
            var random = new Random(1000 + (int)material * 31 + variant);

            switch (material)
            {
                case SoundMaterial.Grass:
                case SoundMaterial.Leaves:
                    return Noise(random, 0.13f, cutoff: 1800, decay: 28, grains: 0.35f, volume: 0.55f);
                case SoundMaterial.Dirt:
                    return Mix(Noise(random, 0.11f, 900, 34, 0.2f, 0.6f), Tone(random, 0.09f, 95, 40, 0.35f));
                case SoundMaterial.Sand:
                    return Noise(random, 0.16f, 3200, 22, 0.75f, 0.5f);
                case SoundMaterial.Snow:
                    return Noise(random, 0.18f, 2400, 18, 0.85f, 0.45f);
                case SoundMaterial.Wood:
                    return Mix(Tone(random, 0.12f, 210, 38, 0.6f), Noise(random, 0.05f, 2500, 60, 0f, 0.25f));
                case SoundMaterial.Water:
                    return Splash(random, 0.25f, 0.45f);
                case SoundMaterial.Glass:
                    return Mix(Tone(random, 0.08f, 1400, 50, 0.25f), Noise(random, 0.06f, 6000, 70, 0f, 0.3f));
                default:
                    return Mix(Noise(random, 0.07f, 5200, 55, 0.1f, 0.55f), Tone(random, 0.07f, 170, 50, 0.3f));
            }
        }

        public static float[] Break(SoundMaterial material, int variant)
        {
            var random = new Random(2000 + (int)material * 31 + variant);

            switch (material)
            {
                case SoundMaterial.Wood:
                    return Mix(Tone(random, 0.22f, 160, 18, 0.6f), Noise(random, 0.2f, 1800, 16, 0.6f, 0.5f));
                case SoundMaterial.Glass:
                    return Mix(Tone(random, 0.3f, 1900, 14, 0.3f), Noise(random, 0.25f, 7000, 14, 0.9f, 0.5f));
                case SoundMaterial.Grass:
                case SoundMaterial.Leaves:
                    return Noise(random, 0.22f, 2200, 14, 0.7f, 0.6f);
                case SoundMaterial.Sand:
                case SoundMaterial.Snow:
                case SoundMaterial.Dirt:
                    return Noise(random, 0.24f, 1600, 13, 0.8f, 0.65f);
                default:
                    return Mix(Noise(random, 0.25f, 4200, 13, 0.85f, 0.6f), Tone(random, 0.15f, 120, 22, 0.35f));
            }
        }

        public static float[] Place(SoundMaterial material, int variant)
        {
            var random = new Random(3000 + (int)material * 31 + variant);
            float pitch = material == SoundMaterial.Wood ? 190 : material == SoundMaterial.Stone ? 150 : 110;

            return Mix(Tone(random, 0.12f, pitch, 30, 0.6f), Noise(random, 0.06f, 2600, 50, 0.2f, 0.4f));
        }

        public static float[] Splash(int variant) => Splash(new Random(4000 + variant), 0.5f, 0.6f);

        public static float[] Swim(int variant) => Splash(new Random(5000 + variant), 0.35f, 0.3f);

        // ------------------------------------------------------------

        /// <summary>Filtered noise burst. Grains make it crunchy (sand, snow).</summary>
        private static float[] Noise(Random random, float seconds, float cutoff, float decay, float grains, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var samples = new float[n];
            float alpha = Alpha(cutoff);
            float lp = 0f, grain = 1f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;

                if (grains > 0 && random.NextDouble() < 0.004)
                    grain = 1f - grains * (float)random.NextDouble();

                float white = (float)(random.NextDouble() * 2 - 1);
                lp += alpha * (white - lp);

                float envelope = Attack(t) * (float)Math.Exp(-decay * t) * grain;
                samples[i] = lp * envelope * volume * 2.5f;
            }

            return Normalise(samples, volume);
        }

        /// <summary>Decaying tone with a little pitch drop (thuds, knocks).</summary>
        private static float[] Tone(Random random, float seconds, float frequency, float decay, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var samples = new float[n];
            float f = frequency * (0.92f + (float)random.NextDouble() * 0.16f);
            double phase = 0;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float freq = f * (1f - 0.3f * t / seconds);
                phase += 2 * Math.PI * freq / SampleRate;

                float wave = (float)(Math.Sin(phase) + 0.3 * Math.Sin(phase * 2.01));
                samples[i] = wave * Attack(t) * (float)Math.Exp(-decay * t) * volume;
            }

            return samples;
        }

        /// <summary>Noise with a falling filter: water splashes and strokes.</summary>
        private static float[] Splash(Random random, float seconds, float volume)
        {
            int n = (int)(seconds * SampleRate);
            var samples = new float[n];
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float cutoff = 4000f * (float)Math.Exp(-5 * t) + 300f;
                lp += Alpha(cutoff) * ((float)(random.NextDouble() * 2 - 1) - lp);

                // Bubbly amplitude wobble.
                float bubbles = 0.75f + 0.25f * (float)Math.Sin(t * 2 * Math.PI * (18 + 10 * random.NextDouble()));
                samples[i] = lp * bubbles * Attack(t * 0.5f) * (float)Math.Exp(-6 * t);
            }

            return Normalise(samples, volume);
        }

        private static float Attack(float t) => t < 0.004f ? t / 0.004f : 1f;

        private static float Alpha(float cutoff)
        {
            float rc = 1f / (2f * (float)Math.PI * cutoff);
            float dt = 1f / SampleRate;
            return dt / (rc + dt);
        }

        private static float[] Mix(float[] a, float[] b)
        {
            var result = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < result.Length; i++)
                result[i] = (i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f);
            return Normalise(result, 0.7f);
        }

        private static float[] Normalise(float[] samples, float peak)
        {
            float max = 0f;
            foreach (float s in samples)
                max = Math.Max(max, Math.Abs(s));

            if (max > 1e-5f)
            {
                float scale = peak / max;
                for (int i = 0; i < samples.Length; i++)
                    samples[i] *= scale;
            }

            return samples;
        }
    }
}
