using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Full-chain headless smoke verifier for Ticket #7:
/// verifies Title -> ACT_SMOKE=1 -> CoreLoop entry, all 13 .tscn scenes loading/instantiating/tearing down
/// without leaks, scene progression links, non-empty procedural PCM16 buffers, and exit/reset TimeScale hygiene.
/// </summary>
public static class FullChainSmoke
{
    private sealed record SceneSpec(
        string Path,
        Type ExpectedRootType,
        bool ExpectGameplayNodes,
        string? ExpectedNextScene = null);

    private static readonly SceneSpec[] CanonicalScenes =
    {
        new("res://Scenes/Title.tscn", typeof(TitleScreen), ExpectGameplayNodes: false),
        new("res://Scenes/Tutorial/Movement.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/Attack.tscn"),
        new("res://Scenes/Tutorial/Attack.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/Skill.tscn"),
        new("res://Scenes/Tutorial/Skill.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/Defense.tscn"),
        new("res://Scenes/Tutorial/Defense.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/Dodge.tscn"),
        new("res://Scenes/Tutorial/Dodge.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/PerfectDefense.tscn"),
        new("res://Scenes/Tutorial/PerfectDefense.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/TimeSlow.tscn"),
        new("res://Scenes/Tutorial/TimeSlow.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/Tutorial/Combat.tscn"),
        new("res://Scenes/Tutorial/Combat.tscn", typeof(TutorialStage), ExpectGameplayNodes: true, "res://Scenes/FormalRoute.tscn"),
        new("res://Scenes/FormalRoute.tscn", typeof(FormalStage), ExpectGameplayNodes: true, "res://Scenes/FormalEncounter.tscn"),
        new("res://Scenes/FormalEncounter.tscn", typeof(FormalStage), ExpectGameplayNodes: true, "res://Scenes/CoreLoop.tscn"),
        new("res://Scenes/CoreLoop.tscn", typeof(TestArena), ExpectGameplayNodes: true),
        new("res://Scenes/Victory.tscn", typeof(VictoryScreen), ExpectGameplayNodes: false)
    };

    private static bool _checkingScenes;

    /// <summary>Runs full-chain scene, audio buffer, and entry verification on startup.</summary>
    public static void Check(Node host, Action<string> fail)
    {
        if (_checkingScenes)
        {
            fail("full-chain: recursive SmokeTest spawned during child scene probe");
            return;
        }

        _checkingScenes = true;
        var failures = new List<string>();
        try
        {
            CheckTitleSmokeEntry(failures);
            CheckSceneInventoryCoverage(failures);
            CheckAllScenesInstantiateAndTeardown(host, failures);
            CheckStageTeardownResetsTimeScale(host, failures);
            CheckProceduralAudioBuffersNonEmpty(failures);
        }
        finally
        {
            _checkingScenes = false;
        }

        if (failures.Count == 0)
        {
            GD.Print($"[Smoke] PASS full-chain ({CanonicalScenes.Length} scenes, Title->CoreLoop smoke entry, PCM buffers non-empty, TimeScale=1.0)");
            return;
        }

        foreach (string reason in failures)
            fail($"full-chain: {reason}");
    }

    /// <summary>Verifies no Engine.TimeScale or pause-state leak remains before reporting ALL PASS or exiting.</summary>
    public static void CheckExitHygiene(Node host, TimeSlowManager timeSlow, Action<string> fail)
    {
        if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            fail($"full-chain exit hygiene: Engine.TimeScale leaked at {Engine.TimeScale:0.000}");

        if (host.GetTree().Paused)
            fail("full-chain exit hygiene: SceneTree remained paused");

        if (timeSlow.Active)
            fail("full-chain exit hygiene: TimeSlowManager remained active");

        if (!TitleScreen.EnteredFromTitleInSmoke)
            fail("full-chain exit hygiene: did not enter CoreLoop from Title.tscn under ACT_SMOKE=1");
    }

