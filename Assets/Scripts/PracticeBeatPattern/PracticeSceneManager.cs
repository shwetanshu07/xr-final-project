using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Master controller for the Practice Beat Pattern scene.
// Same flow as LearnBeatPatternSceneManager but:
// - No guide path shown
// - Trigger release = gesture complete (not fail)
// - Adaptive timer based on previous score
// - Always immediate feedback, unlimited attempts
// - Uses directional segment analysis for scoring

public class PracticeSceneManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private ScoreStandUI        scoreStandUI;
    [SerializeField] private SpatialLabelManager spatialLabel;

    [Header("Path")]
    [SerializeField] private BeatPathManager beatPathManager;

    [Header("Tracing")]
    [SerializeField] private PracticeTracingSystem tracingSystem;

    [Header("Metrics")]
    [SerializeField] private PracticeMetricsCollector metricsCollector;

    [Header("Feedback")]
    [SerializeField] private FeedbackPanel       feedbackPanel;
    [SerializeField] private LLMFeedbackManager  llmFeedbackManager;

    [Header("Minimap")]
    [SerializeField] private PracticeMinimapGenerator minimapGenerator;

    [Header("Adaptive Timer Settings")]
    // Base timeout in seconds
    [SerializeField] private float baseTimeout    = 30f;
    // Score above this → increase timeout to maxTimeout
    [SerializeField] private float highScoreThreshold = 70f;
    // Score below this → decrease timeout to minTimeout
    [SerializeField] private float lowScoreThreshold  = 40f;
    [SerializeField] private float maxTimeout     = 60f;
    [SerializeField] private float minTimeout     = 15f;

    private float currentTimeout;

    public enum SceneState
    {
        Loading, BeatSelection, CompanionIntro, TraceReady, Tracing,
        TraceComplete, Exiting
    }

    private SceneState  currentState    = SceneState.Loading;
    private BeatPattern selectedPattern = BeatPattern.None;
    private int         attemptCount    = 0;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Start()
    {
        SubscribeToEvents();
        StartCoroutine(InitialiseScene());
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ── INITIALISATION ────────────────────────────────────────────

    private IEnumerator InitialiseScene()
    {
        SetState(SceneState.Loading);
        yield return new WaitForSeconds(0.1f);

        currentTimeout = baseTimeout;
        attemptCount   = 0;

        scoreStandUI.ShowBeatSelection();
        scoreStandUI.ShowTryAgain(false);

        if (feedbackPanel != null)
        {
            feedbackPanel.SetMode(false); // immediate mode
            feedbackPanel.ClearSession();
        }

        SetState(SceneState.BeatSelection);
    }

    // ── EVENT SUBSCRIPTIONS ───────────────────────────────────────

    private void SubscribeToEvents()
    {
        scoreStandUI.OnBeatSelected    += HandleBeatSelected;
        tracingSystem.OnTraceComplete  += HandleTraceComplete;
        tracingSystem.OnTraceStarted   += HandleTraceStarted;
    }

    private void UnsubscribeFromEvents()
    {
        scoreStandUI.OnBeatSelected    -= HandleBeatSelected;
        tracingSystem.OnTraceComplete  -= HandleTraceComplete;
        tracingSystem.OnTraceStarted   -= HandleTraceStarted;
    }

    // ── EVENT HANDLERS ────────────────────────────────────────────

    private void HandleBeatSelected(BeatPattern pattern)
    {
        Debug.Log($"[SceneManager] HandleBeatSelected {pattern} currentState:{currentState}");
        if (currentState != SceneState.BeatSelection) return;
        selectedPattern = pattern;
        StartCoroutine(BeatSelectedSequence(pattern));
    }

    private void HandleTraceComplete(MetricsResult result, BeatPattern pattern)
    {
        if (currentState != SceneState.Tracing) return;
        StartCoroutine(CompleteSequence(result, pattern));
    }

    private void HandleTraceStarted()
    {
        SetState(SceneState.Tracing);
    }

    public void HandleTryAgainFromButton()
    {
        if (currentState != SceneState.TraceComplete) return;
        StartCoroutine(ResetForNewAttempt());
    }

    // ── SEQUENCES ─────────────────────────────────────────────────

    private IEnumerator BeatSelectedSequence(BeatPattern pattern)
    {
        Debug.Log($"[SceneBeatDebug] BeatSelectedSequence starting for {pattern}");
        SetState(SceneState.CompanionIntro);

        scoreStandUI.ShowBeatInfo(pattern);
        Debug.Log($"[SceneBeatDebug] ShowBeatInfo called for {pattern}");
        yield return new WaitForSeconds(0.2f);

        // Show path to build SplineContainer but immediately hide guide line
        beatPathManager.ShowPath(pattern);
        beatPathManager.HideGuidePath();
        yield return new WaitForSeconds(0.2f);

        yield return new WaitForSeconds(2f); // placeholder for audio

        // Set timeout on tracing system
        tracingSystem.SetTimeout(currentTimeout);
        tracingSystem.Activate(pattern);

        spatialLabel.SetText($"Attempt {attemptCount + 1} — Hold trigger and draw the {GetPatternName(pattern)} pattern");
        SetState(SceneState.TraceReady);
    }

    private IEnumerator CompleteSequence(MetricsResult result, BeatPattern pattern)
    {
        SetState(SceneState.TraceComplete);
        attemptCount++;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySuccess();
        yield return new WaitForSeconds(0.3f);

        spatialLabel.SetText("Gesture complete!");
        yield return new WaitForSeconds(0.2f);

        // Generate minimap
        Texture2D minimap = minimapGenerator.Generate();

        // Calculate overall practice score for adaptive timer
        float overallScore = CalculatePracticeScore(result);

        // Adapt timer for next attempt
        UpdateAdaptiveTimer(overallScore, result);

        // Show feedback
        feedbackPanel.ShowImmediatePractice(result, minimap, attemptCount);
        llmFeedbackManager.RequestPracticeFeedback(result, pattern, attemptCount - 1);

        scoreStandUI.ShowTryAgain(true);

        Debug.Log($"[PracticeSceneManager] Attempt {attemptCount} complete. " +
                  $"Score: {overallScore:F0} Next timeout: {currentTimeout}s");
    }

    private IEnumerator ResetForNewAttempt()
    {
        feedbackPanel.Hide();
        yield return new WaitForSeconds(0.1f);

        tracingSystem.ResetTrace();
        yield return new WaitForSeconds(0.1f);

        scoreStandUI.ShowTryAgain(false);

        // Update timeout for next attempt
        tracingSystem.SetTimeout(currentTimeout);

        spatialLabel.SetText($"Attempt {attemptCount + 1} — Hold trigger and draw the {GetPatternName(selectedPattern)} pattern");
        SetState(SceneState.TraceReady);
    }

    // ── ADAPTIVE TIMER ────────────────────────────────────────────

    private void UpdateAdaptiveTimer(float score, MetricsResult result)
    {
        float previousTimeout = currentTimeout;

        if (score >= highScoreThreshold)
        {
            currentTimeout = maxTimeout;
            Debug.Log($"[PracticeSceneManager] Score {score:F0} >= {highScoreThreshold} → timeout increased to {maxTimeout}s");
        }
        else if (score < lowScoreThreshold)
        {
            currentTimeout = minTimeout;
            Debug.Log($"[PracticeSceneManager] Score {score:F0} < {lowScoreThreshold} → timeout decreased to {minTimeout}s");
        }
        else
        {
            currentTimeout = baseTimeout;
            Debug.Log($"[PracticeSceneManager] Score {score:F0} → timeout stays at {baseTimeout}s");
        }

        // Store in result for LLM context
        result.nextAttemptTimeoutSeconds = currentTimeout;

        if (currentTimeout != previousTimeout)
            spatialLabel.SetText($"Timer adjusted to {currentTimeout}s based on your performance");
    }

    private float CalculatePracticeScore(MetricsResult result)
    {
        return (result.directionalAccuracyPercent * 0.50f) +
               (result.ictusAccuracyPercent        * 0.25f) +
               (result.speedConsistencyScore       * 0.25f);
    }

    private string GetPatternName(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:   return "2/4";
            case BeatPattern.ThreeBeat: return "3/4";
            default:                    return "pattern";
        }
    }

    private void SetState(SceneState newState)
    {
        Debug.Log($"[PracticeSceneManager] {currentState} → {newState}");
        currentState = newState;
    }
}