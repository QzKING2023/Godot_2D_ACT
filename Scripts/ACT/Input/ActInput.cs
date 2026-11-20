using Godot;

namespace ACTSpike.ACT.Input;

public static class ActInput
{
    public static Vector2 MoveAxis()
    {
        return Godot.Input.GetVector("move_left", "move_right", "move_up", "move_down");
    }

    public static bool JumpPressed() => Godot.Input.IsActionJustPressed("jump");
    public static bool AttackPressed() => Godot.Input.IsActionJustPressed("attack");
    public static bool DodgePressed() => Godot.Input.IsActionJustPressed("dodge");
    public static bool BlockHeld() => Godot.Input.IsActionPressed("block");
    public static bool TimeSlowPressed() => Godot.Input.IsActionJustPressed("time_slow");
    public static bool SkillPressed() => Godot.Input.IsActionJustPressed("skill");
    public static bool SkillHeld() => Godot.Input.IsActionPressed("skill");
    public static bool ResetPressed() => Godot.Input.IsActionJustPressed("reset_round");
    public static bool TimeSlowHeld() => Godot.Input.IsActionPressed("time_slow");

    public static bool PausePressed() => Godot.Input.IsActionJustPressed("pause");
    public static bool ToggleDebugPressed() => Godot.Input.IsActionJustPressed("toggle_debug");

    public static bool MenuUpPressed()
        => Godot.Input.IsActionJustPressed("ui_up") || Godot.Input.IsActionJustPressed("move_up");

    public static bool MenuDownPressed()
        => Godot.Input.IsActionJustPressed("ui_down") || Godot.Input.IsActionJustPressed("move_down");

    public static bool MenuConfirmPressed()
        => Godot.Input.IsActionJustPressed("ui_accept") || Godot.Input.IsActionJustPressed("jump");

    public static bool MenuBackPressed()
        => Godot.Input.IsActionJustPressed("ui_cancel") || Godot.Input.IsActionJustPressed("pause");

    public static bool IsMenuUpEvent(InputEvent ev)
    {
        if (ev is InputEventJoypadMotion) return false;
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
            return key.PhysicalKeycode is Key.W or Key.Up || key.Keycode is Key.W or Key.Up;
        if (ev is InputEventJoypadButton { Pressed: true } pad)
            return pad.ButtonIndex == JoyButton.DpadUp;
        return ev.IsActionPressed("ui_up", allowEcho: false) || ev.IsActionPressed("move_up", allowEcho: false);
    }

    public static bool IsMenuDownEvent(InputEvent ev)
    {
        if (ev is InputEventJoypadMotion) return false;
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
            return key.PhysicalKeycode is Key.S or Key.Down || key.Keycode is Key.S or Key.Down;
        if (ev is InputEventJoypadButton { Pressed: true } pad)
            return pad.ButtonIndex == JoyButton.DpadDown;
        return ev.IsActionPressed("ui_down", allowEcho: false) || ev.IsActionPressed("move_down", allowEcho: false);
    }

    public static bool IsMenuConfirmEvent(InputEvent ev)
    {
        if (ev is InputEventMouseButton or InputEventMouseMotion or InputEventJoypadMotion) return false;
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
            return key.PhysicalKeycode is Key.Enter or Key.KpEnter or Key.Space or Key.J
                || key.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.J;
        if (ev is InputEventJoypadButton { Pressed: true } pad)
            return pad.ButtonIndex == JoyButton.A;
        return ev.IsActionPressed("ui_accept", allowEcho: false) || ev.IsActionPressed("jump", allowEcho: false);
    }

    public static bool IsMenuBackEvent(InputEvent ev)
    {
        if (ev is InputEventMouseButton or InputEventMouseMotion or InputEventJoypadMotion) return false;
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
            return key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape;
        if (ev is InputEventJoypadButton { Pressed: true } pad)
            return pad.ButtonIndex == JoyButton.B;
        return ev.IsActionPressed("ui_cancel", allowEcho: false) || ev.IsActionPressed("pause", allowEcho: false);
    }
}
