using System.Collections.Generic;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Upgraded Simplified Chinese victory screen reusing RuinEnvironmentLayer (RuinStageTheme.Victory),
/// GeometricDraw, RuinGeometryPalette, and UiFonts.Default.
/// Supports keyboard, Xbox controller, mouse hover/click, and R rematch shortcut.
/// </summary>
public partial class VictoryScreen : Node2D
{
    private const float MenuX = 485f;
    private const float MenuStartY = 398f;
    private const float OptionWidth = 310f;
    private const float OptionHeight = 48f;
    private const float OptionGap = 16f;

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
            Theme = RuinStageTheme.Victory,
            FallbackCameraCenter = new Vector2(640f, 360f)
        };
        AddChild(_environment);
        _environment.RegisterSurface(new Rect2(0f, 560f, 1280f, 160f));

        _menu = new GeometricMenuController(
            new GeometricMenuController.Option("再战 Boss", RematchBoss),
            new GeometricMenuController.Option("返回标题", ReturnToTitle));

        var uiLayer = new CanvasLayer { Name = "VictoryUILayer", Layer = 1 };
        _uiCanvas = new Control
        {
            Name = "VictoryCanvas",
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _uiCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiCanvas.Draw += OnDrawVictoryUi;
        _uiCanvas.GuiInput += OnGuiInput;
        uiLayer.AddChild(_uiCanvas);
        AddChild(uiLayer);
    }

    public override void _Process(double delta)
    {
        if (_transitioning) return;

        if (ActInput.ResetPressed())
        {
            RematchBoss();
            return;
        }

        if (_menu.PollStickNavigation())
            _uiCanvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_transitioning) return;

        if (ActInput.IsMenuBackEvent(@event))
        {
            GetViewport()?.SetInputAsHandled();
            ReturnToTitle();
            return;
        }

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

    private void OnDrawVictoryUi()
    {
        Font font = UiFonts.Default;

        // Centered translucent monolith scrim so Victory typography pops over the sunset horizon
        GeometricDraw.FillAndStrokePolygon(_uiCanvas, new[]
        {
            new Vector2(340f, 118f),
            new Vector2(960f, 118f),
            new Vector2(932f, 566f),
            new Vector2(312f, 566f)
        }, RuinGeometryPalette.HudPanelDark with { A = 0.80f }, RuinGeometryPalette.PlatformTopLit, 2f);

        GeometricDraw.Diamond(_uiCanvas, new Vector2(640f, 162f), 7f, 10f, RuinGeometryPalette.GateUnlocked);
        _uiCanvas.DrawString(font, new Vector2(360f, 198f),
            "垂直切片通关", HorizontalAlignment.Center, 560f, 22, RuinGeometryPalette.HudBossTitle);

        const string heading = "遗迹守卫已击破";
        _uiCanvas.DrawString(font, new Vector2(342f, 266f),
            heading, HorizontalAlignment.Center, 596f, 48, RuinGeometryPalette.PlatformShadow);
        _uiCanvas.DrawString(font, new Vector2(340f, 263f),
            heading, HorizontalAlignment.Center, 596f, 48, RuinGeometryPalette.GateUnlocked);

        _uiCanvas.DrawRect(new Rect2(465f, 300f, 350f, 3f), RuinGeometryPalette.PlatformTopLit);
        _uiCanvas.DrawRect(new Rect2(575f, 300f, 130f, 3f), RuinGeometryPalette.GateUnlocked);

        _uiCanvas.DrawString(font, new Vector2(360f, 344f),
            "荒原高塔前的试炼已告一段落",
            HorizontalAlignment.Center, 560f, 17, RuinGeometryPalette.HudTextLight with { A = 0.9f });

        _menu.DrawOptions(_uiCanvas, font, OptionRect, fontSize: 20, skew: 12f);

        _uiCanvas.DrawString(font, new Vector2(340f, 540f),
            "W / S 选择   Enter / J / A 确认   R 立即再战   Esc / B 返回标题",
            HorizontalAlignment.Center, 580f, 13, RuinGeometryPalette.HudTextLight with { A = 0.72f });
    }

    private static Rect2 OptionRect(int index)
        => new(MenuX, MenuStartY + index * (OptionHeight + OptionGap), OptionWidth, OptionHeight);

    private void RematchBoss() => TransitionTo("res://Scenes/CoreLoop.tscn");

    private void ReturnToTitle() => TransitionTo("res://Scenes/Title.tscn");

    private void TransitionTo(string scenePath)
    {
        if (_transitioning) return;
        _transitioning = true;
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
        GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        GetTree().ChangeSceneToFile(scenePath);
    }
}
