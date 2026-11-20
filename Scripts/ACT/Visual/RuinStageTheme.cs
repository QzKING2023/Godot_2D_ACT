using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Immutable stage atmosphere configuration for「遗迹几何表现」backdrop progression
/// across Tutorial, FormalRoute, FormalEncounter, BossArena, Title, and Victory.
/// </summary>
public sealed record RuinStageTheme(
    string Name,
    Color SkyTop,
    Color SkyHorizon,
    Color SunOuterGlow,
    Color SunCoreGlow,
    float SunAltitudeY,
    float SunScale,
    Color HillsColor,
    float HillsParallax,
    float TowerBaseX,
    float TowerScale,
    float TowerParallax,
    int PillarCount,
    float PillarStartX,
    float PillarSpacing,
    float PillarScale,
    float PillarParallax)
{
    public static RuinStageTheme Tutorial { get; } = new(
        Name: "Tutorial",
        SkyTop: new Color("3c4b63"),
        SkyHorizon: new Color("b78c6a"),
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow with { A = 0.09f },
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow with { A = 0.18f },
        SunAltitudeY: 124f,
        SunScale: 0.85f,
        HillsColor: RuinGeometryPalette.FarHills.Lerp(new Color("3c4b63"), 0.22f),
        HillsParallax: 0.20f,
        TowerBaseX: 960f,
        TowerScale: 0.68f,
        TowerParallax: 0.12f,
        PillarCount: 2,
        PillarStartX: 520f,
        PillarSpacing: 740f,
        PillarScale: 0.80f,
        PillarParallax: 0.42f);

    public static RuinStageTheme FormalRoute { get; } = new(
        Name: "FormalRoute",
        SkyTop: new Color("344158"),
        SkyHorizon: new Color("c08b60"),
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow with { A = 0.10f },
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow with { A = 0.20f },
        SunAltitudeY: 136f,
        SunScale: 0.92f,
        HillsColor: RuinGeometryPalette.FarHills.Lerp(new Color("344158"), 0.12f),
        HillsParallax: 0.20f,
        TowerBaseX: 920f,
        TowerScale: 0.82f,
        TowerParallax: 0.12f,
        PillarCount: 3,
        PillarStartX: 460f,
        PillarSpacing: 450f,
        PillarScale: 0.88f,
        PillarParallax: 0.42f);

    public static RuinStageTheme FormalEncounter { get; } = new(
        Name: "FormalEncounter",
        SkyTop: new Color("2f394e"),
        SkyHorizon: new Color("c58b59"),
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow with { A = 0.11f },
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow with { A = 0.22f },
        SunAltitudeY: 146f,
        SunScale: 0.98f,
        HillsColor: RuinGeometryPalette.FarHills.Lerp(new Color("2f394e"), 0.06f),
        HillsParallax: 0.20f,
        TowerBaseX: 890f,
        TowerScale: 0.94f,
        TowerParallax: 0.12f,
        PillarCount: 4,
        PillarStartX: 410f,
        PillarSpacing: 330f,
        PillarScale: 0.96f,
        PillarParallax: 0.42f);

    public static RuinStageTheme BossArena { get; } = new(
        Name: "BossArena",
        SkyTop: RuinGeometryPalette.SkyTop,
        SkyHorizon: RuinGeometryPalette.SkyHorizon,
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow,
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow,
        SunAltitudeY: 155f,
        SunScale: 1.05f,
        HillsColor: RuinGeometryPalette.FarHills,
        HillsParallax: 0.20f,
        TowerBaseX: 860f,
        TowerScale: 1.08f,
        TowerParallax: 0.12f,
        PillarCount: 7,
        PillarStartX: 280f,
        PillarSpacing: 285f,
        PillarScale: 1.04f,
        PillarParallax: 0.42f);

    public static RuinStageTheme Title { get; } = new(
        Name: "Title",
        SkyTop: RuinGeometryPalette.SkyTop,
        SkyHorizon: RuinGeometryPalette.SkyHorizon,
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow,
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow,
        SunAltitudeY: 144f,
        SunScale: 1.00f,
        HillsColor: RuinGeometryPalette.FarHills,
        HillsParallax: 0.20f,
        TowerBaseX: 880f,
        TowerScale: 0.90f,
        TowerParallax: 0.12f,
        PillarCount: 4,
        PillarStartX: 360f,
        PillarSpacing: 360f,
        PillarScale: 0.92f,
        PillarParallax: 0.42f);

    public static RuinStageTheme Victory { get; } = new(
        Name: "Victory",
        SkyTop: new Color("344359"),
        SkyHorizon: new Color("d69c69"),
        SunOuterGlow: RuinGeometryPalette.SunOuterGlow with { A = 0.14f },
        SunCoreGlow: RuinGeometryPalette.SunCoreGlow with { A = 0.28f },
        SunAltitudeY: 130f,
        SunScale: 1.10f,
        HillsColor: RuinGeometryPalette.FarHills.Lerp(new Color("d69c69"), 0.08f),
        HillsParallax: 0.20f,
        TowerBaseX: 880f,
        TowerScale: 0.84f,
        TowerParallax: 0.12f,
        PillarCount: 3,
        PillarStartX: 420f,
        PillarSpacing: 420f,
        PillarScale: 0.88f,
        PillarParallax: 0.42f);
}
