using ACTSpike.ACT.Combat;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// Monolith sub-boss「遗迹守卫」(Ruin Guardian) decoupled from EnemyController's generic
/// survival/hit base. Owns the 3-move boss kit: Normal Strike, Break Grab, and Delayed Area.
/// </summary>
public partial class RuinGuardianBoss : EnemyController
{
    public const string DefaultDisplayName = "遗迹守卫 · 巨构核心";

    private int _attackCycle;

    public RuinGuardianBoss()
    {
        Name = "Boss";
        DisplayName = DefaultDisplayName;
        IsBoss = true;
        MaxHp = 6f;
    }

    public BossAttackDefinition DefinitionFor(BossAttackKind kind)
        => kind switch
        {
            BossAttackKind.BreakGrab => BossAttackDefinition.CreateBreakGrab(Windows),
            BossAttackKind.DelayedArea => BossAttackDefinition.CreateDelayedArea(Windows),
            _ => BossAttackDefinition.CreateNormal(Windows)
        };

    protected override BossAttackDefinition SelectNextAttack()
    {
        var kind = (BossAttackKind)(_attackCycle % 3);
        _attackCycle++;
        return DefinitionFor(kind);
    }

    protected override bool IsTargetInStrikeThreat()
    {
        if (CurrentAttack.Kind == BossAttackKind.DelayedArea)
            return TargetPlayer is not null &&
                   GodotObject.IsInstanceValid(TargetPlayer) &&
                   TargetPlayer.GlobalPosition.DistanceTo(GlobalPosition) <= CurrentAttack.HazardRadius;

        return base.IsTargetInStrikeThreat();
    }

    protected override void OnReset()
    {
        _attackCycle = 0;
    }
}
