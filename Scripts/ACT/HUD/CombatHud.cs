using Godot;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;

namespace ACTSpike.ACT.HUD;

/// <summary>
/// Placeholder HUD drawn procedurally: HP / Guard / Time bars,
/// player & enemy state, last combat line, controls. Replace with a
/// Control-based skin later; this only reads state, it owns no logic.
/// </summary>
public partial class CombatHud : Control
{
    private const float BarX = 32f;
    private const float BarW = 260f;
    private const float BarH = 16f;

    public PlayerController Player { get; set; } = null!;
    public EnemyController Enemy { get; set; } = null!;
    public Combat.TimeSlowManager TimeSlow { get; set; } = null!;

    private string _lastCombatLine = "";
    private string _lastTimeLine = "";

    public void ReportCombat(string line) => _lastCombatLine = line;
    public void ReportTime(string line) => _lastTimeLine = line;

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(32, 40), "ACT CORE LOOP | placeholder arena", HorizontalAlignment.Left, -1, 24, Colors.White);
        DrawString(font, new Vector2(32, 68),
            "A/D move  Space jump  J/LMB attack  Shift dodge  Ctrl block(hold)  Q time slow(hold)  K skill  E interact  R reset",
            HorizontalAlignment.Left, -1, 15, new Color("a8dadc"));
        DrawString(font, new Vector2(32, 90),
            "Pad: LS move  A jump  X attack  B dodge  RB block  LB slow  Y skill",
            HorizontalAlignment.Left, -1, 15, new Color("a8dadc"));

        float y = 130f;
        Bar("HP", Player.Hp / Player.MaxHp, new Color("e63946"), y);
        Bar("Guard", Player.Guard / Player.MaxGuard, new Color("4cc9f0"), y + 26);
        Bar("Time", TimeSlow.ResourceFraction, new Color("9d4edd"), y + 52);

        DrawString(font, new Vector2(BarX + BarW + 16, y + 13),
            $"state {Player.State}{(Player.IsDown ? "  DOWN - press R" : "")}",
            HorizontalAlignment.Left, -1, 18, Player.IsDown ? Colors.Red : Colors.White);
        DrawString(font, new Vector2(BarX + BarW + 16, y + 39),
            $"enemy {Enemy.State}  hp {Enemy.Hp:0}/{Enemy.MaxHp:0}",
            HorizontalAlignment.Left, -1, 18, Colors.Orange);

        DrawString(font, new Vector2(32, 250), $"combat: {_lastCombatLine}", HorizontalAlignment.Left, -1, 19, Colors.Yellow);
        DrawString(font, new Vector2(32, 276), $"time:   {_lastTimeLine}", HorizontalAlignment.Left, -1, 16, new Color("c77dff"));
        if (TimeSlow.Active)
            DrawString(font, new Vector2(1090, 40), "TIME SLOW", HorizontalAlignment.Left, -1, 26, new Color("9d4edd"));
    }

    private void Bar(string label, float fraction, Color color, float y)
    {
        fraction = Mathf.Clamp(fraction, 0f, 1f);
        DrawString(ThemeDB.FallbackFont, new Vector2(BarX, y - 4), label, HorizontalAlignment.Left, -1, 13, Colors.LightGray);
        DrawRect(new Rect2(BarX, y, BarW, BarH), new Color(1, 1, 1, 0.12f));
        DrawRect(new Rect2(BarX, y, BarW * fraction, BarH), color);
    }
}
