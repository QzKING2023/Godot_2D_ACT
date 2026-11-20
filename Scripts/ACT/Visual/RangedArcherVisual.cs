using ACTSpike.ACT.Enemy;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Procedural「遗迹几何表现」visual for「远程射手」:
/// slender levitating prism silhouette with flanking geometric wings that unfold during
/// aiming telegraph and a converging golden aiming line「战斗前摇信号」.
/// </summary>
public partial class RangedArcherVisual : EnemyVisual
{
    private const float MaxAimLineLength = 760f;

    private static readonly Vector2[] PrismLeftFacet =
    {
        new(0f, -44f), new(-15f, -12f), new(-11f, 22f), new(0f, 36f)
    };

    private static readonly Vector2[] PrismRightFacet =
    {
        new(0f, -44f), new(15f, -12f), new(11f, 22f), new(0f, 36f)
    };

    private static readonly Vector2[] CrownSpirePoints =
    {
        new(0f, -52f), new(7f, -38f), new(0f, -32f), new(-7f, -38f)
    };

    private static readonly Vector2[] LeftWingPoints =
    {
        new(-14f, -22f), new(-26f, -34f), new(-22f, -4f), new(-12f, 2f)
    };

    private static readonly Vector2[] RightWingPoints =
    {
        new(14f, -22f), new(26f, -34f), new(22f, -4f), new(12f, 2f)
    };

    private static readonly Vector2[] VisorSlitPoints =
    {
        new(-3f, -24f), new(5f, -20f), new(5f, -8f), new(-3f, -4f)
    };

    private static readonly Vector2[] HoverChevronPoints =
    {
        new(-12f, 36f), new(0f, 43f), new(12f, 36f), new(0f, 40f)
    };

    protected override float OverheadHealthBarOffsetY => -66f;

    /// <summary>Normalized 0..1 wing deployment factor exposed for visual verification.</summary>
    public float WingSpreadFactor
    {
        get
        {
            EnemyController? e = BoundEnemy;
            if (e is null || e.IsDefeated) return 0f;
            if (e.State == EnemyState.Telegraph) return Mathf.Clamp(e.TelegraphProgress, 0f, 1f);
            if (e.State == EnemyState.Strike || StrikeFlash > 0f) return 1f;
            return 0f;
        }
    }

    protected override void DrawTelegraphOrStrike()
    {
        if (BoundEnemy is not RangedArcherEnemy archer || archer.IsDefeated)
            return;

        bool timeSlowActive = BoundTimeSlow is { Active: true };
        float shimmer = timeSlowActive ? 0.84f + 0.16f * Mathf.Sin(RealElapsedSec * 16f) : 1f;
        Vector2 muzzleLocal = archer.MuzzleLocalOffset;
        Vector2 dir = archer.AimDirection.LengthSquared() > 0.001f
            ? archer.AimDirection.Normalized()
            : new Vector2(Facing, 0f);
        float lineLength = ComputeClampedAimLength(archer, dir);

        if (archer.State == EnemyState.Telegraph)
        {
            float prog = Mathf.Clamp(archer.TelegraphProgress, 0f, 1f);
            float inv = 1f - prog;
            Vector2 aimEnd = muzzleLocal + dir * lineLength;
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            // Converging twin bracket lines narrowing toward the central beam as telegraph finishes
            float outerOffset = 14f * inv;
            if (outerOffset > 0.6f)
            {
                Color guideColor = RuinGeometryPalette.TelegraphNormal with { A = (0.28f + 0.42f * prog) * shimmer };
                DrawLine(muzzleLocal + perp * (outerOffset * 0.4f), aimEnd + perp * outerOffset, guideColor, 1.4f, true);
                DrawLine(muzzleLocal - perp * (outerOffset * 0.4f), aimEnd - perp * outerOffset, guideColor, 1.4f, true);
            }

            // Primary golden aiming line intensifying in brightness and sharpness
            Color beamColor = RuinGeometryPalette.TelegraphNormal with { A = (0.35f + 0.65f * prog) * shimmer };
            float beamWidth = Mathf.Lerp(3.2f, 1.8f, prog);
            DrawLine(muzzleLocal, aimEnd, beamColor, beamWidth, true);

            if (prog > 0.6f)
            {
                float hotT = (prog - 0.6f) / 0.4f;
                Color coreColor = RuinGeometryPalette.SlashInnerGold with { A = (0.45f + 0.55f * hotT) * shimmer };
                DrawLine(muzzleLocal, aimEnd, coreColor, 1.1f, true);
            }

            // Contracting diamond focus reticle at muzzle
            float focusRadius = 4f + 9f * inv;
            GeometricDraw.Diamond(this, muzzleLocal, focusRadius * 0.65f, focusRadius,
                RuinGeometryPalette.TelegraphNormal with { A = (0.45f + 0.50f * prog) * shimmer });

            if (timeSlowActive)
                DrawLine(muzzleLocal, aimEnd, RuinGeometryPalette.TimeSlowEdge with { A = 0.25f * shimmer }, 4.5f, true);
        }
        else if (archer.State == EnemyState.Strike || StrikeFlash > 0f)
        {
            float flashStrength = archer.State == EnemyState.Strike ? 1f : Mathf.Clamp(StrikeFlash / 0.22f, 0f, 1f);
            Vector2 streakEnd = muzzleLocal + dir * Mathf.Min(lineLength, 96f);
            DrawLine(muzzleLocal, streakEnd, RuinGeometryPalette.SlashOuterGold with { A = 0.85f * flashStrength }, 4f, true);
            GeometricDraw.Diamond(this, muzzleLocal, 6f, 10f, RuinGeometryPalette.SlashInnerGold with { A = flashStrength });
        }
    }

