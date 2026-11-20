using System.Collections.Generic;
using Godot;

namespace ACTSpike.ACT.Visual;

public enum RuinGateState
{
    Locked,
    Active,
    Unlocked
}

public readonly record struct RuinSurfaceEntry(Rect2 Rect, bool DrawLitTopEdge);

public readonly record struct RuinGateEntry(string Id, Vector2 BasePosition, RuinGateState State);

/// <summary>
/// Shared procedural「遗迹几何场景」backdrop + foreground platform/gate renderer.
/// Mounts a far parallax CanvasLayer (Layer = -10) and a world Node2D drawer (ZIndex = -2)
/// behind actors and telegraphs, and provides AddFacetedStaticBody for 1:1 collider parity.
/// </summary>
public partial class RuinEnvironmentLayer : Node2D
{
    private readonly List<RuinSurfaceEntry> _surfaces = new();
    private readonly List<RuinGateEntry> _gates = new();
    private readonly Dictionary<string, int> _gateIndexById = new();

    private Control? _backdropCanvas;
    private bool _worldDirty = true;
    private bool _backdropDirty = true;
    private Vector2 _lastDrawnCameraCenter = new(float.NaN, float.NaN);
    private RuinStageTheme? _lastDrawnTheme;

    public RuinStageTheme Theme { get; set; } = RuinStageTheme.BossArena;
    public Camera2D? Camera { get; set; }
    [Export] public Vector2 FallbackCameraCenter { get; set; } = new(640f, 360f);

    public CanvasLayer BackdropLayer { get; private set; } = null!;
    public Node2D WorldDrawer { get; private set; } = null!;
    public IReadOnlyList<RuinSurfaceEntry> Surfaces => _surfaces;
    public IReadOnlyList<RuinGateEntry> Gates => _gates;

    public static RuinEnvironmentLayer CreateMenuBackdrop(Node parent, RuinStageTheme theme)
    {
        var environment = new RuinEnvironmentLayer
        {
            Name = "RuinEnvironment",
            Theme = theme,
            FallbackCameraCenter = new Vector2(640f, 360f)
        };
        parent.AddChild(environment);
        environment.RegisterSurface(new Rect2(0f, 560f, 1280f, 160f));
        return environment;
    }

