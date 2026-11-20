using System;
using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Audio;

/// <summary>
/// Headless smoke verifier for procedural 16-bit PCM synthesis and read-only audio controller wiring.
/// </summary>
public static class ProceduralAudioSmoke
{
    private const int ExpectedCueCount = 13;
    private const int MinSampleCount = 1100;
    private const int MaxSampleCount = ProceduralSfxBank.SampleRate * 2;
    private const short MinPeakAmplitude = 2000;
    private const short MaxPeakAmplitude = 31500;
    private const double MinRmsEnergy = 200.0;
    private const short MaxEndpointAmplitude = 64;

    public static void Check(Node arena)
    {
        var failures = new List<string>();
        var cues = Enum.GetValues<CombatSfxId>();
        CheckSfxBank(cues, failures);
        CheckControllerWiring(arena, cues, failures);

        if (failures.Count == 0)
        {
            GD.Print($"[Smoke] PASS procedural audio bank & controller ({cues.Length} PCM16 cues, anti-pop, polyphonic pool, headless-safe)");
        }
        else
        {
            foreach (string failure in failures)
                GD.PrintErr($"[Smoke] FAIL procedural audio: {failure}");
        }
    }

    private static void CheckSfxBank(CombatSfxId[] cues, List<string> failures)
    {
        if (cues.Length != ExpectedCueCount)
            failures.Add($"expected {ExpectedCueCount} cues, got {cues.Length}");

        var pcmByCue = new Dictionary<CombatSfxId, short[]>(cues.Length);
        ProceduralSfxBank.Prewarm();

        foreach (var id in cues)
        {
            short[] pcm = ProceduralSfxBank.GeneratePcm16(id);
            pcmByCue[id] = pcm;
            if (pcm.Length < MinSampleCount || pcm.Length > MaxSampleCount)
            {
                failures.Add($"{id} invalid sample count {pcm.Length}");
                continue;
            }

            short peak = 0;
            double sumSquares = 0.0;
            for (int i = 0; i < pcm.Length; i++)
            {
                int abs = Math.Abs((int)pcm[i]);
                if (abs > peak)
                    peak = (short)Math.Min(abs, short.MaxValue);
                sumSquares += (double)pcm[i] * pcm[i];
            }

            double rms = Math.Sqrt(sumSquares / pcm.Length);
            if (peak < MinPeakAmplitude || peak > MaxPeakAmplitude)
                failures.Add($"{id} peak {peak} out of safe range [{MinPeakAmplitude}, {MaxPeakAmplitude}]");
            if (rms < MinRmsEnergy)
                failures.Add($"{id} RMS {rms:0.0} too quiet");
            if (Math.Abs((int)pcm[0]) > MaxEndpointAmplitude || Math.Abs((int)pcm[^1]) > MaxEndpointAmplitude)
                failures.Add($"{id} endpoint discontinuity ({pcm[0]}, {pcm[^1]})");

            AudioStreamWav stream = ProceduralSfxBank.GetStream(id);
            if (!ReferenceEquals(stream, ProceduralSfxBank.GetStream(id)))
                failures.Add($"{id} stream was not cached");
            if (stream.Format != AudioStreamWav.FormatEnum.Format16Bits)
                failures.Add($"{id} expected Format16Bits, got {stream.Format}");
            if (stream.MixRate != ProceduralSfxBank.SampleRate)
                failures.Add($"{id} expected MixRate {ProceduralSfxBank.SampleRate}, got {stream.MixRate}");
            if (stream.Stereo)
                failures.Add($"{id} expected mono (Stereo=false)");
            if (stream.LoopMode != AudioStreamWav.LoopModeEnum.Disabled)
                failures.Add($"{id} expected LoopMode Disabled, got {stream.LoopMode}");

            byte[] data = stream.Data;
            if (data.Length != pcm.Length * 2)
            {
                failures.Add($"{id} WAV byte length {data.Length} != {pcm.Length * 2}");
                continue;
            }

            if (!MatchesLittleEndianSample(data, 0, pcm[0]) ||
                !MatchesLittleEndianSample(data, pcm.Length / 2, pcm[pcm.Length / 2]) ||
                !MatchesLittleEndianSample(data, pcm.Length - 1, pcm[^1]))
            {
                failures.Add($"{id} little-endian PCM16 encoding mismatch");
            }
        }

        CheckDistinct(pcmByCue, CombatSfxId.PlayerSlash, CombatSfxId.PlayerSkillDash, failures);
        CheckDistinct(pcmByCue, CombatSfxId.PlayerHit, CombatSfxId.PlayerDamaged, failures);
        CheckDistinct(pcmByCue, CombatSfxId.BlockNormal, CombatSfxId.BlockPerfect, failures);
        CheckDistinct(pcmByCue, CombatSfxId.DodgeNormal, CombatSfxId.DodgePerfect, failures);
        CheckDistinct(pcmByCue, CombatSfxId.TelegraphNormal, CombatSfxId.TelegraphBreakGrab, failures);
        CheckDistinct(pcmByCue, CombatSfxId.TelegraphNormal, CombatSfxId.TelegraphDelayedArea, failures);
        CheckDistinct(pcmByCue, CombatSfxId.TelegraphBreakGrab, CombatSfxId.TelegraphDelayedArea, failures);
        CheckDistinct(pcmByCue, CombatSfxId.TimeSlowStart, CombatSfxId.TimeSlowEnd, failures);
    }

