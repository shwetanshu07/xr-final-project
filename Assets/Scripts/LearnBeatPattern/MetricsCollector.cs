using UnityEngine;
using System.Collections.Generic;

// Records metrics data every frame during a trace attempt.
// Called by TracingSystem each frame while tracing.
// Calculates final scores when GetResult() is called.
//
// HOW TO ADD A NEW METRIC:
// 1. Add private storage fields below
// 2. Add recording logic in RecordFrame()
// 3. Add reset logic in Reset()
// 4. Add a CalculateMetricN() method
// 5. Call it from GetResult()
// 6. Add the result fields to MetricsResult in BeatPatternTypes.cs

public class MetricsCollector : MonoBehaviour
{
    // ── TUNING PARAMETERS ─────────────────────────────────────────
    // All exposed in Inspector so scoring can be adjusted without code changes

    [Header("Speed Consistency Tuning")]
    // CV of 0 = perfectly consistent speed = 100%
    // CV >= maxCVForSpeedScore = 0%
    // Lower value = stricter, Higher value = more lenient
    [SerializeField] private float maxCVForSpeedScore = 1.0f;

    [Header("Deviation Scoring Tuning")]
    // Deviation >= maxDeviationForScore maps to score of 0%
    // Lower = stricter, Higher = more lenient
    [SerializeField] private float maxDeviationForScore = 0.10f;

    [Header("Ictus Detection Tuning")]
    // Radius in metres within which controller must be to count as ictus hit
    [SerializeField] private float ictusHitRadius = 0.05f;

    // ── RAW DATA STORAGE ──────────────────────────────────────────

    // Metric 1 - path deviation in metres per frame
    private List<float> deviationSamples = new List<float>();

    // Metric 1 - additional tracking for richer LLM context
    private float maxDeviationRecorded = 0f;
    private int framesWithinGoodRange  = 0;
    private float goodRangeThreshold   = 0.05f;

    // Metric 2 - ictus positions to check against
    private Vector3[] ictusPositions;

    // Metric 2 - closest distance controller got to each ictus point
    // Proximity only - no direction reversal required
    private float[] closestDistanceToIctus;

    // Metric 3 - controller speed in metres/second per frame
    private List<float> speedSamples = new List<float>();

    // Overall tracking
    private float traceStartTime = 0f;

    // Whether recording is currently active
    private bool isRecording = false;

    // ── PUBLIC METHODS ────────────────────────────────────────────

    // Called by TracingSystem when tracing begins
    public void StartRecording(Vector3[] ictusWorldPositions)
    {
        Reset();
        ictusPositions = ictusWorldPositions;

        closestDistanceToIctus = new float[ictusPositions.Length];
        for (int i = 0; i < closestDistanceToIctus.Length; i++)
            closestDistanceToIctus[i] = float.MaxValue;

        traceStartTime = Time.time;
        isRecording    = true;

        Debug.Log($"[MetricsCollector] Recording started - tracking {ictusPositions.Length} ictus points");
    }

    // Called by TracingSystem every frame while tracing
    // tipPosition  = ray hit position on tracing plane
    // deviation    = distance from hit position to nearest spline point in metres
    // tipVelocity  = controller velocity vector in world space
    public void RecordFrame(Vector3 tipPosition, float deviation, Vector3 tipVelocity)
    {
        if (!isRecording) return;

        float speed = tipVelocity.magnitude;

        // ── Metric 1 - Path Deviation ─────────────────────────────

        deviationSamples.Add(deviation);

        if (deviation > maxDeviationRecorded)
            maxDeviationRecorded = deviation;

        if (deviation <= goodRangeThreshold)
            framesWithinGoodRange++;

        // ── Metric 2 - Ictus Accuracy (proximity only) ────────────

        if (ictusPositions != null)
        {
            for (int i = 0; i < ictusPositions.Length; i++)
            {
                float distToIctus = Vector3.Distance(tipPosition, ictusPositions[i]);
                if (distToIctus < closestDistanceToIctus[i])
                    closestDistanceToIctus[i] = distToIctus;
            }
        }

        // ── Metric 3 - Speed Consistency ──────────────────────────

        speedSamples.Add(speed);
    }

    // Resets all recorded data ready for a fresh attempt
    public void Reset()
    {
        deviationSamples.Clear();
        speedSamples.Clear();

        ictusPositions         = null;
        closestDistanceToIctus = null;

        maxDeviationRecorded  = 0f;
        framesWithinGoodRange = 0;
        traceStartTime        = 0f;
        isRecording           = false;

        Debug.Log("[MetricsCollector] Reset");
    }

