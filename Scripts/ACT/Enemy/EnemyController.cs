using System;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;

namespace ACTSpike.ACT.Enemy;

public enum EnemyState { Spawn, Idle, Chase, Telegraph, Strike, Recover, Hitstun, Dead, Victory }

public enum BossAttackKind
{
    Normal,
    BreakGrab,
    DelayedArea
}

/// <summary>Resolved attack data used by timestamp-driven enemy state machines.</summary>
public readonly record struct BossAttackDefinition(
    BossAttackKind Kind,
    string Name,
    string ResponseHint,
    float Damage,
    float TelegraphSeconds,
    float ActiveSeconds,
    float RecoverySeconds,
    float Range,
    float HazardRadius)
{
    public static BossAttackDefinition CreateNormal(CombatWindowConfig windows)
        => new(
            BossAttackKind.Normal,
            "NORMAL STRIKE",
            "BLOCK or DODGE",
            windows.NormalAttackDamage,
            windows.NormalAttackTelegraphSeconds,
            windows.NormalAttackActiveSeconds,
            windows.NormalAttackRecoverySeconds,
            windows.NormalAttackRange,
            0f);

    public static BossAttackDefinition CreateBreakGrab(CombatWindowConfig windows)
        => new(
            BossAttackKind.BreakGrab,
            "BREAK GRAB",
            "DODGE or K",
            windows.BreakGrabDamage,
            windows.BreakGrabTelegraphSeconds,
            windows.BreakGrabActiveSeconds,
            windows.BreakGrabRecoverySeconds,
            windows.BreakGrabRange,
            0f);

    public static BossAttackDefinition CreateDelayedArea(CombatWindowConfig windows)
        => new(
            BossAttackKind.DelayedArea,
            "DELAYED AREA",
            "MOVE or TIME SLOW",
            windows.DelayedAreaDamage,
            windows.DelayedAreaTelegraphSeconds,
            windows.DelayedAreaActiveSeconds,
            windows.DelayedAreaRecoverySeconds,
            0f,
            windows.DelayedAreaRadius);
}

/// <summary>
/// Generic enemy survival, directional hit-intake, real-timestamp state machine,
/// hurtbox/body setup, overhead health bar hook, and read-only presentation event base.
/// Boss-specific attack cycles and enemy archetype AI live in dedicated subclasses.
/// </summary>
public partial class EnemyController : CharacterBody2D
{
    [Export] public float MaxHp { get; set; } = 6f;
    [Export] public float ChaseSpeed { get; set; } = 90f;
    [Export] public float Gravity { get; set; } = 1200f;
    [Export] public bool IsBoss { get; set; }
    [Export] public string DisplayName { get; set; } = "遗迹敌兵";
    public float Hp { get; private set; }
    public int Facing { get; protected set; } = -1;
    public EnemyVisual Visual { get; private set; } = null!;

    public CombatWindowConfig Windows { get; set; } = new();
    public EnemyState State { get; private set; } = EnemyState.Spawn;
    public EnemyState PreviousChaseState { get; private set; } = EnemyState.Idle;
    public BossAttackDefinition CurrentAttack => _hasCurrentAttack ? _currentAttack : CreateNormalAttackDefinition();
    public BossAttackKind CurrentAttackKind => CurrentAttack.Kind;
    public bool IsDefeated => State is EnemyState.Dead or EnemyState.Victory;
    public bool ShowsOverheadHealthBar => !IsBoss && !IsDefeated;
    /// <summary>Normalized 0..1 telegraph progress computed from real timestamps.</summary>
    public float TelegraphProgress => State == EnemyState.Telegraph
        ? Mathf.Clamp((Now - _telegraphStartedAt) / Mathf.Max(0.001f, CurrentAttack.TelegraphSeconds), 0f, 1f)
        : 0f;

