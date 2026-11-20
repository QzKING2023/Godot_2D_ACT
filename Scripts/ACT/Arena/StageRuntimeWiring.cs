using System;
using ACTSpike.ACT.Audio;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Core;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.HUD;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.Arena;

/// <summary>
/// Shared stage runtime wiring across TestArena, TutorialStage, and FormalStage:
/// mounts presentation feedback, HUD, pause menu, audio controller, wires F3/resource signals,
/// and handles round reset requests consistently.
/// </summary>
public static class StageRuntimeWiring
{
    public static CombatHud MountStagePresentationAndHud(
        Node2D stage,
        PlayerController player,
        EnemyController? enemy,
        TimeSlowManager timeSlow,
        Camera2D camera,
        string arenaMessage,
        string enemyDisplayName = CombatHud.DefaultEnemyDisplayName)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(timeSlow);
        ArgumentNullException.ThrowIfNull(camera);

        player.Visual?.BindTimeSlow(timeSlow);
        CombatFeedbackLayer.Attach(stage, player, enemy, timeSlow, camera);

        var layer = new CanvasLayer { Name = "HUDLayer", Layer = 10 };
        var hud = new CombatHud
        {
            Name = "HUD",
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Ignore, // keep LMB attack working over the HUD
            Player = player,
            Enemy = enemy,
            TimeSlow = timeSlow,
            ArenaMessage = arenaMessage,
            EnemyDisplayName = enemyDisplayName
        };
        layer.AddChild(hud);
        stage.AddChild(layer);

        PauseMenuOverlay.Attach(stage, timeSlow);
        WireFeedback(player, timeSlow, hud);
        CombatAudioController.Attach(stage, player, enemy, timeSlow);
        return hud;
    }

    public static void WireFeedback(
        PlayerController player,
        TimeSlowManager timeSlow,
        CombatHud hud)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(timeSlow);
        ArgumentNullException.ThrowIfNull(hud);

        player.CombatFeedback += hud.ReportCombat;
        player.TimeResourceRefund += timeSlow.AddResource;
        timeSlow.StateChanged += line =>
        {
            GD.Print($"[TimeSlow] {line}");
            hud.ReportTime(line);
        };
    }

    public static bool HandleResetIfRequested(Node stage, TimeSlowManager timeSlow)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(timeSlow);

        if (!ActInput.ResetPressed()) return false;

        GD.Print("[Arena] reset requested");
        timeSlow.Reset();
        stage.GetNodeOrNull<RunSession>("/root/RunSession")?.Clear();
        stage.GetTree().ReloadCurrentScene();
        return true;
    }
}
