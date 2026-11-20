using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// 重盾卫兵 (Heavy Shield Guard): directional frontal shield barrier enemy with 5 HP,
/// minimum real-timestamp turn delay, locked facing across attack phases, and
/// alternating golden shield slam / crimson unblockable shield charge.
/// </summary>
public partial class HeavyShieldEnemy : EnemyController
{
    public const string DefaultDisplayName = "重盾卫兵";
    public const float DefaultMaxHp = 5f;
    public const float DefaultChaseSpeed = 62f;
    public const float DefaultTurnDelaySeconds = 0.35f;
    public const float DefaultShieldBreakDurationSeconds = 2.40f;
    public const float DefaultShieldChargeSpeed = 360f;

    private const float FacingDeadzonePx = 4f;
    private const float StrikeBackMarginPx = 24f;
    private const float ShieldSlamDamage = 18f;
    private const float ShieldSlamRange = 175f;
    private const float ShieldChargeDamage = 24f;
    private const float ShieldChargeRange = 225f;

    [Export] public float TurnDelaySeconds { get; set; } = DefaultTurnDelaySeconds;
    [Export] public float ShieldBreakDurationSeconds { get; set; } = DefaultShieldBreakDurationSeconds;
    [Export] public float ShieldChargeSpeed { get; set; } = DefaultShieldChargeSpeed;

    private bool _facingInitialized;
    private int _pendingFacing;
    private float _pendingTurnSinceRealSec = -1f;
    private int _attackCycle;
    private bool _currentStrikeResolved;

    public HeavyShieldEnemy()
    {
        Name = "HeavyShieldEnemy";
        DisplayName = DefaultDisplayName;
        IsBoss = false;
        MaxHp = DefaultMaxHp;
        ChaseSpeed = DefaultChaseSpeed;
    }

    public override bool HasFrontalDefense => true;
    public override AttackTokenPool AttackTokenPoolKind => AttackTokenPool.Melee;
    public bool IsFacingLocked =>
        State is EnemyState.Telegraph or EnemyState.Strike or EnemyState.Recover
            or EnemyState.Hitstun or EnemyState.ShieldBroken or EnemyState.Dead or EnemyState.Victory;

    protected override Vector2 BodySize => new(62f, 100f);

    protected override EnemyVisual CreateVisual()
        => new HeavyShieldVisual { Name = "Visual" };

    protected override void UpdateFacing()
    {
        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
            return;

        float dx = player.GlobalPosition.X - GlobalPosition.X;

        // Immediately face the target on initial Bind / ResetTo before Spawn settles.
        if (!_facingInitialized)
        {
            _facingInitialized = true;
            ClearPendingTurn();
            if (Mathf.Abs(dx) > FacingDeadzonePx)
                SetFacing(dx > 0f ? 1 : -1);
            return;
        }

        // Completely lock horizontal facing during Telegraph, Strike, Recover, stagger, and defeat.
        if (IsFacingLocked)
        {
            ClearPendingTurn();
            return;
        }

        if (Mathf.Abs(dx) <= FacingDeadzonePx)
        {
            ClearPendingTurn();
            return;
        }

        int desiredFacing = dx > 0f ? 1 : -1;
        if (desiredFacing == Facing)
        {
            ClearPendingTurn();
            return;
        }

        float now = Now;
        if (_pendingFacing != desiredFacing)
        {
            _pendingFacing = desiredFacing;
            _pendingTurnSinceRealSec = now;
        }

        if (now - _pendingTurnSinceRealSec >= Mathf.Max(0f, TurnDelaySeconds))
        {
            SetFacing(desiredFacing);
            ClearPendingTurn();
        }
    }

    protected override void ThinkIdleOrChase()
    {
        PlayerController? player = TargetPlayer;
        if (player is not null && GodotObject.IsInstanceValid(player))
        {
            float signedDx = (player.GlobalPosition.X - GlobalPosition.X) * Facing;
            if (signedDx < -FacingDeadzonePx)
            {
                Velocity = Velocity with { X = 0f };
                SetState(EnemyState.Idle, "turning toward player");
                return;
            }
        }

        base.ThinkIdleOrChase();
    }

