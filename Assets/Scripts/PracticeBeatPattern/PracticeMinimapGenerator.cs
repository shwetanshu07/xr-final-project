using UnityEngine;
using System.Collections.Generic;

// Practice mode minimap generator.
// Shows ideal path (grey) overlaid with player trace (white).
// Both paths are independently normalised to the texture bounds
// so size differences do not affect the visual comparison.

public class PracticeMinimapGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BeatPathManager      beatPathManager;
    [SerializeField] private PracticeTracingSystem tracingSystem;

    [Header("Texture Settings")]
    [SerializeField] private int textureSize = 256;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.15f);
    [SerializeField] private Color idealPathColor   = new Color(0.4f, 0.4f, 0.5f);
    [SerializeField] private Color traceColor       = Color.white;

    [Header("Line Thickness")]
    [SerializeField] private int idealPathThickness = 1;
    [SerializeField] private int traceThickness     = 2;

    public Texture2D Generate()
    {
        Texture2D tex    = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        Color[] pixels   = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = backgroundColor;
        tex.SetPixels(pixels);

        List<Vector3> idealPoints = beatPathManager.GetSplineSamplePoints(80);
        List<Vector3> tracePoints = tracingSystem.GetTracePoints();

        // Draw ideal path normalised to its own bounds (top-left quadrant area)
        // Draw player trace normalised to its own bounds (full texture)
        // This way both are readable regardless of scale difference
        DrawPathNormalised(tex, idealPoints, null, idealPathColor, idealPathThickness);
        DrawPathNormalised(tex, tracePoints, tracingSystem.GetTraceColors(), traceColor, traceThickness);

        tex.Apply();
        return tex;
    }

    // Normalises path to fit within texture with padding
    // Each path uses its own bounds so scale difference does not matter
    private void DrawPathNormalised(Texture2D tex, List<Vector3> points,
                                    List<Color> colors, Color defaultColor, int thickness)
    {
        if (points == null || points.Count < 2) return;

        // Calculate bounds for this path only
        Vector3 min = points[0];
        Vector3 max = points[0];
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        float rangeX  = Mathf.Max(max.x - min.x, 0.01f);
        float rangeY  = Mathf.Max(max.y - min.y, 0.01f);
        float range   = Mathf.Max(rangeX, rangeY);

        float padding = 0.1f;
        float scale   = textureSize * (1f - padding * 2f);
        float offsetX = textureSize * padding;
        float offsetY = textureSize * padding;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Color c = (colors != null && i < colors.Count) ? colors[i] : defaultColor;

            int x0 = Mathf.RoundToInt(((points[i].x   - min.x) / range) * scale + offsetX);
            int y0 = Mathf.RoundToInt(((points[i].y   - min.y) / range) * scale + offsetY);
            int x1 = Mathf.RoundToInt(((points[i+1].x - min.x) / range) * scale + offsetX);
            int y1 = Mathf.RoundToInt(((points[i+1].y - min.y) / range) * scale + offsetY);

            DrawLine(tex, x0, y0, x1, y1, c, thickness);
        }
    }

    private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1,
                          Color color, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            for (int tx = -thickness; tx <= thickness; tx++)
                for (int ty = -thickness; ty <= thickness; ty++)
                {
                    int px = Mathf.Clamp(x0 + tx, 0, textureSize - 1);
                    int py = Mathf.Clamp(y0 + ty, 0, textureSize - 1);
                    tex.SetPixel(px, py, color);
                }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
    }
}