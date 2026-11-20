using System;
using Godot;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;

namespace ACTSpike.ACT.HUD;

/// <summary>
/// Variant A formal Simplified Chinese HUD with angled HP / GUARD / TIME bars,
/// bottom-center Boss health bar, concise objective & combat event banners,
/// defeat overlay, and F3-toggled debug layer.
/// </summary>
public partial class CombatHud : Control
{
    private const float ViewportWidth = 1280f;
    private const float ViewportHeight = 720f;
    private const float TopBarX = 28f;
    private const float BarSkew = 8f;
    private const float BossBarWidth = 540f;
    private const float BossBarHeight = 12f;
    private const float BossBarBottomMargin = 56f;

    private static readonly Rect2 HpBarRect = new(TopBarX, 24f, 230f, 14f);
    private static readonly Rect2 GuardBarRect = new(TopBarX, 48f, 196f, 10f);
    private static readonly Rect2 TimeBarRect = new(TopBarX, 68f, 196f, 10f);
    private static readonly Rect2 ObjectivePillRect = new(390f, 20f, 500f, 32f);
    private static readonly Rect2 DebugPanelRect = new(24f, 96f, 560f, 194f);
    private static readonly Rect2 DefeatPanelRect = new(340f, 268f, 600f, 152f);

    private PlayerController? _player;
    private PlayerController? _subscribedPlayer;
    private string _lastCombatLine = "";
    private string _lastTimeLine = "";
    private string _eventBannerText = "";
    private Color _eventBannerColor = RuinGeometryPalette.TelegraphNormal;
    private int _eventBannerFontSize = 24;
    private float _eventBannerExpiresAt = -1f;
    private float _eventBannerFadeDuration = 0.35f;

    public PlayerController Player
    {
        get => _player!;
        set
        {
            if (ReferenceEquals(_player, value)) return;
            UnsubscribePlayer();
            _player = value;
            if (IsInsideTree())
                SubscribePlayer();
        }
    }

    public const string DefaultBossDisplayName = "遗迹守卫";

    public StageEnemyRoster? EnemyRoster { get; set; }
    public EnemyController? Enemy { get; set; }
    public TimeSlowManager TimeSlow { get; set; } = null!;
    public string ArenaMessage { get; set; } = "向右穿过试炼门柱，迎战遗迹守卫";
    public string EnemyDisplayName { get; set; } = "";
    public bool IsObjectiveCleared { get; set; }
    public bool DebugOverlayVisible { get; set; }

    public EnemyController? ActiveBoss
    {
        get
        {
            if (EnemyRoster is not null && GodotObject.IsInstanceValid(EnemyRoster))
            {
                EnemyController? boss = EnemyRoster.PrimaryBoss;
                return boss is not null && GodotObject.IsInstanceValid(boss) && boss.IsBoss ? boss : null;
            }
            return Enemy is not null && GodotObject.IsInstanceValid(Enemy) && Enemy.IsBoss ? Enemy : null;
        }
    }

    public bool IsBossHealthBarVisible => ActiveBoss is { IsDefeated: false };

    public string ActiveBossDisplayName
    {
        get
        {
            EnemyController? boss = ActiveBoss;
            if (boss is null) return "";
            if (!string.IsNullOrWhiteSpace(EnemyDisplayName))
                return EnemyDisplayName;
            return string.IsNullOrWhiteSpace(boss.DisplayName) ? DefaultBossDisplayName : boss.DisplayName;
        }
    }

    public string CurrentEventBannerText => NowSeconds < _eventBannerExpiresAt ? _eventBannerText : "";
    public string LastCombatLine => _lastCombatLine;
    public string LastTimeLine => _lastTimeLine;

    private static float NowSeconds => Time.GetTicksMsec() / 1000f;

    public void ReportCombat(string line) => _lastCombatLine = line;

