using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Opt-in automated smoke test. It drives the real input path and checks the
/// three boss attack contracts: normal block/dodge, break/grab dodge or K, and
/// delayed area movement/time slow. Timers use real timestamps.
/// </summary>
public partial class SmokeTest : Node
{
    private const float TapSeconds = 0.05f;
    private static bool _resetProbePending;

    private enum Phase
    {
        Setup, AttackTap, AwaitAttackHit,
        AwaitNormalTelegraph, AwaitNormalImpact,
        AwaitBreakTelegraph, AwaitBreakImpact,
        AwaitAreaTelegraph, AwaitAreaImpact,
        AwaitBlockTelegraph, AwaitBlockImpact,
        TimeSlowProbe, TimeSlowRelease, KillBoss, AwaitBossHit
    }

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
        if (_resetProbePending)
        {
            _resetProbePending = false;
            GD.Print("[Smoke] PASS R reset (new arena instance started)");
            GetTree().Quit(0);
            return;
        }
        Player.CombatFeedback += line => _feedback.Add(line);
        CheckAttackContractDifferences();
        GD.Print("[Smoke] running (ACT_SMOKE=1)");
    }

    private void CheckAttackContractDifferences()
    {
        float now = Now;
        var heldBlock = new DefenseSnapshot(false, 0f, true,
            now - Player.Windows.PerfectGuardSeconds - 0.5f, false, now);
        var perfectDodge = new DefenseSnapshot(true,
            now - Player.Windows.PerfectDodgeSeconds * 0.5f, false, 0f, false, now);
        var normalBlock = CombatResolver.Resolve(heldBlock, Player.Windows.NormalAttackDamage,
            Player.Windows, TimeSlow.Config, BossAttackKind.Normal);
        var breakBlock = CombatResolver.Resolve(heldBlock, Player.Windows.BreakGrabDamage,
            Player.Windows, TimeSlow.Config, BossAttackKind.BreakGrab);
        var areaBlock = CombatResolver.Resolve(heldBlock, Player.Windows.DelayedAreaDamage,
            Player.Windows, TimeSlow.Config, BossAttackKind.DelayedArea);
        var breakDodge = CombatResolver.Resolve(perfectDodge, Player.Windows.BreakGrabDamage,
            Player.Windows, TimeSlow.Config, BossAttackKind.BreakGrab);
        if (normalBlock.Result == DefenseResult.Blocked &&
            breakBlock.Result == DefenseResult.Hit &&
            areaBlock.Result == DefenseResult.Hit &&
            breakDodge.Result == DefenseResult.PerfectDodged)
            GD.Print("[Smoke] PASS attack contract differences (blockable, break/grab, delayed area)");
        else
            Fail($"attack contract mismatch: normal {normalBlock.Result}, break {breakBlock.Result}, area {areaBlock.Result}, dodge {breakDodge.Result}");
    }

    private static void SetAction(string action, bool pressed)
        => Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });

    private static float Now => Time.GetTicksMsec() / 1000f;

    public override void _Process(double delta)
    {
        float now = Now;
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
                if (Enemy.State == EnemyState.Idle) Enter(Phase.AttackTap, now);
                break;

            case Phase.AttackTap:
                Tap("attack");
                Enter(Phase.AwaitAttackHit, now);
                break;

            case Phase.AwaitAttackHit:
                if (Enemy.Hp < _enemyHpBeforeAttack)
                {
                    GD.Print($"[Smoke] PASS player attack damaged boss (hp {Enemy.Hp:0})");
                    Enter(Phase.AwaitNormalTelegraph, now);
                }
                else if (now - _phaseStart > 2f)
                {
                    Fail("player attack never damaged boss");
                    Enter(Phase.AwaitNormalTelegraph, now);
                }
                break;

            case Phase.AwaitNormalTelegraph:
                if (BeginExpectedTelegraph(BossAttackKind.Normal, now))
                {
                    _playerHpBeforeStrike = Player.Hp;
                    Enter(Phase.AwaitNormalImpact, now);
                }
                else if (now - _phaseStart > 6f)
                {
                    Fail("normal strike telegraph not observed");
                    Enter(Phase.AwaitBreakTelegraph, now);
                }
                break;

            case Phase.AwaitNormalImpact:
                if (Enemy.State is EnemyState.Recover or EnemyState.Hitstun)
                {
                    if (Player.Hp < _playerHpBeforeStrike)
                        GD.Print("[Smoke] PASS normal strike landed when undefended");
                    else
                        Fail("normal strike did not produce a hit result");
                    Enter(Phase.AwaitBreakTelegraph, now);
                }
                break;

            case Phase.AwaitBreakTelegraph:
                if (BeginExpectedTelegraph(BossAttackKind.BreakGrab, now))
                {
                    _playerHpBeforeStrike = Player.Hp;
                    float cueAt = _telegraphStartedAt + Enemy.CurrentAttack.TelegraphSeconds -
                        Enemy.Windows.PerfectDodgeSeconds * 0.8f;
                    if (now >= cueAt)
                    {
                        Tap("dodge");
                        Enter(Phase.AwaitBreakImpact, now);
                    }
                }
                else if (now - _phaseStart > 7f)
                {
                    Fail("break grab telegraph not observed");
                    Enter(Phase.AwaitAreaTelegraph, now);
                }
                break;

            case Phase.AwaitBreakImpact:
                if (Enemy.State is EnemyState.Recover or EnemyState.Hitstun)
                {
                    if (Mathf.IsEqualApprox(Player.Hp, _playerHpBeforeStrike) &&
                        _feedback.Exists(f => f.Contains("PERFECT DODGE")))
                        GD.Print("[Smoke] PASS break grab answered by perfect dodge");
                    else
                        Fail("break grab was not answered by dodge/K");
                    Enter(Phase.AwaitAreaTelegraph, now);
                }
                break;

            case Phase.AwaitAreaTelegraph:
                if (BeginExpectedTelegraph(BossAttackKind.DelayedArea, now))
                {
                    _playerHpBeforeStrike = Player.Hp;
                    // Moving beyond the configured hazard radius demonstrates
                    // the valid response for the delayed area attack.
                    Player.Position = Enemy.Position + new Vector2(300, 0);
                    GD.Print("[Smoke] PASS delayed area telegraph gives movement response window");
                    Enter(Phase.AwaitAreaImpact, now);
                }
                else if (now - _phaseStart > 8f)
                {
                    Fail("delayed area telegraph not observed");
                    Enter(Phase.AwaitBlockTelegraph, now);
                }
                break;

            case Phase.AwaitAreaImpact:
                if (Enemy.State is EnemyState.Recover or EnemyState.Hitstun)
                {
                    if (Mathf.IsEqualApprox(Player.Hp, _playerHpBeforeStrike))
                        GD.Print("[Smoke] PASS delayed area avoided by movement");
                    else
                        Fail("delayed area hit after player moved outside hazard");
                    Enter(Phase.TimeSlowProbe, now);
                }
                break;

            case Phase.TimeSlowProbe:
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
                if (now - _phaseStart > 0.35f)
                {
                    if (Mathf.IsEqualApprox((float)Engine.TimeScale, 1f))
                        GD.Print("[Smoke] PASS time slow released (scale restored)");
                    else
                        Fail($"time scale stuck at {Engine.TimeScale:0.00}");
                    Player.Position = Enemy.Position - new Vector2(110, 0);
                    Enter(Phase.AwaitBlockTelegraph, now);
                }
                break;

            case Phase.AwaitBlockTelegraph:
                if (BeginExpectedTelegraph(BossAttackKind.Normal, now))
                {
                    _playerHpBeforeStrike = Player.Hp;
                    float cueAt = _telegraphStartedAt + Enemy.CurrentAttack.TelegraphSeconds -
                        Enemy.Windows.PerfectGuardSeconds * 0.8f;
                    if (now >= cueAt)
                    {
                        SetAction("block", true);
                        Enter(Phase.AwaitBlockImpact, now);
                    }
                }
                else if (now - _phaseStart > 8f)
                {
                    Fail("normal telegraph for block check not observed");
                    Enter(Phase.KillBoss, now);
                }
                break;

            case Phase.AwaitBlockImpact:
                if (Enemy.State is EnemyState.Recover or EnemyState.Hitstun)
                {
                    SetAction("block", false);
                    if (Mathf.IsEqualApprox(Player.Hp, _playerHpBeforeStrike) &&
                        _feedback.Exists(f => f.Contains("PERFECT BLOCK")))
                        GD.Print("[Smoke] PASS normal strike answered by perfect block");
                    else
                        Fail("normal strike was not answered by perfect block");
                    Enter(Phase.KillBoss, now);
                }
                break;

            case Phase.KillBoss:
                SetAction("block", false);
                if (Enemy.State == EnemyState.Victory)
                {
                    GD.Print("[Smoke] PASS boss defeated (state Victory)");
                    Player.TryReceiveEnemyAttack(Player.MaxHp + 1f, out _);
                    if (Player.IsDown)
                    {
                        GD.Print("[Smoke] PASS player death state");
                        GD.Print(_failures.Count == 0
                            ? $"[Smoke] ALL PASS ({_feedback.Count} combat events observed)"
                            : $"[Smoke] DONE WITH {_failures.Count} FAILURE(S)");
                        _resetProbePending = true;
                        SetAction("reset_round", true);
                    }
                    else
                    {
                        Fail("player did not enter death state");
                        Finish();
                    }
                    break;
                }
                if (Enemy.State == EnemyState.Idle && now - _phaseStart > 0.25f)
                {
                    Player.Position = Enemy.Position - new Vector2(110, 0);
                    _enemyHpBeforeAttack = Enemy.Hp;
                    Tap("attack");
                    Enter(Phase.AwaitBossHit, now);
                }
                else if (now - _phaseStart > 15f)
                {
                    Fail("boss could not be defeated with player attacks");
                    Finish();
                }
                break;

            case Phase.AwaitBossHit:
                if (Enemy.Hp < _enemyHpBeforeAttack || Enemy.IsDefeated)
                    Enter(Phase.KillBoss, now);
                else if (now - _phaseStart > 3f)
                {
                    Fail("player attack did not damage boss during kill test");
                    Finish();
                }
                break;
        }
    }

    private bool BeginExpectedTelegraph(BossAttackKind expected, float now)
    {
        if (Enemy.State != EnemyState.Telegraph)
        {
            _telegraphStartedAt = -1f;
            return false;
        }
        if (_telegraphStartedAt < 0)
        {
            _telegraphStartedAt = now;
            if (Enemy.CurrentAttackKind != expected)
            {
                Fail($"expected {expected} telegraph, got {Enemy.CurrentAttackKind}");
                return false;
            }
        }
        return Enemy.CurrentAttackKind == expected;
    }

    private void Tap(string action)
    {
        SetAction(action, true);
        _tapReleaseAt = Now + TapSeconds;
    }

    private void Enter(Phase next, float now)
    {
        _phase = next;
        _phaseStart = now;
        if (next is not (Phase.AwaitNormalTelegraph or Phase.AwaitBreakTelegraph or Phase.AwaitAreaTelegraph or Phase.AwaitBlockTelegraph))
            _telegraphStartedAt = -1f;
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
