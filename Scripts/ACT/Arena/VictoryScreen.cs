using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Upgraded Simplified Chinese victory screen reusing RuinEnvironmentLayer (RuinStageTheme.Victory),
/// GeometricDraw, RuinGeometryPalette, GeometricMenuPanel, and UiFonts.Default.
/// Supports keyboard, Xbox controller, mouse hover/click, and R rematch shortcut.
/// </summary>
public partial class VictoryScreen : Node2D
{
    private static readonly MenuOptionLayout OptionLayout = new(485f, 398f, 310f, 48f, 16f);
    private static readonly Rect2 CardPanelRect = new(340f, 118f, 620f, 448f);

    private GeometricMenuController _menu = null!;
    private GeometricMenuPanel _menuPanel = null!;

    public int SelectedIndex => _menu.SelectedIndex;
    public IReadOnlyList<string> OptionLabels => _menu.OptionLabels;

    public override void _Ready()
    {
        TimeSlowManager.EnsureNormalScale();
        GetTree().Paused = false;

        RuinEnvironmentLayer.CreateMenuBackdrop(this, RuinStageTheme.Victory);

        _menu = new GeometricMenuController(
            new GeometricMenuController.Option("再战 Boss", RematchBoss),
            new GeometricMenuController.Option("返回标题", ReturnToTitle));

        _menuPanel = GeometricMenuPanel.Attach(
            this,
            "VictoryUILayer",
            "VictoryCanvas",
            _menu,
            OptionLayout,
            optionFontSize: 20,
            optionSkew: 12f,
            DrawVictoryHeader);
    }

    public override void _Process(double delta)
    {
        if (_menuPanel.IsTransitioning) return;

        if (ActInput.ResetPressed())
        {
            RematchBoss();
            return;
        }

        _menuPanel.PollStickNavigation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_menuPanel.IsTransitioning) return;

        if (ActInput.IsMenuBackEvent(@event))
        {
            GetViewport()?.SetInputAsHandled();
            ReturnToTitle();
            return;
        }

        _menuPanel.HandleUnhandledInput(this, @event);
    }

    public void MoveSelection(int delta)
    {
        _menu.MoveSelection(delta);
        _menuPanel.Redraw();
    }

    private static void DrawVictoryHeader(Control canvas, Font font)
    {
        // Centered translucent monolith scrim so Victory typography pops over the sunset horizon
        GeometricDraw.SkewedRect(canvas, CardPanelRect, 28f,
            RuinGeometryPalette.HudPanelDark with { A = 0.80f }, RuinGeometryPalette.PlatformTopLit, 2f);

        GeometricDraw.Diamond(canvas, new Vector2(640f, 162f), 7f, 10f, RuinGeometryPalette.GateUnlocked);
        canvas.DrawString(font, new Vector2(360f, 198f),
            "垂直切片通关", HorizontalAlignment.Center, 560f, 22, RuinGeometryPalette.HudBossTitle);

        GeometricMenuPanel.DrawShadowedText(canvas, font, new Vector2(340f, 263f),
            "遗迹守卫已击破", HorizontalAlignment.Center, 596f, 48,
            RuinGeometryPalette.GateUnlocked, RuinGeometryPalette.PlatformShadow, new Vector2(2f, 3f));

        GeometricMenuPanel.DrawAccentRule(canvas, new Rect2(465f, 300f, 350f, 3f),
            110f, 130f, RuinGeometryPalette.PlatformTopLit, RuinGeometryPalette.GateUnlocked);

        canvas.DrawString(font, new Vector2(360f, 344f),
            "荒原高塔前的试炼已告一段落",
            HorizontalAlignment.Center, 560f, 17, RuinGeometryPalette.HudTextLight with { A = 0.9f });

        canvas.DrawString(font, new Vector2(340f, 540f),
            "W / S 选择   Enter / J / A 确认   R 立即再战   Esc / B 返回标题",
            HorizontalAlignment.Center, 580f, 13, RuinGeometryPalette.HudTextLight with { A = 0.72f });
    }

    private void RematchBoss() => _menuPanel.TransitionTo(this, "res://Scenes/CoreLoop.tscn");

    private void ReturnToTitle() => _menuPanel.TransitionTo(this, "res://Scenes/Title.tscn");
}
