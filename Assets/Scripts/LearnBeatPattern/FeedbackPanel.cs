using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

// Controls the feedback panel shown after each trace attempt.
// MetricsText always shows raw metrics.
// LLMFeedbackText shows AI coaching on success only.
// On fail LLMFeedbackText is hidden entirely.

public class FeedbackPanel : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject feedbackCanvas;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI resultHeading;
    [SerializeField] private TextMeshProUGUI metricsText;

    // Separate text field for LLM coaching
    // Only shown on success - hidden on fail
    [SerializeField] private TextMeshProUGUI llmFeedbackText;

    [SerializeField] private Button tryAgainButton;

    [Header("Heading Colors")]
    [SerializeField] private Color successColor = new Color(0.1f, 0.6f, 0.1f);
    [SerializeField] private Color failColor    = new Color(0.7f, 0.1f, 0.1f);

    // SceneManager subscribes to this
    public event Action OnTryAgainPressed;

    // Prevents double click from XR ray interactor
    private bool isProcessingClick = false;

    void Awake()
    {
        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(HandleTryAgainClicked);
    }

    void OnDestroy()
    {
        if (tryAgainButton != null)
            tryAgainButton.onClick.RemoveAllListeners();
    }

    // Shows panel with result and metrics
    // Always shows raw metrics
    // On success shows loading state in LLM field (LLMFeedbackManager updates it)
    // On fail hides LLM field entirely
    public void Show(MetricsResult result)
    {
        if (feedbackCanvas == null)
        {
            Debug.LogWarning("[FeedbackPanel] FeedbackCanvas not assigned");
            return;
        }

        // Always update result heading
        UpdateResultHeading(result);

        // Always show raw metrics regardless of success or fail
        metricsText.text = BuildMetricsText(result);

        if (result.isSuccess)
        {
            // Show LLM field with loading state
            // LLMFeedbackManager will update this text when response arrives
            if (llmFeedbackText != null)
            {
                llmFeedbackText.gameObject.SetActive(true);
                llmFeedbackText.text = "Analysing your performance...";
            }
        }
        else
        {
            // Hide LLM field on fail - no AI feedback for failed attempts
            if (llmFeedbackText != null)
                llmFeedbackText.gameObject.SetActive(false);
        }

        feedbackCanvas.SetActive(true);
        Debug.Log("[FeedbackPanel] Panel shown");
    }

    // Called by LLMFeedbackManager when API response arrives
    // Only called on success attempts
    public void SetLLMFeedback(string feedback)
    {
        if (llmFeedbackText != null)
        {
            llmFeedbackText.text = feedback;
            Debug.Log("[FeedbackPanel] LLM feedback set");
        }
    }

    // Hides the panel and resets LLM text
    public void Hide()
    {
        if (feedbackCanvas != null)
            feedbackCanvas.SetActive(false);

        // Reset LLM text for next attempt
        if (llmFeedbackText != null)
        {
            llmFeedbackText.text = "";
            llmFeedbackText.gameObject.SetActive(false);
        }

        isProcessingClick = false;
        Debug.Log("[FeedbackPanel] Panel hidden");
    }

    // ── PRIVATE HELPERS ───────────────────────────────────────────

    private void UpdateResultHeading(MetricsResult result)
    {
        if (resultHeading == null) return;

        if (result.isSuccess)
        {
            resultHeading.text  = "Success!";
            resultHeading.color = successColor;
        }
        else
        {
            string reason = result.failReason == FailReason.Timeout
                ? "Time ran out"
                : "Trigger released";
            resultHeading.text  = $"Failed — {reason}";
            resultHeading.color = failColor;
        }
    }

    // Builds raw metrics text - always shown regardless of success or fail
    private string BuildMetricsText(MetricsResult result)
    {
        float deviationCm      = result.averagePathDeviation * 100f;
        string deviationRating = GetDeviationRating(deviationCm);
        string speedRating     = GetSpeedRating(result.speedConsistencyScore);

        string text = "";

        text += $"• Path Accuracy: {deviationRating} ({deviationCm:F1}cm avg)\n\n";

        if (result.ictusTotal > 0)
            text += $"• Beat Positions: {result.ictusHits}/{result.ictusTotal} hit ({result.ictusAccuracyPercent:F0}%)\n\n";
        else
            text += "• Beat Positions: Not tracked\n\n";

        text += $"• Speed Consistency: {speedRating} ({result.speedConsistencyScore:F0}%)";

        return text;
    }

    private string GetDeviationRating(float deviationCm)
    {
        if (deviationCm <= 5f)  return "Excellent";
        if (deviationCm <= 10f) return "Good";
        return "Needs Work";
    }

    private string GetSpeedRating(float score)
    {
        if (score >= 80f) return "Excellent";
        if (score >= 60f) return "Good";
        return "Needs Work";
    }

    private void HandleTryAgainClicked()
    {

        Debug.Log("[FeedbackPanel] Before check Try Again pressed");
        if (isProcessingClick) return;

        isProcessingClick = true;
        Debug.Log("[FeedbackPanel] Try Again pressed");
        OnTryAgainPressed?.Invoke();

        StartCoroutine(ResetClickFlag());
    }

    private System.Collections.IEnumerator ResetClickFlag()
    {
        yield return new WaitForSeconds(0.3f);
        isProcessingClick = false;
    }
}