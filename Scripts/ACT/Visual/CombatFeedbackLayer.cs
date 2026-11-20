using System;
using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Global presentation feedback layer: geometric burst particles, shockwave rings,
/// subtle Camera2D.Offset shake, visual-only ~50ms hitstop (never touching Engine.TimeScale),
/// and fullscreen「时缓视觉场」CanvasLayer (Layer = 5, MouseFilter = Ignore).
/// </summary>
public partial class CombatFeedbackLayer : Node2D
{
    private enum ParticleShape { Diamond = 0, Shard = 1, Spark = 2 }

    private struct BurstParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Age;
        public float MaxAge;
        public Color Tint;
        public ParticleShape Shape;
    }

    private struct RingPulse
    {
        public Vector2 Position;
        public float StartRadius;
        public float EndRadius;
        public float Age;
        public float MaxAge;
        public Color Tint;
        public float Width;
    }

    private static readonly Color DodgeTeal = new("72efdd");
    private static readonly Color HitCrimson = new("ff4769");

    private readonly List<BurstParticle> _particles = new();
    private readonly List<RingPulse> _rings = new();
    private readonly RandomNumberGenerator _rng = new();

    private PlayerController? _player;
    private EnemyController? _enemy;
    private TimeSlowManager? _timeSlow;
    private Camera2D? _camera;
    private CanvasLayer? _timeSlowLayer;
    private TimeSlowOverlayControl? _timeSlowOverlay;

    private float _lastRealSec = -1f;
    private float _shakeIntensity;
    private float _hitstopRemainingSec;
    private float _timeSlowBlend;
    private bool _smokeMode;
    private bool _boundSignals;

    public int ActiveParticleCount => _particles.Count;
    public int ActiveRingCount => _rings.Count;
    public float TimeSlowBlend => _timeSlowBlend;
    public bool IsHitstopActive => _hitstopRemainingSec > 0f;
    public CanvasLayer TimeSlowFieldLayer => EnsureTimeSlowOverlay();

    public static CombatFeedbackLayer Attach(
        Node2D stage,
        PlayerController player,
        EnemyController? enemy,
        TimeSlowManager timeSlow,
        Camera2D camera)
    {
        var layer = new CombatFeedbackLayer { Name = "CombatFeedbackLayer" };
        layer.Initialize(player, enemy, timeSlow, camera);
        stage.AddChild(layer);
        return layer;
    }

    public override void _Ready()
    {
        ZIndex = 25;
        _rng.Randomize();
        _smokeMode = OS.GetEnvironment("ACT_SMOKE") == "1";
        EnsureTimeSlowOverlay();
        SubscribeSignals();
    }

    public override void _Process(double delta)
    {
        float nowReal = Time.GetTicksMsec() / 1000f;
        float realDt = _lastRealSec < 0f ? (float)delta : Mathf.Clamp(nowReal - _lastRealSec, 0f, 0.05f);
        _lastRealSec = nowReal;

        UpdateTimeSlowField(realDt);

        if (_hitstopRemainingSec > 0f)
        {
            _hitstopRemainingSec = Mathf.Max(0f, _hitstopRemainingSec - realDt);
            if (_hitstopRemainingSec <= 0f)
                ApplyVisualFreeze(false);
        }
        else
        {
            UpdateParticlesAndRings(realDt);
        }

        UpdateCameraShake(realDt);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (RingPulse ring in _rings)
        {
            float t = Mathf.Clamp(ring.Age / ring.MaxAge, 0f, 1f);
            float ease = 1f - MathF.Pow(1f - t, 3f);
            float rad = Mathf.Lerp(ring.StartRadius, ring.EndRadius, ease);
            Color col = ring.Tint with { A = ring.Tint.A * (1f - t) };
            DrawArc(ring.Position, rad, 0f, Mathf.Tau, 36, col, ring.Width * (1f - 0.5f * t), true);
        }

        foreach (BurstParticle particle in _particles)
        {
            float t = Mathf.Clamp(particle.Age / particle.MaxAge, 0f, 1f);
            float size = particle.Size * (1f - 0.65f * t);
            Color col = particle.Tint with { A = particle.Tint.A * (1f - t) };
            switch (particle.Shape)
            {
                case ParticleShape.Spark:
                {
                    Vector2 dir = particle.Velocity.LengthSquared() > 1f
                        ? particle.Velocity.Normalized()
                        : Vector2.Right;
                    DrawLine(particle.Position - dir * size * 2.2f,
                        particle.Position + dir * size * 2.2f, col, 2.2f, true);
                    break;
                }
                case ParticleShape.Shard:
                {
                    GeometricDraw.FillPolygon(this, new[]
                    {
                        particle.Position + new Vector2(0f, -size),
                        particle.Position + new Vector2(size * 0.9f, size * 0.7f),
                        particle.Position + new Vector2(-size * 0.9f, size * 0.7f)
                    }, col);
                    break;
                }
                default:
                    GeometricDraw.Diamond(this, particle.Position, size, size, col);
                    break;
            }
        }
    }

    public override void _ExitTree()
    {
        Cleanup();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Cleanup();
        base.Dispose(disposing);
    }

    private void Initialize(
        PlayerController player,
        EnemyController? enemy,
        TimeSlowManager timeSlow,
        Camera2D camera)
    {
        _player = player;
        _enemy = enemy;
        _timeSlow = timeSlow;
        _camera = camera;
        _smokeMode = OS.GetEnvironment("ACT_SMOKE") == "1";

        player.Visual?.BindTimeSlow(timeSlow);
        enemy?.Visual?.BindTimeSlow(timeSlow);
        EnsureTimeSlowOverlay();
    }

    private CanvasLayer EnsureTimeSlowOverlay()
    {
        if (_timeSlowLayer is not null && GodotObject.IsInstanceValid(_timeSlowLayer))
            return _timeSlowLayer;

        _timeSlowLayer = new CanvasLayer
        {
            Name = "TimeSlowFieldLayer",
            Layer = 5
        };
        _timeSlowOverlay = new TimeSlowOverlayControl
        {
            Name = "TimeSlowFieldOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(1280f, 720f)
        };
        _timeSlowOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _timeSlowLayer.AddChild(_timeSlowOverlay);
        AddChild(_timeSlowLayer);
        return _timeSlowLayer;
    }

    private void SubscribeSignals()
    {
        if (_boundSignals) return;
        _boundSignals = true;

        if (_player is not null)
        {
            _player.AttackStarted += OnPlayerAttackStarted;
            _player.AttackLanded += OnPlayerAttackLanded;
            _player.DodgeStarted += OnPlayerDodgeStarted;
            _player.DefenseResolved += OnPlayerDefenseResolved;
            _player.Landed += OnPlayerLanded;
        }

        if (_enemy is not null)
        {
            _enemy.StrikeActivated += OnEnemyStrikeActivated;
            _enemy.Damaged += OnEnemyDamaged;
        }
    }

    private void Cleanup()
    {
        ApplyVisualFreeze(false);

        if (_camera is not null && GodotObject.IsInstanceValid(_camera))
            _camera.Offset = Vector2.Zero;

        if (_boundSignals)
        {
            _boundSignals = false;
            if (_player is not null && GodotObject.IsInstanceValid(_player))
            {
                _player.AttackStarted -= OnPlayerAttackStarted;
                _player.AttackLanded -= OnPlayerAttackLanded;
                _player.DodgeStarted -= OnPlayerDodgeStarted;
                _player.DefenseResolved -= OnPlayerDefenseResolved;
                _player.Landed -= OnPlayerLanded;
            }
            if (_enemy is not null && GodotObject.IsInstanceValid(_enemy))
            {
                _enemy.StrikeActivated -= OnEnemyStrikeActivated;
                _enemy.Damaged -= OnEnemyDamaged;
            }
        }
    }

    private void UpdateTimeSlowField(float realDt)
    {
        float target = _timeSlow is { Active: true } ? 1f : 0f;
        _timeSlowBlend = Mathf.MoveToward(_timeSlowBlend, target, realDt * 6f);

        if (_timeSlowOverlay is null || !GodotObject.IsInstanceValid(_timeSlowOverlay))
            return;

        Vector2 vp = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);
        if (vp.X > 1f && vp.Y > 1f)
            _timeSlowOverlay.Size = vp;
        _timeSlowOverlay.Blend = _timeSlowBlend;
        _timeSlowOverlay.QueueRedraw();
    }

    private void UpdateParticlesAndRings(float realDt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            BurstParticle p = _particles[i];
            p.Age += realDt;
            if (p.Age >= p.MaxAge)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.Position += p.Velocity * realDt;
            p.Velocity *= Mathf.Max(0f, 1f - 3.5f * realDt);
            _particles[i] = p;
        }

        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            RingPulse r = _rings[i];
            r.Age += realDt;
            if (r.Age >= r.MaxAge)
                _rings.RemoveAt(i);
            else
                _rings[i] = r;
        }
    }

    private void UpdateCameraShake(float realDt)
    {
        if (_camera is null || !GodotObject.IsInstanceValid(_camera))
            return;

        if (_shakeIntensity > 0.1f)
        {
            _camera.Offset = new Vector2(
                _rng.RandfRange(-_shakeIntensity, _shakeIntensity),
                _rng.RandfRange(-_shakeIntensity, _shakeIntensity));
            if (_hitstopRemainingSec <= 0f)
                _shakeIntensity = Mathf.MoveToward(_shakeIntensity, 0f, realDt * 42f);
        }
        else
        {
            _shakeIntensity = 0f;
            _camera.Offset = Vector2.Zero;
        }
    }

    private void TriggerHitstop(float durationSeconds)
    {
        // Visual-only freeze (~50ms); never touch Engine.TimeScale and auto-skip in ACT_SMOKE=1.
        if (_smokeMode) return;
        _hitstopRemainingSec = Mathf.Max(_hitstopRemainingSec, durationSeconds);
        ApplyVisualFreeze(true);
    }

    private void ApplyVisualFreeze(bool frozen)
    {
        if (_player is not null && GodotObject.IsInstanceValid(_player))
            _player.Visual?.SetVisualFreeze(frozen);
        if (_enemy is not null && GodotObject.IsInstanceValid(_enemy))
            _enemy.Visual?.SetVisualFreeze(frozen);
    }

    private void SpawnBurst(Vector2 origin, int count, Color tint, float maxSpeed, ParticleShape shape)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = _rng.RandfRange(0f, Mathf.Tau);
            float speed = _rng.RandfRange(maxSpeed * 0.35f, maxSpeed);
            _particles.Add(new BurstParticle
            {
                Position = origin,
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                Size = _rng.RandfRange(4f, 9f),
                Age = 0f,
                MaxAge = _rng.RandfRange(0.18f, 0.38f),
                Tint = tint,
                Shape = shape
            });
        }
    }

    private void SpawnRing(Vector2 origin, float startRadius, float endRadius, float durationSeconds, Color tint, float width)
    {
        _rings.Add(new RingPulse
        {
            Position = origin,
            StartRadius = startRadius,
            EndRadius = endRadius,
            Age = 0f,
            MaxAge = durationSeconds,
            Tint = tint,
            Width = width
        });
    }

    private void OnPlayerAttackStarted(PlayerAttackCue cue)
    {
        Vector2 dir = cue.Direction.LengthSquared() > 0.01f
            ? cue.Direction.Normalized()
            : new Vector2(_player?.Facing ?? 1, 0f);
        SpawnBurst(cue.Origin + dir * 36f + new Vector2(0f, -4f), 8, RuinGeometryPalette.PlayerBlade, 240f, ParticleShape.Spark);
    }

    private void OnPlayerAttackLanded(PlayerHitCue cue)
    {
        TriggerHitstop(0.05f);
        SpawnBurst(cue.HitPosition, 6, Colors.White, 240f, ParticleShape.Spark);
    }

    private void OnPlayerDodgeStarted(PlayerDodgeCue cue)
    {
        SpawnRing(cue.Origin, 10f, 44f, 0.22f, DodgeTeal, 2.5f);
    }

    private void OnPlayerLanded(Vector2 pos)
    {
        SpawnBurst(pos + new Vector2(0f, 22f), 5, RuinGeometryPalette.PlatformTopLit, 110f, ParticleShape.Diamond);
    }

    private void OnPlayerDefenseResolved(PlayerDefenseCue cue)
    {
        int facing = _player?.Facing ?? 1;
        switch (cue.Result)
        {
            case DefenseResult.PerfectBlocked:
            {
                TriggerHitstop(0.055f);
                _shakeIntensity = Mathf.Max(_shakeIntensity, 9f);
                Vector2 blockPos = cue.Position + new Vector2(facing * 20f, 0f);
                SpawnRing(blockPos, 12f, 95f, 0.34f, RuinGeometryPalette.PlayerBlade, 5f);
                SpawnBurst(blockPos, 18, RuinGeometryPalette.PlayerBlade, 360f, ParticleShape.Diamond);
                break;
            }
            case DefenseResult.PerfectDodged:
            {
                TriggerHitstop(0.045f);
                SpawnRing(cue.Position, 12f, 88f, 0.32f, DodgeTeal, 4.5f);
                SpawnBurst(cue.Position, 16, DodgeTeal, 340f, ParticleShape.Spark);
                break;
            }
            case DefenseResult.Blocked when !cue.GuardBroken:
            {
                _shakeIntensity = Mathf.Max(_shakeIntensity, 4f);
                Vector2 guardPos = cue.Position + new Vector2(facing * 20f, 0f);
                SpawnBurst(guardPos, 8, RuinGeometryPalette.PlayerShieldEdge, 220f, ParticleShape.Shard);
                break;
            }
            case DefenseResult.Hit:
            case DefenseResult.Blocked when cue.GuardBroken:
            {
                TriggerHitstop(0.05f);
                _shakeIntensity = Mathf.Max(_shakeIntensity, 10f);
                SpawnBurst(cue.Position, 12, HitCrimson, 280f, ParticleShape.Shard);
                break;
            }
        }
    }

    private void OnEnemyStrikeActivated(EnemyStrikeCue cue)
    {
        _shakeIntensity = Mathf.Max(_shakeIntensity, 8f);
        Color col = RuinGeometryPalette.ForTelegraph(cue.Attack.Kind);
        float endRadius = cue.Attack.Kind == BossAttackKind.DelayedArea ? cue.Attack.HazardRadius : 120f;
        SpawnRing(cue.Origin, 20f, endRadius, 0.28f, col, 4f);
    }

    private void OnEnemyDamaged(EnemyDamagedCue cue)
    {
        TriggerHitstop(cue.Defeated ? 0.065f : 0.05f);
        _shakeIntensity = Mathf.Max(_shakeIntensity, cue.Defeated ? 11f : 6f);
        int facing = _player is not null && GodotObject.IsInstanceValid(_player) && _player.GlobalPosition.X >= cue.Position.X
            ? 1
            : -1;
        SpawnBurst(
            cue.Position + new Vector2(-facing * 18f, -8f),
            cue.Defeated ? 24 : 14,
            RuinGeometryPalette.TelegraphNormal,
            320f,
            ParticleShape.Shard);
        if (cue.Defeated)
            SpawnRing(cue.Position, 16f, 140f, 0.40f, RuinGeometryPalette.TelegraphNormal, 5f);
    }

    private sealed partial class TimeSlowOverlayControl : Control
    {
        public float Blend { get; set; }

        public override void _Draw()
        {
            if (Blend <= 0.01f) return;

            Vector2 vp = GetViewportRect().Size;
            if (vp.X <= 1f || vp.Y <= 1f)
                vp = new Vector2(1280f, 720f);

            float a = Mathf.Clamp(Blend, 0f, 1f);
            DrawRect(new Rect2(Vector2.Zero, vp), RuinGeometryPalette.TimeSlowField with { A = 0.22f * a });

            float bandWidth = 18f * a;
            Color stripColor = RuinGeometryPalette.TimeSlowEdge with { A = 0.14f * a };
            DrawRect(new Rect2(0f, 0f, vp.X, bandWidth), stripColor);
            DrawRect(new Rect2(0f, vp.Y - bandWidth, vp.X, bandWidth), stripColor);
            DrawRect(new Rect2(10f, 10f, vp.X - 20f, vp.Y - 20f),
                RuinGeometryPalette.TimeSlowEdge with { A = 0.42f * a }, false, 2f);
        }
    }
}
