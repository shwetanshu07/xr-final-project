using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

// Practice mode tracing system.
// Key differences from TracingSystem:
// - No start orb proximity check — trigger anywhere on plane starts tracing
// - Trigger RELEASE ends the attempt and counts as a completed gesture
// - Timeout still exists as a safety fallback
// - Trail is always white/blue (no deviation colour coding)
// - Calls PracticeMetricsCollector instead of MetricsCollector

public class PracticeTracingSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BeatPathManager       beatPathManager;
    [SerializeField] private PracticeMetricsCollector metricsCollector;

    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Ray")]
    [SerializeField] private Transform rayOriginTransform;

    [Header("Trail Drawing")]
    [SerializeField] private float trailWidth = 0.01f;
    [SerializeField] private Color trailColor = Color.white;

    [Header("Timeout")]
    [SerializeField] private float timeoutSeconds = 30f;

    [Header("Editor Testing")]
    [SerializeField] private bool enableEditorShortcut = true;

    [Header("UI")]
    [SerializeField] private SpatialLabelManager spatialLabel;

    // Events fired to PracticeSceneManager
    public event Action<MetricsResult, BeatPattern> OnTraceComplete;
    public event Action                             OnTraceStarted;

    // Which pattern is currently active — needed for metrics analysis
    private BeatPattern currentPattern = BeatPattern.None;

    private enum TracingState { Inactive, Idle, Tracing, Finished }
    private TracingState currentState = TracingState.Inactive;

    private InputAction triggerAction;
    private bool        triggerHeld  = false;
    private float       elapsedTime  = 0f;

    private List<LineRenderer> trailSegments = new List<LineRenderer>();
    private LineRenderer       currentSegment;
    private Vector3            lastHitPosition;
    private bool               hasLastPosition = false;
    private GameObject         trailContainer;

    private Vector3 previousControllerPosition;
    private bool    hasPreviousControllerPos = false;

    private List<Vector3> recordedTracePoints = new List<Vector3>();
    private List<Color>   recordedTraceColors = new List<Color>();

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Awake()
    {
        SetupInputActions();
        trailContainer = new GameObject("PracticeTrailContainer");
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
            currentState == TracingState.Idle)
        {
            StartTracing();
            return;
        }
        if (enableEditorShortcut &&
            Input.GetKeyDown(KeyCode.Return) &&
            currentState == TracingState.Tracing)
        {
            HandleComplete();
            return;
        }
