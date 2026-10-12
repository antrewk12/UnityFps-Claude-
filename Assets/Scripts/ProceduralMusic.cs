using System;

/// <summary>
/// Generative, layered background music synthesised sample by sample (no audio files).
///
/// Mood: ambient and playful. A slow major-key chord loop (Cmaj9 - Am9 - Fmaj9 - G6/9) carries a soft pad,
/// marimba-like plucks that wander up and down the chord tones, sparkling bells, and, as intensity rises,
/// a bass line, a shaker and a soft kick. Every melody note is a tone of the current chord, so whatever the
/// random generator picks always sounds consonant.
///
/// Thread safety: <see cref="Render"/> runs on Unity's audio thread. It only touches this class's own state
/// and uses no Unity APIs. The main thread talks to it through <see cref="SetIntensity"/> and
/// <see cref="TriggerStinger"/>, which only write volatile fields.
/// </summary>
public sealed class ProceduralMusic
{
    private const double TwoPi = Math.PI * 2.0;
    private const double Bpm = 96.0;
    private const int StepsPerBar = 16;      // sixteenth-note grid
    private const int BarsPerChord = 2;
    private const int ChordCount = 4;
    private const int PadVoiceCount = 5;
    private const int NoteVoiceCount = 24;

    private const int Pluck = 0;
    private const int Bell = 1;
    private const int Bass = 2;

    // Pad voicings, low to high, so the voices glide smoothly from chord to chord.
    private static readonly int[][] PadVoicings =
    {
        new[] { 48, 55, 64, 71, 74 },   // Cmaj9
        new[] { 45, 52, 60, 67, 71 },   // Am9
        new[] { 41, 48, 57, 64, 67 },   // Fmaj9
        new[] { 43, 50, 59, 64, 69 },   // G6/9
    };

    private static readonly int[] BassRoots = { 36, 33, 41, 43 };

    // Pitch classes (0 = C) that belong to each chord, used for every melodic note.
    private static readonly int[][] MelodyPitchClasses =
    {
        new[] { 0, 2, 4, 7, 11 },
        new[] { 0, 4, 7, 9, 11 },
        new[] { 0, 4, 5, 7, 9 },
        new[] { 2, 4, 7, 9, 11 },
    };

    // How likely the plucks are to play on each sixteenth: strong beats first, off-beats less often.
    private static readonly float[] PluckWeights =
    {
        1.0f, 0.2f, 0.7f, 0.3f,
        0.9f, 0.2f, 0.7f, 0.35f,
        1.0f, 0.2f, 0.7f, 0.3f,
        0.9f, 0.3f, 0.8f, 0.4f,
    };

    // Per instrument: three integer partials (integer ratios keep the wave continuous when the phase wraps).
    private static readonly int[][] PartialRatios = { new[] { 1, 2, 4 }, new[] { 1, 2, 3 }, new[] { 1, 2, 3 } };
    private static readonly float[][] PartialAmps = { new[] { 1f, 0.25f, 0.12f }, new[] { 1f, 0.4f, 0.2f }, new[] { 1f, 0.35f, 0.1f } };
    // Envelope power per partial: higher powers make that partial die away faster than the fundamental.
    private static readonly int[][] PartialExps = { new[] { 1, 2, 3 }, new[] { 1, 2, 3 }, new[] { 1, 2, 3 } };
    private static readonly double[] DecaySeconds = { 0.45, 1.4, 0.32 };
    private static readonly float[] ReverbSends = { 0.30f, 0.50f, 0.04f };
    private static readonly float[] DelaySends = { 0.40f, 0.50f, 0.0f };

    // ---------- Public controls and counters ----------

    /// <summary>Overall level of the reverb tail. 0 turns it off.</summary>
    public float ReverbMix = 1f;
    /// <summary>Overall level of the echo. 0 turns it off.</summary>
    public float DelayMix = 1f;

