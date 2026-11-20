using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// High-mobility close-quarters regular enemy (近战斥候).
/// Presses the player at high chase speed and requests a melee attack token to deliver
/// a golden-fan normal slash telegraph.
/// </summary>
public partial class MeleeScoutEnemy : EnemyController
{
    public const string DefaultDisplayName = "近战斥候";
    public const float DefaultMaxHp = 3f;
    public const float DefaultChaseSpeed = 175f;
    public const float DefaultSlashDamage = 12f;
    public const float DefaultSlashTelegraphSeconds = 0.55f;
    public const float DefaultSlashActiveSeconds = 0.16f;
    public const float DefaultSlashRecoverySeconds = 0.34f;
    public const float DefaultSlashRange = 135f;
    public const float DefaultEngageRange = 120f;
    public const float DefaultAttackCooldownSeconds = 1.35f;
    public const float DefaultStrikeLungeSpeed = 130f;
    public static readonly Vector2 DefaultBodySize = new(42f, 64f);

    [Export] public float SlashDamage { get; set; } = DefaultSlashDamage;
    [Export] public float SlashTelegraphSeconds { get; set; } = DefaultSlashTelegraphSeconds;
    [Export] public float SlashActiveSeconds { get; set; } = DefaultSlashActiveSeconds;
    [Export] public float SlashRecoverySeconds { get; set; } = DefaultSlashRecoverySeconds;
    [Export] public float SlashRange { get; set; } = DefaultSlashRange;
    [Export] public float EngageRange { get; set; } = DefaultEngageRange;
    [Export] public float AttackCooldownSeconds { get; set; } = DefaultAttackCooldownSeconds;
    [Export] public float StrikeLungeSpeed { get; set; } = DefaultStrikeLungeSpeed;

    public MeleeScoutEnemy()
    {
        Name = "MeleeScout";
        DisplayName = DefaultDisplayName;
        IsBoss = false;
        MaxHp = DefaultMaxHp;
        ChaseSpeed = DefaultChaseSpeed;
    }

    public override AttackTokenPool AttackTokenPoolKind => AttackTokenPool.Melee;
    public override bool UsesEncounterAttackToken => true;
    protected override Vector2 BodySize => DefaultBodySize;

    protected override EnemyVisual CreateVisual() => new MeleeScoutVisual { Name = "Visual" };

    protected override BossAttackDefinition SelectNextAttack()
        => new(
            BossAttackKind.Normal,
            "SCOUT SLASH",
            "BLOCK or DODGE",
            SlashDamage,
            SlashTelegraphSeconds,
            SlashActiveSeconds,
            SlashRecoverySeconds,
            SlashRange,
            0f);

    protected override void ThinkIdleOrChase()
    {
        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "no target");
            return;
        }

        Vector2 delta = player.GlobalPosition - GlobalPosition;
        float distance = delta.Length();
        bool inRange = distance <= EngageRange && Mathf.Abs(delta.Y) < 90f;

        if (inRange && CanStartAttack && TryBeginTelegraph())
            return;

        if (Mathf.Abs(delta.X) > EngageRange * 0.65f)
        {
            SetState(EnemyState.Chase, "chasing player");
            float dir = Mathf.Sign(delta.X);
            Velocity = Velocity with { X = dir * ChaseSpeed };
        }
        else
        {
            SetState(EnemyState.Idle, "holding close pressure");
            Velocity = Velocity with { X = 0f };
        }
    }

    protected override void BeginStrike()
    {
        base.BeginStrike();
        Velocity = Velocity with { X = Facing * StrikeLungeSpeed };
    }

    protected override bool IsTargetInStrikeThreat()
    {
        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
            return false;

        Vector2 delta = player.GlobalPosition - GlobalPosition;
        float forwardDot = delta.X * Facing;
        return delta.Length() <= CurrentAttack.Range &&
               Mathf.Abs(delta.Y) < 95f &&
               forwardDot >= -26f;
    }

    protected override void ResolveStrike()
    {
        base.ResolveStrike();
        SetNextAttackCooldown(AttackCooldownSeconds);
    }
}
