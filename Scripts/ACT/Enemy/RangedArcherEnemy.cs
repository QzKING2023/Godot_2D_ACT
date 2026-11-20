using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Enemy;

/// <summary>
/// 「远程射手」ranged suppression enemy:
/// 2 HP slender levitating prism silhouette using the encounter Ranged attack token pool,
/// executing ledge- and wall-safe horizontal backsteps when the player closes in,
/// locking aim during telegraph and firing solid geometric projectiles.
/// </summary>
public partial class RangedArcherEnemy : EnemyController
{
    public const string DefaultDisplayName = "远程射手";
    public const float DefaultMaxHp = 2f;
    public const float DefaultShootRange = 720f;
    public const float DefaultVerticalAimTolerance = 320f;
    public const float DefaultAimTelegraphSeconds = 0.78f;
    public const float DefaultStrikeActiveSeconds = 0.16f;
    public const float DefaultRecoverySeconds = 0.55f;
    public const float DefaultShootCooldownSeconds = 1.85f;
    public const float DefaultProjectileDamage = EnemyProjectile.DefaultEnemyDamage;
    public const float DefaultProjectileSpeed = EnemyProjectile.DefaultEnemySpeed;
    public const float DefaultCloseAlertRange = 190f;
    public const float DefaultBackstepCooldownSeconds = 2.4f;
    public const float DefaultBackstepDurationSeconds = 0.22f;
    public const float DefaultBackstepSpeed = 360f;

    private const float LedgeProbeHorizontalOffset = 26f;
    private const float LedgeProbeDownDepth = 36f;
    private const float WallProbeDistance = 30f;

    [Export] public float ShootRange { get; set; } = DefaultShootRange;
    [Export] public float VerticalAimTolerance { get; set; } = DefaultVerticalAimTolerance;
    [Export] public float AimTelegraphSeconds { get; set; } = DefaultAimTelegraphSeconds;
    [Export] public float ShootCooldownSeconds { get; set; } = DefaultShootCooldownSeconds;
    [Export] public float ProjectileDamage { get; set; } = DefaultProjectileDamage;
    [Export] public float ProjectileSpeed { get; set; } = DefaultProjectileSpeed;
    [Export] public float CloseAlertRange { get; set; } = DefaultCloseAlertRange;
    [Export] public float BackstepCooldownSeconds { get; set; } = DefaultBackstepCooldownSeconds;
    [Export] public float BackstepDurationSeconds { get; set; } = DefaultBackstepDurationSeconds;
    [Export] public float BackstepSpeed { get; set; } = DefaultBackstepSpeed;

    private readonly List<EnemyProjectile> _ownedProjectiles = new();
    private Vector2 _aimDirection = Vector2.Left;
    private bool _shotFiredForCurrentStrike;
    private bool _isBackstepping;
    private int _backstepDirection = 1;
    private float _backstepEndsAt = -10f;
    private float _nextBackstepAllowedAt = -10f;

    public override AttackTokenPool AttackTokenPoolKind => AttackTokenPool.Ranged;
    public Vector2 AimDirection => _aimDirection;
    public Vector2 MuzzleLocalOffset => new(Facing * 18f, -10f);
    public Vector2 MuzzleGlobalPosition => GlobalPosition + MuzzleLocalOffset;
    public bool IsBackstepping => _isBackstepping;
    public int BackstepDirection => _backstepDirection;
    public bool IsBackstepOnCooldown => Now < _nextBackstepAllowedAt;

    protected override Vector2 BodySize => new(36f, 84f);

    public RangedArcherEnemy()
    {
        Name = "RangedArcher";
        DisplayName = DefaultDisplayName;
        IsBoss = false;
        MaxHp = DefaultMaxHp;
        ChaseSpeed = 0f;
    }

    protected override EnemyVisual CreateVisual() => new RangedArcherVisual { Name = "Visual" };

    protected override void UpdateFacing()
    {
        if (State is not (EnemyState.Idle or EnemyState.Chase))
            _isBackstepping = false;

        if (_isBackstepping ||
            State is EnemyState.Telegraph or EnemyState.Strike or EnemyState.Hitstun or EnemyState.ShieldBroken or EnemyState.Dead or EnemyState.Victory)
            return;

        base.UpdateFacing();
    }

