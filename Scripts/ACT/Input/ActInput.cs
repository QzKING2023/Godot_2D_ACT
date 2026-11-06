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
    public static bool InteractPressed() => Godot.Input.IsActionJustPressed("interact");
    public static bool ResetPressed() => Godot.Input.IsActionJustPressed("reset_round");
    public static bool TimeSlowHeld() => Godot.Input.IsActionPressed("time_slow");
}
