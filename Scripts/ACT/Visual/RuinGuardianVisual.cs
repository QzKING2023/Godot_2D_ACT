using ACTSpike.ACT.Enemy;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Procedural monolith renderer for「遗迹守卫」(Ruin Guardian), adding Phase 2
/// incandescent armor fissures and overload transition pulse rings over EnemyVisual.
/// </summary>
public partial class RuinGuardianVisual : EnemyVisual
{
    private static readonly Vector2[] CenterFissureOuter =
    {
        new(-5f, -38f), new(5f, -38f), new(9f, -6f), new(4f, 36f), new(-4f, 36f), new(-9f, -6f)
    };

    private static readonly Vector2[] CenterFissureCore =
    {
        new(-2.5f, -34f), new(2.5f, -34f), new(4.5f, -6f), new(1.8f, 32f), new(-1.8f, 32f), new(-4.5f, -6f)
    };

    private static readonly Vector2[] LeftFissure =
    {
        new(-26f, -14f), new(-11f, -4f), new(-15f, 4f), new(-28f, -5f)
    };

    private static readonly Vector2[] RightFissure =
    {
        new(11f, -4f), new(26f, -14f), new(28f, -5f), new(15f, 4f)
    };

    public bool IsOverloadVisualActive => BoundEnemy is RuinGuardianBoss { IsPhaseTwo: true, IsDefeated: false };

    protected override void DrawBody()
    {
        base.DrawBody();

        if (BoundEnemy is not RuinGuardianBoss boss || !boss.IsPhaseTwo || boss.IsDefeated)
            return;

        float floatY = Mathf.Sin(RealElapsedSec * 3.8f) * 3.5f;
        float windup = boss.State == EnemyState.Telegraph
            ? -Facing * (8f + 14f * boss.TelegraphProgress)
            : (boss.State == EnemyState.Strike ? Facing * 22f : 0f);
        Vector2 origin = new(windup, floatY);

        float pulse = 0.78f + 0.22f * Mathf.Sin(RealElapsedSec * 12f);
        Color glow = IsHitFlashing
            ? Colors.White
            : RuinGeometryPalette.BossOverloadFissureGlow with { A = 0.92f * pulse };
        Color core = IsHitFlashing
            ? Colors.White
            : RuinGeometryPalette.BossOverloadFissureCore with { A = 0.98f * pulse };

        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(CenterFissureOuter, origin), glow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(CenterFissureCore, origin), core);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(LeftFissure, origin), glow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RightFissure, origin), glow);

        if (boss.IsOverloadTransitionActive)
        {
            float ringPulse = 0.5f + 0.5f * Mathf.Sin(RealElapsedSec * 24f);
            float radius = 64f + 46f * ringPulse;
            DrawArc(origin, radius, 0f, Mathf.Tau, 36,
                RuinGeometryPalette.BossOverloadPulseRing with { A = 0.90f }, 5f, true);
            DrawArc(origin, radius * 0.72f, 0f, Mathf.Tau, 28,
                RuinGeometryPalette.BossOverloadFissureCore with { A = 0.85f }, 3f, true);
        }
    }
}
