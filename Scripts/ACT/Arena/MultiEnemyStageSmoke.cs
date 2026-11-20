using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless runtime smoke verifier for Issue #9:
/// multi-enemy StageEnemyRoster lifecycle, dynamic mount/destroy event forwarding,
/// independent visual/audio feedback, tiered HUD vs overhead health bars, and TrainingTarget.
/// </summary>
public static class MultiEnemyStageSmoke
{
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckRosterDynamicLifecycleAndBroadcast(host, failures);
        CheckMultiEnemyFeedbackAndAudio(host, failures);
        CheckTieredHealthBarContract(host, failures);
        CheckTrainingTargetAndTutorialsContract(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS multi-enemy stage roster, dynamic mount/destroy & tiered HUD/TrainingTarget contracts");
            return;
        }

        foreach (string reason in failures)
            fail($"multi-enemy: {reason}");
    }

    private static void CheckRosterDynamicLifecycleAndBroadcast(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "MultiEnemyRosterProbe" };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0.05f,
            NormalAttackActiveSeconds = 0.05f,
            NormalAttackRecoverySeconds = 0.05f,
            EnemyAttackRange = 300f
        };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(200f, 500f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var roster = new StageEnemyRoster { Name = "EnemyRoster" };
        roster.BindTimeSlow(timeSlow);

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(roster);
        host.AddChild(probeStage);

        var telegraphSources = new List<EnemyController>();
        var strikeSources = new List<EnemyController>();
        var damagedSources = new List<EnemyController>();

        roster.EnemyTelegraphStarted += cue => telegraphSources.Add(cue.Enemy);
        roster.EnemyStrikeActivated += cue => strikeSources.Add(cue.Enemy);
        roster.EnemyDamaged += cue => damagedSources.Add(cue.Enemy);

        try
        {
            // 0-enemy baseline
            if (roster.Count != 0 || roster.AliveCount != 0 || roster.PrimaryBoss is not null || roster.HasBoss)
                failures.Add($"0-enemy roster baseline invalid (count={roster.Count}, alive={roster.AliveCount}, hasBoss={roster.HasBoss})");

            // Dynamic runtime registration of 2 regular enemies
            var enemyA = new EnemyController { Name = "ScoutA", Position = new Vector2(260f, 500f), MaxHp = 4f, Windows = windows };
            var enemyB = new EnemyController { Name = "ScoutB", Position = new Vector2(320f, 500f), MaxHp = 4f, Windows = windows };
            probeStage.AddChild(enemyA);
            probeStage.AddChild(enemyB);
            enemyA.Bind(player, windows);
            enemyB.Bind(player, windows);

            if (!roster.Register(enemyA) || roster.Register(enemyA))
                failures.Add("StageEnemyRoster.Register must succeed once and be idempotent on duplicate");
            if (!roster.Register(enemyB))
                failures.Add("StageEnemyRoster.Register failed for second enemy");

            if (roster.Count != 2 || roster.AliveCount != 2 || roster.HasBoss)
                failures.Add($"2 regular enemies state mismatch (count={roster.Count}, alive={roster.AliveCount}, hasBoss={roster.HasBoss})");

            if (!enemyA.Visual.IsTimeSlowBound || !enemyB.Visual.IsTimeSlowBound)
                failures.Add("StageEnemyRoster did not propagate TimeSlowManager binding to dynamically registered enemies");

            // Step both enemies out of Spawn into Telegraph
            enemyA._PhysicsProcess(0.016); // Spawn -> Idle
            enemyB._PhysicsProcess(0.016); // Spawn -> Idle
            enemyA._PhysicsProcess(0.016); // Idle -> Telegraph
            enemyB._PhysicsProcess(0.016); // Idle -> Telegraph

            if (!telegraphSources.Contains(enemyA) || !telegraphSources.Contains(enemyB))
                failures.Add($"roster did not forward TelegraphStarted for both enemies (count={telegraphSources.Count})");

            // Damage both independently
            enemyA.TakeDamage(1f);
            enemyB.TakeDamage(1f);
            if (!damagedSources.Contains(enemyA) || !damagedSources.Contains(enemyB))
                failures.Add($"roster did not forward Damaged for both enemies (count={damagedSources.Count})");

            // Dynamic boss registration & auto-unregister on TreeExiting
            var boss = new EnemyController { Name = "DynamicBoss", Position = new Vector2(450f, 500f), MaxHp = 6f, IsBoss = true, Windows = windows };
            probeStage.AddChild(boss);
            boss.Bind(player, windows);
            roster.Register(boss);

            if (!roster.HasBoss || !ReferenceEquals(roster.PrimaryBoss, boss))
                failures.Add("roster did not promote dynamically registered boss to PrimaryBoss");

            probeStage.RemoveChild(enemyA);
            enemyA.Free();
            if (roster.Count != 2 || roster.Contains(enemyA))
                failures.Add($"TreeExiting did not auto-unregister destroyed enemyA (count={roster.Count})");

            probeStage.RemoveChild(boss);
            boss.Free();
            if (roster.HasBoss || roster.PrimaryBoss is not null || roster.Count != 1)
                failures.Add($"TreeExiting did not clear PrimaryBoss on boss destroy (hasBoss={roster.HasBoss}, count={roster.Count})");

            // Remaining enemyB still forwards events cleanly after enemyA & boss destroyed
            int dmgCountBefore = damagedSources.Count;
            enemyB.TakeDamage(1f);
            if (damagedSources.Count != dmgCountBefore + 1 || !ReferenceEquals(damagedSources[^1], enemyB))
                failures.Add("remaining enemyB failed to forward Damaged after sibling destruction");
        }
        finally
        {
            host.RemoveChild(probeStage);
            if (roster.Count != 0)
                failures.Add($"StageEnemyRoster did not clear all entries on _ExitTree (remaining={roster.Count})");
            probeStage.Free();
        }
    }

    private static void CheckMultiEnemyFeedbackAndAudio(Node host, List<string> failures)
    {
        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();
        var probeStage = new Node2D { Name = "MultiEnemyFeedbackAudioProbe" };
        var timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0.05f,
            NormalAttackRecoverySeconds = 0.05f,
            EnemyAttackRange = 300f
        };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(220f, 500f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var camera = new Camera2D { Name = "ProbeCamera" };
        player.AddChild(camera);

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);

        StageEnemyRoster roster = StageRuntimeWiring.AttachEnemyRoster(probeStage, timeSlow);
        StageRuntimeWiring.MountStagePresentationAndHud(
            probeStage,
            player,
            roster,
            timeSlow,
            camera,
            "多敌人探针");

        host.AddChild(probeStage);

        var feedback = probeStage.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer");
        var audio = probeStage.GetNodeOrNull<CombatAudioController>("CombatAudio");

        try
        {
            if (feedback is null || audio is null)
            {
                failures.Add("StageRuntimeWiring with roster did not mount CombatFeedbackLayer and CombatAudioController");
                return;
            }

            feedback.AllowVisualHitstopInSmoke = true;

            var enemyA = new EnemyController { Name = "EnemyA", Position = new Vector2(300f, 500f), MaxHp = 4f, Windows = windows };
            var enemyB = new EnemyController { Name = "EnemyB", Position = new Vector2(360f, 500f), MaxHp = 3f, Windows = windows };
            probeStage.AddChild(enemyA);
            probeStage.AddChild(enemyB);
            enemyA.Bind(player, windows);
            enemyB.Bind(player, windows);
            roster.Register(enemyA);
            roster.Register(enemyB);

            // 1. Independent telegraph audio + strike shockwave rings for both enemies
            int audioBefore = audio.PlayedCueCount;
            int ringsBefore = feedback.ActiveRingCount;

            enemyA._PhysicsProcess(0.016); // Spawn -> Idle
            enemyA._PhysicsProcess(0.016); // Idle -> Telegraph (plays TelegraphNormal)
            bool audioAfterA = audio.PlayedCueCount == audioBefore + 1 && audio.LastCue == CombatSfxId.TelegraphNormal;
            enemyA._PhysicsProcess(0.016); // Telegraph -> Strike (spawns ring)
            bool ringAfterA = feedback.ActiveRingCount > ringsBefore;

            int audioBeforeB = audio.PlayedCueCount;
            int ringsBeforeB = feedback.ActiveRingCount;
            enemyB._PhysicsProcess(0.016); // Spawn -> Idle
            enemyB._PhysicsProcess(0.016); // Idle -> Telegraph
            bool audioAfterB = audio.PlayedCueCount == audioBeforeB + 1 && audio.LastCue == CombatSfxId.TelegraphNormal;
            enemyB._PhysicsProcess(0.016); // Telegraph -> Strike
            bool ringAfterB = feedback.ActiveRingCount > ringsBeforeB;

            if (!audioAfterA || !ringAfterA || !audioAfterB || !ringAfterB)
                failures.Add($"multi-enemy telegraph/strike cues mismatch: audioA={audioAfterA} ringA={ringAfterA} audioB={audioAfterB} ringB={ringAfterB}");

            // 2. Per-enemy local visual hitstop on EnemyA does NOT freeze EnemyB and never touches Engine.TimeScale
            int particlesBefore = feedback.ActiveParticleCount;
            enemyA.TakeDamage(1f);
            bool hitstopIsolated =
                feedback.ActiveParticleCount > particlesBefore &&
                enemyA.Visual.IsVisualFrozen &&
                !enemyB.Visual.IsVisualFrozen &&
                feedback.IsEnemyHitstopActive(enemyA) &&
                !feedback.IsEnemyHitstopActive(enemyB) &&
                Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);

            if (!hitstopIsolated)
                failures.Add($"local visual hitstop isolation failed: frozenA={enemyA.Visual.IsVisualFrozen} frozenB={enemyB.Visual.IsVisualFrozen} scale={Engine.TimeScale:0.00}");

            // Advance real time to clear EnemyA visual freeze cleanly (first _Process call consumes delta directly)
            feedback._Process(0.06);
            if (enemyA.Visual.IsVisualFrozen || feedback.IsEnemyHitstopActive(enemyA))
                failures.Add("EnemyA visual freeze did not expire cleanly");

            // 3. Fatal hit on EnemyB spawns defeat ring + particles, and destroying EnemyB mid-hitstop leaks nothing
            int ringsBeforeDefeat = feedback.ActiveRingCount;
            int particlesBeforeDefeat = feedback.ActiveParticleCount;
            enemyB.TakeDamage(enemyB.MaxHp);
            bool defeatVfx = feedback.ActiveRingCount > ringsBeforeDefeat &&
                             feedback.ActiveParticleCount > particlesBeforeDefeat &&
                             enemyB.Visual.IsVisualFrozen &&
                             !enemyA.Visual.IsVisualFrozen;
            if (!defeatVfx)
                failures.Add("EnemyB defeat VFX or isolated hitstop failed");

            probeStage.RemoveChild(enemyB);
            enemyB.Free();
            if (roster.Contains(enemyB) || feedback.IsEnemyHitstopActive(enemyB) || feedback.IsHitstopActive)
                failures.Add("destroying EnemyB mid-hitstop leaked roster or hitstop state");

            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                failures.Add($"multi-enemy feedback mutated Engine.TimeScale ({Engine.TimeScale:0.000})");
        }
        finally
        {
            host.RemoveChild(probeStage);
            if (audio is not null && audio.IsSubscribed)
                failures.Add("CombatAudioController remained subscribed after multi-enemy stage teardown");
            if (roster.Count != 0)
                failures.Add("StageEnemyRoster remained non-empty after multi-enemy stage teardown");
            if (!camera.Offset.IsEqualApprox(Vector2.Zero))
                failures.Add("Camera2D.Offset was not reset to zero on multi-enemy stage teardown");
            probeStage.Free();
            previousCamera?.MakeCurrent();
        }
    }

    private static void CheckTieredHealthBarContract(Node host, List<string> failures)
    {
        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();

        // 1. Live CoreLoop boss arena: bottom boss bar visible for 遗迹守卫, no overhead bar on boss
        var coreRoster = host.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
        var coreHud = host.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
        if (coreRoster is null || coreHud is null)
        {
            failures.Add("CoreLoop missing EnemyRoster or HUDLayer/HUD for tiered health bar check");
        }
        else
        {
            EnemyController? boss = coreRoster.PrimaryBoss;
            if (boss is null || !boss.IsBoss)
                failures.Add("CoreLoop EnemyRoster missing boss-tier PrimaryBoss");
            if (!coreHud.IsBossHealthBarVisible || coreHud.ActiveBossDisplayName != "遗迹守卫 · 巨构核心")
                failures.Add($"CoreLoop boss HUD bar mismatch: visible={coreHud.IsBossHealthBarVisible} title='{coreHud.ActiveBossDisplayName}'");
            if (boss is not null && boss.Visual.ShouldDrawOverheadHealthBar)
                failures.Add("CoreLoop boss should not draw overhead narrow health bar");
        }

        // 2. FormalEncounter: non-boss enemy uses overhead bar (hidden on defeat), NO bottom boss bar
        var encounterPacked = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
        if (encounterPacked is null)
        {
            failures.Add("failed to load FormalEncounter.tscn for tiered health bar check");
        }
        else
        {
            Node stage = encounterPacked.Instantiate();
            host.AddChild(stage);
            try
            {
                var roster = stage.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
                var hud = stage.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
                var player = stage.GetNodeOrNull<PlayerController>("Player");
                if (roster is null || hud is null || player is null || roster.Count == 0)
                {
                    failures.Add("FormalEncounter missing EnemyRoster/HUD/Player or initial enemy");
                }
                else
                {
                    EnemyController encounterEnemy = roster.Enemies[0];
                    if (encounterEnemy.IsBoss || roster.HasBoss || hud.IsBossHealthBarVisible)
                        failures.Add($"FormalEncounter non-boss enemy unexpectedly activated bottom boss bar (IsBoss={encounterEnemy.IsBoss}, HudVisible={hud.IsBossHealthBarVisible})");
                    if (!encounterEnemy.Visual.ShouldDrawOverheadHealthBar)
                        failures.Add("FormalEncounter non-boss enemy should draw overhead narrow health bar while alive");

                    encounterEnemy.Bind(player, new CombatWindowConfig { BossSpawnSeconds = 0f });
                    encounterEnemy._PhysicsProcess(0.016);
                    encounterEnemy.TakeDamage(encounterEnemy.MaxHp);
                    if (encounterEnemy.Visual.ShouldDrawOverheadHealthBar)
                        failures.Add("FormalEncounter non-boss enemy overhead health bar stayed visible after defeat");
                }
            }
            finally
            {
                host.RemoveChild(stage);
                stage.Free();
            }
        }

        // 3. Isolated dynamic HUD probe: 0 -> 2 non-boss -> +1 boss -> defeat boss
        var probeStage = new Node2D { Name = "TieredHudDynamicProbe" };
        var timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        var windows = new CombatWindowConfig { BossSpawnSeconds = 0f };
        var playerProbe = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(220f, 500f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var camera = new Camera2D { Name = "ProbeCam" };
        playerProbe.AddChild(camera);
        probeStage.AddChild(timeSlow);
        probeStage.AddChild(playerProbe);
        StageEnemyRoster dynamicRoster = StageRuntimeWiring.AttachEnemyRoster(probeStage, timeSlow);
        CombatHud dynamicHud = StageRuntimeWiring.MountStagePresentationAndHud(
            probeStage,
            playerProbe,
            dynamicRoster,
            timeSlow,
            camera,
            "分级血条探针");
        host.AddChild(probeStage);

        try
        {
            if (dynamicHud.IsBossHealthBarVisible)
                failures.Add("0-enemy HUD should not display bottom boss health bar");

            var mob1 = new EnemyController { Name = "Mob1", Position = new Vector2(360f, 500f), MaxHp = 3f, IsBoss = false, Windows = windows };
            var mob2 = new EnemyController { Name = "Mob2", Position = new Vector2(440f, 500f), MaxHp = 3f, IsBoss = false, Windows = windows };
            probeStage.AddChild(mob1);
            probeStage.AddChild(mob2);
            mob1.Bind(playerProbe, windows);
            mob2.Bind(playerProbe, windows);
            dynamicRoster.Register(mob1);
            dynamicRoster.Register(mob2);

            if (dynamicHud.IsBossHealthBarVisible ||
                !mob1.Visual.ShouldDrawOverheadHealthBar ||
                !mob2.Visual.ShouldDrawOverheadHealthBar)
            {
                failures.Add("multi-mob HUD/overhead health bar contract mismatch");
            }

            var boss = new EnemyController
            {
                Name = "RuinGuardianProbe",
                DisplayName = "遗迹守卫",
                Position = new Vector2(600f, 500f),
                MaxHp = 10f,
                IsBoss = true,
                Windows = windows
            };
            probeStage.AddChild(boss);
            boss.Bind(playerProbe, windows);
            boss._PhysicsProcess(0.016);
            dynamicRoster.Register(boss);

            if (!dynamicHud.IsBossHealthBarVisible ||
                dynamicHud.ActiveBossDisplayName != "遗迹守卫" ||
                boss.Visual.ShouldDrawOverheadHealthBar)
            {
                failures.Add($"dynamic boss bar contract mismatch: visible={dynamicHud.IsBossHealthBarVisible} title='{dynamicHud.ActiveBossDisplayName}' overhead={boss.Visual.ShouldDrawOverheadHealthBar}");
            }

            dynamicHud.DebugOverlayVisible = true;
            dynamicHud.QueueRedraw();
            dynamicHud.DebugOverlayVisible = false;

            boss.TakeDamage(boss.MaxHp);
            if (dynamicHud.IsBossHealthBarVisible)
                failures.Add("bottom boss bar remained visible after boss defeat");

            // Dynamically registering a second live boss while the first defeated boss is still in-tree switches HUD to the live boss
            var secondBoss = new EnemyController
            {
                Name = "RuinGuardianSecondProbe",
                DisplayName = "遗迹守卫 · 续战",
                Position = new Vector2(680f, 500f),
                MaxHp = 12f,
                IsBoss = true,
                Windows = windows
            };
            probeStage.AddChild(secondBoss);
            secondBoss.Bind(playerProbe, windows);
            secondBoss._PhysicsProcess(0.016);
            dynamicRoster.Register(secondBoss);

            if (!dynamicHud.IsBossHealthBarVisible ||
                !ReferenceEquals(dynamicHud.ActiveBoss, secondBoss) ||
                dynamicHud.ActiveBossDisplayName != "遗迹守卫 · 续战")
            {
                failures.Add($"second live boss did not take over bottom HUD bar (visible={dynamicHud.IsBossHealthBarVisible}, title='{dynamicHud.ActiveBossDisplayName}')");
            }
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
            previousCamera?.MakeCurrent();
        }
    }

    private static void CheckTrainingTargetAndTutorialsContract(Node host, List<string> failures)
    {
        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();

        (string Path, bool ExpectTarget, string ExpectedGoal, string ExpectedNext)[] tutorials =
        {
            ("res://Scenes/Tutorial/Movement.tscn", false, "移动左右，然后完成一次跳跃", "res://Scenes/Tutorial/Attack.tscn"),
            ("res://Scenes/Tutorial/Attack.tscn", true, "对训练目标完成 3 次普通攻击命中", "res://Scenes/Tutorial/Skill.tscn"),
            ("res://Scenes/Tutorial/Skill.tscn", false, "释放 3 次技能，再用技能位移通过路线", "res://Scenes/Tutorial/Defense.tscn"),
            ("res://Scenes/Tutorial/Defense.tscn", true, "完成 3 次格挡", "res://Scenes/Tutorial/Dodge.tscn"),
            ("res://Scenes/Tutorial/Dodge.tscn", true, "完成 3 次闪避", "res://Scenes/Tutorial/PerfectDefense.tscn"),
            ("res://Scenes/Tutorial/PerfectDefense.tscn", true, "捕捉前摇信号完成完美格挡与完美闪避（金色可格挡，猩红须闪避）", "res://Scenes/Tutorial/TimeSlow.tscn"),
            ("res://Scenes/Tutorial/TimeSlow.tscn", false, "按住 Q 使用时缓，直到资源开始消耗", "res://Scenes/Tutorial/Combat.tscn"),
            ("res://Scenes/Tutorial/Combat.tscn", true, "击败场景内的训练敌人", "res://Scenes/FormalRoute.tscn")
        };

        foreach (var (path, expectTarget, expectedGoal, expectedNext) in tutorials)
        {
            var packed = GD.Load<PackedScene>(path);
            if (packed is null)
            {
                failures.Add($"failed to load {path}");
                continue;
            }

            Node stage = packed.Instantiate();
            host.AddChild(stage);
            try
            {
                if (stage is not TutorialStage tutorial)
                {
                    failures.Add($"{path} root is not TutorialStage");
                    continue;
                }

                var roster = tutorial.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
                var hud = tutorial.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
                var player = tutorial.GetNodeOrNull<PlayerController>("Player");
                if (roster is null || hud is null || player is null)
                {
                    failures.Add($"{path} missing EnemyRoster/HUD/Player");
                    continue;
                }

                if (hud.ArenaMessage != expectedGoal)
                    failures.Add($"{path} ArenaMessage expected '{expectedGoal}', got '{hud.ArenaMessage}'");
                if (tutorial.NextScene != expectedNext)
                    failures.Add($"{path} NextScene expected '{expectedNext}', got '{tutorial.NextScene}'");
                if (hud.IsBossHealthBarVisible)
                    failures.Add($"{path} unexpectedly showed bottom boss health bar");

                if (!expectTarget)
                {
                    if (roster.Count != 0)
                        failures.Add($"{path} non-combat tutorial expected 0 enemies, got {roster.Count}");
                    continue;
                }

                if (roster.Count != 1 || roster.Enemies[0] is not TrainingTarget target)
                {
                    failures.Add($"{path} expected single TrainingTarget in EnemyRoster");
                    continue;
                }

                if (target.IsBoss || target.DisplayName != "演练靶标" || target.DisplayName.Contains("遗迹守卫", StringComparison.Ordinal))
                    failures.Add($"{path} TrainingTarget identity mismatch: IsBoss={target.IsBoss} DisplayName='{target.DisplayName}'");

                if (target.Visual is not TrainingTargetVisual targetVisual)
                    failures.Add($"{path} TrainingTarget did not mount TrainingTargetVisual");
                else if (!targetVisual.ShouldDrawOverheadHealthBar || !targetVisual.IsTimeSlowBound)
                    failures.Add($"{path} TrainingTargetVisual overhead/TimeSlow contract invalid");

                // Combat tutorial completion regression check: defeating TrainingTarget hides overhead bar & clears objective
                if (tutorial.Kind == TutorialKind.Combat)
                {
                    target.Bind(player, new CombatWindowConfig { BossSpawnSeconds = 0f });
                    target._PhysicsProcess(0.016);
                    target.TakeDamage(target.MaxHp);
                    tutorial._Process(0.016);
                    if (target.Visual.ShouldDrawOverheadHealthBar || !hud.IsObjectiveCleared)
                        failures.Add($"Combat tutorial completion mismatch: overheadVisible={target.Visual.ShouldDrawOverheadHealthBar} cleared={hud.IsObjectiveCleared}");
                }
            }
            finally
            {
                host.RemoveChild(stage);
                stage.Free();
            }
        }

        // Verify TrainingTarget stays stationary and alternates Normal (gold) / BreakGrab (crimson) telegraphs
        var targetProbeStage = new Node2D { Name = "TrainingTargetCycleProbe" };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0f,
            NormalAttackRecoverySeconds = 0f,
            BreakGrabTelegraphSeconds = 0f,
            BreakGrabActiveSeconds = 0f,
            BreakGrabRecoverySeconds = 0f,
            EnemyAttackRange = 220f
        };
        var probePlayer = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(460f, 510f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var probeTarget = new TrainingTarget
        {
            Name = "TrainingTarget",
            Position = new Vector2(620f, 510f),
            MaxHp = 99f
        };
        var telegraphedKinds = new List<BossAttackKind>();
        probeTarget.TelegraphStarted += cue => telegraphedKinds.Add(cue.Attack.Kind);

        targetProbeStage.AddChild(probePlayer);
        targetProbeStage.AddChild(probeTarget);
        host.AddChild(targetProbeStage);

        try
        {
            probeTarget.Bind(probePlayer, windows);
            float startX = probeTarget.Position.X;

            // Step through 2 complete attack cycles synchronously
            for (int step = 0; step < 12 && telegraphedKinds.Count < 2; step++)
                probeTarget._PhysicsProcess(0.016);

            bool movedHorizontally = Mathf.Abs(probeTarget.Position.X - startX) > 0.5f;
            if (movedHorizontally)
                failures.Add($"TrainingTarget moved horizontally ({startX:0.0} -> {probeTarget.Position.X:0.0})");
            if (telegraphedKinds.Count < 2 ||
                telegraphedKinds[0] != BossAttackKind.Normal ||
                telegraphedKinds[1] != BossAttackKind.BreakGrab)
            {
                failures.Add($"TrainingTarget telegraph sequence expected [Normal, BreakGrab], got [{string.Join(", ", telegraphedKinds)}]");
            }
        }
        finally
        {
            host.RemoveChild(targetProbeStage);
            targetProbeStage.Free();
            previousCamera?.MakeCurrent();
        }
    }
}
