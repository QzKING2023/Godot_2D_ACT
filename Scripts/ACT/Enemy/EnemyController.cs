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

/// <summary>Resolved attack data used by the timestamp-driven boss state machine.</summary>
public readonly record struct BossAttackDefinition(
    BossAttackKind Kind,
    string Name,
    string ResponseHint,
    float Damage,
    float TelegraphSeconds,
    float ActiveSeconds,
    float RecoverySeconds,
    float Range,
    float HazardRadius);

/// <summary>
/// Boss attack flow: Chase/Idle -> Telegraph -> Strike -> Recover.
/// Every attack uses real-time timestamps and a configured active/recovery
/// window. Visual rendering and telegraphs are delegated to EnemyVisual.
/// </summary>
public partial class EnemyController : CharacterBody2D
{
    [Export] public float MaxHp { get; set; } = 6f;
    [Export] public float ChaseSpeed { get; set; } = 90f;
    [Export] public float Gravity { get; set; } = 1200f;
    public float Hp { get; private set; }
    public EnemyVisual Visual { get; private set; } = null!;

    public CombatWindowConfig Windows { get; set; } = new();
    public EnemyState State { get; private set; } = EnemyState.Spawn;
    public EnemyState PreviousChaseState { get; private set; } = EnemyState.Idle;
    public BossAttackKind CurrentAttackKind { get; private set; } = BossAttackKind.Normal;
    public BossAttackDefinition CurrentAttack => DefinitionFor(CurrentAttackKind);
    public bool IsDefeated => State is EnemyState.Dead or EnemyState.Victory;
    /// <summary>Normalized 0..1 telegraph progress computed from real timestamps.</summary>
    public float TelegraphProgress => State == EnemyState.Telegraph
        ? Mathf.Clamp((Now - _telegraphStartedAt) / Mathf.Max(0.001f, CurrentAttack.TelegraphSeconds), 0f, 1f)
        : 0f;

    /// <summary>Read-only presentation cues for visual/audio layers (never mutate combat state).</summary>
    public event Action<EnemyTelegraphCue>? TelegraphStarted;
    public event Action<EnemyStrikeCue>? StrikeActivated;
    public event Action<EnemyDamagedCue>? Damaged;

    public virtual bool IsInvulnerable => false;
    public virtual bool TryDefend(AttackResolution incoming) => false;

    private PlayerController _player = null!;
    private float _stateEndsAt;
    private float _telegraphStartedAt;
    private float _nextAttackAllowedAt;
    private bool _strikeResolved;
    private int _attackCycle;

    private float Now => Time.GetTicksMsec() / 1000f;