    protected override BossAttackDefinition SelectNextAttack()
        => new(
            Kind: BossAttackKind.Normal,
            Name: "RANGED SHOT",
            ResponseHint: "BLOCK or DODGE",
            Damage: ProjectileDamage,
            TelegraphSeconds: AimTelegraphSeconds,
            ActiveSeconds: DefaultStrikeActiveSeconds,
            RecoverySeconds: DefaultRecoverySeconds,
            Range: ShootRange,
            HazardRadius: 0f);

    protected override float EstimateExpectedHitLeadSeconds(BossAttackDefinition attack)
    {
        float lead = Mathf.Max(0f, attack.TelegraphSeconds);
        if (TargetPlayer is not null && GodotObject.IsInstanceValid(TargetPlayer) && ProjectileSpeed > 1f)
        {
            float dist = MuzzleGlobalPosition.DistanceTo(TargetPlayer.GlobalPosition);
            lead += Mathf.Clamp(dist / ProjectileSpeed, 0f, 1.2f);
        }
        return lead;
    }

    protected override void ThinkIdleOrChase()
    {
        if (TargetPlayer is null || !GodotObject.IsInstanceValid(TargetPlayer))
        {
            StopBackstep();
            Velocity = Velocity with { X = 0f };
            SetState(EnemyState.Idle, "no target");
            return;
        }

        if (_isBackstepping)
        {
            if (Now >= _backstepEndsAt || !CanSafelyRetreatInDirection(_backstepDirection))
            {
                StopBackstep();
            }
            else
            {
                Velocity = Velocity with { X = _backstepDirection * BackstepSpeed };
                SetState(EnemyState.Chase, "ranged backstep");
                return;
            }
        }

        Vector2 toPlayer = TargetPlayer.GlobalPosition - GlobalPosition;
        float distance = toPlayer.Length();
        bool playerClose = distance <= CloseAlertRange && Mathf.Abs(toPlayer.Y) <= 130f;

        if (playerClose && !IsBackstepOnCooldown)
        {
            int retreatDir = toPlayer.X >= 0f ? -1 : 1;
            if (CanSafelyRetreatInDirection(retreatDir))
            {
                StartBackstep(retreatDir);
                return;
            }
        }

        Velocity = Velocity with { X = 0f };

        bool inRange = distance <= ShootRange && Mathf.Abs(toPlayer.Y) <= VerticalAimTolerance;
        if (inRange && CanStartAttack)
        {
            LockAimTowardPlayer();
            if (TryBeginTelegraph())
                return;
        }

        SetState(EnemyState.Idle, "ranged perch holding");
    }

    protected override void BeginStrike()
    {
        StopBackstep();
        _shotFiredForCurrentStrike = false;
        base.BeginStrike();
    }

    protected override void ResolveStrike()
    {
        if (_shotFiredForCurrentStrike)
            return;

        _shotFiredForCurrentStrike = true;
        SpawnProjectile();
        SetNextAttackCooldown(ShootCooldownSeconds);
    }

    protected override void OnReset()
    {
        StopBackstep();
        _nextBackstepAllowedAt = -10f;
        _shotFiredForCurrentStrike = false;
        _aimDirection = new Vector2(Facing, 0f);
        ClearOwnedProjectiles();
    }

