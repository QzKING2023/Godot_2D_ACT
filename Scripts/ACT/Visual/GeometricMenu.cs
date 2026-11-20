using System;
using System.Collections.Generic;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Input;
using Godot;

namespace ACTSpike.ACT.Visual;

public readonly record struct MenuOptionLayout(float X, float StartY, float Width, float Height, float Gap)
{
    public Rect2 RectAt(int index)
        => new(X, StartY + index * (Height + Gap), Width, Height);
}

/// <summary>
/// Shared hard-edged geometric menu navigation and drawing helper for Title, Pause, and Victory screens.
/// Supports keyboard, Xbox controller (D-pad, Left Stick, A/B), and mouse hover/click without FocusMode conflicts.
/// </summary>
public sealed class GeometricMenuController
{
    public sealed record Option(string Label, Action Activate);

    private readonly List<Option> _options = new();
    private bool _stickUpHeld;
    private bool _stickDownHeld;

    public int SelectedIndex { get; private set; }
    public IReadOnlyList<Option> Options => _options;

    public IReadOnlyList<string> OptionLabels
    {
        get
        {
            var labels = new string[_options.Count];
            for (int i = 0; i < _options.Count; i++)
                labels[i] = _options[i].Label;
            return labels;
        }
    }

    public GeometricMenuController(params Option[] options)
    {
        _options.AddRange(options);
    }

    public void ResetSelection(int index = 0)
    {
        if (_options.Count == 0)
        {
            SelectedIndex = 0;
            return;
        }
        SelectedIndex = Mathf.Clamp(index, 0, _options.Count - 1);
        _stickUpHeld = false;
        _stickDownHeld = false;
    }

    public void MoveSelection(int delta)
    {
        if (_options.Count == 0 || delta == 0) return;
        SelectedIndex = ((SelectedIndex + delta) % _options.Count + _options.Count) % _options.Count;
    }

    public bool PollStickNavigation()
    {
        float y = ActInput.MoveAxis().Y;
        bool moved = false;

        if (y < -0.5f)
        {
            if (!_stickUpHeld)
            {
                _stickUpHeld = true;
                MoveSelection(-1);
                moved = true;
            }
        }
        else if (y > -0.3f)
        {
            _stickUpHeld = false;
        }

        if (y > 0.5f)
        {
            if (!_stickDownHeld)
            {
                _stickDownHeld = true;
                MoveSelection(1);
                moved = true;
            }
        }
        else if (y < 0.3f)
        {
            _stickDownHeld = false;
        }

        return moved;
    }

    public bool HandleGuiInput(InputEvent ev, Func<int, Rect2> rectForIndex, out bool selectionChanged, out bool activated)
    {
        selectionChanged = false;
        activated = false;

        if (ev is InputEventMouseMotion motion)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (rectForIndex(i).HasPoint(motion.Position))
                {
                    if (SelectedIndex != i)
                    {
                        SelectedIndex = i;
                        selectionChanged = true;
                    }
                    return true;
                }
            }
            return false;
        }

        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (rectForIndex(i).HasPoint(click.Position))
                {
                    SelectedIndex = i;
                    selectionChanged = true;
                    activated = true;
                    _options[i].Activate();
                    return true;
                }
            }
            return false;
        }

        return HandleKeyAndPadInput(ev, out selectionChanged, out activated);
    }

    public bool HandleKeyAndPadInput(InputEvent ev, out bool selectionChanged, out bool activated)
    {
        selectionChanged = false;
        activated = false;

        if (ActInput.IsMenuUpEvent(ev))
        {
            MoveSelection(-1);
            selectionChanged = true;
            return true;
        }

        if (ActInput.IsMenuDownEvent(ev))
        {
            MoveSelection(1);
            selectionChanged = true;
            return true;
        }

        if (ActInput.IsMenuConfirmEvent(ev))
        {
            if (SelectedIndex >= 0 && SelectedIndex < _options.Count)
            {
                activated = true;
                _options[SelectedIndex].Activate();
                return true;
            }
        }

        return false;
    }

    public void DrawOptions(
        CanvasItem canvas,
        Font font,
        Func<int, Rect2> rectForIndex,
        int fontSize = 20,
        float skew = 10f)
    {
        for (int i = 0; i < _options.Count; i++)
        {
            Rect2 rect = rectForIndex(i);
            bool selected = i == SelectedIndex;
            DrawOptionButton(canvas, font, rect, _options[i].Label, selected, fontSize, skew);
        }
    }

    public static void DrawOptionButton(
        CanvasItem canvas,
        Font font,
        Rect2 rect,
        string label,
        bool selected,
        int fontSize = 20,
        float skew = 10f)
    {
        Vector2 p = rect.Position;
        float h = rect.Size.Y;

        Color fill = selected
            ? RuinGeometryPalette.MenuOptionSelectedFill
            : RuinGeometryPalette.HudPanelDark;
        Color border = selected
            ? RuinGeometryPalette.PlatformTopLit
            : RuinGeometryPalette.MenuOptionBorder;

        GeometricDraw.SkewedRect(canvas, rect, skew, fill, border, selected ? 2.2f : 1.2f);

        // Left accent strip
        Color stripColor = selected ? RuinGeometryPalette.TelegraphNormal : RuinGeometryPalette.GateLocked;
        GeometricDraw.SkewedRect(canvas, new Rect2(p, new Vector2(6f, h)), skew, stripColor);

        if (selected)
            GeometricDraw.Diamond(canvas, new Vector2(p.X + 20f - skew * 0.5f, p.Y + h * 0.5f), 5f, 6f, RuinGeometryPalette.TelegraphNormal);

        Color textColor = selected ? RuinGeometryPalette.PlayerHead : RuinGeometryPalette.HudTextLight;
        float baselineY = CenteredBaselineY(rect, font, fontSize);
        float textX = p.X + (selected ? 36f : 24f);
        canvas.DrawString(font, new Vector2(textX, baselineY), label, HorizontalAlignment.Left, -1, fontSize, textColor);
    }

    public static float CenteredBaselineY(Rect2 rect, Font font, int fontSize)
        => rect.Position.Y + (rect.Size.Y + font.GetAscent(fontSize) - font.GetDescent(fontSize)) * 0.5f;
}

