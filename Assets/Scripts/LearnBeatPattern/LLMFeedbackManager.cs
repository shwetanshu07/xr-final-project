using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

// Sends metrics to Groq LLM API after successful trace attempts.
// Displays AI generated coaching feedback in the feedback panel.
// On API failure falls back to hardcoded feedback automatically.
//
// SETUP:
// 1. Get free API key from console.groq.com
// 2. Assign key in Inspector
// 3. Assign feedbackPanel reference in Inspector
// 4. Called by SceneManager after successful trace only

public class LLMFeedbackManager : MonoBehaviour
{
    [Header("API Settings")]
    // Get your free key from console.groq.com
    // Do not commit this to version control or share publicly
    [SerializeField] private string apiKey = "";

    [Header("References")]
    // FeedbackPanel script reference
    // LLM response is sent to feedbackPanel.SetLLMFeedback()
    [SerializeField] private FeedbackPanel feedbackPanel;

    [Header("Scoring Weights")]
    // Controls how much each metric contributes to overall score
    // Must add up to 1.0
    [SerializeField] private float pathAccuracyWeight     = 0.35f;
    [SerializeField] private float ictusAccuracyWeight    = 0.40f;
    [SerializeField] private float speedConsistencyWeight = 0.25f;

    // Groq API endpoint and model
    // Groq uses OpenAI compatible format
    private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";
    private const string MODEL   = "llama-3.3-70b-versatile";

    // Prevents multiple simultaneous requests
    private bool isRequesting = false;

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by SceneManager after successful trace only
    // pattern = which beat pattern was being practiced
    public void RequestFeedback(MetricsResult result, BeatPattern pattern)
    {
        if (isRequesting)
        {
            Debug.LogWarning("[LLMFeedback] Request already in progress");
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[LLMFeedback] API key not set in Inspector");
            if (feedbackPanel != null)
                feedbackPanel.SetLLMFeedback("API key not configured.");
            return;
        }

        StartCoroutine(SendRequest(result, pattern));
    }

    // ── PRIVATE METHODS ───────────────────────────────────────────

