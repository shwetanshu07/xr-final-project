using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

// Controls the feedback panel shown after trace attempts.
// Supports two modes:
//   Immediate: shows after each attempt, no Prev/Next buttons
//   Delayed:   shows all attempts together at end, with Prev/Next navigation
//
// SETUP:
// 1. FeedbackCanvas → always active
// 2. FeedbackBackground → starts inactive, toggled by Show/Hide
// 3. feedbackBackground field → assign FeedbackBackground
// 4. All other fields → assign in Inspector

public class FeedbackPanel : MonoBehaviour
{
    [Header("Panel Root")]
    // Assign FeedbackBackground here (NOT FeedbackCanvas)
    [SerializeField] private GameObject feedbackBackground;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI attemptLabel;
    [SerializeField] private RawImage        minimapImage;
    [SerializeField] private TextMeshProUGUI metricsText;
    [SerializeField] private TextMeshProUGUI aiFeedbackText;

    [Header("Navigation (Delayed Mode Only)")]
    [SerializeField] private GameObject navigationRow;
    [SerializeField] private Button     prevButton;
    [SerializeField] private Button     nextButton;

    // Event for external listeners if needed
    public event Action OnPanelClosed;

    // ── SESSION DATA ──────────────────────────────────────────────

    private List<Texture2D>     storedMinimaps = new List<Texture2D>();
    private List<MetricsResult> storedResults  = new List<MetricsResult>();
    private List<string>        storedAI       = new List<string>();

    private int  currentIndex        = 0;
    private bool isDelayedMode       = false;
    private bool isProcessingNavClick = false;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Awake()
    {
        if (feedbackBackground != null)
            feedbackBackground.SetActive(false);

        if (prevButton != null)
            prevButton.onClick.AddListener(ShowPrevAttempt);
        if (nextButton != null)
            nextButton.onClick.AddListener(ShowNextAttempt);
    }

