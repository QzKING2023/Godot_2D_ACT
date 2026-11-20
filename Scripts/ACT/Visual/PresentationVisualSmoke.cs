using System;
using System.Reflection;
using Godot;
using ACTSpike.ACT.Arena;
using ACTSpike.ACT.Combat;
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
        if (enemy.GetType().GetMethod("_Draw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) is not null)
        {
            fail("EnemyController still declares legacy placeholder _Draw()");
            return false;
        }

        if (enemy.Visual is null || enemy.GetNodeOrNull("Visual") is not EnemyVisual)
        {
            fail("EnemyVisual not mounted as Enemy/Visual");
            return false;
        }

        if (!enemy.Visual.IsPlayerBound)
        {
            fail("EnemyVisual is not bound to PlayerController");
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

    private sealed partial class RuntimeVisualObserver : Node
    {
        private readonly PlayerController _player;
        private readonly CombatFeedbackLayer _feedback;
        private readonly TimeSlowManager _timeSlow;
        private readonly Action<string> _fail;

        private bool _sawParticles;
        private bool _sawRings;
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
                _player.DefenseResolved -= OnDefenseResolved;
        }

        private void OnDefenseResolved(PlayerDefenseCue cue)
        {
            if (!cue.IsDown || _verified) return;
            _verified = true;

            bool ok = _sawParticles && _sawRings && _sawAfterimages &&
                      _peakTimeSlowBlend > 0.05f && _sawTimeSlowFieldFade &&
                      !_timeScaleCorrupted && !_hitstopActiveInSmoke;

            if (ok)
            {
                GD.Print("[Smoke] PASS presentation runtime VFX (particles, rings, afterimages, TimeSlowField fade & TimeScale isolation)");
            }
            else
            {
                _fail($"runtime VFX mismatch: particles={_sawParticles} rings={_sawRings} afterimages={_sawAfterimages} " +
                      $"slowPeak={_peakTimeSlowBlend:0.00} slowFade={_sawTimeSlowFieldFade} " +
                      $"timeScaleCorrupted={_timeScaleCorrupted} hitstopInSmoke={_hitstopActiveInSmoke}");
            }
        }
    }
}
