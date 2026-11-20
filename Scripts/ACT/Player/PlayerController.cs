using System;
using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Visual;

namespace ACTSpike.ACT.Player;

/// <summary>
/// Controllable player: movement, jump, attack, dodge, block, skill dash and hitstun.
/// Physics bodies/shapes are placeholder geometry (ColorRect + simple shapes);
/// swap the "Visual" child and shape extents later for real art/collision.
/// Input edges are latched in _Process and consumed in _PhysicsProcess so they
/// survive Engine.TimeScale changes.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
    // --- balance (exported for tuning; windows live in CombatWindowConfig) ---
    [Export] public float MoveSpeed { get; set; } = 300f;
    [Export] public float Acceleration { get; set; } = 1800f;
    [Export] public float JumpVelocity { get; set; } = -520f;
    [Export] public float Gravity { get; set; } = 1200f;
    [Export] public float DodgeSpeed { get; set; } = 700f;
    [Export] public float MaxHp { get; set; } = 100f;
    [Export] public float MaxGuard { get; set; } = 100f;
    [Export] public float GuardRegenPerSecond { get; set; } = 10f;
    [Export] public float AttackDamage { get; set; } = 1f;
    [Export] public float SkillDamage { get; set; } = 2f;

    public CombatWindowConfig Windows { get; set; } = new();
    public TimeSlowConfig TimeSlow { get; set; } = new();
    public PlayerState State { get; private set; } = PlayerState.Idle;
    public float Hp { get; private set; }
    public float Guard { get; private set; }
    public bool IsDown { get; private set; }
    public int Facing { get; private set; } = 1;
    public PlayerVisual Visual { get; private set; } = null!;

    /// <summary>Short human-readable combat feedback line (HUD + debug log).</summary>
    public event Action<string>? CombatFeedback;
    /// <summary>Perfect dodge refunds time-slow resource; arena wires this to the manager.</summary>
    public event Action<float>? TimeResourceRefund;
    /// <summary>Read-only presentation cues for visual/audio layers (never mutate combat state).</summary>
    public event Action<PlayerAttackCue>? AttackStarted;
    public event Action<PlayerHitCue>? AttackLanded;
    public event Action<PlayerDodgeCue>? DodgeStarted;
    public event Action<PlayerDefenseCue>? DefenseResolved;
    public event Action<Vector2>? Landed;

    private Area2D _attackHitbox = null!;
    private Node2D _skillAimIndicator = null!;
    private readonly HashSet<EnemyController> _swingHit = new();

    // Input latches (edges captured in _Process, consumed in _PhysicsProcess).
    private bool _jumpQueued, _attackQueued, _dodgeQueued, _skillQueued;
    private bool _skillCharging;
    private Vector2 _skillAim;
    private bool _dodgeStartedInAir;
    private int _dodgeDirection;
    private Vector2 _moveAxis;

    // Real-timestamp bookkeeping for perfect windows and state timers.
    private float Now => Time.GetTicksMsec() / 1000f;
    private float _stateEndsAt;
    private float _dodgeStartedAt = -10f;
    private float _blockHeldSince = -10f;
    private float _invulnerableUntil = -10f;
    private ulong _lastReportedEventMsec;
    private bool _blockWasHeld;
    private bool _wasOnFloor = true;

    public override void _Ready()
    {
        Hp = MaxHp;
        Guard = MaxGuard;
        CollisionLayer = 1;   // actors
        CollisionMask = 2;    // world geometry only (no body-vs-body with enemy)

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(30, 46) },
            Name = "BodyShape"
        };
        AddChild(shape);

        Visual = new PlayerVisual { Name = "Visual" };
        AddChild(Visual);
        Visual.Bind(this);

        // Programmatic aim cue for the release-to-cast Y skill.
        _skillAimIndicator = new Node2D { Name = "SkillAimIndicator", ZIndex = 5, Visible = false };
        _skillAimIndicator.AddChild(new Line2D
        {
            Width = 3f,
            DefaultColor = new Color("ffd166"),
            Points = new[] { Vector2.Zero, new Vector2(48, 0) }
        });
        _skillAimIndicator.AddChild(new Polygon2D
        {
            Polygon = new[] { new Vector2(60, 0), new Vector2(45, -8), new Vector2(45, 8) },
            Color = new Color("ffd166")
        });
        AddChild(_skillAimIndicator);

        _attackHitbox = new Area2D
        {
            Name = "AttackHitbox", // future: swap for animated frame data
            CollisionLayer = 0,
            CollisionMask = 4,    // enemy hurtboxes
            Monitoring = false,
            Monitorable = false
        };
        var hitShape = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(90, 70) } };
        _attackHitbox.AddChild(hitShape);
        _attackHitbox.AddToGroup("player_attack");
        AddChild(_attackHitbox);

        SetState(PlayerState.Idle, "ready");
    }

    public override void _Process(double delta)
    {
        if (ActInput.JumpPressed()) _jumpQueued = true;
        if (ActInput.AttackPressed()) _attackQueued = true;
        if (ActInput.DodgePressed()) _dodgeQueued = true;
        _moveAxis = ActInput.MoveAxis();

        // Y is a release-to-cast skill: hold it to aim, then release to launch.
        if (ActInput.SkillPressed()) _skillCharging = true;
        if (_skillCharging)
        {
            _skillAim = _moveAxis;
            UpdateSkillAimIndicator();
            if (!ActInput.SkillHeld())
            {
                _skillQueued = true;
                _skillCharging = false;
                _skillAimIndicator.Visible = false;
            }
        }
        else
        {
            _skillAimIndicator.Visible = false;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var now = Now;
        ConsumeLatches(now, dt);
        ApplyGravityAndMove(dt);
        TickTimers(now, dt);
        ApplyHitboxPolling(now);
    }

    // --- input handling / state transitions ---

    private void ConsumeLatches(float now, float dt)
    {
        if (IsDown)
        {
            _jumpQueued = _attackQueued = _dodgeQueued = _skillQueued = false;
            _skillCharging = false;
            _skillAimIndicator.Visible = false;
            return;
        }

        if (_moveAxis.X != 0) Facing = _moveAxis.X > 0 ? 1 : -1;

        bool blockHeld = ActInput.BlockHeld() && IsOnFloor();

        // Priority while locked: hitstun/attack/dodge/skill finish before new actions.
        bool locked = State is PlayerState.Hitstun or PlayerState.Attack
            or PlayerState.Dodge or PlayerState.Skill;

        if (locked || now < _stateEndsAt)
        {
            if (locked && now >= _stateEndsAt)
            {
                if (State == PlayerState.Dodge)
                    Velocity = _dodgeStartedInAir
                        ? new Vector2(_dodgeDirection * MoveSpeed, 0f)
                        : Vector2.Zero;
                else if (State == PlayerState.Skill)
                    Velocity = Vector2.Zero;
                SetState(IsOnFloor() ? PlayerState.Idle : PlayerState.Jump, "action window ended");
            }
        }
        else if (_skillQueued)
        {
            BeginSkill(now);
        }
        else if (_dodgeQueued)
        {
            BeginDodge(now);
        }
        else if (_attackQueued)
        {
            BeginAttack(now);
        }
        else if (_jumpQueued && IsOnFloor())
        {
            Velocity = Velocity with { Y = JumpVelocity };
            SetState(PlayerState.Jump, "jump");
        }

        // Block is a hold state; track when the hold began (perfect-window anchor).
        if (blockHeld && !locked && State is PlayerState.Idle or PlayerState.Move)
        {
            if (!_blockWasHeld) _blockHeldSince = now;
            SetState(PlayerState.Block, "block held");
        }
        else if (State == PlayerState.Block && (!blockHeld || now < _stateEndsAt))
        {
            SetState(IsOnFloor() ? PlayerState.Idle : PlayerState.Jump, "block released");
        }
        _blockWasHeld = blockHeld;

        _jumpQueued = _attackQueued = _dodgeQueued = _skillQueued = false;
    }

    private void BeginAttack(float now)
    {
        _stateEndsAt = now + Windows.AttackActiveSeconds;
        _swingHit.Clear();
        _attackHitbox.Position = new Vector2(Facing * 55f, 0);
        _attackHitbox.Monitoring = true;
        SetState(PlayerState.Attack, "attack");
        AttackStarted?.Invoke(new PlayerAttackCue(
            IsSkill: false,
            Origin: GlobalPosition,
            Direction: new Vector2(Facing, 0f),
            DurationSeconds: Windows.AttackActiveSeconds));
    }

    private void BeginDodge(float now)
    {
        _stateEndsAt = now + Windows.DodgeSeconds;
        _dodgeStartedAt = now;
        _invulnerableUntil = now + Windows.DodgeSeconds;
        _dodgeStartedInAir = !IsOnFloor();
        // Ground dodge is forward-only. Air dodge may reverse direction, but only
        // through the front/back axis relative to the current facing.
        int dir = Facing;
        if (!IsOnFloor() && _moveAxis.X != 0 && Mathf.Sign(_moveAxis.X) != Facing)
            dir = -Facing;
        Facing = dir;
        _dodgeDirection = dir;
        Velocity = new Vector2(dir * DodgeSpeed, 0f);
        SetState(PlayerState.Dodge, "dodge");
        DodgeStarted?.Invoke(new PlayerDodgeCue(
            Origin: GlobalPosition,
            Direction: dir,
            StartedInAir: _dodgeStartedInAir,
            DurationSeconds: Windows.DodgeSeconds));
    }

    private void BeginSkill(float now)
    {
        _stateEndsAt = now + Windows.SkillSeconds;
        _invulnerableUntil = now + Windows.SkillSeconds;
        Vector2 dir = _skillAim.LengthSquared() > 0.15f
            ? _skillAim.Normalized()
            : new Vector2(Facing, 0f);
        if (Mathf.Abs(dir.X) > 0.01f)
            Facing = dir.X > 0 ? 1 : -1;
        // Y skill follows the full aim vector: keyboard gives 8 directions,
        // while an analog stick preserves its continuous 360-degree angle.
        // Gravity remains suspended for the duration of the skill.
        Velocity = dir * Windows.SkillSpeed;
        _swingHit.Clear();
        _attackHitbox.Position = Vector2.Zero;
        _attackHitbox.Monitoring = true;
        SetState(PlayerState.Skill, $"skill dash ({dir.X:0.00},{dir.Y:0.00})");
        AttackStarted?.Invoke(new PlayerAttackCue(
            IsSkill: true,
            Origin: GlobalPosition,
            Direction: dir,
            DurationSeconds: Windows.SkillSeconds));
    }

    private void UpdateSkillAimIndicator()
    {
        Vector2 direction = _skillAim.LengthSquared() > 0.15f
            ? _skillAim.Normalized()
            : new Vector2(Facing, 0f);
        _skillAimIndicator.Rotation = direction.Angle();
        _skillAimIndicator.Visible = true;
    }

    private void ApplyGravityAndMove(float dt)
    {
        // While Y is held for aiming, freeze controllable movement in place.
        // Direction sampling and the aim indicator continue in _Process.
        if (_skillCharging && IsOnFloor()
            && State is (PlayerState.Idle or PlayerState.Move or PlayerState.Block))
        {
            Velocity = Vector2.Zero;
            return;
        }

        if (State == PlayerState.Dodge)
            Velocity = Velocity with { Y = 0f };
        else if (State != PlayerState.Skill && !IsOnFloor())
            Velocity = Velocity with { Y = Velocity.Y + Gravity * dt };

        switch (State)
        {
            case PlayerState.Dodge:
            case PlayerState.Skill:
                break; // velocity is driven by the action
            case PlayerState.Attack:
            case PlayerState.Hitstun:
            case PlayerState.Block:
                Velocity = Velocity with { X = Mathf.MoveToward(Velocity.X, 0, Acceleration * dt) };
                break;
            default:
                // Ground movement is direct. In the air, preserve the launch
                // vector until a dodge or skill explicitly changes it.
                if (IsOnFloor())
                    Velocity = Velocity with { X = _moveAxis.X * MoveSpeed };
                if (State == PlayerState.Jump && IsOnFloor())
                    SetState(Mathf.Abs(Velocity.X) > 10f ? PlayerState.Move : PlayerState.Idle, "landed");
                else if (State == PlayerState.Idle && Mathf.Abs(Velocity.X) > 10f)
                    SetState(PlayerState.Move, "moving");
                else if (State == PlayerState.Move && Mathf.Abs(Velocity.X) <= 10f && _moveAxis.X == 0)
                    SetState(PlayerState.Idle, "stopped");
                break;
        }
        MoveAndSlide();
        bool onFloor = IsOnFloor();
        if (!_wasOnFloor && onFloor)
            Landed?.Invoke(GlobalPosition);
        _wasOnFloor = onFloor;
    }

    private void TickTimers(float now, float dt)
    {
        if (now >= _stateEndsAt && _attackHitbox.Monitoring &&
            State is PlayerState.Attack or PlayerState.Skill)
        {
            _attackHitbox.Monitoring = false; // active window over
        }
        if (State != PlayerState.Block)
            Guard = Mathf.Min(MaxGuard, Guard + GuardRegenPerSecond * dt);
    }

    private void ApplyHitboxPolling(float now)
    {
        if (!_attackHitbox.Monitoring) return;
        foreach (var area in _attackHitbox.GetOverlappingAreas())
        {
            if (!area.IsInGroup("enemy_hurtbox")) continue;
            if (area.GetParent() is not EnemyController enemy || _swingHit.Contains(enemy)) continue;
            _swingHit.Add(enemy);
            float dmg = State == PlayerState.Skill ? SkillDamage : AttackDamage;
            bool isSkill = State == PlayerState.Skill;
            bool applied = enemy.TakeDamage(dmg);
            if (applied)
                AttackLanded?.Invoke(new PlayerHitCue(isSkill, dmg, enemy.GlobalPosition));
            EmitFeedback($"{(isSkill ? "Skill" : "Attack")} hit enemy (-{dmg:0})");
        }
    }

    // --- combat intake: enemy strikes come through here ---

    /// <summary>Snapshot for CombatResolver; timestamps in real seconds.</summary>
    public DefenseSnapshot BuildDefenseSnapshot()
    {
        var now = Now;
        return new DefenseSnapshot(
            IsDodging: State == PlayerState.Dodge,
            DodgeStartedAtSec: _dodgeStartedAt,
            IsBlocking: State == PlayerState.Block,
            BlockHeldSinceSec: _blockHeldSince,
            IsInvulnerable: now < _invulnerableUntil && State == PlayerState.Skill,
            NowSec: now);
    }

    public bool TryReceiveEnemyAttack(float incomingDamage, out AttackResolution resolution)
        => TryReceiveEnemyAttack(incomingDamage, BossAttackKind.Normal, out resolution);

    public bool TryReceiveEnemyAttack(float incomingDamage, BossAttackKind attackKind, out AttackResolution resolution)
    {
        var snapshot = BuildDefenseSnapshot();
        resolution = CombatResolver.Resolve(snapshot, incomingDamage, Windows, TimeSlow, attackKind);
        ApplyResolution(incomingDamage, attackKind, resolution);
        return resolution.Result != DefenseResult.Hit; // false = clean hit landed
    }

    private void ApplyResolution(float incomingDamage, BossAttackKind attackKind, in AttackResolution resolution)
    {
        bool guardBroken = false;
        switch (resolution.Result)
        {
            case DefenseResult.PerfectDodged:
                TimeResourceRefund?.Invoke(resolution.TimeResourceRefund);
                EmitFeedback($"PERFECT DODGE (+{resolution.TimeResourceRefund:0} time)");
                break;
            case DefenseResult.Dodged:
                EmitFeedback("dodged");
                break;
            case DefenseResult.Invulnerable:
                EmitFeedback("skill i-frames");
                break;
            case DefenseResult.PerfectBlocked:
                EmitFeedback("PERFECT BLOCK (enemy staggered)");
                break;
            case DefenseResult.Blocked:
                Guard = Mathf.Max(0, Guard - resolution.DamageToGuard);
                Hp = Mathf.Max(0, Hp - resolution.DamageToHp);
                if (Guard <= 0)
                {
                    guardBroken = true;
                    EnterHitstun("GUARD BREAK");
                }
                else
                {
                    EmitFeedback($"blocked (chip -{resolution.DamageToHp:0})");
                }
                break;
            default:
                Hp = Mathf.Max(0, Hp - resolution.DamageToHp);
                EnterHitstun($"hit -{resolution.DamageToHp:0}");
                break;
        }

        DefenseResolved?.Invoke(new PlayerDefenseCue(
            Result: resolution.Result,
            AttackKind: attackKind,
            IncomingDamage: incomingDamage,
            DamageToHp: resolution.DamageToHp,
            DamageToGuard: resolution.DamageToGuard,
            GuardBroken: guardBroken,
            IsDown: IsDown,
            Position: GlobalPosition));
    }

    private void EnterHitstun(string reason)
    {
        _stateEndsAt = Now + Windows.HitstunSeconds;
        _attackHitbox.Monitoring = false;
        SetState(PlayerState.Hitstun, reason);
        if (Hp <= 0 && !IsDown)
        {
            IsDown = true;
            EmitFeedback("PLAYER DOWN - press R to reset");
        }
    }

    public void ResetTo(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Hp = MaxHp; Guard = MaxGuard;
        IsDown = false;
        _attackHitbox.Monitoring = false;
        _invulnerableUntil = -10f;
        _wasOnFloor = true;
        SetState(PlayerState.Idle, "round reset");
    }

    public void RestoreHp(float hp)
    {
        Hp = Mathf.Clamp(hp, 1f, MaxHp);
        Guard = MaxGuard;
        IsDown = false;
        SetState(PlayerState.Idle, "scene transition");
    }

    private void SetState(PlayerState next, string reason)
    {
        if (State == next) return;
        GD.Print($"[Player] {State} -> {next} ({reason})");
        State = next;
    }

    private void EmitFeedback(string message)
    {
        // Rate-limit identical spam within 100ms only; combat events are sparse by design.
        ulong msec = Time.GetTicksMsec();
        if (msec - _lastReportedEventMsec < 100) return;
        _lastReportedEventMsec = msec;
        GD.Print($"[Combat] {message}");
        CombatFeedback?.Invoke(message);
    }
}