    /// <summary>Verifies a freshly reset arena restored clean Engine.TimeScale, pause state, and actor/resource state.</summary>
    public static bool CheckPostResetHygiene(
        Node arena,
        PlayerController player,
        Enemy.EnemyController enemy,
        TimeSlowManager timeSlow,
        out string? failureReason)
    {
        if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
        {
            failureReason = $"Engine.TimeScale leaked after R reset ({Engine.TimeScale:0.000})";
            return false;
        }

        if (arena.GetTree().Paused)
        {
            failureReason = "SceneTree remained paused after R reset";
            return false;
        }

        if (player.IsDown || !Mathf.IsEqualApprox(player.Hp, player.MaxHp))
        {
            failureReason = $"player state not reset cleanly (IsDown={player.IsDown}, Hp={player.Hp}/{player.MaxHp})";
            return false;
        }

        if (enemy.IsDefeated || !Mathf.IsEqualApprox(enemy.Hp, enemy.MaxHp))
        {
            failureReason = $"enemy state not reset cleanly (IsDefeated={enemy.IsDefeated}, Hp={enemy.Hp}/{enemy.MaxHp})";
            return false;
        }

        if (enemy is RuinGuardianBoss guardian &&
            (guardian.Phase != 1 || guardian.IsPhaseTwo || guardian.IsInvulnerable))
        {
            failureReason = $"RuinGuardianBoss phase state not reset cleanly (Phase={guardian.Phase}, Invuln={guardian.IsInvulnerable})";
            return false;
        }

        if (timeSlow.Active || !Mathf.IsEqualApprox(timeSlow.Resource, timeSlow.Config.MaxResource))
        {
            failureReason = $"TimeSlow not reset cleanly (Active={timeSlow.Active}, Resource={timeSlow.Resource})";
            return false;
        }

        if (arena.GetNodeOrNull<StageEnemyRoster>("EnemyRoster") is { } roster &&
            roster.AttackCoordinator.TotalActiveCount != 0)
        {
            failureReason = $"AttackCoordinator leaked active tokens after R reset (count={roster.AttackCoordinator.TotalActiveCount})";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static void CheckTitleSmokeEntry(List<string> failures)
    {
        string mainScene = ProjectSettings.GetSetting("application/run/main_scene").AsString();
        if (mainScene != "res://Scenes/Title.tscn")
            failures.Add($"main_scene expected 'res://Scenes/Title.tscn', got '{mainScene}'");

        if (!TitleScreen.EnteredFromTitleInSmoke)
            failures.Add("TitleScreen did not redirect into CoreLoop under ACT_SMOKE=1");
    }

    private static void CheckSceneInventoryCoverage(List<string> failures)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        CollectSceneFiles("res://Scenes", discovered);

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneSpec spec in CanonicalScenes)
            expected.Add(spec.Path);

        foreach (string path in discovered)
        {
            if (!expected.Contains(path))
                failures.Add($"scene file on disk not covered by FullChainSmoke: {path}");
        }

        foreach (string path in expected)
        {
            if (!discovered.Contains(path))
                failures.Add($"canonical scene missing from disk: {path}");
        }
    }

