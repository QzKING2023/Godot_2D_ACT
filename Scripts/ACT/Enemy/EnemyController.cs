using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Player;

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
/// window. The procedural draw cues are intentionally distinct placeholders.
/// </summary>
public partial class EnemyController : CharacterBody2D
{
    [Export] public float MaxHp { get; set; } = 6f;
    [Export] public float ChaseSpeed { get; set; } = 90f;
    [Export] public float Gravity { get; set; } = 1200f;
    public float Hp { get; private set; }

    public CombatWindowConfig Windows { get; set; } = new();
    public EnemyState State { get; private set; } = EnemyState.Spawn;
    public EnemyState PreviousChaseState { get; private set; } = EnemyState.Idle;
    public BossAttackKind CurrentAttackKind { get; private set; } = BossAttackKind.Normal;
    public BossAttackDefinition CurrentAttack => DefinitionFor(CurrentAttackKind);
    public bool IsDefeated => State is EnemyState.Dead or EnemyState.Victory;

    public virtual bool IsInvulnerable => false;
    public virtual bool TryDefend(AttackResolution incoming) => false;

    private PlayerController _player = null!;
    private float _stateEndsAt;
    private float _nextAttackAllowedAt;
    private float _telegraphFlash;
    private float _attackFlashUntil;
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
    }

    public void Bind(PlayerController player, CombatWindowConfig windows)
    {
        _player = player;
        Windows = windows;
        Hp = MaxHp;
        _attackCycle = 0;
        _nextAttackAllowedAt = Now + Windows.EnemyInitialDelaySeconds;
        _stateEndsAt = Now + Windows.BossSpawnSeconds;
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
                _telegraphFlash = Mathf.PingPong(Now * 6f, 1f);
                Velocity = Velocity with { X = 0 };
                QueueRedraw();
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
        _telegraphFlash = 0f;
        _strikeResolved = false;
        _stateEndsAt = Now + CurrentAttack.TelegraphSeconds;
        SetState(EnemyState.Telegraph, $"{CurrentAttack.Name} telegraph: {CurrentAttack.ResponseHint}");
        GD.Print($"[Enemy] telegraph {CurrentAttack.Name} | response {CurrentAttack.ResponseHint} | " +
                 $"telegraph {CurrentAttack.TelegraphSeconds:0.00}s active {CurrentAttack.ActiveSeconds:0.00}s " +
                 $"recovery {CurrentAttack.RecoverySeconds:0.00}s damage {CurrentAttack.Damage:0}");
    }

    private void BeginStrike()
    {
        _telegraphFlash = 1f;
        _attackFlashUntil = Now + CurrentAttack.ActiveSeconds;
        _stateEndsAt = Now + CurrentAttack.ActiveSeconds;
        SetState(EnemyState.Strike, $"{CurrentAttack.Name} active");
        QueueRedraw();
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

    public void TakeDamage(float damage)
    {
        if (State == EnemyState.Spawn)
        {
            GD.Print("[Enemy] damage ignored during spawn");
            return;
        }
        if (State is EnemyState.Dead or EnemyState.Victory) return;
        if (IsInvulnerable) { GD.Print("[Enemy] damage ignored (invulnerable)"); return; }

        Hp -= damage;
        if (Hp <= 0)
        {
            Hp = 0;
            _stateEndsAt = Now + Windows.VictoryDelaySeconds;
            SetState(EnemyState.Dead, "destroyed");
            return;
        }
        EnterHitstun($"took {damage:0} damage");
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
        QueueRedraw();
    }

    public override void _Draw()
    {
        var attackColor = CurrentAttackKind switch
        {
            BossAttackKind.BreakGrab => new Color("ef476f"),
            BossAttackKind.DelayedArea => new Color("9d4edd"),
            _ => new Color("ffd166")
        };
        var body = State switch
        {
            EnemyState.Telegraph => new Color("ffd166").Lerp(attackColor, _telegraphFlash),
            EnemyState.Strike => attackColor,
            EnemyState.Hitstun => new Color("b5838d"),
            EnemyState.Dead => new Color("495057"),
            EnemyState.Victory => new Color("80ed99"),
            _ => new Color("ee6c4d")
        };
        DrawRect(new Rect2(-26, -50, 52, 100), body);

        if (State == EnemyState.Telegraph)
        {
            float pulse = 0.6f + 0.4f * _telegraphFlash;
            switch (CurrentAttackKind)
            {
                case BossAttackKind.BreakGrab:
                    DrawRect(new Rect2(-CurrentAttack.Range, -58, CurrentAttack.Range * 2f, 116),
                        new Color(0.94f, 0.28f, 0.44f, 0.15f * pulse), true);
                    DrawRect(new Rect2(-CurrentAttack.Range, -58, CurrentAttack.Range * 2f, 116),
                        new Color(1f, 0.25f, 0.4f, pulse), false, 5f);
                    break;
                case BossAttackKind.DelayedArea:
                    DrawCircle(Vector2.Zero, CurrentAttack.HazardRadius, new Color(0.62f, 0.3f, 0.87f, 0.13f * pulse));
                    DrawArc(Vector2.Zero, CurrentAttack.HazardRadius, 0, Mathf.Tau, 32,
                        new Color(0.75f, 0.45f, 1f, pulse), 5f);
                    break;
                default:
                    DrawArc(Vector2.Zero, 60, -Mathf.Pi / 2 - 0.9f, -Mathf.Pi / 2 + 0.9f, 16,
                        new Color(1, 0.85f, 0.3f, pulse), 6);
                    break;
            }
        }
        if (State == EnemyState.Strike || Now < _attackFlashUntil)
        {
            DrawRect(new Rect2(-34, -58, 68, 116), new Color(1f, 0.1f, 0.1f, 0.72f), false, 8f);
            if (CurrentAttackKind == BossAttackKind.DelayedArea)
                DrawCircle(Vector2.Zero, CurrentAttack.HazardRadius, new Color(0.7f, 0.2f, 0.9f, 0.22f));
        }
    }
}
