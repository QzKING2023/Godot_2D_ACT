using System.Collections.Generic;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Minimalist procedural geometric Chinese title screen.
/// Reuses RuinEnvironmentLayer (RuinStageTheme.Title) and supports keyboard, Xbox controller, and mouse.
/// When ACT_SMOKE=1 at startup, records EnteredFromTitleInSmoke and immediately redirects to CoreLoop.
/// </summary>
public partial class TitleScreen : Node2D
{
    private const float MenuX = 128f;
    private const float MenuStartY = 308f;
    private const float OptionWidth = 310f;
    private const float OptionHeight = 48f;
    private const float OptionGap = 16f;

    public static bool EnteredFromTitleInSmoke { get; private set; }

    private RuinEnvironmentLayer _environment = null!;
    private Control _uiCanvas = null!;
    private GeometricMenuController _menu = null!;
    private bool _transitioning;

    public int SelectedIndex => _menu.SelectedIndex;

    public IReadOnlyList<string> OptionLabels
    {
        get
        {
            var labels = new string[_menu.Options.Count];
            for (int i = 0; i < _menu.Options.Count; i++)
                labels[i] = _menu.Options[i].Label;
            return labels;
        }
    }

    public override void _Ready()
    {
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;

        _environment = new RuinEnvironmentLayer
        {
            Name = "RuinEnvironment",
            Theme = RuinStageTheme.Title,
            FallbackCameraCenter = new Vector2(640f, 360f)
        };
        AddChild(_environment);
        _environment.RegisterSurface(new Rect2(0f, 560f, 1280f, 160f));

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

        BuildUiOverlay();
    }

    public override void _Process(double delta)
    {
        if (_transitioning || _uiCanvas is null) return;
        if (_menu.PollStickNavigation())
            _uiCanvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_transitioning || _uiCanvas is null) return;
        if (_menu.HandleKeyAndPadInput(@event, out bool selectionChanged, out _))
        {
            GetViewport()?.SetInputAsHandled();
            if (selectionChanged)
                _uiCanvas.QueueRedraw();
        }
    }

    public void MoveSelection(int delta)
    {
        _menu.MoveSelection(delta);
        _uiCanvas?.QueueRedraw();
    }

    private void BuildUiOverlay()
    {
        var uiLayer = new CanvasLayer { Name = "TitleUILayer", Layer = 1 };
        _uiCanvas = new Control
        {
            Name = "TitleCanvas",
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _uiCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiCanvas.Draw += OnDrawTitleUi;
        _uiCanvas.GuiInput += OnGuiInput;
        uiLayer.AddChild(_uiCanvas);
        AddChild(uiLayer);
    }

    private void OnGuiInput(InputEvent ev)
    {
        if (_transitioning) return;
        if (_menu.HandleGuiInput(ev, OptionRect, out bool selectionChanged, out _))
        {
            _uiCanvas.AcceptEvent();
            if (selectionChanged)
                _uiCanvas.QueueRedraw();
        }
    }

    private void OnDrawTitleUi()
    {
        Font font = UiFonts.Default;

        // Subtle left-side scrim so Chinese title & menu remain high-contrast against warm horizon light
        GeometricDraw.FillPolygon(_uiCanvas, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(620f, 0f),
            new Vector2(510f, 720f),
            new Vector2(0f, 720f)
        }, new Color(0.06f, 0.08f, 0.12f, 0.56f));

        // Eyebrow badge
        GeometricDraw.Diamond(_uiCanvas, new Vector2(MenuX + 6f, 118f), 5f, 7f, RuinGeometryPalette.PlatformTopLit);
        _uiCanvas.DrawString(font, new Vector2(MenuX + 22f, 124f),
            "遗迹几何表现 · 垂直切片",
            HorizontalAlignment.Left, -1, 15, RuinGeometryPalette.PlatformTopLit);

        // Main title with faceted hard-edge shadow
        const string titleText = "遗迹行者";
        _uiCanvas.DrawString(font, new Vector2(MenuX + 3f, 193f),
            titleText, HorizontalAlignment.Left, -1, 56, RuinGeometryPalette.PlatformShadow);
        _uiCanvas.DrawString(font, new Vector2(MenuX, 190f),
            titleText, HorizontalAlignment.Left, -1, 56, RuinGeometryPalette.HudTextLight);

        // Geometric rule accent
        _uiCanvas.DrawRect(new Rect2(MenuX, 214f, 310f, 3f), RuinGeometryPalette.PlatformTopLit);
        _uiCanvas.DrawRect(new Rect2(MenuX, 214f, 96f, 3f), RuinGeometryPalette.TelegraphNormal);

        _uiCanvas.DrawString(font, new Vector2(MenuX, 246f),
            "荒原巨构与高塔之间的剑刃试炼",
            HorizontalAlignment.Left, -1, 17, RuinGeometryPalette.HudBossTitle);

        _menu.DrawOptions(_uiCanvas, font, OptionRect, fontSize: 21, skew: 12f);

        _uiCanvas.DrawString(font, new Vector2(MenuX, 652f),
            "W / S · 方向键 / 左摇杆 选择    Enter / Space / J / A 确认    鼠标点击",
            HorizontalAlignment.Left, -1, 14, RuinGeometryPalette.HudTextLight with { A = 0.72f });
    }

    private static Rect2 OptionRect(int index)
        => new(MenuX, MenuStartY + index * (OptionHeight + OptionGap), OptionWidth, OptionHeight);

    private void StartTutorial() => TransitionTo("res://Scenes/Tutorial/Movement.tscn");

    private void StartBossDirect() => TransitionTo("res://Scenes/CoreLoop.tscn");

    private void QuitGame()
    {
        if (_transitioning) return;
        _transitioning = true;
        GetTree().Quit(0);
    }

    private void TransitionTo(string scenePath)
    {
        if (_transitioning) return;
        _transitioning = true;
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
        GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        GetTree().ChangeSceneToFile(scenePath);
    }

    private void RedirectToSmokeArena()
    {
        GetTree().ChangeSceneToFile("res://Scenes/CoreLoop.tscn");
    }
}
