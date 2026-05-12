using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

// Manages beat pattern paths in the scene.
// Each pattern has its own child GameObject (patternRoot) containing
// SplineContainer, LineRenderer, TracingPlane, StartOrb and EndOrb.
// This manager activates the correct patternRoot based on selection.
//
// HIERARCHY EXPECTED:
// BeatPathRoot (this script only — no SplineContainer here)
// ├── Pattern_2Beat (has SplineContainer + GuidePath + StartOrb + EndOrb + TracingPlane)
// └── Pattern_3Beat (has SplineContainer + GuidePath + StartOrb + EndOrb + TracingPlane)
//
// HOW TO ADD A NEW PATTERN:
// 1. Create child GameObject with all required children
// 2. Add a PatternData entry in Inspector and fill all fields
// 3. No code changes needed

[System.Serializable]
public class PatternData
{
    // Which BeatPattern enum value this entry represents
    public BeatPattern  pattern;

    // Root GameObject for this pattern — must have SplineContainer attached
    public GameObject   patternRoot;

    // LineRenderer child that shows the guide path
    public LineRenderer guidePathRenderer;

    // Invisible Quad child with MeshCollider — receives raycasts from controller
    public Transform    tracingPlane;

    // Sphere child at start of path — pulses yellow when active
    public GameObject   startOrb;

    // Sphere child at end of path — shown on success
    public GameObject   endOrb;

    // Which knot indices on the spline are ictus (beat position) points
    // 2/4 pattern: { 1, 3 }       knot 1 = beat 1, knot 3 = beat 2
    // 3/4 pattern: { 1, 2, 3 }    knot 1 = beat 1, knot 2 = beat 2, knot 3 = beat 3
    // These indices are passed to MetricsCollector.StartRecording()
    public int[]        ictusKnotIndices;
}

public class BeatPathManager : MonoBehaviour
{
    [Header("Pattern Data")]
    // Add one entry per supported pattern
    // Assign ALL fields for each entry in Inspector
    // Missing fields will cause NullReferenceException at runtime
    [SerializeField] private List<PatternData> patterns = new List<PatternData>();

    [Header("Path Visualization")]
    [SerializeField] private int lineRenderSampleCount = 90;

    [Header("Detection Radii (metres)")]
    [SerializeField] private float startOrbRadius = 0.06f;
    [SerializeField] private float endPointRadius  = 0.06f;

    [Header("Orb Pulse Settings")]
    [SerializeField] private float pulseSpeed  = 2f;
    [SerializeField] private float pulseAmount = 0.1f;

    [Header("Orb Colors")]
    [SerializeField] private Color orbActiveColor   = Color.yellow;
    [SerializeField] private Color orbInactiveColor = Color.grey;
    [SerializeField] private Color orbHoverColor    = Color.green;

    // Currently active pattern — set by ShowPath()
    private PatternData     activePattern;
    private SplineContainer activeSpline;
    private bool            orbActive = false;
    private Vector3         orbOriginalScale;
    private Renderer        startOrbRenderer;
    private Renderer        endOrbRenderer;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Awake()
    {
        // Deactivate all pattern roots at scene start
        // ShowPath() activates the correct one when user selects a pattern
        foreach (PatternData pd in patterns)
        {
            if (pd.patternRoot != null)
                pd.patternRoot.SetActive(false);
            else
                Debug.LogError($"[BeatPathManager] PatternData entry for " +
                               $"{pd.pattern} has null patternRoot. " +
                               $"Assign it in Inspector.");
        }
    }