    /// <summary>Read-only presentation cues for visual/audio layers (never mutate combat state).</summary>
    public event Action<EnemyTelegraphCue>? TelegraphStarted;
    public event Action<EnemyStrikeCue>? StrikeActivated;
    public event Action<EnemyDamagedCue>? Damaged;
    public event Action<EnemyShieldDeflectedCue>? ShieldDeflected;
    public event Action<EnemyProjectileFiredCue>? ProjectileFired;
    public event Action<EnemyOverloadPulseCue>? OverloadPulseTriggered;

    public virtual bool IsInvulnerable => false;
    public virtual bool HasFrontalDefense => false;
    public virtual bool TryDefend(AttackResolution incoming) => false;

    private PlayerController? _player;
    private TimeSlowManager? _timeSlow;
    private BossAttackDefinition _currentAttack;
    private bool _hasCurrentAttack;
    private float _stateEndsAt;
    private float _telegraphStartedAt;
    private float _nextAttackAllowedAt;
    private bool _strikeResolved;

    protected PlayerController? TargetPlayer => _player;
    protected TimeSlowManager? BoundTimeSlow => _timeSlow;
    protected float Now => Time.GetTicksMsec() / 1000f;
    protected bool CanStartAttack => Now >= _nextAttackAllowedAt;
    protected virtual Vector2 BodySize => new(52f, 100f);

