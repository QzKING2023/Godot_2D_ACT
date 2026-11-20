using System.Collections.Generic;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Combat;

public enum ProjectileFaction
{
    Enemy,
    PlayerReflected
}

/// <summary>
/// Physical geometric projectile fired by「远程射手」.
/// Driven strictly by Godot physics steps (naturally scaling with Engine.TimeScale / 时缓视觉场).
/// Enemy faction collides only with PlayerController and world static geometry (never allies).
/// Perfect block flips the projectile to sword-gold PlayerReflected faction aimed back at the
/// original shooter and deals 2 damage (bypassing frontal shields) to the first living enemy hit.
/// </summary>
public partial class EnemyProjectile : Area2D
{
    public const string GroupName = "enemy_projectiles";
    public const float DefaultEnemySpeed = 520f;
    public const float DefaultReflectedSpeed = 920f;
    public const float DefaultEnemyDamage = 15f;
    public const float DefaultReflectedDamage = 2f;
    public const float DefaultMaxRange = 1400f;
    public const float HitRadius = 8f;

    private static readonly Vector2[] EnemyOuterPoints =
    {
        new(14f, 0f), new(-2f, -6.5f), new(-13f, 0f), new(-2f, 6.5f)
    };

    private static readonly Vector2[] EnemyInnerPoints =
    {
        new(8f, 0f), new(-1f, -3f), new(-7f, 0f), new(-1f, 3f)
    };

    private static readonly Vector2[] ReflectedOuterPoints =
    {
        new(18f, 0f), new(-2f, -7.5f), new(-16f, 0f), new(-2f, 7.5f)
    };

    private static readonly Vector2[] ReflectedInnerPoints =
    {
        new(12f, 0f), new(0f, -3.5f), new(-10f, 0f), new(0f, 3.5f)
    };

    private readonly HashSet<ulong> _dodgedPlayerIds = new();
    private CircleShape2D _sweepShape = new() { Radius = HitRadius };
    private float _distanceTraveled;

    public ProjectileFaction Faction { get; private set; } = ProjectileFaction.Enemy;
    public Vector2 Direction { get; private set; } = Vector2.Left;
    public float Speed { get; private set; } = DefaultEnemySpeed;
    public float Damage { get; private set; } = DefaultEnemyDamage;
    public float ReflectedDamage { get; set; } = DefaultReflectedDamage;
    public float ReflectedSpeed { get; set; } = DefaultReflectedSpeed;
    public float MaxRange { get; set; } = DefaultMaxRange;
    public EnemyController? SourceEnemy { get; private set; }
    public Vector2 SpawnOrigin { get; private set; }
    public bool IsConsumed { get; private set; }
    public Vector2 Velocity => Direction * Speed;

    public void Initialize(
        Vector2 origin,
        Vector2 direction,
        float speed,
        float damage,
        EnemyController? sourceEnemy = null)
    {
        GlobalPosition = origin;
        SpawnOrigin = origin;
        Direction = direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector2.Left;
        Speed = Mathf.Max(1f, speed);
        Damage = Mathf.Max(0f, damage);
        SourceEnemy = sourceEnemy;
        Faction = ProjectileFaction.Enemy;
        IsConsumed = false;
        _distanceTraveled = 0f;
        _dodgedPlayerIds.Clear();
        Rotation = Direction.Angle();
        CollisionLayer = 0;
        CollisionMask = 1 | 2;
        QueueRedraw();
    }

    public override void _Ready()
    {
        ZIndex = 6;
        CollisionLayer = 0;
        CollisionMask = Faction == ProjectileFaction.Enemy ? 1u | 2u : 2u | 4u;
        Monitoring = false;
        Monitorable = false;

        AddChild(new CollisionShape2D
        {
            Name = "HitShape",
            Shape = _sweepShape
        });
        AddToGroup(GroupName);
        Rotation = Direction.Angle();
    }

    public override void _Process(double delta)
    {
        if (!IsConsumed)
            QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsConsumed)
            return;

        float dt = (float)delta;
        if (dt <= 0f)
            return;