    // Counters, handy for checking what the music is doing.
    public int StepCount { get; private set; }
    public int PluckCount { get; private set; }
    public int BellCount { get; private set; }
    public int BassCount { get; private set; }
    public int KickCount { get; private set; }
    public int ShakerCount { get; private set; }
    public int StingerNoteCount { get; private set; }

    private volatile float targetIntensity;
    private volatile bool stingerRequested;

    // ---------- State ----------

    private sealed class NoteVoice
    {
        public bool Active;
        public double Phase, Inc;
        public float Env, Decay, Attack, AttackStep, Amp, PanL, PanR, ReverbSend, DelaySend;
        public int Type;
    }

    private sealed class Comb
    {
        private readonly float[] buffer;
        private int index;
        private float store;

        public Comb(int length) { buffer = new float[Math.Max(1, length)]; }

        public float Process(float input, float feedback, float damp)
        {
            float output = buffer[index];
            store = output * (1f - damp) + store * damp;
            buffer[index] = input + store * feedback;
            if (++index >= buffer.Length) index = 0;
            return output;
        }
    }

    private sealed class Allpass
    {
        private readonly float[] buffer;
        private int index;

        public Allpass(int length) { buffer = new float[Math.Max(1, length)]; }

        public float Process(float input)
        {
            float buffered = buffer[index];
            float output = -input + buffered;
            buffer[index] = input + buffered * 0.5f;
            if (++index >= buffer.Length) index = 0;
            return output;
        }
    }

    private readonly double sampleRate;
    private readonly double samplesPerStep;
    private readonly int[][] melodyPools = new int[ChordCount][];
    private readonly NoteVoice[] voices = new NoteVoice[NoteVoiceCount];

    // Pad
    private readonly double[] padFreq = new double[PadVoiceCount];
    private readonly double[] padTarget = new double[PadVoiceCount];
    private readonly double[] padPhaseA = new double[PadVoiceCount];
    private readonly double[] padPhaseB = new double[PadVoiceCount];
    private readonly double[] lfoPhase = new double[PadVoiceCount];
    private readonly double[] lfoInc = new double[PadVoiceCount];
    private readonly float[] padPanL = new float[PadVoiceCount];
    private readonly float[] padPanR = new float[PadVoiceCount];
    private readonly double glideCoef;

    // Percussion
    private double kickPhase, kickFreq;
    private float kickEnv;
    private readonly double kickPitchDecay, kickEnvDecay;
    private float shakerEnv, shakerPrev, shakerPanL = 0.7f, shakerPanR = 0.3f;
    private readonly float shakerDecay;

    // Effects
    private readonly float[] delayBufferL, delayBufferR;
    private int delayIndex;
    private readonly Comb[] combsL = new Comb[4];
    private readonly Comb[] combsR = new Comb[4];
    private readonly Allpass[] allpassL = new Allpass[2];
    private readonly Allpass[] allpassR = new Allpass[2];

    // Sequencer
    private double samplesIntoStep;
    private int step = -1;
    private int chord;
    private int walkIndex;
    private int lastPluckMidi = 76;
    private int stingerLeft;
    private int stingerIndex;
    private float intensity;
    private readonly double intensityCoef;
    private uint rngState;