    void OnDestroy()
    {
        if (prevButton != null) prevButton.onClick.RemoveAllListeners();
        if (nextButton != null) nextButton.onClick.RemoveAllListeners();
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by SceneManager to configure mode before session starts
    public void SetMode(bool delayedFeedback)
    {
        isDelayedMode = delayedFeedback;
        if (navigationRow != null)
            navigationRow.SetActive(false);
    }

    // Store attempt data without showing (used in delayed mode)
    public void StoreAttempt(MetricsResult result, Texture2D minimap)
    {
        storedMinimaps.Add(minimap);
        storedResults.Add(result);
        storedAI.Add("Analysing...");
        Debug.Log($"[FeedbackPanelAttemptDebug] StoreAttempt called - total stored: {storedResults.Count}");
    }

    // Update stored AI feedback for a specific attempt index
    // Called by LLMFeedbackManager when response arrives
    // Works for both immediate and delayed mode
    public void SetStoredAIFeedback(int attemptIndex, string feedback)
    {
        Debug.Log($"[FeedbackPanel] SetStoredAIFeedback - index:{attemptIndex} storedAI.Count:{storedAI.Count} backgroundActive:{feedbackBackground.activeSelf} currentIndex:{currentIndex}");

        if (attemptIndex < 0 || attemptIndex >= storedAI.Count)
        {
            Debug.LogError($"[FeedbackPanel] attemptIndex {attemptIndex} out of range — storedAI has {storedAI.Count} entries. Was ShowImmediate or StoreAttempt called first?");
            return;
        }

        storedAI[attemptIndex] = feedback;

        // If panel is visible and showing this attempt update text immediately
        if (feedbackBackground.activeSelf && currentIndex == attemptIndex)
        {
            if (aiFeedbackText != null)
                aiFeedbackText.text = feedback;
            Debug.Log("[FeedbackPanel] AI feedback text updated on screen");
        }
    }

    // Show single attempt (immediate mode — Learn scene)
    // Called after each attempt in immediate mode
    public void ShowImmediate(MetricsResult result, Texture2D minimap, int attemptNumber)
    {
        if (feedbackBackground == null) return;

        currentIndex = 0;

        // IMPORTANT: Add placeholder to storedAI so SetStoredAIFeedback can
        // update it when LLM response arrives. Without this entry the index
        // check in SetStoredAIFeedback fails and feedback never shows.
        if (storedAI.Count == 0)
            storedAI.Add("Analysing...");
        else
            storedAI[0] = "Analysing...";

        UpdateDisplay(result, minimap, "Analysing...", attemptNumber);

        if (navigationRow != null)
            navigationRow.SetActive(false);

        feedbackBackground.SetActive(true);
    }

    // Show all stored attempts (delayed mode)
    // Called after all attempts complete
    public void ShowAllAttempts()
    {
        if (feedbackBackground == null) return;
        if (storedResults.Count == 0) return;

        currentIndex = 0;
        DisplayStoredAttempt(0);

        if (navigationRow != null)
            navigationRow.SetActive(storedResults.Count > 1);

        feedbackBackground.SetActive(true);
        UpdateNavigationButtons();
    }

    // Update AI feedback text for current immediate attempt
    // Called by LLMFeedbackManager in immediate mode
    public void SetImmediateAIFeedback(string feedback)
    {
        if (aiFeedbackText != null)
            aiFeedbackText.text = feedback;
    }

    // Hides the panel
    public void Hide()
    {
        if (feedbackBackground != null)
            feedbackBackground.SetActive(false);
    }

    // Clears all stored session data - call at start of new session
    public void ClearSession()
    {
        storedMinimaps.Clear();
        storedResults.Clear();
        storedAI.Clear();
        currentIndex = 0;
        Hide();
    }

    // ── NAVIGATION ────────────────────────────────────────────────

    public void ShowNextAttempt()
    {
        if (isProcessingNavClick) return;
        isProcessingNavClick = true;
        StartCoroutine(ResetNavClickFlag());

        Debug.Log($"[AttemptDebug] ShowNext - currentIndex:{currentIndex} storedCount:{storedResults.Count}");
        if (currentIndex < storedResults.Count - 1)
        {
            currentIndex++;
            DisplayStoredAttempt(currentIndex);
            UpdateNavigationButtons();
        }
    }

    public void ShowPrevAttempt()
    {
        if (isProcessingNavClick) return;
        isProcessingNavClick = true;
        StartCoroutine(ResetNavClickFlag());

        if (currentIndex > 0)
        {
            currentIndex--;
            DisplayStoredAttempt(currentIndex);
            UpdateNavigationButtons();
        }
    }

    private System.Collections.IEnumerator ResetNavClickFlag()
    {
        yield return new WaitForSeconds(0.3f);
        isProcessingNavClick = false;
    }

    // ── PRIVATE HELPERS ───────────────────────────────────────────

    private void DisplayStoredAttempt(int index)
    {
        if (index < 0 || index >= storedResults.Count) return;

        UpdateDisplay(
            storedResults[index],
            storedMinimaps[index],
            storedAI[index],
            index + 1
        );
    }

    private void UpdateDisplay(MetricsResult result, Texture2D minimap,
                                string aiFeedback, int attemptNumber)
    {
        if (attemptLabel != null)
            attemptLabel.text = $"Attempt {attemptNumber}";

        if (minimapImage != null && minimap != null)
            minimapImage.texture = minimap;

        if (metricsText != null)
            metricsText.text = BuildMetricsText(result);

        if (aiFeedbackText != null)
            aiFeedbackText.text = aiFeedback;
    }

    private void UpdateNavigationButtons()
    {
        if (prevButton != null)
            prevButton.interactable = currentIndex > 0;
        if (nextButton != null)
            nextButton.interactable = currentIndex < storedResults.Count - 1;
    }

    private string BuildMetricsText(MetricsResult result)
    {
        float deviationCm = result.averagePathDeviation * 100f;

        string text = "";
        text += $"Path Accuracy:      {GetDeviationRating(deviationCm)} ({deviationCm:F1}cm)\n";
        text += $"Beat Positions Hit: {result.ictusHits}/{result.ictusTotal}\n";
        text += $"Speed Consistency:  {GetSpeedRating(result.speedConsistencyScore)}\n";
        text += $"Overall Score:      {CalculateOverallScore(result):F0}/100";
        return text;
    }

    private float CalculateOverallScore(MetricsResult result)
    {
        float deviationScore = Mathf.Clamp01(
            1f - (result.averagePathDeviation / 0.10f)) * 100f;
        return (deviationScore * 0.35f) +
               (result.ictusAccuracyPercent * 0.40f) +
               (result.speedConsistencyScore * 0.25f);
    }

    private string GetDeviationRating(float cm)
    {
        if (cm <= 5f)  return "Excellent";
        if (cm <= 10f) return "Good";
        return "Needs Work";
    }

    private string GetSpeedRating(float score)
    {
        if (score >= 80f) return "Excellent";
        if (score >= 60f) return "Good";
        return "Needs Work";
    }

    // ── PRACTICE MODE ─────────────────────────────────────────────

    // Called by PracticeSceneManager to show practice-specific metrics
    // Replaces path accuracy with directional accuracy
    public void ShowImmediatePractice(MetricsResult result, Texture2D minimap, int attemptNumber)
    {
        if (feedbackBackground == null) return;

        currentIndex = 0;

        // IMPORTANT: Same fix as ShowImmediate — add placeholder to storedAI
        // so SetStoredAIFeedback can update it when LLM response arrives
        if (storedAI.Count == 0)
            storedAI.Add("Analysing...");
        else
            storedAI[0] = "Analysing...";

        UpdateDisplayPractice(result, minimap, "Analysing...", attemptNumber);

        if (navigationRow != null)
            navigationRow.SetActive(false);

        feedbackBackground.SetActive(true);
    }

    private void UpdateDisplayPractice(MetricsResult result, Texture2D minimap,
                                        string aiFeedback, int attemptNumber)
    {
        if (attemptLabel != null)
            attemptLabel.text = $"Attempt {attemptNumber}";

        if (minimapImage != null && minimap != null)
            minimapImage.texture = minimap;

        if (metricsText != null)
            metricsText.text = BuildPracticeMetricsText(result);

        if (aiFeedbackText != null)
            aiFeedbackText.text = aiFeedback;
    }

    private string BuildPracticeMetricsText(MetricsResult result)
    {
        string dirRating = result.directionalAccuracyPercent >= 80f ? "Excellent" :
                           result.directionalAccuracyPercent >= 60f ? "Good" : "Needs Work";

        string speedRating = GetSpeedRating(result.speedConsistencyScore);

        string text = "";
        text += $"Stroke Direction:   {dirRating} " +
                $"({result.correctDirectionalSegments}/{result.totalDirectionalSegments} correct)\n";
        text += $"Beat Positions:     {result.ictusHits}/{result.ictusTotal} reversals\n";
        text += $"Speed Consistency:  {speedRating}\n";
        text += $"Overall Score:      {CalculatePracticeScore(result):F0}/100";
        return text;
    }

    private float CalculatePracticeScore(MetricsResult result)
    {
        return (result.directionalAccuracyPercent * 0.50f) +
               (result.ictusAccuracyPercent        * 0.25f) +
               (result.speedConsistencyScore       * 0.25f);
    }
}