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
    public const float FloorY = 560f;
    public const float RouteExitGateX = 1050f;
    public const float EncounterExitGateX = 1180f;
    public const float HighPlatformElevation = 96f;
    public const float HighPlatformTopY = FloorY - HighPlatformElevation;
    public static readonly Rect2 LeftHighPlatformRect = new(160f, HighPlatformTopY, 220f, 28f);
    public static readonly Rect2 RightHighPlatformRect = new(900f, HighPlatformTopY, 220f, 28f);
    private const string ExitGateId = "Exit";

    [Export] public bool HasEnemy { get; set; }
    [Export] public string NextScene { get; set; } = "res://Scenes/CoreLoop.tscn";

    public float ActiveExitGateX => HasEnemy ? EncounterExitGateX : RouteExitGateX;

    private PlayerController _player = null!;
    private StageEnemyRoster _roster = null!;
    private LockGateEncounterController? _encounter;
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
        if (HasEnemy)
        {
            _environment.AddFacetedStaticBody(this, LeftHighPlatformRect, drawLitTopEdge: true);
            _environment.AddFacetedStaticBody(this, RightHighPlatformRect, drawLitTopEdge: true);
        }
        _environment.AddGate(ExitGateId, new Vector2(ActiveExitGateX, FloorY),
            HasEnemy ? RuinGateState.Locked : RuinGateState.Unlocked);

        _timeSlow = new TimeSlowManager { Name = "TimeSlowManager", Config = new TimeSlowConfig() };
        AddChild(_timeSlow);
        Vector2 playerSpawn = HasEnemy ? new Vector2(110f, FloorY - 23f) : new Vector2(260f, FloorY - 23f);
        _player = new PlayerController { Name = "Player", Position = playerSpawn, Windows = new CombatWindowConfig(), TimeSlow = _timeSlow.Config };
        AddChild(_player);
        if (GetNodeOrNull<RunSession>("/root/RunSession") is { HasPlayerState: true } session)
            _player.RestoreHp(session.PlayerHp);

        _roster = StageRuntimeWiring.AttachEnemyRoster(this, _timeSlow);

        _camera = new Camera2D { Position = new Vector2(380, -80), LimitLeft = 0, LimitTop = 0, LimitRight = 1280, LimitBottom = 720 };
        _player.AddChild(_camera);
        _environment.Camera = _camera;

        _hud = StageRuntimeWiring.MountStagePresentationAndHud(
            this,
            _player,
            _roster,
            _timeSlow,
            _camera,
            HasEnemy ? "锁门遭遇战 · 第 1/3 波" : "沿路线向右移动，抵达出口进入下一场景");

        if (HasEnemy)
        {
            _encounter = new LockGateEncounterController { Name = "LockGateEncounter" };
            AddChild(_encounter);
            _encounter.WaveChanged += OnEncounterWaveChanged;
            _encounter.Initialize(this, _player, _roster);
        }
    }

    public override void _Process(double delta)
    {
        if (ACTSpike.ACT.Input.ActInput.ResetPressed())
            CleanupStageRuntime(detachEnemies: true);

        if (StageRuntimeWiring.HandleResetIfRequested(this, _timeSlow))
            return;

        _encounter?.Tick();

        bool ready = !HasEnemy || (_encounter?.IsEncounterCleared ?? _roster.AliveCount == 0);
        _environment.SetGateState(ExitGateId, ready ? RuinGateState.Unlocked : RuinGateState.Locked);
        if (HasEnemy)
            _hud.IsObjectiveCleared = ready;

        if (_transitioning || _player.IsDown) return;
        if (ready && _player.GlobalPosition.X >= ActiveExitGateX)
        {
            _transitioning = true;
            RunSession? session = GetNodeOrNull<RunSession>("/root/RunSession");
            if (HasEnemy)
            {
                // Entering the Ruin Guardian boss arena after clearing the lock-gate encounter:
                // restore full session health so CoreLoop begins with full HP, Guard, and TimeSlow.
                session?.Clear();
                _timeSlow.Reset();
                CleanupStageRuntime(detachEnemies: true);
            }
            else
            {
                session?.SavePlayerHp(_player.Hp);
            }
            _hud.IsObjectiveCleared = true;
            _hud.ArenaMessage = "出口已到达，正在进入下一场景";
            StageRuntimeWiring.ScheduleSceneChange(this, NextScene);
        }
    }

    public override void _ExitTree()
    {
        CleanupStageRuntime(detachEnemies: false);
    }

    private void OnEncounterWaveChanged(EncounterWaveChangedCue cue)
    {
        if (_hud is null || !GodotObject.IsInstanceValid(_hud))
            return;

        _hud.ArenaMessage = cue.ObjectiveMessage;
        _hud.IsObjectiveCleared = cue.IsCleared;
    }

    private void CleanupStageRuntime(bool detachEnemies = true)
    {
        if (_encounter is not null && GodotObject.IsInstanceValid(_encounter))
        {
            _encounter.WaveChanged -= OnEncounterWaveChanged;
            _encounter.Cleanup(detachEnemies);
        }
        EnemyProjectile.ClearAll(this);
        TimeSlowManager.EnsureNormalScale();
    }
}