    public ProceduralMusic(int sampleRate, int seed = 12345)
    {
        this.sampleRate = sampleRate;
        samplesPerStep = sampleRate * 60.0 / Bpm / 4.0;
        rngState = (uint)(seed == 0 ? 1 : seed);

        for (int c = 0; c < ChordCount; c++)
        {
            melodyPools[c] = BuildPool(MelodyPitchClasses[c]);
        }

        for (int i = 0; i < NoteVoiceCount; i++)
        {
            voices[i] = new NoteVoice();
        }

        for (int i = 0; i < PadVoiceCount; i++)
        {
            double pan = (i / (double)(PadVoiceCount - 1) - 0.5) * 1.0;
            double angle = (pan + 1.0) * Math.PI / 4.0;
            padPanL[i] = (float)Math.Cos(angle);
            padPanR[i] = (float)Math.Sin(angle);
            lfoInc[i] = TwoPi * (0.07 + 0.03 * i) / sampleRate;
            lfoPhase[i] = i * 1.3;
        }

        glideCoef = 1.0 - Math.Exp(-1.0 / (0.35 * sampleRate));
        intensityCoef = 1.0 - Math.Exp(-1.0 / (2.5 * sampleRate));
        kickPitchDecay = Math.Exp(-1.0 / (0.025 * sampleRate));
        kickEnvDecay = Math.Exp(-1.0 / (0.13 * sampleRate));
        shakerDecay = (float)Math.Exp(-1.0 / (0.035 * sampleRate));

        delayBufferL = new float[(int)(samplesPerStep * 3.0) + 1];
        delayBufferR = new float[delayBufferL.Length];

        int[] combLengths = { 1116, 1188, 1277, 1356 };
        int[] allpassLengths = { 556, 441 };
        double scale = sampleRate / 44100.0;
        for (int i = 0; i < 4; i++)
        {
            combsL[i] = new Comb((int)(combLengths[i] * scale));
            combsR[i] = new Comb((int)((combLengths[i] + 23) * scale));
        }
        for (int i = 0; i < 2; i++)
        {
            allpassL[i] = new Allpass((int)(allpassLengths[i] * scale));
            allpassR[i] = new Allpass((int)((allpassLengths[i] + 23) * scale));
        }

        // Start exactly on the first beat of the first chord, pad already in tune.
        SetChord(0);
        for (int i = 0; i < PadVoiceCount; i++)
        {
            padFreq[i] = padTarget[i];
        }
        samplesIntoStep = samplesPerStep;
        targetIntensity = 0.1f;
        intensity = 0.1f;
    }

    // ---------- Control from the main thread ----------

    /// <summary>0 = calm and sparse, 1 = full band. Changes fade in over a couple of seconds.</summary>
    public void SetIntensity(float value)
    {
        targetIntensity = value < 0f ? 0f : (value > 1f ? 1f : value);
    }

    /// <summary>Skips the fade so the intensity jumps straight to its target (used for tests and restarts).</summary>
    public void SnapIntensity()
    {
        intensity = targetIntensity;
    }

    /// <summary>Plays a short rising run of notes on the next sixteenth, e.g. when a wave begins.</summary>
    public void TriggerStinger()
    {
        stingerRequested = true;
    }

    // ---------- Rendering (audio thread) ----------

