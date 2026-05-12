using UnityEngine;
using System.Collections.Generic;

// Generates a 2D minimap texture showing ideal path and player trace
// Called by SceneManager after each attempt
// Returns a Texture2D that FeedbackPanel displays in its RawImage

public class MinimapGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BeatPathManager beatPathManager;
    [SerializeField] private TracingSystem   tracingSystem;

    [Header("Texture Settings")]
    [SerializeField] private int textureSize = 256;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.15f);
    [SerializeField] private Color idealPathColor   = new Color(0.8f, 0.8f, 0.8f);

    [Header("Line Thickness")]
    [SerializeField] private int idealPathThickness = 2;
    [SerializeField] private int traceThickness     = 2;

    // Generates and returns the minimap texture
    // Call after trace attempt ends
    public Texture2D Generate()
    {
        Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);

        // Fill background
        Color[] pixels = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;
        tex.SetPixels(pixels);

        // Get all points to calculate shared bounds
        List<Vector3> idealPoints = beatPathManager.GetSplineSamplePoints(80);
        List<Vector3> tracePoints = tracingSystem.GetTracePoints();
        List<Color>   traceColors = tracingSystem.GetTraceColors();

        // Calculate bounds across both paths
        Bounds bounds = CalculateBounds(idealPoints, tracePoints);

        // Draw ideal path first (underneath)
        DrawPath(tex, idealPoints, null, bounds, idealPathColor, idealPathThickness);

        // Draw player trace on top (coloured)
        DrawPath(tex, tracePoints, traceColors, bounds, Color.green, traceThickness);

        tex.Apply();
        return tex;
    }

    private Bounds CalculateBounds(List<Vector3> ideal, List<Vector3> trace)
    {
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        foreach (Vector3 p in ideal) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        foreach (Vector3 p in trace) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

        return new Bounds((min + max) * 0.5f, max - min);
    }

    private void DrawPath(Texture2D tex, List<Vector3> points, List<Color> colors,
                          Bounds bounds, Color defaultColor, int thickness)
    {
        if (points == null || points.Count < 2) return;

        float padding = 0.15f;
        float scale   = textureSize * (1f - padding * 2f);
        float offsetX = textureSize * padding;
        float offsetY = textureSize * padding;

        float rangeX = Mathf.Max(bounds.size.x, 0.01f);
        float rangeY = Mathf.Max(bounds.size.y, 0.01f);
        float range  = Mathf.Max(rangeX, rangeY);

        for (int i = 0; i < points.Count - 1; i++)
        {
            Color c = (colors != null && i < colors.Count) ? colors[i] : defaultColor;

            int x0 = Mathf.RoundToInt(((points[i].x   - bounds.min.x) / range) * scale + offsetX);
            int y0 = Mathf.RoundToInt(((points[i].y   - bounds.min.y) / range) * scale + offsetY);
            int x1 = Mathf.RoundToInt(((points[i+1].x - bounds.min.x) / range) * scale + offsetX);
            int y1 = Mathf.RoundToInt(((points[i+1].y - bounds.min.y) / range) * scale + offsetY);

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