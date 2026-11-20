using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Headless smoke verifier for Issue #11:
/// centralized 2 Melee + 2 Ranged attack token pools, >= 0.35s real-timestamp hit staggering,
/// immediate release on strike finish / Hitstun / ShieldBroken (破盾失衡) / Dead / TreeExiting / Reset,
/// Boss / TrainingTarget exemption, and a live real-timestamp 4v4 multi-enemy concurrency probe.
/// </summary>
public static partial class EncounterAttackCoordinatorSmoke
{
    public static void Check(Node host, Action<string> fail)
    {
        var failures = new List<string>();
        CheckCoordinatorPoolAndStaggerContract(failures);
        CheckExemptionsAndRosterReleaseContracts(host, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS encounter 2+2 attack token pools, 0.35s hit stagger, exemptions & immediate release contracts");
            return;
        }

        foreach (string reason in failures)
            fail($"attack-tokens: {reason}");
    }

    /// <summary>
    /// Starts an isolated real-timestamp physics probe with 4 melee + 4 ranged regular enemies,
    /// 1 RuinGuardianBoss, and 1 TrainingTarget sharing a StageEnemyRoster.
    /// </summary>
    public static EncounterConcurrencyProbe StartLiveConcurrencyProbe(Node host, Action<string> fail)
    {
        var probe = new EncounterConcurrencyProbe(fail) { Name = "EncounterConcurrencyProbe" };
        host.AddChild(probe);
        return probe;
    }

    private static void CheckCoordinatorPoolAndStaggerContract(List<string> failures)
    {
        var coordinator = new EncounterAttackCoordinator();
        if (coordinator.MeleeCapacity != 2 ||
            coordinator.RangedCapacity != 2 ||
            !Mathf.IsEqualApprox(coordinator.MinHitStaggerSeconds, 0.35f))
        {
            failures.Add($"default coordinator config mismatch: melee={coordinator.MeleeCapacity} ranged={coordinator.RangedCapacity} stagger={coordinator.MinHitStaggerSeconds:0.000}");
        }

        var m1 = new EnemyController { Name = "TokenMelee1" };
        var m2 = new EnemyController { Name = "TokenMelee2" };
        var m3 = new EnemyController { Name = "TokenMelee3" };
        var r1 = new RangedProbeEnemy { Name = "TokenRanged1" };
        var r2 = new RangedProbeEnemy { Name = "TokenRanged2" };
        var r3 = new RangedProbeEnemy { Name = "TokenRanged3" };

        try
        {
            // Melee slot #1 approved
            if (!coordinator.TryAcquire(m1, AttackTokenPool.Melee, 10.00f) ||
                coordinator.ActiveMeleeCount != 1 ||
                !coordinator.HoldsToken(m1))
            {
                failures.Add("melee slot #1 failed to acquire");
            }

            // Melee slot #2 rejected when < 0.35s from active hit
            if (coordinator.TryAcquire(m2, AttackTokenPool.Melee, 10.349f) ||
                coordinator.ActiveMeleeCount != 1 ||
                coordinator.HoldsToken(m2))
            {
                failures.Add("melee slot #2 should be rejected when expected hit diff is 0.349s (< 0.35s)");
            }

            // Melee slot #2 approved at exact 0.35s real-timestamp separation
            if (!coordinator.TryAcquire(m2, AttackTokenPool.Melee, 10.35f) ||
                coordinator.ActiveMeleeCount != 2 ||
                !coordinator.HoldsToken(m2))
            {
                failures.Add("melee slot #2 failed to acquire at 0.35s stagger");
            }

            // Melee slot #3 rejected at capacity even with huge stagger
            if (coordinator.TryAcquire(m3, AttackTokenPool.Melee, 15.00f) ||
                coordinator.ActiveMeleeCount != 2 ||
                coordinator.HoldsToken(m3))
            {
                failures.Add("melee slot #3 should be rejected when melee pool is at capacity 2");
            }

            // Ranged pool is independent of Melee pool (same hit timestamp 10.00f allowed)
            if (!coordinator.TryAcquire(r1, AttackTokenPool.Ranged, 10.00f) ||
                coordinator.ActiveRangedCount != 1)
            {
                failures.Add("ranged slot #1 should not be blocked by saturated melee pool");
            }

            // Ranged slot #2 respects 0.35s stagger within ranged pool
            if (coordinator.TryAcquire(r2, AttackTokenPool.Ranged, 10.20f) ||
                !coordinator.TryAcquire(r2, AttackTokenPool.Ranged, 10.35f) ||
                coordinator.ActiveRangedCount != 2)
            {
                failures.Add("ranged slot #2 0.35s stagger contract failed");
            }

            // Ranged slot #3 rejected at capacity 2
            if (coordinator.TryAcquire(r3, AttackTokenPool.Ranged, 20.00f) ||
                coordinator.ActiveRangedCount != 2)
            {
                failures.Add("ranged slot #3 should be rejected when ranged pool is at capacity 2");
            }

            // Symmetry check: releasing m1 and testing earlier hit timestamp against m2 (10.35f)
            if (!coordinator.Release(m1) || coordinator.ActiveMeleeCount != 1 || coordinator.HoldsToken(m1))
                failures.Add("releasing m1 did not free melee slot immediately");
            if (coordinator.TryAcquire(m3, AttackTokenPool.Melee, 10.01f))
                failures.Add("melee applicant earlier by 0.34s should be rejected");
            if (!coordinator.TryAcquire(m3, AttackTokenPool.Melee, 10.00f))
                failures.Add("melee applicant earlier by 0.35s should be approved");

            // IEEE-754 float boundary check: 2.00f and 2.35f (difference in float32 is 0.3499999f)
            coordinator.Clear();
            if (coordinator.TotalActiveCount != 0)
                failures.Add("Clear did not empty all attack token pools");

            if (!coordinator.TryAcquire(m1, AttackTokenPool.Melee, 2.00f) ||
                !coordinator.TryAcquire(m2, AttackTokenPool.Melee, 2.35f))
            {
                failures.Add("float boundary 2.00f -> 2.35f (0.35s stagger) failed");
            }

            // Duplicate holder acquisition & non-finite timestamps (NaN / Infinity) must be rejected
            coordinator.Clear();
            if (coordinator.TryAcquire(m1, AttackTokenPool.Melee, float.NaN) ||
                coordinator.TryAcquire(m1, AttackTokenPool.Melee, float.PositiveInfinity) ||
                coordinator.TryAcquire(m1, AttackTokenPool.Melee, float.NegativeInfinity) ||
                coordinator.TotalActiveCount != 0)
            {
                failures.Add("coordinator accepted non-finite expectedHitRealSec");
            }

            if (!coordinator.TryAcquire(m1, AttackTokenPool.Melee, 5.00f) ||
                coordinator.TryAcquire(m1, AttackTokenPool.Melee, 6.00f) ||
                coordinator.TryAcquire(m1, AttackTokenPool.Ranged, 6.00f) ||
                coordinator.TotalActiveCount != 1)
            {
                failures.Add("coordinator allowed duplicate token acquisition for an existing holder");
            }

            coordinator.Clear();
        }
        finally
        {
            m1.Free();
            m2.Free();
            m3.Free();
            r1.Free();
            r2.Free();
            r3.Free();
        }
    }