    public void ReportTime(string line) => _lastTimeLine = line;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SubscribePlayer();
    }

    public override void _ExitTree()
    {
        UnsubscribePlayer();
    }

    public override void _Process(double delta)
    {
        if (_subscribedPlayer is null && _player is not null)
            SubscribePlayer();

        if (ActInput.ToggleDebugPressed())
            DebugOverlayVisible = !DebugOverlayVisible;

        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = UiFonts.Default;
        DrawTopLeftResourceBars(font);
        DrawObjectiveBanner(font);
        DrawTimeSlowIndicator(font);
        DrawBossHealthBar(font);
        DrawCombatEventBanner(font);
        if (_player is { IsDown: true })
            DrawDefeatOverlay(font);
        if (DebugOverlayVisible)
            DrawDebugOverlay(font);
    }

    private void SubscribePlayer()
    {
        if (_player is null || ReferenceEquals(_subscribedPlayer, _player)) return;
        _player.DefenseResolved += OnPlayerDefenseResolved;
        _subscribedPlayer = _player;
    }

    private void UnsubscribePlayer()
    {
        if (_subscribedPlayer is null) return;
        _subscribedPlayer.DefenseResolved -= OnPlayerDefenseResolved;
        _subscribedPlayer = null;
    }

    private void OnPlayerDefenseResolved(PlayerDefenseCue cue)
    {
        if (cue.Result == DefenseResult.PerfectBlocked)
        {
            string label = cue.ProjectileReflected ? "完美格挡 · 弹丸弹反" : "完美格挡 · 震慑破势";
            ShowEventBanner(label, RuinGeometryPalette.TelegraphNormal, 1.1f, 26);
        }
        else if (cue.Result == DefenseResult.PerfectDodged)
        {
            ShowEventBanner("完美闪避 · 时缓充能", RuinGeometryPalette.DodgeTeal, 1.1f, 26);
        }
        else if (cue.GuardBroken)
        {
            ShowEventBanner("破防硬直", RuinGeometryPalette.PlayerHead, 0.95f, 22);
        }
        else if (cue.Result == DefenseResult.Hit && cue.AttackKind == BossAttackKind.BreakGrab)
        {
            ShowEventBanner("护盾击破", RuinGeometryPalette.HudBreakHit, 0.95f, 22);
        }
    }

    private void ShowEventBanner(string text, Color color, float durationSeconds, int fontSize)
    {
        _eventBannerText = text;
        _eventBannerColor = color;
        _eventBannerFontSize = fontSize;
        _eventBannerExpiresAt = NowSeconds + durationSeconds;
        _eventBannerFadeDuration = Mathf.Min(0.35f, durationSeconds * 0.4f);
    }

    private void DrawTopLeftResourceBars(Font font)
    {
        float hpFraction = _player is not null && _player.MaxHp > 0f ? _player.Hp / _player.MaxHp : 0f;
        float guardFraction = _player is not null && _player.MaxGuard > 0f ? _player.Guard / _player.MaxGuard : 0f;
        float timeFraction = TimeSlow is not null ? TimeSlow.ResourceFraction : 0f;

        DrawLabeledAngledBar(font, HpBarRect, hpFraction, RuinGeometryPalette.HudHp, "HP");
        DrawLabeledAngledBar(font, GuardBarRect, guardFraction, RuinGeometryPalette.HudGuard, "GUARD");
        DrawLabeledAngledBar(font, TimeBarRect, timeFraction, RuinGeometryPalette.HudTime, "TIME");
    }

    private void DrawLabeledAngledBar(Font font, Rect2 rect, float fraction, Color fillColor, string label)
    {
        GeometricDraw.AngledBar(this, rect, fraction, RuinGeometryPalette.HudPanelDark, fillColor, BarSkew);
        DrawString(font, new Vector2(rect.End.X + 8f, rect.End.Y - 1f),
            label, HorizontalAlignment.Left, -1, 12, RuinGeometryPalette.HudTextLight with { A = 0.85f });
    }

    private void DrawObjectiveBanner(Font font)
    {
        if (string.IsNullOrWhiteSpace(ArenaMessage)) return;

        Color accent = IsObjectiveCleared ? RuinGeometryPalette.GateUnlocked : RuinGeometryPalette.PlatformTopLit;

        GeometricDraw.AngledBar(this, ObjectivePillRect, 1f, RuinGeometryPalette.HudPanelDark, RuinGeometryPalette.HudPanelDark, skew: 10f);
        DrawRect(new Rect2(ObjectivePillRect.Position.X, ObjectivePillRect.End.Y - 2f, ObjectivePillRect.Size.X - 10f, 2f), accent with { A = 0.7f });
        GeometricDraw.Diamond(this, new Vector2(ObjectivePillRect.Position.X + 16f, ObjectivePillRect.GetCenter().Y), 4f, 5f, accent);

        float baselineY = GeometricMenuController.CenteredBaselineY(ObjectivePillRect, font, 15);
        DrawString(font, new Vector2(ObjectivePillRect.Position.X + 28f, baselineY),
            ArenaMessage, HorizontalAlignment.Center, ObjectivePillRect.Size.X - 44f, 15, RuinGeometryPalette.HudTextLight);
    }

    private void DrawTimeSlowIndicator(Font font)
    {
        if (TimeSlow is null || !TimeSlow.Active) return;

        var badgeRect = new Rect2(ViewportWidth - 178f, 22f, 150f, 30f);
        GeometricDraw.AngledBar(this, badgeRect, 1f, RuinGeometryPalette.HudPanelDark, RuinGeometryPalette.TimeSlowField with { A = 0.85f }, skew: 8f);
        GeometricDraw.Diamond(this, new Vector2(badgeRect.Position.X + 16f, badgeRect.GetCenter().Y), 4.5f, 6f, RuinGeometryPalette.PlayerShieldEdge);
        float baselineY = GeometricMenuController.CenteredBaselineY(badgeRect, font, 15);
        DrawString(font, new Vector2(badgeRect.Position.X + 28f, baselineY),
            "时缓展开", HorizontalAlignment.Left, -1, 15, RuinGeometryPalette.PlayerShieldEdge);
    }

    private void DrawBossHealthBar(Font font)
    {
        EnemyController? boss = ActiveBoss;
        if (boss is null || boss.IsDefeated) return;

        float bx = (ViewportWidth - BossBarWidth) * 0.5f;
        float by = ViewportHeight - BossBarBottomMargin;
        float fraction = boss.MaxHp > 0f ? Mathf.Clamp(boss.Hp / boss.MaxHp, 0f, 1f) : 0f;
        string title = ActiveBossDisplayName;

        DrawString(font, new Vector2(bx, by - 10f), title, HorizontalAlignment.Left, -1, 15, RuinGeometryPalette.HudBossTitle);

        var bgRect = new Rect2(bx, by, BossBarWidth, BossBarHeight);
        DrawRect(bgRect, RuinGeometryPalette.HudPanelDark);
        if (fraction > 0f)
            DrawRect(new Rect2(bx, by, BossBarWidth * fraction, BossBarHeight), RuinGeometryPalette.HudBossHp);
        DrawRect(bgRect, RuinGeometryPalette.PlatformTopLit with { A = 0.65f }, filled: false, width: 1.5f);
    }

    private void DrawCombatEventBanner(Font font)
    {
        float remaining = _eventBannerExpiresAt - NowSeconds;
        if (remaining <= 0f || string.IsNullOrEmpty(_eventBannerText)) return;

        float alpha = remaining < _eventBannerFadeDuration
            ? Mathf.Clamp(remaining / _eventBannerFadeDuration, 0f, 1f)
            : 1f;
        Color color = _eventBannerColor with { A = _eventBannerColor.A * alpha };
        Color shadow = RuinGeometryPalette.HudBannerShadow with { A = 0.72f * alpha };

        var pos = new Vector2(ViewportWidth * 0.5f - 220f, 120f);
        DrawString(font, pos + new Vector2(2f, 2f), _eventBannerText, HorizontalAlignment.Center, 440f, _eventBannerFontSize, shadow);
        DrawString(font, pos, _eventBannerText, HorizontalAlignment.Center, 440f, _eventBannerFontSize, color);
    }

    private void DrawDefeatOverlay(Font font)
    {
        DrawRect(new Rect2(0f, 0f, ViewportWidth, ViewportHeight), RuinGeometryPalette.HudDefeatBackdrop);

        Vector2 p = DefeatPanelRect.Position;
        float w = DefeatPanelRect.Size.X;
        const float skew = 14f;

        GeometricDraw.SkewedRect(this, DefeatPanelRect, skew,
            RuinGeometryPalette.HudPanelDark with { A = 0.92f }, RuinGeometryPalette.HudHp, 2f);

        DrawString(font, new Vector2(p.X, p.Y + 68f),
            "已倒下", HorizontalAlignment.Center, w - skew, 40, RuinGeometryPalette.HudHp);
        DrawString(font, new Vector2(p.X, p.Y + 112f),
            "按 R 重置当前关 · 按 Esc 打开菜单", HorizontalAlignment.Center, w - skew, 17, RuinGeometryPalette.HudTextLight);
    }

    private void DrawDebugOverlay(Font font)
    {
        DrawRect(DebugPanelRect, RuinGeometryPalette.HudPanelDark with { A = 0.90f });
        DrawRect(DebugPanelRect, RuinGeometryPalette.GateLocked, filled: false, width: 1.5f);

        float x = DebugPanelRect.Position.X + 14f;
        float y = DebugPanelRect.Position.Y + 24f;
        const float lineStep = 23f;

        DrawString(font, new Vector2(x, y), "[F3 调试覆盖层]", HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.TelegraphNormal);
        y += lineStep;

        if (_player is not null)
        {
            DrawString(font, new Vector2(x, y),
                $"Player: {_player.State}  HP {_player.Hp:0}/{_player.MaxHp:0}  Guard {_player.Guard:0}/{_player.MaxGuard:0}  Down={_player.IsDown}",
                HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudTextLight);
        }
        y += lineStep;

        if (EnemyRoster is not null && GodotObject.IsInstanceValid(EnemyRoster) && EnemyRoster.Count > 0)
        {
            EnemyController? primary = EnemyRoster.PrimaryBoss ?? EnemyRoster.Enemies[0];
            if (EnemyRoster.Count == 1 && primary is not null && GodotObject.IsInstanceValid(primary))
            {
                DrawString(font, new Vector2(x, y),
                    $"Enemy [{primary.DisplayName}]: {primary.State}  HP {primary.Hp:0}/{primary.MaxHp:0}  Attack: {primary.CurrentAttack.Name}",
                    HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudDebugEnemy);
            }
            else
            {
                string focus = primary is not null && GodotObject.IsInstanceValid(primary)
                    ? $"{primary.DisplayName} {primary.State} {primary.Hp:0}/{primary.MaxHp:0}"
                    : "none";
                DrawString(font, new Vector2(x, y),
                    $"Enemies: {EnemyRoster.AliveCount}/{EnemyRoster.Count} alive  Focus: {focus}",
                    HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudDebugEnemy);
            }
        }
        else if (Enemy is not null && GodotObject.IsInstanceValid(Enemy))
        {
            DrawString(font, new Vector2(x, y),
                $"Enemy: {Enemy.State}  HP {Enemy.Hp:0}/{Enemy.MaxHp:0}  Attack: {Enemy.CurrentAttack.Name} ({Enemy.CurrentAttack.ResponseHint})",
                HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudDebugEnemy);
        }
        else
        {
            DrawString(font, new Vector2(x, y),
                "Enemy: none (non-combat / traversal stage)",
                HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudTextLight with { A = 0.7f });
        }
        y += lineStep;

        DrawString(font, new Vector2(x, y), $"Combat: {_lastCombatLine}", HorizontalAlignment.Left, -1, 13, RuinGeometryPalette.HudDebugCombat);
        y += lineStep;

        float res = TimeSlow is not null ? TimeSlow.Resource : 0f;
        DrawString(font, new Vector2(x, y),
            $"Time: {_lastTimeLine} | Scale={Engine.TimeScale:0.00} Res={res:0.0}",
            HorizontalAlignment.Left, -1, 13, RuinGeometryPalette.HudDebugTime);
        y += lineStep;

        DrawString(font, new Vector2(x, y),
            "Keys: A/D Space J Shift Ctrl Q K R Esc F3",
            HorizontalAlignment.Left, -1, 12, RuinGeometryPalette.HudDebugHint);
        y += 18f;

        DrawString(font, new Vector2(x, y),
            "Pad: LS Move  A Jump  X Atk  B Dodge  RB Block  LB Slow  Y Skill  Start Pause",
            HorizontalAlignment.Left, -1, 12, RuinGeometryPalette.HudDebugHint);
    }
}
