using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// Dedicated tutorial training pillar (演练靶标), decoupled from boss identity and combat AI.
/// Holds its ground as a symmetric geometric pillar and alternates golden (Normal) and
/// crimson (BreakGrab) telegraphs for tutorial defense/combat drills.
/// </summary>
public partial class TrainingTarget : EnemyController
{
    public const string DefaultDisplayName = "演练靶标";

    private int _attackCycle;

    public TrainingTarget()
    {
        Name = "TrainingTarget";
        DisplayName = DefaultDisplayName;
        IsBoss = false;
        ChaseSpeed = 0f;
    }

    public override bool UsesEncounterAttackToken => false;

    protected override EnemyVisual CreateVisual()
        => new TrainingTargetVisual { Name = "Visual" };

    protected override BossAttackDefinition SelectNextAttack()
    {
        bool useNormal = _attackCycle % 2 == 0;
        _attackCycle++;
        return useNormal
            ? BossAttackDefinition.CreateNormal(Windows)
            : BossAttackDefinition.CreateBreakGrab(Windows);
    }

    protected override void OnReset()
    {
        _attackCycle = 0;
    }

    protected override void ThinkIdleOrChase()
    {
        Velocity = Velocity with { X = 0f };
        PlayerController? player = TargetPlayer;
        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            SetState(EnemyState.Idle, "training pillar idle");
            return;
        }

        float engageRange = Mathf.Max(Windows.EnemyAttackRange, 195f);
        float distance = player.GlobalPosition.DistanceTo(GlobalPosition);
        bool inRange = distance <= engageRange &&
                       Mathf.Abs(player.GlobalPosition.Y - GlobalPosition.Y) < 110f;

        if (inRange && CanStartAttack && TryBeginTelegraph())
            return;

        SetState(EnemyState.Idle, "training pillar holding");
    }
}