    protected override BossAttackDefinition SelectNextAttack()
    {
        bool useShieldSlam = _attackCycle % 2 == 0;
        _attackCycle++;
        return useShieldSlam ? CreateShieldSlamDefinition() : CreateShieldChargeDefinition();
    }

    protected override void BeginStrike()
    {
        _currentStrikeResolved = false;
        base.BeginStrike();
        SetNextAttackCooldown(Windows.EnemyAttackCooldown);
        Velocity = CurrentAttackKind == BossAttackKind.BreakGrab
            ? Velocity with { X = Facing * ShieldChargeSpeed }
            : Velocity with { X = 0f };
    }

    protected override bool IsTargetInStrikeThreat()
    {
        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
            return false;
        if (!IsInAttackRange(CurrentAttack.Range))
            return false;

        float signedDx = (player.GlobalPosition.X - GlobalPosition.X) * Facing;
        return signedDx >= -StrikeBackMarginPx;
    }

    protected override void ResolveStrike()
    {
        if (_currentStrikeResolved)
            return;

        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            _currentStrikeResolved = true;
            SetNextAttackCooldown(Windows.EnemyAttackCooldown);
            return;
        }

        if (!IsTargetInStrikeThreat())
        {
            if (CurrentAttackKind != BossAttackKind.BreakGrab)
            {
                _currentStrikeResolved = true;
                GD.Print($"[Enemy] {CurrentAttack.Name} whiffed (outside threat)");
                SetNextAttackCooldown(Windows.EnemyAttackCooldown);
            }
            return;
        }

        _currentStrikeResolved = true;
        bool answered = player.TryReceiveEnemyAttack(
            CurrentAttack.Damage,
            CurrentAttack.Kind,
            out AttackResolution resolution);
        GD.Print(answered
            ? $"[Enemy] {CurrentAttack.Name} answered: {resolution.Result}"
            : $"[Enemy] {CurrentAttack.Name} landed: {resolution.Result} (player hp {player.Hp:0})");

        if (resolution.Result == DefenseResult.PerfectBlocked &&
            resolution.EnemyStaggered &&
            CurrentAttackKind == BossAttackKind.Normal)
        {
            float breakDuration = Mathf.Max(ShieldBreakDurationSeconds, Windows.HitstunSeconds * 3f);
            EnterShieldBreak(breakDuration, "shield broken by frontal perfect block");
        }
        else if (resolution.EnemyStaggered)
        {
            EnterHitstun("staggered by perfect block");
        }

        SetNextAttackCooldown(Windows.EnemyAttackCooldown);
    }

    protected override void OnReset()
    {
        _facingInitialized = false;
        _attackCycle = 0;
        _currentStrikeResolved = false;
        ClearPendingTurn();
    }

    private BossAttackDefinition CreateShieldSlamDefinition()
        => new(
            BossAttackKind.Normal,
            "SHIELD SLAM",
            "BLOCK or DODGE",
            ShieldSlamDamage,
            Windows.NormalAttackTelegraphSeconds * 1.1f,
            Windows.NormalAttackActiveSeconds,
            Windows.NormalAttackRecoverySeconds * 1.25f,
            ShieldSlamRange,
            0f);

    private BossAttackDefinition CreateShieldChargeDefinition()
        => new(
            BossAttackKind.BreakGrab,
            "SHIELD CHARGE",
            "DODGE or K",
            ShieldChargeDamage,
            Windows.BreakGrabTelegraphSeconds * 0.9f,
            Windows.BreakGrabActiveSeconds * 1.15f,
            Windows.BreakGrabRecoverySeconds,
            ShieldChargeRange,
            0f);

    private void ClearPendingTurn()
    {
        _pendingFacing = 0;
        _pendingTurnSinceRealSec = -1f;
    }
}
