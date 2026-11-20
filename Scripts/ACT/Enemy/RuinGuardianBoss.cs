using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// Monolith sub-boss「遗迹守卫」(Ruin Guardian):
/// 28 HP super-armor two-phase boss with distance-aware move selection, lunge-step strikes,
/// arena-bounded short backsteps, half-HP invulnerable overload knockback transition,
/// tightened Phase 2 windows, and two readable Phase 2 combo follow-ups.
/// </summary>
public partial class RuinGuardianBoss : EnemyController
{
    public const string DefaultDisplayName = "遗迹守卫 · 巨构核心";
    public const float DefaultMaxHp = 28f;
    public const float PoiseBreakStaggerSeconds = 1.05f;
    public const float CloseRangeThreshold = 220f;
    public const float FarEngagementRange = 540f;
    public const float OverloadPushRadius = 260f;
    public const float OverloadPushSpeed = 580f;
    public const float OverloadPushDurationSeconds = 0.24f;
    public const float PhaseTwoTimingScale = 0.80f;
    public const float PhaseTwoComboDelayedAreaTelegraphScale = 0.60f;

    private const float LungeStepSpeed = 600f;
    private const float LungeStepMaxSeconds = 0.26f;
    private const float LungeMaxDistance = 145f;
    private const float LungeStopDistance = 135f;
    private const float StrikeForwardBurstSpeed = 360f;
    private const float CounterGrabLungeSpeed = 420f;

    private const float BackstepSpeed = 460f;
    private const float BackstepDurationPhase1 = 0.22f;
    private const float BackstepDurationPhase2 = 0.18f;
    private const float BackstepTargetDistancePhase1 = 90f;
    private const float BackstepTargetDistancePhase2 = 82f;
    private const float MinBackstepClearance = 75f;
    private const float PostBackstepCooldownSeconds = 0.30f;
    private const float PostOverloadCooldownSeconds = 0.35f;
    private const float MinReadableTelegraphMarginOverPerfectGuard = 0.10f;

    private enum GuardianMotionMode
    {
        None,
        LungeApproach,
        Backstep,
        OverloadTransition
    }

    [Export] public float ArenaMinX { get; set; } = float.NegativeInfinity;
    [Export] public float ArenaMaxX { get; set; } = float.PositiveInfinity;
    [Export] public float OverloadTransitionSeconds { get; set; } = 0.65f;

    public int Phase { get; private set; } = 1;
    public bool IsPhaseTwo => Phase >= 2;
    public bool IsOverloadFissureActive => IsPhaseTwo && !IsDefeated;
    public bool IsOverloadTransitionActive => _motionMode == GuardianMotionMode.OverloadTransition;
    public override bool IsInvulnerable => IsOverloadTransitionActive;
    public override bool IsFacingLocked =>
        base.IsFacingLocked || _motionMode is GuardianMotionMode.Backstep or GuardianMotionMode.OverloadTransition;
    public bool HasArenaBounds => !float.IsInfinity(ArenaMinX) && !float.IsInfinity(ArenaMaxX) && ArenaMaxX > ArenaMinX;
    public bool IsLunging => _motionMode == GuardianMotionMode.LungeApproach;
    public bool IsBackstepping => _motionMode == GuardianMotionMode.Backstep;

    protected override bool AllowContinuousStrikeResolution
        => _currentAttackIsLungeNormal || _currentAttackIsCounterGrab;

    private GuardianMotionMode _motionMode = GuardianMotionMode.None;
    private int _motionDirection = -1;
    private float _motionEndsAt;
    private float _motionDistanceMoved;
    private float _motionTargetDistance;
    private bool _hasLastAttack;
    private BossAttackKind _lastAttackKind;
    private bool _lastFarAttackWasLungeNormal;
    private bool _currentAttackIsLungeNormal;
    private bool _currentAttackIsCounterGrab;
    private BossAttackKind? _pendingComboFollowup;
    private int _attacksSinceBackstep;