    // Calculates and returns final metrics result
    public MetricsResult GetResult(bool isSuccess)
    {
        isRecording = false;

        MetricsResult result = new MetricsResult();
        result.isSuccess = isSuccess;

        result.traceDurationSeconds = Time.time - traceStartTime;
        result.totalFramesRecorded  = deviationSamples.Count;

        CalculateMetric1(result);
        CalculateMetric2(result);
        CalculateMetric3(result);

        result.mainIssue = DetermineMainIssue(result);

        Debug.Log($"[MetricsCollector] Result - " +
                  $"Deviation: {result.averagePathDeviation * 100f:F1}cm, " +
                  $"Ictus: {result.ictusHits}/{result.ictusTotal}, " +
                  $"Speed: {result.speedConsistencyScore:F0}%, " +
                  $"Duration: {result.traceDurationSeconds:F1}s");

        return result;
    }

    // ── PRIVATE CALCULATIONS ──────────────────────────────────────

    // Metric 1 - Average path deviation
    private void CalculateMetric1(MetricsResult result)
    {
        if (deviationSamples.Count == 0)
        {
            result.averagePathDeviation      = 0f;
            result.maxPathDeviation          = 0f;
            result.percentageWithinGoodRange = 0f;
            return;
        }

        float total = 0f;
        foreach (float d in deviationSamples)
            total += d;

        result.averagePathDeviation      = total / deviationSamples.Count;
        result.maxPathDeviation          = maxDeviationRecorded;
        result.percentageWithinGoodRange = (float)framesWithinGoodRange / deviationSamples.Count * 100f;
    }

    // Metric 2 - Ictus point accuracy
    // Proximity only - controller must get within ictusHitRadius of each ictus point
    private void CalculateMetric2(MetricsResult result)
    {
        if (closestDistanceToIctus == null || closestDistanceToIctus.Length == 0)
        {
            result.ictusHits               = 0;
            result.ictusTotal              = 0;
            result.ictusAccuracyPercent    = 0f;
            result.closestDistancePerIctus = new float[0];
            return;
        }

        int hits = 0;
        foreach (float dist in closestDistanceToIctus)
        {
            if (dist <= ictusHitRadius)
                hits++;
        }

        result.ictusHits               = hits;
        result.ictusTotal              = closestDistanceToIctus.Length;
        result.ictusAccuracyPercent    = (float)hits / closestDistanceToIctus.Length * 100f;
        result.closestDistancePerIctus = (float[])closestDistanceToIctus.Clone();
    }

    // Metric 3 - Speed consistency using Coefficient of Variation
    private void CalculateMetric3(MetricsResult result)
    {
        if (speedSamples.Count < 2)
        {
            result.speedConsistencyScore = 100f;
            result.meanSpeed             = 0f;
            result.speedStdDev           = 0f;
            result.speedCV               = 0f;
            return;
        }

        float minMeaningfulSpeed      = 0.1f;
        List<float> meaningfulSamples = new List<float>();
        foreach (float s in speedSamples)
        {
            if (s >= minMeaningfulSpeed)
                meaningfulSamples.Add(s);
        }

        if (meaningfulSamples.Count < 5)
        {
            result.speedConsistencyScore = 50f;
            result.meanSpeed             = 0f;
            result.speedStdDev           = 0f;
            result.speedCV               = 0f;
            return;
        }

        float mean = 0f;
        foreach (float s in meaningfulSamples)
            mean += s;
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

        result.speedConsistencyScore = Mathf.Clamp01(1f - (cv / maxCVForSpeedScore)) * 100f;
    }

    private string DetermineMainIssue(MetricsResult result)
    {
        float deviationScore = Mathf.Clamp01(1f - (result.averagePathDeviation / maxDeviationForScore)) * 100f;

        float worstScore = Mathf.Min(
            deviationScore,
            result.ictusAccuracyPercent,
            result.speedConsistencyScore
        );

        if (worstScore == deviationScore)
            return $"Try to stay closer to the guide path. " +
                   $"Average distance was {result.averagePathDeviation * 100f:F1}cm.";

        if (worstScore == result.ictusAccuracyPercent)
            return $"You hit {result.ictusHits} out of {result.ictusTotal} beat positions. " +
                   $"Try to pass through the marked points.";

        return "Try to keep your tracing speed more consistent throughout the gesture.";
    }
}