using System;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Marks a static headless smoke probe (<c>public static void Check(Node host, Action&lt;string&gt; fail)</c>)
/// for automatic ordered discovery by <see cref="SmokeTest"/>, avoiding merge-conflict hotspots
/// when concurrent tickets add new smoke checks.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SmokeProbeAttribute : Attribute
{
    public SmokeProbeAttribute(int order)
    {
        Order = order;
    }

    public int Order { get; }
}
