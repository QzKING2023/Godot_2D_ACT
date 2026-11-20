using ACTSpike.ACT.Enemy;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Symmetric geometric pillar (对称几何立柱) procedural visual for 演练靶标.
/// Distinct from 遗迹守卫's broad-shouldered monolith silhouette and renders a compact
/// overhead narrow health bar while alive.
/// </summary>
public partial class TrainingTargetVisual : EnemyVisual
{
    private static readonly Vector2[] PedestalLeftPoints =
    {
        new(-30f, 48f), new(-24f, 34f), new(0f, 34f), new(0f, 48f)
    };

    private static readonly Vector2[] PedestalRightPoints =
    {
        new(0f, 34f), new(24f, 34f), new(30f, 48f), new(0f, 48f)
    };

    private static readonly Vector2[] ShaftLeftPoints =
    {
        new(-18f, 34f), new(-15f, -34f), new(0f, -34f), new(0f, 34f)
    };

    private static readonly Vector2[] ShaftRightPoints =
    {
        new(0f, -34f), new(15f, -34f), new(18f, 34f), new(0f, 34f)
    };

    private static readonly Vector2[] CrownLeftPoints =
    {
        new(-24f, -34f), new(-18f, -48f), new(0f, -54f), new(0f, -34f)
    };

    private static readonly Vector2[] CrownRightPoints =
    {
        new(0f, -54f), new(18f, -48f), new(24f, -34f), new(0f, -34f)
    };

    private static readonly Vector2[] LeftFinPoints =
    {
        new(-28f, -20f), new(-22f, -32f), new(-18f, -24f), new(-22f, -10f)
    };

    private static readonly Vector2[] RightFinPoints =
    {
        new(18f, -24f), new(22f, -32f), new(28f, -20f), new(22f, -10f)
    };

    protected override float OverheadHealthBarOffsetY => -70f;

    protected override void DrawBody()
    {
        EnemyController? e = BoundEnemy;
        if (e is null) return;

        bool defeated = e.IsDefeated;
        bool flash = IsHitFlashing;
        float settleY = defeated ? 12f : 0f;
        float recoilX = e.State == EnemyState.Hitstun ? -Facing * 4f : 0f;
        Vector2 origin = new Vector2(recoilX, settleY);

        Color shadow = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedShadow : RuinGeometryPalette.TrainingPillarShadow;
        Color lit = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.TrainingPillarLit;
        Color trim = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.TrainingPillarTrim;

        // Stepped symmetric pedestal base
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(PedestalLeftPoints, origin), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(PedestalRightPoints, origin), lit);
        DrawRect(new Rect2(origin.X - 24f, origin.Y + 32f, 48f, 3f), trim);

        // Symmetric vertical pillar shaft
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(ShaftLeftPoints, origin), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(ShaftRightPoints, origin), lit);

        // Horizontal weathered bronze collar bands
        DrawRect(new Rect2(origin.X - 17f, origin.Y + 14f, 34f, 3f), trim);
        DrawRect(new Rect2(origin.X - 16f, origin.Y - 22f, 32f, 3f), trim);

        // Stepped symmetric crown + flanking geometric fins
        float crownLift = e.State == EnemyState.Telegraph
            ? -8f * e.TelegraphProgress
            : defeated ? 6f : Mathf.Sin(RealElapsedSec * 2.6f) * 1.5f;
        Vector2 crownOrigin = origin + new Vector2(0f, crownLift);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(CrownLeftPoints, crownOrigin), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(CrownRightPoints, crownOrigin), lit);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(LeftFinPoints, crownOrigin), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RightFinPoints, crownOrigin), trim);

        // Central symmetric diamond core (reflects gold/crimson telegraph signal)
        Color coreColor = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind)
                : defeated ? RuinGeometryPalette.BossDefeatedEye : RuinGeometryPalette.TrainingPillarCoreIdle;
        Vector2 coreCenter = origin + new Vector2(0f, -4f);
        GeometricDraw.Diamond(this, coreCenter, 7f, 12f, coreColor);
    }
}
