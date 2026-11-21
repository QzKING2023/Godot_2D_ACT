using System;
using System.Collections.Generic;
using ACTSpike.ACT.Arena;
using ACTSpike.ACT.Config;
using ACTSpike.ACT.Enemy;
using ACTSpike.ACT.Player;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Contract smoke verifier for Ticket #4「遗迹几何场景」stage theme progression,
/// CoreLoop mounting without legacy marker labels, and Tutorial/Formal stage parity.
/// </summary>
public static class RuinStageSmoke
{
    private static readonly string[] LegacyMarkerTexts = { "START", "BOSS BOUNDARY", "EXIT" };

    private static readonly Rect2[] ExpectedCoreLoopRects =
    {
        new(0f, 560f, 2400f, 160f),
        new(0f, 0f, 40f, 720f),
        new(2360f, 0f, 40f, 720f),
        new(500f, 500f, 180f, 60f),
        new(1380f, 520f, 220f, 40f)
    };

    private static readonly Rect2[] Expected1280RoomRects =
    {
        new(0f, 560f, 1280f, 160f),
        new(0f, 0f, 40f, 720f),
        new(1240f, 0f, 40f, 720f)
    };

    private static readonly Rect2[] ExpectedFormalEncounterRects =
    {
        new(0f, 560f, 1280f, 160f),
        new(0f, 0f, 40f, 720f),
        new(1240f, 0f, 40f, 720f),
        new(160f, 464f, 220f, 28f),
        new(900f, 464f, 220f, 28f)
    };

    private static readonly string[] TutorialScenePaths =
    {
        "res://Scenes/Tutorial/Movement.tscn",
        "res://Scenes/Tutorial/Attack.tscn",
        "res://Scenes/Tutorial/Skill.tscn",
        "res://Scenes/Tutorial/Defense.tscn",
        "res://Scenes/Tutorial/Dodge.tscn",
        "res://Scenes/Tutorial/PerfectDefense.tscn",
        "res://Scenes/Tutorial/TimeSlow.tscn",
        "res://Scenes/Tutorial/Combat.tscn"
    };

    [SmokeProbe(20)]
    public static void Check(Node coreLoop, Action<string> fail)
    {
        CheckThemeProgression(fail);
        CheckCoreLoopEnvironment(coreLoop, fail);
        CheckTutorialAndFormalStages(coreLoop, fail);
        CheckFallbackCameraWithoutCamera2D(fail);
    }

    private static void CheckThemeProgression(Action<string> fail)
    {
        RuinStageTheme[] progression =
        {
            RuinStageTheme.Tutorial,
            RuinStageTheme.FormalRoute,
            RuinStageTheme.FormalEncounter,
            RuinStageTheme.BossArena
        };
        RuinStageTheme[] allPresets =
        {
            RuinStageTheme.Tutorial,
            RuinStageTheme.FormalRoute,
            RuinStageTheme.FormalEncounter,
            RuinStageTheme.BossArena,
            RuinStageTheme.Title,
            RuinStageTheme.Victory
        };

        var distinctNames = new HashSet<string>();
        var distinctSkyTops = new HashSet<Color>();
        var distinctSkyHorizons = new HashSet<Color>();

        foreach (var preset in allPresets)
            distinctNames.Add(preset.Name);

        foreach (var stage in progression)
        {
            distinctSkyTops.Add(stage.SkyTop);
            distinctSkyHorizons.Add(stage.SkyHorizon);
        }

        bool monotonicTower = true;
        bool monotonicPillarCount = true;
        bool monotonicPillarSpacing = true;
        bool monotonicSunAltitude = true;

        for (int i = 1; i < progression.Length; i++)
        {
            var prev = progression[i - 1];
            var curr = progression[i];
            if (curr.TowerScale <= prev.TowerScale) monotonicTower = false;
            if (curr.PillarCount <= prev.PillarCount) monotonicPillarCount = false;
            if (curr.PillarSpacing >= prev.PillarSpacing) monotonicPillarSpacing = false;
            if (curr.SunAltitudeY <= prev.SunAltitudeY) monotonicSunAltitude = false;
        }

        bool bossBaseline =
            RuinStageTheme.BossArena.SkyTop == new Color("2b3548") &&
            RuinStageTheme.BossArena.SkyHorizon == new Color("c98b55");

        if (distinctNames.Count == allPresets.Length &&
            distinctSkyTops.Count == progression.Length &&
            distinctSkyHorizons.Count == progression.Length &&
            monotonicTower &&
            monotonicPillarCount &&
            monotonicPillarSpacing &&
            monotonicSunAltitude &&
            bossBaseline)
        {
            GD.Print("[Smoke] PASS ruin stage theme progression (Tutorial -> FormalRoute -> FormalEncounter -> BossArena)");
        }
        else
        {
            fail($"ruin stage theme progression mismatch: names={distinctNames.Count}/{allPresets.Length}, " +
                 $"skyTops={distinctSkyTops.Count}, skyHorizons={distinctSkyHorizons.Count}, " +
                 $"tower={monotonicTower}, pillars={monotonicPillarCount}, spacing={monotonicPillarSpacing}, " +
                 $"sun={monotonicSunAltitude}, bossBase={bossBaseline}");
        }
    }

