using System;
using System.Collections.Generic;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>Separate encounter attack token pools for melee and ranged regular enemies.</summary>
public enum AttackTokenPool
{
    Melee,
    Ranged
}

/// <summary>Read-only lease snapshot for an active encounter attack token.</summary>
public readonly record struct AttackTokenLease(
    EnemyController Holder,
    AttackTokenPool Pool,
    float ExpectedHitRealSec);

/// <summary>
/// Centralized encounter attack token scheduler enforcing 2 Melee + 2 Ranged concurrency caps
/// and a minimum 0.35s real-timestamp stagger between expected hit times within the same pool.
/// </summary>
public sealed class EncounterAttackCoordinator
{
    public const int DefaultMeleeCapacity = 2;
    public const int DefaultRangedCapacity = 2;
    public const float DefaultMinHitStaggerSeconds = 0.35f;

    // Tolerance absorbs IEEE-754 float32 subtraction error (e.g. 2.35f - 2.00f == 0.3499999f)
    // while strictly rejecting 1ms-resolution differences below 0.350s (e.g. 0.349s).
    private const float StaggerEpsilonSeconds = 0.0001f;

    private readonly struct PoolBucket
    {
        public int Capacity { get; }
        public List<AttackTokenLease> Leases { get; }

        public PoolBucket(int capacity)
        {
            Capacity = capacity;
            Leases = new List<AttackTokenLease>(capacity);
        }
    }

    private readonly PoolBucket _melee;
    private readonly PoolBucket _ranged;

    public int MeleeCapacity => _melee.Capacity;
    public int RangedCapacity => _ranged.Capacity;
    public float MinHitStaggerSeconds { get; }

    public int ActiveMeleeCount
    {
        get
        {
            PruneInvalid(_melee.Leases);
            return _melee.Leases.Count;
        }
    }

    public int ActiveRangedCount
    {
        get
        {
            PruneInvalid(_ranged.Leases);
            return _ranged.Leases.Count;
        }
    }

    public int TotalActiveCount => ActiveMeleeCount + ActiveRangedCount;

    public EncounterAttackCoordinator(
        int meleeCapacity = DefaultMeleeCapacity,
        int rangedCapacity = DefaultRangedCapacity,
        float minHitStaggerSeconds = DefaultMinHitStaggerSeconds)
    {
        if (meleeCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(meleeCapacity));
        if (rangedCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(rangedCapacity));
        if (!float.IsFinite(minHitStaggerSeconds) || minHitStaggerSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(minHitStaggerSeconds));

        _melee = new PoolBucket(meleeCapacity);
        _ranged = new PoolBucket(rangedCapacity);
        MinHitStaggerSeconds = minHitStaggerSeconds;
    }

    public bool HoldsToken(EnemyController enemy)
        => TryGetLease(enemy, out _);

    public bool TryGetLease(EnemyController enemy, out AttackTokenLease lease)
    {
        if (enemy is null || !GodotObject.IsInstanceValid(enemy))
        {
            lease = default;
            return false;
        }

        if (TryFindLease(_melee.Leases, enemy, out lease))
            return true;

        return TryFindLease(_ranged.Leases, enemy, out lease);
    }

    public bool TryAcquire(
        EnemyController requester,
        AttackTokenPool pool,
        float expectedHitRealSec)
    {
        if (requester is null || !GodotObject.IsInstanceValid(requester) || !float.IsFinite(expectedHitRealSec))
            return false;
        if (!requester.UsesEncounterAttackToken)
            return true;

        PruneInvalid(_melee.Leases);
        PruneInvalid(_ranged.Leases);

        if (FindHolderIndex(_melee.Leases, requester) >= 0 || FindHolderIndex(_ranged.Leases, requester) >= 0)
            return false;

        PoolBucket bucket = SelectBucket(pool);
        if (bucket.Leases.Count >= bucket.Capacity)
            return false;

        if (!SatisfiesHitStagger(bucket.Leases, expectedHitRealSec))
            return false;

        bucket.Leases.Add(new AttackTokenLease(requester, pool, expectedHitRealSec));
        return true;
    }

    public bool Release(EnemyController holder)
    {
        if (holder is null)
            return false;

        bool removed = RemoveHolder(_melee.Leases, holder);
        removed |= RemoveHolder(_ranged.Leases, holder);
        return removed;
    }

    public void Clear()
    {
        _melee.Leases.Clear();
        _ranged.Leases.Clear();
    }

    private PoolBucket SelectBucket(AttackTokenPool pool)
        => pool == AttackTokenPool.Ranged ? _ranged : _melee;

    private bool SatisfiesHitStagger(List<AttackTokenLease> leases, float expectedHitRealSec)
    {
        for (int i = 0; i < leases.Count; i++)
        {
            float delta = Mathf.Abs(expectedHitRealSec - leases[i].ExpectedHitRealSec);
            if (delta + StaggerEpsilonSeconds < MinHitStaggerSeconds)
                return false;
        }

        return true;
    }

    private static bool TryFindLease(List<AttackTokenLease> leases, EnemyController enemy, out AttackTokenLease lease)
    {
        PruneInvalid(leases);
        int index = FindHolderIndex(leases, enemy);
        if (index >= 0)
        {
            lease = leases[index];
            return true;
        }

        lease = default;
        return false;
    }

    private static int FindHolderIndex(List<AttackTokenLease> leases, EnemyController holder)
    {
        for (int i = 0; i < leases.Count; i++)
        {
            if (ReferenceEquals(leases[i].Holder, holder))
                return i;
        }
        return -1;
    }

    private static bool RemoveHolder(List<AttackTokenLease> leases, EnemyController holder)
    {
        bool removed = false;
        for (int i = leases.Count - 1; i >= 0; i--)
        {
            EnemyController current = leases[i].Holder;
            if (!GodotObject.IsInstanceValid(current))
            {
                leases.RemoveAt(i);
                continue;
            }
            if (ReferenceEquals(current, holder))
            {
                leases.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    private static void PruneInvalid(List<AttackTokenLease> leases)
    {
        for (int i = leases.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(leases[i].Holder))
                leases.RemoveAt(i);
        }
    }
}