    private static void CheckControllerWiring(Node arena, CombatSfxId[] cues, List<string> failures)
    {
        var liveController = arena.GetNodeOrNull<CombatAudioController>("CombatAudio");
        if (liveController is null)
        {
            failures.Add("TestArena missing CombatAudio controller child");
            return;
        }

        if (!liveController.IsSilentMode)
            failures.Add("CombatAudioController should run in silent mode under headless/ACT_SMOKE");
        if (liveController.PoolCount < 4)
            failures.Add($"expected polyphonic pool >= 4 voices, got {liveController.PoolCount}");

        int playerChildren = 0;
        foreach (Node child in liveController.GetChildren())
        {
            if (child is AudioStreamPlayer)
                playerChildren++;
        }
        if (playerChildren != liveController.PoolCount)
            failures.Add($"pool child count mismatch: {playerChildren} AudioStreamPlayers vs PoolCount {liveController.PoolCount}");

        // Verify cue mapping contracts for defense results, telegraphs, and time slow messages.
        if (!CombatAudioController.TryMapDefenseCue(DefenseResult.Blocked, out var blockCue) || blockCue != CombatSfxId.BlockNormal)
            failures.Add("Blocked defense cue mismatch");
        if (!CombatAudioController.TryMapDefenseCue(DefenseResult.PerfectBlocked, out var pBlockCue) || pBlockCue != CombatSfxId.BlockPerfect)
            failures.Add("PerfectBlocked defense cue mismatch");
        if (!CombatAudioController.TryMapDefenseCue(DefenseResult.PerfectDodged, out var pDodgeCue) || pDodgeCue != CombatSfxId.DodgePerfect)
            failures.Add("PerfectDodged defense cue mismatch");
        if (!CombatAudioController.TryMapDefenseCue(DefenseResult.Hit, out var hitCue) || hitCue != CombatSfxId.PlayerDamaged)
            failures.Add("Hit defense cue mismatch");
        if (CombatAudioController.TryMapDefenseCue(DefenseResult.Dodged, out _))
            failures.Add("Dodged result should not re-trigger DodgeNormal after DodgeStarted");

        if (CombatAudioController.MapTelegraphCue(BossAttackKind.Normal) != CombatSfxId.TelegraphNormal ||
            CombatAudioController.MapTelegraphCue(BossAttackKind.BreakGrab) != CombatSfxId.TelegraphBreakGrab ||
            CombatAudioController.MapTelegraphCue(BossAttackKind.DelayedArea) != CombatSfxId.TelegraphDelayedArea)
        {
            failures.Add("Enemy telegraph cue mapping mismatch");
        }

        if (!CombatAudioController.TryMapTimeSlowCue("time slow started (x0.35)", out var tsStart) || tsStart != CombatSfxId.TimeSlowStart)
            failures.Add("TimeSlow start cue mapping mismatch");
        if (!CombatAudioController.TryMapTimeSlowCue("time slow ended: key released", out var tsEnd1) || tsEnd1 != CombatSfxId.TimeSlowEnd)
            failures.Add("TimeSlow key-released cue mapping mismatch");
        if (!CombatAudioController.TryMapTimeSlowCue("time slow ended: resource empty", out var tsEnd2) || tsEnd2 != CombatSfxId.TimeSlowEnd)
            failures.Add("TimeSlow resource-empty cue mapping mismatch");
        if (CombatAudioController.TryMapTimeSlowCue("time resource reset", out _))
            failures.Add("time resource reset should not trigger a sound cue");

        // Verify null-enemy attachment (TutorialStage / FormalStage), VolumeDb propagation, and clean _ExitTree teardown.
        var player = arena.GetNodeOrNull<PlayerController>("Player");
        var timeSlow = arena.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
        if (player is null || timeSlow is null)
        {
            failures.Add("arena missing Player or TimeSlowManager for null-enemy attach check");
            return;
        }

        var probeHost = new Node { Name = "AudioNullEnemyProbe" };
        arena.AddChild(probeHost);
        var probeController = CombatAudioController.Attach(probeHost, player, enemy: null, timeSlow);
        if (!probeController.IsSubscribed)
            failures.Add("probe CombatAudioController did not subscribe when attached");
        probeController.VolumeDb = -11f;

        foreach (Node child in probeController.GetChildren())
        {
            if (child is AudioStreamPlayer voice && !Mathf.IsEqualApprox(voice.VolumeDb, -11f))
            {
                failures.Add($"VolumeDb did not propagate to {voice.Name}: {voice.VolumeDb}");
                break;
            }
        }

        foreach (var cue in cues)
            probeController.Play(cue);

        if (probeController.PlayedCueCount != cues.Length || probeController.LastCue != cues[^1])
            failures.Add($"probe playback count/last cue mismatch: {probeController.PlayedCueCount}, {probeController.LastCue}");

        foreach (Node child in probeController.GetChildren())
        {
            if (child is AudioStreamPlayer voice && voice.Playing)
            {
                failures.Add($"voice {voice.Name} unexpectedly playing in silent headless mode");
                break;
            }
        }

        arena.RemoveChild(probeHost);
        if (probeController.IsSubscribed)
            failures.Add("CombatAudioController failed to unsubscribe in _ExitTree");
        probeHost.Free();
    }

    private static bool MatchesLittleEndianSample(byte[] data, int sampleIndex, short expected)
    {
        int byteIndex = sampleIndex * 2;
        short decoded = (short)(data[byteIndex] | (data[byteIndex + 1] << 8));
        return decoded == expected;
    }

    private static void CheckDistinct(
        Dictionary<CombatSfxId, short[]> pcmByCue,
        CombatSfxId a,
        CombatSfxId b,
        List<string> failures)
    {
        if (!pcmByCue.TryGetValue(a, out var sa) || !pcmByCue.TryGetValue(b, out var sb))
            return;
        if (sa.Length != sb.Length)
            return;
        for (int i = 0; i < sa.Length; i++)
        {
            if (sa[i] != sb[i])
                return;
        }
        failures.Add($"{a} and {b} produced identical waveforms");
    }
}
