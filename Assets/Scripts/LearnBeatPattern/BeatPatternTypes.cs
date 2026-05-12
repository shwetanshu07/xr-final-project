// Which beat pattern the user selected
public enum BeatPattern
{
    None,
    TwoBeat,    // 2/4 — V shape
    ThreeBeat,  // 3/4 — triangle
    FourBeat    // 4/4 — cross
}

// Why a trace attempt failed
public enum FailReason
{
    TriggerReleased,  // user let go of trigger mid trace
    Timeout           // 30 second timer ran out
}

// All data from one trace attempt
// Passed from MetricsCollector to FeedbackPanel
public class MetricsResult
{
    // ── METRIC 1 - Path Deviation ─────────────────────────────────

    // Average distance from ideal path in metres across entire trace
    public float averagePathDeviation;

    // Single worst deviation recorded at any point in metres
    public float maxPathDeviation;

    // Percentage of frames where deviation was within 5cm
    public float percentageWithinGoodRange;

    // ── METRIC 2 - Ictus Accuracy ─────────────────────────────────

    // How many ictus points were hit within 5cm
    public int ictusHits;

    // Total number of ictus points in the pattern
    public int ictusTotal;

    // Percentage of ictus points hit (0-100)
    public float ictusAccuracyPercent;

    // Closest distance achieved to each ictus point in metres
    // Index 0 = beat 1 ictus, Index 1 = beat 2 ictus etc.
    // LLM uses this to say "you missed beat 2 by Xcm"
    public float[] closestDistancePerIctus;

    // ── METRIC 3 - Speed Consistency ─────────────────────────────

    // Average hand speed in metres per second (filtered)
    public float meanSpeed;

    // Standard deviation of hand speed
    public float speedStdDev;

    // Coefficient of variation (stdDev/mean) - scale independent
    // Lower = more consistent. Range roughly 0.3 (good) to 1.0 (poor)
    public float speedCV;

    // Final 0-100 score
    public float speedConsistencyScore;

    // ── OVERALL ───────────────────────────────────────────────────

    // Whether the trace was completed successfully
    public bool isSuccess;

    // Why it failed - null if success
    public FailReason? failReason;

    // How long the trace took in seconds
    public float traceDurationSeconds;

    // Total frames recorded during trace
    public int totalFramesRecorded;

    // Single most important feedback message - worst performing metric
    public string mainIssue;

    // ── PRACTICE MODE — Directional Segment Analysis ──────────────

    // How many segments had correct dominant direction
    public int correctDirectionalSegments;

    // Total number of segments expected for this pattern
    public int totalDirectionalSegments;

    // Per-segment result — true if direction was correct
    public bool[] segmentDirectionCorrect;

    // Percentage of segments with correct direction (0-100)
    public float directionalAccuracyPercent;

    // Adaptive timer value used for next attempt
    // Set by PracticeSceneManager based on this attempt's score
    public float nextAttemptTimeoutSeconds;

}