    private static void CheckExemptionsAndRosterReleaseContracts(Node host, List<string> failures)
    {
        var probeStage = new Node2D { Name = "AttackTokenReleaseProbeStage" };
        var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var windows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0.60f,
            NormalAttackActiveSeconds = 0f,
            NormalAttackRecoverySeconds = 0.20f,
            HitstunSeconds = 0.30f,
            EnemyAttackRange = 300f
        };
        var fastWindows = new CombatWindowConfig
        {
            BossSpawnSeconds = 0f,
            EnemyInitialDelaySeconds = 0f,
            EnemyAttackCooldown = 0f,
            NormalAttackTelegraphSeconds = 0f,
            NormalAttackActiveSeconds = 0f,
            NormalAttackRecoverySeconds = 0.20f,
            HitstunSeconds = 0.30f,
            EnemyAttackRange = 300f
        };
        var player = new PlayerController
        {
            Name = "TokenProbePlayer",
            Position = new Vector2(220f, 500f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var roster = new StageEnemyRoster { Name = "EnemyRoster" };
        roster.BindTimeSlow(timeSlow);

        probeStage.AddChild(timeSlow);
        probeStage.AddChild(player);
        probeStage.AddChild(roster);
        host.AddChild(probeStage);

        try
        {
            EncounterAttackCoordinator coordinator = roster.AttackCoordinator;

            var m1 = new EnemyController { Name = "MeleeHolder1", Position = new Vector2(300f, 500f), MaxHp = 5f, Windows = windows };
            var m2 = new EnemyController { Name = "MeleeWaiter", Position = new Vector2(340f, 500f), MaxHp = 5f, Windows = windows };
            var r1 = new RangedProbeEnemy { Name = "RangedHolder1", Position = new Vector2(380f, 500f), MaxHp = 5f, Windows = windows };
            var r2 = new RangedProbeEnemy { Name = "RangedWaiter", Position = new Vector2(420f, 500f), MaxHp = 5f, Windows = windows };
            var boss = new RuinGuardianBoss { Name = "ExemptBoss", Position = new Vector2(320f, 500f), MaxHp = 10f, Windows = windows };
            var target = new TrainingTarget { Name = "ExemptTarget", Position = new Vector2(330f, 500f), MaxHp = 99f, Windows = windows };

            probeStage.AddChild(m1);
            probeStage.AddChild(m2);
            probeStage.AddChild(r1);
            probeStage.AddChild(r2);
            probeStage.AddChild(boss);
            probeStage.AddChild(target);

            m1.Bind(player, windows);
            m2.Bind(player, windows);
            r1.Bind(player, windows);
            r2.Bind(player, windows);
            boss.Bind(player, windows);
            target.Bind(player, windows);

            roster.RegisterRange(new EnemyController[] { m1, m2, r1, r2, boss, target });

            // Advance out of Spawn
            StepAll(m1, m2, r1, r2, boss, target);

            // 1. Same-tick acquisition: m1 gets melee token, m2 is denied (< 0.35s stagger) and stays Idle/Chase
            m1._PhysicsProcess(0.016);
            m2._PhysicsProcess(0.016);
            if (m1.State != EnemyState.Telegraph || !m1.HoldsAttackToken || coordinator.ActiveMeleeCount != 1)
                failures.Add($"m1 did not acquire melee token on telegraph (state={m1.State}, holds={m1.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");
            if (m2.State is EnemyState.Telegraph or EnemyState.Strike || m2.HoldsAttackToken)
                failures.Add($"unapproved m2 entered attack flow early (state={m2.State}, holds={m2.HoldsAttackToken})");

            // 2. Boss & TrainingTarget bypass regular mob token pools even when both pools are saturated
            var satM = new EnemyController { Name = "SatMelee", Position = new Vector2(360f, 500f), MaxHp = 5f, Windows = windows };
            var satR1 = new RangedProbeEnemy { Name = "SatRanged1", Position = new Vector2(440f, 500f), MaxHp = 5f, Windows = windows };
            var satR2 = new RangedProbeEnemy { Name = "SatRanged2", Position = new Vector2(460f, 500f), MaxHp = 5f, Windows = windows };
            probeStage.AddChild(satM);
            probeStage.AddChild(satR1);
            probeStage.AddChild(satR2);
            satM.Bind(player, windows);
            satR1.Bind(player, windows);
            satR2.Bind(player, windows);
            roster.RegisterRange(new EnemyController[] { satM, satR1, satR2 });

            float now = Time.GetTicksMsec() / 1000f;
            coordinator.TryAcquire(satM, AttackTokenPool.Melee, now + 1.50f);
            coordinator.TryAcquire(satR1, AttackTokenPool.Ranged, now + 0.60f);
            coordinator.TryAcquire(satR2, AttackTokenPool.Ranged, now + 1.10f);

            if (coordinator.ActiveMeleeCount != 2 || coordinator.ActiveRangedCount != 2)
                failures.Add($"failed to saturate both pools before exemption check (melee={coordinator.ActiveMeleeCount}, ranged={coordinator.ActiveRangedCount})");

            if (boss.UsesEncounterAttackToken || target.UsesEncounterAttackToken)
                failures.Add($"boss/TrainingTarget should be exempt from regular mob tokens (boss={boss.UsesEncounterAttackToken}, target={target.UsesEncounterAttackToken})");

            boss._PhysicsProcess(0.016);
            target._PhysicsProcess(0.016);
            if (boss.State != EnemyState.Telegraph || target.State != EnemyState.Telegraph)
                failures.Add($"boss or TrainingTarget was blocked by saturated token pools (boss={boss.State}, target={target.State})");
            if (coordinator.ActiveMeleeCount != 2 || coordinator.ActiveRangedCount != 2)
                failures.Add("exempt boss/TrainingTarget unexpectedly consumed regular token slots");

            // Clean up saturation helpers
            probeStage.RemoveChild(satM);
            satM.Free();
            probeStage.RemoveChild(satR1);
            satR1.Free();
            probeStage.RemoveChild(satR2);
            satR2.Free();
            if (coordinator.ActiveMeleeCount != 1 || coordinator.ActiveRangedCount != 0)
                failures.Add($"TreeExiting did not release tokens from destroyed helpers (melee={coordinator.ActiveMeleeCount}, ranged={coordinator.ActiveRangedCount})");

            // 3. Immediate release on Hitstun -> waiting m2 acquires immediately
            m1.TakeDamage(1f);
            if (m1.State != EnemyState.Hitstun || m1.HoldsAttackToken || coordinator.ActiveMeleeCount != 0)
                failures.Add($"Hitstun did not immediately release m1 token (state={m1.State}, holds={m1.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");

            m2._PhysicsProcess(0.016);
            if (m2.State != EnemyState.Telegraph || !m2.HoldsAttackToken || coordinator.ActiveMeleeCount != 1)
                failures.Add($"waiting m2 could not acquire melee token immediately after m1 hitstun (state={m2.State}, active={coordinator.ActiveMeleeCount})");

            // 4. Immediate release on ShieldBroken (破盾失衡) -> waiting m1/ranged check
            m2.EnterShieldBreak(1.2f, "perfect block shield break");
            if (m2.State != EnemyState.ShieldBroken || !m2.IsShieldBroken || m2.HoldsAttackToken || coordinator.ActiveMeleeCount != 0)
                failures.Add($"ShieldBroken (破盾失衡) did not immediately release m2 token (state={m2.State}, broken={m2.IsShieldBroken}, active={coordinator.ActiveMeleeCount})");

            // 5. Immediate release on Strike settlement end (Telegraph -> Strike -> Recover)
            var finisher = new EnemyController { Name = "StrikeFinisher", Position = new Vector2(280f, 500f), MaxHp = 5f, Windows = fastWindows };
            probeStage.AddChild(finisher);
            finisher.Bind(player, fastWindows);
            roster.Register(finisher);
            finisher._PhysicsProcess(0.016); // Spawn -> Idle
            finisher._PhysicsProcess(0.016); // Idle -> Telegraph (acquires melee token)
            if (finisher.State != EnemyState.Telegraph || coordinator.ActiveMeleeCount != 1)
                failures.Add($"finisher failed to acquire melee token (state={finisher.State}, active={coordinator.ActiveMeleeCount})");
            finisher._PhysicsProcess(0.016); // Telegraph -> Strike (ActiveSeconds=0)
            finisher._PhysicsProcess(0.016); // Strike resolves -> Recover (releases token)
            if (finisher.State != EnemyState.Recover || finisher.HoldsAttackToken || coordinator.ActiveMeleeCount != 0)
                failures.Add($"strike settlement end did not release token (state={finisher.State}, holds={finisher.HoldsAttackToken}, active={coordinator.ActiveMeleeCount})");

            // 6. Immediate release on Dead -> waiting ranged enemy acquires immediately
            r1._PhysicsProcess(0.016); // Idle -> Telegraph
            r2._PhysicsProcess(0.016); // rejected (< 0.35s stagger)
            if (r1.State != EnemyState.Telegraph || r2.State == EnemyState.Telegraph || coordinator.ActiveRangedCount != 1)
                failures.Add($"ranged stagger setup failed (r1={r1.State}, r2={r2.State}, rangedActive={coordinator.ActiveRangedCount})");

            r1.TakeDamage(r1.MaxHp);
            if (r1.State != EnemyState.Dead || r1.HoldsAttackToken || coordinator.ActiveRangedCount != 0)
                failures.Add($"death did not immediately release r1 token (state={r1.State}, holds={r1.HoldsAttackToken}, rangedActive={coordinator.ActiveRangedCount})");

            r2._PhysicsProcess(0.016);
            if (r2.State != EnemyState.Telegraph || !r2.HoldsAttackToken || coordinator.ActiveRangedCount != 1)
                failures.Add($"waiting r2 did not acquire ranged token immediately after r1 death (state={r2.State}, rangedActive={coordinator.ActiveRangedCount})");

            // 7. ResetTo releases token cleanly
            r2.ResetTo(r2.Position);
            if (r2.HoldsAttackToken || coordinator.ActiveRangedCount != 0)
                failures.Add($"ResetTo did not release r2 token (holds={r2.HoldsAttackToken}, rangedActive={coordinator.ActiveRangedCount})");

            // 8. Denied token must not advance stateful SelectNextAttack() cycle
            var cyclingEnemy = new CyclingMeleeProbeEnemy { Name = "CyclingProbe", Position = new Vector2(310f, 500f), MaxHp = 5f, Windows = windows };
            probeStage.AddChild(cyclingEnemy);
            cyclingEnemy.Bind(player, windows);
            roster.Register(cyclingEnemy);
            cyclingEnemy._PhysicsProcess(0.016); // Spawn -> Idle

            float blockNow = Time.GetTicksMsec() / 1000f;
            coordinator.TryAcquire(m1, AttackTokenPool.Melee, blockNow + 0.60f);
            coordinator.TryAcquire(m2, AttackTokenPool.Melee, blockNow + 1.00f);
            cyclingEnemy._PhysicsProcess(0.016); // Denied (pool full)
            cyclingEnemy._PhysicsProcess(0.016); // Denied again
            if (cyclingEnemy.State == EnemyState.Telegraph || cyclingEnemy.SelectionCount != 1)
                failures.Add($"denied token spun SelectNextAttack cycle (state={cyclingEnemy.State}, selections={cyclingEnemy.SelectionCount})");

            coordinator.Release(m1);
            coordinator.Release(m2);
            cyclingEnemy._PhysicsProcess(0.016); // Approved -> must still launch first attack (Normal)
            if (cyclingEnemy.State != EnemyState.Telegraph || cyclingEnemy.CurrentAttackKind != BossAttackKind.Normal)
                failures.Add($"cycling enemy lost first attack after rejection (state={cyclingEnemy.State}, kind={cyclingEnemy.CurrentAttackKind})");

            // 9. Roster.Clear empties all active tokens cleanly
            roster.Clear();
            if (coordinator.TotalActiveCount != 0)
                failures.Add($"StageEnemyRoster.Clear left active tokens ({coordinator.TotalActiveCount})");
        }
        finally
        {
            host.RemoveChild(probeStage);
            if (roster.AttackCoordinator.TotalActiveCount != 0)
                failures.Add($"StageEnemyRoster._ExitTree left active tokens ({roster.AttackCoordinator.TotalActiveCount})");
            probeStage.Free();
        }
    }

    private static void StepAll(params EnemyController[] enemies)
    {
        for (int i = 0; i < enemies.Length; i++)
            enemies[i]._PhysicsProcess(0.016);
    }

    private sealed partial class CyclingMeleeProbeEnemy : EnemyController
    {
        public int SelectionCount { get; private set; }

        public CyclingMeleeProbeEnemy()
        {
            DisplayName = "轮换近战探针";
            IsBoss = false;
            ChaseSpeed = 0f;
        }

        protected override BossAttackDefinition SelectNextAttack()
        {
            bool useNormal = SelectionCount % 2 == 0;
            SelectionCount++;
            return useNormal
                ? BossAttackDefinition.CreateNormal(Windows)
                : BossAttackDefinition.CreateBreakGrab(Windows);
        }

        protected override void OnReset()
        {
            SelectionCount = 0;
        }
    }

    private sealed partial class RangedProbeEnemy : EnemyController
    {
        public override AttackTokenPool AttackTokenPoolKind => AttackTokenPool.Ranged;

        public RangedProbeEnemy()
        {
            DisplayName = "远程射手探针";
            IsBoss = false;
            ChaseSpeed = 0f;
        }
    }

    /// <summary>
    /// Real-timestamp multi-enemy concurrency probe verifying 2 Melee + 2 Ranged caps,
    /// >= 0.35s real-timestamp hit staggering, unapproved wait behavior, boss/target exemption,
    /// and immediate token release on Hitstun / ShieldBroken / Death over live physics frames.
    /// </summary>
    public sealed partial class EncounterConcurrencyProbe : Node
    {
        private const float ProbeFloorY = -1600f;
        private const float StaggerCheckToleranceSec = 0.0001f;

        private readonly Action<string> _fail;
        private readonly List<string> _failures = new();
        private readonly List<EnemyController> _meleeEnemies = new();
        private readonly List<RangedProbeEnemy> _rangedEnemies = new();
        private readonly Dictionary<EnemyController, float> _activeExpectedHitByEnemy = new();

        private Node2D _stage = null!;
        private PlayerController _player = null!;
        private StageEnemyRoster _roster = null!;
        private RuinGuardianBoss _boss = null!;
        private TrainingTarget _target = null!;

        private float _startedAt;
        private int _maxConcurrentMelee;
        private int _maxConcurrentRanged;
        private int _staggeredMeleePairsObserved;
        private int _staggeredRangedPairsObserved;
        private bool _sawUnapprovedMeleeWaiting;
        private bool _sawUnapprovedRangedWaiting;
        private bool _sawBossTelegraph;
        private bool _sawTargetTelegraph;
        private bool _triggeredReleaseChecks;
        private bool _verifiedHitstunReplacement;
        private bool _verifiedDeathReplacement;
        private EnemyController? _hitstunVictim;
        private EnemyController? _shieldBreakVictim;
        private EnemyController? _deathVictim;

        public bool IsCompleted { get; private set; }

        private static float Now => Time.GetTicksMsec() / 1000f;

        public EncounterConcurrencyProbe(Action<string> fail)
        {
            _fail = fail;
        }

        public override void _Ready()
        {
            var windows = new CombatWindowConfig
            {
                BossSpawnSeconds = 0f,
                EnemyInitialDelaySeconds = 0f,
                EnemyAttackCooldown = 0.15f,
                NormalAttackTelegraphSeconds = 0.65f,
                NormalAttackActiveSeconds = 0.10f,
                NormalAttackRecoverySeconds = 0.15f,
                BreakGrabTelegraphSeconds = 0.65f,
                BreakGrabActiveSeconds = 0.10f,
                BreakGrabRecoverySeconds = 0.15f,
                HitstunSeconds = 0.60f,
                EnemyAttackRange = 360f
            };
            var timeSlowConfig = new TimeSlowConfig();

            _stage = new Node2D { Name = "EncounterConcurrencyStage" };
            var floor = new StaticBody2D
            {
                Name = "ProbeFloor",
                CollisionLayer = 2,
                CollisionMask = 0,
                Position = new Vector2(500f, ProbeFloorY + 20f)
            };
            floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(1200f, 40f) } });
            _stage.AddChild(floor);

