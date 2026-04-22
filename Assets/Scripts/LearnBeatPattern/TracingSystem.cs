using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

// Handles all tracing interaction for Module 1.
//
// APPROACH:
// Ray from right controller hits the invisible TracingPlane.
// The hit position on the plane is the drawing point each frame.
// This gives a clean 2D position for drawing and deviation calculation.
// The baton is purely visual - all logic uses the ray hit point.
//
// STATE MACHINE:
// Inactive → Idle → NearOrb → Tracing → Finished
//
// FLOW:
// 1. Beat selected → path appears → orb activates → Activate() called
// 2. Player points ray at plane → Idle state
// 3. Ray hits near start orb → NearOrb state → orb turns green
// 4. Player holds trigger → Tracing begins
// 5. Ray hit position drawn as colored trail on plane each frame
// 6. Trigger released → Failed
//    Timer runs out → Failed
//    Ray hits near end point → Success

public class TracingSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BeatPathManager beatPathManager;
    [SerializeField] private MetricsCollector metricsCollector;

    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Ray")]
    // Assign Right Controller transform here
    // Found under XR Origin → Right Controller
    [SerializeField] private Transform rayOriginTransform;

    [Header("Trail Drawing")]
    // Width of drawn trail in metres
    [SerializeField] private float trailWidth = 0.01f;

    [Header("Trail Colors")]
    [SerializeField] private Color colorClose  = Color.green;
    [SerializeField] private Color colorMedium = Color.yellow;
    [SerializeField] private Color colorFar    = Color.red;

    [Header("Deviation Thresholds (metres)")]
    // Distance from path that counts as close (green)
    [SerializeField] private float closeThreshold  = 0.20f;
    // Distance from path that counts as medium (yellow-red transition)
    [SerializeField] private float mediumThreshold = 0.30f;

    [Header("Timeout")]
    [SerializeField] private float timeoutSeconds = 30f;

    [Header("Editor Testing")]
    // Press Space in editor to force start tracing without needing headset
    [SerializeField] private bool enableEditorShortcut = true;

    // Events fired to SceneManager
    public event Action<MetricsResult> OnTraceSuccess;
    public event Action<FailReason>    OnTraceFailed;
    public event Action                OnTraceStarted;

    // Internal state machine
    private enum TracingState
    {
        Inactive,  // system not yet activated by SceneManager
        Idle,      // activated, waiting for ray to hit near start orb
        NearOrb,   // ray is near start orb, waiting for trigger hold
        Tracing,   // actively tracing - drawing trail each frame
        Finished   // trace ended (success or fail) waiting for reset
    }

    private TracingState currentState = TracingState.Inactive;

    // Input action for trigger button
    private InputAction triggerAction;

    // Whether trigger is currently held down
    private bool triggerHeld = false;

    // Timer that counts up during tracing
    private float elapsedTime = 0f;

    // Trail is drawn as multiple LineRenderer segments
    // Each segment has one color - new segment created when color changes
    private List<LineRenderer> trailSegments = new List<LineRenderer>();
    private LineRenderer currentSegment;

    // Last hit position on the plane - used to connect segments smoothly
    private Vector3 lastHitPosition;
    private bool hasLastPosition = false;

    // Container GameObject for all trail segment GameObjects
    private GameObject trailContainer;

    // Controller position for velocity calculation (Metric 3)
    private Vector3 previousControllerPosition;
    private bool hasPreviousControllerPos = false;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Awake()
    {
        SetupInputActions();

        trailContainer = new GameObject("TrailContainer");
        trailContainer.transform.SetParent(transform);
    }

    void OnEnable()
    {
        if (triggerAction != null)
        {
            triggerAction.Enable();
            triggerAction.performed += OnTriggerPerformed;
            triggerAction.canceled  += OnTriggerCanceled;
        }
    }

    void OnDisable()
    {
        if (triggerAction != null)
        {
            triggerAction.performed -= OnTriggerPerformed;
            triggerAction.canceled  -= OnTriggerCanceled;
            triggerAction.Disable();
        }
    }

    void Update()
    {
#if UNITY_EDITOR
        if (enableEditorShortcut &&
            Input.GetKeyDown(KeyCode.Space) &&
            currentState != TracingState.Tracing &&
            currentState != TracingState.Finished &&
            currentState != TracingState.Inactive)
        {
            Debug.Log("[TracingSystem] EDITOR: Space key - force starting tracing");
            StartTracing();
            return;
        }
#endif

        switch (currentState)
        {
            case TracingState.Idle:
            case TracingState.NearOrb:
                UpdateIdleAndNearOrb();
                break;

            case TracingState.Tracing:
                UpdateTracing();
                break;
        }
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by SceneManager after companion audio finishes
    public void Activate()
    {
        SetState(TracingState.Idle);
        Debug.Log("[TracingSystem] Activated - point ray at start orb and hold trigger");
    }

    // Called by SceneManager when Try Again button is pressed
    public void ResetTrace()
    {
        foreach (LineRenderer seg in trailSegments)
        {
            if (seg != null)
            {
                if (seg.material != null)
                    Destroy(seg.material);
                Destroy(seg.gameObject);
            }
        }
        trailSegments.Clear();
        currentSegment = null;

        elapsedTime              = 0f;
        triggerHeld              = false;
        hasLastPosition          = false;
        hasPreviousControllerPos = false;

        if (metricsCollector != null)
            metricsCollector.Reset();

        SetState(TracingState.Idle);
        Debug.Log("[TracingSystem] Reset complete - ready for new attempt");
    }

    // ── INPUT CALLBACKS ───────────────────────────────────────────

    private void OnTriggerPerformed(InputAction.CallbackContext context)
    {
        triggerHeld = true;

        if (currentState == TracingState.NearOrb)
            StartTracing();
    }

    private void OnTriggerCanceled(InputAction.CallbackContext context)
    {
        triggerHeld = false;

        if (currentState == TracingState.Tracing)
        {
            Debug.Log("[TracingSystem] Trigger released mid trace - FAILED");
            HandleFail(FailReason.TriggerReleased);
        }
    }

    // ── STATE UPDATE METHODS ──────────────────────────────────────

    private void UpdateIdleAndNearOrb()
    {
        Vector3 hitPosition;
        bool rayHitsPlane = CastRayToPlane(out hitPosition);

        if (!rayHitsPlane)
        {
            if (currentState == TracingState.NearOrb)
            {
                beatPathManager.SetOrbHoverState(false);
                SetState(TracingState.Idle);
            }
            return;
        }

        bool nearOrb = beatPathManager.IsNearStartOrb(hitPosition);

        if (nearOrb && currentState != TracingState.NearOrb)
        {
            SetState(TracingState.NearOrb);
            beatPathManager.SetOrbHoverState(true);
            Debug.Log("[TracingSystem] Ray near start orb - hold trigger to begin");
        }
        else if (!nearOrb && currentState == TracingState.NearOrb)
        {
            beatPathManager.SetOrbHoverState(false);
            SetState(TracingState.Idle);
        }
    }

    private void UpdateTracing()
    {
        elapsedTime += Time.deltaTime;
        if (elapsedTime >= timeoutSeconds)
        {
            Debug.Log("[TracingSystem] 30 second timeout - FAILED");
            HandleFail(FailReason.Timeout);
            return;
        }

        Vector3 hitPosition;
        bool rayHitsPlane = CastRayToPlane(out hitPosition);
        if (!rayHitsPlane) return;

        // Calculate deviation from ideal path using nearest point on spline
        Vector3 nearestOnSpline = beatPathManager.GetNearestPointOnSpline(hitPosition);
        float deviation = Vector3.Distance(hitPosition, nearestOnSpline);

        // Calculate controller velocity for speed consistency metric
        Vector3 velocity = Vector3.zero;
        if (hasPreviousControllerPos)
            velocity = (rayOriginTransform.position - previousControllerPosition) / Time.deltaTime;
        previousControllerPosition = rayOriginTransform.position;
        hasPreviousControllerPos   = true;

        // Only draw new point if hand moved enough
        // Filters out micro-movements that create noisy jagged trail
        float minMoveDistance = 0.01f;
        if (!hasLastPosition || Vector3.Distance(hitPosition, lastHitPosition) >= minMoveDistance)
            DrawTrailPoint(hitPosition, deviation);

        // Send frame data to metrics collector
        metricsCollector.RecordFrame(hitPosition, deviation, velocity);

        // Check if player reached end point
        if (beatPathManager.IsNearEndPoint(hitPosition))
        {
            Debug.Log("[TracingSystem] End point reached - SUCCESS");
            HandleSuccess();
        }
    }

    // ── TRAIL DRAWING ─────────────────────────────────────────────

    private void DrawTrailPoint(Vector3 position, float deviation)
    {
        Color targetColor = GetColorForDeviation(deviation);

        bool needNewSegment = currentSegment == null ||
                              !hasLastPosition ||
                              currentSegment.startColor != targetColor;

        if (needNewSegment)
        {
            currentSegment = CreateTrailSegment(targetColor);
            currentSegment.SetPosition(0, hasLastPosition ? lastHitPosition : position);
            currentSegment.SetPosition(1, position);
        }
        else
        {
            int count = currentSegment.positionCount;
            currentSegment.positionCount = count + 1;
            currentSegment.SetPosition(count, position);
        }

        lastHitPosition = position;
        hasLastPosition = true;
    }

    private LineRenderer CreateTrailSegment(Color color)
    {
        GameObject segObj = new GameObject("TrailSegment");
        segObj.transform.SetParent(trailContainer.transform);

        LineRenderer lr = segObj.AddComponent<LineRenderer>();

        // Sprites/Default shader always respects lr.startColor and lr.endColor
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white;

        lr.material      = mat;
        lr.startColor    = color;
        lr.endColor      = color;
        lr.startWidth    = trailWidth;
        lr.endWidth      = trailWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = true;

        trailSegments.Add(lr);
        return lr;
    }

    private Color GetColorForDeviation(float deviation)
    {
        if (deviation <= closeThreshold)
            return colorClose;

        if (deviation <= mediumThreshold)
            return Color.Lerp(colorClose, colorFar,
                (deviation - closeThreshold) / (mediumThreshold - closeThreshold));

        return colorFar;
    }

    // ── RAY CASTING ───────────────────────────────────────────────

    private bool CastRayToPlane(out Vector3 hitPosition)
    {
        hitPosition = Vector3.zero;

        if (rayOriginTransform == null || beatPathManager == null)
            return false;

        Transform planeTransform = beatPathManager.GetTracingPlane();
        if (planeTransform == null)
            return false;

        Ray ray = new Ray(rayOriginTransform.position, rayOriginTransform.forward);
        Plane tracingPlane = new Plane(-planeTransform.forward, planeTransform.position);

        float distance;
        if (tracingPlane.Raycast(ray, out distance))
        {
            hitPosition = ray.GetPoint(distance);
            return true;
        }

        return false;
    }

    // ── END STATE HANDLERS ────────────────────────────────────────

    private void HandleSuccess()
    {
        SetState(TracingState.Finished);
        beatPathManager.ShowEndOrb();

        MetricsResult result = metricsCollector.GetResult(true);
        OnTraceSuccess?.Invoke(result);
    }

    private void HandleFail(FailReason reason)
    {
        SetState(TracingState.Finished);

        MetricsResult result = metricsCollector.GetResult(false);
        result.failReason = reason;

        OnTraceFailed?.Invoke(reason);
    }

    // ── INTERNAL HELPERS ──────────────────────────────────────────

    private void StartTracing()
    {
        SetState(TracingState.Tracing);

        elapsedTime              = 0f;
        hasLastPosition          = false;
        hasPreviousControllerPos = false;

        metricsCollector.Reset();
        metricsCollector.StartRecording(beatPathManager.GetIctusPositions());

        beatPathManager.DeactivateStartOrb();

        // Notify SceneManager that tracing has begun
        // SceneManager uses this to set state to Tracing
        OnTraceStarted?.Invoke();

        Debug.Log("[TracingSystem] Tracing started");
    }

    private void SetupInputActions()
    {
        if (inputActions == null)
        {
            Debug.LogError("[TracingSystem] InputActionAsset not assigned in Inspector");
            return;
        }

        triggerAction = inputActions
            .FindActionMap("XRI RightHand Interaction")
            .FindAction("Activate");

        if (triggerAction == null)
            Debug.LogError("[TracingSystem] Could not find Activate in XRI RightHand Interaction");
        else
            Debug.Log("[TracingSystem] Trigger action found successfully");
    }

    private void SetState(TracingState newState)
    {
        Debug.Log($"[TracingSystem] State: {currentState} → {newState}");
        currentState = newState;
    }
}