    void Update()
    {
        if (orbActive && activePattern != null &&
            activePattern.startOrb != null &&
            activePattern.startOrb.activeSelf)
            PulseOrb();
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by SceneManager when user selects a beat pattern
    // Deactivates all patterns then activates the selected one
    public void ShowPath(BeatPattern pattern)
    {
        // Deactivate all first
        foreach (PatternData pd in patterns)
        {
            if (pd.patternRoot != null)
                pd.patternRoot.SetActive(false);
        }

        // Find matching entry
        activePattern = patterns.Find(p => p.pattern == pattern);
        if (activePattern == null)
        {
            Debug.LogError($"[BeatPathManager] No PatternData found for {pattern}. " +
                           $"Add an entry to the Patterns list in Inspector.");
            return;
        }

        // Activate this pattern
        activePattern.patternRoot.SetActive(true);

        // Get SplineContainer from the pattern root
        activeSpline = activePattern.patternRoot.GetComponent<SplineContainer>();
        if (activeSpline == null)
        {
            Debug.LogError($"[BeatPathManager] No SplineContainer on " +
                           $"{activePattern.patternRoot.name}. " +
                           $"Add SplineContainer component to it.");
            return;
        }

        // Cache orb renderers
        if (activePattern.startOrb != null)
        {
            orbOriginalScale = activePattern.startOrb.transform.localScale;
            startOrbRenderer = activePattern.startOrb.GetComponent<Renderer>();
        }
        else
            Debug.LogWarning($"[BeatPathManager] StartOrb is null for {pattern}. " +
                             $"Assign it in Inspector.");

        if (activePattern.endOrb != null)
            endOrbRenderer = activePattern.endOrb.GetComponent<Renderer>();
        else
            Debug.LogWarning($"[BeatPathManager] EndOrb is null for {pattern}. " +
                             $"Assign it in Inspector.");

        BuildLineRendererFromSpline();
        PlaceOrbs();

        Debug.Log($"[BeatPathManager] Path shown for {pattern}");
    }

    // Hides active pattern
    public void HidePath()
    {
        if (activePattern == null) return;

        if (activePattern.guidePathRenderer != null)
            activePattern.guidePathRenderer.enabled = false;
        if (activePattern.startOrb != null)
            activePattern.startOrb.SetActive(false);
        if (activePattern.endOrb != null)
            activePattern.endOrb.SetActive(false);

        orbActive     = false;
        activePattern = null;
        activeSpline  = null;
    }

    // Called by SceneManager after intro wait — shows pulsing start orb
    public void ActivateStartOrb()
    {
        if (activePattern == null)
        {
            Debug.LogWarning("[BeatPathManager] ActivateStartOrb — no active pattern. " +
                             "Call ShowPath() first.");
            return;
        }
        if (activePattern.startOrb == null)
        {
            Debug.LogWarning("[BeatPathManager] ActivateStartOrb — startOrb is null. " +
                             "Assign StartOrb in Inspector.");
            return;
        }

        activePattern.startOrb.SetActive(true);
        orbActive = true;
        SetOrbColor(startOrbRenderer, orbActiveColor);
        Debug.Log("[BeatPathManager] Start orb activated");
    }

    // Called by TracingSystem when tracing begins
    public void DeactivateStartOrb()
    {
        orbActive = false;
        SetOrbColor(startOrbRenderer, orbInactiveColor);

        if (activePattern != null && activePattern.startOrb != null)
            activePattern.startOrb.transform.localScale = orbOriginalScale;
    }

    // Called by TracingSystem each frame when ray is near orb
    public void SetOrbHoverState(bool isHovering)
    {
        if (!orbActive) return;
        SetOrbColor(startOrbRenderer, isHovering ? orbHoverColor : orbActiveColor);
    }

    // Called by TracingSystem on success
    public void ShowEndOrb()
    {
        if (activePattern == null || activePattern.endOrb == null) return;
        activePattern.endOrb.SetActive(true);
        SetOrbColor(endOrbRenderer, orbActiveColor);
    }

    // ── DATA ACCESS ───────────────────────────────────────────────
    // All these are called by TracingSystem, MinimapGenerator, MetricsCollector

    // Returns TracingPlane transform — used by TracingSystem for ray intersection
    public Transform GetTracingPlane()
    {
        if (activePattern == null)
        {
            Debug.LogWarning("[BeatPathManager] GetTracingPlane — no active pattern");
            return null;
        }
        if (activePattern.tracingPlane == null)
        {
            Debug.LogWarning("[BeatPathManager] GetTracingPlane — tracingPlane is null. " +
                             "Assign TracingPlane in Inspector.");
            return null;
        }
        return activePattern.tracingPlane;
    }

    // Returns nearest point on active spline to a world position
    // Called by TracingSystem every frame for deviation calculation
    public Vector3 GetNearestPointOnSpline(Vector3 worldPosition)
    {
        if (activeSpline == null) return worldPosition;

        Vector3 localPosition = activePattern.patternRoot.transform
                                    .InverseTransformPoint(worldPosition);

        SplineUtility.GetNearestPoint(
            activeSpline.Spline,
            (float3)localPosition,
            out float3 nearestLocal,
            out float t
        );

        return activePattern.patternRoot.transform.TransformPoint((Vector3)nearestLocal);
    }

    // Returns world position of first knot (start point)
    public Vector3 GetStartPosition()
    {
        if (activeSpline == null) return Vector3.zero;
        Vector3 localPos = activeSpline.Spline[0].Position;
        return activePattern.patternRoot.transform.TransformPoint(localPos);
    }

    // Returns world position of last knot (end point)
    public Vector3 GetEndPosition()
    {
        if (activeSpline == null) return Vector3.zero;
        int lastIndex = activeSpline.Spline.Count - 1;
        Vector3 localPos = activeSpline.Spline[lastIndex].Position;
        return activePattern.patternRoot.transform.TransformPoint(localPos);
    }

    // Returns world positions of ictus knots for active pattern
    // Passed to MetricsCollector.StartRecording() in TracingSystem.StartTracing()
    // 2/4 returns 2 positions, 3/4 returns 3 positions
    public Vector3[] GetIctusPositions()
    {
        if (activeSpline == null || activePattern.ictusKnotIndices == null)
            return new Vector3[0];

        Vector3[] positions = new Vector3[activePattern.ictusKnotIndices.Length];
        for (int i = 0; i < activePattern.ictusKnotIndices.Length; i++)
        {
            int knotIndex = activePattern.ictusKnotIndices[i];
            if (knotIndex < activeSpline.Spline.Count)
            {
                Vector3 localPos = activeSpline.Spline[knotIndex].Position;
                positions[i] = activePattern.patternRoot.transform.TransformPoint(localPos);
            }
            else
            {
                Debug.LogWarning($"[BeatPathManager] ictusKnotIndices[{i}] = {knotIndex} " +
                                 $"is out of range. Spline has {activeSpline.Spline.Count} knots " +
                                 $"(valid indices 0 to {activeSpline.Spline.Count - 1}).");
            }
        }
        return positions;
    }

    // Returns N evenly spaced world positions along active spline
    // Called by MinimapGenerator to draw the ideal path on the minimap image
    public List<Vector3> GetSplineSamplePoints(int count)
    {
        List<Vector3> points = new List<Vector3>();
        if (activeSpline == null) return points;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            float3 localPos = activeSpline.Spline.EvaluatePosition(t);
            points.Add(activePattern.patternRoot.transform.TransformPoint((Vector3)localPos));
        }
        return points;
    }

