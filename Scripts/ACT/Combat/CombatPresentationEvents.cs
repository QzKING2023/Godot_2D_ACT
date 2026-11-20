using Godot;
using ACTSpike.ACT.Enemy;

namespace ACTSpike.ACT.Combat;

/// <summary>Read-only presentation cue emitted when the player starts an attack or directional skill.</summary>
public readonly record struct PlayerAttackCue(
    bool IsSkill,
    Vector2 Origin,
    Vector2 Direction,
    float DurationSeconds);

/// <summary>Read-only presentation cue emitted when a player attack or skill connects with an enemy.</summary>
public readonly record struct PlayerHitCue(
    bool IsSkill,
    float Damage,
    Vector2 HitPosition);

/// <summary>Read-only presentation cue emitted when the player starts a dodge dash.</summary>
public readonly record struct PlayerDodgeCue(
    Vector2 Origin,
    int Direction,
    bool StartedInAir,
    float DurationSeconds);

/// <summary>Read-only presentation cue emitted after an enemy strike is resolved against the player.</summary>
public readonly record struct PlayerDefenseCue(
    DefenseResult Result,
    BossAttackKind AttackKind,
    float IncomingDamage,
    float DamageToHp,
    float DamageToGuard,
    bool GuardBroken,
    bool IsDown,
    Vector2 Position,
    AttackDeliveryKind DeliveryKind = AttackDeliveryKind.Melee,
    bool ProjectileReflected = false);

/// <summary>Read-only presentation cue emitted when an enemy begins an attack telegraph.</summary>
public readonly record struct EnemyTelegraphCue(
    BossAttackDefinition Attack,
    Vector2 Origin,
    float StartedAtRealSec);

/// <summary>Read-only presentation cue emitted when an enemy telegraph transitions into its active strike.</summary>
public readonly record struct EnemyStrikeCue(
    BossAttackDefinition Attack,
    Vector2 Origin);

/// <summary>Read-only presentation cue emitted when an enemy takes damage or is defeated.</summary>
public readonly record struct EnemyDamagedCue(
    float Damage,
    float RemainingHp,
    float MaxHp,
    bool Defeated,
    Vector2 Position);

/// <summary>Read-only presentation cue emitted when an enemy's frontal shield deflects an incoming strike.</summary>
public readonly record struct EnemyShieldDeflectedCue(
    EnemyHitContext Hit,
    Vector2 Position,
    int DefenderFacing);

/// <summary>Read-only presentation cue emitted when a ranged enemy fires a projectile.</summary>
public readonly record struct EnemyProjectileFiredCue(
    Vector2 Origin,
    Vector2 Direction,
    float Damage);

/// <summary>Read-only presentation cue emitted when a boss triggers its Phase 2 overload pulse.</summary>
public readonly record struct EnemyOverloadPulseCue(
    Vector2 Origin,
    int PhaseNumber);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when any registered enemy begins a telegraph.</summary>
public readonly record struct RosterEnemyTelegraphCue(
    EnemyController Enemy,
    EnemyTelegraphCue Cue);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when any registered enemy enters its active strike.</summary>
public readonly record struct RosterEnemyStrikeCue(
    EnemyController Enemy,
    EnemyStrikeCue Cue);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when any registered enemy takes damage or is defeated.</summary>
public readonly record struct RosterEnemyDamagedCue(
    EnemyController Enemy,
    EnemyDamagedCue Cue);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when an enemy deflects a strike with a frontal shield.</summary>
public readonly record struct RosterEnemyShieldDeflectedCue(
    EnemyController Enemy,
    EnemyShieldDeflectedCue Cue);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when an enemy launches a ranged projectile.</summary>
public readonly record struct RosterEnemyProjectileFiredCue(
    EnemyController Enemy,
    EnemyProjectileFiredCue Cue);

/// <summary>Read-only presentation cue forwarded by StageEnemyRoster when a boss triggers its overload pulse.</summary>
public readonly record struct RosterEnemyOverloadPulseCue(
    EnemyController Enemy,
    EnemyOverloadPulseCue Cue);