    private static void CheckCoreLoopEnvironment(Node coreLoop, Action<string> fail)
    {
        var env = FindEnvironmentLayer(coreLoop);
        if (env is null)
        {
            fail("CoreLoop missing RuinEnvironmentLayer");
            return;
        }

        var legacyLabels = FindLegacyMarkerLabels(coreLoop);
        if (legacyLabels.Count > 0)
        {
            fail($"CoreLoop still contains legacy marker label(s): {string.Join(", ", legacyLabels)}");
            return;
        }

        bool layersOk = env.Theme == RuinStageTheme.BossArena &&
                        env.BackdropLayer.Layer == -10 &&
                        env.WorldDrawer.ZIndex == -2;
        bool surfacesOk = HasMatchingSurfacesAndColliders(coreLoop, env, ExpectedCoreLoopRects);
        bool gatesOk = env.GetGateState("BossBoundary") == RuinGateState.Active &&
                       env.GetGateState("VictoryExit") == RuinGateState.Locked;

        if (layersOk && surfacesOk && gatesOk)
            GD.Print("[Smoke] PASS CoreLoop RuinEnvironmentLayer (BossArena, zero legacy labels, 5 colliders, 2 gates)");
        else
            fail($"CoreLoop environment mismatch: layersOk={layersOk} surfacesOk={surfacesOk} gatesOk={gatesOk}");
    }

