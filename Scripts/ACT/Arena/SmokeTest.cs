using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Opt-in automated smoke test (run with environment ACT_SMOKE=1, headless).
/// Injects input actions via Input.ParseInputEvent so the real player code
/// path runs. Prints PASS/FAIL lines and exits non-zero on failure.
/// Not part of normal gameplay; costs nothing when the env var is unset.
/// </summary>
public partial class SmokeTest : Node
{
    private const float TapSeconds = 0.05f;
    private const float DodgeLeadBeforeImpact = 0.20f; // inside the 0.25s perfect window

    private enum Phase { Setup, AttackTap, AwaitAttackHit, AwaitTelegraph, AwaitDodgeImpact, TimeSlowHold, TimeSlowRelease }

    public PlayerController Player { get; set; } = null!;
    public EnemyController Enemy { get; set; } = null!;
    public TimeSlowManager TimeSlow { get; set; } = null!;

    private Phase _phase = Phase.Setup;
    private float _phaseStart;
    private float _telegraphStartedAt = -1f;
    private float _tapReleaseAt = -1f;
    private float _enemyHpBeforeAttack;
    private float _playerHpBeforeStrike;
    private readonly List<string> _failures = new();
    private readonly List<string> _feedback = new();

    public override void _Ready()
    {
        Player.CombatFeedback += line => _feedback.Add(line);
        GD.Print("[Smoke] running (ACT_SMOKE=1)");
    }

    private static void SetAction(string action, bool pressed)
        => Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });

    private static float Now => Time.GetTicksMsec() / 1000f;

    public override void _Process(double delta)
    {
        float now = Now;
        // Release any one-shot tap once its short hold elapses.
        if (_tapReleaseAt >= 0 && now >= _tapReleaseAt)
        {
            if (Godot.Input.IsActionPressed("attack")) SetAction("attack", false);
            if (Godot.Input.IsActionPressed("dodge")) SetAction("dodge", false);
            _tapReleaseAt = -1;
        }

        switch (_phase)
        {
            case Phase.Setup:
                Player.Position = Enemy.Position - new Vector2(110, 0);
                _enemyHpBeforeAttack = Enemy.Hp;
                Enter(Phase.AttackTap, now);
                break;

            case Phase.AttackTap:
                SetAction("attack", true);
                _tapReleaseAt = now + TapSeconds;
                _playerHpBeforeStrike = Player.Hp;
                Enter(Phase.AwaitAttackHit, now);
                break;

            case Phase.AwaitAttackHit:
                if (Enemy.Hp < _enemyHpBeforeAttack)
                {
                    GD.Print($"[Smoke] PASS attack damaged enemy (hp {Enemy.Hp:0})");
                    Enter(Phase.AwaitTelegraph, now);
                }
                else if (now - _phaseStart > 2f)
                {
                    Fail("attack never damaged the enemy");
                    Enter(Phase.AwaitTelegraph, now);
                }
                break;

            case Phase.AwaitTelegraph:
                if (Enemy.State == EnemyState.Telegraph)
                {
                    if (_telegraphStartedAt < 0) _telegraphStartedAt = now;
                    float cueAt = _telegraphStartedAt + Enemy.Windows.EnemyTelegraphSeconds - DodgeLeadBeforeImpact;
                    if (now >= cueAt)
                    {
                        SetAction("dodge", true);
                        _tapReleaseAt = now + TapSeconds;
                        Enter(Phase.AwaitDodgeImpact, now);
                    }
                }
                else
                {
                    _telegraphStartedAt = -1f;
                }
                break;

            case Phase.AwaitDodgeImpact:
                if (Enemy.State == EnemyState.Recover || Enemy.State == EnemyState.Hitstun)
                {
                    bool noDamage = Mathf.IsEqualApprox(Player.Hp, _playerHpBeforeStrike);
                    if (noDamage && _feedback.Exists(f => f.Contains("PERFECT DODGE")))
                        GD.Print("[Smoke] PASS perfect dodge (no damage + resource refund)");
                    else if (noDamage)
                        GD.Print("[Smoke] PASS dodge avoided damage (window edge: not perfect)");
                    else
                        Fail($"dodge failed (hp {_playerHpBeforeStrike:0} -> {Player.Hp:0})");
                    Enter(Phase.TimeSlowHold, now);
                }
                else if (now - _phaseStart > 5f)
                {
                    Fail("enemy never resolved a strike after dodge");
                    Enter(Phase.TimeSlowHold, now);
                }
                break;

            case Phase.TimeSlowHold:
                SetAction("time_slow", true);
                if (now - _phaseStart > 1.0f)
                {
                    bool slowed = Mathf.IsEqualApprox((float)Engine.TimeScale, TimeSlow.Config.TimeScale);
                    bool draining = TimeSlow.Resource < TimeSlow.Config.MaxResource;
                    if (slowed && draining)
                        GD.Print($"[Smoke] PASS time slow active (x{Engine.TimeScale:0.00}, resource {TimeSlow.Resource:0})");
                    else
                        Fail($"time slow wrong: scale {Engine.TimeScale:0.00}, resource {TimeSlow.Resource:0}");
                    Enter(Phase.TimeSlowRelease, now);
                }
                break;

            case Phase.TimeSlowRelease:
                SetAction("time_slow", false);
                if (now - _phaseStart > 0.3f)
                {
                    if (Mathf.IsEqualApprox((float)Engine.TimeScale, 1f))
                        GD.Print("[Smoke] PASS time slow released (scale restored)");
                    else
                        Fail($"time scale stuck at {Engine.TimeScale:0.00}");
                    Finish();
                }
                break;
        }
    }

    private void Enter(Phase next, float now)
    {
        _phase = next;
        _phaseStart = now;
    }

    private void Fail(string reason)
    {
        GD.PrintErr($"[Smoke] FAIL {reason}");
        _failures.Add(reason);
    }

    private void Finish()
    {
        GD.Print(_failures.Count == 0
            ? $"[Smoke] ALL PASS ({_feedback.Count} combat events observed)"
            : $"[Smoke] DONE WITH {_failures.Count} FAILURE(S)");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