#endif

        switch (currentState)
        {
            case TracingState.Tracing:
                UpdateTracing();
                break;
        }
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by PracticeSceneManager after pattern selected
    public void Activate(BeatPattern pattern)
    {
        currentPattern = pattern;
        SetState(TracingState.Idle);
        Debug.Log($"[PracticeTracingSystem] Activated for {pattern} - hold trigger to draw");
    }

    // Called by PracticeSceneManager on Try Again
    public void ResetTrace()
    {
        foreach (LineRenderer seg in trailSegments)
        {
            if (seg != null)
            {
                if (seg.material != null) Destroy(seg.material);
                Destroy(seg.gameObject);
            }
        }
        trailSegments.Clear();
        currentSegment = null;

        recordedTracePoints.Clear();
        recordedTraceColors.Clear();

        elapsedTime              = 0f;
        triggerHeld              = false;
        hasLastPosition          = false;
        hasPreviousControllerPos = false;

        if (metricsCollector != null)
            metricsCollector.Reset();

        SetState(TracingState.Idle);
        Debug.Log("[PracticeTracingSystem] Reset complete");
    }

    // Set adaptive timeout from PracticeSceneManager
    public void SetTimeout(float seconds)
    {
        timeoutSeconds = seconds;
        Debug.Log($"[PracticeTracingSystem] Timeout set to {seconds}s");
    }

    public List<Vector3> GetTracePoints() => new List<Vector3>(recordedTracePoints);
    public List<Color>   GetTraceColors() => new List<Color>(recordedTraceColors);

    // ── INPUT CALLBACKS ───────────────────────────────────────────

    private void OnTriggerPerformed(InputAction.CallbackContext context)
    {
        triggerHeld = true;
        // Trigger held anywhere on plane starts tracing in practice mode
        if (currentState == TracingState.Idle)
            StartTracing();
    }

    private void OnTriggerCanceled(InputAction.CallbackContext context)
    {
        triggerHeld = false;
        // Trigger release = gesture complete in practice mode
        if (currentState == TracingState.Tracing)
        {
            Debug.Log("[PracticeTracingSystem] Trigger released - gesture complete");
            HandleComplete();
        }
    }

    // ── STATE UPDATE ──────────────────────────────────────────────

    private void UpdateTracing()
    {
        elapsedTime += Time.deltaTime;
        // Update timer display on spatial label
        float remaining = Mathf.Max(0f, timeoutSeconds - elapsedTime);
        if (spatialLabel != null)
            spatialLabel.SetText($"{remaining:F0}s");

        if (elapsedTime >= timeoutSeconds)
        {
            Debug.Log("[PracticeTracingSystem] Timeout - ending gesture");
            HandleComplete();
            return;
        }

        Vector3 hitPosition;
        if (!CastRayToPlane(out hitPosition)) return;

        // Controller velocity for metrics
        Vector3 velocity = Vector3.zero;
        if (hasPreviousControllerPos)
            velocity = (rayOriginTransform.position - previousControllerPosition) / Time.deltaTime;
        previousControllerPosition = rayOriginTransform.position;
        hasPreviousControllerPos   = true;

        // Record frame — no deviation in practice mode
        metricsCollector.RecordFrame(velocity);

        // Draw trail — fixed colour, no deviation colouring
        float minMoveDistance = 0.01f;
        if (!hasLastPosition || Vector3.Distance(hitPosition, lastHitPosition) >= minMoveDistance)
            DrawTrailPoint(hitPosition);
    }

    // ── TRAIL DRAWING ─────────────────────────────────────────────

    private void DrawTrailPoint(Vector3 position)
    {
        if (currentSegment == null || !hasLastPosition)
        {
            currentSegment = CreateTrailSegment();
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

        recordedTracePoints.Add(position);
        recordedTraceColors.Add(trailColor);
    }

    private LineRenderer CreateTrailSegment()
    {
        GameObject segObj = new GameObject("PracticeTrailSegment");
        segObj.transform.SetParent(trailContainer.transform);

        LineRenderer lr = segObj.AddComponent<LineRenderer>();
        Material mat    = new Material(Shader.Find("Sprites/Default"));
        mat.color       = Color.white;

        lr.material      = mat;
        lr.startColor    = trailColor;
        lr.endColor      = trailColor;
        lr.startWidth    = trailWidth;
        lr.endWidth      = trailWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = true;

        trailSegments.Add(lr);
        return lr;
    }

    // ── RAY CASTING ───────────────────────────────────────────────

    private bool CastRayToPlane(out Vector3 hitPosition)
    {
        hitPosition = Vector3.zero;
        if (rayOriginTransform == null || beatPathManager == null) return false;

        Transform planeTransform = beatPathManager.GetTracingPlane();
        if (planeTransform == null) return false;

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

    // ── END STATE ─────────────────────────────────────────────────

    private void HandleComplete()
    {
        SetState(TracingState.Finished);

        // Clear timer from label
        if (spatialLabel != null)
            spatialLabel.SetText("Gesture complete!");

        MetricsResult result = metricsCollector.GetResult(currentPattern);
        OnTraceComplete?.Invoke(result, currentPattern);
    }

    private void StartTracing()
    {
        SetState(TracingState.Tracing);

        elapsedTime              = 0f;
        hasLastPosition          = false;
        hasPreviousControllerPos = false;

        metricsCollector.Reset();
        metricsCollector.StartRecording();

        OnTraceStarted?.Invoke();
        Debug.Log("[PracticeTracingSystem] Tracing started");
    }

    private void SetupInputActions()
    {
        if (inputActions == null)
        {
            Debug.LogError("[PracticeTracingSystem] InputActionAsset not assigned");
            return;
        }
        triggerAction = inputActions
            .FindActionMap("XRI RightHand Interaction")
            .FindAction("Activate");

        if (triggerAction == null)
            Debug.LogError("[PracticeTracingSystem] Could not find Activate action");
    }

    private void SetState(TracingState newState)
    {
        Debug.Log($"[PracticeTracingSystem] {currentState} → {newState}");
        currentState = newState;
    }
}