    public RuinGuardianBoss()
    {
        Name = "Boss";
        DisplayName = DefaultDisplayName;
        IsBoss = true;
        MaxHp = DefaultMaxHp;
        ChaseSpeed = 125f;
    }

    public BossAttackDefinition DefinitionFor(BossAttackKind kind, bool acceleratedCombo = false)
    {
        BossAttackDefinition baseDef = kind switch
        {
            BossAttackKind.BreakGrab => BossAttackDefinition.CreateBreakGrab(Windows),
            BossAttackKind.DelayedArea => BossAttackDefinition.CreateDelayedArea(Windows),
            _ => BossAttackDefinition.CreateNormal(Windows)
        };

        if (!IsPhaseTwo)
            return baseDef;

        float telegraphScale = acceleratedCombo && kind == BossAttackKind.DelayedArea
            ? PhaseTwoComboDelayedAreaTelegraphScale
            : PhaseTwoTimingScale;

        float minReadableTelegraph = Windows.PerfectGuardSeconds + MinReadableTelegraphMarginOverPerfectGuard;
        float telegraph = baseDef.TelegraphSeconds > 0f
            ? Mathf.Max(minReadableTelegraph, baseDef.TelegraphSeconds * telegraphScale)
            : 0f;
        float recovery = baseDef.RecoverySeconds * PhaseTwoTimingScale;

        return baseDef with
        {
            TelegraphSeconds = telegraph,
            RecoverySeconds = recovery
        };
    }

    protected override EnemyVisual CreateVisual() => new RuinGuardianVisual { Name = "Visual" };

    protected override bool ShouldEnterHitstunOnDamage() => false;

    protected override void OnDamagedPostApply(bool defeated)
    {
        if (defeated || IsPhaseTwo || Hp > MaxHp * 0.5f)
            return;

        TriggerPhaseTwoOverload();
    }

    protected override bool ProcessCustomPhysicsState(float dt)
    {
        if (IsDefeated || _motionMode == GuardianMotionMode.None)
            return false;

        if (_motionMode == GuardianMotionMode.OverloadTransition)
        {
            Velocity = Velocity with { X = 0f };
            if (Now >= _motionEndsAt)
            {
                _motionMode = GuardianMotionMode.None;
                SetNextAttackCooldown(Mathf.Min(PostOverloadCooldownSeconds, Windows.EnemyAttackCooldown));
                SetState(EnemyState.Idle, "phase 2 overload ready");
            }
            return true;
        }

        if (_motionMode == GuardianMotionMode.LungeApproach)
        {
            StepLungeApproach(dt);
            return true;
        }

        if (_motionMode == GuardianMotionMode.Backstep)
        {
            StepBackstep(dt);
            return true;
        }

        return false;
    }

    protected override void ThinkIdleOrChase()
    {
        if (TargetPlayer is null || !GodotObject.IsInstanceValid(TargetPlayer))
        {
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "no target");
            return;
        }

        float dx = TargetPlayer.GlobalPosition.X - GlobalPosition.X;
        float horizontalDist = Mathf.Abs(dx);
        bool verticalAligned = Mathf.Abs(TargetPlayer.GlobalPosition.Y - GlobalPosition.Y) < 120f;

        if (verticalAligned && CanStartAttack)
        {
            if (horizontalDist <= CloseRangeThreshold)
            {
                BossAttackKind closeKind = SelectCloseRangeKind();
                BeginGuardianAttack(closeKind, isLungeNormal: false, isCounterGrab: false, isAcceleratedCombo: false);
                return;
            }

            if (horizontalDist <= FarEngagementRange)
            {
                if (ShouldSelectFarLungeNormal())
                {
                    StartLungeApproach(dx >= 0f ? 1 : -1);
                }
                else
                {
                    _lastFarAttackWasLungeNormal = false;
                    BeginGuardianAttack(BossAttackKind.DelayedArea, isLungeNormal: false, isCounterGrab: false, isAcceleratedCombo: false);
                }
                return;
            }
        }

