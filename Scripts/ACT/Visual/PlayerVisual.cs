using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Variant A (硬边剪影切面) procedural player renderer driven exclusively by
/// PlayerController read-only cues and TimeSlowManager state.
/// </summary>
public partial class PlayerVisual : Node2D
{
    private readonly record struct AfterimageSample(
        Vector2 WorldPosition,
        int Facing,
        PlayerState State,
        float Age,
        float MaxAge);

    private static readonly Vector2[] BackFacetPoints =
    {
        new(-15f, 23f), new(-8f, -11f), new(2f, -13f), new(2f, 23f)
    };

    private static readonly Vector2[] LitFacetPoints =
    {
        new(2f, -13f), new(13f, -7f), new(11f, 23f), new(2f, 23f)
    };

    private static readonly Vector2[] ShoulderTrimPoints =
    {
        new(-12f, -10f), new(10f, -11f), new(13f, -4f), new(-18f, -2f)
    };

    private static readonly Vector2[] HeadPoints =
    {
        new(-7f, -14f), new(0f, -25f), new(8f, -19f), new(6f, -11f)
    };

    private static readonly Vector2[] BladePoints =
    {
        new(0f, -20f), new(5f, 0f), new(0f, 14f), new(-4f, 0f)
    };

    private static readonly Vector2[] ShieldPoints =
    {
        new(0f, -28f), new(10f, -14f), new(10f, 14f), new(0f, 28f), new(-4f, 0f)
    };

    private readonly List<AfterimageSample> _afterimages = new();

    private PlayerController? _player;
    private TimeSlowManager? _timeSlow;

    private float _realElapsedSec;
    private float _lastRealSec = -1f;
    private float _slashTimer;
    private float _slashDuration = 0.20f;
    private bool _slashIsSkill;
    private Vector2 _slashDirection = Vector2.Right;
    private float _hitFlash;
    private float _shieldPulse;
    private float _landSquash;
    private float _afterimageCooldown;
    private Vector2 _prevGlobalPos;
    private bool _wasDown;
    private bool _visualFrozen;

    public int AfterimageCount => _afterimages.Count;
    public bool IsTimeSlowBound => _timeSlow is not null;

    public override void _Ready()
    {
        ZIndex = 2;
        if (_player is null && GetParent() is PlayerController parent)
            Bind(parent);
    }

    public void Bind(PlayerController player)
    {
        if (ReferenceEquals(_player, player))
            return;

        UnbindPlayer();
        _player = player;
        _prevGlobalPos = player.GlobalPosition;
        _wasDown = player.IsDown;

        _player.AttackStarted += OnAttackStarted;
        _player.AttackLanded += OnAttackLanded;
        _player.DodgeStarted += OnDodgeStarted;
        _player.DefenseResolved += OnDefenseResolved;
        _player.Landed += OnLanded;
    }

    public void BindTimeSlow(TimeSlowManager? timeSlow)
    {
        _timeSlow = timeSlow;
    }

    /// <summary>Called by CombatFeedbackLayer during visual-only hitstop without touching Engine.TimeScale.</summary>
    public void SetVisualFreeze(bool frozen) => _visualFrozen = frozen;

    public override void _Process(double delta)
    {
        if (_player is null) return;

        float nowReal = Time.GetTicksMsec() / 1000f;
        float realDt = _lastRealSec < 0f ? (float)delta : Mathf.Clamp(nowReal - _lastRealSec, 0f, 0.05f);
        _lastRealSec = nowReal;

        // Clear stale ghosts/flashes when round resets or actor teleports.
        Vector2 currentPos = _player.GlobalPosition;
        if ((_wasDown && !_player.IsDown) || _prevGlobalPos.DistanceSquaredTo(currentPos) > 250f * 250f)
        {
            _afterimages.Clear();
            _slashTimer = 0f;
            _hitFlash = 0f;
            _shieldPulse = 0f;
            _landSquash = 0f;
        }
        _prevGlobalPos = currentPos;
        _wasDown = _player.IsDown;

        if (!_visualFrozen)
        {
            _realElapsedSec += realDt;
            _slashTimer = Mathf.Max(0f, _slashTimer - realDt);
            _hitFlash = Mathf.Max(0f, _hitFlash - realDt);
            _shieldPulse = Mathf.Max(0f, _shieldPulse - realDt);
            _landSquash = Mathf.Max(0f, _landSquash - realDt * 5.5f);

            UpdateAfterimages(realDt, currentPos);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_player is null) return;

        // Draw world-space afterimages converted into local player space.
        foreach (AfterimageSample sample in _afterimages)
        {
            float alpha = (1f - sample.Age / sample.MaxAge) * 0.42f;
            var ghostColor = new Color(0.45f, 0.92f, 0.88f, alpha);
            Vector2 localOrigin = ToLocal(sample.WorldPosition);
            DrawPlayerFacet(localOrigin, sample.Facing, sample.State, squash: 0f, isDown: false, ghostColor, ghost: true);
        }

        Color flashTint = _hitFlash > 0f ? Colors.White : new Color("e8edf2");
        DrawPlayerFacet(Vector2.Zero, _player.Facing, _player.State, _landSquash, _player.IsDown, flashTint, ghost: false);
        DrawCombatOverlay();
    }

