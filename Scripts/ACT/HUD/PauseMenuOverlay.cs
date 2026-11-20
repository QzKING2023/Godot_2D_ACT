using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.HUD;

/// <summary>
/// Minimalist Esc / Xbox Start pause overlay (`Layer = 20`, `ProcessMode = Always`).
/// Attached to TestArena, TutorialStage, and FormalStage; guarantees GetTree().Paused = false
/// and Engine.TimeScale = 1.0 on resume, reload, title return, and teardown.
/// </summary>
public partial class PauseMenuOverlay : CanvasLayer
{
    private static readonly Rect2 PanelRect = new(430f, 178f, 420f, 364f);
    private const float OptionX = 484f;
    private const float OptionStartY = 278f;
    private const float OptionWidth = 312f;
    private const float OptionHeight = 46f;
    private const float OptionGap = 14f;

    private TimeSlowManager? _timeSlow;
    private Control _canvas = null!;
    private GeometricMenuController _menu = null!;
    private ulong _openedFrame = ulong.MaxValue;
    private bool _pendingDeferredResume;

    public bool IsPaused => IsInsideTree() && GetTree().Paused;
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

    public static PauseMenuOverlay Attach(Node stage, TimeSlowManager? timeSlow = null)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var overlay = new PauseMenuOverlay
        {
            Name = "PauseMenuLayer",
            Layer = 20,
            ProcessMode = ProcessModeEnum.Always,
            Visible = false,
            _timeSlow = timeSlow
        };
        stage.AddChild(overlay);
        return overlay;
    }

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        _menu = new GeometricMenuController(
            new GeometricMenuController.Option("继续", RequestResume),
            new GeometricMenuController.Option("重置当前关", RestartCurrentStage),
            new GeometricMenuController.Option("返回标题", ReturnToTitle));

        _canvas = new Control
        {
            Name = "PauseCanvas",
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += OnDrawPause;
        _canvas.GuiInput += OnGuiInput;
        AddChild(_canvas);
    }

    public override void _Process(double delta)
    {
        if (ActInput.PausePressed())
        {
            if (!Visible && !_pendingDeferredResume)
            {
                Open();
                return;
            }
            if (Visible && Engine.GetProcessFrames() != _openedFrame)
            {
                RequestResume();
                return;
            }
        }

        if (!Visible) return;

        if (_menu.PollStickNavigation())
            _canvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || Engine.GetProcessFrames() == _openedFrame) return;

        // "pause" (Esc / Xbox Start) is owned exclusively by _Process to prevent double-toggles.
        if (@event.IsAction("pause") ||
            @event is InputEventKey k && (k.PhysicalKeycode == Key.Escape || k.Keycode == Key.Escape))
        {
            return;
        }

        if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
        {
            GetViewport()?.SetInputAsHandled();
            RequestResume();
            return;
        }

        if (_menu.HandleKeyAndPadInput(@event, out bool selectionChanged, out _))
        {
            GetViewport()?.SetInputAsHandled();
            if (selectionChanged)
                _canvas.QueueRedraw();
        }
    }

    public void Open()
    {
        _pendingDeferredResume = false;
        _openedFrame = Engine.GetProcessFrames();
        _menu.ResetSelection(0);
        _timeSlow?.Notification((int)NotificationPaused);
        Engine.TimeScale = 1.0;
        if (IsInsideTree())
            GetTree().Paused = true;
        Visible = true;
        if (_canvas is not null)
        {
            _canvas.MouseFilter = Control.MouseFilterEnum.Stop;
            _canvas.QueueRedraw();
        }
    }

    public void Close()
    {
        _pendingDeferredResume = false;
        Visible = false;
        if (_canvas is not null)
            _canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        _timeSlow?.Notification((int)NotificationUnpaused);
        Engine.TimeScale = 1.0;
        if (IsInsideTree())
            GetTree().Paused = false;
    }

    public void Toggle()
    {
        if (IsPaused || Visible)
            Close();
        else
            Open();
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = 1.0;
        if (GetTree() is { } tree)
            tree.Paused = false;
    }

    private void RequestResume()
    {
        if (_pendingDeferredResume) return;
        _pendingDeferredResume = true;
        Visible = false;
        if (_canvas is not null)
            _canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        Engine.TimeScale = 1.0;
        // Defer unpausing until after this frame's _Process passes so Space/A/LMB doesn't bleed into gameplay.
        CallDeferred(MethodName.Close);
    }

    private void RestartCurrentStage()
    {
        Close();
        GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        GetTree().ReloadCurrentScene();
    }

    private void ReturnToTitle()
    {
        Close();
        GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        GetTree().ChangeSceneToFile("res://Scenes/Title.tscn");
    }

    private void OnGuiInput(InputEvent ev)
    {
        if (!Visible) return;
        if (_menu.HandleGuiInput(ev, OptionRect, out bool selectionChanged, out _))
        {
            _canvas.AcceptEvent();
            if (selectionChanged)
                _canvas.QueueRedraw();
        }
    }

    private void OnDrawPause()
    {
        if (_canvas is null) return;
        Font font = UiFonts.Default;

        _canvas.DrawRect(new Rect2(0f, 0f, 1280f, 720f), new Color(0.04f, 0.06f, 0.09f, 0.72f));

        Vector2 p = PanelRect.Position;
        float w = PanelRect.Size.X;
        float h = PanelRect.Size.Y;
        const float skew = 14f;

        Vector2[] panelPoly =
        {
            p,
            p + new Vector2(w, 0f),
            p + new Vector2(w - skew, h),
            p + new Vector2(-skew, h)
        };
        GeometricDraw.FillAndStrokePolygon(_canvas, panelPoly,
            RuinGeometryPalette.HudPanelDark with { A = 0.95f },
            RuinGeometryPalette.PlatformTopLit, 2f);

        _canvas.DrawRect(new Rect2(p.X + 24f, p.Y + 24f, 72f, 3f), RuinGeometryPalette.TelegraphNormal);
        _canvas.DrawString(font, new Vector2(p.X + 24f, p.Y + 62f),
            "已暂停", HorizontalAlignment.Left, -1, 30, RuinGeometryPalette.HudTextLight);
        _canvas.DrawRect(new Rect2(p.X + 24f, p.Y + 78f, w - 58f, 1.5f), RuinGeometryPalette.GateLocked);

        _menu.DrawOptions(_canvas, font, OptionRect, fontSize: 19, skew: 10f);

        _canvas.DrawString(font, new Vector2(p.X + 22f, p.Y + h - 22f),
            "W / S 选择   Enter / J / A 确认   Esc / B 继续",
            HorizontalAlignment.Left, -1, 13, RuinGeometryPalette.HudTextLight with { A = 0.7f });
    }

    private static Rect2 OptionRect(int index)
        => new(OptionX, OptionStartY + index * (OptionHeight + OptionGap), OptionWidth, OptionHeight);
}