            var timeSlow = new TimeSlowManager { Name = "ProbeTimeSlow", Config = timeSlowConfig };
            _stage.AddChild(timeSlow);

            _player = new PlayerController
            {
                Name = "ProbePlayer",
                Position = new Vector2(500f, ProbeFloorY - 23f),
                MaxHp = 999f,
                Windows = windows,
                TimeSlow = timeSlowConfig
            };
            _stage.AddChild(_player);

            _roster = new StageEnemyRoster { Name = "EnemyRoster" };
            _roster.BindTimeSlow(timeSlow);
            _roster.EnemyTelegraphStarted += OnRosterTelegraphStarted;
            _stage.AddChild(_roster);

            for (int i = 0; i < 4; i++)
            {
                var melee = new EnemyController
                {
                    Name = $"ProbeMelee{i + 1}",
                    Position = new Vector2(380f + i * 70f, ProbeFloorY - 50f),
                    MaxHp = 6f,
                    ChaseSpeed = 60f,
                    Windows = windows
                };
                _stage.AddChild(melee);
                melee.Bind(_player, windows);
                _roster.Register(melee);
                _meleeEnemies.Add(melee);
            }

            for (int i = 0; i < 4; i++)
            {
                var ranged = new RangedProbeEnemy
                {
                    Name = $"ProbeRanged{i + 1}",
                    Position = new Vector2(340f + i * 95f, ProbeFloorY - 50f),
                    MaxHp = 4f,
                    Windows = windows
                };
                _stage.AddChild(ranged);
                ranged.Bind(_player, windows);
                _roster.Register(ranged);
                _rangedEnemies.Add(ranged);
            }

