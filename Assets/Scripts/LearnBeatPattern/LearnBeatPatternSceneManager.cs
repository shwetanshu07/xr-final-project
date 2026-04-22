using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Master controller for the Learn Beat Pattern scene.
// Orchestrates all systems - does not implement detail logic itself.
// Each system (tracing, metrics, UI) is a separate script.
// This script wires them together and controls the scene flow.

public class LearnBeatPatternSceneManager : MonoBehaviour
{
    // ── SYSTEM REFERENCES ─────────────────────────────────────────
    // Assign each in the Inspector once the script is created.
    // Each TODO shows which topic creates that script.

    [Header("UI")]
    [SerializeField] private ScoreStandUI scoreStandUI;
    [SerializeField] private SpatialLabelManager spatialLabel;

    // TODO Topic 4 - uncomment when CompanionController.cs is created
    // [Header("Companion")]
    // [SerializeField] private CompanionController companion;

    [Header("Path")]
    [SerializeField] private BeatPathManager beatPathManager;

    [Header("Tracing")]
    [SerializeField] private TracingSystem tracingSystem;

    [Header("Metrics")]
    [SerializeField] private MetricsCollector metricsCollector;

    [Header("Feedback")]
    [SerializeField] private FeedbackPanel feedbackPanel;

    [Header("LLM")]
    [SerializeField] private LLMFeedbackManager llmFeedbackManager;

    // TODO Topic 9 - uncomment when InputManager.cs is created
    // [Header("Input")]
    // [SerializeField] private InputManager inputManager;

    // ── SCENE STATE ───────────────────────────────────────────────
    // Tracks what the scene is currently doing.
    // Always use SetState() to change state, never set directly.

    public enum SceneState
    {
        Loading,        // scene just loaded
        BeatSelection,  // user selecting a beat pattern on canvas 1
        CompanionIntro, // companion playing audio intro
        TraceReady,     // orb active, waiting for user to start
        Tracing,        // user actively tracing
        TraceSuccess,   // trace completed successfully
        TraceFailed,    // trace failed - trigger released or timeout
        Exiting         // going back to main menu
    }

    private SceneState currentState = SceneState.Loading;

    // Which beat pattern the user picked
    private BeatPattern selectedPattern = BeatPattern.None;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Start()
    {
        StartCoroutine(InitialiseScene());
    }

    void OnEnable()
    {
        SubscribeToEvents();
    }

    void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    // ── INITIALISATION ────────────────────────────────────────────

    private IEnumerator InitialiseScene()
    {
        SetState(SceneState.Loading);

        // Small delay to let all other scripts initialise first
        yield return new WaitForSeconds(0.1f);

        scoreStandUI.ShowBeatSelection();

        // TODO Topic 4 - replace with companion.PlayWelcomeAudio()
        Debug.Log("[SceneManager] Play welcome audio");

        SetState(SceneState.BeatSelection);
    }

    // ── EVENT SUBSCRIPTIONS ───────────────────────────────────────
    // Uncomment each line when its script is created

    private void SubscribeToEvents()
    {
        scoreStandUI.OnBeatSelected += HandleBeatSelected;
        tracingSystem.OnTraceSuccess += HandleTraceSuccess;
        tracingSystem.OnTraceFailed += HandleTraceFailed;
        feedbackPanel.OnTryAgainPressed += HandleTryAgain;
        tracingSystem.OnTraceStarted += HandleTraceStarted;

        // TODO Topic 9 - inputManager.OnAButtonPressed += HandleAButtonPressed;
    }

    private void UnsubscribeFromEvents()
    {
        scoreStandUI.OnBeatSelected -= HandleBeatSelected;
        tracingSystem.OnTraceSuccess -= HandleTraceSuccess;
        tracingSystem.OnTraceFailed -= HandleTraceFailed;
        feedbackPanel.OnTryAgainPressed -= HandleTryAgain;
        tracingSystem.OnTraceStarted -= HandleTraceStarted;

        // TODO Topic 9 - inputManager.OnAButtonPressed -= HandleAButtonPressed;
    }

    // ── EVENT HANDLERS ────────────────────────────────────────────
    // Each validates current state then starts the right sequence

    // Called when user clicks a beat pattern button
    private void HandleBeatSelected(BeatPattern pattern)
    {
        if (currentState != SceneState.BeatSelection) return;
        selectedPattern = pattern;
        StartCoroutine(BeatSelectedSequence(pattern));
    }

    // Called when TracingSystem detects successful trace
    private void HandleTraceSuccess(MetricsResult result)
    {
        if (currentState != SceneState.Tracing) return;
        StartCoroutine(SuccessSequence(result));
    }

    // Called when TracingSystem detects failed trace
    private void HandleTraceFailed(FailReason reason)
    {
        if (currentState != SceneState.Tracing) return;
        StartCoroutine(FailSequence(reason));
    }

    // Called when A button pressed - works from any state except Exiting
    private void HandleAButtonPressed()
    {
        if (currentState == SceneState.Exiting) return;
        StartCoroutine(ExitToMainMenu());
    }

