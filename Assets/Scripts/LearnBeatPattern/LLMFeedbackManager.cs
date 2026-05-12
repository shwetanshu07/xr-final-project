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
    // LLM response sent to feedbackPanel.SetStoredAIFeedback(attemptIndex, feedback)
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
    private const string MODEL   = "openai/gpt-oss-120b";

    // private const string MODEL   = "llama-3.3-70b-versatile";

    // Prevents multiple simultaneous requests
    private bool isRequesting = false;

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by SceneManager after successful trace only
    // attemptIndex = 0-based index of the attempt (attemptCount - 1)
    // Used so LLM response updates the correct attempt slot in FeedbackPanel
    public void RequestFeedback(MetricsResult result, BeatPattern pattern, int attemptIndex = 0)
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
                feedbackPanel.SetStoredAIFeedback(attemptIndex, "API key not configured.");
            return;
        }

        // Pass attemptIndex through to SendRequest so response targets correct slot
        StartCoroutine(SendRequest(result, pattern, attemptIndex));
    }

    // ── PRIVATE METHODS ───────────────────────────────────────────

    // attemptIndex passed through so SetStoredAIFeedback targets correct attempt
    private IEnumerator SendRequest(MetricsResult result, BeatPattern pattern, int attemptIndex)
    {
        // // temporary
        // if (feedbackPanel != null)
        //     feedbackPanel.SetStoredAIFeedback(attemptIndex, "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry's standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book. It has survived not only five centuries, but also the leap into electronic typesetting, remaining essentially unchanged. It was popularised in the 1960s with the release of Letraset sheets containing Lorem Ipsum passages, and more recently with desktop publishing software like Aldus PageMaker including versions of Lorem Ipsum.");
        // yield break;
        
        isRequesting = true;

        string prompt   = BuildPrompt(result, pattern);
        string jsonBody = BuildRequestBody(prompt);

        Debug.Log($"[LLMFeedback] Sending request to Groq for attempt {attemptIndex}");

        using (UnityWebRequest request = new UnityWebRequest(API_URL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type",  "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseJson = request.downloadHandler.text;
                string feedback     = ParseResponse(responseJson);

                // Update the correct attempt slot in FeedbackPanel
                if (feedbackPanel != null)
                    feedbackPanel.SetStoredAIFeedback(attemptIndex, feedback);

                Debug.Log($"[LLMFeedback] Response received for attempt {attemptIndex}");
                Debug.Log($"[LLMFeedback] {feedback}");
            }
            else
            {
                Debug.LogError($"[LLMFeedback] Request failed: {request.error}");
                Debug.LogError($"[LLMFeedback] Response body: {request.downloadHandler.text}");

                if (feedbackPanel != null)
                    feedbackPanel.SetStoredAIFeedback(attemptIndex, BuildFallbackFeedback(result));
            }
        }

        isRequesting = false;
    }

    // Builds the complete prompt with all metric values
    // LLM uses these values to give specific actionable feedback
    // private string BuildPrompt(MetricsResult result, BeatPattern pattern)
    // {
    //     float overallScore        = CalculateWeightedScore(result);
    //     string patternName        = GetPatternName(pattern);
    //     string patternDescription = GetPatternDescription(pattern);
    //     string ictusDistances     = BuildIctusDistanceString(result);

    //     return
    //         "You are an expert orchestral conducting instructor evaluating a student's " +
    //         "conducting gesture in a VR training application.\n\n" +

    //         $"The student was practicing the {patternName} beat pattern.\n" +
    //         $"Pattern description: {patternDescription}\n\n" +

    //         "ATTEMPT RESULT: Completed successfully\n" +
    //         $"DURATION: {result.traceDurationSeconds:F1} seconds\n" +
    //         $"FRAMES RECORDED: {result.totalFramesRecorded}\n\n" +

    //         "PATH ACCURACY:\n" +
    //         $"  Average deviation from ideal path: {result.averagePathDeviation * 100f:F1}cm\n" +
    //         $"  Maximum deviation at any point: {result.maxPathDeviation * 100f:F1}cm\n" +
    //         $"  Percentage of trace within 5cm of path: {result.percentageWithinGoodRange:F1}%\n\n" +

    //         "BEAT POSITION ACCURACY:\n" +
    //         $"  Beat positions hit correctly: {result.ictusHits} out of {result.ictusTotal}\n" +
    //         "  Note: A beat position requires proximity to the beat position point\n" +
    //         $"  Closest distance to each beat position: {ictusDistances}\n\n" +

    //         "SPEED CONSISTENCY:\n" +
    //         $"  Mean hand speed: {result.meanSpeed:F3} m/s\n" +
    //         $"  Speed standard deviation: {result.speedStdDev:F3}\n" +
    //         $"  Coefficient of variation: {result.speedCV:F3} (lower = more consistent)\n" +
    //         $"  Consistency score: {result.speedConsistencyScore:F0}%\n\n" +

    //         $"WEIGHTED OVERALL SCORE: {overallScore:F0}/100\n" +
    //         $"Path accuracy {pathAccuracyWeight * 100f:F0}%, " +
    //         $"Beat positions {ictusAccuracyWeight * 100f:F0}%, " +
    //         $"Speed {speedConsistencyWeight * 100f:F0}%\n\n" +

    //         "Please evaluate this performance and provide:\n" +
    //         "1. An overall assessment in 1-2 sentences\n" +
    //         "2. The single most important thing to improve with specific actionable advice\n" +
    //         "3. One thing they did well\n" +
    //         $"4. Confirm the weighted score: {overallScore:F0}/100\n\n" +

    //         "IMPORTANT FORMATTING RULES:\n" +
    //         "Plain text only, no markdown, no asterisks, no bullet symbols.\n" +
    //         "Maximum 120 words total.\n" +
    //         "This displays in a VR headset so keep it concise.\n" +
    //         "Be encouraging but honest.";
    // }

    private string BuildPrompt(MetricsResult result, BeatPattern pattern)
    {
        float overallScore        = CalculateWeightedScore(result);
        string patternName        = GetPatternName(pattern);
        string patternDescription = GetPatternDescription(pattern);
        string ictusDistances     = BuildIctusDistanceString(result);

        return
            "You are an expert orchestral conducting instructor giving feedback to a beginner " +
            "student practicing in a VR conducting trainer. Be specific, encouraging, and practical. " +
            "The student is learning from scratch — avoid advanced terminology.\n\n" +

            $"PATTERN PRACTICED: {patternName}\n" +
            $"What the correct gesture looks like: {patternDescription}\n\n" +

            $"DURATION: {result.traceDurationSeconds:F1} seconds\n" +
            $"FRAMES RECORDED: {result.totalFramesRecorded}\n\n" +

            "PATH ACCURACY:\n" +
            $"  Average deviation from ideal path: {result.averagePathDeviation * 100f:F1}cm\n" +
            $"  Maximum deviation at any point: {result.maxPathDeviation * 100f:F1}cm\n" +
            $"  Percentage of trace within 5cm of path: {result.percentageWithinGoodRange:F1}%\n\n" +

            "BEAT POSITION ACCURACY:\n" +
            $"  Beat positions hit correctly: {result.ictusHits} out of {result.ictusTotal}\n" +
            $"  How close they got to each beat position: {ictusDistances}\n" +
            "  Note: A beat position is where the hand must reverse direction (the ictus point)\n\n\n" +

            "SPEED CONSISTENCY:\n" +
            $"  Mean hand speed: {result.meanSpeed:F3} m/s\n" +
            $"  Speed standard deviation: {result.speedStdDev:F3}\n" +
            $"  Coefficient of variation: {result.speedCV:F3} (lower = more consistent)\n" +
            $"  Consistency score: {result.speedConsistencyScore:F0}%\n\n" +

            $"OVERALL SCORE: {overallScore:F0}/100\n" +
            $"  (Path {pathAccuracyWeight * 100f:F0}% + Beat positions {ictusAccuracyWeight * 100f:F0}% + Speed {speedConsistencyWeight * 100f:F0}%)\n\n" +

            "Please provide feedback structured as follows:\n\n" +

            "OVERALL: Write 2-3 sentences summarising the attempt and " +
            "whether this is a strong or weak result for a beginner.\n\n" +

            "MAIN ISSUE: Identify the single weakest area (path accuracy, beat positions, or speed). " +
            "Explain in plain language WHY it matters for conducting and give ONE specific, " +
            "concrete action the student should try on their next attempt. Be very specific — " +
            "do not say just improve your path, say something like slow down on the upstroke " +
            "and focus on curving back toward the centre.\n\n" +

            "STRENGTH: Identify the strongest area and explain what the student did well. " +
            "Be specific using the numbers — for example if their speed CV was low say " +
            "your arm speed was very consistent throughout the gesture.\n\n" +
 
            "NEXT ATTEMPT TIP: One short sentence of encouragement and focus for the next try.\n\n" +

            "FORMATTING RULES:\n" +
            "Plain text only. No markdown. No asterisks. No bullet points. No numbered lists.\n" +
            "Use the section labels OVERALL, MAIN ISSUE, STRENGTH, NEXT ATTEMPT TIP as plain text headers.\n" +
            "Maximum 200 words total. Display in VR headset so keep paragraphs short.";
    }

    // Builds JSON request body in OpenAI compatible format
    private string BuildRequestBody(string prompt)
    {
        string escapedPrompt = prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        return $"{{" +
               $"\"model\":\"{MODEL}\"," +
               $"\"max_tokens\":500," +
               $"\"messages\":[{{\"role\":\"user\",\"content\":\"{escapedPrompt}\"}}]" +
               $"}}";
    }

    // Parses Groq/OpenAI response format
    // Response structure: choices[0].message.content
    private string ParseResponse(string jsonResponse)
    {
        int contentIndex = jsonResponse.IndexOf("\"content\":");
        if (contentIndex == -1)
        {
            Debug.LogError("[LLMFeedback] Could not find content in response");
            Debug.LogError($"[LLMFeedback] Full response: {jsonResponse}");
            return "Could not parse response.";
        }

        int startQuote = jsonResponse.IndexOf("\"", contentIndex + 10);
        if (startQuote == -1) return "Parse error.";

        int endQuote = startQuote + 1;
        while (endQuote < jsonResponse.Length)
        {
            if (jsonResponse[endQuote] == '"' && jsonResponse[endQuote - 1] != '\\')
                break;
            endQuote++;
        }

        string rawText = jsonResponse.Substring(startQuote + 1, endQuote - startQuote - 1);

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

    // Called by PracticeSceneManager instead of RequestFeedback
    public void RequestPracticeFeedback(MetricsResult result, BeatPattern pattern, int attemptIndex = 0)
    {
        if (isRequesting) return;
        if (string.IsNullOrEmpty(apiKey))
        {
            if (feedbackPanel != null)
                feedbackPanel.SetStoredAIFeedback(attemptIndex, "API key not configured.");
            return;
        }
        StartCoroutine(SendPracticeRequest(result, pattern, attemptIndex));
    }

    private IEnumerator SendPracticeRequest(MetricsResult result, BeatPattern pattern, int attemptIndex)
    {
        isRequesting = true;

        string prompt   = BuildPracticePrompt(result, pattern);
        string jsonBody = BuildRequestBody(prompt);

        using (UnityWebRequest request = new UnityWebRequest(API_URL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type",  "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            string feedback = request.result == UnityWebRequest.Result.Success
                ? ParseResponse(request.downloadHandler.text)
                : BuildFallbackFeedback(result);

            if (feedbackPanel != null)
                feedbackPanel.SetStoredAIFeedback(attemptIndex, feedback);
        }

        isRequesting = false;
    }

    private string BuildPracticePrompt(MetricsResult result, BeatPattern pattern)
    {
        float score = (result.directionalAccuracyPercent * 0.50f) +
                    (result.ictusAccuracyPercent        * 0.25f) +
                    (result.speedConsistencyScore       * 0.25f);

        string segmentDetails = "";
        if (result.segmentDirectionCorrect != null)
        {
            for (int i = 0; i < result.segmentDirectionCorrect.Length; i++)
                segmentDetails += $"  Stroke {i+1}: {(result.segmentDirectionCorrect[i] ? "correct" : "incorrect")}\n";
        }

        return
            "You are an expert orchestral conducting instructor evaluating a student's " +
            "free-form conducting gesture in VR practice mode. " +
            "There was no guide path — the student drew freely from memory.\n\n" +

            $"Pattern: {GetPatternName(pattern)}\n" +
            $"Duration: {result.traceDurationSeconds:F1} seconds\n\n" +

            "DIRECTIONAL ACCURACY:\n" +
            $"  Correct strokes: {result.correctDirectionalSegments} of {result.totalDirectionalSegments}\n" +
            $"  Accuracy: {result.directionalAccuracyPercent:F0}%\n" +
            $"  Per stroke:\n{segmentDetails}\n" +

            "BEAT POSITIONS:\n" +
            $"  Direction changes detected: {result.ictusHits} (expected {result.ictusTotal})\n\n" +

            "SPEED CONSISTENCY:\n" +
            $"  Consistency score: {result.speedConsistencyScore:F0}%\n\n" +

            $"OVERALL SCORE: {score:F0}/100\n\n" +

            "OVERALL: Write 2-3 sentences summarising the attempt. \n\n" +

            "MAIN ISSUE: Identify the single weakest area (path accuracy, beat positions, or speed). " +
            "Explain in plain language WHY it matters for conducting and give ONE specific, " +
            "concrete action the student should try on their next attempt. Be very specific — " +
            "do not say just improve your path, say something like slow down on the upstroke " +
            "and focus on curving back toward the centre.\n\n" +

            "STRENGTH: Identify the strongest area and explain what the student did well. " +
            "Be specific using the numbers — for example if their speed CV was low say " +
            "your arm speed was very consistent throughout the gesture.\n\n" +

            "NEXT ATTEMPT TIP: One short sentence of encouragement and focus for the next try.\n\n" +

            "FORMATTING RULES:\n" +
            "Plain text only. No markdown. No asterisks. No bullet points. No numbered lists.\n" +
            "Use the section labels OVERALL, MAIN ISSUE, FIX, STRENGTH, NEXT ATTEMPT TIP as plain text headers.\n" +
            "Maximum 200 words total. Display in VR headset so keep paragraphs short.";
    }
}