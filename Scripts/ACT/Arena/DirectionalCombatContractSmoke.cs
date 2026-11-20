using System;
using System.Collections.Generic;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Player;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless smoke verifier for Issue #10:
/// - Pure Melee vs Projectile combat resolution (CombatResolver + PlayerController + CombatHud)
/// - Decoupled EnemyController base vs RuinGuardianBoss 3-move cycle
/// - Directional EnemyHitContext contract & live Godot physics directional skill centerline penetration
/// </summary>
public static partial class DirectionalCombatContractSmoke
{
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckProjectileCombatResolverMatrix(failures);
        CheckPlayerProjectileDefenseIntegration(host, failures);
        CheckEnemyHitContextDirectionalMatrix(failures);
        CheckDecoupledBaseAndRuinGuardianContract(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS directional hit context, projectile combat resolver & decoupled RuinGuardianBoss contracts");
            return;
        }

        foreach (string reason in failures)
            fail($"directional-combat: {reason}");
    }

    /// <summary>
    /// Starts an isolated real-physics probe exercising normal slash front/back and
    /// directional skill centerline penetration vs uncrossed frontal deflection.
    /// </summary>
    public static DirectionalSkillPhysicsProbe StartLivePhysicsProbe(Node host, Action<string> fail)
    {
        var probe = new DirectionalSkillPhysicsProbe(fail) { Name = "DirectionalSkillPhysicsProbe" };
        host.AddChild(probe);
        return probe;
    }

    private static void CheckProjectileCombatResolverMatrix(List<string> failures)
    {
        var windows = new CombatWindowConfig();
        var timeSlow = new TimeSlowConfig();
        const float now = 10f;
        const float dmg = 16f;

        var undefended = new DefenseSnapshot(false, 0f, false, 0f, false, now);
        var heldBlock = new DefenseSnapshot(false, 0f, true, now - windows.PerfectGuardSeconds - 0.5f, false, now);
        var perfectBlock = new DefenseSnapshot(false, 0f, true, now - windows.PerfectGuardSeconds * 0.5f, false, now);
        var normalDodge = new DefenseSnapshot(true, now - windows.PerfectDodgeSeconds - 0.2f, false, 0f, false, now);
        var perfectDodge = new DefenseSnapshot(true, now - windows.PerfectDodgeSeconds * 0.5f, false, 0f, false, now);
        var invulnerable = new DefenseSnapshot(false, 0f, false, 0f, true, now);

        // Projectile - undefended -> Hit, full HP damage, no reflect/stagger
        AttackResolution projHit = CombatResolver.Resolve(undefended, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projHit.Result != DefenseResult.Hit ||
            !Mathf.IsEqualApprox(projHit.DamageToHp, dmg) ||
            projHit.DamageToGuard != 0f ||
            projHit.TimeResourceRefund != 0f ||
            projHit.EnemyStaggered ||
            projHit.ProjectileReflected)
        {
            failures.Add($"projectile undefended mismatch: {projHit}");
        }

        // Projectile - normal block -> Blocked, chip damage + guard drain, no reflect/stagger
        AttackResolution projBlock = CombatResolver.Resolve(heldBlock, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projBlock.Result != DefenseResult.Blocked ||
            !Mathf.IsEqualApprox(projBlock.DamageToHp, dmg * windows.BlockChipRatio) ||
            !Mathf.IsEqualApprox(projBlock.DamageToGuard, windows.GuardDamagePerBlockedHit) ||
            projBlock.EnemyStaggered ||
            projBlock.ProjectileReflected)
        {
            failures.Add($"projectile normal block mismatch: {projBlock}");
        }

        // Projectile - perfect dodge -> zero damage, +20 time slow refund, no reflect/stagger
        AttackResolution projPDodge = CombatResolver.Resolve(perfectDodge, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projPDodge.Result != DefenseResult.PerfectDodged ||
            projPDodge.DamageToHp != 0f ||
            projPDodge.DamageToGuard != 0f ||
            !Mathf.IsEqualApprox(projPDodge.TimeResourceRefund, 20f) ||
            projPDodge.EnemyStaggered ||
            projPDodge.ProjectileReflected)
        {
            failures.Add($"projectile perfect dodge mismatch: {projPDodge}");
        }

        // Projectile - normal dodge -> zero damage, no refund
        AttackResolution projDodge = CombatResolver.Resolve(normalDodge, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projDodge.Result != DefenseResult.Dodged || projDodge.DamageToHp != 0f || projDodge.TimeResourceRefund != 0f || projDodge.ProjectileReflected)
            failures.Add($"projectile normal dodge mismatch: {projDodge}");

        // Projectile - invulnerable -> zero damage, no reflect
        AttackResolution projInvuln = CombatResolver.Resolve(invulnerable, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projInvuln.Result != DefenseResult.Invulnerable || projInvuln.DamageToHp != 0f || projInvuln.ProjectileReflected)
            failures.Add($"projectile invulnerable mismatch: {projInvuln}");

        // Projectile - perfect block -> PerfectBlocked + ProjectileReflected=true + EnemyStaggered=false
        AttackResolution projPBlock = CombatResolver.Resolve(perfectBlock, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Projectile);
        if (projPBlock.Result != DefenseResult.PerfectBlocked ||
            projPBlock.DamageToHp != 0f ||
            projPBlock.DamageToGuard != 0f ||
            projPBlock.EnemyStaggered ||
            !projPBlock.ProjectileReflected)
        {
            failures.Add($"projectile perfect block mismatch (expected ProjectileReflected=true, EnemyStaggered=false): {projPBlock}");
        }

        // Melee - perfect block regression check -> EnemyStaggered=true, ProjectileReflected=false
        AttackResolution meleePBlock = CombatResolver.Resolve(perfectBlock, dmg, windows, timeSlow, BossAttackKind.Normal, AttackDeliveryKind.Melee);
        if (meleePBlock.Result != DefenseResult.PerfectBlocked ||
            !meleePBlock.EnemyStaggered ||
            meleePBlock.ProjectileReflected)
        {
            failures.Add($"melee perfect block regression mismatch: {meleePBlock}");
        }

        // Unblockable attack kinds remain unblockable even with Projectile delivery
        AttackResolution breakProj = CombatResolver.Resolve(perfectBlock, dmg, windows, timeSlow, BossAttackKind.BreakGrab, AttackDeliveryKind.Projectile);
        AttackResolution areaProj = CombatResolver.Resolve(perfectBlock, dmg, windows, timeSlow, BossAttackKind.DelayedArea, AttackDeliveryKind.Projectile);
        if (breakProj.Result != DefenseResult.Hit || breakProj.ProjectileReflected ||
            areaProj.Result != DefenseResult.Hit || areaProj.ProjectileReflected)
        {
            failures.Add($"unblockable projectile regression mismatch: break={breakProj} area={areaProj}");
        }
    }

    private static void CheckPlayerProjectileDefenseIntegration(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "ProjectileDefenseProbeStage" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(260f, 580f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(400f, 40f) } });
        probeStage.AddChild(floor);

        var windows = new CombatWindowConfig
        {
            PerfectGuardSeconds = 0.5f,
            PerfectDodgeSeconds = 0.5f,
            HitstunSeconds = 0.25f,
            GuardDamagePerBlockedHit = 25f
        };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var player = new PlayerController
        {
            Name = "ProjectileProbePlayer",
            Position = new Vector2(260f, 537f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var hud = new CombatHud { Name = "ProbeHud", Player = player, TimeSlow = timeSlow };
        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(hud);
        host.AddChild(probeStage);

        float refunded = 0f;
        PlayerDefenseCue? lastCue = null;
        player.TimeResourceRefund += amount => refunded += amount;
        player.DefenseResolved += cue => lastCue = cue;

        try
        {
            player._PhysicsProcess(0.05);

            // 1. Undefended projectile hit -> HP loss + Hitstun
            float hpBefore = player.Hp;
            bool answeredHit = player.TryReceiveEnemyAttack(16f, BossAttackKind.Normal, AttackDeliveryKind.Projectile, out AttackResolution hitRes);
            if (answeredHit ||
                hitRes.Result != DefenseResult.Hit ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore - 16f) ||
                player.State != PlayerState.Hitstun ||
                lastCue is not { DeliveryKind: AttackDeliveryKind.Projectile, ProjectileReflected: false })
            {
                failures.Add($"player projectile undefended hit mismatch: answered={answeredHit} state={player.State} hp={player.Hp}/{hpBefore}");
            }

            // 2. Normal block projectile -> guard drain + chip damage (block is read directly in _PhysicsProcess)
            player.ResetTo(new Vector2(260f, 537f));
            player._PhysicsProcess(0.05);
            windows.PerfectGuardSeconds = -0.1f;
            hpBefore = player.Hp;
            float guardBefore = player.Guard;
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            bool answeredBlock = player.TryReceiveEnemyAttack(16f, BossAttackKind.Normal, AttackDeliveryKind.Projectile, out AttackResolution blockRes);
            float hpAfterBlock = player.Hp;
            float guardAfterBlock = player.Guard;
            SendAction("block", false);
            player._PhysicsProcess(0.016);
            if (!answeredBlock ||
                blockRes.Result != DefenseResult.Blocked ||
                !Mathf.IsEqualApprox(hpAfterBlock, hpBefore - 16f * windows.BlockChipRatio) ||
                !Mathf.IsEqualApprox(guardAfterBlock, guardBefore - windows.GuardDamagePerBlockedHit))
            {
                failures.Add($"player projectile normal block mismatch: res={blockRes} hp={hpAfterBlock}/{hpBefore} guard={guardAfterBlock}/{guardBefore}");
            }

            // 3. Perfect block projectile -> reflected, zero damage, zero guard loss, no stagger, HUD reflect banner
            player.ResetTo(new Vector2(260f, 537f));
            player._PhysicsProcess(0.05);
            windows.PerfectGuardSeconds = 0.5f;
            hpBefore = player.Hp;
            guardBefore = player.Guard;
            SendAction("block", true);
            player._PhysicsProcess(0.016);
            bool answeredReflect = player.TryReceiveEnemyAttack(16f, BossAttackKind.Normal, AttackDeliveryKind.Projectile, out AttackResolution reflectRes);
            SendAction("block", false);
            player._PhysicsProcess(0.016);
            if (!answeredReflect ||
                reflectRes.Result != DefenseResult.PerfectBlocked ||
                !reflectRes.ProjectileReflected ||
                reflectRes.EnemyStaggered ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore) ||
                !Mathf.IsEqualApprox(player.Guard, guardBefore) ||
                lastCue is not { DeliveryKind: AttackDeliveryKind.Projectile, ProjectileReflected: true } ||
                hud.CurrentEventBannerText != "完美格挡 · 弹丸弹反")
            {
                failures.Add($"player projectile perfect block reflect mismatch: res={reflectRes} cue={lastCue} banner='{hud.CurrentEventBannerText}'");
            }

            // 4. Perfect dodge projectile -> zero damage + +20 refund
            player.ResetTo(new Vector2(260f, 537f));
            player._PhysicsProcess(0.05);
            hpBefore = player.Hp;
            refunded = 0f;
            SendAction("dodge", true);
            player._Process(0.016);
            player._PhysicsProcess(0.016);
            bool answeredDodge = player.TryReceiveEnemyAttack(16f, BossAttackKind.Normal, AttackDeliveryKind.Projectile, out AttackResolution dodgeRes);
            SendAction("dodge", false);
            if (!answeredDodge ||
                dodgeRes.Result != DefenseResult.PerfectDodged ||
                !Mathf.IsEqualApprox(player.Hp, hpBefore) ||
                !Mathf.IsEqualApprox(refunded, 20f))
            {
                failures.Add($"player projectile perfect dodge mismatch: res={dodgeRes} hp={player.Hp}/{hpBefore} refund={refunded}");
            }
        }
        finally
        {
            SendAction("block", false);
            SendAction("dodge", false);
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private static void CheckEnemyHitContextDirectionalMatrix(List<string> failures)
    {
        var targetPos = new Vector2(500f, 500f);

        // Target facing left (-1): front is X < 500, back is X > 500
        var frontLeft = new EnemyHitContext(1f, new Vector2(420f, 500f), Vector2.Right);
        var backRight = new EnemyHitContext(1f, new Vector2(580f, 500f), Vector2.Left);
        var skillCrossed = new EnemyHitContext(2f, new Vector2(420f, 500f), Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: true);
        var skillUncrossed = new EnemyHitContext(2f, new Vector2(460f, 500f), Vector2.Right, IsDirectionalSkill: true, CrossedTargetCenterline: false);

        if (!frontLeft.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("frontal strike from left against left-facing enemy should be frontal");
        if (backRight.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("back strike from right against left-facing enemy should bypass front");
        if (skillCrossed.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("directional skill that crossed centerline should bypass front");
        if (!skillUncrossed.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("directional skill that stopped in front without crossing should remain frontal");

        // Target facing right (+1): front is X > 500, back is X < 500
        if (frontLeft.IsFrontalStrike(targetPos, targetFacing: 1))
            failures.Add("strike from left against right-facing enemy should be back strike");
        if (!backRight.IsFrontalStrike(targetPos, targetFacing: 1))
            failures.Add("strike from right against right-facing enemy should be frontal");
        if (skillCrossed.IsFrontalStrike(targetPos, targetFacing: 1))
            failures.Add("crossed directional skill against right-facing enemy should bypass front");

        // Exact horizontal centerline falls back to MotionDirection
        var centerIntoFront = new EnemyHitContext(1f, targetPos, Vector2.Right);
        var centerFromBack = new EnemyHitContext(1f, targetPos, Vector2.Left);
        if (!centerIntoFront.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("exact-center motion into left-facing front should be frontal");
        if (centerFromBack.IsFrontalStrike(targetPos, targetFacing: -1))
            failures.Add("exact-center motion aligned with facing should bypass front");

        // Centerline trajectory helper must detect crossing regardless of dynamic mid-dash facing flip
        if (!EnemyHitContext.HasCrossedHorizontalCenterline(420f, 505f, 500f, targetFacing: 1))
            failures.Add("trajectory crossing left->right must still count as crossed even after enemy flipped facing to +1");
        if (!EnemyHitContext.HasCrossedHorizontalCenterline(580f, 495f, 500f, targetFacing: -1))
            failures.Add("trajectory crossing right->left must still count as crossed even after enemy flipped facing to -1");
        if (EnemyHitContext.HasCrossedHorizontalCenterline(360f, 462f, 500f, targetFacing: -1))
            failures.Add("uncrossed frontal dash must not report crossed centerline");
        if (!EnemyHitContext.HasCrossedHorizontalCenterline(500f, 510f, 500f, targetFacing: -1))
            failures.Add("dash starting on exact centerline into back side should report crossed");
        if (EnemyHitContext.HasCrossedHorizontalCenterline(500f, 490f, 500f, targetFacing: -1))
            failures.Add("dash starting on exact centerline into front side should not report crossed");
    }

    private static void CheckDecoupledBaseAndRuinGuardianContract(Node host, List<string> failures)
    {
        // Live TestArena boss must be RuinGuardianBoss (decoupled from EnemyController base)
        var coreBoss = host.GetNodeOrNull<EnemyController>("Boss");
        if (coreBoss is not RuinGuardianBoss)
            failures.Add($"TestArena Boss expected RuinGuardianBoss, got {coreBoss?.GetType().Name ?? "null"}");

        var probeStage = new Node2D { Name = "DecoupledEnemyProbeStage" };
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
            EnemyAttackRange = 250f
        };
        var player = new PlayerController
        {
            Name = "DecoupledProbePlayer",
            Position = new Vector2(360f, 510f),
            Windows = windows,
            TimeSlow = new TimeSlowConfig()
        };
        var baseEnemy = new EnemyController { Name = "GenericBaseEnemy", Position = new Vector2(500f, 510f), MaxHp = 10f };
        var guardianBoss = new RuinGuardianBoss { Name = "GuardianProbeBoss", Position = new Vector2(500f, 510f), MaxHp = 10f };

        var baseAttacks = new List<BossAttackKind>();
        var bossAttacks = new List<BossAttackKind>();
        baseEnemy.TelegraphStarted += cue => baseAttacks.Add(cue.Attack.Kind);
        guardianBoss.TelegraphStarted += cue => bossAttacks.Add(cue.Attack.Kind);

        probeStage.AddChild(player);
        probeStage.AddChild(baseEnemy);
        probeStage.AddChild(guardianBoss);
        host.AddChild(probeStage);

        try
        {
            baseEnemy.Bind(player, windows);
            guardianBoss.Bind(player, windows);

            for (int step = 0; step < 24 && (baseAttacks.Count < 3 || bossAttacks.Count < 4); step++)
            {
                baseEnemy._PhysicsProcess(0.016);
                guardianBoss._PhysicsProcess(0.016);
                player.ResetTo(new Vector2(360f, 510f));
            }

            if (baseAttacks.Count < 3 ||
                baseAttacks[0] != BossAttackKind.Normal ||
                baseAttacks[1] != BossAttackKind.Normal ||
                baseAttacks[2] != BossAttackKind.Normal)
            {
                failures.Add($"generic EnemyController base should not run 3-move boss cycle; got [{string.Join(", ", baseAttacks)}]");
            }

            if (bossAttacks.Count < 4 ||
                bossAttacks[0] != BossAttackKind.Normal ||
                bossAttacks[1] != BossAttackKind.BreakGrab ||
                bossAttacks[2] != BossAttackKind.DelayedArea ||
                bossAttacks[3] != BossAttackKind.Normal)
            {
                failures.Add($"RuinGuardianBoss 3-move cycle mismatch; got [{string.Join(", ", bossAttacks)}]");
            }

            // ResetTo resets boss cycle back to Normal
            bossAttacks.Clear();
            guardianBoss.ResetTo(new Vector2(500f, 510f));
            for (int step = 0; step < 6 && bossAttacks.Count == 0; step++)
                guardianBoss._PhysicsProcess(0.016);

            if (bossAttacks.Count == 0 || bossAttacks[0] != BossAttackKind.Normal)
                failures.Add($"RuinGuardianBoss ResetTo did not reset cycle to Normal; got [{string.Join(", ", bossAttacks)}]");
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

    private sealed partial class FrontalShieldProbeEnemy : EnemyController
    {
        public override bool HasFrontalDefense => true;
        public bool AllowDynamicFacing { get; set; }

        public FrontalShieldProbeEnemy()
        {
            Name = "FrontalShieldProbeEnemy";
            DisplayName = "重盾探针";
            IsBoss = false;
            ChaseSpeed = 0f;
            MaxHp = 20f;
        }

        public void LockFacingLeft()
        {
            AllowDynamicFacing = false;
            SetFacing(-1);
        }

        protected override void UpdateFacing()
        {
            if (AllowDynamicFacing)
                base.UpdateFacing();
            else
                SetFacing(-1);
        }

        protected override void ThinkIdleOrChase()
        {
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "shield probe holding");
        }
    }

    /// <summary>
    /// Isolated real-physics probe exercising normal slash & directional skill dash trajectories
    /// against a frontal-shielded enemy across actual Godot PhysicsServer2D frames.
    /// </summary>
    public sealed partial class DirectionalSkillPhysicsProbe : Node
    {
        private const float ProbeFloorY = -1200f;
        private const float EnemyX = 500f;

        private enum Step
        {
            Settle,
            FrontSlashStart,
            FrontSlashWait,
            BackSlashStart,
            BackSlashWait,
            FrontSkillCrossStart,
            FrontSkillCrossWait,
            BackSkillEntryStart,
            BackSkillEntryWait,
            FrontSkillShortStart,
            FrontSkillShortWait,
            DynamicFacingSkillCrossStart,
            DynamicFacingSkillCrossWait,
            Done
        }

        private readonly Action<string> _fail;
        private readonly List<string> _failures = new();

        private Node2D _stage = null!;
        private PlayerController _player = null!;
        private FrontalShieldProbeEnemy _enemy = null!;
        private StageEnemyRoster _roster = null!;
        private CombatAudioController _audio = null!;
        private CombatWindowConfig _windows = null!;

        private Step _step = Step.Settle;
        private int _stepFrames;
        private float _hpBefore;
        private int _deflectBefore;
        private int _landedCount;
        private int _landedBefore;
        private int _rosterDeflectCount;
        private EnemyShieldDeflectedCue? _lastDeflectedCue;
        private bool _skillReleasePending;
        private bool _deflectedDuringShortDash;

        public bool IsCompleted { get; private set; }

        public DirectionalSkillPhysicsProbe(Action<string> fail)
        {
            _fail = fail;
        }

        public override void _Ready()
        {
            _windows = new CombatWindowConfig
            {
                BossSpawnSeconds = 0f,
                EnemyInitialDelaySeconds = 99f,
                HitstunSeconds = 0.02f,
                AttackActiveSeconds = 0.08f,
                SkillSeconds = 0.16f,
                SkillSpeed = 850f
            };
            var timeSlowConfig = new TimeSlowConfig();

            _stage = new Node2D { Name = "DirectionalPhysicsProbeStage" };
            var floor = new StaticBody2D
            {
                Name = "ProbeFloor",
                CollisionLayer = 2,
                CollisionMask = 0,
                Position = new Vector2(EnemyX, ProbeFloorY + 20f)
            };
            floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(900f, 40f) } });
            _stage.AddChild(floor);

            var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = timeSlowConfig };
            _stage.AddChild(timeSlow);

            _player = new PlayerController
            {
                Name = "ProbePlayer",
                Position = new Vector2(EnemyX - 80f, ProbeFloorY - 23f),
                Windows = _windows,
                TimeSlow = timeSlowConfig
            };
            _player.AttackLanded += _ => _landedCount++;
            _stage.AddChild(_player);

            _enemy = new FrontalShieldProbeEnemy
            {
                Position = new Vector2(EnemyX, ProbeFloorY - 50f),
                MaxHp = 20f
            };
            _stage.AddChild(_enemy);
            _enemy.Bind(_player, _windows);
            _enemy.LockFacingLeft();

            _roster = new StageEnemyRoster { Name = "ProbeRoster" };
            _roster.EnemyShieldDeflected += cue =>
            {
                _rosterDeflectCount++;
                _lastDeflectedCue = cue.Cue;
            };
            _stage.AddChild(_roster);
            _roster.Register(_enemy);

            _audio = CombatAudioController.AttachRoster(_stage, _player, _roster, timeSlow);
            AddChild(_stage);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (IsCompleted) return;
            _stepFrames++;

            switch (_step)
            {
                case Step.Settle:
                    if (_stepFrames >= 4 && _enemy.State == EnemyState.Idle && _player.IsOnFloor())
                        Transition(Step.FrontSlashStart);
                    break;

                // 1. Normal slash from front (X = 420 -> Enemy at 500 facing left) -> deflected
                case Step.FrontSlashStart:
                    PrepareActor(EnemyX - 80f, moveRight: true);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    SendAction("attack", true);
                    Transition(Step.FrontSlashWait);
                    break;

                case Step.FrontSlashWait:
                    if (_stepFrames == 2) SendAction("attack", false);
                    if (_stepFrames >= 3 && _player.State != PlayerState.Attack)
                    {
                        SendAction("move_right", false);
                        if (!Mathf.IsEqualApprox(_enemy.Hp, _hpBefore) ||
                            _rosterDeflectCount != _deflectBefore + 1 ||
                            _landedCount != _landedBefore ||
                            _audio.LastCue != CombatSfxId.ShieldDeflect)
                        {
                            _failures.Add($"frontal slash deflection mismatch: hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore} sfx={_audio.LastCue}");
                        }
                        Transition(Step.BackSlashStart);
                    }
                    break;

                // 2. Normal slash from behind (X = 580 -> Enemy at 500 facing left) -> full damage (-1)
                case Step.BackSlashStart:
                    PrepareActor(EnemyX + 80f, moveRight: false);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    SendAction("attack", true);
                    Transition(Step.BackSlashWait);
                    break;

                case Step.BackSlashWait:
                    if (_stepFrames == 2) SendAction("attack", false);
                    if (_stepFrames >= 3 && _player.State != PlayerState.Attack)
                    {
                        SendAction("move_left", false);
                        if (!Mathf.IsEqualApprox(_enemy.Hp, _hpBefore - _player.AttackDamage) ||
                            _rosterDeflectCount != _deflectBefore ||
                            _landedCount != _landedBefore + 1)
                        {
                            _failures.Add($"back slash damage mismatch: hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore}");
                        }
                        Transition(Step.FrontSkillCrossStart);
                    }
                    break;

                // 3. Directional skill from front crossing horizontal centerline (X = 420 -> > 500) -> back-pierce (-2), NOT deflected on early front graze
                case Step.FrontSkillCrossStart:
                    _windows.SkillSeconds = 0.18f;
                    PrepareActor(EnemyX - 80f, moveRight: true);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    SendAction("skill", true);
                    _skillReleasePending = true;
                    Transition(Step.FrontSkillCrossWait);
                    break;

                case Step.FrontSkillCrossWait:
                    if (_skillReleasePending && _stepFrames >= 2)
                    {
                        _skillReleasePending = false;
                        SendAction("skill", false);
                    }
                    if (!_skillReleasePending && _stepFrames >= 5 && _player.State != PlayerState.Skill)
                    {
                        SendAction("move_right", false);
                        bool crossed = _player.GlobalPosition.X > EnemyX;
                        if (!crossed ||
                            !Mathf.IsEqualApprox(_enemy.Hp, _hpBefore - _player.SkillDamage) ||
                            _rosterDeflectCount != _deflectBefore ||
                            _landedCount != _landedBefore + 1)
                        {
                            _failures.Add($"centerline-crossing skill mismatch: endX={_player.GlobalPosition.X:0.0} hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore}");
                        }
                        Transition(Step.BackSkillEntryStart);
                    }
                    break;

                // 4. Directional skill starting behind target (X = 580 aiming left) -> back-entry damage (-2)
                case Step.BackSkillEntryStart:
                    _windows.SkillSeconds = 0.12f;
                    PrepareActor(EnemyX + 80f, moveRight: false);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    SendAction("skill", true);
                    _skillReleasePending = true;
                    Transition(Step.BackSkillEntryWait);
                    break;

                case Step.BackSkillEntryWait:
                    if (_skillReleasePending && _stepFrames >= 2)
                    {
                        _skillReleasePending = false;
                        SendAction("skill", false);
                    }
                    if (!_skillReleasePending && _stepFrames >= 5 && _player.State != PlayerState.Skill)
                    {
                        SendAction("move_left", false);
                        if (!Mathf.IsEqualApprox(_enemy.Hp, _hpBefore - _player.SkillDamage) ||
                            _rosterDeflectCount != _deflectBefore ||
                            _landedCount != _landedBefore + 1)
                        {
                            _failures.Add($"back-entry skill mismatch: endX={_player.GlobalPosition.X:0.0} hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore}");
                        }
                        Transition(Step.FrontSkillShortStart);
                    }
                    break;

                // 5. Directional skill grazing front hurtbox but ending strictly in front of centerline (X = 360 -> ~462 < 500) -> deflected only at skill end
                case Step.FrontSkillShortStart:
                    _windows.SkillSeconds = 0.12f;
                    PrepareActor(EnemyX - 140f, moveRight: true);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    _deflectedDuringShortDash = false;
                    SendAction("skill", true);
                    _skillReleasePending = true;
                    Transition(Step.FrontSkillShortWait);
                    break;

                case Step.FrontSkillShortWait:
                    if (_skillReleasePending && _stepFrames >= 2)
                    {
                        _skillReleasePending = false;
                        SendAction("skill", false);
                    }
                    if (_player.State == PlayerState.Skill && _rosterDeflectCount > _deflectBefore)
                        _deflectedDuringShortDash = true;
                    if (!_skillReleasePending && _stepFrames >= 5 && _player.State != PlayerState.Skill)
                    {
                        SendAction("move_right", false);
                        bool stayedInFront = _player.GlobalPosition.X < EnemyX;
                        if (!stayedInFront ||
                            _deflectedDuringShortDash ||
                            !Mathf.IsEqualApprox(_enemy.Hp, _hpBefore) ||
                            _rosterDeflectCount != _deflectBefore + 1 ||
                            _landedCount != _landedBefore ||
                            _lastDeflectedCue is not { Hit: { IsDirectionalSkill: true, CrossedTargetCenterline: false } } ||
                            _audio.LastCue != CombatSfxId.ShieldDeflect)
                        {
                            _failures.Add($"uncrossed frontal skill deflection mismatch: endX={_player.GlobalPosition.X:0.0} earlyDeflect={_deflectedDuringShortDash} hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore} sfx={_audio.LastCue}");
                        }
                        Transition(Step.DynamicFacingSkillCrossStart);
                    }
                    break;

                // 6. Dynamic-facing shielded enemy: crossing horizontal centerline still pierces even if enemy turns toward player mid-dash
                case Step.DynamicFacingSkillCrossStart:
                    _enemy.AllowDynamicFacing = true;
                    _windows.SkillSeconds = 0.18f;
                    PrepareActor(EnemyX - 80f, moveRight: true);
                    _hpBefore = _enemy.Hp;
                    _deflectBefore = _rosterDeflectCount;
                    _landedBefore = _landedCount;
                    SendAction("skill", true);
                    _skillReleasePending = true;
                    Transition(Step.DynamicFacingSkillCrossWait);
                    break;

                case Step.DynamicFacingSkillCrossWait:
                    if (_skillReleasePending && _stepFrames >= 2)
                    {
                        _skillReleasePending = false;
                        SendAction("skill", false);
                    }
                    if (!_skillReleasePending && _stepFrames >= 5 && _player.State != PlayerState.Skill)
                    {
                        SendAction("move_right", false);
                        bool crossed = _player.GlobalPosition.X > EnemyX;
                        if (!crossed ||
                            !Mathf.IsEqualApprox(_enemy.Hp, _hpBefore - _player.SkillDamage) ||
                            _rosterDeflectCount != _deflectBefore ||
                            _landedCount != _landedBefore + 1)
                        {
                            _failures.Add($"dynamic-facing centerline-crossing skill mismatch: endX={_player.GlobalPosition.X:0.0} enemyFacing={_enemy.Facing} hp={_enemy.Hp}/{_hpBefore} deflects={_rosterDeflectCount - _deflectBefore} landed={_landedCount - _landedBefore}");
                        }
                        CompleteProbe();
                    }
                    break;
            }

            if (_stepFrames > 180)
            {
                _failures.Add($"directional skill physics probe timed out at step {_step}");
                CompleteProbe();
            }
        }

        private void PrepareActor(float playerX, bool moveRight)
        {
            ReleaseAllInputs();
            _player.ResetTo(new Vector2(playerX, ProbeFloorY - 23f));
            _player._PhysicsProcess(0.02);
            SendAction(moveRight ? "move_right" : "move_left", true);
            _player._Process(0.016);
            _player._PhysicsProcess(0.016);
            _player.Position = new Vector2(playerX, ProbeFloorY - 23f);
            _player.Velocity = Vector2.Zero;
        }

        private void Transition(Step next)
        {
            _step = next;
            _stepFrames = 0;
        }

        private void CompleteProbe()
        {
            _step = Step.Done;
            IsCompleted = true;
            ReleaseAllInputs();

            if (_failures.Count == 0)
            {
                GD.Print("[Smoke] PASS live physics directional slash & centerline-penetrating skill dash (front deflect, back hit, centerline pierce, back entry, uncrossed deflect)");
            }
            else
            {
                foreach (string reason in _failures)
                    _fail($"directional-physics: {reason}");
            }

            if (GodotObject.IsInstanceValid(_stage) && _stage.GetParent() == this)
            {
                RemoveChild(_stage);
                _stage.Free();
            }
        }

        public override void _ExitTree()
        {
            ReleaseAllInputs();
        }

        private static void ReleaseAllInputs()
        {
            SendAction("attack", false);
            SendAction("skill", false);
            SendAction("move_left", false);
            SendAction("move_right", false);
        }
    }
}
