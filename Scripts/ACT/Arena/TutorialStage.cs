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

public enum TutorialKind { Movement, Attack, Skill, Defense, Dodge, PerfectDefense, TimeSlow, Combat }

public partial class TutorialStage : Node2D
{
    [Export] public TutorialKind Kind { get; set; } = TutorialKind.Movement;
    [Export] public string NextScene { get; set; } = "res://Scenes/Tutorial/Attack.tscn";

    private const float FloorY = 560f;
    private PlayerController _player = null!;
    private TimeSlowManager _timeSlow = null!;
    private CombatHud _hud = null!;
    private RuinEnvironmentLayer _environment = null!;
    private EnemyController? _enemy;
    private Camera2D _camera = null!;
    private float _startX;
    private int _count;
    private int _blockCount;
    private int _dodgeCount;
    private int _skillUses;
    private bool _perfectBlock;
    private bool _perfectDodge;
    private PlayerState _lastState;
    private bool _jumpSeen;
    private bool _completed;
    private bool _wasSlow;

    public override void _Ready()
    {
        _environment = new RuinEnvironmentLayer
        {
            Name = "RuinEnvironment",
            Theme = RuinStageTheme.Tutorial
        };
        AddChild(_environment);
        _environment.AddFacetedStaticBody(this, new Rect2(0f, FloorY, 1280f, 160f));
        _environment.AddFacetedStaticBody(this, new Rect2(0f, 0f, 40f, 720f), drawLitTopEdge: false);
        _environment.AddFacetedStaticBody(this, new Rect2(1240f, 0f, 40f, 720f), drawLitTopEdge: false);

        _timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        AddChild(_timeSlow);
        _player = new PlayerController { Name = "Player", Position = new Vector2(260, FloorY - 23), Windows = new CombatWindowConfig(), TimeSlow = _timeSlow.Config };
        AddChild(_player);
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { HasPlayerState: true } session)
            _player.RestoreHp(session.PlayerHp);
        _startX = _player.Position.X;
        _lastState = _player.State;

        if (Kind is TutorialKind.Attack or TutorialKind.Defense or TutorialKind.Dodge or TutorialKind.PerfectDefense or TutorialKind.Combat)
        {
            _enemy = new EnemyController { Name = "TrainingTarget", Position = new Vector2(650, FloorY - 50), MaxHp = Kind == TutorialKind.Combat ? 3 : 99 };
            AddChild(_enemy);
            _enemy.Bind(_player, _player.Windows);
        }

        _camera = new Camera2D { Position = new Vector2(380, -80), LimitLeft = 0, LimitTop = 0, LimitRight = 1280, LimitBottom = 720 };
        _player.AddChild(_camera);
        _environment.Camera = _camera;
        _player.Visual.BindTimeSlow(_timeSlow);
        CombatFeedbackLayer.Attach(this, _player, _enemy, _timeSlow, _camera);
        _hud = new CombatHud { Name = "HUD", Size = new Vector2(1280, 720), Player = _player, Enemy = _enemy, TimeSlow = _timeSlow };
        var layer = new CanvasLayer { Name = "HUDLayer", Layer = 10 };
        layer.AddChild(_hud);
        AddChild(layer);
        PauseMenuOverlay.Attach(this, _timeSlow);
        CombatAudioController.Attach(this, _player, _enemy, _timeSlow);
        _player.CombatFeedback += OnCombatFeedback;
        _hud.ArenaMessage = GoalText();
    }

    public override void _Process(double delta)
    {
        if (ActInput.ResetPressed())
        {
            Engine.TimeScale = 1.0;
            GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
            GetTree().ReloadCurrentScene();
            return;
        }
        if (_completed) return;
        if (ActInput.MoveAxis().X != 0) _count = Mathf.Max(_count, 1);
        if (ActInput.JumpPressed() || (_player.State == Core.PlayerState.Jump && _player.Velocity.Y < -20f))
            _jumpSeen = true;
        if (_timeSlow.Active && !_wasSlow) _count++;
        _wasSlow = _timeSlow.Active;
        if (Kind == TutorialKind.Skill && _player.State == PlayerState.Skill && _lastState != PlayerState.Skill)
            _skillUses++;
        if (Kind == TutorialKind.Skill && _player.State == PlayerState.Skill && ActInput.MoveAxis().LengthSquared() > 0.1f)
            _count = Mathf.Max(_count, 3);
        if ((Kind == TutorialKind.Defense || Kind == TutorialKind.PerfectDefense) && _player.State == PlayerState.Block && _lastState != PlayerState.Block)
            _blockCount++;
        if ((Kind == TutorialKind.Dodge || Kind == TutorialKind.PerfectDefense) && _player.State == PlayerState.Dodge && _lastState != PlayerState.Dodge)
            _dodgeCount++;
        if (Kind == TutorialKind.Combat && _enemy is not null && _enemy.IsDefeated) _count = 1;
        _lastState = _player.State;
        if (IsComplete()) Complete();
    }

    private void OnCombatFeedback(string line)
    {
        if (Kind == TutorialKind.Attack && line.Contains("Attack hit")) _count++;
        if ((Kind == TutorialKind.Defense || Kind == TutorialKind.PerfectDefense) && line.Contains("PERFECT BLOCK")) _perfectBlock = true;
        if ((Kind == TutorialKind.Dodge || Kind == TutorialKind.PerfectDefense) && line.Contains("PERFECT DODGE")) _perfectDodge = true;
    }

    private bool IsComplete() => Kind switch
    {
        TutorialKind.Movement => _count >= 1 && _jumpSeen,
        TutorialKind.Attack => _count >= 3,
        TutorialKind.Skill => _skillUses >= 3 && _count >= 3,
        TutorialKind.Defense => _blockCount >= 3,
        TutorialKind.Dodge => _dodgeCount >= 3,
        TutorialKind.PerfectDefense => _perfectBlock && _perfectDodge,
        TutorialKind.TimeSlow => _count >= 1,
        TutorialKind.Combat => _count >= 1,
        _ => false
    };

    private string GoalText() => Kind switch
    {
        TutorialKind.Movement => "移动左右，然后完成一次跳跃",
        TutorialKind.Attack => "对训练目标完成 3 次普通攻击命中",
        TutorialKind.Skill => "释放 3 次技能，再用技能位移通过路线",
        TutorialKind.Defense => "完成 3 次格挡",
        TutorialKind.Dodge => "完成 3 次闪避",
        TutorialKind.PerfectDefense => "在红色闪光的一瞬间完成一次完美格挡和完美闪避",
        TutorialKind.TimeSlow => "按住 Q 使用时缓，直到资源开始消耗",
        _ => "击败场景内的训练敌人"
    };

    private void Complete()
    {
        _completed = true;
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { } session)
            session.SavePlayerHp(_player.Hp);
        _hud.ArenaMessage = "目标完成，正在进入下一场景";
        GetTree().CreateTimer(0.35).Timeout += () => GetTree().ChangeSceneToFile(NextScene);
    }
}
