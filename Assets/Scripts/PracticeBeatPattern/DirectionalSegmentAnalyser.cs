using UnityEngine;
using System.Collections.Generic;

// Analyses the directional accuracy of a conducting gesture.
// Called by PracticeMetricsCollector after trace ends.
//
// HOW IT WORKS:
// The trace is split into segments at each detected ictus point
// (direction reversal). For each segment the dominant movement
// direction is calculated and compared to the expected direction
// for that beat in the selected pattern.
//
// Expected directions per pattern:
// 2/4: Segment 1 = DOWN, Segment 2 = UP
// 3/4: Segment 1 = DOWN, Segment 2 = RIGHT, Segment 3 = UP

public static class DirectionalSegmentAnalyser
{
    // Minimum speed threshold to count as meaningful movement
    private const float MinMeaningfulSpeed = 0.05f;

    // Minimum dot product to count direction as correct
    // 0.3 means within ~72 degrees of expected direction
    // Lower = more lenient, Higher = stricter
    private const float DirectionThreshold = 0.3f;

    // Analyses segments and returns result
    // velocitySamples = controller velocity each frame
    // ictusFrameIndices = frame indices where direction reversals occurred
    // pattern = which beat pattern to check against
    public static void Analyse(
        List<Vector3>   velocitySamples,
        List<int>       ictusFrameIndices,
        BeatPattern     pattern,
        MetricsResult   result)
    {
        Vector3[] expectedDirections = GetExpectedDirections(pattern);
        int expectedSegments         = expectedDirections.Length;

        result.totalDirectionalSegments   = expectedSegments;
        result.segmentDirectionCorrect    = new bool[expectedSegments];

        if (velocitySamples == null || velocitySamples.Count == 0)
        {
            result.correctDirectionalSegments  = 0;
            result.directionalAccuracyPercent  = 0f;
            return;
        }

        // Build segment boundaries from ictus frame indices
        // Segment 0: frame 0 → ictusFrameIndices[0]
        // Segment 1: ictusFrameIndices[0] → ictusFrameIndices[1]
        // etc.
        List<int> boundaries = new List<int>();
        boundaries.Add(0);
        foreach (int idx in ictusFrameIndices)
            boundaries.Add(Mathf.Clamp(idx, 0, velocitySamples.Count - 1));
        boundaries.Add(velocitySamples.Count - 1);

        int correctCount = 0;
        int segmentsToCheck = Mathf.Min(expectedSegments, boundaries.Count - 1);

        for (int s = 0; s < segmentsToCheck; s++)
        {
            int startFrame = boundaries[s];
            int endFrame   = boundaries[s + 1];

            Vector3 dominant = GetDominantDirection(velocitySamples, startFrame, endFrame);
            Vector3 expected = expectedDirections[s];

            float dot = Vector3.Dot(dominant.normalized, expected.normalized);
            bool correct = dot >= DirectionThreshold;

            result.segmentDirectionCorrect[s] = correct;
            if (correct) correctCount++;

            Debug.Log($"[DirectionalAnalyser] Segment {s}: dominant={dominant} " +
                      $"expected={expected} dot={dot:F2} correct={correct}");
        }

        result.correctDirectionalSegments = correctCount;
        result.directionalAccuracyPercent = segmentsToCheck > 0
            ? (float)correctCount / segmentsToCheck * 100f
            : 0f;
    }

    // Calculates the average movement direction across a segment
    // Filters out near-stationary frames
    private static Vector3 GetDominantDirection(List<Vector3> velocities, int start, int end)
    {
        Vector3 sum = Vector3.zero;
        int count   = 0;

        for (int i = start; i <= end && i < velocities.Count; i++)
        {
            if (velocities[i].magnitude >= MinMeaningfulSpeed)
            {
                sum += velocities[i].normalized;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    // Returns expected dominant direction for each segment of a pattern
    // These are approximate — Y axis is vertical (up/down), X is horizontal
    private static Vector3[] GetExpectedDirections(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:
                return new Vector3[]
                {
                    Vector3.down,   // segment 1: downstroke
                    Vector3.up      // segment 2: upstroke
                };

            case BeatPattern.ThreeBeat:
                return new Vector3[]
                {
                    Vector3.down,   // segment 1: downstroke
                    Vector3.right,  // segment 2: right stroke
                    Vector3.up      // segment 3: upstroke
                };

            case BeatPattern.FourBeat:
                return new Vector3[]
                {
                    Vector3.down,   // segment 1: downstroke
                    Vector3.left,   // segment 2: left stroke
                    Vector3.right,  // segment 3: right stroke
                    Vector3.up      // segment 4: upstroke
                };

            default:
                return new Vector3[0];
        }
    }
}