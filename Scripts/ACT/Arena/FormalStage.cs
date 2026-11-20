using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;

namespace ACTSpike.ACT.Arena;

public partial class FormalStage : Node2D
{
    private const float FloorY = 560f;
    private const float ExitGateX = 1050f;
    private const string ExitGateId = "Exit";

    [Export] public bool HasEnemy { get; set; }
    [Export] public string NextScene { get; set; } = "res://Scenes/CoreLoop.tscn";

    private PlayerController _player = null!;
    private StageEnemyRoster _roster = null!;
    private EnemyController? _enemy;
    private CombatHud _hud = null!;
    private TimeSlowManager _timeSlow = null!;
    private RuinEnvironmentLayer _environment = null!;
    private Camera2D _camera = null!;
    private bool _transitioning;

    public override void _Ready()
    {
        _environment = new RuinEnvironmentLayer
        {
            Name = "RuinEnvironment",
            Theme = HasEnemy ? RuinStageTheme.FormalEncounter : RuinStageTheme.FormalRoute
        };
        AddChild(_environment);
        _environment.AddFacetedStaticBody(this, new Rect2(0f, FloorY, 1280f, 160f));
        _environment.AddFacetedStaticBody(this, new Rect2(0f, 0f, 40f, 720f), drawLitTopEdge: false);
        _environment.AddFacetedStaticBody(this, new Rect2(1240f, 0f, 40f, 720f), drawLitTopEdge: false);
        _environment.AddGate(ExitGateId, new Vector2(ExitGateX, FloorY),
            HasEnemy ? RuinGateState.Locked : RuinGateState.Unlocked);

        _timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        AddChild(_timeSlow);
        _player = new PlayerController { Name = "Player", Position = new Vector2(260, 537), Windows = new CombatWindowConfig(), TimeSlow = _timeSlow.Config };
        AddChild(_player);
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { HasPlayerState: true } session)
            _player.RestoreHp(session.PlayerHp);

        _roster = StageRuntimeWiring.AttachEnemyRoster(this, _timeSlow);

        if (HasEnemy)
        {
            _enemy = new EnemyController
            {
                Name = "EncounterEnemy",
                DisplayName = "遗迹敌兵",
                IsBoss = false,
                Position = new Vector2(700, 510),
                MaxHp = 3
            };
            AddChild(_enemy);
            _enemy.Bind(_player, _player.Windows);
            _roster.Register(_enemy);
        }

        _camera = new Camera2D { Position = new Vector2(380, -80), LimitLeft = 0, LimitTop = 0, LimitRight = 1280, LimitBottom = 720 };
        _player.AddChild(_camera);
        _environment.Camera = _camera;

        _hud = StageRuntimeWiring.MountStagePresentationAndHud(
            this,
            _player,
            _roster,
            _timeSlow,
            _camera,
            HasEnemy ? "清除遭遇敌人后前往右侧出口" : "沿路线向右移动，抵达出口进入下一场景");
    }

    public override void _Process(double delta)
    {
        if (StageRuntimeWiring.HandleResetIfRequested(this, _timeSlow))
            return;
        bool ready = !HasEnemy || _roster.AliveCount == 0;
        _environment.SetGateState(ExitGateId, ready ? RuinGateState.Unlocked : RuinGateState.Locked);
        if (ready && HasEnemy)
            _hud.IsObjectiveCleared = true;
        if (_transitioning || _player.IsDown) return;
        if (ready && _player.GlobalPosition.X >= ExitGateX)
        {
            _transitioning = true;
            GetNodeOrNull<RunSession>("/root/RunSession")?.SavePlayerHp(_player.Hp);
            _hud.IsObjectiveCleared = true;
            _hud.ArenaMessage = "出口已到达，正在进入下一场景";
            StageRuntimeWiring.ScheduleSceneChange(this, NextScene);
        }
    }
}