    protected override void DrawBody()
    {
        EnemyController? e = BoundEnemy;
        if (e is null) return;

        bool defeated = e.IsDefeated;
        bool flash = IsHitFlashing;
        float hoverY = defeated ? 16f : Mathf.Sin(RealElapsedSec * 3.2f) * 4.5f - 4f;
        float recoilX = e.State == EnemyState.Strike
            ? -Facing * 7f
            : e.State == EnemyState.Hitstun ? -Facing * 5f : 0f;
        Vector2 origin = new Vector2(recoilX, hoverY);

        Color shadow = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedShadow : RuinGeometryPalette.BossShadowArmor;
        Color lit = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.BossLitArmor;
        Color trim = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.BossPauldronHighlight;
        Color wingAccent = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.TelegraphNormal
                : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.PlatformTopLit;
        Color visorColor = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.TelegraphNormal
                : defeated ? RuinGeometryPalette.BossDefeatedEye : RuinGeometryPalette.BossEyeIdle;

        // Flanking geometric wings: tucked at rest, unfolding outward during aiming telegraph
        float spread = WingSpreadFactor;
        Vector2 leftWingOffset = origin + new Vector2(-spread * 11f, -spread * 6f);
        Vector2 rightWingOffset = origin + new Vector2(spread * 11f, -spread * 6f);
        float wingAngle = spread * 0.32f;

        GeometricDraw.FillAndStrokePolygon(
            this,
            GeometricDraw.TransformPoints(LeftWingPoints, leftWingOffset, rotation: -wingAngle),
            shadow,
            wingAccent,
            width: 1.5f);
        GeometricDraw.FillAndStrokePolygon(
            this,
            GeometricDraw.TransformPoints(RightWingPoints, rightWingOffset, rotation: wingAngle),
            trim,
            wingAccent,
            width: 1.5f);

        // Slender levitating prism core
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(PrismLeftFacet, origin), shadow);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(PrismRightFacet, origin), lit);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(CrownSpirePoints, origin), trim);

        // Directional visor aperture facing the player
        Vector2 visorOrigin = origin + new Vector2(Facing * 4f, 0f);
        GeometricDraw.FillPolygon(
            this,
            GeometricDraw.TransformPoints(VisorSlitPoints, visorOrigin, scaleX: Facing),
            visorColor);

        // Subtle levitation base chevron below prism
        if (!defeated)
        {
            Color chevronColor = e.State == EnemyState.Telegraph
                ? RuinGeometryPalette.TelegraphNormal with { A = 0.85f }
                : RuinGeometryPalette.PlatformTopLit with { A = 0.55f };
            GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(HoverChevronPoints, new Vector2(recoilX, hoverY * 0.5f)), chevronColor);
        }
    }

    private float ComputeClampedAimLength(RangedArcherEnemy archer, Vector2 dir)
    {
        World2D? world = GetWorld2D();
        if (world is null) return MaxAimLineLength;

        Vector2 start = archer.MuzzleGlobalPosition;
        Vector2 end = start + dir * MaxAimLineLength;
        var query = PhysicsRayQueryParameters2D.Create(start, end, collisionMask: 2);
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && hit.ContainsKey("position"))
            return start.DistanceTo(hit["position"].AsVector2());

        return MaxAimLineLength;
    }
}
