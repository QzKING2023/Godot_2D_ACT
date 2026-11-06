using Godot;

namespace ACTSpike.ACT.Config;

/// <summary>
/// Time-slow (Q / Xbox LB) tuning. The resource bar drains while active,
/// regenerates while inactive, and time slow force-stops at zero.
/// </summary>
[GlobalClass]
public partial class TimeSlowConfig : Resource
{
    [Export] public float TimeScale { get; set; } = 0.35f;
    [Export] public float MaxResource { get; set; } = 100f;
    [Export] public float DrainPerSecond { get; set; } = 30f;
    [Export] public float RegenPerSecond { get; set; } = 12f;
    [Export] public float MinResourceToActivate { get; set; } = 10f;
    [Export] public float PerfectDodgeRefund { get; set; } = 20f;
}