            _boss = new RuinGuardianBoss
            {
                Name = "ProbeBoss",
                Position = new Vector2(620f, ProbeFloorY - 50f),
                MaxHp = 20f,
                Windows = windows
            };
            _stage.AddChild(_boss);
            _boss.Bind(_player, windows);
            _roster.Register(_boss);

            _target = new TrainingTarget
            {
                Name = "ProbeTarget",
                Position = new Vector2(360f, ProbeFloorY - 50f),
                MaxHp = 99f,
                Windows = windows
            };
            _stage.AddChild(_target);
            _target.Bind(_player, windows);
            _roster.Register(_target);

            AddChild(_stage);
            _startedAt = Now;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (IsCompleted) return;

            PruneInactiveExpectedHits();

            List<EnemyController> meleeHolders = GetAttacking(_meleeEnemies);
            List<EnemyController> rangedHolders = GetAttacking(_rangedEnemies);
            int activeMelee = meleeHolders.Count;
            int activeRanged = rangedHolders.Count;
            _maxConcurrentMelee = Mathf.Max(_maxConcurrentMelee, activeMelee);
            _maxConcurrentRanged = Mathf.Max(_maxConcurrentRanged, activeRanged);

            if (activeMelee > 2)
                _failures.Add($"concurrent melee attackers exceeded cap 2 (observed {activeMelee})");
            if (activeRanged > 2)
                _failures.Add($"concurrent ranged attackers exceeded cap 2 (observed {activeRanged})");