    public override void _ExitTree()
    {
        UnbindPlayer();
    }

    private void UnbindPlayer()
    {
        if (_player is null) return;
        _player.AttackStarted -= OnAttackStarted;
        _player.AttackLanded -= OnAttackLanded;
        _player.DodgeStarted -= OnDodgeStarted;
        _player.DefenseResolved -= OnDefenseResolved;
        _player.Landed -= OnLanded;
        _player = null;
    }

    private void UpdateAfterimages(float realDt, Vector2 currentPos)
    {
        _afterimageCooldown -= realDt;
        bool wantTrail = !_player!.IsDown &&
                         (_player.State is PlayerState.Dodge or PlayerState.Skill || (_timeSlow?.Active ?? false));
        if (wantTrail && _afterimageCooldown <= 0f)
        {
            _afterimageCooldown = 0.035f;
            _afterimages.Add(new AfterimageSample(currentPos, _player.Facing, _player.State, 0f, 0.24f));
        }

        for (int i = _afterimages.Count - 1; i >= 0; i--)
        {
            AfterimageSample sample = _afterimages[i];
            float age = sample.Age + realDt;
            if (age >= sample.MaxAge)
                _afterimages.RemoveAt(i);
            else
                _afterimages[i] = sample with { Age = age };
        }
    }

    private void DrawPlayerFacet(
        Vector2 basePos,
        int facing,
        PlayerState state,
        float squash,
        bool isDown,
        Color tint,
        bool ghost)
    {
        float sx = 1f + squash * 0.22f;
        float sy = 1f - squash * 0.18f;
        float tilt;

        if (isDown)
        {
            tilt = -facing * 1.22f;
            sx = 1.05f;
            sy = 0.85f;
        }
        else
        {
            tilt = state switch
            {
                PlayerState.Move => facing * 0.16f,
                PlayerState.Dodge => facing * 0.42f,
                PlayerState.Skill => facing * 0.50f,
                PlayerState.Attack => facing * 0.22f,
                PlayerState.Block => -facing * 0.10f,
                PlayerState.Hitstun => -facing * 0.28f,
                _ => 0f
            };
            if (state is PlayerState.Dodge or PlayerState.Skill)
            {
                sx *= 1.32f;
                sy *= 0.78f;
            }
            else if (state == PlayerState.Jump)
            {
                sx *= 0.94f;
                sy *= 1.08f;
            }
        }

        float bob = !ghost && !isDown && state == PlayerState.Idle
            ? Mathf.Sin(_realElapsedSec * 4f) * 1.6f
            : 0f;
        float downSink = isDown ? 13f : 0f;
        Vector2 origin = basePos + new Vector2(0f, 23f * (1f - sy) + bob + downSink);

        bool whiteFlash = !ghost && tint == Colors.White;
        Color backColor = ghost
            ? tint
            : whiteFlash
                ? Colors.White
                : isDown ? RuinGeometryPalette.PlayerShadowFacet.Darkened(0.28f) : RuinGeometryPalette.PlayerShadowFacet;
        Color litColor = ghost
            ? tint
            : whiteFlash
                ? Colors.White
                : isDown ? RuinGeometryPalette.PlayerLitFacet.Darkened(0.42f) : RuinGeometryPalette.PlayerLitFacet;
        Color headColor = ghost
            ? tint
            : whiteFlash
                ? Colors.White
                : isDown ? RuinGeometryPalette.PlayerHead.Darkened(0.32f) : RuinGeometryPalette.PlayerHead;

        Vector2[] backFacet = GeometricDraw.TransformPoints(BackFacetPoints, origin, facing * sx, sy, tilt);
        Vector2[] litFacet = GeometricDraw.TransformPoints(LitFacetPoints, origin, facing * sx, sy, tilt);
        GeometricDraw.FillPolygon(this, backFacet, backColor);
        GeometricDraw.FillPolygon(this, litFacet, litColor);

        if (!ghost)
        {
            Color trimColor = whiteFlash
                ? Colors.White
                : isDown ? RuinGeometryPalette.PlayerWarmTrim.Darkened(0.35f) : RuinGeometryPalette.PlayerWarmTrim;
            Vector2[] scarf = GeometricDraw.TransformPoints(ShoulderTrimPoints, origin, facing * sx, sy, tilt);
            GeometricDraw.FillPolygon(this, scarf, trimColor);
        }

        Vector2[] head = GeometricDraw.TransformPoints(HeadPoints, origin, facing * sx, sy, tilt);
        GeometricDraw.FillPolygon(this, head, headColor);

        if (!ghost)
        {
            float bladeOffX = isDown ? 8f : state == PlayerState.Attack ? 28f : -14f;
            float bladeOffY = isDown ? 8f : -2f;
            float bladeRot = isDown ? facing * 1.45f : state == PlayerState.Attack ? facing * 0.9f : -facing * 0.35f;
            Color bladeColor = whiteFlash
                ? Colors.White
                : isDown ? RuinGeometryPalette.PlayerBlade.Darkened(0.45f) : RuinGeometryPalette.PlayerBlade;

            Vector2[] blade = GeometricDraw.TransformPoints(
                BladePoints,
                origin + new Vector2(facing * bladeOffX, bladeOffY),
                facing,
                1f,
                bladeRot);
            GeometricDraw.FillPolygon(this, blade, bladeColor);
        }
    }

