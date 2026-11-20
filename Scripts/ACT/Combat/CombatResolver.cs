using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;

namespace ACTSpike.ACT.Combat;

public enum DefenseResult
{
    Hit,
    Blocked,
    PerfectBlocked,
    Dodged,
    PerfectDodged,
    Invulnerable
}

/// <summary>Explicitly distinguishes melee direct strikes from ranged projectiles.</summary>
public enum AttackDeliveryKind
{
    Melee,
    Projectile
}

/// <summary>Player defensive state at the moment an enemy strike lands. Timestamps are real seconds.</summary>
public readonly record struct DefenseSnapshot(
    bool IsDodging,
    float DodgeStartedAtSec,
    bool IsBlocking,
    float BlockHeldSinceSec,
    bool IsInvulnerable,
    float NowSec);

/// <summary>Outcome decided by the resolver; the player applies the numbers.</summary>
public readonly record struct AttackResolution(
    DefenseResult Result,
    float DamageToHp,
    float DamageToGuard,
    float TimeResourceRefund,
    bool EnemyStaggered,
    bool ProjectileReflected = false);

/// <summary>
/// Pure combat judgement. One place decides hit / block / perfect variants.
/// Perfect windows are "defensive action began within N seconds before impact"
/// and are measured in real seconds (see CombatWindowConfig), never in frames.
/// </summary>
public static class CombatResolver
{
    public static AttackResolution Resolve(
        DefenseSnapshot defense,
        float incomingDamage,
        CombatWindowConfig windows,
        TimeSlowConfig timeSlow,
        BossAttackKind attackKind = BossAttackKind.Normal,
        AttackDeliveryKind deliveryKind = AttackDeliveryKind.Melee)
    {
        // Skill (and future i-frame sources) override everything.
        if (defense.IsInvulnerable)
            return new AttackResolution(DefenseResult.Invulnerable, 0f, 0f, 0f, false);

        // Dodge: i-frames for the whole window; perfect if it started inside the window.
        if (defense.IsDodging)
        {
            bool perfect = defense.NowSec - defense.DodgeStartedAtSec <= windows.PerfectDodgeSeconds;
            return perfect
                ? new AttackResolution(DefenseResult.PerfectDodged, 0f, 0f, timeSlow.PerfectDodgeRefund, false)
                : new AttackResolution(DefenseResult.Dodged, 0f, 0f, 0f, false);
        }

        // Break/grab attacks are intentionally not blockable. The player must
        // leave the threat with a dodge or the K skill's invulnerability.
        if (attackKind == BossAttackKind.BreakGrab)
            return new AttackResolution(DefenseResult.Hit, incomingDamage, 0f, 0f, false);

        // Delayed area attacks are spatial hazards. The enemy only calls the
        // resolver while the player remains inside the hazard, so blocking does
        // not turn this into a safe third answer.
        if (attackKind == BossAttackKind.DelayedArea)
            return new AttackResolution(DefenseResult.Hit, incomingDamage, 0f, 0f, false);

        // Block: chip damage + guard drain; perfect if the hold began inside the window.
        if (defense.IsBlocking)
        {
            bool perfect = defense.NowSec - defense.BlockHeldSinceSec <= windows.PerfectGuardSeconds;
            if (perfect)
            {
                bool isProjectile = deliveryKind == AttackDeliveryKind.Projectile;
                return new AttackResolution(
                    DefenseResult.PerfectBlocked,
                    0f,
                    0f,
                    0f,
                    EnemyStaggered: !isProjectile,
                    ProjectileReflected: isProjectile);
            }
            float chip = incomingDamage * windows.BlockChipRatio;
            return new AttackResolution(DefenseResult.Blocked, chip, windows.GuardDamagePerBlockedHit, 0f, false);
        }

        return new AttackResolution(DefenseResult.Hit, incomingDamage, 0f, 0f, false);
    }
}
