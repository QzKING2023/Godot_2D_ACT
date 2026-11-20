using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Minimalist procedural geometric Chinese title screen.
/// Reuses RuinEnvironmentLayer (RuinStageTheme.Title) and GeometricMenuPanel, supporting keyboard, Xbox controller, and mouse.
/// When ACT_SMOKE=1 at startup, records EnteredFromTitleInSmoke and immediately redirects to CoreLoop.
/// </summary>
public partial class TitleScreen : Node2D
{
    private const float MenuX = 128f;
    private static readonly MenuOptionLayout OptionLayout = new(MenuX, 308f, 310f, 48f, 16f);

    public static bool EnteredFromTitleInSmoke { get; private set; }

    private GeometricMenuController _menu = null!;
    private GeometricMenuPanel? _menuPanel;

    public int SelectedIndex => _menu.SelectedIndex;
    public IReadOnlyList<string> OptionLabels => _menu.OptionLabels;

    public override void _Ready()
    {
        TimeSlowManager.EnsureNormalScale();
        GetTree().Paused = false;

        RuinEnvironmentLayer.CreateMenuBackdrop(this, RuinStageTheme.Title);

        _menu = new GeometricMenuController(
            new GeometricMenuController.Option("从教学开始", StartTutorial),
            new GeometricMenuController.Option("直接挑战 Boss", StartBossDirect),
            new GeometricMenuController.Option("退出", QuitGame));

        if (OS.GetEnvironment("ACT_SMOKE") == "1" && GetParent() == GetTree().Root)
        {
            EnteredFromTitleInSmoke = true;
            GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
            CallDeferred(MethodName.RedirectToSmokeArena);
            return;
        }

        _menuPanel = GeometricMenuPanel.Attach(
            this,
            "TitleUILayer",
            "TitleCanvas",
            _menu,
            OptionLayout,
            optionFontSize: 21,
            optionSkew: 12f,
            DrawTitleHeader);
    }

    public override void _Process(double delta) => _menuPanel?.PollStickNavigation();

    public override void _UnhandledInput(InputEvent @event)
        => _menuPanel?.HandleUnhandledInput(this, @event);

    public void MoveSelection(int delta)
    {
        _menu.MoveSelection(delta);
        _menuPanel?.Redraw();
    }

    private static void DrawTitleHeader(Control canvas, Font font)
    {
        // Subtle left-side scrim so Chinese title & menu remain high-contrast against warm horizon light
        GeometricDraw.FillPolygon(canvas, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(620f, 0f),
            new Vector2(510f, 720f),
            new Vector2(0f, 720f)
        }, RuinGeometryPalette.MenuTitleScrim);

        GeometricDraw.Diamond(canvas, new Vector2(MenuX + 6f, 118f), 5f, 7f, RuinGeometryPalette.PlatformTopLit);
        canvas.DrawString(font, new Vector2(MenuX + 22f, 124f),
            "遗迹几何表现 · 垂直切片",
            HorizontalAlignment.Left, -1, 15, RuinGeometryPalette.PlatformTopLit);

        GeometricMenuPanel.DrawShadowedText(canvas, font, new Vector2(MenuX, 190f),
            "遗迹行者", HorizontalAlignment.Left, -1f, 56,
            RuinGeometryPalette.HudTextLight, RuinGeometryPalette.PlatformShadow, new Vector2(3f, 3f));

        GeometricMenuPanel.DrawAccentRule(canvas, new Rect2(MenuX, 214f, 310f, 3f),
            0f, 96f, RuinGeometryPalette.PlatformTopLit, RuinGeometryPalette.TelegraphNormal);

        canvas.DrawString(font, new Vector2(MenuX, 246f),
            "荒原巨构与高塔之间的剑刃试炼",
            HorizontalAlignment.Left, -1, 17, RuinGeometryPalette.HudBossTitle);

        canvas.DrawString(font, new Vector2(MenuX, 652f),
            "W / S · 方向键 / 左摇杆 选择    Enter / Space / J / A 确认    鼠标点击",
            HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudTextLight with { A = 0.72f });
    }

    private void StartTutorial() => TransitionTo("res://Scenes/Tutorial/Movement.tscn");

    private void StartBossDirect() => TransitionTo("res://Scenes/CoreLoop.tscn");

    private void QuitGame()
    {
        if (_menuPanel is not null)
            _menuPanel.QuitGame(this);
        else
            GetTree().Quit(0);
    }

    private void TransitionTo(string scenePath)
    {
        if (_menuPanel is not null)
        {
            _menuPanel.TransitionTo(this, scenePath);
            return;
        }
        TimeSlowManager.EnsureNormalScale();
        GetTree().Paused = false;
        GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        GetTree().ChangeSceneToFile(scenePath);
    }

    private void RedirectToSmokeArena()
    {
        GetTree().ChangeSceneToFile("res://Scenes/CoreLoop.tscn");
    }
}