    /// <summary>Fills an interleaved stereo buffer.</summary>
    public void Render(float[] data, int frames)
    {
        float target = targetIntensity;
        float reverbMix = ReverbMix;
        float delayMix = DelayMix;
        int delayLength = delayBufferL.Length;

        for (int f = 0; f < frames; f++)
        {
            if (samplesIntoStep >= samplesPerStep)
            {
                samplesIntoStep -= samplesPerStep;
                AdvanceStep();
            }
            samplesIntoStep += 1.0;
            intensity += (float)((target - intensity) * intensityCoef);

            float dryL = 0f, dryR = 0f, sendReverb = 0f, sendDelay = 0f;

            // Pad: five detuned sine pairs gliding between chords, each breathing slowly.
            float padGain = 0.055f * (1f - 0.2f * intensity);
            for (int i = 0; i < PadVoiceCount; i++)
            {
                padFreq[i] += (padTarget[i] - padFreq[i]) * glideCoef;
                double inc = TwoPi * padFreq[i] / sampleRate;
                padPhaseA[i] += inc * 1.0012;
                padPhaseB[i] += inc * 0.9988;
                if (padPhaseA[i] > TwoPi) padPhaseA[i] -= TwoPi;
                if (padPhaseB[i] > TwoPi) padPhaseB[i] -= TwoPi;
                lfoPhase[i] += lfoInc[i];
                if (lfoPhase[i] > TwoPi) lfoPhase[i] -= TwoPi;

                float breathe = 0.75f + 0.25f * (float)Math.Sin(lfoPhase[i]);
                float s = (float)(Math.Sin(padPhaseA[i]) + Math.Sin(padPhaseB[i]) + 0.18 * Math.Sin(2.0 * padPhaseA[i])) * breathe * padGain;
                dryL += s * padPanL[i];
                dryR += s * padPanR[i];
                sendReverb += s * 0.5f;
            }

            // Plucks, bells and bass.
            for (int i = 0; i < NoteVoiceCount; i++)
            {
                NoteVoice v = voices[i];
                if (!v.Active) continue;

                v.Phase += v.Inc;
                if (v.Phase > TwoPi) v.Phase -= TwoPi;

                float e = v.Env;
                float e2 = e * e;
                float e3 = e2 * e;
                int[] ratios = PartialRatios[v.Type];
                float[] amps = PartialAmps[v.Type];
                int[] exps = PartialExps[v.Type];

                float s = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float partialEnv = exps[k] == 1 ? e : (exps[k] == 2 ? e2 : e3);
                    s += amps[k] * (float)Math.Sin(v.Phase * ratios[k]) * partialEnv;
                }

                if (v.Attack < 1f)
                {
                    v.Attack += v.AttackStep;
                    if (v.Attack > 1f) v.Attack = 1f;
                }
                s *= v.Attack * v.Amp;

                v.Env *= v.Decay;
                if (v.Env < 0.0003f) v.Active = false;

                dryL += s * v.PanL;
                dryR += s * v.PanR;
                sendReverb += s * v.ReverbSend;
                sendDelay += s * v.DelaySend;
            }

            // Soft kick: a sine that drops quickly in pitch.
            if (kickEnv > 0.0005f)
            {
                kickFreq = 45.0 + (kickFreq - 45.0) * kickPitchDecay;
                kickPhase += TwoPi * kickFreq / sampleRate;
                if (kickPhase > TwoPi) kickPhase -= TwoPi;
                float k = (float)Math.Sin(kickPhase) * kickEnv;
                kickEnv *= (float)kickEnvDecay;
                dryL += k * 0.9f;
                dryR += k * 0.9f;
            }

            // Shaker: short bursts of high-passed noise.
            if (shakerEnv > 0.0005f)
            {
                float noise = NextSigned();
                float high = noise - shakerPrev;
                shakerPrev = noise;
                float s = high * shakerEnv * 0.5f;
                shakerEnv *= shakerDecay;
                dryL += s * shakerPanL;
                dryR += s * shakerPanR;
                sendReverb += s * 0.1f;
            }

            // Ping-pong echo: each side feeds the other, quieter every time round.
            float echoL = delayBufferL[delayIndex];
            float echoR = delayBufferR[delayIndex];
            delayBufferL[delayIndex] = sendDelay + echoR * 0.42f;
            delayBufferR[delayIndex] = echoL * 0.42f;
            if (++delayIndex >= delayLength) delayIndex = 0;

            // Reverb: four combs feeding two allpass filters on each side.
            float reverbIn = (sendReverb + (echoL + echoR) * 0.2f) * 0.03f;
            float revL = 0f, revR = 0f;
            for (int i = 0; i < 4; i++)
            {
                revL += combsL[i].Process(reverbIn, 0.84f, 0.25f);
                revR += combsR[i].Process(reverbIn, 0.84f, 0.25f);
            }
            for (int i = 0; i < 2; i++)
            {
                revL = allpassL[i].Process(revL);
                revR = allpassR[i].Process(revR);
            }

            float outL = dryL + revL * 3f * reverbMix + echoL * 0.8f * delayMix;
            float outR = dryR + revR * 3f * reverbMix + echoR * 0.8f * delayMix;

            // Soft clip so stacked layers can never produce harsh digital clipping, and guard against bad values.
            outL = (float)Math.Tanh(outL * 0.9f);
            outR = (float)Math.Tanh(outR * 0.9f);
            if (!(outL > -2f && outL < 2f)) outL = 0f;
            if (!(outR > -2f && outR < 2f)) outR = 0f;

            data[f * 2] = outL;
            data[f * 2 + 1] = outR;
        }
    }

    // ---------- Sequencer ----------

    private void AdvanceStep()
    {
        step++;
        StepCount++;

        int stepInBar = step % StepsPerBar;
        int barIndex = (step / StepsPerBar) % (ChordCount * BarsPerChord);
        int newChord = barIndex / BarsPerChord;
        if (newChord != chord)
        {
            SetChord(newChord);
        }

        float i = intensity;
        int[] pool = melodyPools[chord];

        // Layers fade in as intensity rises.
        float bassGain = SmoothStep(i, 0.28f, 0.45f);
        float shakerGain = SmoothStep(i, 0.45f, 0.62f);
        float kickGain = SmoothStep(i, 0.68f, 0.85f);
        float pluckGain = 0.75f + 0.25f * i;

        // Pluck: a random walk through the chord tones, denser as intensity rises.
        float chance = PluckWeights[stepInBar] * (0.45f + 0.55f * i) * pluckGain;
        if (Next() < chance)
        {
            walkIndex = Clamp(walkIndex + (int)(Next() * 5f) - 2, 0, pool.Length - 1);
            lastPluckMidi = pool[walkIndex];
            float pan = (walkIndex / (float)Math.Max(1, pool.Length - 1) - 0.5f) * 1.0f;
            float velocity = 0.6f + 0.4f * PluckWeights[stepInBar];
            StartNote(Pluck, lastPluckMidi, 0.20f * velocity * pluckGain, pan);
            PluckCount++;
        }

        // Bells: rare, high, and left to ring into the echo and reverb.
        if (stepInBar % 2 == 0 && Next() < 0.14f + 0.04f * (1f - i))
        {
            int half = pool.Length / 2;
            int index = half + (int)(Next() * (pool.Length - half));
            int midi = Math.Min(pool[Clamp(index, 0, pool.Length - 1)] + 12, 100);
            StartNote(Bell, midi, 0.12f, Next() * 1.6f - 0.8f);
            BellCount++;
        }

        // Bass: a bouncy root and fifth pattern.
        if (bassGain > 0.05f)
        {
            int root = BassRoots[chord];
            if (stepInBar == 0) { StartNote(Bass, root, 0.34f * bassGain, 0f); BassCount++; }
            else if (stepInBar == 6 && Next() < 0.8f) { StartNote(Bass, root, 0.24f * bassGain, 0f); BassCount++; }
            else if (stepInBar == 10 && Next() < 0.85f) { StartNote(Bass, root + 7, 0.26f * bassGain, 0f); BassCount++; }
            else if (stepInBar == 14 && Next() < 0.25f) { StartNote(Bass, root + 12, 0.17f * bassGain, 0f); BassCount++; }
        }

        // Kick on beats 1 and 3, with an occasional playful extra.
        if (kickGain > 0.05f)
        {
            bool hit = stepInBar == 0 || stepInBar == 8
                || (stepInBar == 10 && Next() < 0.3f)
                || (stepInBar == 14 && Next() < 0.2f);
            if (hit)
            {
                kickEnv = 0.55f * kickGain;
                kickFreq = 130.0;
                kickPhase = 0.0;
                KickCount++;
            }
        }

        // Shaker on the eighths, accenting the off-beats, with extra sixteenths when it gets busy.
        if (shakerGain > 0.05f)
        {
            if (stepInBar % 2 == 0)
            {
                TriggerShaker((stepInBar % 4 == 2 ? 0.16f : 0.09f) * shakerGain);
            }
            else if (i > 0.8f && Next() < 0.5f)
            {
                TriggerShaker(0.05f * shakerGain);
            }
        }

        // Stinger: a quick rising run up the current chord.
        if (stingerRequested)
        {
            stingerRequested = false;
            stingerLeft = 6;
            stingerIndex = 0;
        }
        if (stingerLeft > 0)
        {
            int midi = pool[Clamp(stingerIndex, 0, pool.Length - 1)];
            float pan = (stingerIndex / 6f - 0.5f) * 0.8f;
            StartNote(Pluck, midi, 0.26f, pan);
            StartNote(Bell, Math.Min(midi + 12, 100), 0.10f, pan);
            StingerNoteCount++;
            stingerIndex++;
            stingerLeft--;
        }
    }

    private void SetChord(int newChord)
    {
        chord = newChord;
        int[] voicing = PadVoicings[newChord];
        for (int v = 0; v < PadVoiceCount; v++)
        {
            padTarget[v] = MidiToFrequency(voicing[v]);
        }
        walkIndex = NearestIndex(melodyPools[newChord], lastPluckMidi);
    }

    private void StartNote(int type, int midi, float amplitude, float pan)
    {
        NoteVoice voice = voices[0];
        for (int i = 0; i < NoteVoiceCount; i++)
        {
            if (!voices[i].Active) { voice = voices[i]; break; }
            if (voices[i].Env < voice.Env) voice = voices[i];   // all busy: steal the quietest
        }

        double frequency = MidiToFrequency(midi);
        double angle = (Math.Max(-1.0, Math.Min(1.0, pan)) + 1.0) * Math.PI / 4.0;

        voice.Active = true;
        voice.Type = type;
        voice.Phase = 0.0;
        voice.Inc = TwoPi * frequency / sampleRate;
        voice.Env = 1f;
        voice.Decay = (float)Math.Exp(-1.0 / (DecaySeconds[type] * sampleRate));
        voice.Attack = 0f;
        voice.AttackStep = (float)(1.0 / (0.003 * sampleRate));   // 3 ms fade-in avoids a click
        voice.Amp = amplitude;
        voice.PanL = (float)Math.Cos(angle);
        voice.PanR = (float)Math.Sin(angle);
        voice.ReverbSend = ReverbSends[type];
        voice.DelaySend = DelaySends[type];
    }

    private void TriggerShaker(float amplitude)
    {
        shakerEnv = amplitude;
        // Alternate sides so the shaker bounces left and right.
        float swap = shakerPanL;
        shakerPanL = shakerPanR;
        shakerPanR = swap;
        ShakerCount++;
    }

    // ---------- Helpers ----------

    private static int[] BuildPool(int[] pitchClasses)
    {
        // Every chord tone between G4 (67) and G6 (91).
        int count = 0;
        for (int midi = 67; midi <= 91; midi++)
        {
            if (Array.IndexOf(pitchClasses, midi % 12) >= 0) count++;
        }

        int[] pool = new int[count];
        int index = 0;
        for (int midi = 67; midi <= 91; midi++)
        {
            if (Array.IndexOf(pitchClasses, midi % 12) >= 0) pool[index++] = midi;
        }
        return pool;
    }

    private static int NearestIndex(int[] pool, int midi)
    {
        int best = 0;
        for (int i = 1; i < pool.Length; i++)
        {
            if (Math.Abs(pool[i] - midi) < Math.Abs(pool[best] - midi)) best = i;
        }
        return best;
    }

    private static double MidiToFrequency(int midi)
    {
        return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }

    private static float SmoothStep(float value, float low, float high)
    {
        float t = (value - low) / (high - low);
        t = t < 0f ? 0f : (t > 1f ? 1f : t);
        return t * t * (3f - 2f * t);
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    // Small xorshift generator: Unity's Random isn't safe to call from the audio thread.
    private float Next()
    {
        rngState ^= rngState << 13;
        rngState ^= rngState >> 17;
        rngState ^= rngState << 5;
        return (rngState & 0xFFFFFF) / 16777216f;
    }

    private float NextSigned()
    {
        return Next() * 2f - 1f;
    }
}
