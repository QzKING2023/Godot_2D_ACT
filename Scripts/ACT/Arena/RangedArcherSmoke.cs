using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless smoke verifier for Issue #13:
/// - RangedArcherEnemy identity (2 HP, slender floating prism visual, overhead bar, Ranged attack token pool)
/// - Close-range horizontal backstep with platform edge and corner wall safety
/// - Golden aiming line telegraph, ProjectileFired audio cue, EntityProjectile TimeScale slowdown, no friendly fire
/// - Normal block, perfect dodge (+20 refund pass-through), perfect block reflect -> 2 dmg bypassing frontal shields
/// - Clean projectile destruction on reset and stage tree exit
/// </summary>
public static partial class RangedArcherSmoke
{
    private const float ProbeFloorY = -2400f;

    [SmokeProbe(100)]
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckIdentityVisualAndRangedTokenContract(host, failures);
        CheckAimFireTimeSlowWallAndNoFriendlyFire(host, failures);
        CheckBackstepEdgeAndWallSafety(host, failures);
        CheckPlayerProjectileInteractionsAndReflectKill(host, failures);
        CheckResetAndTreeExitProjectileCleanup(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS ranged archer identity, visual, backstep safety, projectile reflection & cleanup contracts");
            return;
        }

        foreach (string reason in failures)
            fail($"ranged-archer: {reason}");
    }

    private static void CheckIdentityVisualAndRangedTokenContract(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "RangedArcherIdentityStage" };
        AddStaticBox(stage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0.5f,
            HitstunSeconds = 0.25f
        };
        var timeSlow = new TimeSlowManager { Name = "TimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(260f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var archer = new RangedArcherEnemy { Position = new Vector2(680f, ProbeFloorY - 42f) };
        var roster = new StageEnemyRoster { Name = "Roster" };

        stage.AddChild(timeSlow);
        stage.AddChild(player);
        stage.AddChild(archer);
        stage.AddChild(roster);
        host.AddChild(stage);

        try
        {
            archer.Bind(player, windows);
            roster.BindTimeSlow(timeSlow);
            roster.Register(archer);

            if (!Mathf.IsEqualApprox(archer.MaxHp, 2f) || !Mathf.IsEqualApprox(archer.Hp, 2f))
                failures.Add($"expected default 2 HP, got {archer.Hp}/{archer.MaxHp}");
            if (archer.IsBoss || !archer.ShowsOverheadHealthBar)
                failures.Add($"expected non-boss with overhead health bar, got IsBoss={archer.IsBoss} ShowsBar={archer.ShowsOverheadHealthBar}");
            if (archer.DisplayName != RangedArcherEnemy.DefaultDisplayName)
                failures.Add($"expected DisplayName '{RangedArcherEnemy.DefaultDisplayName}', got '{archer.DisplayName}'");
            if (archer.AttackTokenPoolKind != AttackTokenPool.Ranged || !archer.UsesEncounterAttackToken)
                failures.Add($"expected Ranged token pool, got pool={archer.AttackTokenPoolKind} uses={archer.UsesEncounterAttackToken}");
            if (archer.Visual is not RangedArcherVisual visual || visual.ZIndex != 1 || !visual.ShouldDrawOverheadHealthBar)
                failures.Add($"expected RangedArcherVisual(ZIndex=1, overhead bar), got {archer.Visual?.GetType().Name ?? "null"}");

            foreach (Node child in archer.GetChildren())
            {
                if (child is ColorRect or Label or Polygon2D or Line2D)
                    failures.Add($"legacy placeholder child '{child.Name}' found on RangedArcherEnemy");
            }

            // Step into Idle then Telegraph -> must acquire Ranged pool token
            for (int i = 0; i < 6 && archer.State != EnemyState.Telegraph; i++)
                archer._PhysicsProcess(0.016);

            if (archer.State != EnemyState.Telegraph ||
                !archer.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveRangedCount != 1 ||
                roster.AttackCoordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"expected Telegraph holding Ranged token: state={archer.State} holds={archer.HoldsAttackToken} ranged={roster.AttackCoordinator.ActiveRangedCount} melee={roster.AttackCoordinator.ActiveMeleeCount}");
            }

            // Interrupt with 1 dmg -> must enter Hitstun and release Ranged token
            EnemyHitOutcome hit1 = archer.TakeHit(new EnemyHitContext(1f, player.GlobalPosition, Vector2.Right));
            if (hit1 != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(archer.Hp, 1f) ||
                archer.State != EnemyState.Hitstun ||
                archer.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveRangedCount != 0)
            {
                failures.Add($"interrupt did not release Ranged token cleanly: outcome={hit1} hp={archer.Hp} state={archer.State} holds={archer.HoldsAttackToken} ranged={roster.AttackCoordinator.ActiveRangedCount}");
            }

            // Lethal 1 dmg -> Dead, overhead bar hidden, token remains released
            EnemyHitOutcome hit2 = archer.TakeHit(new EnemyHitContext(1f, player.GlobalPosition, Vector2.Right));
            if (hit2 != EnemyHitOutcome.Damaged ||
                !archer.IsDefeated ||
                archer.ShowsOverheadHealthBar ||
                archer.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveRangedCount != 0)
            {
                failures.Add($"death contract mismatch: outcome={hit2} dead={archer.IsDefeated} bar={archer.ShowsOverheadHealthBar} ranged={roster.AttackCoordinator.ActiveRangedCount}");
            }
        }
        finally
        {
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckAimFireTimeSlowWallAndNoFriendlyFire(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "RangedArcherFireProbeStage" };
        AddStaticBox(stage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));
        AddStaticBox(stage, "LeftWall", new Rect2(200f, ProbeFloorY - 200f, 40f, 200f));

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 2f
        };
        var timeSlow = new TimeSlowManager { Name = "TimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(120f, ProbeFloorY - 23f), // behind LeftWall so projectile hits wall before player
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var archer = new RangedArcherEnemy
        {
            Position = new Vector2(680f, ProbeFloorY - 42f),
            AimTelegraphSeconds = 0f,
            ShootCooldownSeconds = 3f
        };
        var ally = new EnemyController
        {
            Name = "AllyInLine",
            Position = new Vector2(480f, ProbeFloorY - 50f),
            MaxHp = 5f,
            ChaseSpeed = 0f
        };
        var roster = new StageEnemyRoster { Name = "Roster" };

        stage.AddChild(timeSlow);
        stage.AddChild(player);
        stage.AddChild(archer);
        stage.AddChild(ally);
        stage.AddChild(roster);
        host.AddChild(stage);

        int firedEvents = 0;
        archer.ProjectileFired += _ => firedEvents++;

        try
        {
            archer.Bind(player, windows);
            ally.Bind(player, windows);
            roster.BindTimeSlow(timeSlow);
            roster.Register(archer);
            roster.Register(ally);
            CombatAudioController audio = CombatAudioController.AttachRoster(stage, player, roster, timeSlow);

            // Step through Spawn -> Idle -> Telegraph (0s) -> Strike (fires once)
            for (int i = 0; i < 12; i++)
                archer._PhysicsProcess(0.016);

            EnemyProjectile? projectile = FindFirstProjectile(stage);
            if (firedEvents != 1 || audio.LastCue != CombatSfxId.ProjectileFire || projectile is null)
            {
                failures.Add($"expected 1 projectile & ProjectileFire SFX, got events={firedEvents} sfx={audio.LastCue} projNull={projectile is null}");
                return;
            }

            if (EnemyProjectile.CountActiveIn(stage) != 1 ||
                projectile.Faction != ProjectileFaction.Enemy ||
                !ReferenceEquals(projectile.SourceEnemy, archer) ||
                projectile.Visual is not EnemyProjectileVisual)
            {
                failures.Add($"spawned projectile metadata/visual mismatch: count={EnemyProjectile.CountActiveIn(stage)} faction={projectile.Faction} visual={projectile.Visual?.GetType().Name ?? "null"}");
            }

            // Flight is physics-step driven (not _Process wall-clock)
            Vector2 beforeProcess = projectile.GlobalPosition;
            projectile._Process(0.5);
            if (!projectile.GlobalPosition.IsEqualApprox(beforeProcess))
                failures.Add($"projectile moved during _Process ({beforeProcess} -> {projectile.GlobalPosition}); expected physics-step movement");

            // Normal speed vs TimeSlow (0.35x) distance over equal real-time budget
            Vector2 pNormalStart = projectile.GlobalPosition;
            projectile._PhysicsProcess(0.05);
            float normalDist = pNormalStart.DistanceTo(projectile.GlobalPosition);

            SendAction("time_slow", true);
            timeSlow._Process(0.016);
            bool slowedActive = timeSlow.Active && Mathf.IsEqualApprox((float)Engine.TimeScale, 0.35f);
            Vector2 pSlowStart = projectile.GlobalPosition;
            projectile._PhysicsProcess(0.05 * Engine.TimeScale);
            float slowDist = pSlowStart.DistanceTo(projectile.GlobalPosition);
            SendAction("time_slow", false);
            timeSlow._Process(0.016);

            if (!slowedActive || normalDist <= 5f || Mathf.Abs(slowDist / normalDist - 0.35f) > 0.02f)
            {
                failures.Add($"time slow projectile scaling mismatch: active={slowedActive} normal={normalDist:0.00} slow={slowDist:0.00}");
            }

            // Continue stepping past ally (X=480) into LeftWall (X=240): ally unharmed, wall shatters projectile
            float allyHpBefore = ally.Hp;
            for (int step = 0; step < 90 && !projectile.IsConsumed; step++)
                projectile._PhysicsProcess(0.016);

            if (!Mathf.IsEqualApprox(ally.Hp, allyHpBefore))
                failures.Add($"enemy projectile damaged ally! ally hp {ally.Hp}/{allyHpBefore}");
            if (!projectile.IsConsumed || projectile.GlobalPosition.X < 225f)
                failures.Add($"enemy projectile did not shatter on wall: consumed={projectile.IsConsumed} x={projectile.GlobalPosition.X:0.0}");

            // Unparented archer must NOT emit ProjectileFired when no valid container exists
            var orphanArcher = new RangedArcherEnemy
            {
                Name = "OrphanArcherProbe",
                Position = new Vector2(680f, ProbeFloorY - 42f),
                AimTelegraphSeconds = 0f,
                ShootCooldownSeconds = 3f
            };
            int orphanFiredEvents = 0;
            orphanArcher.ProjectileFired += _ => orphanFiredEvents++;
            try
            {
                orphanArcher.Bind(player, windows);
                for (int i = 0; i < 6; i++)
                    orphanArcher._PhysicsProcess(0.016);
                if (orphanFiredEvents != 0)
                    failures.Add($"unparented archer emitted ProjectileFired without spawning projectile (events={orphanFiredEvents})");
            }
            finally
            {
                orphanArcher.Free();
            }
        }
        finally
        {
            SendAction("time_slow", false);
            TimeSlowManager.EnsureNormalScale();
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckBackstepEdgeAndWallSafety(Node host, List<string> failures)
    {
        // 1. Open ground backstep away from close player + cooldown prevents immediate re-backstep
        var openStage = new Node2D { Name = "RangedArcherBackstepOpenStage" };
        AddStaticBox(openStage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0.5f
        };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(500f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var archer = new RangedArcherEnemy
        {
            Position = new Vector2(600f, ProbeFloorY - 42f),
            CloseAlertRange = 190f,
            BackstepDurationSeconds = 0.25f,
            BackstepCooldownSeconds = 3.0f
        };
        openStage.AddChild(player);
        openStage.AddChild(archer);
        host.AddChild(openStage);

        try
        {
            archer.Bind(player, windows);
            float startX = archer.GlobalPosition.X;
            bool sawBackstep = false;
            for (int i = 0; i < 12; i++)
            {
                archer._PhysicsProcess(0.016);
                if (archer.IsBackstepping)
                    sawBackstep = true;
            }

            if (!sawBackstep || !archer.IsBackstepOnCooldown || archer.GlobalPosition.X <= startX + 25f)
            {
                failures.Add($"open-ground backstep mismatch: saw={sawBackstep} cd={archer.IsBackstepOnCooldown} x={archer.GlobalPosition.X:0.0} (start {startX:0.0})");
            }
        }
        finally
        {
            host.RemoveChild(openStage);
            openStage.Free();
        }

        // 2. High platform edge safety: near right drop-off -> never falls, stands ground and aims
        var ledgeStage = new Node2D { Name = "RangedArcherLedgeStage" };
        float platformTopY = ProbeFloorY - 120f;
        AddStaticBox(ledgeStage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));
        AddStaticBox(ledgeStage, "HighPlatform", new Rect2(480f, platformTopY, 160f, 28f)); // X in [480, 640]

        var ledgePlayer = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(520f, platformTopY - 23f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var edgeArcher = new RangedArcherEnemy
        {
            Position = new Vector2(620f, platformTopY - 42f), // right next to drop-off at X=640
            CloseAlertRange = 190f,
            BackstepDurationSeconds = 0.25f,
            BackstepCooldownSeconds = 3.0f
        };
        var midStepArcher = new RangedArcherEnemy
        {
            Name = "MidStepEdgeArcher",
            Position = new Vector2(596f, platformTopY - 42f), // room to start backstep, must brake before X=640
            CloseAlertRange = 190f,
            BackstepDurationSeconds = 0.40f,
            BackstepCooldownSeconds = 3.0f
        };
        ledgeStage.AddChild(ledgePlayer);
        ledgeStage.AddChild(edgeArcher);
        ledgeStage.AddChild(midStepArcher);
        host.AddChild(ledgeStage);

        try
        {
            edgeArcher.Bind(ledgePlayer, windows);
            midStepArcher.Bind(ledgePlayer, windows);

            bool edgeStartedTelegraph = false;
            for (int i = 0; i < 30; i++)
            {
                edgeArcher._PhysicsProcess(0.016);
                midStepArcher._PhysicsProcess(0.016);
                if (edgeArcher.State == EnemyState.Telegraph)
                    edgeStartedTelegraph = true;
            }

            float maxAllowedY = platformTopY - 38f;
            if (!edgeStartedTelegraph || edgeArcher.GlobalPosition.Y > maxAllowedY || edgeArcher.GlobalPosition.X > 640f)
            {
                failures.Add($"edge archer fell or failed to stand ground: tele={edgeStartedTelegraph} pos={edgeArcher.GlobalPosition} maxY={maxAllowedY:0.0}");
            }

            if (midStepArcher.GlobalPosition.Y > maxAllowedY || midStepArcher.GlobalPosition.X > 640f)
            {
                failures.Add($"mid-backstep archer fell off high platform: pos={midStepArcher.GlobalPosition} maxY={maxAllowedY:0.0}");
            }
        }
        finally
        {
            host.RemoveChild(ledgeStage);
            ledgeStage.Free();
        }

        // 3. Corner wall safety: wall immediately behind archer -> aims & shoots in place without retreating
        var wallStage = new Node2D { Name = "RangedArcherCornerWallStage" };
        AddStaticBox(wallStage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));
        AddStaticBox(wallStage, "RightCornerWall", new Rect2(660f, ProbeFloorY - 200f, 40f, 200f));

        var wallPlayer = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(540f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var wallArcher = new RangedArcherEnemy
        {
            Position = new Vector2(636f, ProbeFloorY - 42f),
            CloseAlertRange = 190f
        };
        wallStage.AddChild(wallPlayer);
        wallStage.AddChild(wallArcher);
        host.AddChild(wallStage);

        try
        {
            wallArcher.Bind(wallPlayer, windows);
            bool wallTelegraphed = false;
            for (int i = 0; i < 10; i++)
            {
                wallArcher._PhysicsProcess(0.016);
                if (wallArcher.State == EnemyState.Telegraph)
                    wallTelegraphed = true;
            }

            if (!wallTelegraphed || wallArcher.IsBackstepping || wallArcher.GlobalPosition.X > 646f)
            {
                failures.Add($"cornered archer did not stand ground and aim: tele={wallTelegraphed} backstepping={wallArcher.IsBackstepping} x={wallArcher.GlobalPosition.X:0.0}");
            }
        }
        finally
        {
            host.RemoveChild(wallStage);
            wallStage.Free();
        }
    }

    private sealed partial class FrontalShieldDummy : EnemyController
    {
        public override bool HasFrontalDefense => true;
        public override bool UsesEncounterAttackToken => false;

        public FrontalShieldDummy()
        {
            Name = "FrontalShieldDummy";
            DisplayName = "重盾探针";
            IsBoss = false;
            MaxHp = 5f;
            ChaseSpeed = 0f;
        }

        protected override void UpdateFacing() => SetFacing(-1);

        protected override void ThinkIdleOrChase()
        {
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "holding shield");
        }
    }

    private static void CheckPlayerProjectileInteractionsAndReflectKill(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "RangedArcherInteractionStage" };
        AddStaticBox(stage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));
        AddStaticBox(stage, "FarLeftWall", new Rect2(40f, ProbeFloorY - 220f, 40f, 220f));

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 9f,
            PerfectGuardSeconds = 0.5f,
            PerfectDodgeSeconds = 0.5f,
            HitstunSeconds = 0.25f,
            GuardDamagePerBlockedHit = 25f
        };
        var timeSlow = new TimeSlowManager { Name = "TimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(260f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var archer = new RangedArcherEnemy
        {
            Position = new Vector2(680f, ProbeFloorY - 42f),
            AimTelegraphSeconds = 0f,
            ShootCooldownSeconds = 9f
        };
        var shieldDummy = new FrontalShieldDummy
        {
            Position = new Vector2(480f, ProbeFloorY - 50f),
            MaxHp = 5f
        };
        var roster = new StageEnemyRoster { Name = "Roster" };

        stage.AddChild(timeSlow);
        stage.AddChild(player);
        stage.AddChild(archer);
        stage.AddChild(shieldDummy);
        stage.AddChild(roster);
        host.AddChild(stage);

        float refundedTotal = 0f;
        int shieldDeflectCount = 0;
        player.TimeResourceRefund += amount => refundedTotal += amount;
        shieldDummy.ShieldDeflected += _ => shieldDeflectCount++;

        try
        {
            archer.Bind(player, windows);
            shieldDummy.Bind(player, windows);
            roster.BindTimeSlow(timeSlow);
            roster.Register(archer);
            roster.Register(shieldDummy);
            CombatAudioController audio = CombatAudioController.AttachRoster(stage, player, roster, timeSlow);

            // Settle actors onto floor
            player._PhysicsProcess(0.05);
            shieldDummy._PhysicsProcess(0.05);

            // 1. Normal block -> destroys projectile, drains guard + chip HP
            windows.PerfectGuardSeconds = -0.1f;
            float hpBefore = player.Hp;
            float guardBefore = player.Guard;
            var blockShot = SpawnDirectProjectile(stage, new Vector2(360f, player.GlobalPosition.Y), Vector2.Left, archer);
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            for (int i = 0; i < 30 && !blockShot.IsConsumed; i++)
                blockShot._PhysicsProcess(0.016);
            float hpAfterBlock = player.Hp;
            float guardAfterBlock = player.Guard;
            SendAction("block", false);
            player._PhysicsProcess(0.016);

            if (!blockShot.IsConsumed ||
                !Mathf.IsEqualApprox(hpAfterBlock, hpBefore - EnemyProjectile.DefaultEnemyDamage * windows.BlockChipRatio) ||
                !Mathf.IsEqualApprox(guardAfterBlock, guardBefore - windows.GuardDamagePerBlockedHit))
            {
                failures.Add($"normal block projectile mismatch: consumed={blockShot.IsConsumed} hp={hpAfterBlock}/{hpBefore} guard={guardAfterBlock}/{guardBefore}");
            }

            // 2. Perfect dodge -> zero damage, exactly +20 refund, projectile passes through harmlessly into FarLeftWall
            player.ResetTo(new Vector2(260f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            windows.PerfectDodgeSeconds = 0.5f;
            hpBefore = player.Hp;
            refundedTotal = 0f;
            var dodgeShot = SpawnDirectProjectile(stage, new Vector2(310f, player.GlobalPosition.Y), Vector2.Left, archer);
            SendAction("dodge", true);
            player._Process(0.016);
            player._PhysicsProcess(0.016);
            SendAction("dodge", false);

            // Step projectile across player's entire width (must pass through without consuming and without double-refunding)
            bool passedBehindPlayer = false;
            for (int i = 0; i < 20 && !dodgeShot.IsConsumed; i++)
            {
                dodgeShot._PhysicsProcess(0.016);
                if (dodgeShot.GlobalPosition.X < player.GlobalPosition.X - 30f)
                {
                    passedBehindPlayer = true;
                    break;
                }
            }

            if (dodgeShot.IsConsumed || !passedBehindPlayer || !Mathf.IsEqualApprox(player.Hp, hpBefore) || !Mathf.IsEqualApprox(refundedTotal, 20f))
            {
                failures.Add($"perfect dodge pass-through mismatch: consumed={dodgeShot.IsConsumed} passed={passedBehindPlayer} hp={player.Hp}/{hpBefore} refund={refundedTotal}");
            }
            dodgeShot.Consume();

            // 3. Perfect block reflect -> ProjectileReflect SFX, archer NOT staggered at distance,
            //    reflected golden projectile bypasses FrontalShieldDummy's frontal shield for 2 dmg & stops at first enemy
            player.ResetTo(new Vector2(260f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            windows.PerfectGuardSeconds = 0.5f;
            shieldDeflectCount = 0;
            float dummyHpBefore = shieldDummy.Hp;
            float archerHpBefore = archer.Hp;

            // Fire from archer toward player (passes safely through shieldDummy as enemy projectile)
            for (int i = 0; i < 8; i++)
                archer._PhysicsProcess(0.016);
            EnemyProjectile? liveShot = FindFirstProjectile(stage);
            if (liveShot is null)
            {
                failures.Add("archer failed to spawn projectile for reflect test");
                return;
            }

            // Advance projectile until cleanly in front of player (outside hit radius), then press perfect block
            for (int i = 0; i < 80 && !liveShot.IsConsumed && liveShot.GlobalPosition.X > player.GlobalPosition.X + 45f; i++)
                liveShot._PhysicsProcess(0.016);

            SendAction("block", true);
            player._PhysicsProcess(0.016);
            for (int i = 0; i < 10 && liveShot.Faction == ProjectileFaction.Enemy && !liveShot.IsConsumed; i++)
                liveShot._PhysicsProcess(0.016);
            SendAction("block", false);
            player._PhysicsProcess(0.016);

            if (liveShot.IsConsumed ||
                liveShot.Faction != ProjectileFaction.PlayerReflected ||
                liveShot.Direction.X <= 0.8f ||
                audio.LastCue != CombatSfxId.ProjectileReflect ||
                archer.State == EnemyState.Hitstun)
            {
                failures.Add($"perfect block reflect state mismatch: consumed={liveShot.IsConsumed} faction={liveShot.Faction} dir={liveShot.Direction} sfx={audio.LastCue} archerState={archer.State}");
                return;
            }

            // Step reflected projectile into FrontalShieldDummy (must bypass frontal shield for 2 dmg and consume before reaching archer)
            for (int i = 0; i < 60 && !liveShot.IsConsumed; i++)
                liveShot._PhysicsProcess(0.016);

            if (!liveShot.IsConsumed ||
                shieldDeflectCount != 0 ||
                !Mathf.IsEqualApprox(shieldDummy.Hp, dummyHpBefore - 2f) ||
                !Mathf.IsEqualApprox(archer.Hp, archerHpBefore))
            {
                failures.Add($"reflected frontal-shield bypass mismatch: consumed={liveShot.IsConsumed} deflects={shieldDeflectCount} dummyHp={shieldDummy.Hp}/{dummyHpBefore} archerHp={archer.Hp}/{archerHpBefore}");
            }

            // 4. Clear lane reflect -> one-shot kills full-HP (2 HP) RangedArcherEnemy!
            stage.RemoveChild(shieldDummy);
            shieldDummy.Free();

            player.ResetTo(new Vector2(260f, ProbeFloorY - 23f));
            player._PhysicsProcess(0.05);
            var killShot = SpawnDirectProjectile(stage, new Vector2(310f, player.GlobalPosition.Y), Vector2.Left, archer);
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            for (int i = 0; i < 10 && killShot.Faction == ProjectileFaction.Enemy && !killShot.IsConsumed; i++)
                killShot._PhysicsProcess(0.016);
            SendAction("block", false);
            player._PhysicsProcess(0.016);

            for (int i = 0; i < 80 && !killShot.IsConsumed; i++)
                killShot._PhysicsProcess(0.016);

            if (!killShot.IsConsumed || !archer.IsDefeated || !Mathf.IsEqualApprox(archer.Hp, 0f))
            {
                failures.Add($"reflected shot failed to one-shot kill 2 HP archer: consumed={killShot.IsConsumed} dead={archer.IsDefeated} hp={archer.Hp}");
            }
        }
        finally
        {
            SendAction("block", false);
            SendAction("dodge", false);
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckResetAndTreeExitProjectileCleanup(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "RangedArcherCleanupStage" };
        AddStaticBox(stage, "Floor", new Rect2(0f, ProbeFloorY, 1280f, 80f));

        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 9f
        };
        var player = new PlayerController
        {
            Name = "Player",
            Position = new Vector2(260f, ProbeFloorY - 23f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var archer = new RangedArcherEnemy
        {
            Position = new Vector2(680f, ProbeFloorY - 42f),
            AimTelegraphSeconds = 0f,
            ShootCooldownSeconds = 9f
        };
        var roster = new StageEnemyRoster { Name = "Roster" };

        stage.AddChild(player);
        stage.AddChild(archer);
        stage.AddChild(roster);
        host.AddChild(stage);

        try
        {
            archer.Bind(player, windows);
            roster.Register(archer);

            // 1. archer.ResetTo destroys its active projectile
            for (int i = 0; i < 8; i++)
                archer._PhysicsProcess(0.016);
            if (EnemyProjectile.CountActiveIn(stage) != 1)
                failures.Add($"cleanup setup expected 1 projectile, got {EnemyProjectile.CountActiveIn(stage)}");

            archer.ResetTo(new Vector2(680f, ProbeFloorY - 42f));
            if (EnemyProjectile.CountActiveIn(stage) != 0)
                failures.Add($"archer.ResetTo did not clear active projectiles (remaining={EnemyProjectile.CountActiveIn(stage)})");

            // 2. StageEnemyRoster.Clear destroys all in-flight projectiles on the stage
            SpawnDirectProjectile(stage, new Vector2(520f, ProbeFloorY - 42f), Vector2.Left, archer);
            SpawnDirectProjectile(stage, new Vector2(600f, ProbeFloorY - 42f), Vector2.Left, null);
            if (EnemyProjectile.CountActiveIn(stage) != 2)
                failures.Add($"roster clear setup expected 2 projectiles, got {EnemyProjectile.CountActiveIn(stage)}");

            roster.Clear();
            if (EnemyProjectile.CountActiveIn(stage) != 0)
                failures.Add($"StageEnemyRoster.Clear did not clear in-flight stage projectiles (remaining={EnemyProjectile.CountActiveIn(stage)})");

            // 3. Stage tree exit cleans up all in-flight projectiles with zero leaked group entries
            var exitShot = SpawnDirectProjectile(stage, new Vector2(540f, ProbeFloorY - 42f), Vector2.Left, null);
            int groupBeforeExit = host.GetTree().GetNodesInGroup(EnemyProjectile.GroupName).Count;
            host.RemoveChild(stage);
            stage.Free();
            stage = null!;

            int groupAfterExit = host.GetTree().GetNodesInGroup(EnemyProjectile.GroupName).Count;
            if (GodotObject.IsInstanceValid(exitShot) || groupAfterExit >= groupBeforeExit)
            {
                failures.Add($"stage exit did not cleanly destroy projectile: valid={GodotObject.IsInstanceValid(exitShot)} group={groupBeforeExit}->{groupAfterExit}");
            }
        }
        finally
        {
            if (stage is not null && GodotObject.IsInstanceValid(stage))
            {
                if (stage.GetParent() == host)
                    host.RemoveChild(stage);
                stage.Free();
            }
        }
    }

    private static EnemyProjectile SpawnDirectProjectile(Node stage, Vector2 origin, Vector2 dir, EnemyController? source)
    {
        var proj = new EnemyProjectile { Name = $"DirectProjectile_{Time.GetTicksUsec()}" };
        proj.Initialize(origin, dir, EnemyProjectile.DefaultEnemySpeed, EnemyProjectile.DefaultEnemyDamage, source);
        stage.AddChild(proj);
        return proj;
    }

    private static EnemyProjectile? FindFirstProjectile(Node scope)
    {
        foreach (Node child in scope.GetChildren())
        {
            if (child is EnemyProjectile p && !p.IsConsumed)
                return p;
        }
        return null;
    }

    private static void SendAction(string action, bool pressed)
    {
        Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
        Godot.Input.FlushBufferedEvents();
    }

    private static void AddStaticBox(Node parent, string name, Rect2 rect)
    {
        var body = new StaticBody2D
        {
            Name = name,
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = rect.Position + rect.Size * 0.5f
        };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        parent.AddChild(body);
    }
}