    private static void CheckTutorialAndFormalStages(Node host, Action<string> fail)
    {
        Camera2D? previousCamera = host.GetViewport()?.GetCamera2D();
        int verifiedTutorials = 0;

        foreach (string path in TutorialScenePaths)
        {
            if (!CheckMountedStage(host, path, RuinStageTheme.Tutorial, fail, stage =>
            {
                var env = FindEnvironmentLayer(stage)!;
                if (!HasMatchingSurfacesAndColliders(stage, env, Expected1280RoomRects))
                {
                    fail($"Tutorial stage {path} missing expected floor/wall faceted colliders");
                    return false;
                }
                return true;
            }))
            {
                previousCamera?.MakeCurrent();
                return;
            }
            verifiedTutorials++;
        }

        bool formalRouteOk = CheckMountedStage(host, "res://Scenes/FormalRoute.tscn", RuinStageTheme.FormalRoute, fail, stage =>
        {
            var env = FindEnvironmentLayer(stage)!;
            bool collidersOk = HasMatchingSurfacesAndColliders(stage, env, Expected1280RoomRects);
            bool gateOk = FindExitGateAtX(env, 1050f)?.State == RuinGateState.Unlocked;
            if (!collidersOk || !gateOk)
            {
                fail($"FormalRoute mismatch: collidersOk={collidersOk} gateOk={gateOk}");
                return false;
            }
            return true;
        });

        bool formalEncounterOk = CheckMountedStage(host, "res://Scenes/FormalEncounter.tscn", RuinStageTheme.FormalEncounter, fail, stage =>
        {
            var env = FindEnvironmentLayer(stage)!;
            var formal = (FormalStage)stage;
            var encounter = formal.GetNodeOrNull<LockGateEncounterController>("LockGateEncounter");
            var roster = formal.GetNodeOrNull<StageEnemyRoster>("EnemyRoster");
            PlayerController? player = FindChildOfType<PlayerController>(stage);

            bool collidersOk = HasMatchingSurfacesAndColliders(stage, env, ExpectedFormalEncounterRects);
            bool startsLocked = FindExitGateAtX(env, formal.ActiveExitGateX)?.State == RuinGateState.Locked;

            bool staysLockedAfterWave1 = false;
            bool unlocksAfterAllWaves = false;
            if (encounter is not null && roster is not null && player is not null)
            {
                encounter.InterWaveDelaySeconds = 0f;
                DefeatCurrentRosterEnemies(roster, player);
                formal._Process(0.016);
                staysLockedAfterWave1 =
                    encounter.CurrentWaveNumber == 2 &&
                    FindExitGateAtX(env, formal.ActiveExitGateX)?.State == RuinGateState.Locked;

                DefeatCurrentRosterEnemies(roster, player);
                formal._Process(0.016);
                DefeatCurrentRosterEnemies(roster, player);
                formal._Process(0.016);
                unlocksAfterAllWaves =
                    encounter.IsEncounterCleared &&
                    FindExitGateAtX(env, formal.ActiveExitGateX)?.State == RuinGateState.Unlocked;
            }

            if (!collidersOk || !startsLocked || !staysLockedAfterWave1 || !unlocksAfterAllWaves)
            {
                fail($"FormalEncounter mismatch: collidersOk={collidersOk} startsLocked={startsLocked} staysLockedW1={staysLockedAfterWave1} unlocksAfterAllWaves={unlocksAfterAllWaves}");
                return false;
            }
            return true;
        });

        previousCamera?.MakeCurrent();

        if (verifiedTutorials == TutorialScenePaths.Length && formalRouteOk && formalEncounterOk)
        {
            GD.Print($"[Smoke] PASS {verifiedTutorials} tutorial stages + FormalRoute + FormalEncounter RuinEnvironmentLayer");
        }
    }

    private static void CheckFallbackCameraWithoutCamera2D(Action<string> fail)
    {
        var standalone = new RuinEnvironmentLayer
        {
            Theme = RuinStageTheme.Title,
            FallbackCameraCenter = new Vector2(720f, 360f)
        };
        if (standalone.ResolveCameraCenter() == new Vector2(720f, 360f) &&
            RuinStageTheme.Title.Name == "Title" &&
            RuinStageTheme.Victory.Name == "Victory")
        {
            GD.Print("[Smoke] PASS RuinEnvironmentLayer fallback camera center & Title/Victory presets");
        }
        else
        {
            fail("RuinEnvironmentLayer fallback camera center mismatch");
        }
        standalone.Free();
    }

    private static bool CheckMountedStage(
        Node host,
        string scenePath,
        RuinStageTheme expectedTheme,
        Action<string> fail,
        Func<Node, bool> validate)
    {
        var packed = GD.Load<PackedScene>(scenePath);
        if (packed is null)
        {
            fail($"failed to load scene {scenePath}");
            return false;
        }

        Node stage = packed.Instantiate();
        host.AddChild(stage);
        try
        {
            RuinEnvironmentLayer? env = FindEnvironmentLayer(stage);
            if (env is null)
            {
                fail($"{scenePath} missing RuinEnvironmentLayer");
                return false;
            }
            if (env.Theme != expectedTheme)
            {
                fail($"{scenePath} theme expected {expectedTheme.Name}, got {env.Theme.Name}");
                return false;
            }
            if (env.BackdropLayer.Layer != -10 || env.WorldDrawer.ZIndex != -2)
            {
                fail($"{scenePath} invalid layer/zindex ({env.BackdropLayer.Layer}/{env.WorldDrawer.ZIndex})");
                return false;
            }
            return validate(stage);
        }
        finally
        {
            host.RemoveChild(stage);
            stage.QueueFree();
        }
    }

