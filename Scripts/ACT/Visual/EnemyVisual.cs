using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Variant A (硬边剪影切面) procedural monolith boss/enemy renderer and
/// text-free「战斗前摇信号」driven by EnemyController state and real timestamps.
/// </summary>
public partial class EnemyVisual : Node2D
{
    private static readonly Vector2[] LeftCorePoints =
    {
        new(-28f, 48f), new(-34f, -28f), new(0f, -52f), new(0f, 48f)
    };

    private static readonly Vector2[] RightCorePoints =
    {
        new(0f, -52f), new(34f, -28f), new(28f, 48f), new(0f, 48f)
    };

    private static readonly Vector2[] LeftPauldronPoints =
    {
        new(-46f, -12f), new(-30f, -44f), new(-18f, -26f), new(-30f, 4f)
    };

    private static readonly Vector2[] RightPauldronPoints =
    {
        new(18f, -26f), new(32f, -46f), new(48f, -12f), new(30f, 6f)
    };

    private static readonly Vector2[] EyeSlitPoints =
    {
        new(-14f, -20f), new(14f, -20f), new(8f, -12f), new(-8f, -12f)
    };

    private EnemyController? _enemy;
    private PlayerController? _player;
    private TimeSlowManager? _timeSlow;

    private float _realElapsedSec;
    private float _lastRealSec = -1f;
    private float _hitFlash;
    private float _strikeFlash;
    private int _facing = -1;
    private bool _visualFrozen;

    public int Facing => _facing;
    public bool IsPlayerBound => _player is not null;
    public bool IsTimeSlowBound => _timeSlow is not null;
    public bool IsVisualFrozen => _visualFrozen;
    public bool ShouldDrawOverheadHealthBar => _enemy is not null && _enemy.ShowsOverheadHealthBar;

    protected EnemyController? BoundEnemy => _enemy;
    protected TimeSlowManager? BoundTimeSlow => _timeSlow;
    protected float RealElapsedSec => _realElapsedSec;
    protected bool IsHitFlashing => _hitFlash > 0f;
    protected float StrikeFlash => _strikeFlash;

    public override void _Ready()
    {
        ZIndex = 1;
        if (_enemy is null && GetParent() is EnemyController parent)
            Bind(parent, _player);
    }

    public void Bind(EnemyController enemy, PlayerController? player)
    {
        if (!ReferenceEquals(_enemy, enemy))
        {
            UnbindEnemy();
            _enemy = enemy;
            _enemy.StrikeActivated += OnStrikeActivated;
            _enemy.Damaged += OnDamaged;
        }

        _player = player;
        UpdateFacing();
    }

    public void BindTimeSlow(TimeSlowManager? timeSlow)
    {
        _timeSlow = timeSlow;
    }

    /// <summary>Called by CombatFeedbackLayer during visual-only hitstop without touching Engine.TimeScale.</summary>
    public void SetVisualFreeze(bool frozen) => _visualFrozen = frozen;

    public override void _Process(double delta)
    {
        if (_enemy is null) return;

        float nowReal = Time.GetTicksMsec() / 1000f;
        float realDt = _lastRealSec < 0f ? (float)delta : Mathf.Clamp(nowReal - _lastRealSec, 0f, 0.05f);
        _lastRealSec = nowReal;

        UpdateFacing();
        OnVisualProcess(realDt);

        if (!_visualFrozen)
        {
            _realElapsedSec += realDt;
            _hitFlash = Mathf.Max(0f, _hitFlash - realDt);
            _strikeFlash = Mathf.Max(0f, _strikeFlash - realDt);
        }

        QueueRedraw();
    }

    protected virtual void OnVisualProcess(float realDt) { }

    protected Vector2 ComputeMonolithBodyOrigin()
    {
        EnemyController e = _enemy!;
        float floatY = e.IsDefeated ? 24f : Mathf.Sin(_realElapsedSec * 2.4f) * 3.5f;
        float windup = e.State == EnemyState.Telegraph
            ? -_facing * (8f + 14f * e.TelegraphProgress)
            : (e.State == EnemyState.Strike ? _facing * 22f : 0f);
        return new Vector2(windup, floatY);
    }

    private const float OverheadBarWidth = 58f;
    private const float OverheadBarHeight = 6f;
    private const float OverheadBarSkew = 3f;

