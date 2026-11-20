using ACTSpike.ACT.Combat;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Procedural「遗迹几何表现」renderer for EnemyProjectile, separated from physics/hit resolution.
/// Renders enemy amber-crimson shards and sword-gold reflected shards using shared visual primitives.
/// </summary>
public partial class EnemyProjectileVisual : Node2D
{
    private static readonly Vector2[] EnemyOuterPoints =
    {
        new(14f, 0f), new(-2f, -6.5f), new(-13f, 0f), new(-2f, 6.5f)
    };

    private static readonly Vector2[] EnemyInnerPoints =
    {
        new(8f, 0f), new(-1f, -3f), new(-7f, 0f), new(-1f, 3f)
    };

    private static readonly Vector2[] ReflectedOuterPoints =
    {
        new(18f, 0f), new(-2f, -7.5f), new(-16f, 0f), new(-2f, 7.5f)
    };

    private static readonly Vector2[] ReflectedInnerPoints =
    {
        new(12f, 0f), new(0f, -3.5f), new(-10f, 0f), new(0f, 3.5f)
    };

    private EnemyProjectile? _projectile;

    public void Bind(EnemyProjectile projectile)
    {
        _projectile = projectile;
        QueueRedraw();
    }

    public override void _Ready()
    {
        if (_projectile is null && GetParent() is EnemyProjectile parent)
            _projectile = parent;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_projectile is { IsConsumed: false })
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_projectile is null || _projectile.IsConsumed)
            return;

        if (_projectile.Faction == ProjectileFaction.PlayerReflected)
        {
            DrawLine(new Vector2(-34f, 0f), new Vector2(-10f, 0f), RuinGeometryPalette.SlashOuterGold with { A = 0.72f }, 4.5f, true);
            GeometricDraw.FillAndStrokePolygon(
                this,
                ReflectedOuterPoints,
                RuinGeometryPalette.PlayerBlade,
                RuinGeometryPalette.SlashOuterGold,
                width: 1.8f);
            GeometricDraw.FillPolygon(this, ReflectedInnerPoints, RuinGeometryPalette.SlashInnerGold);
        }
        else
        {
            DrawLine(new Vector2(-24f, 0f), new Vector2(-8f, 0f), RuinGeometryPalette.HitCrimson with { A = 0.60f }, 3f, true);
            GeometricDraw.FillAndStrokePolygon(
                this,
                EnemyOuterPoints,
                RuinGeometryPalette.TelegraphNormal,
                RuinGeometryPalette.HitCrimson,
                width: 1.6f);
            GeometricDraw.FillPolygon(this, EnemyInnerPoints, RuinGeometryPalette.SlashInnerGold);
        }
    }
}
