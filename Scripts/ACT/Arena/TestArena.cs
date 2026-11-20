using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Builds the core boss arena in code and wires the modules together:
/// procedural ruin environment, input audit, player, enemy, time-slow manager, HUD, reset.
/// </summary>
public partial class TestArena : Node2D
{
    private const float FloorTopY = 560f;
    private const float ArenaWidth = 2400f;
    private const float BossBoundaryX = 1640f;
    private const float VictoryBoundaryX = 2260f;
    private const string BossGateId = "BossBoundary";
    private const string VictoryGateId = "VictoryExit";
    private static readonly Vector2 PlayerSpawn = new(260, FloorTopY - 23);
    private static readonly Vector2 EnemySpawn = new(1840, FloorTopY - 50);

    private CombatWindowConfig _windows = new();
    private TimeSlowConfig _timeSlowConfig = new();
    private TimeSlowManager _timeSlow = null!;
    private PlayerController _player = null!;
    private EnemyController _enemy = null!;
    private CombatHud _hud = null!;
    private RuinEnvironmentLayer _environment = null!;
    private bool _bossArenaEntered;
    private bool _transitioning;
    private bool _victory;
    private bool _defeatReported;
    private bool _bossDefeatReported;

    public override void _Ready()
    {
        _windows = new CombatWindowConfig();
        _timeSlowConfig = new TimeSlowConfig();

        BuildRoomGeometry();

        _timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = _timeSlowConfig };
        AddChild(_timeSlow);

