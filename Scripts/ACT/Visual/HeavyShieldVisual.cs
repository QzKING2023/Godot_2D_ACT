using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Procedural「遗迹几何表现」visual for 重盾卫兵:
/// broad trapezoidal heavy armor, facing-side independent polygonal greatshield,
/// local metallic deflection sparks, and drooped/darkened cracked shield during「破盾失衡」.
/// </summary>
public partial class HeavyShieldVisual : EnemyVisual
{
    // Broad grounded trapezoidal heavy armor (local facing-invariant base silhouette)
    private static readonly Vector2[] LeftArmorPoints =
    {
        new(-34f, 48f), new(-28f, -34f), new(-14f, -46f), new(0f, -48f), new(0f, 48f)
    };

    private static readonly Vector2[] RightArmorPoints =
    {
        new(0f, -48f), new(14f, -46f), new(28f, -34f), new(34f, 48f), new(0f, 48f)
    };

    private static readonly Vector2[] ShoulderMantlePoints =
    {
        new(-36f, -20f), new(-24f, -44f), new(24f, -44f), new(36f, -20f), new(22f, -12f), new(-22f, -12f)
    };

    private static readonly Vector2[] HelmCapPoints =
    {
        new(-14f, -40f), new(-10f, -54f), new(10f, -54f), new(14f, -40f)
    };

    private static readonly Vector2[] VisorSlitPoints =
    {
        new(-10f, -38f), new(10f, -38f), new(7f, -32f), new(-7f, -32f)
    };

    // Independent polygonal tower greatshield defined in +X (right-facing) space; mirrored by Facing
    private static readonly Vector2[] GreatshieldOuterPoints =
    {
        new(22f, -44f), new(42f, -36f), new(46f, 0f), new(42f, 38f), new(26f, 48f), new(18f, 34f), new(18f, -32f)
    };

    private static readonly Vector2[] GreatshieldInnerPoints =
    {
        new(24f, -34f), new(38f, -28f), new(41f, 0f), new(38f, 28f), new(26f, 36f), new(22f, 26f), new(22f, -24f)
    };

    private static readonly Vector2[] GreatshieldFrontEdgePoints =
    {
        new(38f, -34f), new(42f, -36f), new(46f, 0f), new(42f, 38f), new(37f, 34f), new(41f, 0f)
    };

    private static readonly Vector2[] ShieldCrackPrimary =
    {
        new(25f, -32f), new(34f, -14f), new(28f, 4f), new(37f, 22f), new(29f, 38f)
    };

    private static readonly Vector2[] ShieldCrackBranch =
    {
        new(34f, -14f), new(42f, -6f), new(37f, 8f)
    };

    private EnemyController? _subscribedEnemy;
    private float _deflectFlash;

    public bool IsShieldRaised => BoundEnemy is { IsFrontalDefenseActive: true };
    public bool IsShieldDrooped => BoundEnemy is { IsShieldBroken: true };
    public bool HasVisibleShieldCrack => IsShieldDrooped;
    public float ShieldDroopOffsetY => IsShieldDrooped ? 16f : 0f;
    public float ShieldCenterOffsetX => Facing * (IsShieldDrooped ? 26f : 34f);

    protected override float OverheadHealthBarOffsetY => -74f;

    public override void _Ready()
    {
        base._Ready();
        SyncDeflectSubscription();
    }

    protected override void OnVisualProcess(float realDt)
    {
        SyncDeflectSubscription();
        if (!IsVisualFrozen && _deflectFlash > 0f)
            _deflectFlash = Mathf.Max(0f, _deflectFlash - realDt);
    }

    public override void _ExitTree()
    {
        UnsubscribeDeflect();
        base._ExitTree();
    }

