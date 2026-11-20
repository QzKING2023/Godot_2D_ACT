using System;
using System.Collections.Generic;
using Godot;

namespace ACTSpike.ACT.Audio;

/// <summary>
/// Pure startup in-memory 16-bit mono PCM AudioStreamWav synthesizer.
/// Generates deterministic, anti-pop, non-clipping combat SFX buffers with zero external assets.
/// </summary>
public static class ProceduralSfxBank
{
    public const int SampleRate = 22050;

    private const float TwoPi = MathF.PI * 2f;
    private static readonly Dictionary<CombatSfxId, AudioStreamWav> StreamCache = new();
    private static readonly Dictionary<CombatSfxId, short[]> PcmCache = new();

    /// <summary>Pre-synthesizes and caches all CombatSfxId AudioStreamWav buffers.</summary>
    public static void Prewarm()
    {
        foreach (var id in Enum.GetValues<CombatSfxId>())
            GetStream(id);
    }

    /// <summary>Returns the cached 16-bit mono AudioStreamWav for a combat cue.</summary>
    public static AudioStreamWav GetStream(CombatSfxId id)
    {
        if (StreamCache.TryGetValue(id, out var cached))
            return cached;

        short[] pcm = GeneratePcm16(id);
        byte[] bytes = EncodeLittleEndianPcm16(pcm);
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
            Data = bytes
        };
        StreamCache[id] = stream;
        return stream;
    }

    /// <summary>Synthesizes deterministic 16-bit signed mono PCM samples for a combat cue.</summary>
    public static short[] GeneratePcm16(CombatSfxId id)
    {
        if (PcmCache.TryGetValue(id, out var cached))
            return (short[])cached.Clone();

        (float[] raw, float targetPeak, float attackSec, float releaseSec) = id switch
        {
            CombatSfxId.PlayerSlash => (SynthesizeSlash(isSkill: false, id), 0.68f, 0.004f, 0.025f),
            CombatSfxId.PlayerSkillDash => (SynthesizeSlash(isSkill: true, id), 0.78f, 0.006f, 0.045f),
            CombatSfxId.PlayerHit => (SynthesizeImpact(heavyWarning: false, id), 0.75f, 0.002f, 0.030f),
            CombatSfxId.PlayerDamaged => (SynthesizeImpact(heavyWarning: true, id), 0.82f, 0.003f, 0.050f),
            CombatSfxId.BlockNormal => (SynthesizeBlock(perfect: false, id), 0.70f, 0.002f, 0.045f),
            CombatSfxId.BlockPerfect => (SynthesizeBlock(perfect: true, id), 0.82f, 0.002f, 0.090f),
            CombatSfxId.DodgeNormal => (SynthesizeDodge(perfect: false, id), 0.62f, 0.016f, 0.045f),
            CombatSfxId.DodgePerfect => (SynthesizeDodge(perfect: true, id), 0.80f, 0.012f, 0.075f),
            CombatSfxId.TelegraphNormal => (SynthesizeTelegraphNormal(), 0.72f, 0.008f, 0.060f),
            CombatSfxId.TelegraphBreakGrab => (SynthesizeTelegraphBreakGrab(), 0.80f, 0.005f, 0.040f),
            CombatSfxId.TelegraphDelayedArea => (SynthesizeTelegraphDelayedArea(id), 0.74f, 0.020f, 0.090f),
            CombatSfxId.TimeSlowStart => (SynthesizeTimeSlowSweep(downward: true, id), 0.76f, 0.010f, 0.075f),
            CombatSfxId.TimeSlowEnd => (SynthesizeTimeSlowSweep(downward: false, id), 0.74f, 0.008f, 0.060f),
            CombatSfxId.ShieldDeflect => (SynthesizeShieldDeflect(id), 0.78f, 0.002f, 0.055f),
            CombatSfxId.ProjectileFire => (SynthesizeProjectileFire(id), 0.72f, 0.003f, 0.040f),
            CombatSfxId.ProjectileReflect => (SynthesizeProjectileReflect(id), 0.84f, 0.002f, 0.080f),
            CombatSfxId.BossOverloadPulse => (SynthesizeBossOverloadPulse(id), 0.86f, 0.012f, 0.120f),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unsupported combat SFX cue")
        };

        short[] finalized = FinalizePcm16(raw, targetPeak, attackSec, releaseSec);
        PcmCache[id] = finalized;
        return (short[])finalized.Clone();
    }

    private static float[] SynthesizeSlash(bool isSkill, CombatSfxId seedId)
    {
        float duration = isSkill ? 0.24f : 0.13f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float startHz = isSkill ? 3200f : 2500f;
        float endHz = isSkill ? 190f : 480f;
        float subPhase = 0f;
        float bladePhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float env = MathF.Pow(1f - t, isSkill ? 1.25f : 1.7f) * SmoothAttack(t, 0.06f);
            float cutoff = startHz * MathF.Pow(endHz / startHz, t);
            float noise = NextWhiteNoise(ref rng);
            float band = filter.StepBandpass(noise, cutoff, q: isSkill ? 0.50f : 0.60f);

            float bladeHz = Mathf.Lerp(isSkill ? 680f : 920f, isSkill ? 140f : 360f, t);
            bladePhase += TwoPi * bladeHz / SampleRate;
            float blade = MathF.Sin(bladePhase) * 0.18f * MathF.Pow(1f - t, 2.2f);

            float sub = 0f;
            if (isSkill)
            {
                float subHz = Mathf.Lerp(230f, 82f, t);
                subPhase += TwoPi * subHz / SampleRate;
                sub = MathF.Sin(subPhase) * 0.42f * MathF.Pow(1f - t, 1.3f);
            }

            buf[i] = (band * 1.35f + blade + sub) * env;
        }
        return buf;
    }

    private static float[] SynthesizeImpact(bool heavyWarning, CombatSfxId seedId)
    {
        float duration = heavyWarning ? 0.27f : 0.14f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float thumpPhase = 0f;
        float warnPhaseA = 0f;
        float warnPhaseB = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float dt = i / (float)SampleRate;

            float f0 = heavyWarning ? 132f : 185f;
            float f1 = heavyWarning ? 38f : 56f;
            float thumpHz = f1 + (f0 - f1) * MathF.Exp(-dt * (heavyWarning ? 15f : 26f));
            thumpPhase += TwoPi * thumpHz / SampleRate;
            float thumpEnv = MathF.Exp(-dt * (heavyWarning ? 9.5f : 17f));
            float thump = MathF.Sin(thumpPhase) * thumpEnv;

            float noise = NextWhiteNoise(ref rng);
            float cutoff = heavyWarning ? 950f : 1900f;
            float transient = filter.StepLowpass(noise, cutoff, q: 0.7f) * MathF.Exp(-dt * (heavyWarning ? 32f : 72f));

            float warn = 0f;
            if (heavyWarning)
            {
                warnPhaseA += TwoPi * 158f / SampleRate;
                warnPhaseB += TwoPi * 224f / SampleRate;
                warn = (MathF.Sin(warnPhaseA) + 0.7f * MathF.Sin(warnPhaseB)) * 0.30f * MathF.Exp(-dt * 12f);
            }
            else
            {
                float clickHz = Mathf.Lerp(820f, 240f, Mathf.Min(1f, t * 3f));
                warnPhaseA += TwoPi * clickHz / SampleRate;
                warn = MathF.Sin(warnPhaseA) * 0.28f * MathF.Exp(-dt * 48f);
            }

            buf[i] = thump * 0.90f + transient * 0.55f + warn;
        }
        return buf;
    }

    private static float[] SynthesizeBlock(bool perfect, CombatSfxId seedId)
    {
        float duration = perfect ? 0.38f : 0.19f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        ReadOnlySpan<float> normalPartials = stackalloc float[] { 587.3f, 1174.6f, 1692f, 2318f };
        ReadOnlySpan<float> perfectPartials = stackalloc float[] { 784.0f, 1174.7f, 1568.0f, 2349.3f, 3136.0f };
        var partials = perfect ? perfectPartials : normalPartials;
        Span<float> phases = stackalloc float[partials.Length];

        for (int i = 0; i < n; i++)
        {
            float dt = i / (float)SampleRate;
            float ping = 0f;
            for (int p = 0; p < partials.Length; p++)
            {
                phases[p] += TwoPi * partials[p] / SampleRate;
                float decay = perfect ? (4.8f + p * 1.6f) : (12f + p * 6.5f);
                float amp = (perfect ? 0.34f : 0.36f) / (1f + p * 0.45f);
                ping += MathF.Sin(phases[p]) * amp * MathF.Exp(-dt * decay);
            }

            if (perfect)
            {
                float shimmer = 0.85f + 0.15f * MathF.Sin(TwoPi * 11f * dt);
                ping *= shimmer;
            }

            float clang = filter.StepBandpass(NextWhiteNoise(ref rng), perfect ? 2800f : 2200f, 0.65f)
                          * MathF.Exp(-dt * 95f) * 0.45f;
            buf[i] = ping + clang;
        }
        return buf;
    }

    private static float[] SynthesizeDodge(bool perfect, CombatSfxId seedId)
    {
        float duration = perfect ? 0.34f : 0.17f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float chimePhaseA = 0f;
        float chimePhaseB = 0f;
        float chimePhaseC = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float dt = i / (float)SampleRate;

            float whooshEnd = perfect ? 0.16f : duration;
            float whooshT = Mathf.Clamp(dt / whooshEnd, 0f, 1f);
            float arch = MathF.Sin(MathF.PI * whooshT);
            float cutoff = 380f + 1250f * MathF.Sin(MathF.PI * whooshT);
            float whoosh = filter.StepBandpass(NextWhiteNoise(ref rng), cutoff, 0.55f) * arch * (dt <= whooshEnd ? 1f : 0f);

            float chime = 0f;
            if (perfect)
            {
                if (dt >= 0.025f)
                {
                    float d1 = dt - 0.025f;
                    chimePhaseA += TwoPi * 1046.5f / SampleRate;
                    chime += MathF.Sin(chimePhaseA) * 0.35f * SmoothAttack(d1 / 0.015f, 1f) * MathF.Exp(-d1 * 8.5f);
                }
                if (dt >= 0.075f)
                {
                    float d2 = dt - 0.075f;
                    chimePhaseB += TwoPi * 1567.98f / SampleRate;
                    chimePhaseC += TwoPi * 2093.0f / SampleRate;
                    float attack = SmoothAttack(d2 / 0.012f, 1f);
                    chime += (MathF.Sin(chimePhaseB) * 0.38f + MathF.Sin(chimePhaseC) * 0.24f) * attack * MathF.Exp(-d2 * 7.2f);
                }
            }

            buf[i] = whoosh * 0.95f + chime;
        }
        return buf;
    }

    private static float[] SynthesizeTelegraphNormal()
    {
        float duration = 0.24f;
        int n = SampleCount(duration);
        var buf = new float[n];
        float p1 = 0f;
        float p2 = 0f;
        float p3 = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float dt = i / (float)SampleRate;
            float env = SmoothAttack(t, 0.12f) * MathF.Exp(-dt * 6.5f);
            p1 += TwoPi * 659.25f / SampleRate;
            p2 += TwoPi * 1318.5f / SampleRate;
            p3 += TwoPi * 1977.75f / SampleRate;
            buf[i] = (MathF.Sin(p1) * 0.70f + MathF.Sin(p2) * 0.25f + MathF.Sin(p3) * 0.10f) * env;
        }
        return buf;
    }

    private static float[] SynthesizeTelegraphBreakGrab()
    {
        float duration = 0.35f;
        int n = SampleCount(duration);
        var buf = new float[n];
        float p1 = 0f;
        float p2 = 0f;

        for (int i = 0; i < n; i++)
        {
            float dt = i / (float)SampleRate;
            float pulse1 = PulseEnvelope(dt, 0.00f, 0.13f, 0.015f);
            float pulse2 = PulseEnvelope(dt, 0.18f, 0.33f, 0.015f);
            float env = MathF.Max(pulse1, pulse2);

            p1 += TwoPi * 880.0f / SampleRate;
            p2 += TwoPi * 1244.5f / SampleRate;
            float tone = MathF.Sin(p1) * 0.65f + MathF.Sin(p2) * 0.45f;
            float clippedEdge = MathF.Tanh(tone * 1.45f);
            buf[i] = clippedEdge * env;
        }
        return buf;
    }

    private static float[] SynthesizeTelegraphDelayedArea(CombatSfxId seedId)
    {
        float duration = 0.48f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();
        float p1 = 0f;
        float p2 = 0f;
        float p3 = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float baseHz = Mathf.Lerp(105f, 265f, t * t);
            p1 += TwoPi * baseHz / SampleRate;
            p2 += TwoPi * (baseHz * 2f) / SampleRate;
            p3 += TwoPi * (baseHz * 3f) / SampleRate;

            float swell = MathF.Sin(MathF.PI * Mathf.Min(1f, t / 0.85f));
            float shimmer = filter.StepBandpass(NextWhiteNoise(ref rng), 280f + 700f * t, 0.5f) * 0.25f;
            float hum = MathF.Sin(p1) * 0.65f + MathF.Sin(p2) * 0.30f + MathF.Sin(p3) * 0.14f;
            buf[i] = (hum + shimmer) * (0.25f + 0.75f * swell);
        }
        return buf;
    }

    private static float[] SynthesizeTimeSlowSweep(bool downward, CombatSfxId seedId)
    {
        float duration = downward ? 0.34f : 0.28f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float fStart = downward ? 740f : 165f;
        float fEnd = downward ? 130f : 700f;
        float pMain = 0f;
        float pSub = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float curve = downward ? MathF.Pow(t, 0.85f) : MathF.Pow(t, 1.15f);
            float hz = fStart * MathF.Pow(fEnd / fStart, curve);

            pMain += TwoPi * hz / SampleRate;
            pSub += TwoPi * (hz * (downward ? 0.5f : 1.5f)) / SampleRate;

            float env = SmoothAttack(t, 0.08f) * MathF.Pow(1f - t, 0.70f);
            float air = filter.StepBandpass(NextWhiteNoise(ref rng), hz * 1.4f, 0.55f) * 0.22f;
            buf[i] = (MathF.Sin(pMain) * 0.68f + MathF.Sin(pSub) * 0.32f + air) * env;
        }
        return buf;
    }

    private static float[] SynthesizeShieldDeflect(CombatSfxId seedId)
    {
        const float duration = 0.22f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        ReadOnlySpan<float> partials = stackalloc float[] { 932.3f, 1894f, 2742f, 3920f };
        Span<float> phases = stackalloc float[partials.Length];

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float dt = i / (float)SampleRate;
            float pitchGlance = 1f + 0.06f * MathF.Exp(-dt * 42f);

            float metallic = 0f;
            for (int p = 0; p < partials.Length; p++)
            {
                phases[p] += TwoPi * (partials[p] * pitchGlance) / SampleRate;
                float decay = 14f + p * 5.2f;
                float amp = 0.34f / (1f + p * 0.38f);
                metallic += MathF.Sin(phases[p]) * amp * MathF.Exp(-dt * decay);
            }

            float scrapeCutoff = Mathf.Lerp(3400f, 1400f, t);
            float scrape = filter.StepBandpass(NextWhiteNoise(ref rng), scrapeCutoff, 0.52f)
                           * MathF.Exp(-dt * 45f) * 0.62f;
            buf[i] = metallic + scrape;
        }
        return buf;
    }

    private static float[] SynthesizeProjectileFire(CombatSfxId seedId)
    {
        const float duration = 0.18f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float chirpPhase = 0f;
        float bodyPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float dt = i / (float)SampleRate;

            float chirpHz = 340f + 1310f * MathF.Exp(-dt * 28f);
            chirpPhase += TwoPi * chirpHz / SampleRate;
            float chirp = MathF.Sin(chirpPhase) * 0.58f * MathF.Exp(-dt * 18f);

            float bodyHz = Mathf.Lerp(260f, 118f, t);
            bodyPhase += TwoPi * bodyHz / SampleRate;
            float body = MathF.Sin(bodyPhase) * 0.34f * MathF.Exp(-dt * 24f);

            float snapCutoff = Mathf.Lerp(2400f, 880f, t);
            float snap = filter.StepBandpass(NextWhiteNoise(ref rng), snapCutoff, 0.58f)
                         * MathF.Exp(-dt * 34f) * 0.52f;
            buf[i] = (chirp + body + snap) * SmoothAttack(t, 0.04f);
        }
        return buf;
    }

    private static float[] SynthesizeProjectileReflect(CombatSfxId seedId)
    {
        const float duration = 0.36f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        ReadOnlySpan<float> ringPartials = stackalloc float[] { 1046.5f, 1567.98f, 2093.0f, 3135.96f };
        Span<float> ringPhases = stackalloc float[ringPartials.Length];
        float counterPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float dt = i / (float)SampleRate;

            float ring = 0f;
            for (int p = 0; p < ringPartials.Length; p++)
            {
                ringPhases[p] += TwoPi * ringPartials[p] / SampleRate;
                ring += MathF.Sin(ringPhases[p]) * (0.28f / (1f + p * 0.42f)) * MathF.Exp(-dt * (6.2f + p * 2.1f));
            }

            float counter = 0f;
            if (dt >= 0.035f)
            {
                float d = dt - 0.035f;
                float u = Math.Clamp(d / (duration - 0.035f), 0f, 1f);
                float sweepHz = 520f * MathF.Pow(1880f / 520f, MathF.Pow(u, 0.85f));
                counterPhase += TwoPi * sweepHz / SampleRate;
                float sweepEnv = SmoothAttack(u, 0.12f) * MathF.Pow(1f - u, 1.35f);
                counter = MathF.Sin(counterPhase) * 0.52f * sweepEnv;
            }

            float spark = filter.StepBandpass(NextWhiteNoise(ref rng), 3100f, 0.55f)
                          * MathF.Exp(-dt * 85f) * 0.48f;
            buf[i] = ring + counter + spark;
        }
        return buf;
    }

    private static float[] SynthesizeBossOverloadPulse(CombatSfxId seedId)
    {
        const float duration = 0.58f;
        int n = SampleCount(duration);
        var buf = new float[n];
        uint rng = SeedFor(seedId);
        var filter = new StateVariableFilter();

        float subPhase = 0f;
        float corePhase = 0f;
        float harmonicPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float dt = i / (float)SampleRate;
            float sample;

            if (dt < 0.18f)
            {
                float u = dt / 0.18f;
                float subHz = Mathf.Lerp(74f, 188f, u * u);
                subPhase += TwoPi * subHz / SampleRate;
                corePhase += TwoPi * (subHz * 2.02f) / SampleRate;
                float swell = SmoothAttack(u, 0.25f) * (0.35f + 0.65f * u);
                sample = (MathF.Sin(subPhase) * 0.72f + MathF.Sin(corePhase) * 0.34f) * swell;
            }
            else
            {
                float d = dt - 0.18f;
                float u = Math.Clamp(d / (duration - 0.18f), 0f, 1f);
                float dropHz = 42f + 128f * MathF.Exp(-d * 9.5f);
                subPhase += TwoPi * dropHz / SampleRate;
                corePhase += TwoPi * (dropHz * 1.5f) / SampleRate;
                harmonicPhase += TwoPi * (dropHz * 3.15f) / SampleRate;

                float boom = (MathF.Sin(subPhase) * 0.85f +
                              MathF.Sin(corePhase) * 0.36f +
                              MathF.Sin(harmonicPhase) * 0.18f) * MathF.Exp(-d * 4.6f);
                float crackleCutoff = Mathf.Lerp(1750f, 320f, u);
                float crackle = filter.StepBandpass(NextWhiteNoise(ref rng), crackleCutoff, 0.62f)
                                * MathF.Exp(-d * 8.5f) * 0.45f;
                sample = MathF.Tanh((boom + crackle) * 1.25f);
            }

            buf[i] = sample;
        }
        return buf;
    }

    private static short[] FinalizePcm16(float[] samples, float targetPeak, float attackSec, float releaseSec)
    {
        int n = samples.Length;
        if (n == 0) return Array.Empty<short>();

        // 1. Remove any residual DC offset before applying edge envelopes.
        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += samples[i];
        float mean = (float)(sum / n);
        for (int i = 0; i < n; i++)
            samples[i] -= mean;

        // 2. Smooth cosine attack/release ramps guarantee zero-valued endpoints and zero click/pop.
        int attackSamples = Math.Clamp((int)(attackSec * SampleRate), 16, n / 3);
        int releaseSamples = Math.Clamp((int)(releaseSec * SampleRate), 32, n / 2);

        for (int i = 0; i < attackSamples; i++)
        {
            float fade = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / attackSamples);
            samples[i] *= fade;
        }
        for (int i = 0; i < releaseSamples; i++)
        {
            int idx = n - 1 - i;
            float fade = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / releaseSamples);
            samples[idx] *= fade;
        }
        samples[0] = 0f;
        samples[n - 1] = 0f;

        // 3. Peak-normalize within safe headroom to prevent integer overflow or clipping.
        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float abs = MathF.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }

        float scale = peak > 1e-5f ? Math.Clamp(targetPeak, 0.1f, 0.90f) / peak : 0f;
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            float v = Math.Clamp(samples[i] * scale, -0.95f, 0.95f);
            pcm[i] = (short)MathF.Round(v * short.MaxValue);
        }
        return pcm;
    }

    private static byte[] EncodeLittleEndianPcm16(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            short s = pcm[i];
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    private static int SampleCount(float durationSec) => Math.Max(1, (int)(durationSec * SampleRate));

    private static float SmoothAttack(float t, float attackFraction)
    {
        if (attackFraction <= 0f || t >= attackFraction) return 1f;
        float u = Math.Clamp(t / attackFraction, 0f, 1f);
        return 0.5f - 0.5f * MathF.Cos(MathF.PI * u);
    }

    private static float PulseEnvelope(float dt, float startSec, float endSec, float edgeSec)
    {
        if (dt < startSec || dt > endSec) return 0f;
        float local = dt - startSec;
        float len = endSec - startSec;
        if (local < edgeSec)
            return 0.5f - 0.5f * MathF.Cos(MathF.PI * local / edgeSec);
        float rem = len - local;
        if (rem < edgeSec)
            return 0.5f - 0.5f * MathF.Cos(MathF.PI * rem / edgeSec);
        return 1f;
    }

    private static uint SeedFor(CombatSfxId id) => 0x9E3779B9u ^ ((uint)id + 1u) * 0x85EBCA6Bu;

    private static float NextWhiteNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state / (float)uint.MaxValue) * 2f - 1f;
    }

    private struct StateVariableFilter
    {
        private float _low;
        private float _band;

        public float StepBandpass(float input, float cutoffHz, float q)
        {
            Step(input, cutoffHz, q);
            return _band;
        }

        public float StepLowpass(float input, float cutoffHz, float q)
        {
            Step(input, cutoffHz, q);
            return _low;
        }

        private void Step(float input, float cutoffHz, float q)
        {
            // 2x oversampled Chamberlin SVF keeps high-frequency sweeps stable at 22050 Hz.
            float clampedHz = Math.Clamp(cutoffHz, 40f, SampleRate * 0.24f);
            float f = 2f * MathF.Sin(MathF.PI * clampedHz / (SampleRate * 2f));
            float damp = Math.Clamp(q, 0.25f, 1.5f);

            for (int step = 0; step < 2; step++)
            {
                float high = input - _low - damp * _band;
                _band += f * high;
                _low += f * _band;
            }
        }
    }
}
