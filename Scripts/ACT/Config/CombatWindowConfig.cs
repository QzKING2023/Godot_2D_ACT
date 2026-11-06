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
    [Export] public float EnemyTelegraphSeconds { get; set; } = 0.70f;
    [Export] public float EnemyAttackCooldown { get; set; } = 2.2f;
    [Export] public float EnemyAttackRange { get; set; } = 170f;
    [Export] public float BlockChipRatio { get; set; } = 0.25f;
    [Export] public float GuardDamagePerBlockedHit { get; set; } = 25f;
}