    /// <summary>
    /// Checks world static geometry (Layer 2) in <paramref name="direction"/> for both wall obstruction
    /// and solid platform support ahead of the backstep edge so the archer never retreats into walls or off ledges.
    /// </summary>
    public bool CanSafelyRetreatInDirection(int direction)
    {
        World2D? world = GetWorld2D();
        if (world is null)
            return false;

        int dir = direction >= 0 ? 1 : -1;
        PhysicsDirectSpaceState2D space = world.DirectSpaceState;

        // 1. Horizontal wall check at upper and lower torso heights
        float[] probeOffsetsY = { -20f, 16f };
        for (int i = 0; i < probeOffsetsY.Length; i++)
        {
            Vector2 wallStart = GlobalPosition + new Vector2(0f, probeOffsetsY[i]);
            Vector2 wallEnd = wallStart + new Vector2(dir * WallProbeDistance, 0f);
            var wallQuery = PhysicsRayQueryParameters2D.Create(wallStart, wallEnd, collisionMask: 2);
            wallQuery.CollideWithBodies = true;
            wallQuery.CollideWithAreas = false;
            wallQuery.HitFromInside = true;
            if (space.IntersectRay(wallQuery).Count > 0)
                return false;
        }

        // 2. Downward ledge check ahead of the retreat edge beneath feet
        float footY = GlobalPosition.Y + BodySize.Y * 0.5f;
        float probeX = GlobalPosition.X + dir * LedgeProbeHorizontalOffset;
        Vector2 ledgeStart = new(probeX, footY - 8f);
        Vector2 ledgeEnd = new(probeX, footY + LedgeProbeDownDepth);
        var ledgeQuery = PhysicsRayQueryParameters2D.Create(ledgeStart, ledgeEnd, collisionMask: 2);
        ledgeQuery.CollideWithBodies = true;
        ledgeQuery.CollideWithAreas = false;
        ledgeQuery.HitFromInside = true;
        return space.IntersectRay(ledgeQuery).Count > 0;
    }

    private void StartBackstep(int retreatDir)
    {
        _isBackstepping = true;
        _backstepDirection = retreatDir >= 0 ? 1 : -1;
        _backstepEndsAt = Now + Mathf.Max(0.02f, BackstepDurationSeconds);
        _nextBackstepAllowedAt = Now + Mathf.Max(0f, BackstepCooldownSeconds);
        Velocity = Velocity with { X = _backstepDirection * BackstepSpeed };
        SetState(EnemyState.Chase, "ranged backstep");
    }

    private void StopBackstep()
    {
        _isBackstepping = false;
    }

    private void SpawnProjectile()
    {
        PruneOwnedProjectiles();

        Vector2 origin = MuzzleGlobalPosition;
        Vector2 dir = _aimDirection.LengthSquared() > 0.0001f
            ? _aimDirection.Normalized()
            : new Vector2(Facing, 0f);

        Node? container = GetParent();
        if (container is not null && GodotObject.IsInstanceValid(container))
        {
            var projectile = new EnemyProjectile { Name = $"ArcherProjectile_{Time.GetTicksUsec()}" };
            projectile.Initialize(origin, dir, ProjectileSpeed, CurrentAttack.Damage, this);
            container.AddChild(projectile);
            _ownedProjectiles.Add(projectile);
        }

        EmitProjectileFired(new EnemyProjectileFiredCue(origin, dir, CurrentAttack.Damage));
    }

    private void PruneOwnedProjectiles()
    {
        for (int i = _ownedProjectiles.Count - 1; i >= 0; i--)
        {
            EnemyProjectile p = _ownedProjectiles[i];
            if (!GodotObject.IsInstanceValid(p) || p.IsConsumed || p.IsQueuedForDeletion())
                _ownedProjectiles.RemoveAt(i);
        }
    }

    private void ClearOwnedProjectiles()
    {
        for (int i = 0; i < _ownedProjectiles.Count; i++)
        {
            EnemyProjectile p = _ownedProjectiles[i];
            if (GodotObject.IsInstanceValid(p) && !p.IsConsumed)
                p.Consume();
        }
        _ownedProjectiles.Clear();
    }

    private void LockAimTowardPlayer()
    {
        if (TargetPlayer is null || !GodotObject.IsInstanceValid(TargetPlayer))
        {
            _aimDirection = new Vector2(Facing, 0f);
            return;
        }

        Vector2 delta = TargetPlayer.GlobalPosition - MuzzleGlobalPosition;
        if (Mathf.Abs(delta.X) > 1f)
            SetFacing(delta.X >= 0f ? 1 : -1);

        delta = TargetPlayer.GlobalPosition - MuzzleGlobalPosition;
        _aimDirection = delta.LengthSquared() > 1f ? delta.Normalized() : new Vector2(Facing, 0f);
    }
}