            if (activeMelee >= 1 && HasWaitingEnemy(_meleeEnemies))
                _sawUnapprovedMeleeWaiting = true;
            if (activeRanged >= 1 && HasWaitingEnemy(_rangedEnemies))
                _sawUnapprovedRangedWaiting = true;

            // Once both pools reach full 2+2 concurrency, trigger Hitstun + ShieldBroken + Death immediate-release checks
            if (!_triggeredReleaseChecks &&
                activeMelee == 2 &&
                activeRanged == 2 &&
                _staggeredMeleePairsObserved > 0 &&
                _staggeredRangedPairsObserved > 0)
            {
                _triggeredReleaseChecks = true;

                _hitstunVictim = meleeHolders[0];
                _shieldBreakVictim = meleeHolders[1];
                _deathVictim = rangedHolders[0];

                // Hitstun immediately releases melee slot #1
                _hitstunVictim.TakeDamage(1f);
                if (_hitstunVictim.State != EnemyState.Hitstun ||
                    _hitstunVictim.HoldsAttackToken ||
                    _roster.AttackCoordinator.ActiveMeleeCount != 1)
                {
                    _failures.Add($"live hitstun release failed (state={_hitstunVictim.State}, holds={_hitstunVictim.HoldsAttackToken}, activeMelee={_roster.AttackCoordinator.ActiveMeleeCount})");
                }

                // ShieldBroken (破盾失衡) immediately releases melee slot #2 -> pool empty so a waiting melee can promote next frame
                _shieldBreakVictim.EnterShieldBreak(1.0f, "live probe shield break");
                if (_shieldBreakVictim.State != EnemyState.ShieldBroken ||
                    _shieldBreakVictim.HoldsAttackToken ||
                    _roster.AttackCoordinator.ActiveMeleeCount != 0)
                {
                    _failures.Add($"live shield-break release failed (state={_shieldBreakVictim.State}, holds={_shieldBreakVictim.HoldsAttackToken}, activeMelee={_roster.AttackCoordinator.ActiveMeleeCount})");
                }

                // Death immediately releases ranged slot #1; kill slot #2 too so waiting ranged promotes immediately
                _deathVictim.TakeDamage(_deathVictim.MaxHp);
                if (_deathVictim.State != EnemyState.Dead ||
                    _deathVictim.HoldsAttackToken ||
                    _roster.AttackCoordinator.ActiveRangedCount != 1)
                {
                    _failures.Add($"live death release failed (state={_deathVictim.State}, holds={_deathVictim.HoldsAttackToken}, activeRanged={_roster.AttackCoordinator.ActiveRangedCount})");
                }

                rangedHolders[1].TakeDamage(rangedHolders[1].MaxHp);
                if (_roster.AttackCoordinator.ActiveRangedCount != 0)
                    _failures.Add($"second ranged death did not release token (activeRanged={_roster.AttackCoordinator.ActiveRangedCount})");
            }