    private static void CollectSceneFiles(string dirPath, HashSet<string> results)
    {
        using DirAccess? dir = DirAccess.Open(dirPath);
        if (dir is null) return;

        dir.ListDirBegin();
        while (true)
        {
            string entry = dir.GetNext();
            if (string.IsNullOrEmpty(entry)) break;
            if (entry.StartsWith(".", StringComparison.Ordinal)) continue;

            string fullPath = $"{dirPath}/{entry}";
            if (dir.CurrentIsDir())
            {
                CollectSceneFiles(fullPath, results);
                continue;
            }

            if (entry.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
                results.Add(fullPath);
        }
        dir.ListDirEnd();
    }

    private static void CheckAllScenesInstantiateAndTeardown(Node host, List<string> failures)
    {
        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();
        var probeHost = new Node { Name = "FullChainSceneProbeHost" };
        host.AddChild(probeHost);

        try
        {
            foreach (SceneSpec spec in CanonicalScenes)
            {
                var packed = GD.Load<PackedScene>(spec.Path);
                if (packed is null)
                {
                    failures.Add($"failed to load PackedScene '{spec.Path}'");
                    continue;
                }

                Node instance = packed.Instantiate();
                try
                {
                    probeHost.AddChild(instance);

                    if (!spec.ExpectedRootType.IsInstanceOfType(instance))
                    {
                        failures.Add($"{spec.Path} root type expected {spec.ExpectedRootType.Name}, got {instance.GetType().Name}");
                    }

                    VerifyProgressionLink(spec, instance, failures);
                    VerifyMountedSceneChildren(spec, instance, failures);

                    if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                        failures.Add($"{spec.Path} mutated Engine.TimeScale on mount ({Engine.TimeScale:0.000})");

                    if (probeHost.GetTree().Paused)
                        failures.Add($"{spec.Path} left SceneTree paused on mount");
                }
                catch (Exception ex)
                {
                    failures.Add($"{spec.Path} threw {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (instance.GetParent() == probeHost)
                        probeHost.RemoveChild(instance);
                    instance.Free();

                    if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                        failures.Add($"{spec.Path} leaked Engine.TimeScale on teardown ({Engine.TimeScale:0.000})");
                }
            }
        }
        finally
        {
            if (probeHost.GetParent() == host)
                host.RemoveChild(probeHost);
            probeHost.Free();
            previousCamera?.MakeCurrent();
        }
    }

    private static void VerifyProgressionLink(SceneSpec spec, Node instance, List<string> failures)
    {
        if (spec.ExpectedNextScene is null) return;

        string? actualNext = instance switch
        {
            TutorialStage tutorial => tutorial.NextScene,
            FormalStage formal => formal.NextScene,
            _ => null
        };

        if (actualNext != spec.ExpectedNextScene)
            failures.Add($"{spec.Path} NextScene expected '{spec.ExpectedNextScene}', got '{actualNext ?? "null"}'");
    }

    private static void VerifyMountedSceneChildren(SceneSpec spec, Node instance, List<string> failures)
    {
        if (instance.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment") is null)
            failures.Add($"{spec.Path} missing RuinEnvironmentLayer");

        if (instance is TestArena && instance.GetNodeOrNull<SmokeTest>("SmokeTest") is not null)
            failures.Add($"{spec.Path} probe unexpectedly spawned nested SmokeTest");

        if (!spec.ExpectGameplayNodes) return;

        if (instance.GetNodeOrNull<PlayerController>("Player") is null)
            failures.Add($"{spec.Path} missing PlayerController");
        if (instance.GetNodeOrNull<TimeSlowManager>("TimeSlowManager") is null)
            failures.Add($"{spec.Path} missing TimeSlowManager");
        if (instance.GetNodeOrNull<StageEnemyRoster>("EnemyRoster") is null)
            failures.Add($"{spec.Path} missing StageEnemyRoster");
        if (instance.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer") is null)
            failures.Add($"{spec.Path} missing CombatFeedbackLayer");
        if (instance.GetNodeOrNull<CombatHud>("HUDLayer/HUD") is null)
            failures.Add($"{spec.Path} missing HUDLayer/HUD");
        if (instance.GetNodeOrNull<PauseMenuOverlay>("PauseMenuLayer") is null)
            failures.Add($"{spec.Path} missing PauseMenuLayer");
        if (instance.GetNodeOrNull<CombatAudioController>("CombatAudio") is null)
            failures.Add($"{spec.Path} missing CombatAudioController");
    }

    private static void CheckStageTeardownResetsTimeScale(Node host, List<string> failures)
    {
        var packed = GD.Load<PackedScene>("res://Scenes/Tutorial/TimeSlow.tscn");
        if (packed is null)
        {
            failures.Add("failed to load TimeSlow.tscn for teardown TimeScale check");
            return;
        }

        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();
        Node stage = packed.Instantiate();
        host.AddChild(stage);
        try
        {
            var timeSlow = stage.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
            if (timeSlow is null)
            {
                failures.Add("TimeSlow.tscn missing TimeSlowManager");
                return;
            }

            Godot.Input.ParseInputEvent(new InputEventAction { Action = "time_slow", Pressed = true });
            Godot.Input.FlushBufferedEvents();
            timeSlow._Process(0.016);

            if (!timeSlow.Active || Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                failures.Add("TimeSlow.tscn probe failed to engage Engine.TimeScale");
        }
        finally
        {
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "time_slow", Pressed = false });
            Godot.Input.FlushBufferedEvents();
            if (stage.GetParent() == host)
                host.RemoveChild(stage);
            stage.Free();
            previousCamera?.MakeCurrent();
        }

        if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            failures.Add($"Engine.TimeScale leaked after TimeSlow stage teardown: {Engine.TimeScale:0.000}");
    }

    private static void CheckProceduralAudioBuffersNonEmpty(List<string> failures)
    {
        foreach (CombatSfxId id in Enum.GetValues<CombatSfxId>())
        {
            short[] pcm = ProceduralSfxBank.GeneratePcm16(id);
            AudioStreamWav stream = ProceduralSfxBank.GetStream(id);
            if (pcm.Length == 0 || stream.Data.Length == 0)
                failures.Add($"audio cue {id} produced empty buffer (pcm={pcm.Length}, wavBytes={stream.Data.Length})");
        }
    }
}
