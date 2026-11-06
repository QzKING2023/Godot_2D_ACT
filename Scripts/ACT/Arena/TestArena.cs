using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Builds the placeholder test arena in code and wires the modules together:
/// input audit, player, enemy, time-slow manager, HUD, reset. All geometry is
/// programmatic placeholder; nothing here depends on art assets.
/// </summary>
public partial class TestArena : Node2D
{
    private const float FloorTopY = 560f;
    private const float ArenaWidth = 2400f;
    private const float BossBoundaryX = 1640f;
    private const float VictoryBoundaryX = 2260f;
    private static readonly Vector2 PlayerSpawn = new(260, FloorTopY - 23);
    private static readonly Vector2 EnemySpawn = new(1840, FloorTopY - 50);

    private CombatWindowConfig _windows = new();
    private TimeSlowConfig _timeSlowConfig = new();
    private TimeSlowManager _timeSlow = null!;
    private PlayerController _player = null!;
    private EnemyController _enemy = null!;
    private CombatHud _hud = null!;
    private bool _bossArenaEntered;
    private bool _transitioning;
    private bool _victory;

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

        _enemy = new EnemyController { Name = "Enemy", Position = EnemySpawn, Windows = _windows };
        AddChild(_enemy);
        _enemy.Bind(_player, _windows);

        var camera = new Camera2D { Name = "RouteCamera", Position = new Vector2(0, -80), LimitLeft = 0, LimitTop = 0, LimitRight = (int)ArenaWidth, LimitBottom = 720, PositionSmoothingEnabled = true, PositionSmoothingSpeed = 8f };
        _player.AddChild(camera);

        BuildHud();
        WireFeedback();
        LogInputAudit();

        if (OS.GetEnvironment("ACT_SMOKE") == "1")
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
        if (!_bossArenaEntered && _player.GlobalPosition.X >= BossBoundaryX)
        {
            _bossArenaEntered = true;
            _hud.ArenaMessage = "BOSS ARENA - defeat the enemy, then exit right";
            _hud.ReportCombat("boss arena entered automatically");
            GD.Print("[Arena] boss boundary crossed");
        }

        if (!_victory && !_transitioning && _bossArenaEntered && _enemy.State == EnemyState.Dead &&
            _player.GlobalPosition.X >= VictoryBoundaryX)
        {
            _victory = true;
            _transitioning = true;
            _hud.ArenaMessage = "EXIT REACHED - transitioning";
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
        AddStaticRect(new Rect2(0, FloorTopY, ArenaWidth, 160), new Color("1b2b3a"));      // floor
        AddStaticRect(new Rect2(0, 0, 40, 720), new Color("16202c"));                // left wall
        AddStaticRect(new Rect2(ArenaWidth - 40, 0, 40, 720), new Color("16202c"));  // right wall

        // Short, collision-safe route: a low step, an automatic boss boundary, and the boss apron.
        AddStaticRect(new Rect2(500, 500, 180, 60), new Color("29445a"));
        AddStaticRect(new Rect2(1380, 520, 220, 40), new Color("29445a"));
        AddMarker(new Vector2(BossBoundaryX - 80, FloorTopY - 72), "BOSS BOUNDARY", new Color("ffd166"));
        AddMarker(new Vector2(VictoryBoundaryX - 55, FloorTopY - 72), "EXIT", new Color("80ed99"));
        AddMarker(new Vector2(100, FloorTopY - 72), "START", new Color("a8dadc"));
    }

    private StaticBody2D AddStaticRect(Rect2 rect, Color color)
    {
        var body = new StaticBody2D
        {
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = rect.GetCenter()
        };
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = rect.Size }
        });
        var visual = new ColorRect
        {
            Size = rect.Size,
            Position = rect.Position - body.Position,
            Color = color,
            Name = "Visual"
        };
        body.AddChild(visual);
        AddChild(body);
        return body;
    }

    private void AddMarker(Vector2 position, string text, Color color)
    {
        var label = new Label { Text = text, Position = position, Modulate = color, ZIndex = 3 };
        AddChild(label);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "HUDLayer" };
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
