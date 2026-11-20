using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Isolated headless smoke verifier for Issue #12 (近战斥候):
/// covers archetype defaults, high-speed chase & TimeSlow movement scaling,
/// golden-fan normal slash telegraph & locked facing, melee attack-token coordination,
/// hitstun interruption, player defense matrix (block/perfect-block/dodge/perfect-dodge),
/// kill flow, and procedural geometric visual contract.
/// </summary>
public static class MeleeScoutSmoke
{
    private const float ProbeFloorY = -2000f;

    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckDefaultsChaseAndTimeSlow(host, failures);
        CheckTelegraphLockFacingAndTokenLifecycle(host, failures);
        CheckCombatDefenseMatrixAndKillFlow(host, failures);
        CheckVisualContract(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS melee scout behavior, melee token coordination, defense matrix & acute-armor visual contract");
            return;
        }

        foreach (string reason in failures)
            fail($"melee-scout: {reason}");
    }

    private static void CheckDefaultsChaseAndTimeSlow(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "MeleeScoutChaseProbeStage" };
        AddProbeFloor(probeStage, 500f, 1200f);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f
        };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "ScoutProbePlayer",
            Position = new Vector2(200f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var scout = new MeleeScoutEnemy
        {
            Name = "ProbeMeleeScout",
            Position = new Vector2(680f, ProbeFloorY - 32f)
        };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(scout);
        host.AddChild(probeStage);

        try
        {
            scout.Bind(player, windows);
            scout.BindTimeSlow(timeSlow);

            // Defaults: 3 HP, high chase speed (160..190), non-boss, melee token pool, no frontal defense
            if (!Mathf.IsEqualApprox(scout.MaxHp, 3f) ||
                !Mathf.IsEqualApprox(scout.Hp, 3f) ||
                scout.ChaseSpeed < 160f ||
                scout.ChaseSpeed > 190f ||
                scout.DisplayName != "近战斥候" ||
                scout.IsBoss ||
                !scout.ShowsOverheadHealthBar ||
                scout.HasFrontalDefense ||
                scout.IsInvulnerable ||
                !scout.UsesEncounterAttackToken ||
                scout.AttackTokenPoolKind != AttackTokenPool.Melee)
            {
                failures.Add($"defaults mismatch: hp={scout.Hp}/{scout.MaxHp} speed={scout.ChaseSpeed:0.0} name='{scout.DisplayName}' boss={scout.IsBoss} bar={scout.ShowsOverheadHealthBar} token={scout.UsesEncounterAttackToken}/{scout.AttackTokenPoolKind}");
            }

            // Spawn -> Idle -> Chase toward player on the left (-1)
            scout._PhysicsProcess(0.016); // Spawn -> Idle
            float normalStartX = scout.GlobalPosition.X;
            scout._PhysicsProcess(0.016); // Idle -> Chase
            if (scout.State != EnemyState.Chase ||
                scout.Facing != -1 ||
                !Mathf.IsEqualApprox(scout.Velocity.X, -scout.ChaseSpeed))
            {
                failures.Add($"chase state mismatch: state={scout.State} facing={scout.Facing} vx={scout.Velocity.X:0.0} expected={-scout.ChaseSpeed:0.0}");
            }

            for (int i = 0; i < 3; i++)
                scout._PhysicsProcess(0.016);
            float normalDisplacement = normalStartX - scout.GlobalPosition.X;
            if (normalDisplacement <= 1f)
                failures.Add($"scout did not move toward player while chasing (dx={normalDisplacement:0.00})");

            // Activate TimeSlowManager via real input path: chase velocity must remain uncompensated
            // so Engine.TimeScale naturally scales real-time movement speed to x0.35.
            SendAction("time_slow", true);
            timeSlow._Process(0.016);
            scout._PhysicsProcess(0.016);
            float realTimeChaseSpeed = Mathf.Abs(scout.Velocity.X) * (float)Engine.TimeScale;
            if (!timeSlow.Active ||
                !Mathf.IsEqualApprox((float)Engine.TimeScale, timeSlow.Config.TimeScale) ||
                !Mathf.IsEqualApprox(scout.Velocity.X, -scout.ChaseSpeed) ||
                !Mathf.IsEqualApprox(realTimeChaseSpeed, scout.ChaseSpeed * timeSlow.Config.TimeScale) ||
                !scout.Visual.IsTimeSlowBound)
            {
                failures.Add($"time slow chase scaling mismatch: active={timeSlow.Active} scale={Engine.TimeScale:0.00} vx={scout.Velocity.X:0.0} realSpeed={realTimeChaseSpeed:0.0} visualBound={scout.Visual.IsTimeSlowBound}");
            }
        }
        finally
        {
            SendAction("time_slow", false);
            TimeSlowManager.EnsureNormalScale();
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckTelegraphLockFacingAndTokenLifecycle(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "MeleeScoutTokenProbeStage" };
        AddProbeFloor(probeStage, 500f, 1200f);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            HitstunSeconds = 0.30f,
            VictoryDelaySeconds = 0.40f
        };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "TokenScoutPlayer",
            Position = new Vector2(400f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var roster = new StageEnemyRoster { Name = "EnemyRoster" };
        roster.BindTimeSlow(timeSlow);

        var scout1 = new MeleeScoutEnemy { Name = "Scout1", Position = new Vector2(490f, ProbeFloorY - 32f) };
        var scout2 = new MeleeScoutEnemy { Name = "Scout2", Position = new Vector2(500f, ProbeFloorY - 32f) };
        var scout3 = new MeleeScoutEnemy { Name = "Scout3", Position = new Vector2(510f, ProbeFloorY - 32f) };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(roster);
        probeStage.AddChild(scout1);
        probeStage.AddChild(scout2);
        probeStage.AddChild(scout3);
        host.AddChild(probeStage);

        var observedTelegraphKinds = new List<BossAttackKind>();
        scout1.TelegraphStarted += cue => observedTelegraphKinds.Add(cue.Attack.Kind);
        scout2.TelegraphStarted += cue => observedTelegraphKinds.Add(cue.Attack.Kind);
        scout3.TelegraphStarted += cue => observedTelegraphKinds.Add(cue.Attack.Kind);

        try
        {
            EncounterAttackCoordinator coordinator = roster.AttackCoordinator;

            scout1.Bind(player, windows);
            scout2.Bind(player, windows);
            scout3.Bind(player, windows);
            roster.RegisterRange(new EnemyController[] { scout1, scout2, scout3 });

            // Advance out of Spawn
            scout1._PhysicsProcess(0.016);
            scout2._PhysicsProcess(0.016);
            scout3._PhysicsProcess(0.016);

            // 1. Same-tick attack request: scout1 gets melee token & golden Normal telegraph; scout2/3 wait
            scout1._PhysicsProcess(0.016);
            scout2._PhysicsProcess(0.016);
            scout3._PhysicsProcess(0.016);

            if (scout1.State != EnemyState.Telegraph ||
                scout1.CurrentAttackKind != BossAttackKind.Normal ||
                !scout1.HoldsAttackToken ||
                coordinator.ActiveMeleeCount != 1)
            {
                failures.Add($"scout1 did not enter golden Normal telegraph with melee token (state={scout1.State}, kind={scout1.CurrentAttackKind}, holds={scout1.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");
            }

            if (scout2.State is EnemyState.Telegraph or EnemyState.Strike || scout2.HoldsAttackToken ||
                scout3.State is EnemyState.Telegraph or EnemyState.Strike || scout3.HoldsAttackToken)
            {
                failures.Add($"unapproved sibling scouts bypassed melee token stagger (s2={scout2.State}/{scout2.HoldsAttackToken}, s3={scout3.State}/{scout3.HoldsAttackToken})");
            }

            // 2. Real-timestamp telegraph: repeated same-tick _PhysicsProcess steps must NOT skip Telegraph early
            for (int i = 0; i < 6; i++)
                scout1._PhysicsProcess(0.25);
            if (scout1.State != EnemyState.Telegraph)
                failures.Add($"telegraph advanced by frame delta instead of real timestamps (state={scout1.State})");

            // 3. Locked facing during Telegraph: moving player behind scout1 must NOT flip scout1.Facing
            int lockedFacing = scout1.Facing;
            player.Position = new Vector2(scout1.GlobalPosition.X + 180f, ProbeFloorY - 23f);
            scout1._PhysicsProcess(0.016);
            if (scout1.Facing != lockedFacing)
                failures.Add($"scout1 flipped facing during Telegraph (locked={lockedFacing}, now={scout1.Facing})");

            // Move player back in front for sibling checks
            player.Position = new Vector2(400f, ProbeFloorY - 23f);

            // 4. Hitstun interrupts Telegraph and immediately releases melee token -> scout2 promotes
            var normalSlashHit = new EnemyHitContext(1f, player.GlobalPosition, Vector2.Right);
            EnemyHitOutcome hitOutcome = scout1.TakeHit(normalSlashHit);
            if (hitOutcome != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(scout1.Hp, 2f) ||
                scout1.State != EnemyState.Hitstun ||
                scout1.HoldsAttackToken ||
                coordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"normal slash did not interrupt scout1 telegraph and release token (outcome={hitOutcome}, hp={scout1.Hp}, state={scout1.State}, holds={scout1.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");
            }

            scout2._PhysicsProcess(0.016);
            if (scout2.State != EnemyState.Telegraph || !scout2.HoldsAttackToken || coordinator.ActiveMeleeCount != 1)
                failures.Add($"waiting scout2 did not acquire melee token after scout1 hitstun (state={scout2.State}, holds={scout2.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");

            // 5. Directional skill (2 dmg) interrupts scout2 & releases token
            var skillHit = new EnemyHitContext(2f, player.GlobalPosition, Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: true);
            EnemyHitOutcome skillOutcome = scout2.TakeHit(skillHit);
            if (skillOutcome != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(scout2.Hp, 1f) ||
                scout2.State != EnemyState.Hitstun ||
                scout2.HoldsAttackToken ||
                coordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"directional skill hit on scout2 mismatch (outcome={skillOutcome}, hp={scout2.Hp}, state={scout2.State}, active={coordinator.ActiveMeleeCount})");
            }

            // 6. Reset scout3 with zero-duration telegraph/active to verify locked-facing whiff & strike-end release
            scout3.SlashTelegraphSeconds = 0f;
            scout3.SlashActiveSeconds = 0f;
            scout3.SlashRecoverySeconds = 0.25f;
            scout3.ResetTo(scout3.Position);
            scout3._PhysicsProcess(0.016); // Spawn -> Idle
            scout3._PhysicsProcess(0.016); // Idle -> Telegraph (facing -1, acquires melee token)
            if (scout3.State != EnemyState.Telegraph || !scout3.HoldsAttackToken || coordinator.ActiveMeleeCount != 1)
                failures.Add($"scout3 failed to acquire melee token (state={scout3.State}, active={coordinator.ActiveMeleeCount})");

            // Move player behind scout3 during its locked facing -> strike must whiff and release token in Recover
            float playerHpBeforeWhiff = player.Hp;
            player.Position = new Vector2(scout3.GlobalPosition.X + 75f, ProbeFloorY - 23f);
            scout3._PhysicsProcess(0.016); // Telegraph -> Strike
            scout3._PhysicsProcess(0.016); // Strike resolves (whiff behind back) -> Recover (releases token)
            if (scout3.State != EnemyState.Recover ||
                scout3.Facing != -1 ||
                scout3.HoldsAttackToken ||
                coordinator.ActiveMeleeCount != 0 ||
                !Mathf.IsEqualApprox(player.Hp, playerHpBeforeWhiff))
            {
                failures.Add($"scout3 locked-facing whiff or strike-end token release mismatch (state={scout3.State}, facing={scout3.Facing}, holds={scout3.HoldsAttackToken}, active={coordinator.ActiveMeleeCount}, playerHp={player.Hp}/{playerHpBeforeWhiff})");
            }

            // 7. Lethal hit during Telegraph immediately releases token and hides overhead bar
            scout3.SlashTelegraphSeconds = 0.55f;
            scout3.SlashRecoverySeconds = 0f;
            scout3.AttackCooldownSeconds = 0f;
            player.Position = new Vector2(scout3.GlobalPosition.X - 80f, ProbeFloorY - 23f);
            scout3.ResetTo(scout3.Position);
            scout3._PhysicsProcess(0.016); // Spawn -> Idle
            scout3._PhysicsProcess(0.016); // Idle -> Telegraph (acquires token)
            scout3.TakeHit(new EnemyHitContext(3f, player.GlobalPosition, Vector2.Right));
            if (scout3.State != EnemyState.Dead ||
                !scout3.IsDefeated ||
                scout3.ShowsOverheadHealthBar ||
                scout3.HoldsAttackToken ||
                coordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"lethal hit during telegraph failed release/defeat contract (state={scout3.State}, bar={scout3.ShowsOverheadHealthBar}, holds={scout3.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");
            }

            // Every telegraph ever emitted by MeleeScoutEnemy must strictly be BossAttackKind.Normal
            if (observedTelegraphKinds.Count < 4 || observedTelegraphKinds.Exists(k => k != BossAttackKind.Normal))
                failures.Add($"melee scout emitted non-Normal attack telegraph: [{string.Join(", ", observedTelegraphKinds)}]");
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckCombatDefenseMatrixAndKillFlow(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "MeleeScoutDefenseProbeStage" };
        AddProbeFloor(probeStage, 500f, 1200f);

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            HitstunSeconds = 0.30f,
            VictoryDelaySeconds = 0f,
            PerfectGuardSeconds = 0.50f,
            PerfectDodgeSeconds = 0.50f,
            GuardDamagePerBlockedHit = 25f
        };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "DefenseProbePlayer",
            Position = new Vector2(400f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var roster = new StageEnemyRoster { Name = "EnemyRoster" };
        roster.BindTimeSlow(timeSlow);

        var scout = new MeleeScoutEnemy
        {
            Name = "DefenseProbeScout",
            Position = new Vector2(485f, ProbeFloorY - 32f),
            SlashTelegraphSeconds = 0f,
            SlashActiveSeconds = 0f,
            SlashRecoverySeconds = 0.15f,
            AttackCooldownSeconds = 0f
        };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(roster);
        probeStage.AddChild(scout);
        host.AddChild(probeStage);

        float totalRefunded = 0f;
        PlayerDefenseCue? lastDefenseCue = null;
        EnemyDamagedCue? lastDamagedCue = null;
        player.TimeResourceRefund += amount =>
        {
            totalRefunded += amount;
            timeSlow.AddResource(amount);
        };
        player.DefenseResolved += cue => lastDefenseCue = cue;
        scout.Damaged += cue => lastDamagedCue = cue;

        try
        {
            scout.Bind(player, windows);
            roster.Register(scout);

            // Settle player onto floor
            player._PhysicsProcess(0.05);

            // 1. Undefended slash -> Hit, player takes full SlashDamage
            float hpBefore = player.Hp;
            RunSingleScoutStrike(scout, player, new Vector2(400f, ProbeFloorY - 23f), new Vector2(485f, ProbeFloorY - 32f));
            if (lastDefenseCue is not { Result: DefenseResult.Hit } ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore - scout.SlashDamage) ||
                scout.State != EnemyState.Recover ||
                scout.HoldsAttackToken)
            {
                failures.Add($"undefended scout slash mismatch: cue={lastDefenseCue} hp={player.Hp}/{hpBefore} scoutState={scout.State} token={scout.HoldsAttackToken}");
            }

            // 2. Normal block -> Blocked, chip damage + guard loss, scout NOT staggered
            windows.PerfectGuardSeconds = -0.1f;
            player.ResetTo(new Vector2(400f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            hpBefore = player.Hp;
            float guardBefore = player.Guard;
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            RunSingleScoutStrike(scout, player, player.Position, new Vector2(485f, ProbeFloorY - 32f));
            float hpAfterBlock = player.Hp;
            float guardAfterBlock = player.Guard;
            SendAction("block", false);
            player._PhysicsProcess(0.016);
            if (lastDefenseCue is not { Result: DefenseResult.Blocked } ||
                !Mathf.IsEqualApprox(hpAfterBlock, hpBefore - scout.SlashDamage * windows.BlockChipRatio) ||
                !Mathf.IsEqualApprox(guardAfterBlock, guardBefore - windows.GuardDamagePerBlockedHit) ||
                scout.State != EnemyState.Recover ||
                scout.HoldsAttackToken)
            {
                failures.Add($"normal block vs scout mismatch: cue={lastDefenseCue} hp={hpAfterBlock}/{hpBefore} guard={guardAfterBlock}/{guardBefore} scoutState={scout.State}");
            }

            // 3. Perfect block -> PerfectBlocked, zero HP/guard loss, scout enters Hitstun & releases token
            windows.PerfectGuardSeconds = 0.50f;
            player.ResetTo(new Vector2(400f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            hpBefore = player.Hp;
            guardBefore = player.Guard;
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            RunSingleScoutStrike(scout, player, player.Position, new Vector2(485f, ProbeFloorY - 32f));
            SendAction("block", false);
            player._PhysicsProcess(0.016);
            if (lastDefenseCue is not { Result: DefenseResult.PerfectBlocked } ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore) ||
                !Mathf.IsEqualApprox(player.Guard, guardBefore) ||
                scout.State != EnemyState.Hitstun ||
                scout.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"perfect block stagger vs scout mismatch: cue={lastDefenseCue} hp={player.Hp}/{hpBefore} guard={player.Guard}/{guardBefore} scoutState={scout.State} token={scout.HoldsAttackToken}");
            }

            // 4. Normal dodge -> Dodged, zero HP loss, zero time slow refund
            windows.PerfectDodgeSeconds = -0.1f;
            player.ResetTo(new Vector2(400f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            hpBefore = player.Hp;
            totalRefunded = 0f;
            SendAction("dodge", true);
            player._Process(0.016);
            player._PhysicsProcess(0.016);
            RunSingleScoutStrike(scout, player, player.Position, player.Position + new Vector2(80f, -9f));
            SendAction("dodge", false);
            if (lastDefenseCue is not { Result: DefenseResult.Dodged } ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore) ||
                !Mathf.IsEqualApprox(totalRefunded, 0f))
            {
                failures.Add($"normal dodge vs scout mismatch: cue={lastDefenseCue} hp={player.Hp}/{hpBefore} refund={totalRefunded}");
            }

            // 5. Perfect dodge -> PerfectDodged, zero HP loss, +20 time slow refund
            windows.PerfectDodgeSeconds = 0.50f;
            player.ResetTo(new Vector2(400f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            hpBefore = player.Hp;
            totalRefunded = 0f;
            SendAction("dodge", true);
            player._Process(0.016);
            player._PhysicsProcess(0.016);
            RunSingleScoutStrike(scout, player, player.Position, player.Position + new Vector2(80f, -9f));
            SendAction("dodge", false);
            if (lastDefenseCue is not { Result: DefenseResult.PerfectDodged } ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore) ||
                !Mathf.IsEqualApprox(totalRefunded, timeSlow.Config.PerfectDodgeRefund))
            {
                failures.Add($"perfect dodge vs scout mismatch: cue={lastDefenseCue} hp={player.Hp}/{hpBefore} refund={totalRefunded}");
            }

            // 6. Full kill flow: 1 (normal slash) + 2 (directional skill) = 3 HP -> Dead -> Victory
            scout.ResetTo(new Vector2(485f, ProbeFloorY - 32f));
            scout._PhysicsProcess(0.016); // Spawn -> Idle
            scout.TakeHit(new EnemyHitContext(player.AttackDamage, player.GlobalPosition, Vector2.Right));
            if (!Mathf.IsEqualApprox(scout.Hp, 2f) || scout.State != EnemyState.Hitstun || !scout.ShowsOverheadHealthBar)
                failures.Add($"first slash kill-flow mismatch: hp={scout.Hp} state={scout.State} bar={scout.ShowsOverheadHealthBar}");

            scout.TakeHit(new EnemyHitContext(player.SkillDamage, player.GlobalPosition, Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: true));
            if (!Mathf.IsEqualApprox(scout.Hp, 0f) ||
                scout.State != EnemyState.Dead ||
                scout.ShowsOverheadHealthBar ||
                lastDamagedCue is not { Defeated: true, RemainingHp: 0f })
            {
                failures.Add($"lethal skill kill-flow mismatch: hp={scout.Hp} state={scout.State} bar={scout.ShowsOverheadHealthBar} cue={lastDamagedCue}");
            }

            scout._PhysicsProcess(0.016); // Dead -> Victory (VictoryDelaySeconds = 0)
            if (scout.State != EnemyState.Victory || roster.AliveCount != 0)
                failures.Add($"post-death victory state mismatch: state={scout.State} alive={roster.AliveCount}");
        }
        finally
        {
            SendAction("block", false);
            SendAction("dodge", false);
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckVisualContract(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "MeleeScoutVisualProbeStage" };
        AddProbeFloor(probeStage, 500f, 800f);

        var windows = new CombatWindowConfig { BossSpawnSeconds = 0f, EnemyInitialDelaySeconds = 0f };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "VisualProbePlayer",
            Position = new Vector2(360f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var scout = new MeleeScoutEnemy
        {
            Name = "VisualProbeScout",
            Position = new Vector2(460f, ProbeFloorY - 32f)
        };

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(scout);
        host.AddChild(probeStage);

        try
        {
            scout.Bind(player, windows);
            scout.BindTimeSlow(timeSlow);

            if (scout.Visual is not MeleeScoutVisual visual ||
                scout.GetNodeOrNull("Visual") is not MeleeScoutVisual ||
                visual.ZIndex != 1 ||
                !visual.IsPlayerBound ||
                !visual.IsTimeSlowBound ||
                !visual.ShouldDrawOverheadHealthBar)
            {
                failures.Add($"visual mount/binding mismatch: type={scout.Visual?.GetType().Name ?? "null"} z={scout.Visual?.ZIndex} pBound={scout.Visual?.IsPlayerBound} tBound={scout.Visual?.IsTimeSlowBound} bar={scout.Visual?.ShouldDrawOverheadHealthBar}");
                return;
            }

            // Low crouched collision box (distinctly shorter than 100px monolith)
            CollisionShape2D? bodyShape = scout.GetNodeOrNull<CollisionShape2D>("BodyShape");
            if (bodyShape?.Shape is not RectangleShape2D rect || rect.Size.Y > 72f)
                failures.Add($"scout BodyShape expected low crouched silhouette (<=72px tall), got {bodyShape?.Shape}");

            // Zero legacy placeholder nodes (ColorRect, Label, Polygon2D, Line2D) anywhere under scout
            CheckNoLegacyNodes(scout, failures);

            // Step through Idle -> Telegraph -> Hitstun -> Dead and ensure visual updates cleanly
            scout._PhysicsProcess(0.016); // Idle
            visual._Process(0.016);
            scout._PhysicsProcess(0.016); // Telegraph
            visual._Process(0.016);
            if (visual.Facing != scout.Facing)
                failures.Add($"visual facing out of sync (visual={visual.Facing}, enemy={scout.Facing})");

            scout.TakeDamage(1f); // Hitstun
            visual._Process(0.016);
            scout.TakeDamage(scout.Hp); // Dead
            visual._Process(0.016);
            if (visual.ShouldDrawOverheadHealthBar)
                failures.Add("overhead health bar should hide when scout is defeated");
        }
        finally
        {
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void RunSingleScoutStrike(
        MeleeScoutEnemy scout,
        PlayerController player,
        Vector2 playerPos,
        Vector2 scoutPos)
    {
        player.Position = playerPos;
        scout.ResetTo(scoutPos);
        scout._PhysicsProcess(0.016); // Spawn -> Idle
        scout._PhysicsProcess(0.016); // Idle -> Telegraph (0s)
        scout._PhysicsProcess(0.016); // Telegraph -> Strike (0s)
        scout._PhysicsProcess(0.016); // Strike resolves -> Recover or Hitstun
    }

    private static void CheckNoLegacyNodes(Node root, List<string> failures)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is ColorRect or Label or Polygon2D or Line2D)
                failures.Add($"legacy placeholder node '{child.Name}' ({child.GetType().Name}) attached under MeleeScoutEnemy");
            CheckNoLegacyNodes(child, failures);
        }
    }

    private static void AddProbeFloor(Node2D stage, float centerX, float width)
    {
        var floor = new StaticBody2D
        {
            Name = "ProbeFloor",
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = new Vector2(centerX, ProbeFloorY + 20f)
        };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(width, 40f) } });
        stage.AddChild(floor);
    }

    private static void SendAction(string action, bool pressed)
    {
        Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
        Godot.Input.FlushBufferedEvents();
    }
}
