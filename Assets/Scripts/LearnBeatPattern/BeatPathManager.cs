using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

// Manages the beat pattern path in the scene.
//
// ARCHITECTURE:
// - SplineContainer holds the path shape drawn visually in editor
// - LineRenderer displays the ideal path as a visible guide line
// - TracingPlane is an invisible Quad that receives raycasts
// - All path positions are projected onto the TracingPlane
// - StartOrb and EndOrb mark the start and end of the path
//
// HOW TO ADD A NEW PATTERN:
// 1. Draw a new spline for that pattern
// 2. Update ictusKnotIndices in the Inspector
// 3. No code changes needed

[RequireComponent(typeof(SplineContainer))]
public class BeatPathManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LineRenderer guidePathRenderer;
    [SerializeField] private Transform tracingPlane;
    [SerializeField] private GameObject startOrb;
    [SerializeField] private GameObject endOrb;

    [Header("Ictus Knot Indices")]
    // Which knot indices are ictus positions
    // 2/4: { 1, 3 } — knot 1 = beat 1 bottom, knot 3 = beat 2 top
    // Update in Inspector when changing patterns
    [SerializeField] private int[] ictusKnotIndices = new int[] { 1, 3 };

    [Header("Path Visualization")]
    // How many points to sample from spline for LineRenderer
    [SerializeField] private int lineRenderSampleCount = 30;

    [Header("Detection Radii (metres)")]
    // How close ray hit must be to start orb to activate tracing
    [SerializeField] private float startOrbRadius = 0.06f;
    // How close ray hit must be to end point to register success
    [SerializeField] private float endPointRadius = 0.06f;

    [Header("Orb Pulse Settings")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseAmount = 0.1f;

    [Header("Orb Colors")]
    [SerializeField] private Color orbActiveColor   = Color.yellow;
    [SerializeField] private Color orbInactiveColor = Color.grey;
    [SerializeField] private Color orbHoverColor    = Color.green;

    // Spline component on this GameObject
    private SplineContainer splineContainer;

    // Whether start orb is active and pulsing
    private bool orbActive = false;

    // Orb components
    private Vector3 orbOriginalScale;
    private Renderer startOrbRenderer;
    private Renderer endOrbRenderer;

    // Current pattern
    private BeatPattern currentPattern = BeatPattern.None;

    void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();

        if (startOrb != null)
        {
            orbOriginalScale  = startOrb.transform.localScale;
            startOrbRenderer  = startOrb.GetComponent<Renderer>();
        }

        if (endOrb != null)
            endOrbRenderer = endOrb.GetComponent<Renderer>();
    }

    void Update()
    {
        if (orbActive && startOrb != null && startOrb.activeSelf)
            PulseOrb();
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Shows the guide path for the selected pattern
    public void ShowPath(BeatPattern pattern)
    {
        currentPattern = pattern;

        if (splineContainer == null)
        {
            Debug.LogWarning("[BeatPathManager] SplineContainer not found");
            return;
        }

        BuildLineRendererFromSpline();
        PlaceOrbs();

        Debug.Log($"[BeatPathManager] Path shown for {pattern}");
    }

    // Hides path and orbs
    public void HidePath()
    {
        if (guidePathRenderer != null) guidePathRenderer.enabled = false;
        if (startOrb != null) startOrb.SetActive(false);
        if (endOrb != null) endOrb.SetActive(false);

        orbActive = false;
        currentPattern = BeatPattern.None;
    }

    // Activates start orb - called by SceneManager after companion audio
    public void ActivateStartOrb()
    {
        if (startOrb == null)
        {
            Debug.LogWarning("[BeatPathManager] Start orb not assigned");
            return;
        }

        startOrb.SetActive(true);
        orbActive = true;
        SetOrbColor(startOrbRenderer, orbActiveColor);
        Debug.Log("[BeatPathManager] Start orb activated");
    }

    // Deactivates start orb - called when tracing begins
    public void DeactivateStartOrb()
    {
        orbActive = false;
        SetOrbColor(startOrbRenderer, orbInactiveColor);

        if (startOrb != null)
            startOrb.transform.localScale = orbOriginalScale;
    }

    // Sets orb to hover color - called by TracingSystem when ray is over orb
    public void SetOrbHoverState(bool isHovering)
    {
        if (!orbActive) return;
        SetOrbColor(startOrbRenderer, isHovering ? orbHoverColor : orbActiveColor);
    }

    // Shows end orb - called by TracingSystem when player is near end
    public void ShowEndOrb()
    {
        if (endOrb != null)
        {
            endOrb.SetActive(true);
            SetOrbColor(endOrbRenderer, orbActiveColor);
        }
    }

    // ── DATA ACCESS ───────────────────────────────────────────────
    // Called by TracingSystem every frame

    // Returns the TracingPlane transform
    // TracingSystem uses this to get the plane for ray intersection
    public Transform GetTracingPlane()
    {
        return tracingPlane;
    }

    // Returns nearest point on spline to a position ON THE PLANE
    // Input position should already be projected onto the plane
    // Used by MetricsCollector for deviation calculation
    public Vector3 GetNearestPointOnSpline(Vector3 planePosition)
    {
        if (splineContainer == null) return planePosition;

        Vector3 localPosition = transform.InverseTransformPoint(planePosition);

        SplineUtility.GetNearestPoint(
            splineContainer.Spline,
            (float3)localPosition,
            out float3 nearestLocal,
            out float t
        );

        return transform.TransformPoint((Vector3)nearestLocal);
    }

    // Returns the nearest point on the spline to hitPosition
    // but only allows progress to move forward along the path
    // currentT is passed by reference so it persists between frames
    // This prevents deviation from snapping to the wrong stroke
    // when two parts of the path are geometrically close
    // public Vector3 GetForwardNearestPoint(Vector3 hitPosition, ref float currentT)
    // {
    //     if (splineContainer == null) return hitPosition;

    //     // Convert world position to spline local space
    //     Vector3 localPosition = transform.InverseTransformPoint(hitPosition);

    //     // Find nearest point anywhere on spline and its t value
    //     SplineUtility.GetNearestPoint(
    //         splineContainer.Spline,
    //         (float3)localPosition,
    //         out float3 nearestLocal,
    //         out float t
    //     );

    //     // Only advance t forward - never allow backward movement
    //     // This locks the reference point to the correct segment
    //     if (t > currentT)
    //         currentT = t;

    //     // Return world position at the forward-locked t value
    //     Vector3 lockedLocalPoint = splineContainer.Spline.EvaluatePosition(currentT);
    //     return transform.TransformPoint(lockedLocalPoint);
    // }

    // Returns world position of spline start projected onto plane
    public Vector3 GetStartPosition()
    {
        if (splineContainer == null) return Vector3.zero;
        Vector3 localPos = splineContainer.Spline[0].Position;
        return transform.TransformPoint(localPos);
    }

    // Returns world position of spline end projected onto plane
    public Vector3 GetEndPosition()
    {
        if (splineContainer == null) return Vector3.zero;
        int lastIndex = splineContainer.Spline.Count - 1;
        Vector3 localPos = splineContainer.Spline[lastIndex].Position;
        return transform.TransformPoint(localPos);
    }

    // Returns world positions of all ictus knots
    public Vector3[] GetIctusPositions()
    {
        if (splineContainer == null || ictusKnotIndices == null)
            return new Vector3[0];

        Vector3[] positions = new Vector3[ictusKnotIndices.Length];
        for (int i = 0; i < ictusKnotIndices.Length; i++)
        {
            int knotIndex = ictusKnotIndices[i];
            if (knotIndex < splineContainer.Spline.Count)
            {
                Vector3 localPos = splineContainer.Spline[knotIndex].Position;
                positions[i] = transform.TransformPoint(localPos);
            }
        }
        return positions;
    }

    // True if plane position is within start orb radius
    public bool IsNearStartOrb(Vector3 planeHitPosition)
    {
        return Vector3.Distance(planeHitPosition, GetStartPosition()) <= startOrbRadius;
    }

    // True if plane position is within end point radius
    public bool IsNearEndPoint(Vector3 planeHitPosition)
    {
        return Vector3.Distance(planeHitPosition, GetEndPosition()) <= endPointRadius;
    }

    public float GetStartOrbRadius() { return startOrbRadius; }
    public float GetEndPointRadius() { return endPointRadius; }

    // ── PRIVATE HELPERS ───────────────────────────────────────────

    // Samples spline into LineRenderer for display
    private void BuildLineRendererFromSpline()
    {
        if (guidePathRenderer == null || splineContainer == null) return;

        guidePathRenderer.enabled = true;
        guidePathRenderer.positionCount = lineRenderSampleCount;

        for (int i = 0; i < lineRenderSampleCount; i++)
        {
            float t = (float)i / (lineRenderSampleCount - 1);
            Vector3 localPos = splineContainer.Spline.EvaluatePosition(t);
            guidePathRenderer.SetPosition(i, transform.TransformPoint(localPos));
        }
    }

    // Places orbs at spline start and end positions
    private void PlaceOrbs()
    {
        if (startOrb != null)
        {
            startOrb.transform.position = GetStartPosition();
            startOrb.SetActive(false);
            SetOrbColor(startOrbRenderer, orbInactiveColor);
        }

        if (endOrb != null)
        {
            endOrb.transform.position = GetEndPosition();
            endOrb.SetActive(false);
        }
    }

    private void PulseOrb()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        startOrb.transform.localScale = orbOriginalScale * (1f + pulse);
    }

    private void SetOrbColor(Renderer r, Color color)
    {
        if (r != null) r.material.color = color;
    }
}