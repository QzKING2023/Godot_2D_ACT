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
    public static readonly Color TowerRackShadow = new("3d4152");
    public static readonly Color TowerRackLit = new("b0805c");
    public static readonly Color TowerRackCap = new("c59268");
    public static readonly Color FarHills = new("4c4957");
    public static readonly Color PillarShadow = new("2d3342");
    public static readonly Color PillarLit = new("6d5347");
    public static readonly Color PillarLintelShadow = new("3a3f4f");
    public static readonly Color PillarLintelLit = new("b37b51");

    // 前景古代金属巨构平台
    public static readonly Color PlatformBody = new("1f2633");
    public static readonly Color PlatformShadow = new("171c26");
    public static readonly Color PlatformTopLit = new("d99b66");
    public static readonly Color PlatformTopBevel = new("6e5244");
    public static readonly Color PlatformJoint = new("2b3445");

    // 主角硬边切面与剑光残影
    public static readonly Color PlayerShadowFacet = new("2a3a4a");
    public static readonly Color PlayerLitFacet = new("53c7b8");
    public static readonly Color PlayerWarmTrim = new("f2b880");
    public static readonly Color PlayerHead = new("f4f1de");
    public static readonly Color PlayerBlade = new("ffe17d");
    public static readonly Color PlayerShieldFill = new(0.32f, 0.84f, 0.96f, 0.42f);
    public static readonly Color PlayerShieldEdge = new("8ce9ff");
    public static readonly Color PlayerAfterimage = new(0.45f, 0.92f, 0.88f);
    public static readonly Color SlashOuterGold = new(1f, 0.90f, 0.55f);
    public static readonly Color SlashInnerGold = Colors.White;

    // Boss 硬边切面
    public static readonly Color BossShadowArmor = new("2a2e3d");
    public static readonly Color BossLitArmor = new("8c5544");
    public static readonly Color BossPauldronHighlight = new("c97b52");
    public static readonly Color BossEyeIdle = new("ff9f5a");
    public static readonly Color BossDefeatedShadow = new("353b45");
    public static readonly Color BossDefeatedLit = new("4d5563");
    public static readonly Color BossDefeatedEye = new("5a6270");

    // 三种「战斗前摇信号」
    public static readonly Color TelegraphNormal = new("ffd166");
    public static readonly Color TelegraphNormalFill = new(1f, 0.82f, 0.32f);
    public static readonly Color TelegraphBreakGrab = new("ff3b5c");
    public static readonly Color TelegraphBreakGrabFill = new(0.95f, 0.22f, 0.36f);
    public static readonly Color TelegraphDelayedArea = new("c77dff");
    public static readonly Color TelegraphDelayedAreaFill = new(0.60f, 0.32f, 0.90f);
    public static readonly Color TelegraphDelayedAreaStrike = new(0.76f, 0.42f, 1f, 0.35f);
    public static readonly Color DelayedAreaCountdown = new("f0d6ff");

    // 打击/受击/闪避反馈语义色
    public static readonly Color DodgeTeal = new("72efdd");
    public static readonly Color HitCrimson = new("ff4769");

    // 「时缓视觉场」
    public static readonly Color TimeSlowField = new("1b3a5c");
    public static readonly Color TimeSlowEdge = new(0.55f, 0.85f, 1f, 0.42f);

    // 边界门柱与正式 HUD / 菜单语义色
    public static readonly Color GateStoneShadow = new("252d3d");
    public static readonly Color GateStoneLit = new("5a4942");
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
    public static readonly Color HudBreakHit = new("ff6b8a");
    public static readonly Color HudBannerShadow = new(0.05f, 0.07f, 0.10f);
    public static readonly Color HudDefeatBackdrop = new(0.05f, 0.04f, 0.06f, 0.52f);
    public static readonly Color HudDebugEnemy = new("ffb703");
    public static readonly Color HudDebugCombat = new("ffd166");
    public static readonly Color HudDebugTime = new("c77dff");
    public static readonly Color HudDebugHint = new("a8dadc");
    public static readonly Color MenuOptionSelectedFill = new(0.14f, 0.17f, 0.24f, 0.92f);
    public static readonly Color MenuOptionBorder = GateLocked with { A = 0.75f };
    public static readonly Color MenuPanelFill = HudPanelDark with { A = 0.95f };
    public static readonly Color MenuPauseBackdrop = new(0.04f, 0.06f, 0.09f, 0.72f);
    public static readonly Color MenuTitleScrim = new(0.06f, 0.08f, 0.12f, 0.56f);

    public static Color ForTelegraph(BossAttackKind kind) => kind switch
    {
        BossAttackKind.BreakGrab => TelegraphBreakGrab,
        BossAttackKind.DelayedArea => TelegraphDelayedArea,
        _ => TelegraphNormal
    };
}
