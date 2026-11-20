using System;
using System.Collections.Generic;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.Audio;

/// <summary>
/// Read-only presentation node subscribing to combat and time-slow events and dispatching
/// pre-synthesized 16-bit PCM cues across a polyphonic AudioStreamPlayer pool.
/// </summary>
public partial class CombatAudioController : Node
{
    private const int MinPoolSize = 1;

    private readonly List<AudioStreamPlayer> _pool = new();
    private PlayerController? _player;
    private EnemyController? _enemy;
    private TimeSlowManager? _timeSlow;
    private float _volumeDb = -6f;
    private int _poolSize = 8;
    private int _nextVoiceIndex;
    private bool _subscribed;
    private bool _silent;

    /// <summary>Master gain (in dB) applied to all voices in the polyphonic pool.</summary>
    [Export]
    public float VolumeDb
    {
        get => _volumeDb;
        set
        {
            _volumeDb = value;
            ApplyVolumeToPool();
        }
    }

    /// <summary>Number of polyphonic AudioStreamPlayer voices created on startup.</summary>
    [Export]
    public int PoolSize
    {
        get => _poolSize;
        set => _poolSize = Math.Max(MinPoolSize, value);
    }

    public int PoolCount => _pool.Count;
    public bool IsSilentMode => _silent;
    public bool IsSubscribed => _subscribed;
    public int PlayedCueCount { get; private set; }
    public CombatSfxId? LastCue { get; private set; }

    /// <summary>Creates, binds, and attaches a CombatAudioController child to the stage parent.</summary>
    public static CombatAudioController Attach(
        Node parent,
        PlayerController player,
        EnemyController? enemy,
        TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(timeSlow);

        var controller = new CombatAudioController { Name = "CombatAudio" };
        controller.Bind(player, enemy, timeSlow);
        parent.AddChild(controller);
        return controller;
    }

    /// <summary>Binds read-only event sources. Enemy may be null in traversal/non-combat stages.</summary>
    public void Bind(PlayerController player, EnemyController? enemy, TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(timeSlow);

        if (_subscribed)
            Unsubscribe();

        _player = player;
        _enemy = enemy;
        _timeSlow = timeSlow;

        if (IsInsideTree())
            Subscribe();
    }

    public override void _EnterTree()
    {
        Subscribe();
    }

    public override void _Ready()
    {
        _silent = DisplayServer.GetName() == "headless" || OS.GetEnvironment("ACT_SMOKE") == "1";
        ProceduralSfxBank.Prewarm();
        EnsurePool();
        Subscribe();
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        StopPool();
    }

    /// <summary>Plays a procedural SFX cue using the polyphonic pool (silent in headless/smoke mode).</summary>
    public void Play(CombatSfxId id)
    {
        EnsurePool();
        AudioStreamWav stream = ProceduralSfxBank.GetStream(id);
        AudioStreamPlayer voice = AcquireVoice();
        voice.Stream = stream;
        voice.VolumeDb = _volumeDb;

        PlayedCueCount++;
        LastCue = id;

        if (!_silent)
            voice.Play();
    }

    /// <summary>Maps a defense resolution to its audio cue, avoiding duplicate cues for normal dodges.</summary>
    public static bool TryMapDefenseCue(DefenseResult result, out CombatSfxId cue)
    {
        switch (result)
        {
            case DefenseResult.Hit:
                cue = CombatSfxId.PlayerDamaged;
                return true;
            case DefenseResult.Blocked:
                cue = CombatSfxId.BlockNormal;
                return true;
            case DefenseResult.PerfectBlocked:
                cue = CombatSfxId.BlockPerfect;
                return true;
            case DefenseResult.PerfectDodged:
                cue = CombatSfxId.DodgePerfect;
                return true;
            default:
                cue = default;
                return false;
        }
    }

    /// <summary>Maps an enemy attack variant to its distinct telegraph alert cue.</summary>
    public static CombatSfxId MapTelegraphCue(BossAttackKind kind) => kind switch
    {
        BossAttackKind.BreakGrab => CombatSfxId.TelegraphBreakGrab,
        BossAttackKind.DelayedArea => CombatSfxId.TelegraphDelayedArea,
        _ => CombatSfxId.TelegraphNormal
    };

    /// <summary>Maps TimeSlowManager state messages to start/end sweep cues.</summary>
    public static bool TryMapTimeSlowCue(string? stateMessage, out CombatSfxId cue)
    {
        if (!string.IsNullOrEmpty(stateMessage))
        {
            if (stateMessage.StartsWith("time slow started", StringComparison.Ordinal))
            {
                cue = CombatSfxId.TimeSlowStart;
                return true;
            }
            if (stateMessage.StartsWith("time slow ended", StringComparison.Ordinal))
            {
                cue = CombatSfxId.TimeSlowEnd;
                return true;
            }
        }
        cue = default;
        return false;
    }

    private void EnsurePool()
    {
        if (_pool.Count > 0) return;

        int count = Math.Max(MinPoolSize, _poolSize);
        for (int i = 0; i < count; i++)
        {
            var voice = new AudioStreamPlayer
            {
                Name = $"Voice{i}",
                VolumeDb = _volumeDb
            };
            AddChild(voice);
            _pool.Add(voice);
        }
    }

    private AudioStreamPlayer AcquireVoice()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].Playing)
                return _pool[i];
        }

        AudioStreamPlayer stolen = _pool[_nextVoiceIndex];
        _nextVoiceIndex = (_nextVoiceIndex + 1) % _pool.Count;
        return stolen;
    }

    private void ApplyVolumeToPool()
    {
        for (int i = 0; i < _pool.Count; i++)
            _pool[i].VolumeDb = _volumeDb;
    }

    private void StopPool()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Playing)
                _pool[i].Stop();
        }
    }

    private void Subscribe()
    {
        if (_subscribed || _player is null || _timeSlow is null) return;

        _player.AttackStarted += OnAttackStarted;
        _player.AttackLanded += OnAttackLanded;
        _player.DodgeStarted += OnDodgeStarted;
        _player.DefenseResolved += OnDefenseResolved;
        if (_enemy is not null)
            _enemy.TelegraphStarted += OnTelegraphStarted;
        _timeSlow.StateChanged += OnTimeSlowStateChanged;

        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (_player is not null)
        {
            _player.AttackStarted -= OnAttackStarted;
            _player.AttackLanded -= OnAttackLanded;
            _player.DodgeStarted -= OnDodgeStarted;
            _player.DefenseResolved -= OnDefenseResolved;
        }
        if (_enemy is not null)
            _enemy.TelegraphStarted -= OnTelegraphStarted;
        if (_timeSlow is not null)
            _timeSlow.StateChanged -= OnTimeSlowStateChanged;

        _subscribed = false;
    }

    private void OnAttackStarted(PlayerAttackCue cue)
        => Play(cue.IsSkill ? CombatSfxId.PlayerSkillDash : CombatSfxId.PlayerSlash);

    private void OnAttackLanded(PlayerHitCue cue)
        => Play(CombatSfxId.PlayerHit);

    private void OnDodgeStarted(PlayerDodgeCue cue)
        => Play(CombatSfxId.DodgeNormal);

    private void OnDefenseResolved(PlayerDefenseCue cue)
    {
        if (TryMapDefenseCue(cue.Result, out var sfx))
            Play(sfx);
    }

    private void OnTelegraphStarted(EnemyTelegraphCue cue)
        => Play(MapTelegraphCue(cue.Attack.Kind));

    private void OnTimeSlowStateChanged(string stateMessage)
    {
        if (TryMapTimeSlowCue(stateMessage, out var sfx))
            Play(sfx);
    }
}
