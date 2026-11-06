using Godot;
using ACTSpike.ACT.Input;

namespace ACTSpike.ACT.Arena;

public partial class VictoryScreen : Node2D
{
    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (ActInput.ResetPressed())
            GetTree().ChangeSceneToFile("res://Scenes/CoreLoop.tscn");
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("101522"));
        DrawString(ThemeDB.FallbackFont, new Vector2(430, 300), "BOSS DEFEATED", HorizontalAlignment.Left, -1, 48, new Color("80ed99"));
        DrawString(ThemeDB.FallbackFont, new Vector2(470, 360), "ROUTE CLEAR", HorizontalAlignment.Left, -1, 28, new Color("ffd166"));
        DrawString(ThemeDB.FallbackFont, new Vector2(455, 430), "Press R to restart", HorizontalAlignment.Left, -1, 22, Colors.White);
    }
}
