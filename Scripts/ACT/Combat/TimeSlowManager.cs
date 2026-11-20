using System;
using Godot;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Input;

namespace ACTSpike.ACT.Combat;

/// <summary>
/// Owns the time-slow resource bar and Engine.TimeScale.
/// Hold Q / Xbox LB to activate; drains per real second while active,
/// regenerates while inactive, force-stops at zero.
/// Real delta is derived from msec timestamps so it is immune to TimeScale.
/// </summary>
public partial class TimeSlowManager : Node
{
    public TimeSlowConfig Config { get; set; } = new();
    public float Resource { get; private set; }
    public bool Active { get; private set; }

    public event Action<string>? StateChanged;
    public event Action<bool>? ActiveChanged;

    private float _lastMsec = -1f;
    private bool _activationArmed = true;

    public static void EnsureNormalScale() => Engine.TimeScale = 1.0;

    public override void _Ready()
    {
        Resource = Config.MaxResource;
        Engine.TimeScale = 1.0;
    }

    public override void _Process(double delta)
    {
        if (IsInsideTree() && GetTree().Paused)
        {
            HandlePaused();
            return;
        }

        float now = Time.GetTicksMsec() / 1000f;
        float realDt = _lastMsec < 0 ? 0f : now - _lastMsec;
        _lastMsec = now;

        bool keyHeld = ActInput.TimeSlowHeld();
        bool canActivate = keyHeld && _activationArmed && Resource >= Config.MinResourceToActivate;
        bool mustStop = Resource <= 0f;

        if (Active && mustStop)
        {
            _activationArmed = false;
            SetActive(false, "time slow ended: resource empty");
        }
        else if (Active && !keyHeld)
        {
            SetActive(false, "time slow ended: key released");
        }
        else if (!Active && canActivate)
        {
            SetActive(true, $"time slow started (x{Config.TimeScale:0.00})");
        }
        if (!keyHeld) _activationArmed = true; // re-arm after release

        if (Active)
        {
            Resource = Mathf.Max(0f, Resource - Config.DrainPerSecond * realDt);
            // Stop in the same tick that the resource reaches zero. This keeps
            // the exhaustion contract deterministic instead of leaving one
            // extra slowed frame until the next _Process call.
            if (Resource <= 0f)
            {
                _activationArmed = false;
                SetActive(false, "time slow ended: resource empty");
            }
        }
        else
            Resource = Mathf.Min(Config.MaxResource, Resource + Config.RegenPerSecond * realDt);

        Engine.TimeScale = Active ? Config.TimeScale : 1.0;
    }

    public void AddResource(float amount)
    {
        Resource = Mathf.Clamp(Resource + amount, 0f, Config.MaxResource);
    }

    public float ResourceFraction => Resource / Config.MaxResource;

    public void SetPaused(bool paused)
    {
        if (paused)
        {
            HandlePaused();
            return;
        }

        _lastMsec = -1f;
        Engine.TimeScale = Active ? Config.TimeScale : 1.0;
    }

    public void Reset()
    {
        Resource = Config.MaxResource;
        _activationArmed = true;
        _lastMsec = -1f;
        Engine.TimeScale = 1.0;
        SetActive(false, "time resource reset");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPaused)
            HandlePaused();
        else if (what == NotificationUnpaused)
            _lastMsec = -1f;
    }

    public override void _ExitTree()
    {
        Active = false;
        Engine.TimeScale = 1.0; // never leak a slowed engine state
    }

    private void SetActive(bool next, string? stateMessage = null)
    {
        bool changed = Active != next;
        Active = next;
        if (changed)
            ActiveChanged?.Invoke(next);
        if (!string.IsNullOrEmpty(stateMessage))
            StateChanged?.Invoke(stateMessage);
    }

    private void HandlePaused()
    {
        Engine.TimeScale = 1.0;
        _lastMsec = -1f;
        if (Active)
            SetActive(false, "time slow ended: paused");
    }
}
