using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;


// Quick POC to test minimap generation
// Attach to empty GameObject, assign references in Inspector
// Call GenerateMinimap() from anywhere to test

public class MinimapPOC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BeatPathManager beatPathManager;
    [SerializeField] private RawImage displayImage; // assign a RawImage on a test canvas

    [Header("Settings")]
    [SerializeField] private int textureSize = 256;
    [SerializeField] private Color backgroundColor = Color.black;
    [SerializeField] private Color idealPathColor  = Color.white;
    [SerializeField] private Color traceColor      = Color.green;

    // Call this after a trace attempt to test the minimap
    [ContextMenu("Generate Test Minimap")]
    public void GenerateMinimap(List<Vector3> tracePoints, List<Color> traceColors)
    {
        Texture2D tex = new Texture2D(textureSize, textureSize);

        // Fill background
        Color[] pixels = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;
        tex.SetPixels(pixels);

        // Sample ideal spline path (60 points)
        List<Vector3> idealPoints = beatPathManager.GetSplineSamplePoints(60);
        DrawPathOnTexture(tex, idealPoints, idealPathColor, 2);

        // Draw player trace
        if (tracePoints != null && tracePoints.Count > 1)
            DrawPathOnTexture(tex, tracePoints, traceColor, 2);

        tex.Apply();

        if (displayImage != null)
            displayImage.texture = tex;
    }

    private void DrawPathOnTexture(Texture2D tex, List<Vector3> points, Color color, int thickness)
    {
        if (points.Count < 2) return;

        // Find bounds of all points to normalise to texture space
        Vector3 min = points[0];
        Vector3 max = points[0];
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        float rangeX = Mathf.Max(max.x - min.x, 0.01f);
        float rangeY = Mathf.Max(max.y - min.y, 0.01f);
        float range  = Mathf.Max(rangeX, rangeY);

        float padding = 0.1f;
        float scale   = textureSize * (1f - padding * 2f);
        float offsetX = textureSize * padding;
        float offsetY = textureSize * padding;

        for (int i = 0; i < points.Count - 1; i++)
        {
            int x0 = Mathf.RoundToInt(((points[i].x - min.x) / range) * scale + offsetX);
            int y0 = Mathf.RoundToInt(((points[i].y - min.y) / range) * scale + offsetY);
            int x1 = Mathf.RoundToInt(((points[i+1].x - min.x) / range) * scale + offsetX);
            int y1 = Mathf.RoundToInt(((points[i+1].y - min.y) / range) * scale + offsetY);

            DrawLine(tex, x0, y0, x1, y1, color, thickness);
        }
    }

    private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color color, int thickness)
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