/// <summary>
/// Shared geometric menu CanvasLayer + Control host handling input, mouse hover/click,
/// stick navigation, scene transitions, and shadowed header/accent drawing for full-screen menus.
/// </summary>
public sealed partial class GeometricMenuPanel : CanvasLayer
{
    private Control _canvas = null!;
    private GeometricMenuController _menu = null!;
    private MenuOptionLayout _layout;
    private int _optionFontSize = 20;
    private float _optionSkew = 12f;
    private Action<Control, Font>? _drawHeader;
    private bool _transitioning;

    public bool IsTransitioning => _transitioning;

    public static GeometricMenuPanel Attach(
        Node owner,
        string layerName,
        string canvasName,
        GeometricMenuController menu,
        MenuOptionLayout layout,
        int optionFontSize,
        float optionSkew,
        Action<Control, Font> drawHeader)
    {
        var panel = new GeometricMenuPanel
        {
            Name = layerName,
            Layer = 1,
            _menu = menu,
            _layout = layout,
            _optionFontSize = optionFontSize,
            _optionSkew = optionSkew,
            _drawHeader = drawHeader
        };

        panel._canvas = new Control
        {
            Name = canvasName,
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        panel._canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel._canvas.Draw += panel.OnDraw;
        panel._canvas.GuiInput += panel.OnGuiInput;
        panel.AddChild(panel._canvas);
        owner.AddChild(panel);
        return panel;
    }

    public void Redraw() => _canvas?.QueueRedraw();

    public void PollStickNavigation()
    {
        if (_transitioning || _canvas is null) return;
        if (_menu.PollStickNavigation())
            _canvas.QueueRedraw();
    }

    public bool HandleUnhandledInput(Node owner, InputEvent ev)
    {
        if (_transitioning || _canvas is null) return false;
        if (_menu.HandleKeyAndPadInput(ev, out bool selectionChanged, out _))
        {
            owner.GetViewport()?.SetInputAsHandled();
            if (selectionChanged)
                _canvas.QueueRedraw();
            return true;
        }
        return false;
    }

    public void TransitionTo(Node owner, string scenePath)
    {
        if (_transitioning) return;
        _transitioning = true;
        TimeSlowManager.EnsureNormalScale();
        owner.GetTree().Paused = false;
        owner.GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        owner.GetTree().ChangeSceneToFile(scenePath);
    }

    public void QuitGame(Node owner)
    {
        if (_transitioning) return;
        _transitioning = true;
        owner.GetTree().Quit(0);
    }

    public static void DrawShadowedText(
        CanvasItem canvas,
        Font font,
        Vector2 position,
        string text,
        HorizontalAlignment alignment,
        float width,
        int fontSize,
        Color color,
        Color shadowColor,
        Vector2 shadowOffset)
    {
        canvas.DrawString(font, position + shadowOffset, text, alignment, width, fontSize, shadowColor);
        canvas.DrawString(font, position, text, alignment, width, fontSize, color);
    }

    public static void DrawAccentRule(
        CanvasItem canvas,
        Rect2 ruleRect,
        float accentOffsetX,
        float accentWidth,
        Color baseColor,
        Color accentColor)
    {
        canvas.DrawRect(ruleRect, baseColor);
        canvas.DrawRect(new Rect2(ruleRect.Position.X + accentOffsetX, ruleRect.Position.Y, accentWidth, ruleRect.Size.Y), accentColor);
    }

    private void OnDraw()
    {
        if (_canvas is null) return;
        Font font = UiFonts.Default;
        _drawHeader?.Invoke(_canvas, font);
        _menu.DrawOptions(_canvas, font, _layout.RectAt, fontSize: _optionFontSize, skew: _optionSkew);
    }

    private void OnGuiInput(InputEvent ev)
    {
        if (_transitioning) return;
        if (_menu.HandleGuiInput(ev, _layout.RectAt, out bool selectionChanged, out _))
        {
            _canvas.AcceptEvent();
            if (selectionChanged)
                _canvas.QueueRedraw();
        }
    }
}
