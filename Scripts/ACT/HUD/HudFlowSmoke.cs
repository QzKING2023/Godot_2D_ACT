using System;
using System.Collections.Generic;
using ACTSpike.ACT.Arena;
using ACTSpike.ACT.Combat;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Input;
using ACTSpike.ACT.Player;
using ACTSpike.ACT.Visual;
using Godot;

namespace ACTSpike.ACT.HUD;

/// <summary>
/// Contract smoke verifier for Ticket #5: input actions, pause-safe TimeSlow,
/// TitleScreen smoke redirect, PauseMenuOverlay, CombatHud & F3 debug toggle, and VictoryScreen.
/// </summary>
public static class HudFlowSmoke
{
    public static void Check(Node coreLoop, Action<string> fail)
    {
        var failures = new List<string>();
        CheckMainSceneAndTitleEntry(coreLoop, failures);
        CheckInputActions(failures);
        CheckTimeSlowPauseSafety(coreLoop, failures);
        CheckPauseMenuAttached(coreLoop, failures);
        CheckCombatHudContract(coreLoop, failures);
        CheckVictoryScreenContract(coreLoop, failures);
        CheckStageWiringAndTutorialCopyContract(coreLoop, failures);

        if (failures.Count == 0)
        {
            GD.Print("[Smoke] PASS HUD/Title/Pause/Victory flow, pause-safe TimeSlow, and F3 debug toggle");
            return;
        }

        foreach (string reason in failures)
            fail($"hud/flow: {reason}");
    }

    private static void CheckMainSceneAndTitleEntry(Node host, List<string> failures)
    {
        string mainScene = ProjectSettings.GetSetting("application/run/main_scene").AsString();
        if (mainScene != "res://Scenes/Title.tscn")
            failures.Add($"expected main_scene 'res://Scenes/Title.tscn', got '{mainScene}'");

        if (!TitleScreen.EnteredFromTitleInSmoke)
            failures.Add("TitleScreen.EnteredFromTitleInSmoke was not recorded during ACT_SMOKE=1 startup");

        var titlePacked = GD.Load<PackedScene>("res://Scenes/Title.tscn");
        if (titlePacked is null)
        {
            failures.Add("failed to load res://Scenes/Title.tscn");
            return;
        }

        Node titleNode = titlePacked.Instantiate();
        host.AddChild(titleNode);
        try
        {
            if (titleNode is not TitleScreen title)
            {
                failures.Add("Scenes/Title.tscn root is not TitleScreen");
                return;
            }

            var env = title.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment");
            if (env is null || env.Theme != RuinStageTheme.Title)
                failures.Add("TitleScreen missing RuinEnvironmentLayer with RuinStageTheme.Title");

            var labels = title.OptionLabels;
            if (labels.Count != 3 ||
                labels[0] != "从教学开始" ||
                labels[1] != "直接挑战 Boss" ||
                labels[2] != "退出")
            {
                failures.Add($"TitleScreen options mismatch: [{string.Join(", ", labels)}]");
            }

            title.MoveSelection(1);
            if (title.SelectedIndex != 1)
                failures.Add($"TitleScreen MoveSelection(1) expected index 1, got {title.SelectedIndex}");
            title.MoveSelection(-2);
            if (title.SelectedIndex != 2)
                failures.Add($"TitleScreen MoveSelection(-2) wrap expected index 2, got {title.SelectedIndex}");
        }
        finally
        {
            host.RemoveChild(titleNode);
            titleNode.Free();
        }
    }

