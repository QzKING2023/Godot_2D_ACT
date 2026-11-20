using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// Stage-level enemy registry and presentation broadcast seam.
/// Supports zero, one, or many enemies, runtime registration/unregistration,
/// automatic TreeExiting unbind with zero leaks, and primary boss tracking.
/// </summary>
public partial class StageEnemyRoster : Node
{
    private sealed class EnemyBinding
    {
        public required Action<EnemyTelegraphCue> OnTelegraph { get; init; }
        public required Action<EnemyStrikeCue> OnStrike { get; init; }
        public required Action<EnemyDamagedCue> OnDamaged { get; init; }
        public required Action<EnemyShieldDeflectedCue> OnShieldDeflected { get; init; }
        public required Action<EnemyShieldBrokenCue> OnShieldBroken { get; init; }
        public required Action<EnemyProjectileFiredCue> OnProjectileFired { get; init; }
        public required Action<EnemyOverloadPulseCue> OnOverloadPulseTriggered { get; init; }
        public required Action OnTreeExiting { get; init; }
    }

    private readonly List<EnemyController> _enemies = new();
    private readonly Dictionary<EnemyController, EnemyBinding> _bindings = new();
    private TimeSlowManager? _timeSlow;
    private EnemyController? _primaryBoss;

    public IReadOnlyList<EnemyController> Enemies => _enemies;
    public EncounterAttackCoordinator AttackCoordinator { get; } = new();
    public int Count => _enemies.Count;
    public EnemyController? PrimaryBoss
        => _primaryBoss is not null && GodotObject.IsInstanceValid(_primaryBoss) ? _primaryBoss : null;
    public bool HasBoss => PrimaryBoss is not null;

    public int AliveCount
    {
        get
        {
            int alive = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyController enemy = _enemies[i];
                if (GodotObject.IsInstanceValid(enemy) && !enemy.IsDefeated)
                    alive++;
            }
            return alive;
        }
    }

    /// <summary>Read-only presentation events forwarded from all currently registered enemies.</summary>
    public event Action<RosterEnemyTelegraphCue>? EnemyTelegraphStarted;
    public event Action<RosterEnemyStrikeCue>? EnemyStrikeActivated;
    public event Action<RosterEnemyDamagedCue>? EnemyDamaged;
    public event Action<RosterEnemyShieldDeflectedCue>? EnemyShieldDeflected;
    public event Action<RosterEnemyShieldBrokenCue>? EnemyShieldBroken;
    public event Action<RosterEnemyProjectileFiredCue>? EnemyProjectileFired;
    public event Action<RosterEnemyOverloadPulseCue>? EnemyOverloadPulseTriggered;

    /// <summary>Lifecycle event fired when an enemy leaves the roster (manual unregister, TreeExiting, or Clear).</summary>
    public event Action<EnemyController>? EnemyUnregistered;

    public void BindTimeSlow(TimeSlowManager? timeSlow)
    {
        _timeSlow = timeSlow;
        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyController enemy = _enemies[i];
            if (GodotObject.IsInstanceValid(enemy))
                enemy.BindTimeSlow(timeSlow);
        }
    }

    public bool Contains(EnemyController enemy)
        => enemy is not null && _bindings.ContainsKey(enemy);

    public bool Register(EnemyController enemy)
    {
        if (enemy is null || !GodotObject.IsInstanceValid(enemy) || _bindings.ContainsKey(enemy))
            return false;

        var binding = new EnemyBinding
        {
            OnTelegraph = cue => EnemyTelegraphStarted?.Invoke(new RosterEnemyTelegraphCue(enemy, cue)),
            OnStrike = cue => EnemyStrikeActivated?.Invoke(new RosterEnemyStrikeCue(enemy, cue)),
            OnDamaged = cue =>
            {
                if (cue.Defeated && enemy.IsBoss)
                    UpdatePrimaryBoss();
                EnemyDamaged?.Invoke(new RosterEnemyDamagedCue(enemy, cue));
            },
            OnShieldDeflected = cue => EnemyShieldDeflected?.Invoke(new RosterEnemyShieldDeflectedCue(enemy, cue)),
            OnShieldBroken = cue => EnemyShieldBroken?.Invoke(new RosterEnemyShieldBrokenCue(enemy, cue)),
            OnProjectileFired = cue => EnemyProjectileFired?.Invoke(new RosterEnemyProjectileFiredCue(enemy, cue)),
            OnOverloadPulseTriggered = cue => EnemyOverloadPulseTriggered?.Invoke(new RosterEnemyOverloadPulseCue(enemy, cue)),
            OnTreeExiting = () => Unregister(enemy)
        };

        enemy.TelegraphStarted += binding.OnTelegraph;
        enemy.StrikeActivated += binding.OnStrike;
        enemy.Damaged += binding.OnDamaged;
        enemy.ShieldDeflected += binding.OnShieldDeflected;
        enemy.ShieldBroken += binding.OnShieldBroken;
        enemy.ProjectileFired += binding.OnProjectileFired;
        enemy.OverloadPulseTriggered += binding.OnOverloadPulseTriggered;
        enemy.TreeExiting += binding.OnTreeExiting;

        if (_timeSlow is not null)
            enemy.BindTimeSlow(_timeSlow);
        enemy.BindAttackCoordinator(AttackCoordinator);

        _bindings[enemy] = binding;
        _enemies.Add(enemy);

        UpdatePrimaryBoss();
        return true;
    }

    public void RegisterRange(IEnumerable<EnemyController?> enemies)
    {
        if (enemies is null) return;
        foreach (EnemyController? enemy in enemies)
        {
            if (enemy is not null)
                Register(enemy);
        }
    }

    public bool Unregister(EnemyController enemy)
    {
        if (enemy is null || !_bindings.Remove(enemy, out EnemyBinding? binding))
            return false;

        _enemies.Remove(enemy);

        enemy.TelegraphStarted -= binding.OnTelegraph;
        enemy.StrikeActivated -= binding.OnStrike;
        enemy.Damaged -= binding.OnDamaged;
        enemy.ShieldDeflected -= binding.OnShieldDeflected;
        enemy.ShieldBroken -= binding.OnShieldBroken;
        enemy.ProjectileFired -= binding.OnProjectileFired;
        enemy.OverloadPulseTriggered -= binding.OnOverloadPulseTriggered;
        AttackCoordinator.Release(enemy);
        if (GodotObject.IsInstanceValid(enemy))
        {
            enemy.TreeExiting -= binding.OnTreeExiting;
            enemy.BindAttackCoordinator(null);
        }

        UpdatePrimaryBoss();
        EnemyUnregistered?.Invoke(enemy);
        return true;
    }

    public void Clear()
    {
        if (_enemies.Count > 0)
        {
            EnemyController[] snapshot = _enemies.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                Unregister(snapshot[i]);
        }
        AttackCoordinator.Clear();
        if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            EnemyProjectile.ClearAll(GetParent());
    }

    public override void _ExitTree()
    {
        Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Clear();
        base.Dispose(disposing);
    }

    private void UpdatePrimaryBoss()
    {
        EnemyController? fallbackDefeatedBoss = null;
        EnemyController? nextBoss = null;

        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyController enemy = _enemies[i];
            if (!GodotObject.IsInstanceValid(enemy) || !enemy.IsBoss)
                continue;

            if (!enemy.IsDefeated)
            {
                nextBoss = enemy;
                break;
            }

            fallbackDefeatedBoss ??= enemy;
        }

        _primaryBoss = nextBoss ?? fallbackDefeatedBoss;
    }
}