    private static RuinGateEntry? FindExitGateAtX(RuinEnvironmentLayer env, float expectedX)
    {
        foreach (var gate in env.Gates)
        {
            if (Mathf.IsEqualApprox(gate.BasePosition.X, expectedX))
                return gate;
        }
        return null;
    }

    private static void DefeatCurrentRosterEnemies(StageEnemyRoster roster, PlayerController player)
    {
        var snapshot = new List<EnemyController>(roster.Enemies);
        var zeroSpawn = new CombatWindowConfig { BossSpawnSeconds = 0f };
        foreach (EnemyController enemy in snapshot)
        {
            if (!GodotObject.IsInstanceValid(enemy) || enemy.IsDefeated)
                continue;
            enemy.Bind(player, zeroSpawn);
            enemy._PhysicsProcess(0.016);
            Vector2 backOrigin = enemy.GlobalPosition - new Vector2(enemy.Facing * 48f, 0f);
            enemy.TakeHit(new ACTSpike.ACT.Combat.EnemyHitContext(enemy.MaxHp + 5f, backOrigin, new Vector2(enemy.Facing, 0f)));
        }
    }

    private static T? FindChildOfType<T>(Node root) where T : class
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
                return match;
        }
        return null;
    }

    internal static RuinEnvironmentLayer? FindEnvironmentLayer(Node root) => FindChildOfType<RuinEnvironmentLayer>(root);

    internal static bool HasMatchingSurfacesAndColliders(Node root, RuinEnvironmentLayer env, Rect2[] expectedRects)
    {
        if (env.Surfaces.Count != expectedRects.Length)
            return false;

        var colliderRects = CollectWorldColliderRects(root);
        foreach (Rect2 expected in expectedRects)
        {
            bool hasSurface = false;
            foreach (var surface in env.Surfaces)
            {
                if (surface.Rect.IsEqualApprox(expected))
                {
                    hasSurface = true;
                    break;
                }
            }
            if (!hasSurface) return false;

            bool hasCollider = false;
            foreach (Rect2 collider in colliderRects)
            {
                if (collider.IsEqualApprox(expected))
                {
                    hasCollider = true;
                    break;
                }
            }
            if (!hasCollider) return false;
        }
        return true;
    }

    private static List<Rect2> CollectWorldColliderRects(Node root)
    {
        var rects = new List<Rect2>();
        foreach (Node child in root.GetChildren())
        {
            if (child is not StaticBody2D body || body.CollisionLayer != 2) continue;
            foreach (Node sub in body.GetChildren())
            {
                if (sub is CollisionShape2D { Shape: RectangleShape2D rectShape } shape)
                {
                    Vector2 center = body.Position + shape.Position;
                    rects.Add(new Rect2(center - rectShape.Size * 0.5f, rectShape.Size));
                }
            }
        }
        return rects;
    }

    private static List<string> FindLegacyMarkerLabels(Node root)
    {
        var found = new List<string>();
        ScanLabels(root, found);
        return found;
    }

    private static void ScanLabels(Node node, List<string> found)
    {
        if (node is Label label)
        {
            string trimmed = label.Text.Trim();
            foreach (string legacy in LegacyMarkerTexts)
            {
                if (string.Equals(trimmed, legacy, StringComparison.OrdinalIgnoreCase))
                    found.Add(trimmed);
            }
        }
        foreach (Node child in node.GetChildren())
            ScanLabels(child, found);
    }
}