    public override void _Ready()
    {
        CollisionLayer = 1;
        CollisionMask = 2;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(52, 100) },
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
        hurtbox.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(52, 100) } });
        hurtbox.AddToGroup("enemy_hurtbox");
        AddChild(hurtbox);

        Visual = new EnemyVisual { Name = "Visual" };
        AddChild(Visual);
        Visual.Bind(this, _player);
    }

    public void Bind(PlayerController player, CombatWindowConfig windows)
    {
        _player = player;
        Windows = windows;
        Hp = MaxHp;
        _attackCycle = 0;
        _nextAttackAllowedAt = Now + Windows.EnemyInitialDelaySeconds;
        _stateEndsAt = Now + Windows.BossSpawnSeconds;
        Visual?.Bind(this, player);
        SetState(EnemyState.Spawn, "spawn");
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (!IsOnFloor()) Velocity = Velocity with { Y = Velocity.Y + Gravity * dt };

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

    private void ThinkIdleOrChase()
    {
        float distance = _player.GlobalPosition.DistanceTo(GlobalPosition);
        bool inRange = distance <= Windows.EnemyAttackRange &&
                       Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 90f;

        if (inRange && Now >= _nextAttackAllowedAt)
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

    private void BeginTelegraph()
    {
        CurrentAttackKind = (BossAttackKind)(_attackCycle % 3);
        _attackCycle++;
        _strikeResolved = false;
        float now = Now;
        _telegraphStartedAt = now;
        _stateEndsAt = now + CurrentAttack.TelegraphSeconds;
        SetState(EnemyState.Telegraph, $"{CurrentAttack.Name} telegraph: {CurrentAttack.ResponseHint}");
        TelegraphStarted?.Invoke(new EnemyTelegraphCue(CurrentAttack, GlobalPosition, now));
        GD.Print($"[Enemy] telegraph {CurrentAttack.Name} | response {CurrentAttack.ResponseHint} | " +
                 $"telegraph {CurrentAttack.TelegraphSeconds:0.00}s active {CurrentAttack.ActiveSeconds:0.00}s " +
                 $"recovery {CurrentAttack.RecoverySeconds:0.00}s damage {CurrentAttack.Damage:0}");
    }

    private void BeginStrike()
    {
        _stateEndsAt = Now + CurrentAttack.ActiveSeconds;
        SetState(EnemyState.Strike, $"{CurrentAttack.Name} active");
        StrikeActivated?.Invoke(new EnemyStrikeCue(CurrentAttack, GlobalPosition));
    }

    private void ResolveStrike()
    {
        _strikeResolved = true;
        bool inRange = CurrentAttack.Kind == BossAttackKind.DelayedArea
            ? _player.GlobalPosition.DistanceTo(GlobalPosition) <= CurrentAttack.HazardRadius
            : IsInAttackRange(CurrentAttack.Range);

        if (!inRange)
        {
            GD.Print($"[Enemy] {CurrentAttack.Name} whiffed (outside threat)");
            _nextAttackAllowedAt = Now + Windows.EnemyAttackCooldown;
            return;
        }

        bool answered = _player.TryReceiveEnemyAttack(CurrentAttack.Damage, CurrentAttack.Kind,
            out AttackResolution resolution);
        GD.Print(answered
            ? $"[Enemy] {CurrentAttack.Name} answered: {resolution.Result}"
            : $"[Enemy] {CurrentAttack.Name} landed: {resolution.Result} (player hp {_player.Hp:0})");
        if (resolution.EnemyStaggered)
            EnterHitstun("staggered by perfect block");

        _nextAttackAllowedAt = Now + Windows.EnemyAttackCooldown;
    }

    private bool IsInAttackRange(float range)
        => _player.GlobalPosition.DistanceTo(GlobalPosition) <= range &&
           Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 110f;

    public bool TakeDamage(float damage)
    {
        if (State == EnemyState.Spawn)
        {
            GD.Print("[Enemy] damage ignored during spawn");
            return false;
        }
        if (State is EnemyState.Dead or EnemyState.Victory) return false;
        if (IsInvulnerable) { GD.Print("[Enemy] damage ignored (invulnerable)"); return false; }

        Hp -= damage;
        if (Hp <= 0)
        {
            Hp = 0;
            _stateEndsAt = Now + Windows.VictoryDelaySeconds;
            SetState(EnemyState.Dead, "destroyed");
            Damaged?.Invoke(new EnemyDamagedCue(damage, Hp, MaxHp, Defeated: true, GlobalPosition));
            return true;
        }
        EnterHitstun($"took {damage:0} damage");
        Damaged?.Invoke(new EnemyDamagedCue(damage, Hp, MaxHp, Defeated: false, GlobalPosition));
        return true;
    }

    private void EnterHitstun(string reason)
    {
        _stateEndsAt = Now + Windows.HitstunSeconds;
        SetState(EnemyState.Hitstun, reason);
    }

    public void ResetTo(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Hp = MaxHp;
        _attackCycle = 0;
        _nextAttackAllowedAt = Now + Windows.EnemyInitialDelaySeconds;
        _stateEndsAt = Now + Windows.BossSpawnSeconds;
        _strikeResolved = false;
        SetState(EnemyState.Spawn, "round reset");
    }

    private BossAttackDefinition DefinitionFor(BossAttackKind kind)
        => kind switch
        {
            BossAttackKind.BreakGrab => new BossAttackDefinition(
                kind, "BREAK GRAB", "DODGE or K", Windows.BreakGrabDamage,
                Windows.BreakGrabTelegraphSeconds, Windows.BreakGrabActiveSeconds,
                Windows.BreakGrabRecoverySeconds, Windows.BreakGrabRange, 0f),
            BossAttackKind.DelayedArea => new BossAttackDefinition(
                kind, "DELAYED AREA", "MOVE or TIME SLOW", Windows.DelayedAreaDamage,
                Windows.DelayedAreaTelegraphSeconds, Windows.DelayedAreaActiveSeconds,
                Windows.DelayedAreaRecoverySeconds, 0f, Windows.DelayedAreaRadius),
            _ => new BossAttackDefinition(
                BossAttackKind.Normal, "NORMAL STRIKE", "BLOCK or DODGE", Windows.NormalAttackDamage,
                Windows.NormalAttackTelegraphSeconds, Windows.NormalAttackActiveSeconds,
                Windows.NormalAttackRecoverySeconds, Windows.NormalAttackRange, 0f)
        };

    private void SetState(EnemyState next, string reason)
    {
        if (State == next) return;
        GD.Print($"[Enemy] {State} -> {next} ({reason})");
        State = next;
    }
}