    private IEnumerator SendRequest(MetricsResult result, BeatPattern pattern)
    {
        isRequesting = true;

        string prompt   = BuildPrompt(result, pattern);
        string jsonBody = BuildRequestBody(prompt);

        Debug.Log("[LLMFeedback] Sending request to Groq");

        using (UnityWebRequest request = new UnityWebRequest(API_URL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Groq uses OpenAI compatible auth headers
            request.SetRequestHeader("Content-Type",  "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseJson = request.downloadHandler.text;
                string feedback     = ParseResponse(responseJson);

                if (feedbackPanel != null)
                    feedbackPanel.SetLLMFeedback(feedback);

                Debug.Log($"[LLMFeedback] Response received successfully");
            }
            else
            {
                Debug.LogError($"[LLMFeedback] Request failed: {request.error}");
                Debug.LogError($"[LLMFeedback] Response body: {request.downloadHandler.text}");

                // On failure show fallback so player still gets some feedback
                if (feedbackPanel != null)
                    feedbackPanel.SetLLMFeedback(BuildFallbackFeedback(result));
            }
        }

        isRequesting = false;
    }

    // Builds the complete prompt with all metric values
    // LLM uses these values to give specific actionable feedback
    private string BuildPrompt(MetricsResult result, BeatPattern pattern)
    {
        float overallScore        = CalculateWeightedScore(result);
        string patternName        = GetPatternName(pattern);
        string patternDescription = GetPatternDescription(pattern);
        string ictusDistances     = BuildIctusDistanceString(result);

        return
            "You are an expert orchestral conducting instructor evaluating a student's " +
            "conducting gesture in a VR training application.\n\n" +

            $"The student was practicing the {patternName} beat pattern.\n" +
            $"Pattern description: {patternDescription}\n\n" +

            "ATTEMPT RESULT: Completed successfully\n" +
            $"DURATION: {result.traceDurationSeconds:F1} seconds\n" +
            $"FRAMES RECORDED: {result.totalFramesRecorded}\n\n" +

            "PATH ACCURACY:\n" +
            $"  Average deviation from ideal path: {result.averagePathDeviation * 100f:F1}cm\n" +
            $"  Maximum deviation at any point: {result.maxPathDeviation * 100f:F1}cm\n" +
            $"  Percentage of trace within 5cm of path: {result.percentageWithinGoodRange:F1}%\n\n" +

            "BEAT POSITION ACCURACY:\n" +
            $"  Beat positions hit correctly: {result.ictusHits} out of {result.ictusTotal}\n" +
            "  Note: A beat position requires both proximity AND a downstroke-to-upstroke direction reversal\n" +
            $"  Closest distance to each beat position: {ictusDistances}\n\n" +

            "SPEED CONSISTENCY:\n" +
            $"  Mean hand speed: {result.meanSpeed:F3} m/s\n" +
            $"  Speed standard deviation: {result.speedStdDev:F3}\n" +
            $"  Coefficient of variation: {result.speedCV:F3} (lower = more consistent)\n" +
            $"  Consistency score: {result.speedConsistencyScore:F0}%\n\n" +

            $"WEIGHTED OVERALL SCORE: {overallScore:F0}/100\n" +
            $"  Path accuracy {pathAccuracyWeight * 100f:F0}%, " +
            $"Beat positions {ictusAccuracyWeight * 100f:F0}%, " +
            $"Speed {speedConsistencyWeight * 100f:F0}%\n\n" +

            "Please evaluate this performance and provide:\n" +
            "1. An overall assessment in 1-2 sentences\n" +
            "2. The single most important thing to improve with specific actionable advice\n" +
            "3. One thing they did well\n" +
            $"4. Confirm the weighted score: {overallScore:F0}/100\n\n" +

            "IMPORTANT FORMATTING RULES:\n" +
            "Plain text only, no markdown, no asterisks, no bullet symbols.\n" +
            "Maximum 120 words total.\n" +
            "This displays in a VR headset so keep it concise.\n" +
            "Be encouraging but honest.";
    }

    // Builds JSON request body in OpenAI compatible format
    // Groq accepts the same format as OpenAI
    private string BuildRequestBody(string prompt)
    {
        string escapedPrompt = prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        return $"{{" +
               $"\"model\":\"{MODEL}\"," +
               $"\"max_tokens\":300," +
               $"\"messages\":[{{\"role\":\"user\",\"content\":\"{escapedPrompt}\"}}]" +
               $"}}";
    }

    // Parses Groq/OpenAI response format
    // Response structure: choices[0].message.content
    private string ParseResponse(string jsonResponse)
    {
        // Groq uses OpenAI format - look for "content":"..."
        // Skip the first "content" which is in the request echo if present
        int contentIndex = jsonResponse.IndexOf("\"content\":");
        if (contentIndex == -1)
        {
            Debug.LogError("[LLMFeedback] Could not find content in response");
            Debug.LogError($"[LLMFeedback] Full response: {jsonResponse}");
            return "Could not parse response.";
        }

        // Find opening quote of the content value
        int startQuote = jsonResponse.IndexOf("\"", contentIndex + 10);
        if (startQuote == -1) return "Parse error.";

        // Find closing quote handling escaped quotes inside the content
        int endQuote = startQuote + 1;
        while (endQuote < jsonResponse.Length)
        {
            if (jsonResponse[endQuote] == '"' && jsonResponse[endQuote - 1] != '\\')
                break;
            endQuote++;
        }

        string rawText = jsonResponse.Substring(startQuote + 1, endQuote - startQuote - 1);

        // Unescape escape sequences from JSON
        return rawText
            .Replace("\\n", "\n")
            .Replace("\\r", "")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    // Calculates weighted overall score from individual metric scores
    private float CalculateWeightedScore(MetricsResult result)
    {
        float deviationScore = Mathf.Clamp01(
            1f - (result.averagePathDeviation / 0.10f)) * 100f;

        return (deviationScore               * pathAccuracyWeight)  +
               (result.ictusAccuracyPercent  * ictusAccuracyWeight) +
               (result.speedConsistencyScore * speedConsistencyWeight);
    }

    // Builds readable per-ictus distance string for the prompt
    // Gives LLM specific info on which beat positions were missed
    private string BuildIctusDistanceString(MetricsResult result)
    {
        if (result.closestDistancePerIctus == null ||
            result.closestDistancePerIctus.Length == 0)
            return "not available";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < result.closestDistancePerIctus.Length; i++)
        {
            float distCm = result.closestDistancePerIctus[i] * 100f;
            sb.Append($"Beat {i + 1}: {distCm:F1}cm");
            if (i < result.closestDistancePerIctus.Length - 1)
                sb.Append(", ");
        }
        return sb.ToString();
    }

    // Returns human readable pattern name for the prompt
    private string GetPatternName(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:   return "2/4 (Two Beat)";
            case BeatPattern.ThreeBeat: return "3/4 (Three Beat)";
            case BeatPattern.FourBeat:  return "4/4 (Four Beat)";
            default:                    return "Unknown";
        }
    }

    // Returns gesture description for each pattern
    // Gives LLM context on what correct technique looks like
    private string GetPatternDescription(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:
                return "The baton starts top right, sweeps DOWN to the bottom " +
                       "(beat 1 ictus), then curves UP and LEFT to the top left " +
                       "(beat 2 ictus). The ictus is the moment of direction " +
                       "reversal at the bottom of the downstroke.";

            case BeatPattern.ThreeBeat:
                return "The baton starts top, sweeps DOWN (beat 1 ictus), " +
                       "moves RIGHT-OUT (beat 2 ictus), then sweeps UP " +
                       "(beat 3 ictus). Three distinct direction changes required.";

            case BeatPattern.FourBeat:
                return "The baton starts top, sweeps DOWN (beat 1 ictus), " +
                       "moves LEFT (beat 2 ictus), moves RIGHT (beat 3 ictus), " +
                       "then sweeps UP (beat 4 ictus). Four distinct direction " +
                       "changes required forming a cross shape.";

            default:
                return "Unknown pattern.";
        }
    }

    // Fallback feedback shown if API call fails
    // Ensures player always gets some feedback even without internet
    private string BuildFallbackFeedback(MetricsResult result)
    {
        if (result == null)
            return "Feedback unavailable - check your connection.";

        float deviationCm = result.averagePathDeviation * 100f;

        string text =
            "Feedback service unavailable.\n\n" +
            $"Path accuracy: {deviationCm:F1}cm average deviation\n" +
            $"Beat positions: {result.ictusHits}/{result.ictusTotal} hit\n" +
            $"Speed consistency: {result.speedConsistencyScore:F0}%\n\n";

        if (!string.IsNullOrEmpty(result.mainIssue))
            text += $"Focus on: {result.mainIssue}\n\n";

        text += "(Check your internet connection for AI feedback)";

        return text;
    }
}