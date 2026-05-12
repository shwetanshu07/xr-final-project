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
    [Header("UI")]
    [SerializeField] private ScoreStandUI scoreStandUI;
    [SerializeField] private SpatialLabelManager spatialLabel;

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

    [Header("Minimap")]
    [SerializeField] private MinimapGenerator minimapGenerator;

    [Header("Study Settings")]
    // -1 = unlimited attempts
    // Any positive number = fixed attempt count (e.g. 6 for user study)
    [SerializeField] private int maxAttempts = -1;
    // true  = show feedback after each attempt (immediate)
    // false = show all feedback together after session ends (delayed)
    [SerializeField] private bool immediateFeedback = true;


    [Header("Audio")]
    [SerializeField] private AudioClip welcomeClip;
    [SerializeField] private AudioClip twoBeatIntroClip;
    [SerializeField] private AudioClip threeBeatIntroClip;

    

    // Internal attempt tracking
    private int attemptCount = 0;
    private bool sessionComplete = false;

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
        SubscribeToEvents();
        StartCoroutine(InitialiseScene());
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // void OnEnable()
    // {
    //     SubscribeToEvents();
    // }

    // void OnDisable()
    // {
    //     UnsubscribeFromEvents();
    // }

    // ── INITIALISATION ────────────────────────────────────────────

    private IEnumerator InitialiseScene()
    {
        SetState(SceneState.Loading);

        // Small delay to let all other scripts initialise first
        yield return new WaitForSeconds(0.1f);

        scoreStandUI.ShowBeatSelection();

        attemptCount    = 0;
        sessionComplete = false;
        if (feedbackPanel != null)
        {
            feedbackPanel.SetMode(!immediateFeedback);
            feedbackPanel.ClearSession();
        }
        if (scoreStandUI != null)
            scoreStandUI.ShowTryAgain(false);

        // Play welcome audio
        if (AudioManager.Instance != null && welcomeClip != null)
        {
            AudioManager.Instance.PlayClip(welcomeClip, "welcome");
            yield return new WaitForSeconds(welcomeClip.length + 0.3f);
        }

        SetState(SceneState.BeatSelection);
    }

    // ── EVENT SUBSCRIPTIONS ───────────────────────────────────────
    // Uncomment each line when its script is created

    private void SubscribeToEvents()
    {
        scoreStandUI.OnBeatSelected += HandleBeatSelected;
        tracingSystem.OnTraceSuccess += HandleTraceSuccess;
        tracingSystem.OnTraceFailed += HandleTraceFailed;
        // feedbackPanel.OnTryAgainPressed += HandleTryAgain;
        tracingSystem.OnTraceStarted += HandleTraceStarted;

        // TODO Topic 9 - inputManager.OnAButtonPressed += HandleAButtonPressed;
    }

    private void UnsubscribeFromEvents()
    {
        scoreStandUI.OnBeatSelected -= HandleBeatSelected;
        tracingSystem.OnTraceSuccess -= HandleTraceSuccess;
        tracingSystem.OnTraceFailed -= HandleTraceFailed;
        // feedbackPanel.OnTryAgainPressed -= HandleTryAgain;
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
    public void HandleTryAgainFromButton()
    {
        if (currentState != SceneState.TraceSuccess &&
            currentState != SceneState.TraceFailed) return;

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
        // Debug.Log($"[SceneManager] Play companion audio for {pattern}");
        // yield return new WaitForSeconds(2f); // placeholder - replace with real audio length
        // Play pattern intro audio for selected pattern
        AudioClip introClip = pattern == BeatPattern.TwoBeat
            ? twoBeatIntroClip
            : threeBeatIntroClip;

        if (AudioManager.Instance != null && introClip != null)
        {
            AudioManager.Instance.PlayClip(introClip, "patternIntro");
            yield return new WaitForSeconds(introClip.length + 0.3f);
        }
        else
        {
            yield return new WaitForSeconds(1f); // fallback if no clip assigned
        }

        // 5. Activate start orb only after audio finishes so user is ready
        beatPathManager.ActivateStartOrb();
        tracingSystem.Activate();

        // 6. Update spatial label
        spatialLabel.SetText($"Attempt {attemptCount + 1} — Point at the orb and hold trigger to trace");
        // spatialLabel.SetText("Point at the orb and hold trigger to trace");

        SetState(SceneState.TraceReady);
    }

    // Triggered on successful trace
    // Order: success sound → label update → feedback panel → companion audio
    // private IEnumerator SuccessSequence(MetricsResult result)
    // {
    //     SetState(SceneState.TraceSuccess);

    //     // 1. Play success sound
    //     // Null check - AudioManager may not be in scene during testing
    //     if (AudioManager.Instance != null)
    //         AudioManager.Instance.PlaySuccess();
    //     yield return new WaitForSeconds(0.3f);

    //     // 2. Update spatial label
    //     spatialLabel.SetText("Success! You can try again");
    //     yield return new WaitForSeconds(0.2f);

    //     // 3. Show feedback panel with metrics
    //     // feedbackPanel.Show(result);
    //     yield return new WaitForSeconds(0.2f);

    //     // Call LLM feedback on success only
    //     llmFeedbackManager.RequestFeedback(result, selectedPattern);

    //     // 4. Play companion success audio
    //     // TODO Topic 4 - replace with companion.PlaySuccessAudio()
    //     Debug.Log("[SceneManager] Play companion success audio");
    // }

    // private IEnumerator SuccessSequence(MetricsResult result)
    // {
    //     SetState(SceneState.TraceSuccess);

    //     if (AudioManager.Instance != null)
    //         AudioManager.Instance.PlaySuccess();
    //     yield return new WaitForSeconds(0.3f);

    //     spatialLabel.SetText("Success! You can try again");
    //     yield return new WaitForSeconds(0.2f);

    //     // POC test - generate minimap
    //     // if (minimapPOC != null)
    //     //     minimapPOC.GenerateMinimap(
    //     //         tracingSystem.GetTracePoints(),
    //     //         tracingSystem.GetTraceColors()
    //     //     );

    //     Debug.Log("[SceneManager] Minimap generated");
    // }

    private IEnumerator SuccessSequence(MetricsResult result)
    {
        Debug.Log($"[AttemptDebug] SuccessSequence called - attemptCount before increment: {attemptCount}");
        SetState(SceneState.TraceSuccess);
        attemptCount++;
        Debug.Log($"[AttemptDebug] attemptCount after increment: {attemptCount}");

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySuccess();
        yield return new WaitForSeconds(0.3f);

        spatialLabel.SetText("Success!");
        yield return new WaitForSeconds(0.2f);

        // Generate minimap
        Texture2D minimap = minimapGenerator.Generate();

        // Check if session is complete
        bool isLastAttempt = maxAttempts > 0 && attemptCount >= maxAttempts;

        if (immediateFeedback)
        {
            // Show feedback immediately
            feedbackPanel.ShowImmediate(result, minimap, attemptCount);
            llmFeedbackManager.RequestFeedback(result, selectedPattern, attemptCount - 1);
        }
        else
        {
            // Store for later
            feedbackPanel.StoreAttempt(result, minimap);
            llmFeedbackManager.RequestFeedback(result, selectedPattern, attemptCount - 1);
        }

        if (isLastAttempt)
        {
            sessionComplete = true;
            if (!immediateFeedback)
                feedbackPanel.ShowAllAttempts();
            scoreStandUI.ShowTryAgain(false); // hide try again on last attempt
            spatialLabel.SetText("Session complete!");
        }
        else
        {
            scoreStandUI.ShowTryAgain(true);
        }

        Debug.Log($"[SceneManager] Attempt {attemptCount} complete");
    }




    // Triggered on failed trace
    // Order: fail sound → label update → feedback panel → companion audio
    // private IEnumerator FailSequence(FailReason reason)
    // {
    //     SetState(SceneState.TraceFailed);

    //     // 1. Play fail sound
    //     if (AudioManager.Instance != null)
    //         AudioManager.Instance.PlayFail();
    //     Debug.Log($"[SceneManager] Play fail sound. Reason: {reason}");
    //     yield return new WaitForSeconds(0.3f);

    //     // 2. Update spatial label
    //     spatialLabel.SetText("Failed. Go to start and try again");
    //     yield return new WaitForSeconds(0.2f);

    //     // 3. Get metrics and show feedback panel
    //     MetricsResult result = metricsCollector.GetResult(false);
    //     // feedbackPanel.Show(result);
    //     yield return new WaitForSeconds(0.2f);

    //     // 4. Play companion fail audio
    //     // TODO Topic 4 - replace with companion.PlayFailAudio()
    //     Debug.Log("[SceneManager] Play companion fail audio");
    // }

    private IEnumerator FailSequence(FailReason reason)
    {
        SetState(SceneState.TraceFailed);
        attemptCount++;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayFail();
        yield return new WaitForSeconds(0.3f);

        spatialLabel.SetText("Failed. Try again.");
        yield return new WaitForSeconds(0.2f);

        MetricsResult result = metricsCollector.GetResult(false);
        result.failReason = reason;

        Texture2D minimap = minimapGenerator.Generate();

        bool isLastAttempt = maxAttempts > 0 && attemptCount >= maxAttempts;

        if (immediateFeedback)
        {
            feedbackPanel.ShowImmediate(result, minimap, attemptCount);
            // No LLM on fail
        }
        else
        {
            feedbackPanel.StoreAttempt(result, minimap);
        }

        if (isLastAttempt)
        {
            sessionComplete = true;
            if (!immediateFeedback)
                feedbackPanel.ShowAllAttempts();
            scoreStandUI.ShowTryAgain(false);
            spatialLabel.SetText("Session complete!");
        }
        else
        {
            scoreStandUI.ShowTryAgain(true);
        }
    }

    // Triggered when Try Again button pressed
    // Order: hide panel → clear trail → reset metrics → reactivate orb → reset label
    // private IEnumerator ResetForNewAttempt()
    // {
    //     // 1. Hide feedback panel
    //     // feedbackPanel.Hide();
    //     Debug.Log("[SceneManager] Hide feedback panel");
    //     yield return new WaitForSeconds(0.1f);

    //     // 2. Clear the traced path trail
    //     tracingSystem.ResetTrace();
    //     Debug.Log("[SceneManager] Reset trace");
    //     yield return new WaitForSeconds(0.1f);

    //     // 4. Reactivate start orb
    //     beatPathManager.ActivateStartOrb();
    //     yield return new WaitForSeconds(0.1f);

    //     // 5. Reset spatial label text
    //     spatialLabel.SetText("Point at the orb and hold trigger to trace");

    //     SetState(SceneState.TraceReady);
    // }

    private IEnumerator ResetForNewAttempt()
    {
        if (sessionComplete) yield break;

        feedbackPanel.Hide();
        yield return new WaitForSeconds(0.1f);

        tracingSystem.ResetTrace();
        yield return new WaitForSeconds(0.1f);

        beatPathManager.ActivateStartOrb();
        yield return new WaitForSeconds(0.1f);

        scoreStandUI.ShowTryAgain(false); // hide until next attempt completes
        spatialLabel.SetText($"Attempt {attemptCount + 1} — Point at the orb and hold trigger to trace");
        // spatialLabel.SetText("Point at the orb and hold trigger to trace");

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