    // Proximity check — called by TracingSystem every frame in Idle/NearOrb states
    public bool IsNearStartOrb(Vector3 planeHitPosition)
        => Vector3.Distance(planeHitPosition, GetStartPosition()) <= startOrbRadius;

    // Proximity check — called by TracingSystem every frame during tracing
    public bool IsNearEndPoint(Vector3 planeHitPosition)
        => Vector3.Distance(planeHitPosition, GetEndPosition()) <= endPointRadius;

    public float GetStartOrbRadius() => startOrbRadius;
    public float GetEndPointRadius() => endPointRadius;

    // ── PRIVATE HELPERS ───────────────────────────────────────────

    private void BuildLineRendererFromSpline()
    {
        if (activePattern.guidePathRenderer == null)
        {
            Debug.LogWarning("[BeatPathManager] guidePathRenderer is null. " +
                             "Assign GuidePath LineRenderer in Inspector.");
            return;
        }
        if (activeSpline == null) return;

        activePattern.guidePathRenderer.enabled       = true;
        activePattern.guidePathRenderer.positionCount = lineRenderSampleCount;

        for (int i = 0; i < lineRenderSampleCount; i++)
        {
            float   t        = (float)i / (lineRenderSampleCount - 1);
            Vector3 localPos = activeSpline.Spline.EvaluatePosition(t);
            activePattern.guidePathRenderer.SetPosition(
                i,
                activePattern.patternRoot.transform.TransformPoint(localPos)
            );
        }
    }

    private void PlaceOrbs()
    {
        if (activePattern.startOrb != null)
        {
            activePattern.startOrb.transform.position = GetStartPosition();
            activePattern.startOrb.SetActive(false);
            SetOrbColor(startOrbRenderer, orbInactiveColor);
        }

        if (activePattern.endOrb != null)
        {
            activePattern.endOrb.transform.position = GetEndPosition();
            activePattern.endOrb.SetActive(false);
        }
    }

    private void PulseOrb()
    {
        if (activePattern?.startOrb == null) return;
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        activePattern.startOrb.transform.localScale = orbOriginalScale * (1f + pulse);
    }

    private void SetOrbColor(Renderer r, Color color)
    {
        if (r != null) r.material.color = color;
    }

    // ------------ PRACTICE MODE
    // Hides the visual guide path line without deactivating the pattern root
    // Used by Practice mode — TracingPlane must stay active for raycasts
    public void HideGuidePath()
    {
        if (activePattern != null && activePattern.guidePathRenderer != null)
            activePattern.guidePathRenderer.enabled = false;
    }
}