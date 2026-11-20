using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless smoke verifier for Issue #16:
/// - Symmetric 96px elevated faceted platforms in 1280x720 FormalEncounter (Layer 2 colliders + lit top edges)
/// - Single normal jump & diagonal directional skill climbability + RangedArcher perch/backstep safety
/// - 3-wave lock-gate encounter progression, 2+2 attack token coordination, tiered HUD & gate unlock
/// - Cross-stage full-state recovery into CoreLoop (full HP, Guard, TimeSlow)
/// - Mid-encounter pause, reset, and teardown hygiene (enemies, wave timers, projectiles, TimeScale=1.0)
/// </summary>
public static partial class LockGateEncounterSmoke
{
    private const float ExpectedFloorY = 560f;
    private const float ExpectedElevation = 96f;
    private const float ExpectedPlatformTopY = ExpectedFloorY - ExpectedElevation; // 464f

    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckDualHighPlatformsGeometryAndPerch(host, failures);
        CheckThreeWaveOrchestrationTokensAndHud(host, failures);
        CheckCrossStageFullStateRecoveryIntoCoreLoop(host, failures);
        CheckPauseResetAndTeardownHygiene(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS lock-gate encounter (96px symmetric platforms, 3-wave orchestration, 2+2 tokens, tiered HUD, CoreLoop full recovery & pause/reset hygiene)");
            return;
        }