    protected virtual float OverheadHealthBarOffsetY => -68f;

    public override void _Draw()
    {
        if (_enemy is null) return;

        DrawTelegraphOrStrike();
        DrawBody();
        if (ShouldDrawOverheadHealthBar)
            DrawOverheadHealthBar();
    }

    protected virtual void DrawBody()
    {
        DrawMonolithBody();
    }

    protected virtual void DrawOverheadHealthBar()
    {
        EnemyController e = _enemy!;
        float fraction = e.MaxHp > 0f ? Mathf.Clamp(e.Hp / e.MaxHp, 0f, 1f) : 0f;
        var barRect = new Rect2(
            -OverheadBarWidth * 0.5f,
            OverheadHealthBarOffsetY,
            OverheadBarWidth,
            OverheadBarHeight);

        GeometricDraw.AngledBar(
            this,
            barRect,
            fraction,
            RuinGeometryPalette.HudPanelDark,
            RuinGeometryPalette.HudBossHp,
            OverheadBarSkew);
        GeometricDraw.StrokePolygon(
            this,
            GeometricDraw.BuildSkewedRect(barRect, OverheadBarSkew),
            RuinGeometryPalette.PlatformTopLit with { A = 0.68f },
            width: 1.2f);
    }

    public override void _ExitTree()
    {
        UnbindEnemy();
    }

    private void UnbindEnemy()
    {
        if (_enemy is null) return;
        _enemy.StrikeActivated -= OnStrikeActivated;
        _enemy.Damaged -= OnDamaged;
        _enemy = null;
    }

    private void UpdateFacing()
    {
        if (_enemy is null)
            return;

        _facing = _enemy.Facing >= 0 ? 1 : -1;
    }