    // Called when Try Again button pressed on feedback panel
    private void HandleTryAgain()
    {
        StartCoroutine(ResetForNewAttempt());
    }

    // Called when TracingSystem begins active tracing
    // Updates SceneManager state so success and fail handlers are unblocked
    private void HandleTraceStarted()
    {
        SetState(SceneState.Tracing);
        Debug.Log("[SceneManager] Tracing begun - state set to Tracing");
    }

    // ── SEQUENCES ─────────────────────────────────────────────────
    // Each sequence is a coroutine defining the order of events.
    // To change event order - reorder the yield statements.
    // To change delay between events - change WaitForSeconds values.

    // Triggered when user selects a beat pattern
    // Order: canvas swap → companion appears → path appears → audio → orb activates
    private IEnumerator BeatSelectedSequence(BeatPattern pattern)
    {
        SetState(SceneState.CompanionIntro);

        // 1. Swap to canvas 2 showing beat info
        scoreStandUI.ShowBeatInfo(pattern);
        yield return new WaitForSeconds(0.2f);

        // 2. Show companion character
        // TODO Topic 4 - replace with companion.Show()
        Debug.Log("[SceneManager] Show companion");
        yield return new WaitForSeconds(0.3f);

        // 3. Show beat path in the air
        beatPathManager.ShowPath(pattern);
        yield return new WaitForSeconds(0.2f);

        // 4. Play companion audio - waits until clip finishes
        // TODO Topic 4 - replace with: yield return companion.PlayPatternIntroAudio(pattern)
        Debug.Log($"[SceneManager] Play companion audio for {pattern}");
        yield return new WaitForSeconds(2f); // placeholder - replace with real audio length

        // 5. Activate start orb only after audio finishes so user is ready
        beatPathManager.ActivateStartOrb();
        tracingSystem.Activate();

        // 6. Update spatial label
        spatialLabel.SetText("Point at the orb and hold trigger to trace");

        SetState(SceneState.TraceReady);
    }

    // Triggered on successful trace
    // Order: success sound → label update → feedback panel → companion audio
    private IEnumerator SuccessSequence(MetricsResult result)
    {
        SetState(SceneState.TraceSuccess);

        // 1. Play success sound
        // Null check - AudioManager may not be in scene during testing
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySuccess();
        yield return new WaitForSeconds(0.3f);

        // 2. Update spatial label
        spatialLabel.SetText("Success! You can try again");
        yield return new WaitForSeconds(0.2f);

        // 3. Show feedback panel with metrics
        feedbackPanel.Show(result);
        yield return new WaitForSeconds(0.2f);

        // Call LLM feedback on success only
        llmFeedbackManager.RequestFeedback(result, selectedPattern);

        // 4. Play companion success audio
        // TODO Topic 4 - replace with companion.PlaySuccessAudio()
        Debug.Log("[SceneManager] Play companion success audio");
    }

    // Triggered on failed trace
    // Order: fail sound → label update → feedback panel → companion audio
    private IEnumerator FailSequence(FailReason reason)
    {
        SetState(SceneState.TraceFailed);

        // 1. Play fail sound
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayFail();
        Debug.Log($"[SceneManager] Play fail sound. Reason: {reason}");
        yield return new WaitForSeconds(0.3f);

        // 2. Update spatial label
        spatialLabel.SetText("Failed. Go to start and try again");
        yield return new WaitForSeconds(0.2f);

        // 3. Get metrics and show feedback panel
        MetricsResult result = metricsCollector.GetResult(false);
        feedbackPanel.Show(result);
        yield return new WaitForSeconds(0.2f);

        // 4. Play companion fail audio
        // TODO Topic 4 - replace with companion.PlayFailAudio()
        Debug.Log("[SceneManager] Play companion fail audio");
    }

    // Triggered when Try Again button pressed
    // Order: hide panel → clear trail → reset metrics → reactivate orb → reset label
    private IEnumerator ResetForNewAttempt()
    {
        // 1. Hide feedback panel
        feedbackPanel.Hide();
        Debug.Log("[SceneManager] Hide feedback panel");
        yield return new WaitForSeconds(0.1f);

        // 2. Clear the traced path trail
        tracingSystem.ResetTrace();
        Debug.Log("[SceneManager] Reset trace");
        yield return new WaitForSeconds(0.1f);

        // 4. Reactivate start orb
        beatPathManager.ActivateStartOrb();
        yield return new WaitForSeconds(0.1f);

        // 5. Reset spatial label text
        spatialLabel.SetText("Point at the orb and hold trigger to trace");

        SetState(SceneState.TraceReady);
    }

    // Exit to main menu - simple instant scene switch for now
    // TODO - if fade is added later, call transitionManager.FadeOut() before LoadScene
    private IEnumerator ExitToMainMenu()
    {
        SetState(SceneState.Exiting);
        yield return null;
        SceneManager.LoadScene("MainMenu");
    }

    // ── HELPERS ───────────────────────────────────────────────────

    // Always use this to change state - never set currentState directly
    private void SetState(SceneState newState)
    {
        Debug.Log($"[SceneManager] {currentState} → {newState}");
        currentState = newState;
    }
}