    public override void _Ready()
    {
        if (BackdropLayer is not null) return;

        BackdropLayer = new CanvasLayer
        {
            Name = "BackdropLayer",
            Layer = -10
        };
        _backdropCanvas = new Control
        {
            Name = "BackdropCanvas",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _backdropCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdropCanvas.Draw += OnDrawBackdrop;
        BackdropLayer.AddChild(_backdropCanvas);
        AddChild(BackdropLayer);

        WorldDrawer = new Node2D
        {
            Name = "WorldDrawer",
            ZIndex = -2
        };
        WorldDrawer.Draw += OnDrawWorld;
        AddChild(WorldDrawer);

        RequestRedraw();
    }

    public override void _Process(double delta)
    {
        Vector2 camCenter = ResolveCameraCenter();
        if (_backdropDirty || !camCenter.IsEqualApprox(_lastDrawnCameraCenter) || !Equals(_lastDrawnTheme, Theme))
        {
            _backdropDirty = false;
            _lastDrawnCameraCenter = camCenter;
            _lastDrawnTheme = Theme;
            _backdropCanvas?.QueueRedraw();
        }
        if (_worldDirty)
        {
            _worldDirty = false;
            WorldDrawer?.QueueRedraw();
        }
    }

    /// <summary>
    /// Creates a world StaticBody2D (CollisionLayer = 2, CollisionMask = 0) and registers the
    /// exact same Rect2 for procedural faceted platform rendering.
    /// </summary>
    public StaticBody2D AddFacetedStaticBody(Node2D parent, Rect2 rect, bool drawLitTopEdge = true)
    {
        var body = new StaticBody2D
        {
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = rect.GetCenter()
        };
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = rect.Size }
        });
        parent.AddChild(body);
        RegisterSurface(rect, drawLitTopEdge);
        return body;
    }

    public void RegisterSurface(Rect2 rect, bool drawLitTopEdge = true)
    {
        _surfaces.Add(new RuinSurfaceEntry(rect, drawLitTopEdge));
        RequestRedraw();
    }

    public void AddGate(string id, Vector2 basePosition, RuinGateState initialState = RuinGateState.Locked)
    {
        if (_gateIndexById.TryGetValue(id, out int existingIndex))
        {
            _gates[existingIndex] = new RuinGateEntry(id, basePosition, initialState);
        }
        else
        {
            _gateIndexById[id] = _gates.Count;
            _gates.Add(new RuinGateEntry(id, basePosition, initialState));
        }
        RequestRedraw();
    }

    public void SetGateState(string id, RuinGateState state)
    {
        if (!_gateIndexById.TryGetValue(id, out int index)) return;
        RuinGateEntry current = _gates[index];
        if (current.State == state) return;
        _gates[index] = current with { State = state };
        RequestRedraw();
    }

    public RuinGateState? GetGateState(string id)
        => _gateIndexById.TryGetValue(id, out int index) ? _gates[index].State : null;

    public Vector2 ResolveCameraCenter()
    {
        Camera2D? cam = Camera;
        if (cam is not null && cam.IsInsideTree())
            return cam.GetScreenCenterPosition();
        Camera2D? viewportCam = IsInsideTree() ? GetViewport()?.GetCamera2D() : null;
        if (viewportCam is not null && viewportCam.IsInsideTree())
            return viewportCam.GetScreenCenterPosition();
        return FallbackCameraCenter;
    }

    private void RequestRedraw()
    {
        _worldDirty = true;
        _backdropDirty = true;
        _backdropCanvas?.QueueRedraw();
        WorldDrawer?.QueueRedraw();
    }

    private void OnDrawBackdrop()
    {
        if (_backdropCanvas is null) return;

        Vector2 vp = _backdropCanvas.GetViewportRect().Size;
        if (vp.X <= 0f || vp.Y <= 0f)
            vp = new Vector2(1280f, 720f);

        float camX = ResolveCameraCenter().X;

        // 1. 「荒原暖侧光」vertical sky gradient & upper-right sun glow
        GeometricDraw.VerticalGradient(_backdropCanvas, new Rect2(Vector2.Zero, vp), Theme.SkyTop, Theme.SkyHorizon, bands: 18);
        var sunPos = new Vector2(vp.X * 0.78f - camX * 0.03f, Theme.SunAltitudeY);
        _backdropCanvas.DrawCircle(sunPos, 150f * Theme.SunScale, Theme.SunOuterGlow);
        _backdropCanvas.DrawCircle(sunPos, 86f * Theme.SunScale, Theme.SunCoreGlow);

        // 2. Far parallax「中央遗迹高塔」(broken cantilever ring racks, NO glowing antenna)
        float tx = Theme.TowerBaseX - camX * Theme.TowerParallax;
        float s = Theme.TowerScale;
        const float towerBaseY = 560f;
        GeometricDraw.FillPolygon(_backdropCanvas, new[]
        {
            new Vector2(tx - 38f * s, towerBaseY),
            new Vector2(tx - 24f * s, towerBaseY - 468f * s),
            new Vector2(tx + 4f * s, towerBaseY - 480f * s),
            new Vector2(tx + 6f * s, towerBaseY)
        }, RuinGeometryPalette.TowerShadow);
        GeometricDraw.FillPolygon(_backdropCanvas, new[]
        {
            new Vector2(tx + 4f * s, towerBaseY - 480f * s),
            new Vector2(tx + 34f * s, towerBaseY - 462f * s),
            new Vector2(tx + 44f * s, towerBaseY),
            new Vector2(tx + 6f * s, towerBaseY)
        }, RuinGeometryPalette.TowerLit);

        float[] rackOffsets = { 395f, 305f, 210f };
        foreach (float offset in rackOffsets)
        {
            float ry = towerBaseY - offset * s;
            _backdropCanvas.DrawRect(new Rect2(tx - 92f * s, ry, 96f * s, 12f * s), RuinGeometryPalette.TowerRackShadow);
            _backdropCanvas.DrawRect(new Rect2(tx + 4f * s, ry, 108f * s, 12f * s), RuinGeometryPalette.TowerRackLit);
            _backdropCanvas.DrawRect(new Rect2(tx + 78f * s, ry - 18f * s, 14f * s, 30f * s), RuinGeometryPalette.TowerRackCap);
        }

        // 3. Far「干旱草原荒原」rolling arid hills
        float h1 = -camX * Theme.HillsParallax;
        float rightEdge = Mathf.Max(vp.X + 200f, 2400f + h1);
        GeometricDraw.FillPolygon(_backdropCanvas, new[]
        {
            new Vector2(-100f, vp.Y + 40f),
            new Vector2(-100f, 470f),
            new Vector2(260f + h1, 415f),
            new Vector2(580f + h1, 455f),
            new Vector2(960f + h1, 390f),
            new Vector2(1420f + h1, 445f),
            new Vector2(1800f + h1, 400f),
            new Vector2(2260f + h1, 440f),
            new Vector2(rightEdge, 475f),
            new Vector2(rightEdge, vp.Y + 40f)
        }, Theme.HillsColor);

        // 4. Mid-ground「古代金属巨构」pillars + lintels
        float mOff = -camX * Theme.PillarParallax;
        float ps = Theme.PillarScale;
        const float pillarBaseY = 620f;
        for (int i = 0; i < Theme.PillarCount; i++)
        {
            float x = Theme.PillarStartX + i * Theme.PillarSpacing + mOff;
            GeometricDraw.FillPolygon(_backdropCanvas, new[]
            {
                new Vector2(x - 28f * ps, pillarBaseY),
                new Vector2(x - 22f * ps, pillarBaseY - 390f * ps),
                new Vector2(x + 6f * ps, pillarBaseY - 405f * ps),
                new Vector2(x + 6f * ps, pillarBaseY)
            }, RuinGeometryPalette.PillarShadow);
            GeometricDraw.FillPolygon(_backdropCanvas, new[]
            {
                new Vector2(x + 6f * ps, pillarBaseY - 405f * ps),
                new Vector2(x + 28f * ps, pillarBaseY - 390f * ps),
                new Vector2(x + 34f * ps, pillarBaseY),
                new Vector2(x + 6f * ps, pillarBaseY)
            }, RuinGeometryPalette.PillarLit);

            float lintelY = pillarBaseY - 330f * ps;
            _backdropCanvas.DrawRect(new Rect2(x - 42f * ps, lintelY, 90f * ps, 14f * ps), RuinGeometryPalette.PillarLintelShadow);
            _backdropCanvas.DrawRect(new Rect2(x + 6f * ps, lintelY, 42f * ps, 6f * ps), RuinGeometryPalette.PillarLintelLit);
        }
    }

    private void OnDrawWorld()
    {
        if (WorldDrawer is null) return;

        foreach (RuinSurfaceEntry surface in _surfaces)
            GeometricDraw.FacetedPlatform(WorldDrawer, surface.Rect, surface.DrawLitTopEdge);

        foreach (RuinGateEntry gate in _gates)
            DrawGate(WorldDrawer, gate);
    }

    private static void DrawGate(CanvasItem canvas, RuinGateEntry gate)
    {
        float x = gate.BasePosition.X;
        float y = gate.BasePosition.Y;
        Color accent = GateAccentColor(gate.State);

        GeometricDraw.FillPolygon(canvas, new[]
        {
            new Vector2(x - 10f, y),
            new Vector2(x - 6f, y - 135f),
            new Vector2(x + 2f, y - 148f),
            new Vector2(x + 2f, y)
        }, RuinGeometryPalette.GateStoneShadow);
        GeometricDraw.FillPolygon(canvas, new[]
        {
            new Vector2(x + 2f, y - 148f),
            new Vector2(x + 12f, y - 132f),
            new Vector2(x + 14f, y),
            new Vector2(x + 2f, y)
        }, RuinGeometryPalette.GateStoneLit);

        GeometricDraw.Diamond(canvas, new Vector2(x, y - 164f), 10f, 14f, accent);
        float beamAlpha = gate.State == RuinGateState.Locked ? 0.14f : 0.28f;
        canvas.DrawLine(new Vector2(x, y - 148f), new Vector2(x, y), accent with { A = beamAlpha }, 3f);
    }

    private static Color GateAccentColor(RuinGateState state) => state switch
    {
        RuinGateState.Active => RuinGeometryPalette.GateActive,
        RuinGateState.Unlocked => RuinGeometryPalette.GateUnlocked,
        _ => RuinGeometryPalette.GateLocked
    };
}
