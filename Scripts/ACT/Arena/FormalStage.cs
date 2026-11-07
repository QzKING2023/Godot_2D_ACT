using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Arena;

public partial class FormalStage : Node2D
{
    [Export] public bool HasEnemy { get; set; }
    [Export] public string NextScene { get; set; } = "res://Scenes/CoreLoop.tscn";

    private PlayerController _player = null!;
    private EnemyController? _enemy;
    private CombatHud _hud = null!;
    private TimeSlowManager _timeSlow = null!;
    private bool _transitioning;

    public override void _Ready()
    {
        var floor = new StaticBody2D { Name = "Floor", CollisionLayer = 2 };
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(1280, 160) }, Position = new Vector2(640, 640) });
        AddChild(floor);
        AddBoundary(floor, new Vector2(20, 360));
        AddBoundary(floor, new Vector2(1260, 360));

        _timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        AddChild(_timeSlow);
        _player = new PlayerController { Name = "Player", Position = new Vector2(260, 537), Windows = new CombatWindowConfig(), TimeSlow = _timeSlow.Config };
        AddChild(_player);
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { HasPlayerState: true } session)
            _player.RestoreHp(session.PlayerHp);

        if (HasEnemy)
        {
            _enemy = new EnemyController { Name = "EncounterEnemy", Position = new Vector2(700, 510), MaxHp = 3 };
            AddChild(_enemy);
            _enemy.Bind(_player, _player.Windows);
        }

        var camera = new Camera2D { Position = new Vector2(380, -80), LimitLeft = 0, LimitTop = 0, LimitRight = 1280, LimitBottom = 720 };
        _player.AddChild(camera);
        _hud = new CombatHud { Name = "HUD", Size = new Vector2(1280, 720), Player = _player, Enemy = _enemy, TimeSlow = _timeSlow };
        var layer = new CanvasLayer { Name = "HUDLayer" };
        layer.AddChild(_hud);
        AddChild(layer);
        _hud.ArenaMessage = HasEnemy ? "清除遭遇敌人后前往右侧出口" : "沿路线向右移动，抵达出口进入下一场景";
    }

    public override void _Process(double delta)
    {
        if (ActInput.ResetPressed())
        {
            GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
            GetTree().ReloadCurrentScene();
            return;
        }
        if (_transitioning || _player.IsDown) return;
        bool ready = !HasEnemy || _enemy is not null && _enemy.IsDefeated;
        if (ready && _player.GlobalPosition.X >= 1050f)
        {
            _transitioning = true;
            GetNodeOrNull<RunSession>("/root/RunSession")?.SavePlayerHp(_player.Hp);
            _hud.ArenaMessage = "出口已到达，正在进入下一场景";
            GetTree().CreateTimer(0.35).Timeout += () => GetTree().ChangeSceneToFile(NextScene);
        }
    }

    private static void AddBoundary(StaticBody2D floor, Vector2 position)
    {
        floor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40, 720) }, Position = position });
    }
}
