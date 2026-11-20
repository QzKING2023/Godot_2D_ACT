using System;
using System.Collections.Generic;
using ACTSpike.ACT.Input;
using Godot;

namespace ACTSpike.ACT.Visual;

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
        float w = rect.Size.X;
        float h = rect.Size.Y;

        Vector2[] poly =
        {
            p,
            p + new Vector2(w, 0f),
            p + new Vector2(w - skew, h),
            p + new Vector2(-skew, h)
        };

        Color fill = selected
            ? new Color(0.14f, 0.17f, 0.24f, 0.92f)
            : RuinGeometryPalette.HudPanelDark;
        Color border = selected
            ? RuinGeometryPalette.PlatformTopLit
            : new Color(RuinGeometryPalette.GateLocked, 0.75f);

        GeometricDraw.FillAndStrokePolygon(canvas, poly, fill, border, selected ? 2.2f : 1.2f);

        // Left accent strip
        Color stripColor = selected ? RuinGeometryPalette.TelegraphNormal : RuinGeometryPalette.GateLocked;
        GeometricDraw.FillPolygon(canvas, new[]
        {
            p,
            p + new Vector2(6f, 0f),
            p + new Vector2(6f - skew, h),
            p + new Vector2(-skew, h)
        }, stripColor);

        if (selected)
            GeometricDraw.Diamond(canvas, new Vector2(p.X + 20f - skew * 0.5f, p.Y + h * 0.5f), 5f, 6f, RuinGeometryPalette.TelegraphNormal);

        Color textColor = selected ? RuinGeometryPalette.PlayerHead : RuinGeometryPalette.HudTextLight;
        float baselineY = p.Y + (h + font.GetAscent(fontSize) - font.GetDescent(fontSize)) * 0.5f;
        float textX = p.X + (selected ? 36f : 24f);
        canvas.DrawString(font, new Vector2(textX, baselineY), label, HorizontalAlignment.Left, -1, fontSize, textColor);
    }

    public static float CenteredBaselineY(Rect2 rect, Font font, int fontSize)
        => rect.Position.Y + (rect.Size.Y + font.GetAscent(fontSize) - font.GetDescent(fontSize)) * 0.5f;
}
