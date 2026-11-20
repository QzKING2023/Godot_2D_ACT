using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using Godot;

namespace ACTSpike.ACT.Arena;

public enum EncounterEnemyArchetype
{
    MeleeScout,
    RangedArcher,
    HeavyShield
}

public readonly record struct EncounterSpawnSpec(
    EncounterEnemyArchetype Archetype,
    Vector2 Position,
    string NodeName);

public readonly record struct EncounterWaveSpec(
    int WaveNumber,
    string Label,
    IReadOnlyList<EncounterSpawnSpec> Spawns);

public readonly record struct EncounterWaveChangedCue(
    int WaveNumber,
    int TotalWaves,
    bool IsCleared,
    string ObjectiveMessage);

/// <summary>
/// Orchestrates a multi-wave「锁门遭遇战」:
/// spawns progressive enemy formations across ground and 96px high platforms, registers all enemies
/// into StageEnemyRoster (2+2 staggered attack tokens, visual/audio/feedback/overhead bars),
/// advances waves via real timestamps, and cleans up all enemies/timers/projectiles on reset or exit.
/// </summary>
public partial class LockGateEncounterController : Node
{
    public const float DefaultInterWaveDelaySeconds = 0.65f;
    private const float PauseGapThresholdSeconds = 0.25f;

    [Export] public float InterWaveDelaySeconds { get; set; } = DefaultInterWaveDelaySeconds;

    private readonly List<EnemyController> _activeWaveEnemies = new();
    private IReadOnlyList<EncounterWaveSpec> _waves = Array.Empty<EncounterWaveSpec>();
    private Node2D? _stage;
    private PlayerController? _player;
    private StageEnemyRoster? _roster;
    private bool _initialized;
    private int _currentWaveIndex = -1;
    private float _nextWaveAtRealSec = -1f;
    private float _lastTickRealSec = -1f;

    public int CurrentWaveNumber => _currentWaveIndex >= 0 ? _waves[_currentWaveIndex].WaveNumber : 0;
    public int TotalWaves => _waves.Count;
    public bool IsWaitingForNextWave { get; private set; }
    public bool IsEncounterCleared { get; private set; }
    public string CurrentObjectiveMessage { get; private set; } = string.Empty;

    public event Action<EncounterWaveChangedCue>? WaveChanged;

    private static float Now => Time.GetTicksMsec() / 1000f;

    public static IReadOnlyList<EncounterWaveSpec> CreateDefaultWaves()
    {
        float scoutFloorY = FormalStage.FloorY - MeleeScoutEnemy.DefaultBodySize.Y * 0.5f;   // 528
        float shieldFloorY = FormalStage.FloorY - 50f;                                        // 510
        float archerHighY = FormalStage.HighPlatformTopY - 42f;                               // 422

        return new[]
        {
            new EncounterWaveSpec(
                WaveNumber: 1,
                Label: "第 1/3 波",
                Spawns: new[]
                {
                    new EncounterSpawnSpec(EncounterEnemyArchetype.MeleeScout, new Vector2(560f, scoutFloorY), "MeleeScout_W1_A"),
                    new EncounterSpawnSpec(EncounterEnemyArchetype.MeleeScout, new Vector2(780f, scoutFloorY), "MeleeScout_W1_B")
                }),
            new EncounterWaveSpec(
                WaveNumber: 2,
                Label: "第 2/3 波",
                Spawns: new[]
                {
                    new EncounterSpawnSpec(EncounterEnemyArchetype.MeleeScout, new Vector2(620f, scoutFloorY), "MeleeScout_W2_A"),
                    new EncounterSpawnSpec(EncounterEnemyArchetype.RangedArcher, new Vector2(980f, archerHighY), "RangedArcher_W2_R")
                }),
            new EncounterWaveSpec(
                WaveNumber: 3,
                Label: "第 3/3 波",
                Spawns: new[]
                {
                    new EncounterSpawnSpec(EncounterEnemyArchetype.MeleeScout, new Vector2(500f, scoutFloorY), "MeleeScout_W3_A"),
                    new EncounterSpawnSpec(EncounterEnemyArchetype.HeavyShield, new Vector2(740f, shieldFloorY), "HeavyShield_W3_B"),
                    new EncounterSpawnSpec(EncounterEnemyArchetype.RangedArcher, new Vector2(300f, archerHighY), "RangedArcher_W3_L"),
                    new EncounterSpawnSpec(EncounterEnemyArchetype.RangedArcher, new Vector2(980f, archerHighY), "RangedArcher_W3_R")
                })
        };
    }

    public void Initialize(
        Node2D stage,
        PlayerController player,
        StageEnemyRoster roster)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(roster);

        Cleanup();

        _stage = stage;
        _player = player;
        _roster = roster;
        _waves = CreateDefaultWaves();
        _initialized = true;
        _currentWaveIndex = -1;
        IsWaitingForNextWave = false;
        IsEncounterCleared = false;
        _nextWaveAtRealSec = -1f;
        _lastTickRealSec = Now;