    protected virtual void DrawTelegraphOrStrike()
    {
        EnemyController e = _enemy!;
        bool timeSlowActive = _timeSlow is { Active: true };
        float shimmer = timeSlowActive ? 0.84f + 0.16f * Mathf.Sin(_realElapsedSec * 16f) : 1f;

        if (e.State == EnemyState.Telegraph)
        {
            float prog = Mathf.Clamp(e.TelegraphProgress, 0f, 1f);
            float inv = 1f - prog;
            switch (e.CurrentAttackKind)
            {
                case BossAttackKind.Normal:
                {
                    float range = e.CurrentAttack.Range;
                    float ang = _facing > 0 ? 0f : Mathf.Pi;
                    float spread = 0.85f;
                    Color fillArcColor = RuinGeometryPalette.TelegraphNormalFill with { A = (0.25f + 0.45f * prog) * shimmer };
                    Color edgeColor = RuinGeometryPalette.TelegraphNormal with { A = 0.90f * shimmer };
                    DrawArc(Vector2.Zero, range * (0.45f + 0.55f * prog), ang - spread, ang + spread, 24, fillArcColor, 18f, true);
                    DrawArc(Vector2.Zero, range, ang - spread, ang + spread, 24, edgeColor, 3.5f, true);
                    DrawLine(Vector2.Zero, Vector2.FromAngle(ang - spread) * range, edgeColor with { A = 0.35f * shimmer }, 2f, true);
                    DrawLine(Vector2.Zero, Vector2.FromAngle(ang + spread) * range, edgeColor with { A = 0.35f * shimmer }, 2f, true);
                    if (timeSlowActive)
                        DrawArc(Vector2.Zero, range + 5f, ang - spread, ang + spread, 24,
                            RuinGeometryPalette.TimeSlowEdge with { A = 0.28f * shimmer }, 2f, true);
                    break;
                }
                case BossAttackKind.BreakGrab:
                {
                    float range = e.CurrentAttack.Range;
                    var dangerRect = new Rect2(-range, -58f, range * 2f, 116f);
                    DrawRect(dangerRect, RuinGeometryPalette.TelegraphBreakGrabFill with { A = (0.14f + 0.16f * prog) * shimmer }, true);
                    DrawRect(dangerRect, RuinGeometryPalette.HitCrimson with { A = shimmer }, false, 4f);
                    if (timeSlowActive)
                    {
                        var outerRect = new Rect2(-range - 4f, -62f, range * 2f + 8f, 124f);
                        DrawRect(outerRect, RuinGeometryPalette.TimeSlowEdge with { A = 0.26f * shimmer }, false, 2f);
                    }

                    // Contracting crimson X mark (unblockable cue)
                    float shrink = 22f + 46f * inv;
                    Vector2 mark = new Vector2(_facing * 68f, -10f);
                    Color xColor = RuinGeometryPalette.TelegraphBreakGrab with { A = shimmer };
                    DrawLine(mark + new Vector2(-shrink, -shrink), mark + new Vector2(shrink, shrink), xColor, 5f, true);
                    DrawLine(mark + new Vector2(shrink, -shrink), mark + new Vector2(-shrink, shrink), xColor, 5f, true);
                    break;
                }
                case BossAttackKind.DelayedArea:
                {
                    float radius = e.CurrentAttack.HazardRadius;
                    DrawCircle(Vector2.Zero, radius, RuinGeometryPalette.TelegraphDelayedAreaFill with { A = (0.14f + 0.12f * prog) * shimmer });
                    DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 40,
                        RuinGeometryPalette.TelegraphDelayedArea with { A = shimmer }, 4f, true);
                    DrawArc(Vector2.Zero, Mathf.Max(6f, radius * inv), 0f, Mathf.Tau, 32,
                        RuinGeometryPalette.DelayedAreaCountdown with { A = shimmer }, 5f, true);
                    if (timeSlowActive)
                        DrawArc(Vector2.Zero, radius + 5f, 0f, Mathf.Tau, 40,
                            RuinGeometryPalette.TimeSlowEdge with { A = 0.28f * shimmer }, 2f, true);
                    break;
                }
            }
        }
        else if (e.State == EnemyState.Strike || _strikeFlash > 0f)
        {
            if (e.CurrentAttackKind == BossAttackKind.DelayedArea)
            {
                DrawCircle(Vector2.Zero, e.CurrentAttack.HazardRadius, RuinGeometryPalette.TelegraphDelayedAreaStrike);
            }
            else
            {
                float ang = _facing > 0 ? 0f : Mathf.Pi;
                Color strikeColor = RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind) with { A = 0.85f };
                DrawArc(Vector2.Zero, e.CurrentAttack.Range * 0.85f, ang - 0.95f, ang + 0.95f, 22, strikeColor, 26f, true);
            }
        }
    }

    private void DrawMonolithBody()
    {
        EnemyController e = _enemy!;
        Vector2 bodyOrigin = ComputeMonolithBodyOrigin();
        bool flash = _hitFlash > 0f;

        Color darkArmor = flash
            ? Colors.White
            : e.IsDefeated ? RuinGeometryPalette.BossDefeatedShadow : RuinGeometryPalette.BossShadowArmor;
        Color litArmor = flash
            ? Colors.White
            : e.IsDefeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.BossLitArmor;
        Color pauldronLit = flash
            ? Colors.White
            : e.IsDefeated ? RuinGeometryPalette.BossDefeatedLit : RuinGeometryPalette.BossPauldronHighlight;
        Color eyeColor = e.State == EnemyState.Telegraph
            ? RuinGeometryPalette.ForTelegraph(e.CurrentAttackKind)
            : e.IsDefeated ? RuinGeometryPalette.BossDefeatedEye : RuinGeometryPalette.BossEyeIdle;

        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(LeftCorePoints, bodyOrigin), darkArmor);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RightCorePoints, bodyOrigin), litArmor);

        float shoulderLift = e.State == EnemyState.Telegraph
            ? -12f * e.TelegraphProgress
            : e.IsDefeated ? 8f : 0f;
        Vector2 shoulderOrigin = bodyOrigin + new Vector2(0f, shoulderLift);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(LeftPauldronPoints, shoulderOrigin), darkArmor);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(RightPauldronPoints, shoulderOrigin), pauldronLit);

        Vector2 eyeOrigin = bodyOrigin + new Vector2(_facing * 4f, e.IsDefeated ? 6f : 0f);
        GeometricDraw.FillPolygon(this, GeometricDraw.TransformPoints(EyeSlitPoints, eyeOrigin), eyeColor);
    }

    private void OnStrikeActivated(EnemyStrikeCue _)
    {
        _strikeFlash = 0.22f;
    }

    private void OnDamaged(EnemyDamagedCue _)
    {
        _hitFlash = 0.14f;
    }
}
