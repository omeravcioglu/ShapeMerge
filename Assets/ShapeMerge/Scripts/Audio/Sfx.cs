using UnityEngine;

namespace ShapeMerge
{
    /// <summary>
    /// Procedurally synthesised sound effects (no audio assets needed) played through a small
    /// voice pool. Merges pop lower as shapes grow; combos climb a pentatonic scale.
    /// </summary>
    public sealed class Sfx
    {
        const int Rate = 44100;
        static readonly int[] Pentatonic = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26, 28, 31 };

        readonly AudioSource[] voices = new AudioSource[24];
        readonly AudioSource chargeVoice;
        readonly AudioClip pop, chime, thud, shoot, load, tick, gameOver, boom, charge, slurp;
        readonly System.Random rng = new System.Random(7);
        int nextVoice;

        public float Volume = 0.8f;

        public Sfx(Transform parent)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent, false);
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                voices[i] = s;
            }
            chargeVoice = go.AddComponent<AudioSource>();
            chargeVoice.playOnAwake = false;
            chargeVoice.loop = true;
            chargeVoice.volume = 0f;

            pop = Build("pop", 0.3f, Pop);
            chime = Build("chime", 1.1f, Chime);
            thud = Build("thud", 0.25f, Thud);
            shoot = Build("shoot", 0.35f, Shoot);
            load = Build("load", 0.14f, Load);
            tick = Build("tick", 0.09f, Tick);
            gameOver = Build("gameover", 1.6f, GameOverJingle);
            boom = Build("boom", 1.8f, Boom);
            charge = Build("charge", 1f, ChargeHum, fadeTail: false);
            slurp = Build("slurp", 0.12f, Slurp);
            chargeVoice.clip = charge;
        }

        // ---------------------------------------------------------------- playback

        void Play(AudioClip clip, float volume, float pitch)
        {
            AudioSource v = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            v.clip = clip;
            v.volume = Mathf.Clamp01(volume * Volume);
            v.pitch = pitch;
            v.Play();
        }

        float Jitter(float amount) => 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;

        static float Semitones(float s) => Mathf.Pow(2f, s / 12f);

        public void Merge(int tier, int combo)
        {
            // Bigger shapes pop deeper; the chime climbs with the combo.
            Play(pop, 0.9f, Semitones(9f - tier * 2.2f) * Jitter(0.03f));
            int step = Pentatonic[Mathf.Clamp(combo - 1, 0, Pentatonic.Length - 1)];
            Play(chime, 0.32f + 0.04f * Mathf.Min(combo, 6), Semitones(step - tier * 0.5f));
        }

        public void MergeStart(int tier) => Play(slurp, 0.35f, Semitones(5f - tier) * Jitter(0.05f));

        public void Impact(float strength, int tier) =>
            Play(thud, Mathf.Lerp(0.15f, 0.7f, strength), Semitones(5f - tier * 1.6f) * Jitter(0.08f));

        public void Shoot(float power) => Play(shoot, 0.45f + 0.4f * power, Mathf.Lerp(1.15f, 0.8f, power) * Jitter(0.04f));

        public void Load() => Play(load, 0.35f, Jitter(0.05f));

        public void DangerTick(float urgency) => Play(tick, 0.25f + 0.25f * urgency, 1f + 0.5f * urgency);

        public void PopShape(int tier) => Play(pop, 0.6f, Semitones(4f - tier * 1.5f) * Jitter(0.08f));

        public void GameOver() => Play(gameOver, 0.7f, 1f);

        public void Supernova()
        {
            Play(boom, 1f, 1f);
            Play(chime, 0.6f, Semitones(0f));
            Play(chime, 0.5f, Semitones(7f));
            Play(chime, 0.45f, Semitones(12f));
        }

        /// <summary>Drives the looping charge hum; pass a negative charge when idle.</summary>
        public void Tick(float dt, float chargeLevel)
        {
            bool charging = chargeLevel >= 0f;
            if (charging && !chargeVoice.isPlaying) chargeVoice.Play();
            float target = charging ? (0.12f + 0.2f * chargeLevel) * Volume : 0f;
            chargeVoice.volume = Mathf.MoveTowards(chargeVoice.volume, target, dt * 3f);
            if (charging) chargeVoice.pitch = 0.75f + 0.75f * chargeLevel;
            if (!charging && chargeVoice.volume <= 0.001f && chargeVoice.isPlaying) chargeVoice.Stop();
        }

        // ---------------------------------------------------------------- synthesis

        delegate void Synth(float[] data);

        static AudioClip Build(string name, float seconds, Synth synth, bool fadeTail = true)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            var data = new float[n];
            synth(data);
            if (fadeTail)
            {
                // Short fade at the tail to avoid clicks.
                int fade = Mathf.Min(n, 256);
                for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
            }
            AudioClip clip = AudioClip.Create("ShapeMerge " + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sweep(float t, float from, float to, float tau) =>
            2f * Mathf.PI * (to * t + (from - to) * tau * (1f - Mathf.Exp(-t / tau)));

        // A bubbly "bloop": a sine diving in pitch with a soft click on the front.
        static void Pop(float[] d)
        {
            var noise = new System.Random(1);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = Sweep(t, 980f, 330f, 0.022f);
                float env = (1f - Mathf.Exp(-t / 0.0015f)) * Mathf.Exp(-t / 0.075f);
                float click = ((float)noise.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.0025f) * 0.25f;
                d[i] = (Mathf.Sin(ph) + 0.28f * Mathf.Sin(2f * ph)) * env * 0.75f + click;
            }
        }

        // A soft bell: inharmonic partials with longer tails on the low ones.
        static void Chime(float[] d)
        {
            float[] ratio = { 1f, 2.01f, 3.02f, 4.17f, 5.43f };
            float[] amp = { 1f, 0.45f, 0.22f, 0.12f, 0.06f };
            const float f0 = 784f; // G5
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < ratio.Length; k++)
                    s += amp[k] * Mathf.Sin(2f * Mathf.PI * f0 * ratio[k] * t) * Mathf.Exp(-t * (3.2f + 2.5f * k));
                d[i] = s * (1f - Mathf.Exp(-t / 0.002f)) * 0.42f;
            }
        }

        // A squishy thump: a low sine that sags in pitch plus a puff of filtered noise.
        static void Thud(float[] d)
        {
            var noise = new System.Random(2);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = Sweep(t, 210f, 85f, 0.03f);
                lp += (((float)noise.NextDouble() * 2f - 1f) - lp) * 0.08f;
                float env = (1f - Mathf.Exp(-t / 0.002f)) * Mathf.Exp(-t / 0.055f);
                d[i] = (Mathf.Sin(ph) * 0.85f + lp * 0.6f) * env;
            }
        }

        // A "fwump": low thump with a breathy noise sweep.
        static void Shoot(float[] d)
        {
            var noise = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(0.35f, 0.03f, Mathf.Clamp01(t / 0.25f));
                lp += (((float)noise.NextDouble() * 2f - 1f) - lp) * cutoff;
                float body = Mathf.Sin(Sweep(t, 160f, 60f, 0.05f)) * Mathf.Exp(-t / 0.07f);
                float air = lp * (1f - Mathf.Exp(-t / 0.01f)) * Mathf.Exp(-t / 0.11f);
                d[i] = body * 0.7f + air * 0.55f;
            }
        }

        static void Load(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = Sweep(t, 520f, 1250f, 0.04f);
                d[i] = Mathf.Sin(ph) * (1f - Mathf.Exp(-t / 0.002f)) * Mathf.Exp(-t / 0.045f) * 0.5f;
            }
        }

        static void Tick(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = Mathf.Sin(2f * Mathf.PI * 1318f * t);
                d[i] = Mathf.Clamp(s * 2.5f, -1f, 1f) * Mathf.Exp(-t / 0.025f) * 0.35f;
            }
        }

        static void Slurp(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = Sweep(t, 260f, 720f, 0.05f);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.12f));
                d[i] = Mathf.Sin(ph) * env * 0.35f;
            }
        }

        static void GameOverJingle(float[] d)
        {
            float[] notes = { 659.3f, 523.3f, 415.3f, 329.6f }; // E5 C5 G#4 E4
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float start = k * 0.2f;
                    if (t < start) continue;
                    float u = t - start;
                    float tone = Mathf.Sin(2f * Mathf.PI * notes[k] * u) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[k] * u);
                    s += tone * (1f - Mathf.Exp(-u / 0.004f)) * Mathf.Exp(-u / (k == notes.Length - 1 ? 0.5f : 0.18f));
                }
                d[i] = s * 0.3f;
            }
        }

        static void Boom(float[] d)
        {
            var noise = new System.Random(4);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                lp += (((float)noise.NextDouble() * 2f - 1f) - lp) * 0.04f;
                float body = Mathf.Sin(Sweep(t, 140f, 32f, 0.12f)) * Mathf.Exp(-t / 0.45f);
                d[i] = (body * 0.9f + lp * 1.4f * Mathf.Exp(-t / 0.35f)) * (1f - Mathf.Exp(-t / 0.003f)) * 0.8f;
            }
        }

        // Seamless one-second loop: every frequency completes whole cycles.
        static void ChargeHum(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = Mathf.Sin(2f * Mathf.PI * 220f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 330f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 660f * t);
                float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 9f * t);
                d[i] = s * tremolo * 0.22f;
            }
        }
    }
}
