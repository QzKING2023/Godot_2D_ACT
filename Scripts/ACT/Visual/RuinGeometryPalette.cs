using Godot;
using ACTSpike.ACT.Enemy;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Shared hard-edged facet color tokens for「遗迹几何表现」(derived from Prototype Variant A).
/// </summary>
public static class RuinGeometryPalette
{
    // 干旱草原荒原 & 荒原暖侧光
    public static readonly Color SkyTop = new("2b3548");
    public static readonly Color SkyHorizon = new("c98b55");
    public static readonly Color SunOuterGlow = new(0.98f, 0.76f, 0.45f, 0.12f);
    public static readonly Color SunCoreGlow = new(1f, 0.84f, 0.56f, 0.24f);

    // 中央遗迹高塔 & 远景古代金属巨构
    public static readonly Color TowerShadow = new("44495c");
    public static readonly Color TowerLit = new("9e7457");
    public static readonly Color FarHills = new("4c4957");
    public static readonly Color PillarShadow = new("2d3342");
    public static readonly Color PillarLit = new("6d5347");

    // 前景古代金属巨构平台
    public static readonly Color PlatformBody = new("1f2633");
    public static readonly Color PlatformShadow = new("171c26");
    public static readonly Color PlatformTopLit = new("d99b66");
    public static readonly Color PlatformTopBevel = new("6e5244");
    public static readonly Color PlatformJoint = new("2b3445");

    // 主角硬边切面
    public static readonly Color PlayerShadowFacet = new("2a3a4a");
    public static readonly Color PlayerLitFacet = new("53c7b8");
    public static readonly Color PlayerWarmTrim = new("f2b880");
    public static readonly Color PlayerHead = new("f4f1de");
    public static readonly Color PlayerBlade = new("ffe17d");
    public static readonly Color PlayerShieldFill = new(0.32f, 0.84f, 0.96f, 0.42f);
    public static readonly Color PlayerShieldEdge = new("8ce9ff");

    // Boss 硬边切面
    public static readonly Color BossShadowArmor = new("2a2e3d");
    public static readonly Color BossLitArmor = new("8c5544");
    public static readonly Color BossPauldronHighlight = new("c97b52");
    public static readonly Color BossEyeIdle = new("ff9f5a");
    public static readonly Color BossDefeatedShadow = new("353b45");
    public static readonly Color BossDefeatedLit = new("4d5563");

    // 三种「战斗前摇信号」
    public static readonly Color TelegraphNormal = new("ffd166");
    public static readonly Color TelegraphBreakGrab = new("ff3b5c");
    public static readonly Color TelegraphDelayedArea = new("c77dff");

    // 「时缓视觉场」
    public static readonly Color TimeSlowField = new("1b3a5c");
    public static readonly Color TimeSlowEdge = new(0.55f, 0.85f, 1f, 0.42f);

    // 边界门柱与正式 HUD 语义色
    public static readonly Color GateActive = new("ef8354");
    public static readonly Color GateUnlocked = new("7ae582");
    public static readonly Color GateLocked = new("475266");
    public static readonly Color HudHp = new("e05252");
    public static readonly Color HudGuard = new("5cc8e6");
    public static readonly Color HudTime = new("a770ef");
    public static readonly Color HudBossHp = new("d96b43");
    public static readonly Color HudPanelDark = new(0.07f, 0.09f, 0.13f, 0.78f);
    public static readonly Color HudTextLight = new("e8edf2");
    public static readonly Color HudBossTitle = new("f4d7b0");

    public static Color ForTelegraph(BossAttackKind kind) => kind switch
    {
        BossAttackKind.BreakGrab => TelegraphBreakGrab,
        BossAttackKind.DelayedArea => TelegraphDelayedArea,
        _ => TelegraphNormal
    };
}
