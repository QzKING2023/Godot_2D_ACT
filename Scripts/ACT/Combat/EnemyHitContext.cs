using Godot;

namespace ACTSpike.ACT.Combat;

/// <summary>Outcome of resolving an incoming directional hit against an enemy.</summary>
public enum EnemyHitOutcome
{
    Ignored,
    Damaged,
    Deflected
}

/// <summary>
/// Immutable directional hit context passed when player attacks, directional skills,
/// or reflected projectiles strike an enemy.
/// </summary>
public readonly record struct EnemyHitContext(
    float Damage,
    Vector2 AttackerOrigin,
    Vector2 MotionDirection,
    bool IsDirectionalSkill = false,
    bool CrossedTargetCenterline = false)
{
    public const float CenterlineEpsilon = 0.01f;

    /// <summary>Returns true when <paramref name="pointX"/> lies strictly behind a target facing <paramref name="targetFacing"/>.</summary>
    public static bool IsOnBackSide(float pointX, float targetX, int targetFacing)
    {
        int facing = targetFacing >= 0 ? 1 : -1;
        float horizontalOffset = pointX - targetX;
        return Mathf.Abs(horizontalOffset) > CenterlineEpsilon && horizontalOffset * facing < 0f;
    }

    /// <summary>
    /// Returns true when a directional skill trajectory from <paramref name="startX"/> to <paramref name="currentX"/>
    /// crosses <paramref name="targetX"/> horizontally (independent of mid-dash target facing flips),
    /// or departs from exact center onto the target's back side.
    /// </summary>
    public static bool HasCrossedHorizontalCenterline(float startX, float currentX, float targetX, int targetFacing)
    {
        float startOffset = startX - targetX;
        float currentOffset = currentX - targetX;

        if ((startOffset < -CenterlineEpsilon && currentOffset > CenterlineEpsilon) ||
            (startOffset > CenterlineEpsilon && currentOffset < -CenterlineEpsilon))
        {
            return true;
        }

        if (Mathf.Abs(startOffset) <= CenterlineEpsilon)
            return IsOnBackSide(currentX, targetX, targetFacing);

        return false;
    }

    /// <summary>
    /// Returns true when the strike impacts the target's frontal defensive arc.
    /// Directional skills that crossed the target's horizontal centerline or entered
    /// from behind bypass frontal defense.
    /// </summary>
    public bool IsFrontalStrike(Vector2 targetPosition, int targetFacing)
    {
        if (IsDirectionalSkill && CrossedTargetCenterline)
            return false;

        int facing = targetFacing >= 0 ? 1 : -1;
        float horizontalOffset = AttackerOrigin.X - targetPosition.X;
        if (Mathf.Abs(horizontalOffset) > CenterlineEpsilon)
            return horizontalOffset * facing > 0f;

        if (Mathf.Abs(MotionDirection.X) > CenterlineEpsilon)
            return MotionDirection.X * facing < 0f;

        return true;
    }
}