            if (_triggeredReleaseChecks)
            {
                if (!_verifiedHitstunReplacement &&
                    HasNewAttackerExcluding(_meleeEnemies, _hitstunVictim, _shieldBreakVictim))
                {
                    _verifiedHitstunReplacement = true;
                }

                if (!_verifiedDeathReplacement &&
                    HasNewAttackerExcluding(_rangedEnemies, _deathVictim, null))
                {
                    _verifiedDeathReplacement = true;
                }

                if (_verifiedHitstunReplacement && _verifiedDeathReplacement && _sawBossTelegraph && _sawTargetTelegraph)
                {
                    CompleteProbe();
                    return;
                }
            }

            if (Now - _startedAt > 4.0f)
            {
                _failures.Add($"live concurrency probe timed out (maxM={_maxConcurrentMelee}, maxR={_maxConcurrentRanged}, " +
                              $"staggerM={_staggeredMeleePairsObserved}, staggerR={_staggeredRangedPairsObserved}, " +
                              $"boss={_sawBossTelegraph}, target={_sawTargetTelegraph}, " +
                              $"replM={_verifiedHitstunReplacement}, replR={_verifiedDeathReplacement})");
                CompleteProbe();
            }
        }

        private void OnRosterTelegraphStarted(RosterEnemyTelegraphCue rosterCue)
        {
            EnemyController enemy = rosterCue.Enemy;
            if (ReferenceEquals(enemy, _boss))
            {
                _sawBossTelegraph = true;
                return;
            }
            if (ReferenceEquals(enemy, _target))
            {
                _sawTargetTelegraph = true;
                return;
            }

            float expectedHit = rosterCue.Cue.StartedAtRealSec + rosterCue.Cue.Attack.TelegraphSeconds;
            AttackTokenPool pool = enemy.AttackTokenPoolKind;

            foreach (KeyValuePair<EnemyController, float> existing in _activeExpectedHitByEnemy)
            {
                EnemyController other = existing.Key;
                if (ReferenceEquals(other, enemy) || !GodotObject.IsInstanceValid(other))
                    continue;
                if (other.State is not (EnemyState.Telegraph or EnemyState.Strike))
                    continue;
                if (other.AttackTokenPoolKind != pool)
                    continue;

                float stagger = Mathf.Abs(expectedHit - existing.Value);
                if (stagger + StaggerCheckToleranceSec < EncounterAttackCoordinator.DefaultMinHitStaggerSeconds)
                {
                    _failures.Add($"same-pool ({pool}) real-timestamp hit stagger violated: {stagger:0.000}s < 0.350s");
                }
                else if (pool == AttackTokenPool.Melee)
                {
                    _staggeredMeleePairsObserved++;
                }
                else
                {
                    _staggeredRangedPairsObserved++;
                }
            }

            _activeExpectedHitByEnemy[enemy] = expectedHit;
        }

        private void PruneInactiveExpectedHits()
        {
            var stale = new List<EnemyController>();
            foreach (EnemyController enemy in _activeExpectedHitByEnemy.Keys)
            {
                if (!GodotObject.IsInstanceValid(enemy) || enemy.State is not (EnemyState.Telegraph or EnemyState.Strike))
                    stale.Add(enemy);
            }
            for (int i = 0; i < stale.Count; i++)
                _activeExpectedHitByEnemy.Remove(stale[i]);
        }

        private static bool IsAttacking(EnemyController enemy)
            => GodotObject.IsInstanceValid(enemy) &&
               enemy.State is EnemyState.Telegraph or EnemyState.Strike;

        private static List<EnemyController> GetAttacking<T>(List<T> enemies) where T : EnemyController
        {
            var list = new List<EnemyController>();
            for (int i = 0; i < enemies.Count; i++)
            {
                if (IsAttacking(enemies[i]))
                    list.Add(enemies[i]);
            }
            return list;
        }

        private static bool HasWaitingEnemy<T>(List<T> enemies) where T : EnemyController
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (GodotObject.IsInstanceValid(enemies[i]) &&
                    enemies[i].State is EnemyState.Idle or EnemyState.Chase &&
                    !enemies[i].HoldsAttackToken)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasNewAttackerExcluding<T>(List<T> enemies, EnemyController? excludeA, EnemyController? excludeB)
            where T : EnemyController
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyController enemy = enemies[i];
                if (ReferenceEquals(enemy, excludeA) || ReferenceEquals(enemy, excludeB))
                    continue;
                if (GodotObject.IsInstanceValid(enemy) &&
                    enemy.State is EnemyState.Telegraph or EnemyState.Strike &&
                    enemy.HoldsAttackToken)
                {
                    return true;
                }
            }
            return false;
        }

        private void CompleteProbe()
        {
            IsCompleted = true;

            if (_maxConcurrentMelee != 2)
                _failures.Add($"expected max concurrent melee == 2, observed {_maxConcurrentMelee}");
            if (_maxConcurrentRanged != 2)
                _failures.Add($"expected max concurrent ranged == 2, observed {_maxConcurrentRanged}");
            if (_staggeredMeleePairsObserved == 0 || _staggeredRangedPairsObserved == 0)
                _failures.Add($"expected real-timestamp staggered pairs in both pools (melee={_staggeredMeleePairsObserved}, ranged={_staggeredRangedPairsObserved})");
            if (!_sawUnapprovedMeleeWaiting || !_sawUnapprovedRangedWaiting)
                _failures.Add($"expected unapproved enemies to wait in Idle/Chase (melee={_sawUnapprovedMeleeWaiting}, ranged={_sawUnapprovedRangedWaiting})");
            if (!_sawBossTelegraph || !_sawTargetTelegraph)
                _failures.Add($"expected Boss & TrainingTarget to telegraph unobstructed (boss={_sawBossTelegraph}, target={_sawTargetTelegraph})");
            if (!_verifiedHitstunReplacement || !_verifiedDeathReplacement)
                _failures.Add($"expected waiting enemies to promote immediately after Hitstun/ShieldBroken/Death (melee={_verifiedHitstunReplacement}, ranged={_verifiedDeathReplacement})");

            if (GodotObject.IsInstanceValid(_stage) && _stage.GetParent() == this)
            {
                RemoveChild(_stage);
                if (_roster.AttackCoordinator.TotalActiveCount != 0)
                    _failures.Add($"teardown left active attack tokens ({_roster.AttackCoordinator.TotalActiveCount})");
                _stage.Free();
            }

            if (_failures.Count == 0)
            {
                GD.Print($"[Smoke] PASS live 4v4 encounter concurrency probe (max 2M+2R, staggered pairs M={_staggeredMeleePairsObserved} R={_staggeredRangedPairsObserved}, >=0.35s real timestamps, hitstun/shield-break/death immediate release)");
            }
            else
            {
                foreach (string reason in _failures)
                    _fail($"encounter-concurrency: {reason}");
            }
        }
    }
}
