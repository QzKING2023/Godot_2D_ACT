using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Unified Chinese sans-serif fallback font loader shared across HUD, menus, and debug overlays.
/// </summary>
public static class UiFonts
{
    public static readonly string[] SansFallbackNames =
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "PingFang SC",
        "SimHei",
        "Segoe UI"
    };

    private static Font? _default;

    /// <summary>Cached Chinese sans-serif font with engine fallback.</summary>
    public static Font Default => _default ??= CreateDefaultFont();

    private static Font CreateDefaultFont()
    {
        var font = new SystemFont
        {
            FontNames = SansFallbackNames,
            Antialiasing = TextServer.FontAntialiasing.Gray
        };
        if (ThemeDB.FallbackFont is { } fallback)
            font.Fallbacks = new Godot.Collections.Array<Font> { fallback };
        return font;
    }
}
