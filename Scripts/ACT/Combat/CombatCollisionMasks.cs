namespace ACTSpike.ACT.Combat;

/// <summary>
/// Authoritative 2D physics collision layer bitmasks shared by actors, world geometry,
/// enemy hurtboxes, and ranged projectiles.
/// </summary>
public static class CombatCollisionMasks
{
    public const uint None = 0u;
    public const uint ActorBody = 1u;
    public const uint WorldGeometry = 2u;
    public const uint EnemyHurtbox = 4u;

    public const uint EnemyProjectileTargets = ActorBody | WorldGeometry;
    public const uint ReflectedProjectileTargets = WorldGeometry | EnemyHurtbox;
    public const uint ReflectedEnemyQuery = ActorBody | EnemyHurtbox;
}