        SpawnWave(0);
    }

    public void Tick()
    {
        if (!_initialized || IsEncounterCleared)
            return;

        if (IsInsideTree() && GetTree().Paused)
            return;

        float now = Now;
        if (_lastTickRealSec >= 0f && IsWaitingForNextWave)
        {
            float elapsed = now - _lastTickRealSec;
            if (elapsed > PauseGapThresholdSeconds)
                _nextWaveAtRealSec += elapsed;
        }
        _lastTickRealSec = now;

        if (IsWaitingForNextWave)
        {
            if (InterWaveDelaySeconds <= 0f || now >= _nextWaveAtRealSec)
            {
                IsWaitingForNextWave = false;
                _nextWaveAtRealSec = -1f;
                SpawnWave(_currentWaveIndex + 1);
            }
            return;
        }

        if (!AreActiveWaveEnemiesDefeated())
            return;

        int nextIndex = _currentWaveIndex + 1;
        if (nextIndex >= _waves.Count)
        {
            IsEncounterCleared = true;
            CurrentObjectiveMessage = "锁门遭遇战已清空 · 出口门柱已解锁";
            EmitWaveChanged();
            return;
        }

        if (InterWaveDelaySeconds <= 0f)
        {
            SpawnWave(nextIndex);
            return;
        }

        IsWaitingForNextWave = true;
        _nextWaveAtRealSec = now + InterWaveDelaySeconds;
        string nextLabel = nextIndex == _waves.Count - 1 ? "最终波逼近" : "下一波逼近";
        CurrentObjectiveMessage = $"锁门遭遇战 · 第 {CurrentWaveNumber}/{TotalWaves} 波已清空，{nextLabel}";
        EmitWaveChanged();
    }

    public void Cleanup(bool detachFromStage = true)
    {
        IsWaitingForNextWave = false;
        _nextWaveAtRealSec = -1f;
        _lastTickRealSec = -1f;
        _initialized = false;

        if (_stage is not null && GodotObject.IsInstanceValid(_stage))
            EnemyProjectile.ClearAll(_stage);

        ClearTrackedEnemies(detachFromStage);
        _roster?.AttackCoordinator.Clear();
    }

    public override void _ExitTree()
    {
        Cleanup(detachFromStage: false);
    }

    private void SpawnWave(int waveIndex)
    {
        if (_stage is null || _player is null || _roster is null ||
            !GodotObject.IsInstanceValid(_stage) || !GodotObject.IsInstanceValid(_player))
        {
            return;
        }

        ClearTrackedEnemies(detachFromStage: true);

        _currentWaveIndex = waveIndex;
        EncounterWaveSpec wave = _waves[waveIndex];

        foreach (EncounterSpawnSpec spec in wave.Spawns)
        {
            EnemyController enemy = CreateEnemy(spec);
            _stage.AddChild(enemy);
            enemy.Bind(_player, _player.Windows);
            _roster.Register(enemy);
            _activeWaveEnemies.Add(enemy);
        }

        CurrentObjectiveMessage = $"锁门遭遇战 · {wave.Label}";
        EmitWaveChanged();
    }

    private static EnemyController CreateEnemy(EncounterSpawnSpec spec) => spec.Archetype switch
    {
        EncounterEnemyArchetype.MeleeScout => new MeleeScoutEnemy
        {
            Name = spec.NodeName,
            Position = spec.Position
        },
        EncounterEnemyArchetype.RangedArcher => new RangedArcherEnemy
        {
            Name = spec.NodeName,
            Position = spec.Position
        },
        EncounterEnemyArchetype.HeavyShield => new HeavyShieldEnemy
        {
            Name = spec.NodeName,
            Position = spec.Position
        },
        _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Archetype, "Unsupported encounter enemy archetype")
    };

    private bool AreActiveWaveEnemiesDefeated()
    {
        for (int i = 0; i < _activeWaveEnemies.Count; i++)
        {
            EnemyController enemy = _activeWaveEnemies[i];
            if (GodotObject.IsInstanceValid(enemy) && !enemy.IsDefeated)
                return false;
        }
        return true;
    }

    private void ClearTrackedEnemies(bool detachFromStage)
    {
        for (int i = 0; i < _activeWaveEnemies.Count; i++)
        {
            EnemyController enemy = _activeWaveEnemies[i];
            if (!GodotObject.IsInstanceValid(enemy))
                continue;
            _roster?.Unregister(enemy);
            if (detachFromStage)
            {
                enemy.GetParent()?.RemoveChild(enemy);
                enemy.Free();
            }
            else if (!enemy.IsQueuedForDeletion())
            {
                enemy.QueueFree();
            }
        }
        _activeWaveEnemies.Clear();
    }

    private void EmitWaveChanged()
    {
        WaveChanged?.Invoke(new EncounterWaveChangedCue(
            WaveNumber: CurrentWaveNumber,
            TotalWaves: TotalWaves,
            IsCleared: IsEncounterCleared,
            ObjectiveMessage: CurrentObjectiveMessage));
    }
}
