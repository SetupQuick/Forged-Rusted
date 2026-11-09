using System.Collections.Generic;
using UnityEngine;
using World.Blocks;

namespace GameAudio
{
    /// <summary>
    /// Creates the generated sound clips once and plays them. Never runs
    /// on a dedicated server (nothing calls it there).
    /// </summary>
    public static class GameSounds
    {
        private const int Variants = 4;

        private static readonly Dictionary<string, AudioClip[]> Clips = new Dictionary<string, AudioClip[]>();

        /// <summary>Master volume for generated effects (0..1).</summary>
        public static float Volume = 0.8f;

        public static AudioClip Footstep(BlockId surface) =>
            Get("step", SoundSynth.MaterialOf(surface), SoundSynth.Footstep);

        public static AudioClip Break(BlockId block) =>
            Get("break", SoundSynth.MaterialOf(block), SoundSynth.Break);

        public static AudioClip Place(BlockId block) =>
            Get("place", SoundSynth.MaterialOf(block), SoundSynth.Place);

        public static AudioClip Splash() =>
            Get("splash", SoundMaterial.Water, (m, v) => SoundSynth.Splash(v));

        public static AudioClip SwimStroke() =>
            Get("swim", SoundMaterial.Water, (m, v) => SoundSynth.Swim(v));

        /// <summary>One-shot 3D sound at a position (block break/place).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null)
                return;

            var go = new GameObject("Sound " + clip.name);
            go.transform.position = position;

            var source = go.AddComponent<AudioSource>();
            Configure3D(source);
            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(clip, volume * Volume);

            Object.Destroy(go, clip.length + 0.1f);
        }

        /// <summary>Settings for positional sounds heard within ~30 m.</summary>
        public static void Configure3D(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 32f;
            source.dopplerLevel = 0f;
        }

        private static AudioClip Get(string kind, SoundMaterial material, System.Func<SoundMaterial, int, float[]> generate)
        {
            string key = kind + ":" + material;

            if (!Clips.TryGetValue(key, out AudioClip[] clips))
            {
                clips = new AudioClip[Variants];

                for (int i = 0; i < Variants; i++)
                {
                    float[] samples = generate(material, i);
                    var clip = AudioClip.Create($"{kind}_{material}_{i}", samples.Length, 1, SoundSynth.SampleRate, false);
                    clip.SetData(samples, 0);
                    clips[i] = clip;
                }

                Clips[key] = clips;
            }

            return clips[Random.Range(0, clips.Length)];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clips.Clear();
        }
    }
}
