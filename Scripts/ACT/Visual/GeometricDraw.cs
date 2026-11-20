using System;
using Godot;

namespace ACTSpike.ACT.Visual;

/// <summary>
/// Shared 2D procedural drawing primitives for「遗迹几何表现」hard-edged faceted geometry.
/// </summary>
public static class GeometricDraw
{
    public static void FillPolygon(CanvasItem canvas, Vector2[] points, Color color)
    {
        if (points.Length < 3) return;
        canvas.DrawPolygon(points, new[] { color });
    }

    public static void StrokePolygon(CanvasItem canvas, Vector2[] points, Color color, float width = 2f, bool antialiased = true)
    {
        if (points.Length < 2) return;
        var closed = new Vector2[points.Length + 1];
        Array.Copy(points, closed, points.Length);
        closed[points.Length] = points[0];
        canvas.DrawPolyline(closed, color, width, antialiased);
    }

    public static void FillAndStrokePolygon(CanvasItem canvas, Vector2[] points, Color fill, Color stroke, float width = 2f, bool antialiased = true)
    {
        FillPolygon(canvas, points, fill);
        StrokePolygon(canvas, points, stroke, width, antialiased);
    }

    public static Vector2[] TransformPoints(
        ReadOnlySpan<Vector2> localPoints,
        Vector2 origin,
        float scaleX = 1f,
        float scaleY = 1f,
        float rotation = 0f)
    {
        var result = new Vector2[localPoints.Length];
        float cos = Mathf.Cos(rotation);
        float sin = Mathf.Sin(rotation);
        for (int i = 0; i < localPoints.Length; i++)
        {
            float x = localPoints[i].X * scaleX;
            float y = localPoints[i].Y * scaleY;
            result[i] = origin + new Vector2(x * cos - y * sin, x * sin + y * cos);
        }
        return result;
    }

    public static void VerticalGradient(CanvasItem canvas, Rect2 rect, Color top, Color bottom, int bands = 16)
    {
        int count = Mathf.Max(1, bands);
        float bandHeight = rect.Size.Y / count;
        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            canvas.DrawRect(
                new Rect2(rect.Position.X, rect.Position.Y + i * bandHeight, rect.Size.X, bandHeight + 1f),
                top.Lerp(bottom, t));
        }
    }

    public static void FacetedPlatform(CanvasItem canvas, Rect2 rect, bool drawLitTopEdge = true)
    {
        canvas.DrawRect(rect, RuinGeometryPalette.PlatformBody);
        if (rect.Size.Y > 12f)
        {
            canvas.DrawRect(
                new Rect2(rect.Position.X, rect.Position.Y + 12f, rect.Size.X, rect.Size.Y - 12f),
                RuinGeometryPalette.PlatformShadow);
        }

        if (!drawLitTopEdge) return;

        canvas.DrawRect(new Rect2(rect.Position.X, rect.Position.Y, rect.Size.X, 7f), RuinGeometryPalette.PlatformTopLit);
        if (rect.Size.Y > 12f)
            canvas.DrawRect(new Rect2(rect.Position.X, rect.Position.Y + 7f, rect.Size.X, 5f), RuinGeometryPalette.PlatformTopBevel);

        for (float gx = rect.Position.X + 80f; gx < rect.End.X - 20f; gx += 120f)
            canvas.DrawLine(new Vector2(gx, rect.Position.Y + 12f), new Vector2(gx, rect.End.Y), RuinGeometryPalette.PlatformJoint, 2f);
    }

    public static void Diamond(CanvasItem canvas, Vector2 center, float halfWidth, float halfHeight, Color color)
    {
        FillPolygon(canvas, new[]
        {
            center + new Vector2(0f, -halfHeight),
            center + new Vector2(halfWidth, 0f),
            center + new Vector2(0f, halfHeight),
            center + new Vector2(-halfWidth, 0f)
        }, color);
    }

    public static void AngledBar(
        CanvasItem canvas,
        Rect2 rect,
        float fraction,
        Color background,
        Color fill,
        float skew = 8f)
    {
        float clamped = Mathf.Clamp(fraction, 0f, 1f);
        FillSkewedRect(canvas, rect, skew, background);

        if (clamped <= 0.005f) return;

        var fillRect = new Rect2(rect.Position, new Vector2(rect.Size.X * clamped, rect.Size.Y));
        FillSkewedRect(canvas, fillRect, skew, fill);
    }

    private static void FillSkewedRect(CanvasItem canvas, Rect2 rect, float skew, Color color)
    {
        Vector2 pos = rect.Position;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        FillPolygon(canvas, new[]
        {
            pos,
            pos + new Vector2(w, 0f),
            pos + new Vector2(w - skew, h),
            pos + new Vector2(-skew, h)
        }, color);
    }
}