    private void DrawCombatOverlay()
    {
        if (_player is null || _player.IsDown) return;

        if (_slashTimer > 0f)
        {
            float t = Mathf.Clamp(_slashTimer / _slashDuration, 0f, 1f);
            float radius = _slashIsSkill ? 68f : 56f;
            Vector2 dir = _slashDirection.LengthSquared() > 0.01f
                ? _slashDirection.Normalized()
                : new Vector2(_player.Facing, 0f);
            Vector2 center = _slashIsSkill ? dir * 8f : new Vector2(_player.Facing * 34f, -4f);
            float baseAng = dir.Angle();
            DrawArc(center, radius, baseAng - 1.15f, baseAng + 1.05f, 20, new Color(1f, 0.90f, 0.55f, t), 14f * t, true);
            DrawArc(center, radius - 10f, baseAng - 0.95f, baseAng + 0.85f, 16, new Color(1f, 1f, 1f, t * 0.9f), 5f * t, true);
        }

        if (_player.State == PlayerState.Block)
        {
            Vector2 shieldPos = new Vector2(_player.Facing * 24f, 0f);
            Vector2[] shield = GeometricDraw.TransformPoints(ShieldPoints, shieldPos, _player.Facing, 1f);
            float pulseBoost = _shieldPulse > 0f ? 0.30f : 0f;
            Color fill = RuinGeometryPalette.PlayerShieldFill with
            {
                A = Mathf.Min(0.85f, RuinGeometryPalette.PlayerShieldFill.A + pulseBoost)
            };
            Color edge = _shieldPulse > 0f ? RuinGeometryPalette.PlayerBlade : RuinGeometryPalette.PlayerShieldEdge;
            GeometricDraw.FillAndStrokePolygon(this, shield, fill, edge, _shieldPulse > 0f ? 3.5f : 2.5f);
        }
    }

    private void OnAttackStarted(PlayerAttackCue cue)
    {
        _slashDuration = Mathf.Max(0.05f, cue.DurationSeconds);
        _slashTimer = _slashDuration;
        _slashIsSkill = cue.IsSkill;
        _slashDirection = cue.Direction.LengthSquared() > 0.01f
            ? cue.Direction.Normalized()
            : new Vector2(_player?.Facing ?? 1, 0f);
    }

    private void OnAttackLanded(PlayerHitCue cue)
    {
        _slashTimer = Mathf.Max(_slashTimer, 0.08f);
    }

    private void OnDodgeStarted(PlayerDodgeCue cue)
    {
        _afterimages.Add(new AfterimageSample(cue.Origin, cue.Direction, PlayerState.Dodge, 0f, 0.24f));
        _afterimageCooldown = 0.035f;
    }

    private void OnDefenseResolved(PlayerDefenseCue cue)
    {
        if (cue.DamageToHp > 0f || cue.GuardBroken)
            _hitFlash = 0.16f;
        if (cue.Result is DefenseResult.Blocked or DefenseResult.PerfectBlocked)
            _shieldPulse = 0.18f;
    }

    private void OnLanded(Vector2 _)
    {
        _landSquash = 1f;
    }
}