        StepFlight(dt);
    }

    public override void _Draw()
    {
        if (IsConsumed)
            return;

        if (Faction == ProjectileFaction.PlayerReflected)
        {
            DrawLine(new Vector2(-34f, 0f), new Vector2(-10f, 0f), RuinGeometryPalette.SlashOuterGold with { A = 0.72f }, 4.5f, true);
            GeometricDraw.FillAndStrokePolygon(
                this,
                ReflectedOuterPoints,
                RuinGeometryPalette.PlayerBlade,
                RuinGeometryPalette.SlashOuterGold,
                width: 1.8f);
            GeometricDraw.FillPolygon(this, ReflectedInnerPoints, RuinGeometryPalette.SlashInnerGold);
        }
        else
        {
            DrawLine(new Vector2(-24f, 0f), new Vector2(-8f, 0f), RuinGeometryPalette.HitCrimson with { A = 0.60f }, 3f, true);
            GeometricDraw.FillAndStrokePolygon(
                this,
                EnemyOuterPoints,
                RuinGeometryPalette.TelegraphNormal,
                RuinGeometryPalette.HitCrimson,
                width: 1.6f);
            GeometricDraw.FillPolygon(this, EnemyInnerPoints, RuinGeometryPalette.SlashInnerGold);
        }
    }

    public override void _ExitTree()
    {
        IsConsumed = true;
        if (IsInGroup(GroupName))
            RemoveFromGroup(GroupName);
    }

    /// <summary>Counts active (non-consumed) projectiles under <paramref name="scope"/>.</summary>
    public static int CountActiveIn(Node? scope)
    {
        if (scope is null || !GodotObject.IsInstanceValid(scope))
            return 0;

        int count = 0;
        CountRecursive(scope, ref count);
        return count;
    }

    /// <summary>Cleanly removes and frees all active projectiles under <paramref name="scope"/>.</summary>
    public static void ClearAll(Node? scope)
    {
        if (scope is null || !GodotObject.IsInstanceValid(scope))
            return;

        var found = new List<EnemyProjectile>();
        CollectRecursive(scope, found);
        for (int i = 0; i < found.Count; i++)
            found[i].Consume();
    }

    private static void CountRecursive(Node node, ref int count)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is EnemyProjectile proj && GodotObject.IsInstanceValid(proj) && !proj.IsConsumed && !proj.IsQueuedForDeletion())
                count++;
            else if (child.GetChildCount() > 0)
                CountRecursive(child, ref count);
        }
    }

    private static void CollectRecursive(Node node, List<EnemyProjectile> results)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is EnemyProjectile proj && GodotObject.IsInstanceValid(proj))
                results.Add(proj);
            else if (child.GetChildCount() > 0)
                CollectRecursive(child, results);
        }
    }

    private void StepFlight(float dt)
    {
        Vector2 from = GlobalPosition;
        Vector2 displacement = Direction * (Speed * dt);
        Vector2 to = from + displacement;
        _distanceTraveled += displacement.Length();

        World2D? world = GetWorld2D();
        if (world is null)
        {
            GlobalPosition = to;
            if (_distanceTraveled >= MaxRange)
                Consume();
            return;
        }

        PhysicsDirectSpaceState2D space = world.DirectSpaceState;

        bool hitWall = TryRaycastWorldWall(space, from, to + Direction * HitRadius, out Vector2 wallPos);
        Vector2 segmentEnd = hitWall ? wallPos : to;

        if (Faction == ProjectileFaction.Enemy)
        {
            if (TryFindPlayerHit(space, from, segmentEnd, out PlayerController? player, out Vector2 hitPos) && player is not null)
            {
                GlobalPosition = hitPos;
                HandlePlayerContact(player);
                if (IsConsumed || Faction == ProjectileFaction.PlayerReflected)
                    return;
            }
        }
        else
        {
            if (TryFindEnemyHit(space, from, segmentEnd, out EnemyController? enemy, out Vector2 hitPos) && enemy is not null)
            {
                GlobalPosition = hitPos;
                HandleReflectedEnemyContact(enemy, from);
                if (IsConsumed)
                    return;
            }
        }

        if (hitWall)
        {
            GlobalPosition = wallPos;
            Consume();
            return;
        }

        GlobalPosition = to;
        if (_distanceTraveled >= MaxRange)
            Consume();
    }

    private bool TryRaycastWorldWall(PhysicsDirectSpaceState2D space, Vector2 from, Vector2 to, out Vector2 hitPos)
    {
        var query = PhysicsRayQueryParameters2D.Create(from, to, collisionMask: 2);
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit.ContainsKey("position"))
        {
            hitPos = hit["position"].AsVector2();
            return true;
        }

        hitPos = to;
        return false;
    }

    private bool TryFindPlayerHit(
        PhysicsDirectSpaceState2D space,
        Vector2 from,
        Vector2 to,
        out PlayerController? player,
        out Vector2 hitPos)
    {
        var exclude = new Godot.Collections.Array<Rid>();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var ray = PhysicsRayQueryParameters2D.Create(from, to + Direction * HitRadius, collisionMask: 1, exclude: exclude);
            ray.CollideWithBodies = true;
            ray.CollideWithAreas = false;
            ray.HitFromInside = true;
            Godot.Collections.Dictionary hit = space.IntersectRay(ray);
            if (hit.Count == 0)
                break;

            GodotObject colliderObj = hit["collider"].AsGodotObject();
            if (colliderObj is PlayerController candidate && !_dodgedPlayerIds.Contains(candidate.GetInstanceId()))
            {
                player = candidate;
                hitPos = hit["position"].AsVector2();
                return true;
            }

            if (hit.ContainsKey("rid"))
                exclude.Add(hit["rid"].AsRid());
            else
                break;
        }

        // Shape overlap check at segment end to catch vertical grazes within HitRadius
        var shapeParams = new PhysicsShapeQueryParameters2D
        {
            Shape = _sweepShape,
            Transform = new Transform2D(0f, to),
            CollisionMask = 1,
            CollideWithBodies = true,
            CollideWithAreas = false
        };
        Godot.Collections.Array<Godot.Collections.Dictionary> overlaps = space.IntersectShape(shapeParams, 8);
        for (int i = 0; i < overlaps.Count; i++)
        {
            GodotObject colliderObj = overlaps[i]["collider"].AsGodotObject();
            if (colliderObj is PlayerController candidate && !_dodgedPlayerIds.Contains(candidate.GetInstanceId()))
            {
                player = candidate;
                hitPos = to;
                return true;
            }
        }

        player = null;
        hitPos = to;
        return false;
    }

    private bool TryFindEnemyHit(
        PhysicsDirectSpaceState2D space,
        Vector2 from,
        Vector2 to,
        out EnemyController? enemy,
        out Vector2 hitPos)
    {
        var exclude = new Godot.Collections.Array<Rid>();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var ray = PhysicsRayQueryParameters2D.Create(from, to + Direction * HitRadius, collisionMask: 1u | 4u, exclude: exclude);
            ray.CollideWithBodies = true;
            ray.CollideWithAreas = true;
            ray.HitFromInside = true;
            Godot.Collections.Dictionary hit = space.IntersectRay(ray);
            if (hit.Count == 0)
                break;

            EnemyController? candidate = ExtractLivingEnemy(hit["collider"].AsGodotObject());
            if (candidate is not null)
            {
                enemy = candidate;
                hitPos = hit["position"].AsVector2();
                return true;
            }

            if (hit.ContainsKey("rid"))
                exclude.Add(hit["rid"].AsRid());
            else
                break;
        }

        var shapeParams = new PhysicsShapeQueryParameters2D
        {
            Shape = _sweepShape,
            Transform = new Transform2D(0f, to),
            CollisionMask = 1u | 4u,
            CollideWithBodies = true,
            CollideWithAreas = true
        };
        Godot.Collections.Array<Godot.Collections.Dictionary> overlaps = space.IntersectShape(shapeParams, 8);
        for (int i = 0; i < overlaps.Count; i++)
        {
            EnemyController? candidate = ExtractLivingEnemy(overlaps[i]["collider"].AsGodotObject());
            if (candidate is not null)
            {
                enemy = candidate;
                hitPos = to;
                return true;
            }
        }

        enemy = null;
        hitPos = to;
        return false;
    }

    private static EnemyController? ExtractLivingEnemy(GodotObject? collider)
    {
        EnemyController? enemy = collider as EnemyController;
        if (enemy is null && collider is Area2D area)
            enemy = area.GetParent() as EnemyController;

        if (enemy is null || !GodotObject.IsInstanceValid(enemy) || enemy.IsDefeated)
            return null;

        return enemy;
    }

    private void HandlePlayerContact(PlayerController player)
    {
        player.TryReceiveEnemyAttack(Damage, BossAttackKind.Normal, AttackDeliveryKind.Projectile, out AttackResolution resolution);

        switch (resolution.Result)
        {
            case DefenseResult.PerfectBlocked when resolution.ProjectileReflected:
                ReflectTowardSource();
                break;
            case DefenseResult.PerfectDodged:
            case DefenseResult.Dodged:
            case DefenseResult.Invulnerable:
                _dodgedPlayerIds.Add(player.GetInstanceId());
                break;
            default:
                Consume();
                break;
        }
    }

    private void ReflectTowardSource()
    {
        Faction = ProjectileFaction.PlayerReflected;
        Speed = ReflectedSpeed;
        Damage = ReflectedDamage;
        _distanceTraveled = 0f;
        _dodgedPlayerIds.Clear();

        if (SourceEnemy is not null &&
            GodotObject.IsInstanceValid(SourceEnemy) &&
            !SourceEnemy.IsDefeated)
        {
            Vector2 toSource = SourceEnemy.GlobalPosition - GlobalPosition;
            Direction = toSource.LengthSquared() > 1f ? toSource.Normalized() : -Direction;
        }
        else
        {
            Direction = -Direction;
        }

        CollisionMask = 2u | 4u;
        Rotation = Direction.Angle();
        QueueRedraw();
    }

    private void HandleReflectedEnemyContact(EnemyController enemy, Vector2 attackOrigin)
    {
        var hit = new EnemyHitContext(
            Damage: ReflectedDamage,
            AttackerOrigin: attackOrigin,
            MotionDirection: Direction,
            IsDirectionalSkill: false,
            CrossedTargetCenterline: false,
            BypassFrontalDefense: true);

        EnemyHitOutcome outcome = enemy.TakeHit(hit);
        if (outcome != EnemyHitOutcome.Ignored || enemy.IsInvulnerable)
            Consume();
    }

    public void Consume()
    {
        if (IsConsumed)
            return;

        IsConsumed = true;
        Visible = false;
        Monitoring = false;
        Monitorable = false;
        if (IsInGroup(GroupName))
            RemoveFromGroup(GroupName);
        QueueFree();
    }
}
