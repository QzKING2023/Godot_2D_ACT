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
    Vector2 Position);

/// <summary>Read-only presentation cue emitted when an enemy begins an attack telegraph.</summary>
public readonly record struct BossTelegraphCue(
    BossAttackDefinition Attack,
    Vector2 Origin,
    float StartedAtRealSec);

/// <summary>Read-only presentation cue emitted when an enemy telegraph transitions into its active strike.</summary>
public readonly record struct BossStrikeCue(
    BossAttackDefinition Attack,
    Vector2 Origin);

/// <summary>Read-only presentation cue emitted when an enemy takes damage or is defeated.</summary>
public readonly record struct EnemyDamagedCue(
    float Damage,
    float RemainingHp,
    float MaxHp,
    bool Defeated,
    Vector2 Position);
