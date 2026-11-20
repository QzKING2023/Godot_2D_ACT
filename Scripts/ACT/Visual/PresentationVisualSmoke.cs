using System;
using Godot;
using ACTSpike.ACT.Arena;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Runtime smoke contract checks for Ticket #3 procedural visuals, telegraphs,
/// combat feedback, and time-slow visual field.
/// </summary>
public static partial class PresentationVisualSmoke
{
    public static void Check(
        SmokeTest host,
        PlayerController player,
        EnemyController enemy,
        TimeSlowManager timeSlow,
        Action<string> fail)
    {
        if (!CheckPlayerVisualMounted(player, fail))
            return;
        if (!CheckEnemyVisualMounted(enemy, fail))
            return;
        if (!CheckCombatFeedbackLayerMounted(host, out CombatFeedbackLayer? feedback, fail) || feedback is null)
            return;
        if (!CheckNullEnemyAttachContract(player, timeSlow, fail))
            return;
        if (!CheckFeedbackCueResponseContract(host, timeSlow, fail))
            return;

        host.AddChild(new RuntimeVisualObserver(player, feedback, timeSlow, fail)
        {
            Name = "PresentationVisualRuntimeObserver"
        });
        GD.Print("[Smoke] PASS presentation visuals (PlayerVisual + EnemyVisual + CombatFeedbackLayer + null-enemy Attach)");
    }

    private static bool CheckPlayerVisualMounted(PlayerController player, Action<string> fail)
    {
        foreach (Node child in player.GetChildren())
        {
            if (child is ColorRect)
            {
                fail($"legacy ColorRect '{child.Name}' still attached to Player");
                return false;
            }
        }

        if (player.Visual is null || player.GetNodeOrNull("Visual") is not PlayerVisual)
        {
            fail("PlayerVisual not mounted as Player/Visual");
            return false;
        }

        if (!player.Visual.IsTimeSlowBound)
        {
            fail("PlayerVisual is not bound to TimeSlowManager");
            return false;
        }

        return true;
    }

    private static bool CheckEnemyVisualMounted(EnemyController enemy, Action<string> fail)
    {
        foreach (Node child in enemy.GetChildren())
        {
            if (child is ColorRect or Label or Polygon2D or Line2D)
            {
                fail($"legacy placeholder node '{child.Name}' ({child.GetType().Name}) still attached to Enemy");
                return false;
            }
        }

        if (enemy.Visual is null || enemy.GetNodeOrNull("Visual") is not EnemyVisual)
        {
            fail("EnemyVisual not mounted as Enemy/Visual");
            return false;
        }

        if (enemy.GetNodeOrNull<Area2D>("Hurtbox") is null || enemy.GetNodeOrNull<CollisionShape2D>("BodyShape") is null)
        {
            fail("EnemyController missing expected BodyShape/Hurtbox child nodes");
            return false;
        }

        if (!enemy.Visual.IsPlayerBound || enemy.Visual.ZIndex != 1)
        {
            fail($"EnemyVisual binding/ZIndex invalid (bound={enemy.Visual.IsPlayerBound}, ZIndex={enemy.Visual.ZIndex})");
            return false;
        }

        return true;
    }

    private static bool CheckCombatFeedbackLayerMounted(
        SmokeTest host,
        out CombatFeedbackLayer? feedback,
        Action<string> fail)
    {
        Node? stage = host.GetParent();
        feedback = stage?.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer");
        if (feedback is null)
        {
            fail("CombatFeedbackLayer not mounted on stage");
            return false;
        }

        CanvasLayer? fieldLayer = feedback.GetNodeOrNull<CanvasLayer>("TimeSlowFieldLayer");
        if (fieldLayer is null || fieldLayer.Layer != 5)
        {
            fail($"TimeSlowFieldLayer missing or wrong Layer ({fieldLayer?.Layer.ToString() ?? "null"})");
            return false;
        }

        Control? overlay = fieldLayer.GetNodeOrNull<Control>("TimeSlowFieldOverlay");
        if (overlay is null || overlay.MouseFilter != Control.MouseFilterEnum.Ignore)
        {
            fail("TimeSlowFieldOverlay missing or MouseFilter != Ignore");
            return false;
        }

        return true;
    }