    public override void _Ready()
    {
        CollisionLayer = 1;
        CollisionMask = 2;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = BodySize },
            Name = "BodyShape"
        });

        var hurtbox = new Area2D
        {
            Name = "Hurtbox",
            CollisionLayer = 4,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = true
        };
        hurtbox.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = BodySize } });
        hurtbox.AddToGroup("enemy_hurtbox");
        AddChild(hurtbox);

        Visual = CreateVisual();
        AddChild(Visual);
        Visual.Bind(this, _player);
        Visual.BindTimeSlow(_timeSlow);
    }

    protected virtual EnemyVisual CreateVisual() => new EnemyVisual { Name = "Visual" };

    public void Bind(PlayerController player, CombatWindowConfig windows)
    {
        _player = player;
        Windows = windows;
        Hp = MaxHp;
        _hasCurrentAttack = false;
        _strikeResolved = false;
        _nextAttackAllowedAt = Now + Windows.EnemyInitialDelaySeconds;
        _stateEndsAt = Now + Windows.BossSpawnSeconds;
        OnReset();
        UpdateFacing();
        Visual?.Bind(this, player);
        SetState(EnemyState.Spawn, "spawn");
    }

    public void BindTimeSlow(TimeSlowManager? timeSlow)
    {
        _timeSlow = timeSlow;
        Visual?.BindTimeSlow(timeSlow);
    }

    /// <summary>Delegates local visual-only freeze to the enemy's visual node without touching Engine.TimeScale.</summary>
    public void SetVisualFreeze(bool frozen) => Visual?.SetVisualFreeze(frozen);

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (!IsOnFloor()) Velocity = Velocity with { Y = Velocity.Y + Gravity * dt };
        UpdateFacing();

        switch (State)
        {
            case EnemyState.Spawn:
                Velocity = Velocity with { X = 0 };
                if (Now >= _stateEndsAt) SetState(EnemyState.Idle, "spawn complete");
                break;
            case EnemyState.Dead:
                Velocity = Vector2.Zero;
                if (Now >= _stateEndsAt) SetState(EnemyState.Victory, "victory confirmed");
                break;
            case EnemyState.Victory:
                Velocity = Vector2.Zero;
                break;
            case EnemyState.Hitstun:
                Velocity = Velocity with { X = 0 };
                if (Now >= _stateEndsAt) SetState(PreviousChaseState, "recovered from hitstun");
                break;
            case EnemyState.Telegraph:
                Velocity = Velocity with { X = 0 };
                if (Now >= _stateEndsAt) BeginStrike();
                break;
            case EnemyState.Strike:
                if (!_strikeResolved) ResolveStrike();
                if (Now >= _stateEndsAt)
                {
                    _stateEndsAt = Now + CurrentAttack.RecoverySeconds;
                    SetState(EnemyState.Recover, $"{CurrentAttack.Name} active window ended");
                }
                break;
            case EnemyState.Recover:
                Velocity = Velocity with { X = 0 };
                if (Now >= _stateEndsAt) SetState(EnemyState.Idle, "recovered");
                break;
            default:
                ThinkIdleOrChase();
                break;
        }
        MoveAndSlide();
    }

    protected virtual void UpdateFacing()
    {
        if (IsDefeated || _player is null || !GodotObject.IsInstanceValid(_player))
            return;
        Facing = _player.GlobalPosition.X >= GlobalPosition.X ? 1 : -1;
    }

    protected void SetFacing(int facing) => Facing = facing >= 0 ? 1 : -1;

    protected void SetNextAttackCooldown(float delaySeconds) => _nextAttackAllowedAt = Now + delaySeconds;

    protected virtual void ThinkIdleOrChase()
    {
        if (_player is null || !GodotObject.IsInstanceValid(_player))
        {
            Velocity = Velocity with { X = 0 };
            SetState(EnemyState.Idle, "no target");
            return;
        }

        float distance = _player.GlobalPosition.DistanceTo(GlobalPosition);
        bool inRange = distance <= Windows.EnemyAttackRange &&
                       Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 90f;

        if (inRange && CanStartAttack)
        {
            BeginTelegraph();
            return;
        }

        if (distance > Windows.EnemyAttackRange * 0.7f)
        {
            PreviousChaseState = EnemyState.Chase;
            SetState(EnemyState.Chase, "chasing player");
            float dir = Mathf.Sign(_player.GlobalPosition.X - GlobalPosition.X);
            Velocity = Velocity with { X = dir * ChaseSpeed };
        }
        else
        {
            PreviousChaseState = EnemyState.Idle;
            SetState(EnemyState.Idle, "holding position");
            Velocity = Velocity with { X = 0 };
        }
    }

    protected virtual BossAttackDefinition SelectNextAttack() => CreateNormalAttackDefinition();

    protected void BeginTelegraph() => BeginTelegraph(SelectNextAttack());

    protected void BeginTelegraph(BossAttackDefinition attack)
    {
        _currentAttack = attack;
        _hasCurrentAttack = true;
        _strikeResolved = false;
        float now = Now;
        _telegraphStartedAt = now;
        _stateEndsAt = now + attack.TelegraphSeconds;
        SetState(EnemyState.Telegraph, $"{attack.Name} telegraph: {attack.ResponseHint}");
        TelegraphStarted?.Invoke(new EnemyTelegraphCue(attack, GlobalPosition, now));
        GD.Print($"[Enemy] telegraph {attack.Name} | response {attack.ResponseHint} | " +
                 $"telegraph {attack.TelegraphSeconds:0.00}s active {attack.ActiveSeconds:0.00}s " +
                 $"recovery {attack.RecoverySeconds:0.00}s damage {attack.Damage:0}");
    }

    protected virtual void BeginStrike()
    {
        _stateEndsAt = Now + CurrentAttack.ActiveSeconds;
        SetState(EnemyState.Strike, $"{CurrentAttack.Name} active");
        StrikeActivated?.Invoke(new EnemyStrikeCue(CurrentAttack, GlobalPosition));
    }

    protected virtual bool IsTargetInStrikeThreat() => IsInAttackRange(CurrentAttack.Range);

    protected virtual void ResolveStrike()
    {
        _strikeResolved = true;
        if (_player is null || !GodotObject.IsInstanceValid(_player))
        {
            SetNextAttackCooldown(Windows.EnemyAttackCooldown);
            return;
        }

        if (!IsTargetInStrikeThreat())
        {
            GD.Print($"[Enemy] {CurrentAttack.Name} whiffed (outside threat)");
            SetNextAttackCooldown(Windows.EnemyAttackCooldown);
            return;
        }

        bool answered = _player.TryReceiveEnemyAttack(CurrentAttack.Damage, CurrentAttack.Kind,
            out AttackResolution resolution);
        GD.Print(answered
            ? $"[Enemy] {CurrentAttack.Name} answered: {resolution.Result}"
            : $"[Enemy] {CurrentAttack.Name} landed: {resolution.Result} (player hp {_player.Hp:0})");
        if (resolution.EnemyStaggered)
            EnterHitstun("staggered by perfect block");

        SetNextAttackCooldown(Windows.EnemyAttackCooldown);
    }

    protected bool IsInAttackRange(float range)
        => _player is not null &&
           _player.GlobalPosition.DistanceTo(GlobalPosition) <= range &&
           Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 110f;

    protected virtual bool ShouldDeflectHit(in EnemyHitContext hit)
        => HasFrontalDefense && hit.IsFrontalStrike(GlobalPosition, Facing);

    /// <summary>
    /// Resolves an incoming directional strike against this enemy, supporting frontal shield
    /// deflection and directional skill centerline back-piercing.
    /// </summary>
    public EnemyHitOutcome TakeHit(in EnemyHitContext hit)
    {
        if (State == EnemyState.Spawn)
        {
            GD.Print("[Enemy] damage ignored during spawn");
            return EnemyHitOutcome.Ignored;
        }
        if (State is EnemyState.Dead or EnemyState.Victory)
            return EnemyHitOutcome.Ignored;
        if (IsInvulnerable)
        {
            GD.Print("[Enemy] damage ignored (invulnerable)");
            return EnemyHitOutcome.Ignored;
        }

        if (ShouldDeflectHit(hit))
        {
            GD.Print($"[Enemy] frontal shield deflected {(hit.IsDirectionalSkill ? "skill" : "attack")} ({hit.Damage:0})");
            ShieldDeflected?.Invoke(new EnemyShieldDeflectedCue(hit, GlobalPosition, Facing));
            return EnemyHitOutcome.Deflected;
        }

        Hp -= hit.Damage;
        if (Hp <= 0)
        {
            Hp = 0;
            _stateEndsAt = Now + Windows.VictoryDelaySeconds;
            SetState(EnemyState.Dead, "destroyed");
            Damaged?.Invoke(new EnemyDamagedCue(hit.Damage, Hp, MaxHp, Defeated: true, GlobalPosition));
            return EnemyHitOutcome.Damaged;
        }

        EnterHitstun($"took {hit.Damage:0} damage");
        Damaged?.Invoke(new EnemyDamagedCue(hit.Damage, Hp, MaxHp, Defeated: false, GlobalPosition));
        return EnemyHitOutcome.Damaged;
    }

    /// <summary>Convenience wrapper for direct non-directional damage (bypasses frontal shields).</summary>
    public bool TakeDamage(float damage)
    {
        var directHit = new EnemyHitContext(
            Damage: damage,
            AttackerOrigin: GlobalPosition - new Vector2(Facing, 0f),
            MotionDirection: new Vector2(Facing, 0f),
            IsDirectionalSkill: false,
            CrossedTargetCenterline: false);
        return TakeHit(directHit) == EnemyHitOutcome.Damaged;
    }

    protected void EnterHitstun(string reason)
    {
        _stateEndsAt = Now + Windows.HitstunSeconds;
        SetState(EnemyState.Hitstun, reason);
    }

    protected void EmitProjectileFired(EnemyProjectileFiredCue cue) => ProjectileFired?.Invoke(cue);

    protected void EmitOverloadPulseTriggered(EnemyOverloadPulseCue cue) => OverloadPulseTriggered?.Invoke(cue);

    protected virtual void OnReset() { }

    public void ResetTo(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Hp = MaxHp;
        _hasCurrentAttack = false;
        _nextAttackAllowedAt = Now + Windows.EnemyInitialDelaySeconds;
        _stateEndsAt = Now + Windows.BossSpawnSeconds;
        _strikeResolved = false;
        OnReset();
        UpdateFacing();
        SetState(EnemyState.Spawn, "round reset");
    }

    protected BossAttackDefinition CreateNormalAttackDefinition()
        => BossAttackDefinition.CreateNormal(Windows);

    protected void SetState(EnemyState next, string reason)
    {
        if (State == next) return;
        GD.Print($"[Enemy] {State} -> {next} ({reason})");
        State = next;
    }
}
