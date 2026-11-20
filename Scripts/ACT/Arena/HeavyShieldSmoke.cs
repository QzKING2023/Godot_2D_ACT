using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless smoke verifier for Issue #14 (重盾卫兵、定向巨盾偏转与破盾失衡博弈).
/// Uses isolated synchronous probes so zero extra physics frames are consumed in SmokeTest.
/// </summary>
public static class HeavyShieldSmoke
{
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckIdentityVisualAndFacingContract(host, failures);
        CheckDeflectionAndBackPierceContract(host, failures);
        CheckTokenPoolAndAlternatingAttacksContract(host, failures);
        CheckPerfectBlockShieldBreakAndFrontalKillContract(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS heavy shield guard (5 HP, facing delay/lock, frontal deflection, back/centerline pierce, crimson charge, shield-break kill)");
            return;
        }

        foreach (string reason in failures)
            fail($"heavy-shield: {reason}");
    }

    private static void CheckIdentityVisualAndFacingContract(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "HeavyShieldIdentityProbeStage" };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 99f,
            NormalAttackTelegraphSeconds = 0.25f,
            NormalAttackActiveSeconds = 0.15f,
            NormalAttackRecoverySeconds = 0.25f,
            EnemyAttackRange = 200f
        };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(320f, 500f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var guard = new HeavyShieldEnemy
        {
            Position = new Vector2(460f, 500f),
            Windows = windows
        };

        stage.AddChild(player);
        stage.AddChild(guard);
        host.AddChild(stage);

        try
        {
            // Default stats & identity
            if (!Mathf.IsEqualApprox(guard.MaxHp, 5f) ||
                guard.IsBoss ||
                guard.DisplayName != HeavyShieldEnemy.DefaultDisplayName ||
                !guard.HasFrontalDefense ||
                !guard.IsFrontalDefenseActive ||
                guard.AttackTokenPoolKind != AttackTokenPool.Melee ||
                !guard.UsesEncounterAttackToken ||
                !guard.ShowsOverheadHealthBar)
            {
                failures.Add($"identity mismatch: hp={guard.MaxHp} boss={guard.IsBoss} name='{guard.DisplayName}' frontal={guard.HasFrontalDefense}/{guard.IsFrontalDefenseActive} pool={guard.AttackTokenPoolKind} token={guard.UsesEncounterAttackToken} bar={guard.ShowsOverheadHealthBar}");
            }

            if (guard.Visual is not HeavyShieldVisual visual ||
                visual.ZIndex != 1 ||
                !visual.IsShieldRaised ||
                visual.IsShieldDrooped ||
                visual.HasVisibleShieldCrack)
            {
                failures.Add($"visual seam mismatch: type={guard.Visual?.GetType().Name ?? "null"} z={guard.Visual?.ZIndex ?? -1}");
            }

            // Ensure no legacy placeholder child nodes on enemy
            foreach (Node child in guard.GetChildren())
            {
                if (child is ColorRect or Label or Polygon2D or Line2D)
                    failures.Add($"legacy visual node found on HeavyShieldEnemy: {child.GetType().Name}");
            }

            guard.Bind(player, windows);
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard.Visual?._Process(0.016);

            if (guard.Facing != -1)
                failures.Add($"expected initial facing -1 toward left player, got {guard.Facing}");

            if (guard.Visual is HeavyShieldVisual boundVisual && boundVisual.ShieldCenterOffsetX >= 0f)
                failures.Add($"expected shield on facing (-1) side, got offsetX={boundVisual.ShieldCenterOffsetX}");

            // Turn delay check: move player behind guard (right side) while Idle/Chase
            guard.TurnDelaySeconds = 0.05f;
            player.Position = new Vector2(620f, 500f);
            guard._PhysicsProcess(0.016);
            if (guard.Facing != -1)
                failures.Add($"facing flipped immediately without minimum turn delay (got {guard.Facing})");

            OS.DelayMsec(65);
            guard._PhysicsProcess(0.016);
            if (guard.Facing != 1)
                failures.Add($"facing did not flip after minimum turn delay elapsed (got {guard.Facing})");

            guard.Visual?._Process(0.016);
            if (guard.Visual is HeavyShieldVisual turnedVisual && turnedVisual.ShieldCenterOffsetX <= 0f)
                failures.Add($"shield side did not follow facing (+1), got offsetX={turnedVisual.ShieldCenterOffsetX}");

            // Facing lock during Telegraph, Strike, and Recover (even with TurnDelaySeconds = 0)
            var fastWindows = new CombatWindowConfig
            {
                BossSpawnSeconds = 0f,
                EnemyInitialDelaySeconds = 0f,
                EnemyAttackCooldown = 99f,
                NormalAttackTelegraphSeconds = 0.04f,
                NormalAttackActiveSeconds = 0.04f,
                NormalAttackRecoverySeconds = 0.04f,
                EnemyAttackRange = 220f
            };
            guard.TurnDelaySeconds = 0f;
            player.Position = new Vector2(360f, 500f);
            guard.Bind(player, fastWindows);
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard._PhysicsProcess(0.016); // Idle -> Telegraph (facing -1)

            if (guard.State != EnemyState.Telegraph || guard.Facing != -1 || !guard.IsFacingLocked)
                failures.Add($"expected locked Telegraph facing -1, got state={guard.State} facing={guard.Facing} locked={guard.IsFacingLocked}");

            // Move player behind guard during Telegraph
            player.Position = new Vector2(620f, 500f);
            guard._PhysicsProcess(0.016);
            if (guard.Facing != -1)
                failures.Add($"facing unlocked during Telegraph (flipped to {guard.Facing})");

            // Advance into Strike while player stays behind
            OS.DelayMsec(50);
            guard._PhysicsProcess(0.016); // Telegraph -> Strike
            if (guard.State != EnemyState.Strike || guard.Facing != -1 || !guard.IsFacingLocked)
                failures.Add($"expected locked Strike facing -1, got state={guard.State} facing={guard.Facing} locked={guard.IsFacingLocked}");

            guard._PhysicsProcess(0.016); // mid-Strike
            if (guard.Facing != -1)
                failures.Add($"facing unlocked during Strike (flipped to {guard.Facing})");

            // Advance into Recover while player stays behind
            OS.DelayMsec(50);
            guard._PhysicsProcess(0.016); // Strike -> Recover
            if (guard.State != EnemyState.Recover || guard.Facing != -1 || !guard.IsFacingLocked)
                failures.Add($"expected locked Recover facing -1, got state={guard.State} facing={guard.Facing} locked={guard.IsFacingLocked}");

            guard._PhysicsProcess(0.016); // mid-Recover
            if (guard.Facing != -1)
                failures.Add($"facing unlocked during Recover (flipped to {guard.Facing})");

            // Advance out of Recover -> Idle; now facing can flip (TurnDelaySeconds == 0)
            OS.DelayMsec(50);
            guard._PhysicsProcess(0.016); // Recover -> Idle
            guard._PhysicsProcess(0.016); // Idle updates facing
            if (guard.State is EnemyState.Telegraph or EnemyState.Strike or EnemyState.Recover || guard.Facing != 1)
                failures.Add($"expected facing to unlock after Recover (state={guard.State}, facing={guard.Facing})");
        }
        finally
        {
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckDeflectionAndBackPierceContract(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "HeavyShieldDeflectionProbeStage" };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 99f,
            NormalAttackTelegraphSeconds = 0.60f,
            NormalAttackActiveSeconds = 0.15f,
            NormalAttackRecoverySeconds = 0.25f,
            HitstunSeconds = 0.05f,
            EnemyAttackRange = 220f
        };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var camera = new Camera2D { Name = "ProbeCamera" };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(340f, 500f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var guard = new HeavyShieldEnemy
        {
            Position = new Vector2(480f, 500f),
            Windows = windows
        };
        var roster = new StageEnemyRoster { Name = "ProbeRoster" };
        roster.BindTimeSlow(timeSlow);

        stage.AddChild(timeSlow);
        stage.AddChild(camera);
        stage.AddChild(player);
        stage.AddChild(guard);
        stage.AddChild(roster);
        host.AddChild(stage);

        int deflectEvents = 0;
        int damageEvents = 0;
        roster.EnemyShieldDeflected += _ => deflectEvents++;
        roster.EnemyDamaged += _ => damageEvents++;

        var feedback = CombatFeedbackLayer.AttachRoster(stage, player, roster, timeSlow, camera);
        var audio = CombatAudioController.AttachRoster(stage, player, roster, timeSlow);

        try
        {
            guard.Bind(player, windows);
            roster.Register(guard);
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard._PhysicsProcess(0.016); // Idle -> Telegraph (facing -1)

            if (guard.State != EnemyState.Telegraph || guard.Facing != -1)
                failures.Add($"deflection probe expected Telegraph facing -1, got state={guard.State} facing={guard.Facing}");

            // 1. Frontal normal slash (1 dmg) -> deflected, 0 dmg, Telegraph uninterrupted, sparks + ring + ShieldDeflect SFX
            int particlesBefore = feedback.ActiveParticleCount;
            int ringsBefore = feedback.ActiveRingCount;
            var frontSlash = new EnemyHitContext(
                Damage: player.AttackDamage,
                AttackerOrigin: new Vector2(360f, 500f),
                MotionDirection: Vector2.Right,
                IsDirectionalSkill: false,
                CrossedTargetCenterline: false);
            EnemyHitOutcome frontSlashOutcome = guard.TakeHit(frontSlash);
            if (frontSlashOutcome != EnemyHitOutcome.Deflected ||
                !Mathf.IsEqualApprox(guard.Hp, 5f) ||
                guard.State != EnemyState.Telegraph ||
                deflectEvents != 1 ||
                damageEvents != 0 ||
                feedback.ActiveParticleCount <= particlesBefore ||
                feedback.ActiveRingCount <= ringsBefore ||
                audio.LastCue != CombatSfxId.ShieldDeflect)
            {
                failures.Add($"frontal slash deflection mismatch: outcome={frontSlashOutcome} hp={guard.Hp} state={guard.State} deflects={deflectEvents} particles={feedback.ActiveParticleCount}/{particlesBefore} rings={feedback.ActiveRingCount}/{ringsBefore} sfx={audio.LastCue}");
            }

            // 2. Uncrossed frontal directional skill (2 dmg) -> deflected, 0 dmg, Telegraph still uninterrupted
            particlesBefore = feedback.ActiveParticleCount;
            var uncrossedSkill = new EnemyHitContext(
                Damage: player.SkillDamage,
                AttackerOrigin: new Vector2(420f, 500f),
                MotionDirection: Vector2.Right,
                IsDirectionalSkill: true,
                CrossedTargetCenterline: false);
            EnemyHitOutcome uncrossedOutcome = guard.TakeHit(uncrossedSkill);
            if (uncrossedOutcome != EnemyHitOutcome.Deflected ||
                !Mathf.IsEqualApprox(guard.Hp, 5f) ||
                guard.State != EnemyState.Telegraph ||
                deflectEvents != 2 ||
                feedback.ActiveParticleCount <= particlesBefore)
            {
                failures.Add($"uncrossed frontal skill deflection mismatch: outcome={uncrossedOutcome} hp={guard.Hp} state={guard.State} deflects={deflectEvents}");
            }

            // 3. Directional skill crossing horizontal centerline (2 dmg) -> full 2 damage (5 -> 3) + damaged event
            var crossedSkill = new EnemyHitContext(
                Damage: player.SkillDamage,
                AttackerOrigin: new Vector2(510f, 500f),
                MotionDirection: Vector2.Right,
                IsDirectionalSkill: true,
                CrossedTargetCenterline: true);
            EnemyHitOutcome crossedOutcome = guard.TakeHit(crossedSkill);
            if (crossedOutcome != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(guard.Hp, 3f) ||
                damageEvents != 1 ||
                deflectEvents != 2)
            {
                failures.Add($"centerline-crossing skill mismatch: outcome={crossedOutcome} hp={guard.Hp} dmgEvents={damageEvents} deflects={deflectEvents}");
            }

            // 4. Back normal slash (1 dmg) -> full 1 damage (3 -> 2) + damaged event
            var backSlash = new EnemyHitContext(
                Damage: player.AttackDamage,
                AttackerOrigin: new Vector2(560f, 500f),
                MotionDirection: Vector2.Left,
                IsDirectionalSkill: false,
                CrossedTargetCenterline: false);
            EnemyHitOutcome backOutcome = guard.TakeHit(backSlash);
            if (backOutcome != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(guard.Hp, 2f) ||
                damageEvents != 2 ||
                deflectEvents != 2)
            {
                failures.Add($"back normal slash mismatch: outcome={backOutcome} hp={guard.Hp} dmgEvents={damageEvents} deflects={deflectEvents}");
            }
        }
        finally
        {
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckTokenPoolAndAlternatingAttacksContract(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "HeavyShieldAttacksProbeStage" };
        AddProbeFloor(stage, centerX: 500f, floorTopY: 560f, width: 900f);
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
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
            PerfectGuardSeconds = 0.50f,
            EnemyAttackRange = 220f
        };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(360f, 537f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var guard = new HeavyShieldEnemy
        {
            Position = new Vector2(480f, 510f),
            Windows = windows
        };
        var blocker1 = new EnemyController { Name = "MeleeBlocker1", Position = new Vector2(700f, 510f), Windows = windows };
        var blocker2 = new EnemyController { Name = "MeleeBlocker2", Position = new Vector2(740f, 510f), Windows = windows };
        var roster = new StageEnemyRoster { Name = "ProbeRoster" };
        roster.BindTimeSlow(timeSlow);

        stage.AddChild(timeSlow);
        stage.AddChild(player);
        stage.AddChild(guard);
        stage.AddChild(blocker1);
        stage.AddChild(blocker2);
        stage.AddChild(roster);
        host.AddChild(stage);

        var observedKinds = new List<BossAttackKind>();
        guard.TelegraphStarted += cue => observedKinds.Add(cue.Attack.Kind);

        try
        {
            guard.Bind(player, windows);
            blocker1.Bind(player, windows);
            blocker2.Bind(player, windows);
            roster.RegisterRange(new EnemyController[] { guard, blocker1, blocker2 });

            guard._PhysicsProcess(0.016); // Spawn -> Idle

            // 1. Saturate Melee pool -> HeavyShieldEnemy must wait without acquiring or telegraphing
            EncounterAttackCoordinator coordinator = roster.AttackCoordinator;
            float now = Time.GetTicksMsec() / 1000f;
            coordinator.TryAcquire(blocker1, AttackTokenPool.Melee, now + 0.60f);
            coordinator.TryAcquire(blocker2, AttackTokenPool.Melee, now + 1.00f);
            guard._PhysicsProcess(0.016);
            if (guard.State == EnemyState.Telegraph || guard.HoldsAttackToken || observedKinds.Count != 0)
                failures.Add($"guard bypassed saturated melee token pool (state={guard.State}, holds={guard.HoldsAttackToken}, telegraphs={observedKinds.Count})");

            coordinator.Release(blocker1);
            coordinator.Release(blocker2);

            // 2. Alternation: Normal (Golden Shield Slam) -> BreakGrab (Crimson Shield Charge) -> Normal
            // Also verify crimson shield charge has forward lunge velocity and breaks held player block
            bool sawChargeForwardLunge = false;
            bool verifiedCrimsonBreaksBlock = false;
            for (int step = 0; step < 30 && observedKinds.Count < 3; step++)
            {
                if (guard.State == EnemyState.Telegraph && guard.CurrentAttackKind == BossAttackKind.BreakGrab && !verifiedCrimsonBreaksBlock)
                {
                    // Player holds block (even inside perfect guard window!) against crimson shield charge -> must STILL take HP damage (unblockable)
                    player.ResetTo(new Vector2(360f, 537f));
                    player._PhysicsProcess(0.05);
                    float hpBeforeCharge = player.Hp;
                    SendAction("block", true);
                    player._PhysicsProcess(0.016);
                    if (player.State != PlayerState.Block)
                        failures.Add($"player failed to enter Block before crimson shield charge (state={player.State})");

                    guard._PhysicsProcess(0.016); // Telegraph -> Strike (sets forward lunge velocity)
                    if (guard.State == EnemyState.Strike && guard.Velocity.X * guard.Facing > 100f)
                        sawChargeForwardLunge = true;

                    guard._PhysicsProcess(0.016); // Strike resolves against blocking player -> Recover
                    SendAction("block", false);
                    player._PhysicsProcess(0.016);

                    if (player.Hp < hpBeforeCharge)
                        verifiedCrimsonBreaksBlock = true;
                    continue;
                }

                guard._PhysicsProcess(0.016);
                player.ResetTo(new Vector2(360f, 537f));
            }

            if (observedKinds.Count < 3 ||
                observedKinds[0] != BossAttackKind.Normal ||
                observedKinds[1] != BossAttackKind.BreakGrab ||
                observedKinds[2] != BossAttackKind.Normal)
            {
                failures.Add($"expected alternating Normal -> BreakGrab -> Normal, got [{string.Join(", ", observedKinds)}]");
            }

            if (!sawChargeForwardLunge)
                failures.Add("crimson BreakGrab shield charge did not apply forward lunge velocity during Strike");

            if (!verifiedCrimsonBreaksBlock)
                failures.Add("crimson BreakGrab shield charge failed to break/bypass player block");

            // 3. ResetTo resets cycle back to Normal
            observedKinds.Clear();
            guard.ResetTo(new Vector2(480f, 510f));
            player.ResetTo(new Vector2(360f, 537f));
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard._PhysicsProcess(0.016); // Idle -> Telegraph
            if (observedKinds.Count == 0 || observedKinds[0] != BossAttackKind.Normal)
                failures.Add($"ResetTo did not reset alternation back to Normal, got [{string.Join(", ", observedKinds)}]");
        }
        finally
        {
            SendAction("block", false);
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void CheckPerfectBlockShieldBreakAndFrontalKillContract(Node host, List<string> failures)
    {
        var stage = new Node2D { Name = "HeavyShieldBreakKillProbeStage" };
        AddProbeFloor(stage, centerX: 500f, floorTopY: 560f, width: 900f);
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 99f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0.05f,
            NormalAttackRecoverySeconds = 0.05f,
            HitstunSeconds = 0.04f,
            PerfectGuardSeconds = 0.50f,
            EnemyAttackRange = 220f
        };
        var player = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(360f, 537f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var guard = new HeavyShieldEnemy
        {
            Position = new Vector2(480f, 510f),
            Windows = windows,
            ShieldBreakDurationSeconds = 0.25f
        };
        var roster = new StageEnemyRoster { Name = "ProbeRoster" };
        roster.BindTimeSlow(timeSlow);

        stage.AddChild(timeSlow);
        stage.AddChild(player);
        stage.AddChild(guard);
        stage.AddChild(roster);
        host.AddChild(stage);

        int deflectEvents = 0;
        roster.EnemyShieldDeflected += _ => deflectEvents++;

        try
        {
            // Verify default shield-break duration is strictly longer than default hitstun
            if (HeavyShieldEnemy.DefaultShieldBreakDurationSeconds <= new CombatWindowConfig().HitstunSeconds)
                failures.Add($"DefaultShieldBreakDurationSeconds ({HeavyShieldEnemy.DefaultShieldBreakDurationSeconds}) must exceed default HitstunSeconds");

            guard.Bind(player, windows);
            roster.Register(guard);

            // Step into Golden Shield Slam (Normal) Telegraph holding melee token
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard._PhysicsProcess(0.016); // Idle -> Telegraph (Normal)
            if (guard.State != EnemyState.Telegraph ||
                guard.CurrentAttackKind != BossAttackKind.Normal ||
                !guard.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveMeleeCount != 1)
            {
                failures.Add($"shield-break setup failed (state={guard.State}, kind={guard.CurrentAttackKind}, holds={guard.HoldsAttackToken}, activeMelee={roster.AttackCoordinator.ActiveMeleeCount})");
            }

            // Player raises frontal perfect block right before strike resolution
            player.ResetTo(new Vector2(360f, 537f));
            player._PhysicsProcess(0.05);
            float playerHpBefore = player.Hp;
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            if (player.State != PlayerState.Block)
                failures.Add($"player failed to enter Block before golden shield slam (state={player.State})");

            guard._PhysicsProcess(0.016); // Telegraph -> Strike
            guard._PhysicsProcess(0.016); // Strike resolves against frontal perfect block -> ShieldBroken!

            SendAction("block", false);
            player._PhysicsProcess(0.016);

            if (!Mathf.IsEqualApprox(player.Hp, playerHpBefore))
                failures.Add($"perfect block took unexpected HP damage ({player.Hp}/{playerHpBefore})");

            if (guard.State != EnemyState.ShieldBroken ||
                !guard.IsShieldBroken ||
                guard.IsFrontalDefenseActive ||
                guard.HoldsAttackToken ||
                roster.AttackCoordinator.ActiveMeleeCount != 0)
            {
                failures.Add($"frontal perfect block did not enter ShieldBroken & release token (state={guard.State}, broken={guard.IsShieldBroken}, frontalActive={guard.IsFrontalDefenseActive}, holds={guard.HoldsAttackToken}, activeMelee={roster.AttackCoordinator.ActiveMeleeCount})");
            }

            guard.Visual?._Process(0.016);
            if (guard.Visual is not HeavyShieldVisual brokenVisual ||
                !brokenVisual.IsShieldDrooped ||
                brokenVisual.IsShieldRaised ||
                !brokenVisual.HasVisibleShieldCrack ||
                brokenVisual.ShieldDroopOffsetY <= 0f)
            {
                failures.Add("HeavyShieldVisual did not render drooped cracked shield during ShieldBroken");
            }

            // Duration must exceed HitstunSeconds (0.04s): wait 55ms -> still ShieldBroken
            OS.DelayMsec(55);
            guard._PhysicsProcess(0.016);
            if (guard.State != EnemyState.ShieldBroken || guard.IsFrontalDefenseActive)
                failures.Add($"ShieldBroken ended earlier than HitstunSeconds+margin (state={guard.State})");

            // Full frontal + back kill during ShieldBroken: non-lethal hits must NOT cancel ShieldBroken!
            var frontSlash = new EnemyHitContext(player.AttackDamage, new Vector2(360f, 500f), Vector2.Right);
            var frontSkill = new EnemyHitContext(player.SkillDamage, new Vector2(400f, 500f), Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: false);
            var backSlash = new EnemyHitContext(player.AttackDamage, new Vector2(560f, 500f), Vector2.Left);

            // Hit 1: Frontal normal slash (5 -> 4), must remain ShieldBroken!
            EnemyHitOutcome hit1 = guard.TakeHit(frontSlash);
            if (hit1 != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(guard.Hp, 4f) ||
                guard.State != EnemyState.ShieldBroken ||
                guard.IsFrontalDefenseActive)
            {
                failures.Add($"first frontal hit during ShieldBroken failed or exited ShieldBroken (outcome={hit1}, hp={guard.Hp}, state={guard.State}, frontalActive={guard.IsFrontalDefenseActive})");
            }

            // Hit 2: Uncrossed frontal skill (4 -> 2), must still deal full 2 damage & remain ShieldBroken
            EnemyHitOutcome hit2 = guard.TakeHit(frontSkill);
            if (hit2 != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(guard.Hp, 2f) ||
                guard.State != EnemyState.ShieldBroken)
            {
                failures.Add($"frontal skill during ShieldBroken failed (outcome={hit2}, hp={guard.Hp}, state={guard.State})");
            }

            // Hit 3: Back slash (2 -> 1), full 1 damage
            EnemyHitOutcome hit3 = guard.TakeHit(backSlash);
            if (hit3 != EnemyHitOutcome.Damaged || !Mathf.IsEqualApprox(guard.Hp, 1f))
                failures.Add($"back slash during ShieldBroken failed (outcome={hit3}, hp={guard.Hp})");

            // Hit 4: Final frontal slash (1 -> 0 -> Dead)
            EnemyHitOutcome hit4 = guard.TakeHit(frontSlash);
            if (hit4 != EnemyHitOutcome.Damaged ||
                !Mathf.IsEqualApprox(guard.Hp, 0f) ||
                guard.State != EnemyState.Dead ||
                !guard.IsDefeated ||
                deflectEvents != 0)
            {
                failures.Add($"frontal kill during ShieldBroken failed (outcome={hit4}, hp={guard.Hp}, state={guard.State}, deflects={deflectEvents})");
            }

            // Recovery check: reset guard, enter short ShieldBroken, confirm shield restores after expiry
            guard.ResetTo(new Vector2(480f, 500f));
            guard._PhysicsProcess(0.016); // Spawn -> Idle
            guard.EnterShieldBreak(0.04f, "recovery check");
            if (!guard.IsShieldBroken || guard.IsFrontalDefenseActive)
                failures.Add("EnterShieldBreak did not deactivate frontal defense");

            OS.DelayMsec(55);
            guard._PhysicsProcess(0.016); // ShieldBroken -> Idle
            guard.Visual?._Process(0.016);
            if (guard.IsShieldBroken ||
                !guard.IsFrontalDefenseActive ||
                guard.Visual is HeavyShieldVisual restoredVisual && (!restoredVisual.IsShieldRaised || restoredVisual.IsShieldDrooped))
            {
                failures.Add($"shield did not restore after ShieldBroken expired (state={guard.State}, frontalActive={guard.IsFrontalDefenseActive})");
            }
        }
        finally
        {
            SendAction("block", false);
            host.RemoveChild(stage);
            stage.Free();
        }
    }

    private static void AddProbeFloor(Node2D stage, float centerX, float floorTopY, float width)
    {
        var floor = new StaticBody2D
        {
            Name = "ProbeFloor",
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = new Vector2(centerX, floorTopY + 20f)
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