        if (horizontalDist > CloseRangeThreshold * 0.72f)
        {
            int dir = dx >= 0f ? 1 : -1;
            if (WouldExceedArenaBounds(dir, ChaseSpeed * 0.05f))
            {
                Velocity = Velocity with { X = 0f };
                SetState(EnemyState.Idle, "holding at arena boundary");
                return;
            }

            SetState(EnemyState.Chase, "advancing on player");
            Velocity = Velocity with { X = dir * ChaseSpeed };
        }
        else
        {
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "holding combat stance");
        }
    }

    protected override BossAttackDefinition SelectNextAttack()
    {
        BossAttackKind kind = SelectCloseRangeKind();
        RecordAttackSelection(kind, isLungeNormal: false, isCounterGrab: false);
        return DefinitionFor(kind);
    }

    protected override void BeginStrike()
    {
        base.BeginStrike();
        _pendingComboFollowup = IsPhaseTwo && _currentAttackIsLungeNormal && CurrentAttack.Kind == BossAttackKind.Normal
            ? BossAttackKind.DelayedArea
            : null;

        if (_currentAttackIsCounterGrab && !WouldExceedArenaBounds(Facing, 30f))
            Velocity = Velocity with { X = Facing * CounterGrabLungeSpeed };
        else if (_currentAttackIsLungeNormal && !WouldExceedArenaBounds(Facing, 24f))
            Velocity = Velocity with { X = Facing * StrikeForwardBurstSpeed };
        else
            Velocity = Velocity with { X = 0f };
    }

    protected override bool IsTargetInStrikeThreat()
    {
        if (CurrentAttack.Kind == BossAttackKind.DelayedArea)
            return TargetPlayer is not null &&
                   GodotObject.IsInstanceValid(TargetPlayer) &&
                   TargetPlayer.GlobalPosition.DistanceTo(GlobalPosition) <= CurrentAttack.HazardRadius;

        return base.IsTargetInStrikeThreat();
    }

    protected override void OnStrikeResolved(AttackResolution resolution)
    {
        if (resolution.EnemyStaggered && CurrentAttack.Kind == BossAttackKind.Normal)
        {
            _pendingComboFollowup = null;
            EnterHitstun("poise broken by perfect block", PoiseBreakStaggerSeconds);
        }
    }

    protected override void OnStrikeEnded()
    {
        if (_pendingComboFollowup is { } followup)
        {
            _pendingComboFollowup = null;
            Velocity = Velocity with { X = 0f };
            BeginGuardianAttack(followup, isLungeNormal: false, isCounterGrab: false, isAcceleratedCombo: true);
            return;
        }

        base.OnStrikeEnded();
    }

    protected override void OnRecoverEnded()
    {
        if (ShouldTriggerPostRecoveryBackstep(out int retreatDir))
        {
            StartBackstep(retreatDir);
            return;
        }

        base.OnRecoverEnded();
    }

    protected override void OnReset()
    {
        Phase = 1;
        _motionMode = GuardianMotionMode.None;
        _motionDirection = -1;
        _motionEndsAt = 0f;
        _motionDistanceMoved = 0f;
        _motionTargetDistance = 0f;
        _hasLastAttack = false;
        _lastAttackKind = BossAttackKind.Normal;
        _lastFarAttackWasLungeNormal = false;
        _currentAttackIsLungeNormal = false;
        _currentAttackIsCounterGrab = false;
        _pendingComboFollowup = null;
        _attacksSinceBackstep = 0;
    }

    private void TriggerPhaseTwoOverload()
    {
        Phase = 2;
        _motionMode = GuardianMotionMode.OverloadTransition;
        _motionEndsAt = Now + Mathf.Max(0f, OverloadTransitionSeconds);
        _currentAttackIsLungeNormal = false;
        _currentAttackIsCounterGrab = false;
        _pendingComboFollowup = null;
        Velocity = Velocity with { X = 0f };
        ClearPendingAttack();
        ReleaseAttackToken();
        SetState(EnemyState.Idle, "phase 2 overload pulse");

        if (TargetPlayer is not null && GodotObject.IsInstanceValid(TargetPlayer))
        {
            Vector2 offset = TargetPlayer.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(offset.X) <= OverloadPushRadius && Mathf.Abs(offset.Y) < 160f)
            {
                int pushDir = Mathf.Abs(offset.X) > 1f ? (offset.X > 0f ? 1 : -1) : Facing;
                TargetPlayer.ApplyExternalHorizontalPush(pushDir * OverloadPushSpeed, OverloadPushDurationSeconds);
            }
        }

        EmitOverloadPulseTriggered(new EnemyOverloadPulseCue(GlobalPosition, PhaseNumber: 2));
        GD.Print($"[Boss] Phase 2 overload triggered at {Hp:0}/{MaxHp:0} HP");
    }

    private BossAttackKind SelectCloseRangeKind()
    {
        if (!_hasLastAttack)
            return BossAttackKind.Normal;

        return _lastAttackKind == BossAttackKind.Normal
            ? BossAttackKind.BreakGrab
            : BossAttackKind.Normal;
    }

    private bool ShouldSelectFarLungeNormal()
    {
        if (_hasLastAttack && _lastAttackKind == BossAttackKind.Normal)
            return false;
        if (_hasLastAttack && _lastAttackKind == BossAttackKind.DelayedArea)
            return true;
        return !_lastFarAttackWasLungeNormal;
    }

    private void BeginGuardianAttack(
        BossAttackKind kind,
        bool isLungeNormal,
        bool isCounterGrab,
        bool isAcceleratedCombo)
    {
        ClearPendingAttack();
        RecordAttackSelection(kind, isLungeNormal, isCounterGrab);
        BossAttackDefinition definition = DefinitionFor(kind, isAcceleratedCombo);
        TryBeginTelegraph(definition);
    }

    private void RecordAttackSelection(BossAttackKind kind, bool isLungeNormal, bool isCounterGrab)
    {
        _hasLastAttack = true;
        _lastAttackKind = kind;
        _currentAttackIsLungeNormal = isLungeNormal;
        _currentAttackIsCounterGrab = isCounterGrab;
        if (isLungeNormal)
            _lastFarAttackWasLungeNormal = true;
        else if (kind == BossAttackKind.DelayedArea)
            _lastFarAttackWasLungeNormal = false;
        _attacksSinceBackstep++;
    }

    private void StartLungeApproach(int dir)
    {
        SetFacing(dir);
        _motionMode = GuardianMotionMode.LungeApproach;
        _motionDirection = dir;
        _motionDistanceMoved = 0f;
        _motionTargetDistance = LungeMaxDistance;
        _motionEndsAt = Now + LungeStepMaxSeconds;
        SetState(EnemyState.Chase, "lunge step toward player");
        Velocity = Velocity with { X = dir * LungeStepSpeed };
    }

    private void StepLungeApproach(float dt)
    {
        float stepDist = LungeStepSpeed * dt;
        bool targetInvalid = TargetPlayer is null || !GodotObject.IsInstanceValid(TargetPlayer);
        float distToPlayer = targetInvalid
            ? 0f
            : Mathf.Abs(TargetPlayer!.GlobalPosition.X - GlobalPosition.X);

        bool blockedByWall = IsOnWall() || WouldExceedArenaBounds(_motionDirection, stepDist);
        bool done = targetInvalid ||
                    distToPlayer <= LungeStopDistance ||
                    _motionDistanceMoved >= _motionTargetDistance ||
                    Now >= _motionEndsAt ||
                    blockedByWall;

        if (done)
        {
            _motionMode = GuardianMotionMode.None;
            Velocity = Velocity with { X = 0f };
            BeginGuardianAttack(BossAttackKind.Normal, isLungeNormal: true, isCounterGrab: false, isAcceleratedCombo: false);
            return;
        }

        Velocity = Velocity with { X = _motionDirection * LungeStepSpeed };
        _motionDistanceMoved += stepDist;
    }

    private bool ShouldTriggerPostRecoveryBackstep(out int retreatDir)
    {
        retreatDir = -Facing;
        if (TargetPlayer is null || !GodotObject.IsInstanceValid(TargetPlayer))
            return false;

        float horizontalDist = Mathf.Abs(TargetPlayer.GlobalPosition.X - GlobalPosition.X);
        if (horizontalDist > CloseRangeThreshold)
            return false;

        if (IsPhaseTwo)
        {
            if (_currentAttackIsCounterGrab || _attacksSinceBackstep < 1)
                return false;
        }
        else
        {
            if (_attacksSinceBackstep < 2 && _lastAttackKind != BossAttackKind.BreakGrab)
                return false;
        }

        retreatDir = TargetPlayer.GlobalPosition.X >= GlobalPosition.X ? -1 : 1;
        return CanBackstep(retreatDir);
    }

    private bool CanBackstep(int retreatDir)
    {
        if (WouldExceedArenaBounds(retreatDir, MinBackstepClearance))
            return false;

        return !TestMove(GlobalTransform, new Vector2(retreatDir * MinBackstepClearance, 0f));
    }

    private void StartBackstep(int retreatDir)
    {
        _motionMode = GuardianMotionMode.Backstep;
        _motionDirection = retreatDir;
        _motionDistanceMoved = 0f;
        _motionTargetDistance = IsPhaseTwo ? BackstepTargetDistancePhase2 : BackstepTargetDistancePhase1;
        _motionEndsAt = Now + (IsPhaseTwo ? BackstepDurationPhase2 : BackstepDurationPhase1);
        _attacksSinceBackstep = 0;
        SetState(EnemyState.Recover, "short tactical backstep");
        Velocity = Velocity with { X = retreatDir * BackstepSpeed };
    }

    private void StepBackstep(float dt)
    {
        float stepDist = BackstepSpeed * dt;
        bool blocked = IsOnWall() || WouldExceedArenaBounds(_motionDirection, stepDist);
        bool done = _motionDistanceMoved >= _motionTargetDistance ||
                    Now >= _motionEndsAt ||
                    blocked;

        if (done)
        {
            _motionMode = GuardianMotionMode.None;
            Velocity = Velocity with { X = 0f };
            ClampPositionToArenaBounds();

            if (IsPhaseTwo && TargetPlayer is not null && GodotObject.IsInstanceValid(TargetPlayer))
            {
                SetFacing(TargetPlayer.GlobalPosition.X >= GlobalPosition.X ? 1 : -1);
                BeginGuardianAttack(BossAttackKind.BreakGrab, isLungeNormal: false, isCounterGrab: true, isAcceleratedCombo: false);
                return;
            }

            SetNextAttackCooldown(Mathf.Min(PostBackstepCooldownSeconds, Windows.EnemyAttackCooldown));
            SetState(EnemyState.Idle, "backstep landed");
            return;
        }

        Velocity = Velocity with { X = _motionDirection * BackstepSpeed };
        _motionDistanceMoved += stepDist;
    }

    private bool WouldExceedArenaBounds(int dir, float distance)
    {
        if (!HasArenaBounds) return false;
        float projectedX = GlobalPosition.X + dir * distance;
        return projectedX < ArenaMinX || projectedX > ArenaMaxX;
    }

    private void ClampPositionToArenaBounds()
    {
        if (!HasArenaBounds) return;
        if (GlobalPosition.X < ArenaMinX || GlobalPosition.X > ArenaMaxX)
            GlobalPosition = GlobalPosition with { X = Mathf.Clamp(GlobalPosition.X, ArenaMinX, ArenaMaxX) };
    }
}
