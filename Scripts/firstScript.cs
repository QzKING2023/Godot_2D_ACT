using Godot;
using System;

public partial class firstScript : Node
{
    public override void _Ready()
    {
        GD.Print("Hello World!");
    }

    public override void _Process(double delta)
    {
    }
}