    private static bool CheckNullEnemyAttachContract(
        PlayerController player,
        TimeSlowManager timeSlow,
        Action<string> fail)
    {
        var detachedStage = new Node2D { Name = "DetachedStageProbe" };
        var detachedCamera = new Camera2D { Name = "DetachedCameraProbe" };
        detachedStage.AddChild(detachedCamera);

        try
        {
            CombatFeedbackLayer attached = CombatFeedbackLayer.Attach(
                detachedStage,
                player,
                enemy: null,
                timeSlow,
                detachedCamera);
            if (attached.TimeSlowFieldLayer.Layer != 5)
            {
                fail("CombatFeedbackLayer.Attach(enemy: null) did not configure TimeSlowFieldLayer(Layer=5)");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            fail($"CombatFeedbackLayer.Attach(enemy: null) threw {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            detachedStage.Free();
        }
    }

    private static bool CheckFeedbackCueResponseContract(
        SmokeTest host,
        TimeSlowManager timeSlow,
        Action<string> fail)
    {
        var probeStage = new Node2D { Name = "FeedbackCueProbeStage" };
        var floor = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(260f, 580f) };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(400f, 40f) } });
        probeStage.AddChild(floor);

        var windows = new CombatWindowConfig
        {
            PerfectGuardSeconds = -0.1f,
            PerfectDodgeSeconds = 0.5f,
            HitstunSeconds = 0f,
            GuardDamagePerBlockedHit = 25f
        };
        var probePlayer = new PlayerController
        {
            Name = "CueProbePlayer",
            Position = new Vector2(260f, 537f),
            Windows = windows,
            TimeSlow = timeSlow.Config
        };
        var probeCam = new Camera2D { Name = "CueProbeCamera" };
        probePlayer.AddChild(probeCam);
        probeStage.AddChild(probePlayer);

        host.AddChild(probeStage);
        CombatFeedbackLayer probeFeedback = CombatFeedbackLayer.Attach(probeStage, probePlayer, null, timeSlow, probeCam);
        try
        {
            // Settle onto floor so block action is valid.
            probePlayer._PhysicsProcess(0.05);

            // 1. Normal Blocked (not guard broken): ring + particles + shake.
            int ringsBefore = probeFeedback.ActiveRingCount;
            int particlesBefore = probeFeedback.ActiveParticleCount;
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "block", Pressed = true });
            Godot.Input.FlushBufferedEvents();
            probePlayer._Process(0.016);
            probePlayer._PhysicsProcess(0.016);
            probePlayer.TryReceiveEnemyAttack(15f, BossAttackKind.Normal, out AttackResolution blockRes);
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "block", Pressed = false });
            Godot.Input.FlushBufferedEvents();
            probePlayer._PhysicsProcess(0.016);
            if (blockRes.Result != DefenseResult.Blocked ||
                probeFeedback.ActiveRingCount <= ringsBefore ||
                probeFeedback.ActiveParticleCount <= particlesBefore ||
                probeFeedback.ShakeIntensity < 4f)
            {
                fail($"Blocked feedback contract mismatch: result={blockRes.Result} rings={probeFeedback.ActiveRingCount - ringsBefore} " +
                     $"particles={probeFeedback.ActiveParticleCount - particlesBefore} shake={probeFeedback.ShakeIntensity:0.0}");
                return false;
            }

            // 2. GuardBroken (with GuardDamagePerBlockedHit = 120): ring + particles + shake >= 10f.
            windows.GuardDamagePerBlockedHit = 120f;
            ringsBefore = probeFeedback.ActiveRingCount;
            particlesBefore = probeFeedback.ActiveParticleCount;
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "block", Pressed = true });
            Godot.Input.FlushBufferedEvents();
            probePlayer._Process(0.016);
            probePlayer._PhysicsProcess(0.016);
            probePlayer.TryReceiveEnemyAttack(15f, BossAttackKind.Normal, out AttackResolution guardBreakRes);
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "block", Pressed = false });
            Godot.Input.FlushBufferedEvents();
            probePlayer._PhysicsProcess(0.016);
            if (guardBreakRes.Result != DefenseResult.Blocked ||
                probeFeedback.ActiveRingCount <= ringsBefore ||
                probeFeedback.ActiveParticleCount <= particlesBefore ||
                probeFeedback.ShakeIntensity < 10f)
            {
                fail($"GuardBroken feedback contract mismatch: result={guardBreakRes.Result} rings={probeFeedback.ActiveRingCount - ringsBefore} " +
                     $"particles={probeFeedback.ActiveParticleCount - particlesBefore} shake={probeFeedback.ShakeIntensity:0.0}");
                return false;
            }

            // 3. Hit: ring + particles + shake >= 10f.
            probePlayer.ResetTo(new Vector2(260f, 537f));
            probePlayer._PhysicsProcess(0.05);
            ringsBefore = probeFeedback.ActiveRingCount;
            particlesBefore = probeFeedback.ActiveParticleCount;
            probePlayer.TryReceiveEnemyAttack(15f, BossAttackKind.Normal, out AttackResolution hitRes);
            if (hitRes.Result != DefenseResult.Hit ||
                probeFeedback.ActiveRingCount <= ringsBefore ||
                probeFeedback.ActiveParticleCount <= particlesBefore ||
                probeFeedback.ShakeIntensity < 10f)
            {
                fail($"Hit feedback contract mismatch: result={hitRes.Result} rings={probeFeedback.ActiveRingCount - ringsBefore} " +
                     $"particles={probeFeedback.ActiveParticleCount - particlesBefore} shake={probeFeedback.ShakeIntensity:0.0}");
                return false;
            }

            return true;
        }
        finally
        {
            Godot.Input.ParseInputEvent(new InputEventAction { Action = "block", Pressed = false });
            Godot.Input.FlushBufferedEvents();
            host.RemoveChild(probeStage);
            probeStage.Free();
        }
    }

    private sealed partial class RuntimeVisualObserver : Node
    {
        private readonly PlayerController _player;
        private readonly CombatFeedbackLayer _feedback;
        private readonly TimeSlowManager _timeSlow;
        private readonly Action<string> _fail;

        private bool _sawParticles;
        private bool _sawRings;
        private bool _sawAttackLandedRing;
        private bool _sawPerfectDodgeShake;
        private bool _sawHitRing;
        private bool _sawAfterimages;
        private float _peakTimeSlowBlend;
        private bool _sawTimeSlowFieldFade;
        private bool _timeScaleCorrupted;
        private bool _hitstopActiveInSmoke;
        private bool _verified;

        public RuntimeVisualObserver(
            PlayerController player,
            CombatFeedbackLayer feedback,
            TimeSlowManager timeSlow,
            Action<string> fail)
        {
            _player = player;
            _feedback = feedback;
            _timeSlow = timeSlow;
            _fail = fail;
        }

        public override void _Ready()
        {
            _player.AttackLanded += OnAttackLanded;
            _player.DefenseResolved += OnDefenseResolved;
        }

        public override void _Process(double delta)
        {
            if (_feedback.ActiveParticleCount > 0) _sawParticles = true;
            if (_feedback.ActiveRingCount > 0) _sawRings = true;
            if (_player.Visual.AfterimageCount > 0) _sawAfterimages = true;

            if (_timeSlow.Active)
            {
                _peakTimeSlowBlend = Mathf.Max(_peakTimeSlowBlend, _feedback.TimeSlowBlend);
            }
            else
            {
                if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1f))
                    _timeScaleCorrupted = true;
                if (_peakTimeSlowBlend > 0.05f && _feedback.TimeSlowBlend < _peakTimeSlowBlend)
                    _sawTimeSlowFieldFade = true;
            }

            if (_feedback.IsHitstopActive)
                _hitstopActiveInSmoke = true;
        }

        public override void _ExitTree()
        {
            if (GodotObject.IsInstanceValid(_player))
            {
                _player.AttackLanded -= OnAttackLanded;
                _player.DefenseResolved -= OnDefenseResolved;
            }
        }

        private void OnAttackLanded(PlayerHitCue _)
        {
            if (_feedback.ActiveRingCount > 0 && _feedback.ActiveParticleCount > 0 && _feedback.ShakeIntensity > 0f)
                _sawAttackLandedRing = true;
        }

        private void OnDefenseResolved(PlayerDefenseCue cue)
        {
            if (cue.Result == DefenseResult.PerfectDodged &&
                _feedback.ActiveRingCount > 0 &&
                _feedback.ActiveParticleCount > 0 &&
                _feedback.ShakeIntensity >= 6f)
            {
                _sawPerfectDodgeShake = true;
            }

            if (cue.Result == DefenseResult.Hit &&
                _feedback.ActiveRingCount > 0 &&
                _feedback.ActiveParticleCount > 0 &&
                _feedback.ShakeIntensity >= 10f)
            {
                _sawHitRing = true;
            }

            if (!cue.IsDown || _verified) return;
            _verified = true;

            bool ok = _sawParticles && _sawRings && _sawAttackLandedRing &&
                      _sawPerfectDodgeShake && _sawHitRing && _sawAfterimages &&
                      _peakTimeSlowBlend > 0.05f && _sawTimeSlowFieldFade &&
                      !_timeScaleCorrupted && !_hitstopActiveInSmoke;

            if (ok)
            {
                GD.Print("[Smoke] PASS presentation runtime VFX (particles, rings, shake, afterimages, TimeSlowField fade & TimeScale isolation)");
            }
            else
            {
                _fail($"runtime VFX mismatch: particles={_sawParticles} rings={_sawRings} atkRing={_sawAttackLandedRing} " +
                      $"pDodgeShake={_sawPerfectDodgeShake} hitRing={_sawHitRing} afterimages={_sawAfterimages} " +
                      $"slowPeak={_peakTimeSlowBlend:0.00} slowFade={_sawTimeSlowFieldFade} " +
                      $"timeScaleCorrupted={_timeScaleCorrupted} hitstopInSmoke={_hitstopActiveInSmoke}");
            }
        }
    }
}
