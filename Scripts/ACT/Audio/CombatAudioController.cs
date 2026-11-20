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
    private const int VoicePoolSize = 8;

    private readonly List<AudioStreamPlayer> _pool = new();
    private PlayerController? _player;
    private StageEnemyRoster? _roster;
    private StageEnemyRoster? _ownedRoster;
    private TimeSlowManager? _timeSlow;
    private float _volumeDb = -6f;
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

    public int PoolCount => _pool.Count;
    public bool IsSilentMode => _silent;
    public bool IsSubscribed => _subscribed;
    public int PlayedCueCount { get; private set; }
    public CombatSfxId? LastCue { get; private set; }

    /// <summary>Creates, binds to a multi-enemy roster, and attaches a CombatAudioController child to the stage parent.</summary>
    public static CombatAudioController AttachRoster(
        Node parent,
        PlayerController player,
        StageEnemyRoster roster,
        TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(timeSlow);

        var controller = new CombatAudioController { Name = "CombatAudio" };
        controller.BindRoster(player, roster, timeSlow);
        parent.AddChild(controller);
        return controller;
    }

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

    /// <summary>Binds read-only event sources using a stage-level multi-enemy roster.</summary>
    public void BindRoster(PlayerController player, StageEnemyRoster roster, TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(timeSlow);

        if (_subscribed)
            Unsubscribe();
        ReleaseOwnedRoster();

        _player = player;
        _roster = roster;
        _timeSlow = timeSlow;

        if (IsInsideTree())
            Subscribe();
    }

    /// <summary>Binds read-only event sources. Enemy may be null in traversal/non-combat stages.</summary>
    public void Bind(PlayerController player, EnemyController? enemy, TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(timeSlow);

        if (_subscribed)
            Unsubscribe();
        ReleaseOwnedRoster();

        _ownedRoster = new StageEnemyRoster { Name = "OwnedEnemyRoster" };
        if (enemy is not null)
            _ownedRoster.Register(enemy);
        AddChild(_ownedRoster);

        _player = player;
        _roster = _ownedRoster;
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
        _ownedRoster?.Clear();
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
        => TryMapDefenseCue(result, projectileReflected: false, out cue);

    /// <summary>Maps a defense resolution (including projectile reflect) to its audio cue.</summary>
    public static bool TryMapDefenseCue(DefenseResult result, bool projectileReflected, out CombatSfxId cue)
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
                cue = projectileReflected ? CombatSfxId.ProjectileReflect : CombatSfxId.BlockPerfect;
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

    /// <summary>Maps TimeSlowManager active transitions to start/end sweep cues.</summary>
    public static CombatSfxId MapTimeSlowCue(bool active)
        => active ? CombatSfxId.TimeSlowStart : CombatSfxId.TimeSlowEnd;

    private void EnsurePool()
    {
        if (_pool.Count > 0) return;

        for (int i = 0; i < VoicePoolSize; i++)
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

    private void ReleaseOwnedRoster()
    {
        if (_ownedRoster is null) return;
        if (GodotObject.IsInstanceValid(_ownedRoster))
        {
            _ownedRoster.Clear();
            if (_ownedRoster.GetParent() == this)
                RemoveChild(_ownedRoster);
            _ownedRoster.QueueFree();
        }
        _ownedRoster = null;
    }

    private void Subscribe()
    {
        if (_subscribed || _player is null || _timeSlow is null) return;

        _player.AttackStarted += OnAttackStarted;
        _player.AttackLanded += OnAttackLanded;
        _player.DodgeStarted += OnDodgeStarted;
        _player.DefenseResolved += OnDefenseResolved;
        if (_roster is not null && GodotObject.IsInstanceValid(_roster))
        {
            _roster.EnemyTelegraphStarted += OnRosterTelegraphStarted;
            _roster.EnemyShieldDeflected += OnRosterShieldDeflected;
            _roster.EnemyProjectileFired += OnRosterProjectileFired;
            _roster.EnemyOverloadPulseTriggered += OnRosterOverloadPulseTriggered;
        }
        _timeSlow.ActiveChanged += OnTimeSlowActiveChanged;

        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _player.AttackStarted -= OnAttackStarted;
            _player.AttackLanded -= OnAttackLanded;
            _player.DodgeStarted -= OnDodgeStarted;
            _player.DefenseResolved -= OnDefenseResolved;
        }
        if (_roster is not null && GodotObject.IsInstanceValid(_roster))
        {
            _roster.EnemyTelegraphStarted -= OnRosterTelegraphStarted;
            _roster.EnemyShieldDeflected -= OnRosterShieldDeflected;
            _roster.EnemyProjectileFired -= OnRosterProjectileFired;
            _roster.EnemyOverloadPulseTriggered -= OnRosterOverloadPulseTriggered;
        }
        if (_timeSlow is not null && GodotObject.IsInstanceValid(_timeSlow))
            _timeSlow.ActiveChanged -= OnTimeSlowActiveChanged;

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
        if (TryMapDefenseCue(cue.Result, cue.ProjectileReflected, out var sfx))
            Play(sfx);
    }

    private void OnRosterTelegraphStarted(RosterEnemyTelegraphCue rosterCue)
        => Play(MapTelegraphCue(rosterCue.Cue.Attack.Kind));

    private void OnRosterShieldDeflected(RosterEnemyShieldDeflectedCue rosterCue)
        => Play(CombatSfxId.ShieldDeflect);

    private void OnRosterProjectileFired(RosterEnemyProjectileFiredCue rosterCue)
        => Play(CombatSfxId.ProjectileFire);

    private void OnRosterOverloadPulseTriggered(RosterEnemyOverloadPulseCue rosterCue)
        => Play(CombatSfxId.BossOverloadPulse);

    private void OnTimeSlowActiveChanged(bool active)
        => Play(MapTimeSlowCue(active));
}