        foreach (string reason in failures)
            fail($"lock-gate-encounter: {reason}");
    }

    public static LockGateClimbProbe StartLiveClimbProbe(Node host, Action<string> fail)
    {
        var probe = new LockGateClimbProbe(fail) { Name = "LockGateClimbProbe" };
        host.AddChild(probe);
        return probe;
    }

    private static void CheckDualHighPlatformsGeometryAndPerch(Node host, List<string> failures)
    {
        var packed = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
        if (packed is null)
        {
            failures.Add("failed to load res://Scenes/FormalEncounter.tscn");
            return;
        }

        Camera2D? prevCam = host.GetViewport()?.GetCamera2D();
        Node stageNode = packed.Instantiate();
        host.AddChild(stageNode);
        try
        {
            var env = stageNode.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment");
            var player = stageNode.GetNodeOrNull<PlayerController>("Player");
            if (env is null || player is null)
            {
                failures.Add("FormalEncounter missing RuinEnvironment or Player");
                return;
            }

            // 1. Verify two symmetric elevated solid faceted platforms at Y = 464 (96px above floor 560) with lit top edges
            var highPlatforms = new List<RuinSurfaceEntry>();
            foreach (RuinSurfaceEntry surface in env.Surfaces)
            {
                if (Mathf.IsEqualApprox(surface.Rect.Position.Y, ExpectedPlatformTopY))
                    highPlatforms.Add(surface);
            }

            if (highPlatforms.Count != 2)
            {
                failures.Add($"expected 2 elevated platforms at Y={ExpectedPlatformTopY:0} (96px above floor), found {highPlatforms.Count}");
                return;
            }

            highPlatforms.Sort((a, b) => a.Rect.Position.X.CompareTo(b.Rect.Position.X));
            RuinSurfaceEntry leftPlat = highPlatforms[0];
            RuinSurfaceEntry rightPlat = highPlatforms[1];

            bool litTopEdges = leftPlat.DrawLitTopEdge && rightPlat.DrawLitTopEdge;
            bool symmetricSize = leftPlat.Rect.Size.IsEqualApprox(rightPlat.Rect.Size);
            bool symmetricX = Mathf.IsEqualApprox(leftPlat.Rect.Position.X + rightPlat.Rect.End.X, 1280f);
            bool wideEnoughForArcher = leftPlat.Rect.Size.X >= 180f && rightPlat.Rect.Size.X >= 180f;
            bool insideSingleScreen = leftPlat.Rect.Position.X >= 40f && rightPlat.Rect.End.X <= 1240f;

            if (!litTopEdges || !symmetricSize || !symmetricX || !wideEnoughForArcher || !insideSingleScreen)
            {
                failures.Add($"high platform geometry mismatch: left={leftPlat.Rect} right={rightPlat.Rect} lit={litTopEdges} symSize={symmetricSize} symX={symmetricX}");
            }

            // 2. Verify matching Layer-2 StaticBody2D colliders exist for both platforms
            bool hasLeftCollider = HasMatchingLayer2Collider(stageNode, leftPlat.Rect);
            bool hasRightCollider = HasMatchingLayer2Collider(stageNode, rightPlat.Rect);
            if (!hasLeftCollider || !hasRightCollider)
            {
                failures.Add($"high platform Layer-2 StaticBody2D colliders missing: left={hasLeftCollider} right={hasRightCollider}");
                return;
            }

            // 3. Verify RangedArcherEnemy can perch & backstep safely without falling off either platform
            float targetLandY = ExpectedPlatformTopY - 23f;
            var probeArcher = new RangedArcherEnemy
            {
                Name = "PlatformPerchProbeArcher",
                Position = new Vector2(rightPlat.Rect.Position.X + 80f, ExpectedPlatformTopY - 42f),
                CloseAlertRange = 190f,
                BackstepDurationSeconds = 0.25f,
                BackstepCooldownSeconds = 3.0f
            };
            stageNode.AddChild(probeArcher);
            try
            {
                player.ResetTo(new Vector2(rightPlat.Rect.Position.X + 15f, targetLandY));
                probeArcher.Bind(player, new CombatWindowConfig { BossSpawnSeconds = 0f, EnemyInitialDelaySeconds = 0f });
                float startX = probeArcher.GlobalPosition.X;
                for (int step = 0; step < 25; step++)
                    probeArcher._PhysicsProcess(0.016);

                bool retreatedSafely =
                    probeArcher.GlobalPosition.X > startX + 20f &&
                    probeArcher.GlobalPosition.X <= rightPlat.Rect.End.X &&
                    probeArcher.GlobalPosition.Y <= ExpectedPlatformTopY - 38f;
                if (!retreatedSafely)
                {
                    failures.Add($"RangedArcher perch/backstep unsafe on high platform: startX={startX:0.0} endPos={probeArcher.GlobalPosition} platRight={rightPlat.Rect.End.X:0.0}");
                }
            }
            finally
            {
                stageNode.RemoveChild(probeArcher);
                probeArcher.Free();
            }
        }
        finally
        {
            host.RemoveChild(stageNode);
            stageNode.Free();
            prevCam?.MakeCurrent();
        }
    }

    private static void CheckThreeWaveOrchestrationTokensAndHud(Node host, List<string> failures)
    {
        var packed = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
        if (packed is null)
        {
            failures.Add("failed to load res://Scenes/FormalEncounter.tscn for wave check");
            return;
        }

        Camera2D? prevCam = host.GetViewport()?.GetCamera2D();
        Node stageNode = packed.Instantiate();
        host.AddChild(stageNode);
        try
        {
            if (stageNode is not FormalStage formal)
            {
                failures.Add("FormalEncounter root is not FormalStage");
                return;
            }

            var encounter = formal.GetNodeOrNull<LockGateEncounterController>("LockGateEncounter");
            var roster = formal.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
            var hud = formal.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
            var env = formal.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment");
            var player = formal.GetNodeOrNull<PlayerController>("Player");
            var audio = formal.GetNodeOrNull<CombatAudioController>("CombatAudio");
            var feedback = formal.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer");

            if (encounter is null || roster is null || hud is null || env is null || player is null || audio is null || feedback is null)
            {
                failures.Add($"FormalEncounter missing core encounter nodes (encounter={encounter is not null}, roster={roster is not null}, hud={hud is not null})");
                return;
            }

            // Wave 1: 2 floor MeleeScoutEnemy, gate locked, HUD shows wave 1/3, no boss bar
            RuinGateState? gateState = env.GetGateState("Exit");
            if (gateState != RuinGateState.Locked ||
                encounter.CurrentWaveNumber != 1 ||
                encounter.TotalWaves != 3 ||
                encounter.IsEncounterCleared ||
                hud.IsObjectiveCleared ||
                hud.IsBossHealthBarVisible ||
                !hud.ArenaMessage.Contains("1/3", StringComparison.Ordinal))
            {
                failures.Add($"Wave 1 header/gate contract mismatch: gate={gateState} wave={encounter.CurrentWaveNumber}/{encounter.TotalWaves} cleared={encounter.IsEncounterCleared} hudCleared={hud.IsObjectiveCleared} bossBar={hud.IsBossHealthBarVisible} msg='{hud.ArenaMessage}'");
            }

            int w1Scouts = 0;
            foreach (EnemyController enemy in roster.Enemies)
            {
                if (enemy is MeleeScoutEnemy && !enemy.IsDefeated && !enemy.IsBoss &&
                    enemy.Visual.ShouldDrawOverheadHealthBar && enemy.Visual.IsTimeSlowBound &&
                    enemy.GlobalPosition.Y > ExpectedPlatformTopY)
                {
                    w1Scouts++;
                }
            }
            if (roster.AliveCount != 2 || w1Scouts != 2)
                failures.Add($"Wave 1 expected 2 living floor MeleeScoutEnemy with overhead bars, got alive={roster.AliveCount} scouts={w1Scouts}");

            // Defeat Wave 1 -> verify real-timestamp inter-wave delay keeps gate locked before Wave 2 spawns
            encounter.InterWaveDelaySeconds = 0.05f;
            DefeatAllAliveEnemies(roster, player);
            formal._Process(0.016);

            if (!encounter.IsWaitingForNextWave ||
                encounter.CurrentWaveNumber != 1 ||
                encounter.IsEncounterCleared ||
                env.GetGateState("Exit") != RuinGateState.Locked ||
                hud.IsObjectiveCleared)
            {
                failures.Add($"inter-wave delay after Wave 1 violated: waiting={encounter.IsWaitingForNextWave} wave={encounter.CurrentWaveNumber} gate={env.GetGateState("Exit")} cleared={hud.IsObjectiveCleared}");
            }

            OS.DelayMsec(65);
            formal._Process(0.016);

            // Wave 2: floor MeleeScoutEnemy + high-platform RangedArcherEnemy, gate still locked, HUD shows 2/3
            if (encounter.CurrentWaveNumber != 2 ||
                encounter.IsWaitingForNextWave ||
                encounter.IsEncounterCleared ||
                env.GetGateState("Exit") != RuinGateState.Locked ||
                hud.IsObjectiveCleared ||
                hud.IsBossHealthBarVisible ||
                !hud.ArenaMessage.Contains("2/3", StringComparison.Ordinal))
            {
                failures.Add($"Wave 2 transition mismatch: wave={encounter.CurrentWaveNumber} waiting={encounter.IsWaitingForNextWave} gate={env.GetGateState("Exit")} msg='{hud.ArenaMessage}'");
            }

            int w2Scouts = 0;
            int w2HighArchers = 0;
            foreach (EnemyController enemy in roster.Enemies)
            {
                if (enemy.IsDefeated) continue;
                if (enemy.IsBoss || !enemy.Visual.ShouldDrawOverheadHealthBar || !enemy.Visual.IsTimeSlowBound)
                    failures.Add($"Wave 2 enemy '{enemy.Name}' violated non-boss/overhead/TimeSlow contract");
                if (enemy is MeleeScoutEnemy && enemy.GlobalPosition.Y > ExpectedPlatformTopY)
                    w2Scouts++;
                if (enemy is RangedArcherEnemy && enemy.GlobalPosition.Y <= ExpectedPlatformTopY - 30f)
                    w2HighArchers++;
            }
            if (w2Scouts < 1 || w2HighArchers < 1)
                failures.Add($"Wave 2 expected floor MeleeScout + high-platform RangedArcher, got scouts={w2Scouts} highArchers={w2HighArchers}");

            // Defeat Wave 2 -> instant Wave 3 transition (mixed 3-archetype formation)
            encounter.InterWaveDelaySeconds = 0f;
            DefeatAllAliveEnemies(roster, player);
            formal._Process(0.016);

            if (encounter.CurrentWaveNumber != 3 ||
                encounter.IsEncounterCleared ||
                env.GetGateState("Exit") != RuinGateState.Locked ||
                hud.IsObjectiveCleared ||
                hud.IsBossHealthBarVisible ||
                !hud.ArenaMessage.Contains("3/3", StringComparison.Ordinal))
            {
                failures.Add($"Wave 3 transition mismatch: wave={encounter.CurrentWaveNumber} gate={env.GetGateState("Exit")} cleared={hud.IsObjectiveCleared} msg='{hud.ArenaMessage}'");
            }

            int w3Scouts = 0;
            int w3Shields = 0;
            int w3HighArchers = 0;
            foreach (EnemyController enemy in roster.Enemies)
            {
                if (enemy.IsDefeated) continue;
                if (enemy.IsBoss || !enemy.Visual.ShouldDrawOverheadHealthBar || !enemy.Visual.IsTimeSlowBound)
                    failures.Add($"Wave 3 enemy '{enemy.Name}' violated non-boss/overhead/TimeSlow contract");
                if (enemy is MeleeScoutEnemy && enemy.GlobalPosition.Y > ExpectedPlatformTopY)
                    w3Scouts++;
                else if (enemy is HeavyShieldEnemy && enemy.GlobalPosition.Y > ExpectedPlatformTopY)
                    w3Shields++;
                else if (enemy is RangedArcherEnemy && enemy.GlobalPosition.Y <= ExpectedPlatformTopY - 30f)
                    w3HighArchers++;
            }
            if (w3Scouts < 1 || w3Shields < 1 || w3HighArchers < 1)
                failures.Add($"Wave 3 expected floor MeleeScout + floor HeavyShield + high-platform RangedArcher, got scouts={w3Scouts} shields={w3Shields} highArchers={w3HighArchers}");

            // Verify dynamic Wave 3 enemies obey 2+2 attack token pools and drive audio + visual feedback
            var fastWindows = new CombatWindowConfig
            {
                BossSpawnSeconds = 0f,
                EnemyInitialDelaySeconds = 0f,
                EnemyAttackCooldown = 0f,
                EnemyAttackRange = 900f
            };
            player.ResetTo(new Vector2(640f, ExpectedFloorY - 23f));
            foreach (EnemyController enemy in roster.Enemies)
            {
                if (!enemy.IsDefeated)
                    enemy.Bind(player, fastWindows);
            }

            int audioBefore = audio.PlayedCueCount;
            int particlesBefore = feedback.ActiveParticleCount;
            int observedTelegraphTokens = 0;
            for (int step = 0; step < 12; step++)
            {
                foreach (EnemyController enemy in roster.Enemies)
                {
                    if (!enemy.IsDefeated)
                        enemy._PhysicsProcess(0.016);
                }

                if (roster.AttackCoordinator.ActiveMeleeCount > EncounterAttackCoordinator.DefaultMeleeCapacity ||
                    roster.AttackCoordinator.ActiveRangedCount > EncounterAttackCoordinator.DefaultRangedCapacity)
                {
                    failures.Add($"Wave 3 exceeded 2+2 token budget (melee={roster.AttackCoordinator.ActiveMeleeCount}, ranged={roster.AttackCoordinator.ActiveRangedCount})");
                }

                foreach (EnemyController enemy in roster.Enemies)
                {
                    if (!enemy.IsDefeated && enemy.State is EnemyState.Telegraph or EnemyState.Strike)
                    {
                        if (!enemy.HoldsAttackToken)
                            failures.Add($"Wave 3 enemy '{enemy.Name}' entered {enemy.State} without attack token");
                        else
                            observedTelegraphTokens++;
                    }
                }
            }

            // Verify Wave 3 enemy damage triggers audio + feedback particles
            DefeatAllAliveEnemies(roster, player);
            bool cuesFired = audio.PlayedCueCount > audioBefore && feedback.ActiveParticleCount > particlesBefore;
            if (observedTelegraphTokens == 0 || !cuesFired)
                failures.Add($"Wave 3 dynamic wiring mismatch: tokensObserved={observedTelegraphTokens} audioDelta={audio.PlayedCueCount - audioBefore} particlesDelta={feedback.ActiveParticleCount - particlesBefore}");

            // Wave 3 fully cleared -> exit gate unlocks and objective marks cleared
            formal._Process(0.016);
            if (!encounter.IsEncounterCleared ||
                !hud.IsObjectiveCleared ||
                env.GetGateState("Exit") != RuinGateState.Unlocked ||
                hud.IsBossHealthBarVisible)
            {
                failures.Add($"Wave 3 clear unlock mismatch: cleared={encounter.IsEncounterCleared} hudCleared={hud.IsObjectiveCleared} gate={env.GetGateState("Exit")} bossBar={hud.IsBossHealthBarVisible}");
            }
        }
        finally
        {
            host.RemoveChild(stageNode);
            stageNode.Free();
            prevCam?.MakeCurrent();
        }
    }

    private static void CheckCrossStageFullStateRecoveryIntoCoreLoop(Node host, List<string> failures)
    {
        var encounterPacked = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
        var coreLoopPacked = GD.Load<PackedScene>("res://Scenes/CoreLoop.tscn");
        if (encounterPacked is null || coreLoopPacked is null)
        {
            failures.Add("failed to load FormalEncounter.tscn or CoreLoop.tscn for cross-stage recovery check");
            return;
        }

        Camera2D? prevCam = host.GetViewport()?.GetCamera2D();
        RunSession? session = host.GetNodeOrNull<RunSession>("/root/RunSession");
        if (session is null)
        {
            failures.Add("missing /root/RunSession autoload");
            return;
        }

        bool prevHasState = session.HasPlayerState;
        float prevHp = session.PlayerHp;

        Node encounterNode = encounterPacked.Instantiate();
        host.AddChild(encounterNode);
        try
        {
            var formal = (FormalStage)encounterNode;
            var encounter = formal.GetNodeOrNull<LockGateEncounterController>("LockGateEncounter");
            var roster = formal.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
            var player = formal.GetNodeOrNull<PlayerController>("Player");
            var timeSlow = formal.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
            if (encounter is null || roster is null || player is null || timeSlow is null)
            {
                failures.Add("FormalEncounter missing required nodes for cross-stage recovery check");
                return;
            }

            // Simulate injured player with drained TimeSlow clearing all 3 waves
            session.SavePlayerHp(35f);
            player.RestoreHp(35f);
            timeSlow.AddResource(-65f);
            encounter.InterWaveDelaySeconds = 0f;

            for (int wave = 0; wave < 3; wave++)
            {
                DefeatAllAliveEnemies(roster, player);
                formal._Process(0.016);
            }

            // Cross the unlocked right exit gate into CoreLoop
            player.Position = new Vector2(formal.ActiveExitGateX + 12f, ExpectedFloorY - 23f);
            formal._Process(0.016);

            if (session.HasPlayerState || !Mathf.IsEqualApprox(session.PlayerHp, 100f))
            {
                failures.Add($"crossing FormalEncounter exit did not reset RunSession for full boss recovery (HasPlayerState={session.HasPlayerState}, PlayerHp={session.PlayerHp:0})");
            }
        }
        finally
        {
            host.RemoveChild(encounterNode);
            encounterNode.Free();
            prevCam?.MakeCurrent();
        }

        // Mount CoreLoop.tscn using the post-exit RunSession state and assert full HP, full Guard, full TimeSlow
        Node coreLoopNode = coreLoopPacked.Instantiate();
        host.AddChild(coreLoopNode);
        try
        {
            var bossPlayer = coreLoopNode.GetNodeOrNull<PlayerController>("Player");
            var bossTimeSlow = coreLoopNode.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
            if (bossPlayer is null || bossTimeSlow is null)
            {
                failures.Add("CoreLoop missing Player or TimeSlowManager for post-encounter full recovery check");
            }
            else if (!Mathf.IsEqualApprox(bossPlayer.Hp, bossPlayer.MaxHp) ||
                     !Mathf.IsEqualApprox(bossPlayer.Guard, bossPlayer.MaxGuard) ||
                     !Mathf.IsEqualApprox(bossTimeSlow.Resource, bossTimeSlow.Config.MaxResource) ||
                     !Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            {
                failures.Add($"CoreLoop did not start with full HP/Guard/TimeSlow after FormalEncounter exit: hp={bossPlayer.Hp}/{bossPlayer.MaxHp} guard={bossPlayer.Guard}/{bossPlayer.MaxGuard} ts={bossTimeSlow.Resource}/{bossTimeSlow.Config.MaxResource} scale={Engine.TimeScale:0.00}");
            }
        }
        finally
        {
            host.RemoveChild(coreLoopNode);
            coreLoopNode.Free();
            if (prevHasState)
                session.SavePlayerHp(prevHp);
            else
                session.Clear();
            prevCam?.MakeCurrent();
        }
    }

    private static void CheckPauseResetAndTeardownHygiene(Node host, List<string> failures)
    {
        var packed = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
        if (packed is null)
        {
            failures.Add("failed to load FormalEncounter.tscn for pause/reset hygiene check");
            return;
        }

        Camera2D? prevCam = host.GetViewport()?.GetCamera2D();
        Node stageNode = packed.Instantiate();
        host.AddChild(stageNode);

        StageEnemyRoster? roster = null;
        LockGateEncounterController? encounter = null;
        PauseMenuOverlay? pauseMenu = null;
        try
        {
            var formal = (FormalStage)stageNode;
            encounter = formal.GetNodeOrNull<LockGateEncounterController>("LockGateEncounter");
            roster = formal.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
            var player = formal.GetNodeOrNull<PlayerController>("Player");
            var timeSlow = formal.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
            pauseMenu = formal.GetNodeOrNull<PauseMenuOverlay>("PauseMenuLayer");

            if (encounter is null || roster is null || player is null || timeSlow is null || pauseMenu is null)
            {
                failures.Add("FormalEncounter missing required nodes for pause/reset hygiene check");
                return;
            }

            // 1. Mid-encounter pause during active TimeSlow + inter-wave delay
            SendAction("time_slow", true);
            timeSlow._Process(0.016);
            bool engagedSlow = timeSlow.Active && !Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);

            encounter.InterWaveDelaySeconds = 0.05f;
            DefeatAllAliveEnemies(roster, player);
            formal._Process(0.016);

            pauseMenu.Open();
            bool pausedScaleRestored =
                formal.GetTree().Paused &&
                Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f) &&
                !timeSlow.Active;

            OS.DelayMsec(65);
            formal._Process(0.016); // Must NOT advance inter-wave spawn while paused
            bool waveFrozenWhilePaused = encounter.CurrentWaveNumber == 1 && encounter.IsWaitingForNextWave;

            pauseMenu.Close();
            SendAction("time_slow", false);
            bool resumedCleanly = !formal.GetTree().Paused && Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);

            if (!engagedSlow || !pausedScaleRestored || !waveFrozenWhilePaused || !resumedCleanly)
            {
                failures.Add($"mid-encounter pause hygiene mismatch: engagedSlow={engagedSlow} pausedScale={pausedScaleRestored} waveFrozen={waveFrozenWhilePaused} resumed={resumedCleanly}");
            }

            // 2. Resume real-timestamp inter-wave timer into Wave 2 with live archer + projectile + active TimeSlow, then cleanup & teardown
            OS.DelayMsec(65);
            formal._Process(0.016);
            if (encounter.CurrentWaveNumber != 2 || roster.AliveCount < 2)
                failures.Add($"expected Wave 2 active after resuming inter-wave timer, got wave={encounter.CurrentWaveNumber} alive={roster.AliveCount}");

            RangedArcherEnemy? archer = null;
            foreach (EnemyController enemy in roster.Enemies)
            {
                if (enemy is RangedArcherEnemy candidate && !candidate.IsDefeated)
                {
                    archer = candidate;
                    break;
                }
            }

            var residualProjectile = new EnemyProjectile { Name = "ResidualEncounterProjectile" };
            residualProjectile.Initialize(
                archer?.MuzzleGlobalPosition ?? new Vector2(960f, 412f),
                Vector2.Left,
                EnemyProjectile.DefaultEnemySpeed,
                EnemyProjectile.DefaultEnemyDamage,
                archer);
            formal.AddChild(residualProjectile);

            SendAction("time_slow", true);
            timeSlow._Process(0.016);
            if (EnemyProjectile.CountActiveIn(formal) != 1 || !timeSlow.Active)
                failures.Add($"cleanup probe setup failed: projectiles={EnemyProjectile.CountActiveIn(formal)} timeSlowActive={timeSlow.Active}");

            // Simulate explicit encounter reset cleanup (enemies, wave timers, tokens, projectiles)
            encounter.Cleanup();
            if (EnemyProjectile.CountActiveIn(formal) != 0 ||
                roster.Count != 0 ||
                roster.AliveCount != 0 ||
                roster.AttackCoordinator.TotalActiveCount != 0 ||
                encounter.IsWaitingForNextWave)
            {
                failures.Add($"encounter.Cleanup() left residual state: proj={EnemyProjectile.CountActiveIn(formal)} roster={roster.Count} tokens={roster.AttackCoordinator.TotalActiveCount} waiting={encounter.IsWaitingForNextWave}");
            }
        }
        finally
        {
            SendAction("time_slow", false);
            if (pauseMenu is not null && GodotObject.IsInstanceValid(pauseMenu) && pauseMenu.IsPaused)
                pauseMenu.Close();
            host.RemoveChild(stageNode);
            stageNode.Free();
            prevCam?.MakeCurrent();
        }

        // 3. Direct tree exit mid-encounter with live projectile + active TimeSlow cleans up everything cleanly
        Node teardownStage = packed.Instantiate();
        host.AddChild(teardownStage);
        try
        {
            var tdTimeSlow = teardownStage.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
            var tdProj = new EnemyProjectile { Name = "TeardownProbeProjectile" };
            tdProj.Initialize(new Vector2(960f, 412f), Vector2.Left, EnemyProjectile.DefaultEnemySpeed, EnemyProjectile.DefaultEnemyDamage);
            teardownStage.AddChild(tdProj);

            if (tdTimeSlow is not null)
            {
                SendAction("time_slow", true);
                tdTimeSlow._Process(0.016);
            }
        }
        finally
        {
            SendAction("time_slow", false);
            host.RemoveChild(teardownStage);
            if (EnemyProjectile.CountActiveIn(teardownStage) != 0)
                failures.Add($"FormalEncounter _ExitTree leaked active projectiles ({EnemyProjectile.CountActiveIn(teardownStage)})");
            teardownStage.Free();
            prevCam?.MakeCurrent();
        }

        if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f) || host.GetTree().Paused)
        {
            failures.Add($"teardown leaked global state: TimeScale={Engine.TimeScale:0.000} Paused={host.GetTree().Paused}");
        }
    }

    private static void DefeatAllAliveEnemies(StageEnemyRoster roster, PlayerController player)
    {
        var snapshot = new List<EnemyController>(roster.Enemies);
        var zeroSpawnWindows = new CombatWindowConfig { BossSpawnSeconds = 0f };
        foreach (EnemyController enemy in snapshot)
        {
            if (!GodotObject.IsInstanceValid(enemy) || enemy.IsDefeated)
                continue;
            enemy.Bind(player, zeroSpawnWindows);
            enemy._PhysicsProcess(0.016);
            // Hit from behind to cleanly bypass frontal shield on HeavyShieldEnemy during orchestration tests
            Vector2 backOrigin = enemy.GlobalPosition - new Vector2(enemy.Facing * 48f, 0f);
            enemy.TakeHit(new EnemyHitContext(enemy.MaxHp + 5f, backOrigin, new Vector2(enemy.Facing, 0f)));
        }
    }

    private static bool HasMatchingLayer2Collider(Node root, Rect2 expected)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is not StaticBody2D body || body.CollisionLayer != 2)
                continue;
            foreach (Node sub in body.GetChildren())
            {
                if (sub is CollisionShape2D { Shape: RectangleShape2D rectShape } shape)
                {
                    Vector2 center = body.Position + shape.Position;
                    var rect = new Rect2(center - rectShape.Size * 0.5f, rectShape.Size);
                    if (rect.IsEqualApprox(expected))
                        return true;
                }
            }
        }
        return false;
    }

    private static void SendAction(string action, bool pressed)
    {
        Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
        Godot.Input.FlushBufferedEvents();
    }

    /// <summary>
    /// Real 60Hz Godot physics probe verifying that a player on FormalEncounter's floor can smoothly
    /// climb the 96px high platforms via both a single normal jump and a diagonal directional skill (K).
    /// </summary>
    public sealed partial class LockGateClimbProbe : Node2D
    {
        private enum Stage
        {
            SettleForJump,
            WaitJumpLand,
            SettleForSkill,
            ChargeSkill,
            WaitSkillLand,
            Done
        }

        private readonly Action<string> _fail;
        private Stage _stage = Stage.SettleForJump;
        private int _frames;
        private FormalStage? _encounter;
        private PlayerController? _player;
        private Camera2D? _previousCamera;
        private Rect2 _leftPlat;
        private Rect2 _rightPlat;
        private bool _jumpPassed;
        private bool _skillPassed;

        public bool IsCompleted { get; private set; }

        public LockGateClimbProbe(Action<string> fail)
        {
            _fail = fail;
        }

        public override void _Ready()
        {
            _previousCamera = GetViewport()?.GetCamera2D();
            Position = new Vector2(0f, -3600f);

            var packed = GD.Load<PackedScene>("res://Scenes/FormalEncounter.tscn");
            if (packed is null)
            {
                CompleteWithFailure("climb probe failed to load FormalEncounter.tscn");
                return;
            }

            _encounter = packed.Instantiate<FormalStage>();
            AddChild(_encounter);
            _player = _encounter.GetNodeOrNull<PlayerController>("Player");
            var env = _encounter.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment");

            if (_player is null || env is null)
            {
                CompleteWithFailure("climb probe missing Player or RuinEnvironment");
                return;
            }

            // Freeze encounter progression & clear enemies during the pure platform traversal probe
            _encounter.SetProcess(false);
            _encounter.GetNodeOrNull<LockGateEncounterController>("LockGateEncounter")?.Cleanup();

            var platforms = new List<Rect2>();
            foreach (RuinSurfaceEntry surface in env.Surfaces)
            {
                if (Mathf.IsEqualApprox(surface.Rect.Position.Y, ExpectedPlatformTopY))
                    platforms.Add(surface.Rect);
            }

            if (platforms.Count != 2)
            {
                CompleteWithFailure($"climb probe expected 2 platforms at Y={ExpectedPlatformTopY:0}, got {platforms.Count}");
                return;
            }

            platforms.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
            _leftPlat = platforms[0];
            _rightPlat = platforms[1];

            // Start 95px left of Right High Platform on the floor
            _player.ResetTo(new Vector2(_rightPlat.Position.X - 95f, ExpectedFloorY - 23f));
            _stage = Stage.SettleForJump;
            _frames = 0;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (IsCompleted || _player is null)
                return;

            _frames++;
            float targetLandY = ExpectedPlatformTopY - 23f;

            switch (_stage)
            {
                case Stage.SettleForJump:
                    if (_player.IsOnFloor() && _frames >= 3)
                    {
                        SendAction("move_right", true);
                        SendAction("jump", true);
                        _stage = Stage.WaitJumpLand;
                        _frames = 0;
                    }
                    else if (_frames > 20)
                    {
                        CompleteWithFailure("climb probe player did not settle on floor before jump");
                    }
                    break;

                case Stage.WaitJumpLand:
                    if (_frames == 2)
                        SendAction("jump", false);

                    if (_frames >= 10 &&
                        _player.IsOnFloor() &&
                        _player.Position.X >= _rightPlat.Position.X + 4f &&
                        _player.Position.X <= _rightPlat.End.X - 4f &&
                        Mathf.Abs(_player.Position.Y - targetLandY) <= 4f)
                    {
                        _jumpPassed = true;
                        ReleaseInputs();
                        _player.ResetTo(new Vector2(_leftPlat.End.X + 120f, ExpectedFloorY - 23f));
                        _stage = Stage.SettleForSkill;
                        _frames = 0;
                    }
                    else if (_frames > 65)
                    {
                        CompleteWithFailure($"single normal jump failed to climb 96px high platform (pos={_player.Position}, onFloor={_player.IsOnFloor()}, targetY={targetLandY:0.0})");
                    }
                    break;

                case Stage.SettleForSkill:
                    if (_player.IsOnFloor() && _frames >= 3)
                    {
                        SendAction("move_left", true);
                        SendAction("move_up", true);
                        SendAction("skill", true);
                        _stage = Stage.ChargeSkill;
                        _frames = 0;
                    }
                    else if (_frames > 20)
                    {
                        CompleteWithFailure("climb probe player did not settle on floor before diagonal skill");
                    }
                    break;

                case Stage.ChargeSkill:
                    if (_frames >= 2)
                    {
                        SendAction("skill", false);
                        _stage = Stage.WaitSkillLand;
                        _frames = 0;
                    }
                    break;

                case Stage.WaitSkillLand:
                    if (_frames == 2)
                    {
                        SendAction("move_left", false);
                        SendAction("move_up", false);
                    }

                    if (_frames >= 12 &&
                        _player.IsOnFloor() &&
                        _player.Position.X >= _leftPlat.Position.X + 4f &&
                        _player.Position.X <= _leftPlat.End.X - 4f &&
                        Mathf.Abs(_player.Position.Y - targetLandY) <= 4f)
                    {
                        _skillPassed = true;
                        FinishCleanly();
                    }
                    else if (_frames > 85)
                    {
                        CompleteWithFailure($"diagonal directional skill failed to climb 96px high platform (pos={_player.Position}, onFloor={_player.IsOnFloor()}, targetY={targetLandY:0.0})");
                    }
                    break;
            }
        }

        public override void _ExitTree()
        {
            ReleaseInputs();
            _previousCamera?.MakeCurrent();
        }

        private void FinishCleanly()
        {
            ReleaseInputs();
            IsCompleted = true;
            _stage = Stage.Done;
            if (_jumpPassed && _skillPassed)
                GD.Print("[Smoke] PASS lock-gate live 60Hz climb probe (single normal jump + diagonal K skill onto 96px platforms)");
            else
                _fail($"lock-gate climb probe incomplete (jump={_jumpPassed}, skill={_skillPassed})");
        }

        private void CompleteWithFailure(string reason)
        {
            ReleaseInputs();
            IsCompleted = true;
            _stage = Stage.Done;
            _fail($"lock-gate-encounter: {reason}");
        }

        private static void ReleaseInputs()
        {
            SendAction("move_right", false);
            SendAction("move_left", false);
            SendAction("move_up", false);
            SendAction("jump", false);
            SendAction("skill", false);
        }
    }
}
