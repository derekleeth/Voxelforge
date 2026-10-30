using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using Random = System.Random;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Makes every sound effect in the game from code (Roadmap 3.9, D-022), the way <c>build_props.py</c> makes the
    /// models: filtered noise bursts, pitched thuds, wooden knocks, stone rings and bells, all from fixed random seeds,
    /// so building again gives the same files. Writes 16-bit mono WAVs to <c>Art/Audio/Sfx</c>, then the
    /// <see cref="SurfaceSound"/>s and the <see cref="SoundBook"/> that say which block and prop plays what.
    /// Tweak a number below and run <b>Voxelforge &gt; Build Sound Effects</b> again.
    /// </summary>
    public static class SfxBuilder
    {
        const int Rate = 44100;
        const string SfxDir = "Assets/_Voxelforge/Art/Audio/Sfx";
        const string AudioDataDir = "Assets/_Voxelforge/Data/Audio";
        const string SurfaceDir = "Assets/_Voxelforge/Data/Audio/Surfaces";
        const string BlockDir = "Assets/_Voxelforge/Data/Blocks";
        const string DecorationDir = "Assets/_Voxelforge/Data/Decorations";

        /// <summary>What one kind of ground sounds like. Every step, hit, break and place is built from these.</summary>
        sealed class Surface
        {
            public string Name;
            public int Seed;
            /// <summary>The low body of the sound, in Hz.</summary>
            public float Thud;
            public float ThudAmp;
            /// <summary>The noise part: kept between these two frequencies, dying away over <see cref="NoiseDecay"/> s.</summary>
            public float NoiseLow;
            public float NoiseHigh;
            public float NoiseDecay;
            /// <summary>Tiny bursts after the hit (sand and snow crunch, stone crumbles).</summary>
            public int Grains;
            public float GrainSpan;
            /// <summary>Grass and leaves on top.</summary>
            public float Rustle;
            /// <summary>Ringing partials: a hollow knock for wood, a click-ring for stone.</summary>
            public float[] Knock = Array.Empty<float>();
            public float KnockDecay = 0.05f;
            public float KnockAmp;
            public float HitPitchFirst = 0.9f;
            public float HitPitchLast = 1.35f;
        }

        static readonly Surface Stone = new Surface
        {
            Name = "Stone", Seed = 11, Thud = 140f, ThudAmp = 0.5f, NoiseLow = 1500f, NoiseHigh = 6000f, NoiseDecay = 0.02f,
            Grains = 3, GrainSpan = 0.03f, Knock = new[] { 1850f, 2930f, 4100f }, KnockDecay = 0.05f, KnockAmp = 0.15f,
        };

        static readonly Surface Dirt = new Surface
        {
            Name = "Dirt", Seed = 23, Thud = 85f, ThudAmp = 0.7f, NoiseLow = 80f, NoiseHigh = 900f, NoiseDecay = 0.05f,
            Grains = 4, GrainSpan = 0.06f, HitPitchFirst = 0.85f, HitPitchLast = 1.2f,
        };

        static readonly Surface Grass = new Surface
        {
            Name = "Grass", Seed = 37, Thud = 90f, ThudAmp = 0.6f, NoiseLow = 100f, NoiseHigh = 1200f, NoiseDecay = 0.045f,
            Grains = 2, GrainSpan = 0.05f, Rustle = 0.35f, HitPitchFirst = 0.85f, HitPitchLast = 1.2f,
        };

        static readonly Surface Sand = new Surface
        {
            Name = "Sand", Seed = 41, Thud = 70f, ThudAmp = 0.3f, NoiseLow = 400f, NoiseHigh = 3500f, NoiseDecay = 0.06f,
            Grains = 18, GrainSpan = 0.1f, HitPitchFirst = 0.9f, HitPitchLast = 1.25f,
        };

        static readonly Surface Snow = new Surface
        {
            Name = "Snow", Seed = 53, Thud = 75f, ThudAmp = 0.35f, NoiseLow = 700f, NoiseHigh = 3000f, NoiseDecay = 0.03f,
            Grains = 26, GrainSpan = 0.12f, HitPitchFirst = 0.9f, HitPitchLast = 1.25f,
        };

        static readonly Surface Wood = new Surface
        {
            Name = "Wood", Seed = 67, Thud = 170f, ThudAmp = 0.5f, NoiseLow = 300f, NoiseHigh = 2500f, NoiseDecay = 0.015f,
            Grains = 1, GrainSpan = 0.02f, Knock = new[] { 210f, 470f, 890f }, KnockDecay = 0.07f, KnockAmp = 0.45f,
            HitPitchFirst = 0.85f, HitPitchLast = 1.3f,
        };

        [MenuItem("Voxelforge/Build Sound Effects")]
        public static void Build()
        {
            EnsureFolder(SfxDir);
            var written = new List<string>();

            foreach (Surface surface in new[] { Stone, Dirt, Grass, Sand, Snow, Wood })
            {
                for (int i = 0; i < 4; i++)
                    written.Add(Write($"Step_{surface.Name}_{i}", Step(surface, new Random(surface.Seed * 100 + i))));
                for (int i = 0; i < 3; i++)
                    written.Add(Write($"Hit_{surface.Name}_{i}", Hit(surface, new Random(surface.Seed * 200 + i))));
                for (int i = 0; i < 2; i++)
                    written.Add(Write($"Break_{surface.Name}_{i}", Break(surface, new Random(surface.Seed * 300 + i))));
                written.Add(Write($"Place_{surface.Name}_0", Place(surface, new Random(surface.Seed * 400))));
            }

            written.Add(Write("Chime_Crystal_0", CrystalChime(1568f)));
            written.Add(Write("Chime_Crystal_1", CrystalChime(1760f)));
            written.Add(Write("Chord_Crystal_0", CrystalChord()));
            written.Add(Write("Jump", Jump(new Random(71))));
            written.Add(Write("TreeCreak", TreeCreak(new Random(73))));
            written.Add(Write("LeafRustle", LeafRustle(new Random(79))));
            written.Add(Write("PickupPop", PickupPop(new Random(83))));
            written.Add(Write("BoltFire", BoltFire(new Random(89))));
            written.Add(Write("BoltImpact", BoltImpact(new Random(97))));
            written.Add(Write("TorchCrackle", TorchCrackle(new Random(101), 6f)));
            written.Add(Write("CastHum", CastHum()));
            written.Add(Write("OrbRelease", OrbRelease(new Random(103))));
            written.Add(Write("OrbImpact", OrbImpact(new Random(107))));
            written.Add(Write("FireRoar", TorchCrackle(new Random(109), 2f)));
            written.Add(Write("PyroRelease", PyroRelease(new Random(113))));
            written.Add(Write("PyroImpact", PyroImpact(new Random(127))));
            written.Add(Write("EarthSlam", EarthSlam(new Random(131))));
            // Roadmap 4.6: the class spells, totems, earth, shields and the Training Dummy.
            written.Add(Write("HammerThrow", HammerThrow(new Random(137))));
            written.Add(Write("HammerClang", HammerClang(new Random(139))));
            written.Add(Write("ShieldChime", ShieldChime()));
            written.Add(Write("ShieldBlock", ShieldBlock(new Random(149))));
            written.Add(Write("HolyHum", HolyHum()));
            written.Add(Write("ForgeBless", ForgeBless(new Random(151))));
            written.Add(Write("HealPulse", HealPulse()));
            written.Add(Write("SpearLaunch", SpearLaunch(new Random(157))));
            written.Add(Write("SpearImpact", SpearImpact(new Random(163))));
            written.Add(Write("EarthRise", EarthRise(new Random(167))));
            written.Add(Write("EarthCrumble", EarthCrumble(new Random(173))));
            written.Add(Write("TotemPlant", TotemPlant(new Random(179))));
            written.Add(Write("WaterPulse", WaterPulse(new Random(181))));
            written.Add(Write("ThunderZap", ThunderZap(new Random(191))));
            written.Add(Write("HayThwack", HayThwack(new Random(193))));

            AssetDatabase.Refresh();
            foreach (string path in written)
                SetImportSettings(path, looping: path.EndsWith("TorchCrackle.wav", StringComparison.Ordinal) ||
                                                 path.EndsWith("CastHum.wav", StringComparison.Ordinal) ||
                                                 path.EndsWith("FireRoar.wav", StringComparison.Ordinal) ||
                                                 path.EndsWith("HolyHum.wav", StringComparison.Ordinal));

            BuildAssets();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voxelforge] Built {written.Count} sound effects in {SfxDir} and the Sound Book in {AudioDataDir}.");
        }

        // ---------------------------------------------------------------- Sounds

        /// <summary>
        /// A footstep: a dull heel thud, then a softer toe a moment later. Kept low and muffled (nothing above about
        /// 2.5 kHz but the crunch of sand and snow), because a footstep is heard hundreds of times.
        /// </summary>
        static float[] Step(Surface s, Random rng)
        {
            float[] buffer = New(0.3f);
            float low = s.NoiseLow * 0.5f;
            float high = Math.Min(s.NoiseHigh, 2200f);
            float toe = Range(rng, 0.05f, 0.08f);

            AddTone(buffer, 0f, s.Thud * Range(rng, 0.85f, 1f), s.Thud * 0.7f, 0.025f, s.ThudAmp);
            AddNoise(buffer, rng, 0f, 0.025f, 0.5f, low, high);
            AddTone(buffer, toe, s.Thud * Range(rng, 1f, 1.15f), s.Thud * 0.8f, 0.02f, s.ThudAmp * 0.4f);
            AddNoise(buffer, rng, toe, 0.03f, 0.3f, low, high);
            AddGrains(buffer, rng, 0.005f, s.GrainSpan + toe, s.Grains / 2, 0.006f, 0.25f, s.NoiseLow, Math.Min(s.NoiseHigh, 3000f));
            if (s.Rustle > 0f)
                AddNoise(buffer, rng, 0.01f, 0.07f, s.Rustle * 0.5f, 1500f, 4500f);
            // Only the low, hollow knock of wood; the high ring of stone sounds like metal underfoot.
            foreach (float partial in s.Knock)
            {
                if (partial < 1000f)
                    AddTone(buffer, 0f, partial, partial, s.KnockDecay * 0.6f, s.KnockAmp * 0.3f);
            }
            return Finish(buffer, 0.5f);
        }

        static float[] Hit(Surface s, Random rng)
        {
            float[] buffer = New(0.35f);
            AddNoise(buffer, rng, 0f, 0.004f, 0.8f, 3000f, 12000f); // The tool striking.
            AddTone(buffer, 0f, s.Thud * Range(rng, 1f, 1.15f), s.Thud * 0.75f, 0.05f, s.ThudAmp);
            AddNoise(buffer, rng, 0.002f, s.NoiseDecay * 0.8f, 1f, s.NoiseLow * 1.3f, s.NoiseHigh);
            AddGrains(buffer, rng, 0.01f, s.GrainSpan, s.Grains, 0.006f, 0.35f, s.NoiseLow, s.NoiseHigh);
            if (s.Rustle > 0f)
                AddNoise(buffer, rng, 0.01f, 0.06f, s.Rustle * 0.7f, 3500f, 10000f);
            AddPartials(buffer, 0f, s.Knock, Range(rng, 0.95f, 1.05f), s.KnockDecay * 1.5f, s.KnockAmp * 1.5f);
            return Finish(buffer, 0.85f);
        }

        static float[] Break(Surface s, Random rng)
        {
            float[] buffer = New(0.7f);
            AddNoise(buffer, rng, 0f, 0.005f, 0.9f, 2500f, 12000f);
            AddTone(buffer, 0f, s.Thud * 0.9f, s.Thud * 0.55f, 0.08f, s.ThudAmp * 1.2f);
            AddNoise(buffer, rng, 0.003f, 0.15f, 0.7f, s.NoiseLow * 0.5f, s.NoiseHigh * 0.6f); // Crumbling rumble.
            AddGrains(buffer, rng, 0.02f, 0.35f, Math.Max(8, s.Grains * 2), 0.008f, 0.5f, s.NoiseLow, s.NoiseHigh);
            if (s.Rustle > 0f)
                AddNoise(buffer, rng, 0.02f, 0.2f, s.Rustle, 3000f, 10000f);
            AddPartials(buffer, 0f, s.Knock, 0.8f, s.KnockDecay * 2f, s.KnockAmp * 1.2f);
            return Finish(buffer, 0.9f);
        }

        static float[] Place(Surface s, Random rng)
        {
            float[] buffer = New(0.3f);
            AddTone(buffer, 0f, s.Thud * 0.8f, s.Thud * 0.6f, 0.06f, 1f);
            AddNoise(buffer, rng, 0f, 0.03f, 0.5f, s.NoiseLow, Math.Min(s.NoiseHigh, 2500f));
            AddPartials(buffer, 0f, s.Knock, 0.9f, s.KnockDecay, s.KnockAmp);
            return Finish(buffer, 0.75f);
        }

        /// <summary>A small glass bell: the Mana Crystal's ring on every hit.</summary>
        static float[] CrystalChime(float pitch)
        {
            float[] buffer = New(0.9f);
            AddBell(buffer, 0f, pitch, 0.5f, 1f);
            return Finish(buffer, 0.6f);
        }

        /// <summary>A rising major chord of bells when Mana Crystal Ore breaks.</summary>
        static float[] CrystalChord()
        {
            float[] buffer = New(2f);
            float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
            for (int i = 0; i < notes.Length; i++)
                AddBell(buffer, i * 0.045f, notes[i], 1f, 0.8f);
            return Finish(buffer, 0.75f);
        }

        /// <summary>Cloth swishing as the hero jumps, after a small push-off.</summary>
        static float[] Jump(Random rng)
        {
            float[] buffer = New(0.28f);
            AddTone(buffer, 0f, 110f, 80f, 0.02f, 0.4f);
            float low = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float shape = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.25f));
                float cutoff = Mathf.Lerp(400f, 2500f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.25f)));
                low += OnePole(cutoff) * (Noise(rng) - low);
                buffer[i] += low * shape * shape * 0.8f;
            }
            return Finish(buffer, 0.6f);
        }

        /// <summary>A trunk groaning as it tips over, then landing.</summary>
        static float[] TreeCreak(Random rng)
        {
            float[] buffer = New(1.7f);
            float[] clicks = New(1.7f);
            // Stick-slip: a train of clicks whose rate wanders, rung through the wood's resonances.
            const float creakTime = 1.1f;
            float t = 0f;
            while (t < creakTime)
            {
                float rate = 28f + 22f * Mathf.Sin(Mathf.PI * t / creakTime) + Range(rng, -4f, 4f);
                float amp = Mathf.Sin(Mathf.PI * t / creakTime) * Range(rng, 0.6f, 1f);
                clicks[(int)(t * Rate)] += amp;
                t += 1f / rate;
            }
            Add(buffer, Resonate(clicks, 320f, 0.996f), 1f);
            Add(buffer, Resonate(clicks, 760f, 0.993f), 0.5f);
            Add(buffer, Resonate(clicks, 1450f, 0.99f), 0.2f);
            // The crash.
            AddNoise(buffer, rng, 1.15f, 0.25f, 3f, 40f, 700f);
            AddTone(buffer, 1.15f, 60f, 40f, 0.2f, 2f);
            return Finish(buffer, 0.85f);
        }

        /// <summary>The leaves of a falling tree.</summary>
        static float[] LeafRustle(Random rng)
        {
            float[] buffer = New(1.1f);
            float phaseA = Range(rng, 0f, 6.28f), phaseB = Range(rng, 0f, 6.28f);
            float low = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float envelope = Mathf.Clamp01(t / 0.1f) * Mathf.Exp(-t / 0.45f);
                float wobble = 0.6f + 0.25f * Mathf.Sin(t * 23f + phaseA) + 0.15f * Mathf.Sin(t * 41f + phaseB);
                float n = Noise(rng);
                low += OnePole(2500f) * (n - low);
                buffer[i] += (n - low) * envelope * wobble * 0.5f;
            }
            AddGrains(buffer, rng, 0.02f, 0.8f, 40, 0.004f, 0.25f, 3000f, 12000f);
            return Finish(buffer, 0.6f);
        }

        /// <summary>A bright bubble-pop as a drop flies into a bag.</summary>
        static float[] PickupPop(Random rng)
        {
            float[] buffer = New(0.15f);
            AddTone(buffer, 0f, 520f, 1500f, 0.05f, 1f);
            AddTone(buffer, 0f, 1040f, 3000f, 0.03f, 0.3f);
            AddNoise(buffer, rng, 0f, 0.002f, 0.3f, 2000f, 10000f);
            return Finish(buffer, 0.6f);
        }

        /// <summary>The M.G.R.S. firing: a rising magic zap with a sparkle on top and a kick underneath.</summary>
        static float[] BoltFire(Random rng)
        {
            float[] buffer = New(0.55f);
            AddTone(buffer, 0f, 220f, 1760f, 0.12f, 0.6f, sweepTime: 0.15f);
            AddTone(buffer, 0f, 330f, 2640f, 0.1f, 0.3f, sweepTime: 0.15f);
            AddTone(buffer, 0f, 90f, 50f, 0.06f, 0.5f);
            AddNoise(buffer, rng, 0f, 0.05f, 0.4f, 3000f, 14000f);
            foreach (float note in new[] { 2093f, 2637f, 3136f })
                AddTone(buffer, 0.03f, note, note, 0.25f, 0.12f);
            return Finish(buffer, 0.8f);
        }

        /// <summary>A Mana Bolt landing: a burst and a boom, then sparks and a shimmering tail.</summary>
        static float[] BoltImpact(Random rng)
        {
            float[] buffer = New(0.95f);
            AddNoise(buffer, rng, 0f, 0.12f, 0.9f, 60f, 3500f);
            AddTone(buffer, 0f, 80f, 45f, 0.2f, 0.8f);
            for (int i = 0; i < 10; i++)
                AddTone(buffer, Range(rng, 0f, 0.4f), Range(rng, 2000f, 5000f), Range(rng, 1500f, 4000f),
                    Range(rng, 0.05f, 0.15f), Range(rng, 0.1f, 0.2f));
            foreach (float note in new[] { 1568f, 1976f, 2349f })
                AddTone(buffer, 0.02f, note, note, 0.35f, 0.12f);
            return Finish(buffer, 0.9f);
        }

        /// <summary>
        /// Gathering mana while casting (Roadmap 4.2): a low, breathing hum with a faint shimmer on top. Every partial
        /// and every swell fits a whole number of times into the two seconds, so it loops without a seam.
        /// </summary>
        static float[] CastHum()
        {
            const float Seconds = 2f;
            float[] buffer = New(Seconds);
            (float frequency, float amp, float swell)[] partials =
            {
                (110f, 0.5f, 1f), (165f, 0.3f, 1.5f), (220f, 0.25f, 2f), (330f, 0.12f, 1f), (1320f, 0.05f, 3f), (1980f, 0.03f, 2.5f),
            };
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float sample = 0f;
                foreach ((float frequency, float amp, float swell) in partials)
                {
                    float breathe = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * swell * t);
                    sample += Mathf.Sin(2f * Mathf.PI * frequency * t) * amp * breathe;
                }
                buffer[i] = sample;
            }
            Normalize(buffer, 0.55f);
            return buffer;
        }

        /// <summary>The Arcane Orb leaving the hands: a deep whump under a rising swell, and a bright ring.</summary>
        static float[] OrbRelease(Random rng)
        {
            float[] buffer = New(0.9f);
            AddTone(buffer, 0f, 70f, 40f, 0.18f, 0.9f);
            AddTone(buffer, 0f, 180f, 900f, 0.16f, 0.5f, sweepTime: 0.25f);
            AddTone(buffer, 0f, 270f, 1350f, 0.14f, 0.25f, sweepTime: 0.25f);
            AddNoise(buffer, rng, 0f, 0.12f, 0.45f, 200f, 4000f);
            AddBell(buffer, 0.05f, 880f, 0.35f, 0.15f);
            return Finish(buffer, 0.8f);
        }

        /// <summary>The Arcane Orb landing: a heavy boom, crumbling grit, and a ringing chord that fades.</summary>
        static float[] OrbImpact(Random rng)
        {
            float[] buffer = New(1.4f);
            AddNoise(buffer, rng, 0f, 0.22f, 1f, 40f, 2500f);
            AddTone(buffer, 0f, 60f, 32f, 0.3f, 1f);
            AddGrains(buffer, rng, 0.05f, 0.5f, 18, 0.03f, 0.25f, 400f, 5000f);
            foreach (float note in new[] { 1046f, 1318f, 1568f })
                AddTone(buffer, 0.03f, note, note, 0.45f, 0.1f);
            return Finish(buffer, 0.95f);
        }

        /// <summary>Pyroblast leaving the hands (Roadmap 4.3): a rushing whoosh of flame over a deep thump.</summary>
        static float[] PyroRelease(Random rng)
        {
            float[] buffer = New(1.1f);
            AddTone(buffer, 0f, 60f, 35f, 0.2f, 0.9f);
            AddNoise(buffer, rng, 0f, 0.3f, 0.8f, 150f, 2500f);
            AddNoise(buffer, rng, 0.02f, 0.15f, 0.35f, 2000f, 7000f);
            AddTone(buffer, 0f, 140f, 420f, 0.2f, 0.3f, sweepTime: 0.35f);
            AddGrains(buffer, rng, 0.05f, 0.6f, 14, 0.02f, 0.15f, 1500f, 6000f);
            return Finish(buffer, 0.85f);
        }

        /// <summary>
        /// Pyroblast landing (Roadmap 4.3): a huge boom with a long rolling rumble, rock thrown and pattering back
        /// down, and the crackle of fire.
        /// </summary>
        static float[] PyroImpact(Random rng)
        {
            float[] buffer = New(2.4f);
            AddNoise(buffer, rng, 0f, 0.45f, 1f, 30f, 1800f);
            AddTone(buffer, 0f, 55f, 25f, 0.6f, 1f);
            AddTone(buffer, 0.01f, 90f, 40f, 0.25f, 0.6f);
            AddNoise(buffer, rng, 0f, 0.06f, 0.6f, 1000f, 8000f);
            AddGrains(buffer, rng, 0.1f, 0.6f, 30, 0.04f, 0.3f, 300f, 4000f);
            AddGrains(buffer, rng, 0.6f, 1.2f, 40, 0.02f, 0.15f, 800f, 6000f);
            AddGrains(buffer, rng, 0.2f, 1.8f, 50, 0.015f, 0.08f, 2000f, 9000f);
            return Finish(buffer, 1f);
        }

        /// <summary>
        /// EarthShatter (Roadmap 4.3): a deep ground-shaking slam, the crack of splitting stone, then rubble tumbling.
        /// </summary>
        static float[] EarthSlam(Random rng)
        {
            float[] buffer = New(2f);
            AddTone(buffer, 0f, 48f, 28f, 0.5f, 1f);
            AddNoise(buffer, rng, 0f, 0.3f, 0.9f, 25f, 900f);
            float[] crack = New(2f);
            AddNoise(crack, rng, 0.015f, 0.05f, 1f, 400f, 6000f);
            Add(buffer, Resonate(crack, 700f, 0.995f), 0.5f);
            Add(buffer, crack, 0.5f);
            AddGrains(buffer, rng, 0.08f, 1.2f, 60, 0.04f, 0.3f, 150f, 3000f);
            return Finish(buffer, 1f);
        }

        // ---- Roadmap 4.6 ----

        /// <summary>The Rune Hammer leaving the hand: a heavy whoosh of air with a bright ring of rune-metal in it.</summary>
        static float[] HammerThrow(Random rng)
        {
            float[] buffer = New(0.7f);
            AddNoise(buffer, rng, 0f, 0.18f, 0.7f, 250f, 3000f);
            AddTone(buffer, 0f, 300f, 160f, 0.2f, 0.25f);
            AddBell(buffer, 0.02f, 1175f, 0.35f, 0.2f);
            AddTone(buffer, 0f, 80f, 55f, 0.08f, 0.5f);
            return Finish(buffer, 0.75f);
        }

        /// <summary>The Rune Hammer landing: a big metal clang over a thud, and a golden ring fading.</summary>
        static float[] HammerClang(Random rng)
        {
            float[] buffer = New(1.2f);
            AddTone(buffer, 0f, 70f, 45f, 0.12f, 0.9f);
            AddNoise(buffer, rng, 0f, 0.03f, 0.8f, 800f, 8000f);
            AddBell(buffer, 0f, 520f, 0.5f, 0.55f);
            AddBell(buffer, 0.005f, 784f, 0.4f, 0.3f);
            foreach (float note in new[] { 1568f, 1976f })
                AddTone(buffer, 0.02f, note, note, 0.4f, 0.08f);
            return Finish(buffer, 0.9f);
        }

        /// <summary>Bulwark Rune going on: a rising major arpeggio of bells, warm and bright.</summary>
        static float[] ShieldChime()
        {
            float[] buffer = New(1.3f);
            float[] notes = { 659f, 784f, 988f, 1319f };
            for (int i = 0; i < notes.Length; i++)
                AddBell(buffer, i * 0.06f, notes[i], 0.6f, 0.35f);
            AddTone(buffer, 0f, 330f, 330f, 0.5f, 0.2f);
            return Finish(buffer, 0.7f);
        }

        /// <summary>A shield soaking up a hit: a short, high metal clink.</summary>
        static float[] ShieldBlock(Random rng)
        {
            float[] buffer = New(0.4f);
            AddBell(buffer, 0f, 1480f, 0.12f, 0.6f);
            AddNoise(buffer, rng, 0f, 0.01f, 0.5f, 2000f, 9000f);
            return Finish(buffer, 0.6f);
        }

        /// <summary>
        /// Hallowed Forge's cast (a loop): a warm major chord breathing slowly, like a choir held under a forge's roar.
        /// Every partial and swell fits whole times into two seconds, so it loops without a seam.
        /// </summary>
        static float[] HolyHum()
        {
            const float Seconds = 2f;
            float[] buffer = New(Seconds);
            (float frequency, float amp, float swell)[] partials =
            {
                (130.5f, 0.45f, 1f), (164.5f, 0.3f, 1.5f), (196f, 0.3f, 1f), (261f, 0.2f, 2f), (329f, 0.1f, 2.5f), (784f, 0.04f, 3f),
            };
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float sample = 0f;
                foreach ((float frequency, float amp, float swell) in partials)
                    sample += Mathf.Sin(2f * Mathf.PI * frequency * t) * amp * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * swell * t));
                buffer[i] = sample;
            }
            Normalize(buffer, 0.5f);
            return buffer;
        }

        /// <summary>Hallowed Forge blessing the ground: an anvil strike, then a swelling bright chord.</summary>
        static float[] ForgeBless(Random rng)
        {
            float[] buffer = New(1.6f);
            AddBell(buffer, 0f, 880f, 0.4f, 0.5f);
            AddNoise(buffer, rng, 0f, 0.02f, 0.5f, 1500f, 8000f);
            foreach (float note in new[] { 523f, 659f, 784f, 1047f })
                AddTone(buffer, 0.08f, note, note, 0.55f, 0.15f);
            AddNoise(buffer, rng, 0.05f, 0.4f, 0.25f, 300f, 2500f);
            return Finish(buffer, 0.8f);
        }

        /// <summary>A healing pulse: a soft, quick sparkle that won't tire the ear once a second.</summary>
        static float[] HealPulse()
        {
            float[] buffer = New(0.6f);
            foreach ((float start, float note) in new[] { (0f, 1319f), (0.04f, 1568f), (0.08f, 2093f) })
                AddTone(buffer, start, note, note, 0.15f, 0.25f);
            return Finish(buffer, 0.4f);
        }

        /// <summary>Stone Spear leaving the hands: grinding rock torn free, then a heavy rushing whoosh.</summary>
        static float[] SpearLaunch(Random rng)
        {
            float[] buffer = New(0.8f);
            AddGrains(buffer, rng, 0f, 0.15f, 14, 0.02f, 0.4f, 200f, 2500f);
            AddNoise(buffer, rng, 0.05f, 0.2f, 0.7f, 120f, 1800f);
            AddTone(buffer, 0f, 110f, 60f, 0.15f, 0.6f);
            return Finish(buffer, 0.8f);
        }

        /// <summary>Stone Spear hitting: a sharp crack of stone and a thud, then a little rubble.</summary>
        static float[] SpearImpact(Random rng)
        {
            float[] buffer = New(1f);
            AddTone(buffer, 0f, 65f, 40f, 0.15f, 0.9f);
            float[] crack = New(1f);
            AddNoise(crack, rng, 0f, 0.03f, 1f, 600f, 7000f);
            Add(buffer, Resonate(crack, 900f, 0.99f), 0.4f);
            Add(buffer, crack, 0.6f);
            AddGrains(buffer, rng, 0.05f, 0.5f, 20, 0.03f, 0.25f, 300f, 4000f);
            return Finish(buffer, 0.9f);
        }

        /// <summary>Earth rising (Rising Pillar, Earthen Wall): a deep grinding rumble that climbs, with stone scraping.</summary>
        static float[] EarthRise(Random rng)
        {
            float[] buffer = New(1.2f);
            AddTone(buffer, 0f, 40f, 75f, 0.5f, 0.9f, sweepTime: 0.6f);
            AddNoise(buffer, rng, 0f, 0.4f, 0.8f, 30f, 700f);
            AddGrains(buffer, rng, 0f, 0.9f, 50, 0.03f, 0.3f, 200f, 3000f);
            return Finish(buffer, 0.95f);
        }

        /// <summary>Earthen Rock crumbling away: dry dust and pebbles pattering down, no thud.</summary>
        static float[] EarthCrumble(Random rng)
        {
            float[] buffer = New(1.1f);
            AddNoise(buffer, rng, 0f, 0.25f, 0.4f, 300f, 4000f);
            AddGrains(buffer, rng, 0f, 0.8f, 45, 0.02f, 0.35f, 600f, 6000f);
            AddGrains(buffer, rng, 0.1f, 0.6f, 15, 0.04f, 0.3f, 150f, 1500f);
            return Finish(buffer, 0.6f);
        }

        /// <summary>A totem planted: a heavy wooden thunk driven into the ground, and a puff of earth.</summary>
        static float[] TotemPlant(Random rng)
        {
            float[] buffer = New(0.9f);
            AddTone(buffer, 0f, 95f, 55f, 0.12f, 1f);
            AddTone(buffer, 0f, 210f, 140f, 0.06f, 0.4f);
            float[] knock = New(0.9f);
            AddNoise(knock, rng, 0f, 0.02f, 1f, 200f, 2500f);
            Add(buffer, Resonate(knock, 320f, 0.985f), 0.5f);
            AddGrains(buffer, rng, 0.03f, 0.3f, 12, 0.03f, 0.2f, 300f, 3000f);
            return Finish(buffer, 0.85f);
        }

        /// <summary>The Wellspring's pulse: a soft water bloop with a little trickle after it.</summary>
        static float[] WaterPulse(Random rng)
        {
            float[] buffer = New(0.7f);
            AddTone(buffer, 0f, 420f, 900f, 0.06f, 0.6f, sweepTime: 0.05f);
            AddTone(buffer, 0.12f, 520f, 1100f, 0.05f, 0.35f, sweepTime: 0.04f);
            for (int i = 0; i < 6; i++)
            {
                float f = Range(rng, 900f, 1700f);
                AddTone(buffer, 0.15f + Range(rng, 0f, 0.35f), f, f * 1.6f, 0.03f, Range(rng, 0.08f, 0.18f), sweepTime: 0.02f);
            }
            return Finish(buffer, 0.45f);
        }

        /// <summary>The Thunderhead's strike: a sharp electric crack and a short rumble of thunder.</summary>
        static float[] ThunderZap(Random rng)
        {
            float[] buffer = New(1.4f);
            AddNoise(buffer, rng, 0f, 0.015f, 1f, 1500f, 12000f);
            for (int i = 0; i < 5; i++)
                AddNoise(buffer, rng, Range(rng, 0f, 0.08f), 0.01f, 0.6f, 2000f, 10000f);
            AddTone(buffer, 0f, 1200f, 300f, 0.05f, 0.3f, sweepTime: 0.05f);
            AddNoise(buffer, rng, 0.04f, 0.35f, 0.7f, 30f, 500f);
            AddTone(buffer, 0.04f, 55f, 35f, 0.3f, 0.6f);
            return Finish(buffer, 0.9f);
        }

        /// <summary>A Training Dummy's hay sack landing on a hero: a soft, scratchy thump.</summary>
        static float[] HayThwack(Random rng)
        {
            float[] buffer = New(0.5f);
            AddTone(buffer, 0f, 110f, 70f, 0.06f, 0.7f);
            AddNoise(buffer, rng, 0f, 0.05f, 0.6f, 400f, 5000f);
            AddGrains(buffer, rng, 0.02f, 0.2f, 16, 0.015f, 0.3f, 1500f, 7000f);
            return Finish(buffer, 0.7f);
        }

        /// <summary>
        /// A torch: mostly the soft breathing roar of the flame, with a faint hiss on top and a few quiet crackles,
        /// and now and then a woody snap. Loops without a seam.
        /// </summary>
        static float[] TorchCrackle(Random rng, float seconds)
        {
            int length = (int)(seconds * Rate);
            int overlap = Rate / 2;
            var roar = new float[length + overlap];
            float low1 = 0f, low2 = 0f, hissLow = 0f, hissHigh = 0f;
            float breathA = Range(rng, 0f, 6.28f), breathB = Range(rng, 0f, 6.28f);
            for (int i = 0; i < roar.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(rng);
                low1 += OnePole(450f) * (n - low1);
                low2 += OnePole(450f) * (low1 - low2);
                // The flame breathes: two slow, unrelated swells.
                float breath = 0.75f + 0.15f * Mathf.Sin(t * 2.1f + breathA) + 0.1f * Mathf.Sin(t * 5.3f + breathB);
                hissLow += OnePole(3000f) * (n - hissLow);
                hissHigh += OnePole(1200f) * (hissLow - hissHigh);
                roar[i] = low2 * breath * 3f + (hissLow - hissHigh) * breath * 0.08f;
            }
            // Fold the extra tail over the start, so the end runs straight into the beginning.
            var buffer = new float[length];
            for (int i = 0; i < length; i++)
            {
                float fade = i < overlap ? i / (float)overlap : 1f;
                buffer[i] = roar[i] * fade + (i < overlap ? roar[length + i] * (1f - fade) : 0f);
            }

            // About three soft crackles a second, mostly quiet (cubed random), in the middle of the range.
            int crackles = (int)(seconds * 3f);
            for (int i = 0; i < crackles; i++)
            {
                float r = (float)rng.NextDouble();
                AddCrackle(buffer, rng, rng.Next(length), Range(rng, 0.003f, 0.008f), 0.04f + 0.25f * r * r * r, 700f, 3500f);
            }
            // Now and then a woody snap: a little louder, lower, and in a quick cluster.
            int snaps = Math.Max(1, (int)(seconds / 2.5f));
            for (int i = 0; i < snaps; i++)
            {
                int start = rng.Next(length);
                int count = rng.Next(2, 5);
                for (int k = 0; k < count; k++)
                    AddCrackle(buffer, rng, start + (int)(Range(rng, 0f, 0.06f) * Rate), Range(rng, 0.006f, 0.015f),
                        Range(rng, 0.2f, 0.4f), 300f, 2000f);
            }
            Normalize(buffer, 0.6f);
            return buffer;
        }

        /// <summary>One crackle, wrapped round the end of a loop so crackles can cross the loop point.</summary>
        static void AddCrackle(float[] loop, Random rng, int start, float decay, float amp, float low, float high)
        {
            var grain = new float[(int)(decay * 7f * Rate)];
            AddNoise(grain, rng, 0f, decay, amp, low, high);
            for (int j = 0; j < grain.Length; j++)
                loop[(start + j) % loop.Length] += grain[j];
        }

        // ---------------------------------------------------------------- Building blocks

        static float[] New(float seconds) => new float[(int)(seconds * Rate)];

        static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static float Noise(Random rng) => (float)rng.NextDouble() * 2f - 1f;

        /// <summary>The coefficient of a one-pole low-pass filter at <paramref name="cutoff"/> Hz.</summary>
        static float OnePole(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);

        /// <summary>A short attack, then an exponential fall over <paramref name="decay"/> seconds.</summary>
        static float Envelope(float t, float decay, float attack = 0.002f) =>
            Mathf.Clamp01(t / attack) * Mathf.Exp(-t / decay);

        /// <summary>
        /// White noise kept between <paramref name="low"/> and <paramref name="high"/> Hz, dying away. Two low-pass
        /// stages, because one lets through enough hiss to sound harsh.
        /// </summary>
        static void AddNoise(float[] buffer, Random rng, float start, float decay, float amp, float low, float high)
        {
            int from = (int)(start * Rate);
            int count = Math.Min(buffer.Length - from, (int)(decay * 7f * Rate));
            float firstPass = 0f, lowPass = 0f, highPassLow = 0f;
            float a = OnePole(high), b = OnePole(low);
            for (int i = 0; i < count; i++)
            {
                firstPass += a * (Noise(rng) - firstPass);
                lowPass += a * (firstPass - lowPass);
                highPassLow += b * (lowPass - highPassLow);
                buffer[from + i] += (lowPass - highPassLow) * Envelope(i / (float)Rate, decay) * amp * 2f;
            }
        }

        /// <summary>A sine gliding from <paramref name="frequency"/> to <paramref name="endFrequency"/>, dying away.</summary>
        static void AddTone(float[] buffer, float start, float frequency, float endFrequency, float decay, float amp,
            float sweepTime = 0f)
        {
            int from = (int)(start * Rate);
            int count = Math.Min(buffer.Length - from, (int)(decay * 7f * Rate));
            float glide = sweepTime > 0f ? sweepTime : decay * 3f;
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float f = frequency * Mathf.Pow(endFrequency / frequency, Mathf.Clamp01(t / glide));
                phase += 2.0 * Math.PI * f / Rate;
                buffer[from + i] += (float)Math.Sin(phase) * Envelope(t, decay) * amp;
            }
        }

        static void AddPartials(float[] buffer, float start, float[] partials, float pitch, float decay, float amp)
        {
            for (int i = 0; i < partials.Length; i++)
                AddTone(buffer, start, partials[i] * pitch, partials[i] * pitch, decay / (1f + i * 0.5f), amp / (1f + i));
        }

        /// <summary>A bell: the inharmonic partials of a struck bar, the high ones fading first.</summary>
        static void AddBell(float[] buffer, float start, float frequency, float decay, float amp)
        {
            float[] ratios = { 1f, 2.756f, 5.404f, 8.933f };
            float[] amps = { 1f, 0.45f, 0.2f, 0.08f };
            for (int i = 0; i < ratios.Length; i++)
                AddTone(buffer, start, frequency * ratios[i], frequency * ratios[i], decay / (1f + i * 0.7f), amp * amps[i]);
        }

        /// <summary>Many tiny noise bursts scattered over <paramref name="span"/> seconds: crunching, crumbling.</summary>
        static void AddGrains(float[] buffer, Random rng, float start, float span, int count, float decay, float amp,
            float low, float high)
        {
            for (int i = 0; i < count; i++)
                AddNoise(buffer, rng, start + Range(rng, 0f, span), decay * Range(rng, 0.5f, 1.5f), amp * Range(rng, 0.3f, 1f), low, high);
        }

        /// <summary>Rings <paramref name="input"/> through a two-pole resonator at <paramref name="frequency"/> Hz.</summary>
        static float[] Resonate(float[] input, float frequency, float radius)
        {
            var output = new float[input.Length];
            float c = 2f * radius * Mathf.Cos(2f * Mathf.PI * frequency / Rate), r2 = radius * radius;
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < input.Length; i++)
            {
                float y = input[i] * (1f - radius) + c * y1 - r2 * y2;
                output[i] = y;
                y2 = y1;
                y1 = y;
            }
            return output;
        }

        static void Add(float[] buffer, float[] other, float amp)
        {
            for (int i = 0; i < buffer.Length && i < other.Length; i++)
                buffer[i] += other[i] * amp;
        }

        static void Normalize(float[] buffer, float peak)
        {
            float max = 0f;
            foreach (float sample in buffer)
                max = Mathf.Max(max, Mathf.Abs(sample));
            if (max <= 0f)
                return;
            float scale = peak / max;
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] *= scale;
        }

        /// <summary>Normalizes and fades the last few milliseconds, so nothing clicks at the end.</summary>
        static float[] Finish(float[] buffer, float peak)
        {
            Normalize(buffer, peak);
            int fade = Math.Min(buffer.Length, Rate / 100);
            for (int i = 0; i < fade; i++)
                buffer[buffer.Length - 1 - i] *= i / (float)fade;
            return buffer;
        }

        // ---------------------------------------------------------------- Files and assets

        static string Write(string name, float[] samples)
        {
            string path = $"{SfxDir}/{name}.wav";
            using (var stream = new FileStream(path, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int bytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // Mono
                writer.Write(Rate);
                writer.Write(Rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(bytes);
                foreach (float sample in samples)
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
            return path;
        }

        static void SetImportSettings(string path, bool looping)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
                return;
            importer.forceToMono = true;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = looping ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = looping ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        static void BuildAssets()
        {
            var surfaces = new Dictionary<string, SurfaceSound>();
            foreach (Surface surface in new[] { Stone, Dirt, Grass, Sand, Snow, Wood })
                surfaces[surface.Name] = WriteSurface(surface.Name, surface, surface.Name, null, null);
            surfaces["Crystal"] = WriteSurface("Crystal", Stone, "Stone",
                new[] { Clip("Chime_Crystal_0"), Clip("Chime_Crystal_1") }, new[] { Clip("Chord_Crystal_0") });

            var book = LoadOrCreate<SoundBook>($"{AudioDataDir}/SoundBook.asset");
            var serialized = new SerializedObject(book);
            serialized.FindProperty("defaultSurface").objectReferenceValue = surfaces["Grass"];

            var blocks = new (string block, string surface)[]
            {
                ("Grass", "Grass"), ("Dirt", "Dirt"), ("Stone", "Stone"), ("ManaCrystalOre", "Crystal"), ("Sand", "Sand"),
                ("Snow", "Snow"), ("Sandstone", "Stone"), ("RedRock", "Stone"), ("TreeTrunk", "Wood"), ("Log", "Wood"),
                ("Torch", "Wood"), ("Workbench", "Wood"), ("HomeTotem", "Wood"), ("Chest", "Wood"), ("TrainingDummy", "Wood"),
                ("EarthenRock", "Stone"),
            };
            SerializedProperty list = serialized.FindProperty("blocks");
            list.arraySize = 0;
            foreach ((string blockName, string surfaceName) in blocks)
            {
                var block = AssetDatabase.LoadAssetAtPath<BlockDefinition>($"{BlockDir}/{blockName}.asset");
                if (block == null)
                {
                    Debug.LogWarning($"[Voxelforge] No block asset {blockName}; it gets the default sounds.");
                    continue;
                }
                list.arraySize++;
                SerializedProperty entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("block").objectReferenceValue = block;
                entry.FindPropertyRelative("surface").objectReferenceValue = surfaces[surfaceName];
            }

            serialized.FindProperty("jump").objectReferenceValue = Clip("Jump");
            serialized.FindProperty("pickupPop").objectReferenceValue = Clip("PickupPop");
            serialized.FindProperty("treeCreak").objectReferenceValue = Clip("TreeCreak");
            serialized.FindProperty("leafRustle").objectReferenceValue = Clip("LeafRustle");
            serialized.FindProperty("earthCrumble").objectReferenceValue = Clip("EarthCrumble");
            serialized.FindProperty("shieldBlock").objectReferenceValue = Clip("ShieldBlock");
            serialized.FindProperty("hayThwack").objectReferenceValue = Clip("HayThwack");

            SerializedProperty loops = serialized.FindProperty("loops");
            loops.arraySize = 0;
            foreach (string torch in new[] { "TorchFloor", "TorchWall" })
            {
                var decoration = AssetDatabase.LoadAssetAtPath<DecorationDefinition>($"{DecorationDir}/{torch}.asset");
                if (decoration == null)
                    continue;
                loops.arraySize++;
                SerializedProperty entry = loops.GetArrayElementAtIndex(loops.arraySize - 1);
                entry.FindPropertyRelative("decoration").objectReferenceValue = decoration;
                entry.FindPropertyRelative("clip").objectReferenceValue = Clip("TorchCrackle");
                entry.FindPropertyRelative("volume").floatValue = 0.35f;
                entry.FindPropertyRelative("maxDistance").floatValue = 14f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
        }

        static SurfaceSound WriteSurface(string assetName, Surface pitches, string clips, AudioClip[] hitExtras, AudioClip[] breakExtras)
        {
            var asset = LoadOrCreate<SurfaceSound>($"{SurfaceDir}/{assetName}.asset");
            var serialized = new SerializedObject(asset);
            SetClips(serialized, "steps", Clips($"Step_{clips}", 4));
            SetClips(serialized, "hits", Clips($"Hit_{clips}", 3));
            SetClips(serialized, "breaks", Clips($"Break_{clips}", 2));
            SetClips(serialized, "places", Clips($"Place_{clips}", 1));
            SetClips(serialized, "hitExtras", hitExtras ?? Array.Empty<AudioClip>());
            SetClips(serialized, "breakExtras", breakExtras ?? Array.Empty<AudioClip>());
            serialized.FindProperty("hitPitchFirst").floatValue = pitches.HitPitchFirst;
            serialized.FindProperty("hitPitchLast").floatValue = pitches.HitPitchLast;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav");

        static AudioClip[] Clips(string prefix, int count)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++)
                clips[i] = Clip($"{prefix}_{i}");
            return clips;
        }

        static void SetClips(SerializedObject owner, string field, AudioClip[] clips)
        {
            SerializedProperty list = owner.FindProperty(field);
            list.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
