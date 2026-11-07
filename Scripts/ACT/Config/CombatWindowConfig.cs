using Godot;

namespace ACTSpike.ACT.Config;

[GlobalClass]
public partial class CombatWindowConfig : Resource
{
    [Export] public float PerfectGuardSeconds { get; set; } = 0.25f;
    [Export] public float AttackActiveSeconds { get; set; } = 0.18f;
    [Export] public float DodgeSeconds { get; set; } = 0.25f;
    [Export] public float PerfectDodgeSeconds { get; set; } = 0.25f;
    [Export] public float SkillSeconds { get; set; } = 0.30f;
    [Export] public float SkillSpeed { get; set; } = 850f;
    [Export] public float HitstunSeconds { get; set; } = 0.30f;
    [Export] public float BossSpawnSeconds { get; set; } = 0.25f;
    [Export] public float EnemyTelegraphSeconds { get; set; } = 0.70f;
    [Export] public float EnemyRecoverSeconds { get; set; } = 0.40f;
    [Export] public float EnemyAttackCooldown { get; set; } = 2.2f;
    [Export] public float EnemyInitialDelaySeconds { get; set; } = 1.5f;
    [Export] public float VictoryDelaySeconds { get; set; } = 0.40f;
    [Export] public float EnemyAttackRange { get; set; } = 170f;
    [Export] public float BlockChipRatio { get; set; } = 0.25f;
    [Export] public float GuardDamagePerBlockedHit { get; set; } = 25f;

    // Boss attack contract. These values are kept in the Resource so timing and
    // damage tuning is reviewable in one place instead of scattered through the
    // arena or state machine code.
    [ExportGroup("Boss normal attack")]
    [Export] public float NormalAttackDamage { get; set; } = 15f;
    [Export] public float NormalAttackTelegraphSeconds { get; set; } = 0.70f;
    [Export] public float NormalAttackActiveSeconds { get; set; } = 0.18f;
    [Export] public float NormalAttackRecoverySeconds { get; set; } = 0.40f;
    [Export] public float NormalAttackRange { get; set; } = 195f;

    [ExportGroup("Boss break grab")]
    [Export] public float BreakGrabDamage { get; set; } = 30f;
    [Export] public float BreakGrabTelegraphSeconds { get; set; } = 0.95f;
    [Export] public float BreakGrabActiveSeconds { get; set; } = 0.22f;
    [Export] public float BreakGrabRecoverySeconds { get; set; } = 0.65f;
    [Export] public float BreakGrabRange { get; set; } = 215f;

    [ExportGroup("Boss delayed area")]
    [Export] public float DelayedAreaDamage { get; set; } = 22f;
    [Export] public float DelayedAreaTelegraphSeconds { get; set; } = 2.00f;
    [Export] public float DelayedAreaActiveSeconds { get; set; } = 0.28f;
    [Export] public float DelayedAreaRecoverySeconds { get; set; } = 0.75f;
    [Export] public float DelayedAreaRadius { get; set; } = 150f;
}
