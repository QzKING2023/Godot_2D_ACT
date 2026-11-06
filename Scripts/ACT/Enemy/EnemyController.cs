using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Enemy;

public enum EnemyState { Idle, Chase, Telegraph, Strike, Recover, Hitstun, Dead }

/// <summary>
/// Minimal enemy with a readable attack flow: Chase -> Telegraph (warning)
/// -> Strike (one judgement through the player's defense) -> Recover.
/// Reserved for later: own block/dodge reactions (virtual stubs below) and
/// invulnerability windows. Time slow affects it via Engine.TimeScale.
/// </summary>
public partial class EnemyController : CharacterBody2D
{
    [Export] public float MaxHp { get; set; } = 6f;
    [Export] public float AttackDamage { get; set; } = 15f;
    [Export] public float ChaseSpeed { get; set; } = 90f;
    [Export] public float Gravity { get; set; } = 1200f;
    public float Hp { get; private set; }

    public CombatWindowConfig Windows { get; set; } = new();
    public EnemyState State { get; private set; } = EnemyState.Idle;
    public EnemyState PreviousChaseState { get; private set; } = EnemyState.Idle;

    /// <summary>Reserved interface: future enemies can turn i-frames on.</summary>
    public virtual bool IsInvulnerable => false;
    /// <summary>Reserved interface: future enemy defensive reactions.</summary>
    public virtual bool TryDefend(AttackResolution incoming) => false;

    private PlayerController _player = null!;
    private float _stateEndsAt;
    private float _nextAttackAllowedAt;
    private float _telegraphFlash;

    public override void _Ready()
    {
        CollisionLayer = 1;
        CollisionMask = 2;
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(52, 100) },
            Name = "BodyShape"
        };
        AddChild(shape);

        var hurtbox = new Area2D
        {
            Name = "Hurtbox",
            CollisionLayer = 4,   // player attack hitboxes look for this
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
        _nextAttackAllowedAt = Now + 1.5f; // grace period before first telegraph
        SetState(EnemyState.Idle, "spawn");
    }

    private float Now => Time.GetTicksMsec() / 1000f;

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (!IsOnFloor()) Velocity = Velocity with { Y = Velocity.Y + Gravity * dt };

        switch (State)
        {
            case EnemyState.Dead:
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
                ResolveStrike();
                break;
            case EnemyState.Recover:
                Velocity = Velocity with { X = 0 };
                if (Now >= _stateEndsAt) SetState(EnemyState.Idle, "recovered");
                break;
            default:
                ThinkIdleOrChase(dt);
                break;
        }
        MoveAndSlide();
    }

    private void ThinkIdleOrChase(float dt)
    {
        float distance = _player.GlobalPosition.DistanceTo(GlobalPosition);
        bool inRange = distance <= Windows.EnemyAttackRange &&
                       Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 90f;

        if (inRange && Now >= _nextAttackAllowedAt)
        {
            _stateEndsAt = Now + Windows.EnemyTelegraphSeconds;
            SetState(EnemyState.Telegraph, "attack warning");
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

    private void BeginStrike()
    {
        _telegraphFlash = 1f;
        SetState(EnemyState.Strike, "striking");
    }

    private void ResolveStrike()
    {
        float distance = _player.GlobalPosition.DistanceTo(GlobalPosition);
        bool inRange = distance <= Windows.EnemyAttackRange * 1.15f &&
                       Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) < 110f;

        if (inRange)
        {
            bool cleanHit = !_player.TryReceiveEnemyAttack(AttackDamage, out AttackResolution resolution);
            GD.Print(cleanHit
                ? $"[Enemy] strike landed: {resolution.Result} (player hp {_player.Hp:0})"
                : $"[Enemy] strike answered: {resolution.Result}");
            if (resolution.EnemyStaggered)
                EnterHitstun("staggered by perfect block");
        }
        else
        {
            GD.Print("[Enemy] strike whiffed (player out of range)");
        }

        _nextAttackAllowedAt = Now + Windows.EnemyAttackCooldown;
        _stateEndsAt = Now + 0.4f; // short recover pose
        SetState(EnemyState.Recover, "recovering");
    }

    public void TakeDamage(float damage)
    {
        if (State == EnemyState.Dead) return;
        if (IsInvulnerable) { GD.Print("[Enemy] damage ignored (invulnerable)"); return; }

        Hp -= damage;
        if (Hp <= 0)
        {
            Hp = 0;
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
        _nextAttackAllowedAt = Now + 1.5f;
        SetState(EnemyState.Idle, "round reset");
    }

    private void SetState(EnemyState next, string reason)
    {
        if (State == next) return;
        GD.Print($"[Enemy] {State} -> {next} ({reason})");
        State = next;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var body = State switch
        {
            EnemyState.Telegraph => new Color("ffd166").Lerp(new Color("ef476f"), _telegraphFlash),
            EnemyState.Strike => new Color("ef476f"),
            EnemyState.Hitstun => new Color("b5838d"),
            EnemyState.Dead => new Color("495057"),
            _ => new Color("ee6c4d")
        };
        DrawRect(new Rect2(-26, -50, 52, 100), body);

        if (State == EnemyState.Telegraph)
        {
            // Warning arc: readable pre-attack signal (placeholder art later).
            DrawArc(Vector2.Zero, 60, -Mathf.Pi / 2 - 0.9f, -Mathf.Pi / 2 + 0.9f, 16,
                new Color(1, 0.85f, 0.3f, 0.6f + 0.4f * _telegraphFlash), 6);
        }
    }
}
