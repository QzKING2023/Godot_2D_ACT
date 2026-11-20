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
    public static StageEnemyRoster AttachEnemyRoster(
        Node stage,
        TimeSlowManager timeSlow,
        params EnemyController?[] initialEnemies)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(timeSlow);

        var roster = new StageEnemyRoster { Name = "EnemyRoster" };
        roster.BindTimeSlow(timeSlow);
        stage.AddChild(roster);
        roster.RegisterRange(initialEnemies);
        return roster;
    }

    public static CombatHud MountStagePresentationAndHud(
        Node2D stage,
        PlayerController player,
        StageEnemyRoster roster,
        TimeSlowManager timeSlow,
        Camera2D camera,
        string arenaMessage,
        string enemyDisplayName = "")
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(timeSlow);
        ArgumentNullException.ThrowIfNull(camera);

        player.Visual?.BindTimeSlow(timeSlow);
        roster.BindTimeSlow(timeSlow);
        CombatFeedbackLayer.AttachRoster(stage, player, roster, timeSlow, camera);

        var layer = new CanvasLayer { Name = "HUDLayer", Layer = 10 };
        var hud = new CombatHud
        {
            Name = "HUD",
            Size = new Vector2(1280f, 720f),
            MouseFilter = Control.MouseFilterEnum.Ignore, // keep LMB attack working over the HUD
            Player = player,
            EnemyRoster = roster,
            TimeSlow = timeSlow,
            ArenaMessage = arenaMessage,
            EnemyDisplayName = enemyDisplayName
        };
        layer.AddChild(hud);
        stage.AddChild(layer);

        PauseMenuOverlay.Attach(stage, timeSlow);
        WireFeedback(player, timeSlow, hud);
        CombatAudioController.AttachRoster(stage, player, roster, timeSlow);
        return hud;
    }

    public static CombatHud MountStagePresentationAndHud(
        Node2D stage,
        PlayerController player,
        EnemyController? enemy,
        TimeSlowManager timeSlow,
        Camera2D camera,
        string arenaMessage,
        string enemyDisplayName = "")
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(timeSlow);

        StageEnemyRoster roster = AttachEnemyRoster(stage, timeSlow, enemy);
        return MountStagePresentationAndHud(
            stage,
            player,
            roster,
            timeSlow,
            camera,
            arenaMessage,
            enemyDisplayName);
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

    public static void ScheduleSceneChange(Node stage, string nextScene, double delaySeconds = 0.35)
    {
        ArgumentNullException.ThrowIfNull(stage);
        stage.GetTree().CreateTimer(delaySeconds).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(stage) && stage.IsInsideTree())
                stage.GetTree().ChangeSceneToFile(nextScene);
        };
    }
}