    protected override void DrawBody()
    {
        EnemyController? e = BoundEnemy;
        if (e is null) return;

        bool defeated = e.IsDefeated;
        bool shieldBroken = e.IsShieldBroken;
        bool flash = IsHitFlashing;

        float bodyShiftX = e.State switch
        {
            EnemyState.Telegraph when e.CurrentAttackKind == BossAttackKind.BreakGrab => -Facing * (6f + 8f * e.TelegraphProgress),
            EnemyState.Telegraph => -Facing * (4f + 10f * e.TelegraphProgress),
            EnemyState.Strike when e.CurrentAttackKind == BossAttackKind.BreakGrab => Facing * 18f,
            EnemyState.Strike => Facing * 12f,
            EnemyState.ShieldBroken => -Facing * 8f,
            EnemyState.Hitstun => -Facing * 6f,
            _ => 0f
        };
        float settleY = defeated ? 18f : (shieldBroken ? 5f : Mathf.Sin(RealElapsedSec * 2.1f) * 1.4f);
        Vector2 origin = new Vector2(bodyShiftX, settleY);

        Color shadowArmor = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedShadow : RuinGeometryPalette.BossShadowArmor;
        Color litArmor = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.PillarLit;
        Color mantleColor = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.BossLitArmor;
        Color trimColor = flash
            ? Colors.White
            : defeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.TrainingPillarTrim;

        // Broad trapezoidal heavy armor torso
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(LeftArmorPoints, origin), shadowArmor);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RightArmorPoints, origin), litArmor);
        GeometricDraw.FillAndStrokePolygon(
            this,
            GeometricDraw.TransformPoints(ShoulderMantlePoints, origin),
            mantleColor,
            trimColor with { A = 0.72f },
            width: 1.8f);
        DrawRect(new Rect2(origin.X - 28f, origin.Y + 24f, 56f, 4f), trimColor);
        DrawRect(new Rect2(origin.X - 24f, origin.Y - 4f, 48f, 3f), trimColor with { A = 0.8f });

        // Armored helm & directional visor slit
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(HelmCapPoints, origin), shadowArmor);
        Color visorColor = flash
            ? Colors.White
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind)
                : (defeated || shieldBroken) ? RuinGeometryPalette.BossDefeatedEye : RuinGeometryPalette.BossEyeIdle;
        Vector2 visorOrigin = origin + new Vector2(Facing * 4f, shieldBroken ? 3f : 0f);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(VisorSlitPoints, visorOrigin), visorColor);

        DrawGreatshield(e, origin, defeated, shieldBroken, flash);
    }

    private void DrawGreatshield(EnemyController e, Vector2 bodyOrigin, bool defeated, bool shieldBroken, bool hitFlash)
    {
        float shieldOffsetX = 0f;
        float shieldOffsetY = 0f;
        float shieldRotation = 0f;

        if (shieldBroken)
        {
            shieldOffsetX = -Facing * 8f;
            shieldOffsetY = ShieldDroopOffsetY;
            shieldRotation = Facing * 0.28f;
        }
        else if (defeated)
        {
            shieldOffsetX = Facing * 4f;
            shieldOffsetY = 14f;
            shieldRotation = Facing * 0.42f;
        }
        else if (e.State == EnemyState.Telegraph)
        {
            if (e.CurrentAttackKind == BossAttackKind.BreakGrab)
            {
                // Crimson unblockable shield charge: hunkered forward behind braced tower shield
                shieldOffsetX = Facing * (4f + 6f * e.TelegraphProgress);
                shieldOffsetY = 4f;
                shieldRotation = -Facing * 0.08f;
            }
            else
            {
                // Golden heavy shield slam: raised back to slam down
                shieldOffsetX = -Facing * (4f + 6f * e.TelegraphProgress);
                shieldOffsetY = -10f * e.TelegraphProgress;
                shieldRotation = -Facing * (0.14f * e.TelegraphProgress);
            }
        }
        else if (e.State == EnemyState.Strike)
        {
            shieldOffsetX = Facing * (e.CurrentAttackKind == BossAttackKind.BreakGrab ? 12f : 8f);
            shieldRotation = Facing * 0.12f;
        }
        else if (_deflectFlash > 0f)
        {
            shieldOffsetX = -Facing * 4f * (_deflectFlash / 0.18f);
        }

        Vector2 shieldOrigin = bodyOrigin + new Vector2(shieldOffsetX, shieldOffsetY);
        bool brightDeflect = _deflectFlash > 0f && !shieldBroken && !defeated;

        Color outerFill = hitFlash
            ? Colors.White
            : (shieldBroken || defeated)
                ? RuinGeometryPalette.BossDefeatedShadow
                : brightDeflect ? RuinGeometryPalette.TrainingPillarLit : RuinGeometryPalette.GateStoneShadow;
        Color innerFill = hitFlash
            ? Colors.White
            : (shieldBroken || defeated)
                ? RuinGeometryPalette.BossDefeatedLit
                : brightDeflect ? RuinGeometryPalette.PlatformTopLit : RuinGeometryPalette.GateStoneLit;
        Color frontEdge = (shieldBroken || defeated)
            ? RuinGeometryPalette.BossDefeatedEye
            : e.State == EnemyState.Telegraph
                ? RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind)
                : brightDeflect ? Colors.White : RuinGeometryPalette.PlatformTopLit;

        Vector2[] outerPoly = GeometricDraw.TransformPoints(GreatshieldOuterPoints, shieldOrigin, scaleX: Facing, scaleY: 1f, rotation: shieldRotation);
        Vector2[] innerPoly = GeometricDraw.TransformPoints(GreatshieldInnerPoints, shieldOrigin, scaleX: Facing, scaleY: 1f, rotation: shieldRotation);
        Vector2[] edgePoly = GeometricDraw.TransformPoints(GreatshieldFrontEdgePoints, shieldOrigin, scaleX: Facing, scaleY: 1f, rotation: shieldRotation);

        GeometricDraw.FillAndStrokePolygon(this, outerPoly, outerFill, frontEdge with { A = shieldBroken ? 0.45f : 0.92f }, width: 2f);
        GeometricDraw.FillPolygon(this, innerPoly, innerFill);
        GeometricDraw.FillPolygon(this, edgePoly, frontEdge);

        // Central geometric shield boss diamond
        Vector2 bossCenter = shieldOrigin + new Vector2(Facing * 31f, 0f).Rotated(shieldRotation);
        Color bossTint = shieldBroken
            ? RuinGeometryPalette.PlatformShadow
            : e.State is EnemyState.Telegraph or EnemyState.Strike
                ? RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind)
                : RuinGeometryPalette.TrainingPillarTrim;
        GeometricDraw.Diamond(this, bossCenter, 4.5f, 9f, bossTint);

        // Fissure crack lines rendered across drooped darkened shield during「破盾失衡」
        if (shieldBroken)
        {
            Vector2[] crackA = GeometricDraw.TransformPoints(ShieldCrackPrimary, shieldOrigin, scaleX: Facing, scaleY: 1f, rotation: shieldRotation);
            Vector2[] crackB = GeometricDraw.TransformPoints(ShieldCrackBranch, shieldOrigin, scaleX: Facing, scaleY: 1f, rotation: shieldRotation);
            DrawPolyline(crackA, RuinGeometryPalette.PlatformShadow, 3.2f, true);
            DrawPolyline(crackA, RuinGeometryPalette.BossEyeIdle with { A = 0.65f }, 1.2f, true);
            DrawPolyline(crackB, RuinGeometryPalette.PlatformShadow, 2.6f, true);
        }

        // Local geometric deflection flash rays on the shield face
        if (brightDeflect)
        {
            float t = Mathf.Clamp(_deflectFlash / 0.18f, 0f, 1f);
            Vector2 impact = shieldOrigin + new Vector2(Facing * 44f, -4f);
            Color sparkCol = RuinGeometryPalette.PlayerBlade with { A = t };
            DrawLine(impact, impact + new Vector2(Facing * 20f, -18f) * t, sparkCol, 2.5f, true);
            DrawLine(impact, impact + new Vector2(Facing * 24f, 0f) * t, Colors.White with { A = t }, 2.5f, true);
            DrawLine(impact, impact + new Vector2(Facing * 18f, 18f) * t, RuinGeometryPalette.PlayerShieldEdge with { A = t }, 2.2f, true);
        }

        // Forward crimson lunge chevrons during crimson shield charge telegraph/strike
        if (!shieldBroken && !defeated &&
            e.CurrentAttackKind == BossAttackKind.BreakGrab &&
            e.State is EnemyState.Telegraph or EnemyState.Strike)
        {
            float pulse = e.State == EnemyState.Telegraph ? e.TelegraphProgress : 1f;
            Vector2 tip = shieldOrigin + new Vector2(Facing * (56f + 16f * pulse), -4f);
            Color chevronCol = RuinGeometryPalette.TelegraphBreakGrab with { A = 0.85f };
            DrawPolyline(new[]
            {
                tip + new Vector2(-Facing * 12f, -16f),
                tip,
                tip + new Vector2(-Facing * 12f, 16f)
            }, chevronCol, 3f, true);
        }
    }

    private void SyncDeflectSubscription()
    {
        EnemyController? current = BoundEnemy;
        if (ReferenceEquals(_subscribedEnemy, current))
            return;
        UnsubscribeDeflect();
        _subscribedEnemy = current;
        if (_subscribedEnemy is not null && GodotObject.IsInstanceValid(_subscribedEnemy))
            _subscribedEnemy.ShieldDeflected += OnShieldDeflected;
    }

    private void UnsubscribeDeflect()
    {
        if (_subscribedEnemy is not null && GodotObject.IsInstanceValid(_subscribedEnemy))
            _subscribedEnemy.ShieldDeflected -= OnShieldDeflected;
        _subscribedEnemy = null;
    }

    private void OnShieldDeflected(EnemyShieldDeflectedCue _)
    {
        _deflectFlash = 0.18f;
    }
}