        _player = new PlayerController { Name = "Player", Position = PlayerSpawn, Windows = _windows, TimeSlow = _timeSlowConfig };
        AddChild(_player);
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { HasPlayerState: true } session)
            _player.RestoreHp(session.PlayerHp);

        _enemy = new EnemyController { Name = "Boss", Position = EnemySpawn, MaxHp = 6f, Windows = _windows };
        AddChild(_enemy);
        _enemy.Bind(_player, _windows);

        var camera = new Camera2D { Name = "RouteCamera", Position = new Vector2(0, -80), LimitLeft = 0, LimitTop = 0, LimitRight = (int)ArenaWidth, LimitBottom = 720, PositionSmoothingEnabled = true, PositionSmoothingSpeed = 8f };
        _player.AddChild(camera);
        _environment.Camera = camera;

        _player.Visual.BindTimeSlow(_timeSlow);
        CombatFeedbackLayer.Attach(this, _player, _enemy, _timeSlow, camera);

        BuildHud();
        PauseMenuOverlay.Attach(this, _timeSlow);
        WireFeedback();
        CombatAudioController.Attach(this, _player, _enemy, _timeSlow);
        LogInputAudit();

        if (OS.GetEnvironment("ACT_SMOKE") == "1" && GetParent() == GetTree().Root)
        {
            AddChild(new SmokeTest { Name = "SmokeTest", Player = _player, Enemy = _enemy, TimeSlow = _timeSlow });
        }
        GD.Print($"[Arena] core loop ready | perfect window {_windows.PerfectGuardSeconds:0.00}s | " +
                 $"time slow x{_timeSlowConfig.TimeScale:0.00} drain {_timeSlowConfig.DrainPerSecond:0}/s");
    }

    public override void _Process(double delta)
    {
        if (ActInput.ResetPressed())
        {
            GD.Print("[Arena] reset requested");
            Engine.TimeScale = 1.0;
            GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
            GetTree().ReloadCurrentScene();
        }
        if (_player.IsDown && !_defeatReported)
        {
            _defeatReported = true;
            _hud.ArenaMessage = "已倒下 · 按 R 重置当前关";
            _hud.ReportCombat("failure: player defeated");
            GD.Print("[Arena] player defeated; waiting for R reset");
        }
        if (!_bossArenaEntered && _player.GlobalPosition.X >= BossBoundaryX)
        {
            _bossArenaEntered = true;
            _environment.SetGateState(BossGateId, RuinGateState.Locked);
            _hud.ArenaMessage = "遗迹守卫苏醒 · 击败强敌以开启出口";
            _hud.ReportCombat("boss arena entered automatically");
            GD.Print("[Arena] boss boundary crossed");
        }

        if (_bossArenaEntered && !_bossDefeatReported && _enemy.IsDefeated)
        {
            _bossDefeatReported = true;
            _environment.SetGateState(BossGateId, RuinGateState.Unlocked);
            _environment.SetGateState(VictoryGateId, RuinGateState.Unlocked);
            _hud.ArenaMessage = "遗迹守卫已击破 · 前往右侧出口";
            _hud.ReportCombat("victory condition met: boss defeated");
            GD.Print("[Arena] boss defeated; exit is now active");
        }

        if (!_victory && !_transitioning && _bossArenaEntered && _enemy.IsDefeated &&
            _player.GlobalPosition.X >= VictoryBoundaryX)
        {
            _victory = true;
            _transitioning = true;
            _hud.ArenaMessage = "已抵达出口 · 正在前往结算";
            GD.Print("[Arena] victory boundary crossed; changing scene");
            GetTree().ChangeSceneToFile("res://Scenes/Victory.tscn");
        }
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = 1.0;
    }

    private void BuildRoomGeometry()
    {
        _environment = new RuinEnvironmentLayer
        {
            Name = "RuinEnvironment",
            Theme = RuinStageTheme.BossArena
        };
        AddChild(_environment);

        _environment.AddFacetedStaticBody(this, new Rect2(0f, FloorTopY, ArenaWidth, 160f));
        _environment.AddFacetedStaticBody(this, new Rect2(0f, 0f, 40f, 720f), drawLitTopEdge: false);
        _environment.AddFacetedStaticBody(this, new Rect2(ArenaWidth - 40f, 0f, 40f, 720f), drawLitTopEdge: false);

        // Short, collision-safe route: a low step, an automatic boss boundary, and the boss apron.
        _environment.AddFacetedStaticBody(this, new Rect2(500f, 500f, 180f, 60f));
        _environment.AddFacetedStaticBody(this, new Rect2(1380f, 520f, 220f, 40f));

        _environment.AddGate(BossGateId, new Vector2(BossBoundaryX, FloorTopY), RuinGateState.Active);
        _environment.AddGate(VictoryGateId, new Vector2(VictoryBoundaryX, FloorTopY), RuinGateState.Locked);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "HUDLayer", Layer = 10 };
        _hud = new CombatHud
        {
            Name = "HUD",
            Size = new Vector2(1280, 720),
            MouseFilter = Control.MouseFilterEnum.Ignore, // keep LMB attack working over the HUD
            Player = _player,
            Enemy = _enemy,
            TimeSlow = _timeSlow
        };
        layer.AddChild(_hud);
        AddChild(layer);
    }

    private void WireFeedback()
    {
        _player.CombatFeedback += _hud.ReportCombat;
        _player.TimeResourceRefund += _timeSlow.AddResource;
        _timeSlow.StateChanged += line =>
        {
            GD.Print($"[TimeSlow] {line}");
            _hud.ReportTime(line);
        };
    }

    /// <summary>Startup evidence that every action is bound, including Xbox pad events.</summary>
    private void LogInputAudit()
    {
        var padNames = new Dictionary<JoyButton, string>
        {
            [JoyButton.A] = "A", [JoyButton.B] = "B", [JoyButton.X] = "X", [JoyButton.Y] = "Y",
            [JoyButton.LeftShoulder] = "LB", [JoyButton.RightShoulder] = "RB"
        };
        foreach (var action in new[]
                 {
                     "move_left", "move_right", "move_up", "move_down", "jump", "attack",
                     "dodge", "block", "time_slow", "skill", "reset_round"
                 })
        {
            var parts = new List<string>();
            foreach (var ev in InputMap.ActionGetEvents(action))
            {
                switch (ev)
                {
                    case InputEventKey k:
                        parts.Add($"key:{k.PhysicalKeycode}");
                        break;
                    case InputEventMouseButton m:
                        parts.Add($"mouse:{m.ButtonIndex}");
                        break;
                    case InputEventJoypadButton b:
                        parts.Add($"pad:{padNames.GetValueOrDefault(b.ButtonIndex, $"btn{b.ButtonIndex}")}");
                        break;
                    case InputEventJoypadMotion a:
                        parts.Add($"axis:{a.Axis}{(a.AxisValue < 0 ? "-" : "+")}");
                        break;
                }
            }
            GD.Print($"[Input] {action}: {string.Join(", ", parts)}");
        }
    }
}
