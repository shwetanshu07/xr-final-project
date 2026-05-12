# XR Conducting Trainer — Metrics Documentation

> A complete reference for every metric collected in the **Learn** and **Practice** environments: what it measures, how it is calculated step-by-step, and the maths behind each formula.

---

## Table of Contents

1. [Overview — What Gets Measured and When](#overview)
2. [How Data Is Collected Per Frame](#how-data-is-collected-per-frame)
3. [Learn Mode Metrics](#learn-mode-metrics)
   - [Metric 1 — Path Deviation](#metric-1--path-deviation-learn-mode)
   - [Metric 2 — Ictus Accuracy (Proximity)](#metric-2--ictus-accuracy-proximity-learn-mode)
   - [Metric 3 — Speed Consistency](#metric-3--speed-consistency-both-modes)
   - [Learn Mode Overall Score](#learn-mode-overall-score)
4. [Practice Mode Metrics](#practice-mode-metrics)
   - [Metric 1 — Ictus Count (Direction Reversals)](#metric-1--ictus-count-direction-reversals-practice-mode)
   - [Metric 2 — Directional Segment Accuracy](#metric-2--directional-segment-accuracy-practice-mode)
   - [Metric 3 — Speed Consistency](#metric-3--speed-consistency-both-modes)
   - [Practice Mode Overall Score](#practice-mode-overall-score)
5. [Adaptive Timer (Practice Mode Only)](#adaptive-timer-practice-mode-only)
6. [Feedback Ratings Thresholds](#feedback-ratings-thresholds)
7. [How the LLM Receives the Metrics](#how-the-llm-receives-the-metrics)
8. [Metric Comparison Table](#metric-comparison-table)

---

## Overview

The two scenes measure different things because they serve different learning goals.

| Goal | Scene | What is being assessed |
|---|---|---|
| **Learn** the path shape | LearnBeatPattern | Did the hand follow the exact guide path? Did it hit the beat positions? Was speed consistent? |
| **Practice** the gesture freely | PracticeBeatPattern | Did the hand move in the right *directions*? Did it reverse clearly at each beat? Was speed consistent? |

There is no guide path shown in Practice mode — so path deviation cannot be measured. Instead, direction of movement per stroke segment is assessed.

---

## How Data Is Collected Per Frame

Both `MetricsCollector` (Learn) and `PracticeMetricsCollector` (Practice) are called once per `Update()` frame by their respective tracing systems while the user is actively tracing.

**Learn mode — `MetricsCollector.RecordFrame(tipPosition, deviation, tipVelocity)`**
- `tipPosition` — the 3D world position where the ray from the controller hit the tracing plane
- `deviation` — the distance in metres from `tipPosition` to the nearest point on the ideal spline (pre-computed by `BeatPathManager.GetNearestPointOnSpline`)
- `tipVelocity` — the controller velocity vector in world space

**Practice mode — `PracticeMetricsCollector.RecordFrame(tipVelocity)`**
- `tipVelocity` — the controller velocity vector in world space
- No deviation is recorded because there is no guide path

At the end of a trace attempt, `GetResult()` is called once to compute all final scores from the raw per-frame buffers.

---

## Learn Mode Metrics

### Metric 1 — Path Deviation (Learn Mode)

**What it measures:** How closely the conducting baton followed the ideal spline path throughout the entire gesture.

**What is the "ideal path"?** It is a Unity Spline (a smooth curve) set up in the Unity Editor for each beat pattern. During tracing, every frame the system finds the nearest point on this spline to the current hand position, then measures the straight-line distance between them.

#### Step-by-step calculation

1. **Every frame**: compute `deviation = distance from hand position to nearest spline point` (in metres). Store in a list.

2. **Track the worst frame**: keep a running maximum `maxDeviationRecorded`.

3. **Track "good" frames**: a frame is "within good range" if `deviation ≤ 0.05 m` (5 cm). Count those frames.

4. **At the end of the attempt**, calculate three values:

   **Average path deviation:**
   ```
   averagePathDeviation = sum of all deviation samples / total number of frames
   ```

   **Max path deviation:**
   ```
   maxPathDeviation = largest single deviation recorded across all frames
   ```

   **Percentage within good range:**
   ```
   percentageWithinGoodRange = (frames where deviation ≤ 0.05m / total frames) × 100
   ```

#### Scoring

The average deviation is converted to a 0–100 score for display and the overall weighted score:

```
deviationScore = clamp(1 − (averagePathDeviation / 0.10), 0, 1) × 100
```

In plain language:
- If average deviation = **0 m** → score = **100** (perfect)
- If average deviation = **0.05 m** (5 cm) → score = **50**
- If average deviation ≥ **0.10 m** (10 cm) → score = **0** (clamped at zero)

The `0.10` value (10 cm) is the `maxDeviationForScore` tuning parameter — adjustable in the Inspector.

#### Display ratings

| Average deviation | Rating shown |
|---|---|
| ≤ 5 cm | Excellent |
| ≤ 10 cm | Good |
| > 10 cm | Needs Work |

---

### Metric 2 — Ictus Accuracy (Proximity, Learn Mode)

**What it means in conducting:** An *ictus* is the precise moment and point in space where a conducting beat "lands" — the lowest point of a downstroke or the turning point of a gesture. In the Learn scene these are fixed 3D world positions on the spline, set by the `ictusKnotIndices` array in each `PatternData`.

**What it measures:** Did the hand get close enough to each beat's target position at any point during the gesture?

#### Step-by-step calculation

1. **Before tracing begins**: `TracingSystem` queries `BeatPathManager.GetIctusPositions()` which evaluates the spline at each knot index flagged as an ictus. These world positions are passed to `MetricsCollector.StartRecording()`.

2. **Every frame**: for each ictus position, compute the 3D Euclidean distance from the current hand position:
   ```
   distance = √( (x₂−x₁)² + (y₂−y₁)² + (z₂−z₁)² )
   ```
   Track the **minimum** distance ever reached for each ictus across the whole attempt.

3. **At the end of the attempt**: for each ictus, if the minimum distance achieved was ≤ `ictusHitRadius` (default **5 cm**), it counts as a "hit".

   ```
   ictusHits = count of ictus positions where minDistance ≤ 0.05m
   ictusTotal = total number of ictus positions for the selected pattern
   ictusAccuracyPercent = (ictusHits / ictusTotal) × 100
   ```

4. The `closestDistancePerIctus` array is also stored so the LLM can say things like *"you missed beat 2 by 7 cm"*.

#### Example

For a 3-beat pattern (`ictusTotal = 3`):
- Beat 1 closest approach: 3 cm → ✓ hit
- Beat 2 closest approach: 8 cm → ✗ miss (threshold is 5 cm)
- Beat 3 closest approach: 2 cm → ✓ hit
- Result: `ictusHits = 2`, `ictusAccuracyPercent = 66.7%`

#### Key tuning parameter

| Parameter | Default | Effect |
|---|---|---|
| `ictusHitRadius` | `0.05 m` (5 cm) | Smaller = stricter; larger = more lenient |

---

### Metric 3 — Speed Consistency (Both Modes)

**What it measures:** Was the conducting hand moving at a steady, consistent speed throughout the gesture, or was it jerky and irregular?

**Why it matters in conducting:** A conductor who speeds up and slows down erratically produces unclear beat patterns for an ensemble.

**The mathematical tool used:** [Coefficient of Variation (CV)](https://en.wikipedia.org/wiki/Coefficient_of_variation) — a standard statistical measure of relative variability that is scale-independent, meaning it works regardless of whether the overall speed is fast or slow.

#### Step-by-step calculation

1. **Every frame**: compute `speed = |tipVelocity|` (the magnitude/length of the velocity vector, in metres/second). Store in a list.

2. **Filter near-stationary frames**: remove any frame where `speed < 0.1 m/s`. These are frames where the hand was barely moving (waiting, repositioning) and should not penalise speed consistency.

3. **Require at least 5 meaningful samples**. If fewer than 5 pass the filter, the score defaults to **50** (neutral — not enough data).

4. **Calculate the mean (average) speed:**
   ```
   mean = (sum of all meaningful speed samples) / count
   ```

5. **Calculate the variance** (average squared difference from the mean):
   ```
   variance = sum of (speed − mean)² / count
   ```

6. **Calculate standard deviation** (square root of variance — puts the measure back in the same units as speed):
   ```
   stdDev = √variance
   ```

7. **Calculate the Coefficient of Variation (CV):**
   ```
   CV = stdDev / mean
   ```
   - CV = **0** → perfectly consistent speed (impossible in practice)
   - CV = **0.3** → good consistency (30% variation relative to mean)
   - CV = **1.0** → poor consistency (100% variation relative to mean)
   - If `mean = 0` (hand never moved), CV defaults to 1 (worst case)

8. **Convert CV to a 0–100 score:**
   ```
   speedConsistencyScore = clamp(1 − (CV / maxCVForSpeedScore), 0, 1) × 100
   ```
   With the default `maxCVForSpeedScore = 1.0`:
   - CV = 0.0 → score = **100**
   - CV = 0.5 → score = **50**
   - CV ≥ 1.0 → score = **0** (clamped)

#### Display ratings

| `speedConsistencyScore` | Rating shown |
|---|---|
| ≥ 80 | Excellent |
| ≥ 60 | Good |
| < 60 | Needs Work |

#### Why CV instead of just standard deviation?

Standard deviation alone depends on the absolute speed. A fast conductor with high speed variation might have the same stdDev as a slow conductor with low speed variation, even though the slow one is actually less consistent *relative to their own pace*. CV normalises by the mean, making it a fair comparison regardless of overall tempo.

---

### Learn Mode Overall Score

The overall score shown in the feedback panel is a **weighted average** of the three metric scores:

```
overallScore = (deviationScore × 0.35) + (ictusAccuracyPercent × 0.40) + (speedConsistencyScore × 0.25)
```

| Metric | Weight | Rationale |
|---|---|---|
| Path Deviation | **35%** | Path shape matters but some deviation is acceptable |
| Ictus Accuracy | **40%** | Hitting exact beat positions is the most critical skill |
| Speed Consistency | **25%** | Important but secondary to hitting the right places |

**Example calculation:**
- deviationScore = 80 → contributes `80 × 0.35 = 28`
- ictusAccuracyPercent = 66.7 → contributes `66.7 × 0.40 = 26.7`
- speedConsistencyScore = 70 → contributes `70 × 0.25 = 17.5`
- **Overall = 72.2 / 100**

These same weights are used by the LLM prompt (in `LLMFeedbackManager`) so the AI coaching feedback is aligned with the displayed score.

---

### Main Issue Detection (Learn Mode)

After calculating all three scores, the system identifies the **single weakest metric** and sets `mainIssue` to a human-readable feedback sentence:

```
deviationScore  = clamp(1 − (averagePathDeviation / 0.10), 0, 1) × 100
worstScore      = min(deviationScore, ictusAccuracyPercent, speedConsistencyScore)
```

- If `worstScore == deviationScore` → *"Try to stay closer to the guide path. Average distance was X cm."*
- If `worstScore == ictusAccuracyPercent` → *"You hit N out of M beat positions. Try to pass through the marked points."*
- Otherwise (speed is worst) → *"Try to keep your tracing speed more consistent throughout the gesture."*

---

## Practice Mode Metrics

In Practice mode the guide path is **hidden**. There is no path deviation metric. Instead, the system evaluates whether the hand moved in the **correct directions** for each stroke of the chosen conducting pattern.

### Metric 1 — Ictus Count (Direction Reversals, Practice Mode)

**What it means:** In conducting, an ictus is the moment the baton changes direction (e.g., from downstroke to upstroke). In Practice mode there is no fixed spatial target — instead, the system detects ictus points by watching for a direction reversal in the Y-axis velocity.

**What it measures:** Did the user make the correct number of clear directional reversals for the selected pattern?

#### Step-by-step calculation

1. **Every frame**, record the current Y-axis velocity (`tipVelocity.y`).

2. **Detect a reversal**: compare the current frame's Y velocity to the previous frame's:
   ```
   wasMovingDown = previousVelocityY < −0.05   (downstroke threshold)
   nowMovingUp   = currentVelocityY  >  0.05   (upstroke threshold)

   if (wasMovingDown AND nowMovingUp):
       record this frame index as an ictus
   ```
   The two thresholds (`−0.05` and `+0.05` m/s) create a dead zone that filters out small oscillations and noise. Only a genuine, committed direction reversal crosses both thresholds.

3. **At the end of the attempt**:
   ```
   ictusHits  = count of detected direction reversals
   ictusTotal = expected reversals for the pattern (TwoBeat=2, ThreeBeat=3, FourBeat=4)
   ictusAccuracyPercent = clamp(ictusHits / ictusTotal, 0, 1) × 100
   ```
   The `clamp` prevents the score exceeding 100% if extra reversals were detected.

#### Expected reversal counts

| Pattern | Expected ictus count | Reason |
|---|---|---|
| 2/4 (Two Beat) | 2 | Down, Up = 1 reversal × 2 beats |
| 3/4 (Three Beat) | 3 | Down, Right, Up = 2 reversals + 1 final reversal |
| 4/4 (Four Beat) | 4 | Down, Left, Right, Up = multiple reversals |

> **Difference from Learn mode:** Learn mode uses *spatial proximity* (was the hand within 5 cm of a fixed 3D point?). Practice mode uses *velocity direction change* (did Y-velocity flip sign decisively?).

---

### Metric 2 — Directional Segment Accuracy (Practice Mode)

**What it measures:** For each stroke in the conducting pattern, did the hand actually move in the correct direction? This is the primary metric in Practice mode.

**Computed by:** `DirectionalSegmentAnalyser.Analyse()` — a static utility class called at the end of each trace.

#### The concept: segments

The full trace is divided into **segments** by the ictus frame indices detected in Metric 1. Each segment represents one stroke of the conducting pattern:

```
Segment 0:  frame 0        → ictusFrameIndex[0]    (first stroke)
Segment 1:  ictusFrameIndex[0] → ictusFrameIndex[1]  (second stroke)
...
Last segment: ictusFrameIndex[last] → final frame    (last stroke)
```

#### Step-by-step calculation

1. **Get expected directions** for the selected pattern:

   | Pattern | Segment 0 | Segment 1 | Segment 2 | Segment 3 |
   |---|---|---|---|---|
   | TwoBeat (2/4) | DOWN `(0,−1,0)` | UP `(0,1,0)` | — | — |
   | ThreeBeat (3/4) | DOWN `(0,−1,0)` | RIGHT `(1,0,0)` | UP `(0,1,0)` | — |
   | FourBeat (4/4) | DOWN `(0,−1,0)` | LEFT `(−1,0,0)` | RIGHT `(1,0,0)` | UP `(0,1,0)` |

2. **For each segment**, calculate the **dominant direction**:
   - Collect all velocity vectors within the segment's frame range
   - Filter out frames where `speed < 0.05 m/s` (near-stationary noise)
   - Normalise each remaining velocity vector to unit length (just the direction, not the magnitude)
   - Average all the normalised vectors:
   ```
   dominantDirection = (sum of normalised velocity vectors for meaningful frames) / count
   ```

3. **Compare to expected direction** using the **dot product**:
   ```
   dot = dominantDirection.normalised · expectedDirection.normalised
   ```
   The dot product of two unit vectors equals the **cosine of the angle between them**:
   - dot = **1.0** → same direction (0°) → perfect
   - dot = **0.0** → perpendicular (90°) → wrong direction
   - dot = **−1.0** → opposite direction (180°) → completely wrong

4. **Threshold check:**
   ```
   correct = dot ≥ 0.3
   ```
   A dot product of 0.3 corresponds to an angle of approximately **72°** between the dominant and expected directions. This is intentionally lenient — conducting gestures are not perfectly axis-aligned.

5. **Final directional accuracy:**
   ```
   correctDirectionalSegments = count of segments where correct == true
   directionalAccuracyPercent = (correctDirectionalSegments / totalDirectionalSegments) × 100
   ```

#### Example (Three Beat pattern)

Suppose the trace produces 3 segments and the user gestures roughly correctly:

| Segment | Expected | Dominant detected | Dot product | Correct? |
|---|---|---|---|---|
| 0 (downstroke) | (0, −1, 0) | (0.1, −0.95, 0.1) normalised | ~0.93 | ✓ |
| 1 (right stroke) | (1, 0, 0) | (0.6, 0.5, 0) normalised | ~0.77 | ✓ |
| 2 (upstroke) | (0, 1, 0) | (0.2, −0.8, 0.1) normalised | ~−0.78 | ✗ (went down again) |

Result: `correctDirectionalSegments = 2`, `directionalAccuracyPercent = 66.7%`

#### Display ratings

| `directionalAccuracyPercent` | Rating shown |
|---|---|
| ≥ 80% | Excellent |
| ≥ 60% | Good |
| < 60% | Needs Work |

---

### Metric 3 — Speed Consistency (Both Modes)

Speed consistency is calculated **identically** in both Learn and Practice modes using the Coefficient of Variation formula. See the [full explanation above](#metric-3--speed-consistency-both-modes).

The only difference is the data source:
- **Learn mode**: speed samples come from the velocity magnitude of the controller while it was casting a ray to the tracing plane
- **Practice mode**: speed samples come from `tipVelocity.magnitude` recorded each frame alongside the directional velocity used for ictus detection

---

### Practice Mode Overall Score

The overall score in Practice mode uses a **different weighting** from Learn mode, reflecting that directional accuracy is the most important skill being trained:

```
overallScore = (directionalAccuracyPercent × 0.50) + (ictusAccuracyPercent × 0.25) + (speedConsistencyScore × 0.25)
```

| Metric | Weight | Rationale |
|---|---|---|
| Directional Accuracy | **50%** | The primary goal of Practice mode |
| Ictus Accuracy | **25%** | Clear reversals matter but are secondary to direction |
| Speed Consistency | **25%** | Same importance as ictus count |

**Example calculation:**
- directionalAccuracyPercent = 66.7 → contributes `66.7 × 0.50 = 33.3`
- ictusAccuracyPercent = 66.7 → contributes `66.7 × 0.25 = 16.7`
- speedConsistencyScore = 80 → contributes `80 × 0.25 = 20`
- **Overall = 70 / 100**

---

### Main Issue Detection (Practice Mode)

```
if directionalAccuracyPercent < 60%:
    → "Focus on the direction of each stroke. X of Y strokes were in the correct direction."

else if ictusHits < ictusTotal:
    → "You made N clear direction changes but M are needed. Make each reversal more decisive."

else:
    → "Try to keep your arm speed more consistent throughout the gesture."
```

The order of checks reflects priority: direction correctness first, then ictus count, then speed.

---

## Adaptive Timer (Practice Mode Only)

Practice mode has no fixed timeout — it adapts to the user's last score to provide appropriate challenge.

### How it works

At the end of each trace attempt, `PracticeSceneManager.UpdateAdaptiveTimer()` runs:

```
if overallScore ≥ highScoreThreshold (default 70):
    currentTimeout = maxTimeout (default 60s)     ← more time as difficulty increases

else if overallScore < lowScoreThreshold (default 40):
    currentTimeout = minTimeout (default 15s)     ← less time to encourage commitment

else:
    currentTimeout = baseTimeout (default 30s)    ← neutral zone
```

In plain language:
- **Score ≥ 70**: the system gives **60 seconds** — the user is doing well, so a longer gesture is encouraged
- **Score < 40**: the system gives only **15 seconds** — short attempts force the user to commit to a definite gesture rather than wandering
- **Score 40–69**: stays at **30 seconds** (the starting default)

The new timeout is applied to `PracticeTracingSystem.SetTimeout()` before the next attempt begins. A spatial label notifies the user when the timer changes.

### Why these values?

Giving struggling users *less* time might seem counterintuitive. The reasoning is that a very low score often comes from the user hesitating or making many small micro-movements instead of committing to clear strokes. A tighter window encourages decisive, committed gestures.

All four values (`baseTimeout`, `highScoreThreshold`, `lowScoreThreshold`, `maxTimeout`, `minTimeout`) are exposed in the Inspector and can be tuned without code changes.

---

## Feedback Ratings Thresholds

Summary of all thresholds used for rating strings in `FeedbackPanel`:

| Metric | Excellent | Good | Needs Work |
|---|---|---|---|
| Path Deviation (Learn) | ≤ 5 cm | ≤ 10 cm | > 10 cm |
| Speed Consistency | score ≥ 80 | score ≥ 60 | score < 60 |
| Directional Accuracy (Practice) | ≥ 80% | ≥ 60% | < 60% |

---

## How the LLM Receives the Metrics

`LLMFeedbackManager.BuildPrompt()` assembles a structured text prompt that includes:

- The pattern name and its expected gesture description
- All three raw metric values (deviation, ictus hits/total, speed CV)
- The `closestDistancePerIctus[]` array so the LLM can reference specific beats by index
- The weighted overall score (using the same weights as the UI)
- The `mainIssue` string from `DetermineMainIssue()`

The LLM (Groq API, model `openai/gpt-oss-120b`) receives this structured data and generates a natural language coaching response. If the API call fails, `BuildFallbackFeedback()` returns a deterministic string based on `mainIssue`.

In Practice mode, `BuildPracticePrompt()` replaces path deviation data with the `directionalAccuracyPercent`, `segmentDirectionCorrect[]` array, and `ictusHits` from direction reversal detection.

---

## Metric Comparison Table

| | **Learn Mode** | **Practice Mode** |
|---|---|---|
| **Path Deviation** | ✓ Average, Max, % within 5 cm | ✗ Not measured (no guide path) |
| **Ictus detection method** | Spatial proximity (≤ 5 cm to fixed 3D point) | Velocity direction reversal (Y-axis sign change) |
| **Ictus score** | % of fixed positions hit | % of expected reversals detected |
| **Directional accuracy** | ✗ Not measured | ✓ Dot product per stroke segment |
| **Speed consistency** | ✓ CV of filtered speed samples | ✓ CV of filtered speed samples (identical formula) |
| **Overall score weights** | 35% path + 40% ictus + 25% speed | 50% direction + 25% ictus + 25% speed |
| **Adaptive timer** | ✗ Fixed 30s timeout | ✓ 15s / 30s / 60s based on previous score |
| **Success condition** | Reach end orb with trigger held | Trigger release (always succeeds) |
| **Failure conditions** | Trigger released mid-trace, Timeout | Timeout only |
| **LLM prompt variant** | `BuildPrompt()` | `BuildPracticePrompt()` |