    private static void CheckInputActions(List<string> failures)
    {
        if (!InputMap.HasAction("pause"))
        {
            failures.Add("missing InputMap action 'pause'");
        }
        else
        {
            bool hasEsc = false;
            bool hasStart = false;
            foreach (InputEvent ev in InputMap.ActionGetEvents("pause"))
            {
                if (ev is InputEventKey k && (k.PhysicalKeycode == Key.Escape || k.Keycode == Key.Escape))
                    hasEsc = true;
                if (ev is InputEventJoypadButton b && b.ButtonIndex == JoyButton.Start)
                    hasStart = true;
            }
            if (!hasEsc || !hasStart)
                failures.Add($"'pause' binding incomplete (esc={hasEsc}, start={hasStart})");
        }

        if (!InputMap.HasAction("toggle_debug"))
        {
            failures.Add("missing InputMap action 'toggle_debug'");
        }
        else
        {
            bool hasF3 = false;
            foreach (InputEvent ev in InputMap.ActionGetEvents("toggle_debug"))
            {
                if (ev is InputEventKey k && (k.PhysicalKeycode == Key.F3 || k.Keycode == Key.F3))
                    hasF3 = true;
            }
            if (!hasF3)
                failures.Add("'toggle_debug' missing F3 key binding");
        }

        // Verify ActInput edge helpers respond to action events cleanly.
        SendAction("pause", true);
        if (!ActInput.PausePressed())
            failures.Add("ActInput.PausePressed() did not detect 'pause' action");
        SendAction("pause", false);

        SendAction("toggle_debug", true);
        if (!ActInput.ToggleDebugPressed())
            failures.Add("ActInput.ToggleDebugPressed() did not detect 'toggle_debug' action");
        SendAction("toggle_debug", false);

        var jKey = new InputEventKey { PhysicalKeycode = Key.J, Pressed = true };
        var bPad = new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true };
        var dpadDown = new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true };
        var stickMotion = new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -0.9f };
        var mouseClick = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
        if (!ActInput.IsMenuConfirmEvent(jKey) ||
            ActInput.IsMenuConfirmEvent(mouseClick) ||
            !ActInput.IsMenuBackEvent(bPad) ||
            !ActInput.IsMenuDownEvent(dpadDown) ||
            ActInput.IsMenuUpEvent(stickMotion))
        {
            failures.Add("ActInput menu navigation/confirm/back event helper mismatch");
        }
    }

    private static void SendAction(string action, bool pressed)
    {
        Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
        Godot.Input.FlushBufferedEvents();
    }

    private static void CheckTimeSlowPauseSafety(Node host, List<string> failures)
    {
        var activeTransitions = new List<bool>();
        var probe = new TimeSlowManager
        {
            Name = "TimeSlowPauseProbe",
            Config = new TimeSlowConfig()
        };
        probe.ActiveChanged += active => activeTransitions.Add(active);
        host.AddChild(probe);
        try
        {
            SendAction("time_slow", true);
            probe._Process(0.016);
            if (!probe.Active || Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                failures.Add("TimeSlow probe failed to activate before pause check");

            probe.SetPaused(true);
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                failures.Add($"Engine.TimeScale leaked during pause: {Engine.TimeScale:0.00}");
            if (probe.Active)
                failures.Add("TimeSlow stayed Active across SetPaused(true)");

            SendAction("time_slow", false);
            probe.SetPaused(false);
            probe._Process(0.016);
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                failures.Add($"Engine.TimeScale did not stay 1.0 after unpause: {Engine.TimeScale:0.00}");

            if (activeTransitions.Count != 2 || !activeTransitions[0] || activeTransitions[1])
                failures.Add($"TimeSlow ActiveChanged expected [True, False], got [{string.Join(", ", activeTransitions)}]");
        }
        finally
        {
            SendAction("time_slow", false);
            probe.Reset();
            TimeSlowManager.EnsureNormalScale();
            host.RemoveChild(probe);
            probe.Free();
        }
    }

    private static void CheckPauseMenuAttached(Node coreLoop, List<string> failures)
    {
        var pauseMenu = coreLoop.GetNodeOrNull<PauseMenuOverlay>("PauseMenuLayer");
        if (pauseMenu is null)
        {
            failures.Add("CoreLoop missing PauseMenuLayer (PauseMenuOverlay)");
            return;
        }

        if (pauseMenu.Layer != 20 || pauseMenu.ProcessMode != Node.ProcessModeEnum.Always)
            failures.Add($"PauseMenuOverlay invalid Layer/ProcessMode: {pauseMenu.Layer}/{pauseMenu.ProcessMode}");

        var labels = pauseMenu.OptionLabels;
        if (labels.Count != 3 || labels[0] != "继续" || labels[1] != "重置当前关" || labels[2] != "返回标题")
            failures.Add($"PauseMenuOverlay options mismatch: [{string.Join(", ", labels)}]");

        // Verify pause toggle with active TimeSlow does not leak Engine.TimeScale or Paused state
        var timeSlow = coreLoop.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
        if (timeSlow is not null)
        {
            SendAction("time_slow", true);
            timeSlow._Process(0.016);
        }

        pauseMenu.Open();
        bool pausedWhenOpen = pauseMenu.GetTree().Paused && pauseMenu.Visible;
        bool scaleRestoredOnOpen = Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);

        pauseMenu.Close();
        SendAction("time_slow", false);
        bool unpausedOnClose = !pauseMenu.GetTree().Paused && !pauseMenu.Visible;
        bool scaleRestoredOnClose = Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);

        if (!pausedWhenOpen || !scaleRestoredOnOpen || !unpausedOnClose || !scaleRestoredOnClose)
        {
            failures.Add($"PauseMenuOverlay open/close state mismatch: open={pausedWhenOpen} scaleOpen={scaleRestoredOnOpen} close={unpausedOnClose} scaleClose={scaleRestoredOnClose}");
        }

        // Verify all 8 Tutorial stages and Formal stages also attach PauseMenuOverlay
        Camera2D? prevCam = coreLoop.GetViewport()?.GetCamera2D();
        foreach (string path in new[]
        {
            "res://Scenes/Tutorial/Movement.tscn",
            "res://Scenes/Tutorial/Attack.tscn",
            "res://Scenes/Tutorial/Skill.tscn",
            "res://Scenes/Tutorial/Defense.tscn",
            "res://Scenes/Tutorial/Dodge.tscn",
            "res://Scenes/Tutorial/PerfectDefense.tscn",
            "res://Scenes/Tutorial/TimeSlow.tscn",
            "res://Scenes/Tutorial/Combat.tscn",
            "res://Scenes/FormalRoute.tscn",
            "res://Scenes/FormalEncounter.tscn"
        })
        {
            var packed = GD.Load<PackedScene>(path);
            if (packed is null)
            {
                failures.Add($"failed to load {path} for PauseMenuOverlay check");
                continue;
            }
            Node stage = packed.Instantiate();
            coreLoop.AddChild(stage);
            try
            {
                if (stage.GetNodeOrNull<PauseMenuOverlay>("PauseMenuLayer") is null)
                    failures.Add($"{path} missing PauseMenuOverlay");
            }
            finally
            {
                coreLoop.RemoveChild(stage);
                stage.QueueFree();
            }
        }
        prevCam?.MakeCurrent();
    }

    private static void CheckCombatHudContract(Node coreLoop, List<string> failures)
    {
        var hudLayer = coreLoop.GetNodeOrNull<CanvasLayer>("HUDLayer");
        var hud = coreLoop.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
        if (hudLayer is null || hud is null)
        {
            failures.Add("CoreLoop missing HUDLayer/HUD (CombatHud)");
            return;
        }

        var feedbackLayer = coreLoop.GetNodeOrNull<CombatFeedbackLayer>("CombatFeedbackLayer");
        var pauseLayer = coreLoop.GetNodeOrNull<PauseMenuOverlay>("PauseMenuLayer");
        if (feedbackLayer is not null && hudLayer.Layer <= feedbackLayer.TimeSlowFieldLayer.Layer)
            failures.Add($"HUDLayer.Layer ({hudLayer.Layer}) must sit above TimeSlowFieldLayer ({feedbackLayer.TimeSlowFieldLayer.Layer})");
        if (pauseLayer is not null && pauseLayer.Layer <= hudLayer.Layer)
            failures.Add($"PauseMenuLayer.Layer ({pauseLayer.Layer}) must sit above HUDLayer ({hudLayer.Layer})");

        if (hud.ArenaMessage.Contains("Reach", StringComparison.OrdinalIgnoreCase) ||
            hud.ArenaMessage.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"CombatHud ArenaMessage still uses placeholder English: '{hud.ArenaMessage}'");
        }

        if (hud.EnemyDisplayName != "遗迹守卫 · 巨构核心")
            failures.Add($"CoreLoop CombatHud.EnemyDisplayName mismatch: '{hud.EnemyDisplayName}'");

        if (hud.DebugOverlayVisible)
            failures.Add("CombatHud.DebugOverlayVisible must default to false");

        // Verify F3 toggle_debug flips DebugOverlayVisible cleanly and leaves it false
        SendAction("toggle_debug", true);
        hud._Process(0.016);
        SendAction("toggle_debug", false);
        bool toggledOn = hud.DebugOverlayVisible;

        SendAction("toggle_debug", true);
        hud._Process(0.016);
        SendAction("toggle_debug", false);
        bool toggledOff = !hud.DebugOverlayVisible;

        if (!toggledOn || !toggledOff)
            failures.Add($"CombatHud F3 toggle failed: on={toggledOn}, off={toggledOff}");

        // Verify DefenseResolved banner cues and null-Enemy safety on an isolated probe
        var probeHost = new Node2D { Name = "CombatHudProbeHost" };
        coreLoop.AddChild(probeHost);
        var floorBody = new StaticBody2D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector2(260f, 580f) };
        floorBody.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(400f, 40f) } });
        probeHost.AddChild(floorBody);

        var probeWindows = new CombatWindowConfig
        {
            PerfectGuardSeconds = 0.5f,
            PerfectDodgeSeconds = 0.5f,
            HitstunSeconds = 0f,
            GuardDamagePerBlockedHit = 120f
        };
        var probeTime = new TimeSlowManager { Name = "ProbeTimeSlow", Config = new TimeSlowConfig() };
        var probePlayer = new PlayerController
        {
            Name = "ProbePlayer",
            Position = new Vector2(260f, 537f),
            Windows = probeWindows,
            TimeSlow = probeTime.Config
        };
        var probeHud = new CombatHud { Name = "ProbeHud", Player = probePlayer, Enemy = null, TimeSlow = probeTime };
        probeHost.AddChild(probeTime);
        probeHost.AddChild(probePlayer);
        probeHost.AddChild(probeHud);

        try
        {
            // Land on probe floor so IsOnFloor() is true for block
            probePlayer._PhysicsProcess(0.05);

            // Ensure ReportCombat updates LastCombatLine for F3 without string-matching event banners, and ReportTime updates LastTimeLine.
            probeHud.ReportCombat("PERFECT BLOCK (string ignored for banner)");
            if (probeHud.LastCombatLine != "PERFECT BLOCK (string ignored for banner)" ||
                !string.IsNullOrEmpty(probeHud.CurrentEventBannerText))
            {
                failures.Add($"CombatHud.ReportCombat banner decoupling mismatch: line='{probeHud.LastCombatLine}' banner='{probeHud.CurrentEventBannerText}'");
            }
            probeHud.ReportTime("time slow probe line");
            if (probeHud.LastTimeLine != "time slow probe line")
                failures.Add($"CombatHud.LastTimeLine mismatch: '{probeHud.LastTimeLine}'");

            // 1. Perfect Block -> "完美格挡 · 震慑破势"
            SendAction("block", true);
            probePlayer._Process(0.016);
            probePlayer._PhysicsProcess(0.016);
            probePlayer.TryReceiveEnemyAttack(15f, BossAttackKind.Normal, out _);
            SendAction("block", false);
            probePlayer._PhysicsProcess(0.016);
            if (probeHud.CurrentEventBannerText != "完美格挡 · 震慑破势")
                failures.Add($"CombatHud banner mismatch on PerfectBlocked: '{probeHud.CurrentEventBannerText}'");

            // 2. Guard Break (after PerfectGuard window expires) -> "破防硬直"
            probeWindows.PerfectGuardSeconds = -0.1f;
            SendAction("block", true);
            probePlayer._Process(0.016);
            probePlayer._PhysicsProcess(0.016);
            probePlayer.TryReceiveEnemyAttack(15f, BossAttackKind.Normal, out _);
            SendAction("block", false);
            probePlayer._PhysicsProcess(0.016);
            if (probeHud.CurrentEventBannerText != "破防硬直")
                failures.Add($"CombatHud banner mismatch on GuardBroken: '{probeHud.CurrentEventBannerText}'");

            // 3. BreakGrab Hit -> "护盾击破"
            probePlayer.ResetTo(new Vector2(260f, 537f));
            probePlayer._PhysicsProcess(0.05);
            probePlayer.TryReceiveEnemyAttack(30f, BossAttackKind.BreakGrab, out _);
            if (probeHud.CurrentEventBannerText != "护盾击破")
                failures.Add($"CombatHud banner mismatch on BreakGrab Hit: '{probeHud.CurrentEventBannerText}'");

            // 4. Perfect Dodge -> "完美闪避 · 时缓充能"
            probePlayer.ResetTo(new Vector2(260f, 537f));
            probePlayer._PhysicsProcess(0.05);
            SendAction("dodge", true);
            probePlayer._Process(0.016);
            probePlayer._PhysicsProcess(0.016);
            probePlayer.TryReceiveEnemyAttack(30f, BossAttackKind.BreakGrab, out _);
            SendAction("dodge", false);
            if (probeHud.CurrentEventBannerText != "完美闪避 · 时缓充能")
                failures.Add($"CombatHud banner mismatch on PerfectDodged: '{probeHud.CurrentEventBannerText}'");

            // Ensure QueueRedraw executes cleanly with Enemy == null & DebugOverlayVisible toggles
            probeHud.DebugOverlayVisible = false;
            probeHud.QueueRedraw();
            probeHud.DebugOverlayVisible = true;
            probeHud.QueueRedraw();
        }
        finally
        {
            SendAction("block", false);
            SendAction("dodge", false);
            coreLoop.RemoveChild(probeHost);
            probeHost.Free();
        }
    }

    private static void CheckVictoryScreenContract(Node coreLoop, List<string> failures)
    {
        var packed = GD.Load<PackedScene>("res://Scenes/Victory.tscn");
        if (packed is null)
        {
            failures.Add("failed to load res://Scenes/Victory.tscn");
            return;
        }

        Node node = packed.Instantiate();
        coreLoop.AddChild(node);
        try
        {
            if (node is not VictoryScreen victory)
            {
                failures.Add("Scenes/Victory.tscn root is not VictoryScreen");
                return;
            }
            var env = victory.GetNodeOrNull<RuinEnvironmentLayer>("RuinEnvironment");
            if (env is null || env.Theme != RuinStageTheme.Victory)
                failures.Add("VictoryScreen missing RuinEnvironmentLayer with RuinStageTheme.Victory");

            var labels = victory.OptionLabels;
            if (labels.Count != 2 || labels[0] != "再战 Boss" || labels[1] != "返回标题")
                failures.Add($"VictoryScreen options mismatch: [{string.Join(", ", labels)}]");

            victory.MoveSelection(1);
            if (victory.SelectedIndex != 1)
                failures.Add($"VictoryScreen MoveSelection(1) expected 1, got {victory.SelectedIndex}");
        }
        finally
        {
            coreLoop.RemoveChild(node);
            node.Free();
        }
    }

    private static void CheckStageWiringAndTutorialCopyContract(Node coreLoop, List<string> failures)
    {
        Camera2D? prevCam = coreLoop.GetViewport()?.GetCamera2D();

        // 1. PerfectDefense tutorial copy matches Variant A telegraphs (no stale "红色闪光" text).
        var perfectDefPacked = GD.Load<PackedScene>("res://Scenes/Tutorial/PerfectDefense.tscn");
        if (perfectDefPacked is null)
        {
            failures.Add("failed to load res://Scenes/Tutorial/PerfectDefense.tscn");
        }
        else
        {
            Node stage = perfectDefPacked.Instantiate();
            coreLoop.AddChild(stage);
            try
            {
                var hud = stage.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
                const string expectedCopy = "捕捉前摇信号完成完美格挡与完美闪避（金色可格挡，猩红须闪避）";
                if (hud is null || hud.ArenaMessage != expectedCopy)
                    failures.Add($"PerfectDefense tutorial copy mismatch: '{hud?.ArenaMessage ?? "null"}'");
            }
            finally
            {
                coreLoop.RemoveChild(stage);
                stage.Free();
            }
        }

        // 2. TutorialStage and FormalStage wire F3 logs (CombatFeedback/StateChanged), TimeResourceRefund, and tiered HUD (no boss bar on non-boss stages).
        foreach (string path in new[]
        {
            "res://Scenes/Tutorial/Combat.tscn",
            "res://Scenes/FormalEncounter.tscn"
        })
        {
            var packed = GD.Load<PackedScene>(path);
            if (packed is null)
            {
                failures.Add($"failed to load {path} for stage wiring check");
                continue;
            }

            Node stage = packed.Instantiate();
            coreLoop.AddChild(stage);
            try
            {
                var player = stage.GetNodeOrNull<PlayerController>("Player");
                var timeSlow = stage.GetNodeOrNull<TimeSlowManager>("TimeSlowManager");
                var hud = stage.GetNodeOrNull<CombatHud>("HUDLayer/HUD");
                if (player is null || timeSlow is null || hud is null)
                {
                    failures.Add($"{path} missing Player/TimeSlowManager/HUD for stage wiring check");
                    continue;
                }

                if (hud.IsBossHealthBarVisible)
                    failures.Add($"{path} non-boss stage unexpectedly displayed bottom boss health bar");

                timeSlow.Reset();
                if (hud.LastTimeLine != "time resource reset")
                    failures.Add($"{path} TimeSlow.StateChanged did not update HUD.LastTimeLine ('{hud.LastTimeLine}')");

                timeSlow.AddResource(-30f);
                float resourceBefore = timeSlow.Resource;
                player.Windows.PerfectDodgeSeconds = 0.5f;
                SendAction("dodge", true);
                player._Process(0.016);
                player._PhysicsProcess(0.016);
                player.TryReceiveEnemyAttack(30f, BossAttackKind.BreakGrab, out AttackResolution resolution);
                SendAction("dodge", false);

                if (resolution.Result != DefenseResult.PerfectDodged ||
                    timeSlow.Resource <= resourceBefore ||
                    !hud.LastCombatLine.Contains("PERFECT DODGE", StringComparison.Ordinal))
                {
                    failures.Add($"{path} stage wiring mismatch: result={resolution.Result} resource={resourceBefore:0}->{timeSlow.Resource:0} combat='{hud.LastCombatLine}'");
                }
            }
            finally
            {
                SendAction("dodge", false);
                coreLoop.RemoveChild(stage);
                stage.Free();
            }
        }

        prevCam?.MakeCurrent();
    }
}
