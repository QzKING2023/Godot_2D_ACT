using ACTSpike.ACT.Enemy;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Low, forward-leaning acute-angled triangular light-armor silhouette (低矮前倾锐角三角轻甲切面剪影)
/// for 近战斥候. Renders strictly via 2D procedural polygons and inherits the text-free
/// golden-fan normal slash「战斗前摇信号」and compact overhead health bar from EnemyVisual.
/// </summary>
public partial class MeleeScoutVisual : EnemyVisual
{
    // Local coordinates authored facing right (+1); mirrored along X via Facing.
    // Feet rest at Y = +32f (matching MeleeScoutEnemy.DefaultBodySize = 42x64).
    private static readonly Vector2[] RearLegPoints =
    {
        new(-22f, 32f), new(-10f, 10f), new(-2f, 16f), new(-11f, 32f)
    };

    private static readonly Vector2[] FrontLegPoints =
    {
        new(0f, 18f), new(11f, 8f), new(20f, 32f), new(10f, 32f)
    };

    private static readonly Vector2[] ShadowHullPoints =
    {
        new(-24f, 14f), new(-10f, -20f), new(6f, -12f), new(-4f, 20f)
    };

    private static readonly Vector2[] LitProwPoints =
    {
        new(-10f, -20f), new(26f, -4f), new(8f, 16f), new(-4f, 16f)
    };

    private static readonly Vector2[] SpineTrimPoints =
    {
        new(-12f, -19f), new(24f, -6f), new(16f, -2f), new(-8f, -13f)
    };

    private static readonly Vector2[] HelmCowlPoints =
    {
        new(-12f, -18f), new(10f, -29f), new(21f, -16f), new(2f, -11f)
    };

    private static readonly Vector2[] VisorSlitPoints =
    {
        new(6f, -21f), new(18f, -17f), new(9f, -14f)
    };

    private static readonly Vector2[] ArmBladeIdlePoints =
    {
        new(4f, 2f), new(32f, -3f), new(16f, 10f)
    };

    private static readonly Vector2[] ArmBladeTelegraphPoints =
    {
        new(-4f, -2f), new(-30f, -16f), new(-10f, 8f)
    };

    private static readonly Vector2[] ArmBladeStrikePoints =
    {
        new(8f, -4f), new(46f, 2f), new(16f, 13f)
    };

    protected override float OverheadHealthBarOffsetY => -46f;

    protected override void DrawBody()
    {
        EnemyController? e = BoundEnemy;
        if (e is null) return;

        bool defeated = e.IsDefeated;
        bool flash = IsHitFlashing;
        int facing = Facing;

        float strideBob = e.State == EnemyState.Chase
            ? Mathf.Sin(RealElapsedSec * 16f) * 2.5f
            : defeated ? 0f : Mathf.Sin(RealElapsedSec * 3.2f) * 1.2f;
        float settleY = defeated ? 14f : strideBob;

        float offsetX = e.State switch
        {
            EnemyState.Telegraph => -facing * (6f + 12f * e.TelegraphProgress),
            EnemyState.Strike => facing * 16f,
            EnemyState.Hitstun => -facing * 8f,
            _ => 0f
        };

        float leanRotation = e.State switch
        {
            EnemyState.Chase => facing * 0.11f,
            EnemyState.Telegraph => -facing * (0.08f + 0.10f * e.TelegraphProgress),
            EnemyState.Strike => facing * 0.18f,
            EnemyState.Hitstun => -facing * 0.16f,
            EnemyState.Dead or EnemyState.Victory => facing * 0.28f,
            _ => facing * 0.04f
        };

        Vector2 origin = new Vector2(offsetX, settleY);

        Color shadow = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedShadow : RuinGeometryPalette.BossShadowArmor;
        Color lit = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.TowerLit;
        Color trim = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.PlatformTopLit;
        Color visor = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.TelegraphNormal
                : defeated ? RuinGeometryPalette.BossDefeatedEye : RuinGeometryPalette.BossEyeIdle;
        Color bladeFill = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.TelegraphNormal
                : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.PlatformTopLit;

        // Grounded acute-angle legs (subtle stride shift during chase)
        float strideShift = e.State == EnemyState.Chase ? Mathf.Sin(RealElapsedSec * 16f) * 4f : 0f;
        Vector2 rearLegOrigin = new Vector2(offsetX - facing * strideShift, defeated ? 12f : 0f);
        Vector2 frontLegOrigin = new Vector2(offsetX + facing * strideShift, defeated ? 12f : 0f);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RearLegPoints, rearLegOrigin, scaleX: facing), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(FrontLegPoints, frontLegOrigin, scaleX: facing), lit);

        // Low forward-leaning acute triangular light-armor torso
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(ShadowHullPoints, origin, scaleX: facing, rotation: leanRotation),
            shadow);
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(LitProwPoints, origin, scaleX: facing, rotation: leanRotation),
            lit);
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(SpineTrimPoints, origin, scaleX: facing, rotation: leanRotation),
            trim);

        // Swept-back acute helm cowl & visor glint
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(HelmCowlPoints, origin, scaleX: facing, rotation: leanRotation),
            shadow);
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(VisorSlitPoints, origin, scaleX: facing, rotation: leanRotation),
            visor);

        // Acute triangular arm-blade reflecting windup/slash posture
        Vector2[] bladeLocal = e.State switch
        {
            EnemyState.Telegraph => ArmBladeTelegraphPoints,
            EnemyState.Strike => ArmBladeStrikePoints,
            _ => ArmBladeIdlePoints
        };
        GeometricDraw.FillAndStrokePolygon(
            this,
            GeometricDraw.TransformPoints(bladeLocal, origin, scaleX: facing, rotation: leanRotation),
            bladeFill,
            shadow,
            width: 1.6f);
    }
}
