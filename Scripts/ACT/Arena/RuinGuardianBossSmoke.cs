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
/// Headless smoke verifier for Issue #15 (遗迹守卫双阶段重铸与过载二连派生):
/// tests public behavior across Boss, Player, Roster, Feedback, Audio, and HUD seams.
/// </summary>
public static class RuinGuardianBossSmoke
{
    [SmokeProbe(110)]
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckIdentitySuperArmorAndPerfectBlockPoiseBreak(host, failures);
        CheckPhaseOneDistanceSelectionAndBackstep(host, failures);
        CheckHalfHpOverloadTransitionPushbackAndWiring(host, failures);
        CheckPhaseTwoTightenedWindowsCombosAndReset(host, failures);
        CheckContinuousStrikeResolutionForLungeAndCounterGrab(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS RuinGuardianBoss two-phase overhaul, super armor, overload pulse & combo contracts");
            return;
        }

        foreach (string reason in failures)
            fail($"ruin-guardian-boss: {reason}");
    }

    private static void CheckIdentitySuperArmorAndPerfectBlockPoiseBreak(Node host, List<string> failures)
    {
        const float probeFloorY = -1600f;
        float playerY = probeFloorY - 23f;
        float bossY = probeFloorY - 50f;

        var probeStage = new Node2D { Name = "RuinGuardianSuperArmorProbe" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(500f, probeFloorY + 20f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(1200f, 40f) } });
        probeStage.AddChild(floor);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0.05f,
            PerfectGuardSeconds = 0.50f,
            HitstunSeconds = 0.30f,
            NormalAttackTelegraphSeconds = 0.35f,
            NormalAttackActiveSeconds = 0.12f,
            NormalAttackRecoverySeconds = 0.30f
        };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(390f, playerY),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var boss = new RuinGuardianBoss
        {
            Position = new Vector2(500f, bossY)
        };

        probeStage.AddChild(player);
        probeStage.AddChild(boss);
        host.AddChild(probeStage);

        try
        {
            // 1. Default 28 HP & boss identity
            if (!Mathf.IsEqualApprox(boss.MaxHp, 28f) || !boss.IsBoss || boss.ShowsOverheadHealthBar)
                failures.Add($"default boss identity mismatch: MaxHp={boss.MaxHp} IsBoss={boss.IsBoss} Overhead={boss.ShowsOverheadHealthBar}");

            boss.Bind(player, windows);
            player._PhysicsProcess(0.05);
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            if (!Mathf.IsEqualApprox(boss.Hp, 28f))
                failures.Add($"bound boss HP expected 28, got {boss.Hp}");

            // 2. Super armor during Idle: slash & directional skill damage HP without entering Hitstun
            EnemyHitOutcome slashOutcome = boss.TakeHit(new EnemyHitContext(1f, player.GlobalPosition, Vector2.Right, IsDirectionalSkill: false));
            if (slashOutcome != EnemyHitOutcome.Damaged || !Mathf.IsEqualApprox(boss.Hp, 27f) || boss.State == EnemyState.Hitstun)
                failures.Add($"Idle super armor failed on slash: outcome={slashOutcome} hp={boss.Hp} state={boss.State}");

            EnemyHitOutcome skillOutcome = boss.TakeHit(new EnemyHitContext(2f, player.GlobalPosition, Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: true));
            if (skillOutcome != EnemyHitOutcome.Damaged || !Mathf.IsEqualApprox(boss.Hp, 25f) || boss.State == EnemyState.Hitstun)
                failures.Add($"Idle super armor failed on skill: outcome={skillOutcome} hp={boss.Hp} state={boss.State}");

            // 3. Super armor during Telegraph: hitting boss must NOT interrupt telegraph
            boss._PhysicsProcess(0.016); // Idle -> Telegraph (Normal)
            if (boss.State != EnemyState.Telegraph || boss.CurrentAttackKind != BossAttackKind.Normal)
                failures.Add($"expected close opening Normal telegraph, got state={boss.State} kind={boss.CurrentAttackKind}");

            boss.TakeHit(new EnemyHitContext(2f, player.GlobalPosition, Vector2.Right, IsDirectionalSkill: true));
            if (boss.State != EnemyState.Telegraph || !Mathf.IsEqualApprox(boss.Hp, 23f))
                failures.Add($"Telegraph super armor broken: state={boss.State} hp={boss.Hp}");

            // 4. Perfect block against golden Normal strike MUST interrupt Boss into poise-broken Hitstun
            windows.NormalAttackTelegraphSeconds = 0f;
            boss.ResetTo(new Vector2(500f, bossY));
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss._PhysicsProcess(0.016); // Idle -> Telegraph (Normal, 0s telegraph)
            SendAction("block", true);
            player._PhysicsProcess(0.016); // Enter Block within PerfectGuardSeconds
            boss._PhysicsProcess(0.016);   // Telegraph -> Strike
            boss._PhysicsProcess(0.016);   // Strike resolves against perfect block -> poise-broken Hitstun
            SendAction("block", false);
            player._PhysicsProcess(0.016);

            if (boss.State != EnemyState.Hitstun)
                failures.Add($"Perfect block on golden Normal strike did not poise-break boss into Hitstun (state={boss.State})");
        }
        finally
        {
            SendAction("block", false);
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckPhaseOneDistanceSelectionAndBackstep(Node host, List<string> failures)
    {
        const float probeFloorY = -1600f;
        float playerY = probeFloorY - 23f;
        float bossY = probeFloorY - 50f;

        var probeStage = new Node2D { Name = "RuinGuardianPhase1SelectionProbe" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(800f, probeFloorY + 20f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(2000f, 40f) } });
        probeStage.AddChild(floor);

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
            DelayedAreaTelegraphSeconds = 0f,
            DelayedAreaActiveSeconds = 0f,
            DelayedAreaRecoverySeconds = 0f,
            EnemyAttackRange = 220f
        };
        var player = new PlayerController
        {
            Name = "Phase1ProbePlayer",
            Position = new Vector2(480f, playerY),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var boss = new RuinGuardianBoss
        {
            Position = new Vector2(600f, bossY),
            ArenaMinX = 200f,
            ArenaMaxX = 1100f
        };

        var observed = new List<BossAttackKind>();
        boss.TelegraphStarted += cue => observed.Add(cue.Attack.Kind);

        probeStage.AddChild(player);
        probeStage.AddChild(boss);
        host.AddChild(probeStage);

        try
        {
            boss.Bind(player, windows);

            // 1. Close range (120px): must abolish old 3-move rotation and interleave Normal <-> BreakGrab without consecutive repeats
            bool sawBackstep = false;
            float maxBossX = boss.GlobalPosition.X;
            for (int step = 0; step < 60 && observed.Count < 4; step++)
            {
                player.ResetTo(new Vector2(boss.GlobalPosition.X - 120f, playerY));
                float prevX = boss.GlobalPosition.X;
                boss._PhysicsProcess(0.016);
                if (boss.GlobalPosition.X > prevX + 1.5f)
                    sawBackstep = true;
                maxBossX = Mathf.Max(maxBossX, boss.GlobalPosition.X);
            }

            if (observed.Count < 4 ||
                observed[0] != BossAttackKind.Normal ||
                observed[1] != BossAttackKind.BreakGrab ||
                observed[2] != BossAttackKind.Normal ||
                observed[3] != BossAttackKind.BreakGrab)
            {
                failures.Add($"Phase 1 close-range selection expected [Normal, BreakGrab, Normal, BreakGrab], got [{string.Join(", ", observed)}]");
            }
            if (!sawBackstep)
                failures.Add($"Phase 1 post-recovery short backstep never moved boss away from close player (maxX={maxBossX:0.0})");

            // 2. Far range (340px): alternates lunge-step Normal (closing gap) and DelayedArea without consecutive repetition
            observed.Clear();
            boss.ResetTo(new Vector2(820f, bossY));
            bool sawLungeCloseIn = false;
            for (int attackIdx = 0; attackIdx < 3; attackIdx++)
            {
                boss.Position = new Vector2(820f, bossY);
                player.ResetTo(new Vector2(480f, playerY));
                int countBefore = observed.Count;
                float startX = boss.GlobalPosition.X;
                for (int step = 0; step < 40 && observed.Count == countBefore; step++)
                    boss._PhysicsProcess(0.016);
                if (observed.Count > countBefore && observed[^1] == BossAttackKind.Normal && boss.GlobalPosition.X < startX - 40f)
                    sawLungeCloseIn = true;
                // Complete the attack cycle
                for (int step = 0; step < 12 && boss.State != EnemyState.Idle; step++)
                    boss._PhysicsProcess(0.016);
            }

            if (observed.Count < 3 ||
                observed[0] != BossAttackKind.Normal ||
                observed[1] != BossAttackKind.DelayedArea ||
                observed[2] != BossAttackKind.Normal)
            {
                failures.Add($"Phase 1 far-range selection expected [Normal, DelayedArea, Normal], got [{string.Join(", ", observed)}]");
            }
            if (!sawLungeCloseIn)
                failures.Add("Phase 1 far-range Normal strike did not perform forward lunge step to close distance");

            // 3. Boundary protection: boss pinned near ArenaMaxX must not backstep past ArenaMaxX
            boss.ArenaMinX = 200f;
            boss.ArenaMaxX = 620f;
            boss.ResetTo(new Vector2(605f, bossY));
            observed.Clear();
            for (int step = 0; step < 48 && observed.Count < 3; step++)
            {
                player.ResetTo(new Vector2(485f, playerY));
                boss._PhysicsProcess(0.016);
                if (boss.GlobalPosition.X > boss.ArenaMaxX + 2f)
                {
                    failures.Add($"Boss backstepped outside ArenaMaxX ({boss.GlobalPosition.X:0.0} > {boss.ArenaMaxX:0.0})");
                    break;
                }
            }
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckHalfHpOverloadTransitionPushbackAndWiring(Node host, List<string> failures)
    {
        const float probeFloorY = -1600f;
        float playerY = probeFloorY - 23f;
        float bossY = probeFloorY - 50f;

        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();
        var probeStage = new Node2D { Name = "RuinGuardianOverloadProbe" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(600f, probeFloorY + 20f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(1400f, 40f) } });
        probeStage.AddChild(floor);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0.50f
        };
        var timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(520f, playerY),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var camera = new Camera2D { Name = "ProbeCamera" };
        player.AddChild(camera);

        var boss = new RuinGuardianBoss
        {
            Name = "Boss",
            Position = new Vector2(620f, bossY)
        };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(boss);
        StageEnemyRoster roster = StageRuntimeWiring.AttachEnemyRoster(probeStage, timeSlow, boss);
        CombatHud hud = StageRuntimeWiring.MountStagePresentationAndHud(
            probeStage,
            player,
            roster,
            timeSlow,
            camera,
            "过载转阶段探针",
            RuinGuardianBoss.DefaultDisplayName);
        host.AddChild(probeStage);

        var feedback = probeStage.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer");
        var audio = probeStage.GetNodeOrNull<CombatAudioController>("CombatAudio");

        int overloadCueCount = 0;
        EnemyOverloadPulseCue? lastOverloadCue = null;
        boss.OverloadPulseTriggered += cue =>
        {
            overloadCueCount++;
            lastOverloadCue = cue;
        };

        try
        {
            boss.Bind(player, windows);
            player._PhysicsProcess(0.05);
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss._PhysicsProcess(0.016); // Idle -> Telegraph (active telegraph to verify immediate interrupt)

            // Drop to 15 HP (still Phase 1)
            boss.TakeHit(new EnemyHitContext(13f, player.GlobalPosition, Vector2.Right));
            if (boss.IsPhaseTwo || boss.IsInvulnerable || overloadCueCount != 0)
                failures.Add($"boss entered Phase 2 prematurely at {boss.Hp} HP (isP2={boss.IsPhaseTwo}, cues={overloadCueCount})");

            // Drop from 15 HP -> 14 HP (<= 50% threshold): triggers immediate invulnerable overload transition
            float playerHpBefore = player.Hp;
            float playerGuardBefore = player.Guard;
            float playerXBefore = player.GlobalPosition.X;
            int ringsBefore = feedback?.ActiveRingCount ?? 0;

            boss.TakeHit(new EnemyHitContext(1f, player.GlobalPosition, Vector2.Right));

            // Advance player physics so horizontal knockback executes across multiple ground frames without being swallowed
            for (int step = 0; step < 12; step++)
                player._PhysicsProcess(0.016);

            bool visualOverload = boss.Visual is RuinGuardianVisual guardianVisual && guardianVisual.IsOverloadVisualActive;
            if (!boss.IsPhaseTwo || !boss.IsOverloadTransitionActive || !boss.IsInvulnerable)
                failures.Add($"half-HP did not enter invulnerable Phase 2 overload transition (P2={boss.IsPhaseTwo} trans={boss.IsOverloadTransitionActive} invuln={boss.IsInvulnerable})");
            if (overloadCueCount != 1 || lastOverloadCue is not { PhaseNumber: 2 })
                failures.Add($"OverloadPulseTriggered expected once with PhaseNumber=2, got count={overloadCueCount} cue={lastOverloadCue}");
            if (!Mathf.IsEqualApprox(player.Hp, playerHpBefore) ||
                !Mathf.IsEqualApprox(player.Guard, playerGuardBefore) ||
                player.State == PlayerState.Hitstun)
            {
                failures.Add($"overload knockback pulse must deal 0 damage and no hitstun (hp={player.Hp}/{playerHpBefore}, guard={player.Guard}/{playerGuardBefore}, state={player.State})");
            }
            if (player.GlobalPosition.X >= playerXBefore - 30f)
                failures.Add($"overload pulse did not push close player horizontally away from boss ({playerXBefore:0.0} -> {player.GlobalPosition.X:0.0})");
            if (audio is null || audio.LastCue != CombatSfxId.BossOverloadPulse)
                failures.Add($"overload pulse did not trigger BossOverloadPulse SFX (got {audio?.LastCue.ToString() ?? "null"})");
            if (feedback is null || feedback.ActiveRingCount <= ringsBefore)
                failures.Add($"overload pulse did not spawn geometric shockwave rings in CombatFeedbackLayer (rings {ringsBefore} -> {feedback?.ActiveRingCount})");
            if (hud.CurrentEventBannerText != "遗迹守卫 · 过载二阶段")
                failures.Add($"HUD Phase 2 banner mismatch: '{hud.CurrentEventBannerText}'");
            if (!visualOverload)
                failures.Add($"RuinGuardianVisual did not activate Phase 2 fissure visual (visualType={boss.Visual?.GetType().Name ?? "null"})");

            // Hits during invulnerable overload transition must be ignored
            float hpDuringOverload = boss.Hp;
            EnemyHitOutcome invulnOutcome = boss.TakeHit(new EnemyHitContext(5f, player.GlobalPosition, Vector2.Right));
            if (invulnOutcome != EnemyHitOutcome.Ignored || !Mathf.IsEqualApprox(boss.Hp, hpDuringOverload))
                failures.Add($"hit during overload invulnerability was not ignored (outcome={invulnOutcome}, hp={boss.Hp}/{hpDuringOverload})");

            // Lethal hit directly from Phase 1 must NOT trigger Phase 2 overload
            boss.ResetTo(new Vector2(620f, bossY));
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            int cuesBeforeLethal = overloadCueCount;
            boss.TakeHit(new EnemyHitContext(boss.MaxHp, player.GlobalPosition, Vector2.Right));
            if (overloadCueCount != cuesBeforeLethal || boss.IsPhaseTwo)
                failures.Add("lethal hit directly from Phase 1 unexpectedly triggered Phase 2 overload");
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
            previousCamera?.MakeCurrent();
        }
    }

    private static void CheckPhaseTwoTightenedWindowsCombosAndReset(Node host, List<string> failures)
    {
        const float probeFloorY = -1600f;
        float playerY = probeFloorY - 23f;
        float bossY = probeFloorY - 50f;

        var probeStage = new Node2D { Name = "RuinGuardianPhase2ComboProbe" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(800f, probeFloorY + 20f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(2000f, 40f) } });
        probeStage.AddChild(floor);

        var defaultWindows = new CombatWindowConfig { BossSpawnSeconds = 0f };
        var instantWindows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            PerfectGuardSeconds = 0.50f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0f,
            NormalAttackRecoverySeconds = 0f,
            BreakGrabTelegraphSeconds = 0f,
            BreakGrabActiveSeconds = 0f,
            BreakGrabRecoverySeconds = 0f,
            DelayedAreaTelegraphSeconds = 0f,
            DelayedAreaActiveSeconds = 0f,
            DelayedAreaRecoverySeconds = 0f,
            EnemyAttackRange = 220f
        };
        var timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "Phase2ProbePlayer",
            Position = new Vector2(460f, playerY),
            Windows = defaultWindows,
            TimeSlow = timeSlow.Config
        };
        var boss = new RuinGuardianBoss
        {
            Name = "Boss",
            Position = new Vector2(800f, bossY),
            ArenaMinX = 200f,
            ArenaMaxX = 1300f,
            OverloadTransitionSeconds = 0f
        };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(boss);
        StageEnemyRoster roster = StageRuntimeWiring.AttachEnemyRoster(probeStage, timeSlow, boss);
        host.AddChild(probeStage);

        var telegraphs = new List<BossAttackDefinition>();
        boss.TelegraphStarted += cue => telegraphs.Add(cue.Attack);

        try
        {
            // 1. Verify ~20% tightened Phase 2 timings while remaining strictly above 0.25s perfect guard window
            boss.Bind(player, defaultWindows);
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            BossAttackDefinition p1Normal = boss.DefinitionFor(BossAttackKind.Normal);
            BossAttackDefinition p1Break = boss.DefinitionFor(BossAttackKind.BreakGrab);
            BossAttackDefinition p1Area = boss.DefinitionFor(BossAttackKind.DelayedArea);

            boss.TakeHit(new EnemyHitContext(14f, player.GlobalPosition, Vector2.Right)); // -> 14 HP, Phase 2
            boss._PhysicsProcess(0.016); // Finish 0s overload transition

            BossAttackDefinition p2Normal = boss.DefinitionFor(BossAttackKind.Normal);
            BossAttackDefinition p2Break = boss.DefinitionFor(BossAttackKind.BreakGrab);
            BossAttackDefinition p2Area = boss.DefinitionFor(BossAttackKind.DelayedArea);
            BossAttackDefinition p2ComboArea = boss.DefinitionFor(BossAttackKind.DelayedArea, acceleratedCombo: true);

            bool tightenedOk =
                p2Normal.TelegraphSeconds < p1Normal.TelegraphSeconds * 0.85f &&
                p2Normal.TelegraphSeconds > defaultWindows.PerfectGuardSeconds &&
                p2Normal.RecoverySeconds < p1Normal.RecoverySeconds * 0.85f &&
                p2Break.TelegraphSeconds < p1Break.TelegraphSeconds * 0.85f &&
                p2Break.TelegraphSeconds > defaultWindows.PerfectGuardSeconds &&
                p2Break.RecoverySeconds < p1Break.RecoverySeconds * 0.85f &&
                p2Area.TelegraphSeconds < p1Area.TelegraphSeconds * 0.85f &&
                p2Area.TelegraphSeconds > defaultWindows.PerfectGuardSeconds &&
                p2Area.RecoverySeconds < p1Area.RecoverySeconds * 0.85f &&
                p2ComboArea.TelegraphSeconds < p2Area.TelegraphSeconds &&
                p2ComboArea.TelegraphSeconds > defaultWindows.PerfectGuardSeconds;
            if (!tightenedOk)
            {
                failures.Add($"Phase 2 ~20% timing tightening mismatch: Normal {p1Normal.TelegraphSeconds:0.00}->{p2Normal.TelegraphSeconds:0.00}, " +
                             $"Break {p1Break.TelegraphSeconds:0.00}->{p2Break.TelegraphSeconds:0.00}, Area {p1Area.TelegraphSeconds:0.00}->{p2Area.TelegraphSeconds:0.00}");
            }

            // 2. Combo 1 (un-parried): Phase 2 far lunge Normal -> skips recovery straight into accelerated DelayedArea
            player.Windows = instantWindows;
            boss.Bind(player, instantWindows);
            boss.Position = new Vector2(820f, bossY);
            player.ResetTo(new Vector2(480f, playerY)); // Far range (340px)
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss.TakeHit(new EnemyHitContext(14f, player.GlobalPosition, Vector2.Right)); // Enter Phase 2
            boss._PhysicsProcess(0.016); // Exit 0s overload transition

            telegraphs.Clear();
            bool enteredRecoverBetweenCombo = false;
            for (int step = 0; step < 36 && telegraphs.Count < 2; step++)
            {
                boss._PhysicsProcess(0.016);
                if (telegraphs.Count == 1 && boss.State == EnemyState.Recover)
                    enteredRecoverBetweenCombo = true;
            }

            if (telegraphs.Count < 2 ||
                telegraphs[0].Kind != BossAttackKind.Normal ||
                telegraphs[1].Kind != BossAttackKind.DelayedArea ||
                enteredRecoverBetweenCombo)
            {
                failures.Add($"Phase 2 Combo 1 (Lunge Normal -> DelayedArea) mismatch: kinds=[{string.Join(", ", telegraphs.ConvertAll(a => a.Kind))}] recoveredBetween={enteredRecoverBetweenCombo}");
            }

            // 3. Combo 1 cancelled by Perfect Block: Lunge Normal perfect-blocked -> poise-broken Hitstun, NO DelayedArea follow-up
            boss.ResetTo(new Vector2(820f, bossY));
            player.ResetTo(new Vector2(540f, playerY)); // Far range (280px), boss lunges into melee range
            player._PhysicsProcess(0.05);
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss.TakeHit(new EnemyHitContext(14f, player.GlobalPosition, Vector2.Right)); // Phase 2
            boss._PhysicsProcess(0.016); // Exit overload transition
            telegraphs.Clear();

            SendAction("block", true);
            player._PhysicsProcess(0.016);
            bool sawPoiseBreak = false;
            for (int step = 0; step < 36; step++)
            {
                boss._PhysicsProcess(0.016);
                if (boss.State == EnemyState.Hitstun)
                {
                    sawPoiseBreak = true;
                    break;
                }
            }
            SendAction("block", false);
            player._PhysicsProcess(0.016);

            if (!sawPoiseBreak || telegraphs.Count != 1 || telegraphs[0].Kind != BossAttackKind.Normal)
            {
                failures.Add($"Phase 2 Combo 1 was not cancelled by Perfect Block: poiseBreak={sawPoiseBreak} kinds=[{string.Join(", ", telegraphs.ConvertAll(a => a.Kind))}]");
            }

            // 4. Combo 2: Phase 2 short backstep landing -> immediate counter BreakGrab
            boss.ResetTo(new Vector2(640f, bossY));
            player.ResetTo(new Vector2(520f, playerY)); // Close range (120px)
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss.TakeHit(new EnemyHitContext(14f, player.GlobalPosition, Vector2.Right)); // Phase 2
            boss._PhysicsProcess(0.016); // Exit overload transition
            telegraphs.Clear();

            bool sawBackstep = false;
            bool sawImmediateBreakAfterBackstep = false;
            for (int step = 0; step < 48; step++)
            {
                player.ResetTo(new Vector2(boss.GlobalPosition.X - 120f, playerY));
                bool wasBackstepping = boss.IsBackstepping;
                boss._PhysicsProcess(0.016);
                if (boss.IsBackstepping)
                    sawBackstep = true;
                if (wasBackstepping && !boss.IsBackstepping &&
                    boss.State == EnemyState.Telegraph &&
                    boss.CurrentAttackKind == BossAttackKind.BreakGrab)
                {
                    sawImmediateBreakAfterBackstep = true;
                    break;
                }
            }

            if (!sawBackstep || !sawImmediateBreakAfterBackstep)
            {
                failures.Add($"Phase 2 Combo 2 (Backstep -> immediate BreakGrab) mismatch: sawBackstep={sawBackstep} immediateBreak={sawImmediateBreakAfterBackstep} state={boss.State} kind={boss.CurrentAttackKind}");
            }

            // 5. Pure 1v1 contract: zero minions summoned across both phases
            if (roster.Count != 1)
                failures.Add($"RuinGuardianBoss summoned extra enemies (roster.Count={roster.Count})");

            // 6. ResetTo restores Phase 1 & full HP cleanly
            boss.ResetTo(new Vector2(800f, bossY));
            if (boss.Phase != 1 || boss.IsPhaseTwo || boss.IsInvulnerable || !Mathf.IsEqualApprox(boss.Hp, boss.MaxHp))
                failures.Add($"ResetTo did not restore Phase 1 state cleanly (Phase={boss.Phase}, Invuln={boss.IsInvulnerable}, Hp={boss.Hp}/{boss.MaxHp})");
        }
        finally
        {
            SendAction("block", false);
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckContinuousStrikeResolutionForLungeAndCounterGrab(Node host, List<string> failures)
    {
        const float probeFloorY = -1600f;
        float playerY = probeFloorY - 23f;
        float bossY = probeFloorY - 50f;

        var probeStage = new Node2D { Name = "RuinGuardianContinuousStrikeProbe" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(800f, probeFloorY + 20f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(2000f, 40f) } });
        probeStage.AddChild(floor);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0.22f,
            NormalAttackRecoverySeconds = 0f,
            BreakGrabTelegraphSeconds = 0f,
            BreakGrabActiveSeconds = 0.22f,
            BreakGrabRecoverySeconds = 0f,
            DelayedAreaTelegraphSeconds = 0f,
            DelayedAreaActiveSeconds = 0f,
            DelayedAreaRecoverySeconds = 0f
        };
        var player = new PlayerController
        {
            Name = "ContinuousStrikeProbePlayer",
            Position = new Vector2(480f, playerY),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var boss = new RuinGuardianBoss
        {
            Name = "Boss",
            Position = new Vector2(840f, bossY), // 360px (> 330px) far lunge distance
            ArenaMinX = 200f,
            ArenaMaxX = 1300f,
            OverloadTransitionSeconds = 0f
        };

        int defenseHits = 0;
        player.DefenseResolved += _ => defenseHits++;

        probeStage.AddChild(player);
        probeStage.AddChild(boss);
        host.AddChild(probeStage);

        try
        {
            // 1. Far lunge golden Normal strike (>330px: 360px start) must connect during Strike lunge and settle exactly once
            boss.Bind(player, windows);
            player._PhysicsProcess(0.05);

            // Step through Spawn -> Idle -> LungeApproach -> Telegraph -> Strike
            for (int step = 0; step < 40 && boss.State != EnemyState.Strike; step++)
                boss._PhysicsProcess(0.016);

            if (boss.State != EnemyState.Strike || boss.CurrentAttackKind != BossAttackKind.Normal)
            {
                failures.Add($"far lunge setup did not reach Normal Strike (state={boss.State}, kind={boss.CurrentAttackKind})");
            }
            else
            {
                float hpBeforeLunge = player.Hp;
                defenseHits = 0;
                for (int step = 0; step < 18 && boss.State == EnemyState.Strike; step++)
                    boss._PhysicsProcess(0.016);

                if (defenseHits != 1 || player.Hp >= hpBeforeLunge)
                {
                    failures.Add($"far lunge Normal strike failed continuous hit resolution: hits={defenseHits} hp={player.Hp}/{hpBeforeLunge} dist={boss.GlobalPosition.DistanceTo(player.GlobalPosition):0.0}");
                }
            }

            // 2. Phase 2 backstep from 150px -> immediate counter BreakGrab must lunge in during Strike and connect exactly once
            windows.NormalAttackActiveSeconds = 0f;
            boss.ResetTo(new Vector2(630f, bossY));
            player.ResetTo(new Vector2(480f, playerY)); // 150px close range
            player._PhysicsProcess(0.05);
            boss._PhysicsProcess(0.016); // Spawn -> Idle
            boss.TakeHit(new EnemyHitContext(14f, player.GlobalPosition, Vector2.Right)); // Enter Phase 2
            boss._PhysicsProcess(0.016); // Exit 0s overload transition

            // Advance through close Normal strike -> Recover -> Backstep -> Counter BreakGrab Telegraph
            bool reachedCounterGrabTelegraph = false;
            for (int step = 0; step < 60; step++)
            {
                bool wasBackstepping = boss.IsBackstepping;
                boss._PhysicsProcess(0.016);
                if (wasBackstepping && !boss.IsBackstepping &&
                    boss.State == EnemyState.Telegraph &&
                    boss.CurrentAttackKind == BossAttackKind.BreakGrab)
                {
                    reachedCounterGrabTelegraph = true;
                    break;
                }
            }

            if (!reachedCounterGrabTelegraph)
            {
                failures.Add($"counter BreakGrab setup did not reach post-backstep BreakGrab Telegraph (state={boss.State}, kind={boss.CurrentAttackKind}, dist={boss.GlobalPosition.DistanceTo(player.GlobalPosition):0.0})");
            }
            else
            {
                // Keep player stationary at 150px origin (now ~232px away after 82px backstep) and advance into Strike
                player.ResetTo(new Vector2(480f, playerY));
                player._PhysicsProcess(0.05);
                float hpBeforeGrab = player.Hp;
                defenseHits = 0;

                boss._PhysicsProcess(0.016); // Telegraph (0s) -> Strike
                for (int step = 0; step < 18 && boss.State == EnemyState.Strike; step++)
                    boss._PhysicsProcess(0.016);

                if (defenseHits != 1 || player.Hp >= hpBeforeGrab)
                {
                    failures.Add($"post-backstep counter BreakGrab failed continuous hit resolution: hits={defenseHits} hp={player.Hp}/{hpBeforeGrab} dist={boss.GlobalPosition.DistanceTo(player.GlobalPosition):0.0}");
                }
            }
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void SendAction(string action, bool pressed)
    {
        Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
        Godot.Input.FlushBufferedEvents();
    }
}
