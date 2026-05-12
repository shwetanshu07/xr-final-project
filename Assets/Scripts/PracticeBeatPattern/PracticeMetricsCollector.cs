using UnityEngine;
using System.Collections.Generic;

// Practice mode metrics collector.
// Detects ictus points by velocity direction reversal (not proximity).
// Passes velocity data to DirectionalSegmentAnalyser after trace ends.
// No path deviation metric — no guide path in practice mode.

public class PracticeMetricsCollector : MonoBehaviour
{
    [Header("Speed Consistency Tuning")]
    [SerializeField] private float maxCVForSpeedScore = 1.0f;

    [Header("Ictus Detection Tuning")]
    // Minimum downward velocity to count as a downstroke
    [SerializeField] private float downstrokeThreshold = -0.05f;
    // Minimum upward velocity to count as start of upstroke
    [SerializeField] private float upstrokeThreshold   =  0.05f;

    // ── RAW DATA STORAGE ──────────────────────────────────────────

    // Velocity samples per frame — used for speed consistency and direction analysis
    private List<Vector3> velocitySamples = new List<Vector3>();

    // Speed magnitude per frame — used for CV calculation
    private List<float> speedSamples = new List<float>();

    // Frame indices where direction reversals (ictus) were detected
    // These define segment boundaries for DirectionalSegmentAnalyser
    private List<int> ictusFrameIndices = new List<int>();

    // Ictus detection state
    private float previousVelocityY  = 0f;
    private bool  hasPreviousVelocity = false;
    private int   frameCount          = 0;

    // Overall tracking
    private float traceStartTime = 0f;
    private bool  isRecording    = false;

    // ── PUBLIC METHODS ────────────────────────────────────────────

    public void StartRecording()
    {
        Reset();
        traceStartTime = Time.time;
        isRecording    = true;
        Debug.Log("[PracticeMetricsCollector] Recording started");
    }

    // Called every frame during tracing
    // tipVelocity = controller velocity in world space
    public void RecordFrame(Vector3 tipVelocity)
    {
        if (!isRecording) return;

        float speed = tipVelocity.magnitude;
        velocitySamples.Add(tipVelocity);
        speedSamples.Add(speed);

        // Detect direction reversal — was moving down, now moving up
        // This is what defines an ictus in conducting
        if (hasPreviousVelocity)
        {
            bool wasMovingDown = previousVelocityY < downstrokeThreshold;
            bool nowMovingUp   = tipVelocity.y > upstrokeThreshold;

            if (wasMovingDown && nowMovingUp)
            {
                ictusFrameIndices.Add(frameCount);
                Debug.Log($"[PracticeMetricsCollector] Ictus detected at frame {frameCount}");
            }
        }

        previousVelocityY  = tipVelocity.y;
        hasPreviousVelocity = true;
        frameCount++;
    }

    public void Reset()
    {
        velocitySamples.Clear();
        speedSamples.Clear();
        ictusFrameIndices.Clear();

        previousVelocityY  = 0f;
        hasPreviousVelocity = false;
        frameCount          = 0;
        traceStartTime      = 0f;
        isRecording         = false;

        Debug.Log("[PracticeMetricsCollector] Reset");
    }

    public MetricsResult GetResult(BeatPattern pattern)
    {
        isRecording = false;

        MetricsResult result = new MetricsResult();
        result.isSuccess            = true; // practice mode always succeeds
        result.traceDurationSeconds = Time.time - traceStartTime;
        result.totalFramesRecorded  = velocitySamples.Count;

        // Ictus count — how many direction reversals detected
        result.ictusHits            = ictusFrameIndices.Count;
        result.ictusTotal           = GetExpectedIctusCount(pattern);
        result.ictusAccuracyPercent = result.ictusTotal > 0
            ? Mathf.Clamp01((float)result.ictusHits / result.ictusTotal) * 100f
            : 0f;

        // Directional segment analysis
        DirectionalSegmentAnalyser.Analyse(
            velocitySamples,
            ictusFrameIndices,
            pattern,
            result
        );

        // Speed consistency — reuse same CV approach as Learn mode
        CalculateSpeedConsistency(result);

        // No path deviation in practice mode
        result.averagePathDeviation      = 0f;
        result.maxPathDeviation          = 0f;
        result.percentageWithinGoodRange = 0f;

        result.mainIssue = DetermineMainIssue(result);

        Debug.Log($"[PracticeMetricsCollector] Result - " +
                  $"Ictus: {result.ictusHits}/{result.ictusTotal}, " +
                  $"DirectionalAccuracy: {result.directionalAccuracyPercent:F0}%, " +
                  $"Speed: {result.speedConsistencyScore:F0}%");

        return result;
    }

    // ── PRIVATE ───────────────────────────────────────────────────

    private int GetExpectedIctusCount(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:   return 2;
            case BeatPattern.ThreeBeat: return 3;
            case BeatPattern.FourBeat:  return 4;
            default:                    return 0;
        }
    }

    private void CalculateSpeedConsistency(MetricsResult result)
    {
        float minMeaningfulSpeed      = 0.1f;
        List<float> meaningfulSamples = new List<float>();
        foreach (float s in speedSamples)
            if (s >= minMeaningfulSpeed)
                meaningfulSamples.Add(s);

        if (meaningfulSamples.Count < 5)
        {
            result.speedConsistencyScore = 50f;
            result.meanSpeed             = 0f;
            result.speedStdDev           = 0f;
            result.speedCV               = 0f;
            return;
        }

        float mean = 0f;
        foreach (float s in meaningfulSamples) mean += s;
        mean /= meaningfulSamples.Count;

        float variance = 0f;
        foreach (float s in meaningfulSamples)
            variance += (s - mean) * (s - mean);
        variance /= meaningfulSamples.Count;
        float stdDev = Mathf.Sqrt(variance);

        float cv = mean > 0 ? stdDev / mean : 1f;

        result.meanSpeed   = mean;
        result.speedStdDev = stdDev;
        result.speedCV     = cv;

        result.speedConsistencyScore =
            Mathf.Clamp01(1f - (cv / maxCVForSpeedScore)) * 100f;
    }

    private string DetermineMainIssue(MetricsResult result)
    {
        if (result.directionalAccuracyPercent < 60f)
            return $"Focus on the direction of each stroke. " +
                   $"{result.correctDirectionalSegments} of " +
                   $"{result.totalDirectionalSegments} strokes were in the correct direction.";

        if (result.ictusHits < result.ictusTotal)
            return $"You made {result.ictusHits} clear direction changes but " +
                   $"{result.ictusTotal} are needed. Make each reversal more decisive.";

        return "Try to keep your arm speed more consistent throughout the gesture.";
    }
}