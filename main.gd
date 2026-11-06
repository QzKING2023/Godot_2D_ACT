extends Node2D
func _ready():
    queue_redraw()
func _draw():
    draw_rect(Rect2(0,0,1280,720), Color("101522"))
    draw_string(ThemeDB.fallback_font, Vector2(40,80), "ACT Spike - Godot 4.5.1", HORIZONTAL_ALIGNMENT_LEFT, -1, 32, Color.WHITE)
    draw_string(ThemeDB.fallback_font, Vector2(40,130), "Environment smoke project", HORIZONTAL_ALIGNMENT_LEFT, -1, 22, Color("a8dadc"))
