# 4ViviTools — Vision Wiring + Detector Stability Fix Plan
**Date:** 2026-07-11 · **Audience:** implementation agent (Codex) · **Type:** execution plan
**Repo root:** `D:\vs code clone 4rtool\4ViviTools`
**Goal:** stop the monster-box flicker, stop the "too many boxes", make the Smart Bot lock a stable target, and fix the root-cause detector calibration. Every task below is self-contained with file path, exact change, code, and a log-based acceptance check.

---

## 0. Evidence base (from the live logs — do not skip)

Source: `DebugTrace.log` (18,744 lines, one real farming session). The relevant published line format is:

```
[2026-07-11 10:23:15.390] [OCR] Entity scan mode=monitor index=1 dxgi=True frame=1920x1080 \
  raw=0 entities=0 hpBars=0 elapsedMs=250 safeForBot=True clientCoords=True published=True \
  minScore=0.70 detectMonsters=True.
```

Measured facts across the session (496 entity scans):

| Signal | Measurement | Meaning |
|---|---|---|
| `minScore` values | ranges **0.10 → 0.95**, e.g. 0.25×132, 0.70×85, 0.46×45, 0.54×42, 0.86×27 | **the confidence threshold is recomputed every frame** — this is the flicker source |
| `raw` vs `entities` | almost always **equal** (raw=2⇒entities=2, raw=3⇒entities=3 …) | **no confirmation gating** — every raw YOLO box is published |
| `clientCoords` | **False 179 / True 317** (36 % unusable) | monitor→client conversion fails intermittently; bot ignores False frames |
| `raw=0 entities=0` | **287 of 496 frames** | when the threshold parks high the detector returns nothing → target drops |
| `mode` | `monitor` on **100 %** of scans | never attaching capture to the client window directly |

**Conclusion:** the flicker is NOT a tracker/overlay split-brain problem. It is (1) a per-frame thrashing confidence threshold, (2) a missing confirmation gate, and (3) fragile monitor-space capture. Fix those three first; the overlay wiring is fourth.

---

## 1. Target architecture (single source of truth)

```
                        ┌──────────────────────────────────────────────┐
                        │  4rVivi.OcrServer  (out-of-process, ONNX)     │
 client window ── grab ─┤  EntityDetector.Detect(bmp)                   │
 (PrintWindow/DXGI      │    conf=TRACK_CONF (fixed) · NMS(iou)         │
  on the client HWND)   │    → List<Box>{X,Y,W,H,Score,ClassId}        │
                        └───────────────┬──────────────────────────────┘
                                        │ stdio: DETECT rows
                                        ▼
        ┌────────────────────────────────────────────────────────────────┐
        │  4rVivi.App  OcrService.EntityScan()                            │
        │   1. parse rows → RawDetection[]                                │
        │   2. filters: exclusion classes, player-class, map-name prior  │
        │   3. ByteTrackLite.Update(raw)  → Track[] with State + Hits    │
        │   4. build LiveScene.Entities  (CONFIRMED tracks only)         │
        │   5. publish ONE snapshot (client coords)                      │
        └───────────────┬───────────────────────────┬────────────────────┘
                        │                            │
                        ▼                            ▼
             Overlay draws LiveScene        SmartBot targets LiveScene
             (Visible + LostGrace)          (Visible && Confirmed && Misses==0)
```

**Invariant:** the overlay and the bot read the **same** `LiveScene`. The overlay never draws independent raw YOLO boxes except behind an explicit `DebugRawBoxes` flag.

**Two-threshold rule (core of the fix):**
- `TRACK_CONF = 0.30` — low bar, feeds the tracker (keeps recall of the recall-first model).
- `ATTACK_CONF = 0.55` — high bar, a track is only *attackable* once its best detection crossed this.
This reconciles "recall-first detector" with "too many boxes" **without any per-frame formula.**

---

## 2. Work items

Each task = **files → change → code → acceptance (log assertion)**. Do them in order; each is independently testable.

### T1 — Freeze the confidence threshold (kill the auto-formula)

**Why:** `minScore` swinging 0.10→0.95 per frame is the flicker. A detector needs a *stable* gate.

**Files:**
- `src/4rVivi.App/Services/OcrService.cs` — where "auto monster confidence" is computed and passed as `minScore`.
- `src/4rVivi.Core/Ocr/RegionProfiles.cs` or a new `VisionConfig` for the constants.
- `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` — the "Auto" checkbox for monster confidence.

**Change:** remove the per-frame formula. Introduce two fixed constants, user-overridable but NOT recomputed each frame.

```csharp
// src/4rVivi.Core/Ocr/VisionConfig.cs  (NEW)
namespace FourRVivi.Core.Ocr;

/// <summary>Fixed vision thresholds. These are set once (defaults or user slider) and never
/// recomputed per frame — a per-frame threshold is what caused box flicker (see 2026-07-11 log).</summary>
public sealed class VisionConfig
{
    public float TrackConf   { get; set; } = 0.30f;  // low bar: feed the tracker (recall)
    public float AttackConf  { get; set; } = 0.55f;  // high bar: a track may be attacked
    public float Iou         { get; set; } = 0.45f;  // NMS
    public int   MinHits     { get; set; } = 2;      // consecutive hits before Confirmed
    public int   MaxAge      { get; set; } = 4;      // missed frames before a track is Removed
    public int   MaxCoast    { get; set; } = 4;      // frames a LostGrace box may still be drawn
}
```

In `OcrService`, delete the auto block and use `_vision.TrackConf` for the detector `minScore`:

```csharp
// BEFORE (pseudo — the thrashing auto formula)
// float minScore = AutoMonsterConfidence(frameStats, history);  // <-- swings 0.10..0.95

// AFTER
float minScore = _vision.TrackConf;   // fixed 0.30, detector-side recall gate
```

Wire the UI "Auto" checkbox to simply lock/unlock the slider; when checked it sets `TrackConf=0.30, AttackConf=0.55` once — it does not run a per-frame function.

**Acceptance:** in `DebugTrace.log`, `minScore=` is **constant** for the whole session (one value, not a distribution). Grep proof:
```bash
grep -oE 'minScore=[0-9.]+' DebugTrace.log | sort -u   # must print exactly ONE line
```

---

### T2 — Enforce confirmation gating before publish

**Why:** logs show `raw == entities` — every raw box is published. `entities` must be *confirmed tracks only*.

**Files:**
- `src/4rVivi.Core/Ocr/ByteTrackLite.cs` (align to this contract; create if the current one differs).
- `src/4rVivi.App/Services/OcrService.cs` — build `LiveScene.Entities` from confirmed tracks.

**ByteTrackLite contract (reference implementation — ~120 lines, no deps):**

```csharp
// src/4rVivi.Core/Ocr/ByteTrackLite.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FourRVivi.Core.Ocr;

public enum TrackState { Tentative, Visible, LostGrace, Removed }

public sealed class Track
{
    public int Id;
    public int ClassId;
    public float X, Y, W, H;         // last CONFIRMED box (client coords)
    public float Score;              // last detection score
    public float BestScore;          // max score ever seen (drives Attackable)
    public int Hits;                 // total confirmations
    public int Misses;               // consecutive missed frames
    public TrackState State = TrackState.Tentative;
    public string Name = "";         // voted name (T5)

    public int Cx => (int)(X + W / 2);
    public int Cy => (int)(Y + H / 2);
}

/// <summary>Minimal IoU tracker: greedy IoU match, min-hits to confirm, max-age to drop.
/// NO velocity prediction — RO sprites teleport-step, so we hold the last confirmed box
/// (a coasting box is drawn but never becomes attackable).</summary>
public sealed class ByteTrackLite
{
    private readonly List<Track> _tracks = new();
    private int _nextId = 1;

    public IReadOnlyList<Track> Tracks => _tracks;

    public void Update(IReadOnlyList<(int cls, float x, float y, float w, float h, float score)> dets,
                       float iouThr, int minHits, int maxAge, float attackConf)
    {
        var unmatched = new HashSet<int>(Enumerable.Range(0, dets.Count));

        // 1) greedy match existing tracks to detections by IoU (same class)
        foreach (var t in _tracks.OrderByDescending(t => t.BestScore))
        {
            int best = -1; float bestIou = iouThr;
            foreach (var i in unmatched)
            {
                if (dets[i].cls != t.ClassId) continue;
                float io = Iou(t, dets[i]);
                if (io >= bestIou) { bestIou = io; best = i; }
            }
            if (best >= 0)
            {
                var d = dets[best];
                t.X = d.x; t.Y = d.y; t.W = d.w; t.H = d.h; t.Score = d.score;
                t.BestScore = Math.Max(t.BestScore, d.score);
                t.Hits++; t.Misses = 0;
                t.State = t.Hits >= minHits ? TrackState.Visible : TrackState.Tentative;
                unmatched.Remove(best);
            }
            else
            {
                t.Misses++;
                t.State = t.Misses <= maxAge ? TrackState.LostGrace : TrackState.Removed;
            }
        }

        // 2) spawn new tentative tracks for unmatched detections
        foreach (var i in unmatched)
        {
            var d = dets[i];
            _tracks.Add(new Track { Id = _nextId++, ClassId = d.cls, X = d.x, Y = d.y, W = d.w, H = d.h,
                                    Score = d.score, BestScore = d.score, Hits = 1, State = TrackState.Tentative });
        }

        // 3) reap removed
        _tracks.RemoveAll(t => t.State == TrackState.Removed);
    }

    /// <summary>Attackable = confirmed, currently seen (Misses==0), and quality above the attack bar.</summary>
    public static bool IsAttackable(Track t, float attackConf)
        => t.State == TrackState.Visible && t.Misses == 0 && t.BestScore >= attackConf;

    private static float Iou(Track t, (int cls, float x, float y, float w, float h, float score) d)
    {
        float ax2 = t.X + t.W, ay2 = t.Y + t.H, bx2 = d.x + d.w, by2 = d.y + d.h;
        float ix = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(t.X, d.x));
        float iy = Math.Max(0, Math.Min(ay2, by2) - Math.Max(t.Y, d.y));
        float inter = ix * iy, uni = t.W * t.H + d.w * d.h - inter;
        return uni <= 0 ? 0 : inter / uni;
    }
}
```

In `OcrService.EntityScan`, publish confirmed tracks only:

```csharp
_tracker.Update(rawDets, _vision.Iou, _vision.MinHits, _vision.MaxAge, _vision.AttackConf);

var entities = _tracker.Tracks
    .Where(t => t.State == TrackState.Visible || t.State == TrackState.LostGrace)
    .Select(t => new SceneEntity {
        TrackId    = t.Id,
        ClassId    = t.ClassId,
        X = t.X, Y = t.Y, W = t.W, H = t.H,
        Score      = t.Score,
        Name       = t.Name,
        State      = t.State.ToString(),
        Attackable = ByteTrackLite.IsAttackable(t, _vision.AttackConf)
    })
    .ToList();

LiveScene.Instance.Publish(entities, clientCoords: true);

// log both counts so the gate is observable
Log($"[OCR] Entity scan ... raw={rawDets.Count} entities={entities.Count} " +
    $"confirmed={entities.Count(e=>e.State=="Visible")} attackable={entities.Count(e=>e.Attackable)} ...");
```

**Acceptance:** in the log, `raw` is frequently **greater** than `entities`, and a new `attackable=` field appears. Assertion:
```bash
grep -oE 'raw=[0-9]+ entities=[0-9]+' DebugTrace.log \
 | awk -F'[= ]' '{if($2<$4){bad++}} END{print (bad?"FAIL raw<entities":"OK gate")}'
```

---

### T3 — Attach capture to the client window (drop monitor+convert default)

**Why:** 36 % of frames were `clientCoords=False` in `mode=monitor`; the bot ignores those. You already have client-only capture — make it the default.

**Files:**
- `src/4rVivi.App/Services/OcrService.cs` — `CaptureWindow` / capture-mode selection.
- `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` — the capture-mode toggle default.

**Change:** default to **client-attached** capture (PrintWindow `PW_CLIENTONLY|PW_RENDERFULLCONTENT`, or DXGI restricted to the client rect). In client mode, detections are already in client coordinates, so `clientCoords` is unconditionally true — delete the monitor→client back-conversion from the hot path.

```csharp
// OcrService.cs
private (SKBitmap bmp, bool clientCoords) GrabForEntities()
{
    if (_captureMode == CaptureMode.Client && Session.WindowHandle != IntPtr.Zero)
    {
        var bmp = CaptureClient(Session.WindowHandle);   // PW_CLIENTONLY path (already implemented)
        return (bmp, true);                              // native client coords — no conversion
    }
    // monitor mode kept only for the overlay/debug, never the default for the bot
    var mb = CaptureMonitor(_monitorIndex);
    return (mb, TryConvertMonitorToClient(ref mb));      // may be false
}
```

**Acceptance:** with default settings, `mode=client` and `clientCoords=True` on **100 %** of scans:
```bash
grep -oE 'clientCoords=(True|False)' DebugTrace.log | sort | uniq -c   # False count == 0
```

---

### T4 — Single-source overlay (draw LiveScene, not raw boxes)

**Why:** overlay must show exactly what the bot can act on, with a visible reason when a box is not clickable.

**Files:**
- `src/4rVivi.App/Overlay/OcrOverlayWindow.cs` (or `OverlayController.cs`) — render loop.
- `src/4rVivi.App/ViewModels/OverlayViewModel.cs`.

**Change:** overlay iterates `LiveScene.Instance.Entities` only. Colour by state; label with attackability.

```csharp
foreach (var e in LiveScene.Instance.Entities)
{
    var (stroke, label) = e.State switch
    {
        "Visible"   when e.Attackable => (Brushes.Lime,   $"#{e.TrackId} {e.Name}"),
        "Visible"                     => (Brushes.Yellow, $"#{e.TrackId} {e.Name} (not attackable)"),
        "LostGrace"                   => (Brushes.Gray,   $"#{e.TrackId} lost"),
        _                             => (Brushes.DarkGray, "")
    };
    DrawBox(e.X, e.Y, e.W, e.H, stroke, label);   // client coords; convert to monitor only if overlay is monitor-space
}
```

Gate any raw-box drawing behind `if (DebugRawBoxes) { ... }`.

**Acceptance:** toggling monster confidence clears stale boxes immediately (already partly done); a yellow "(not attackable)" box is visible for tracks below `AttackConf` — proving overlay and bot share truth.

---

### T5 — Per-track name voting + map-focus as a naming prior

**Why:** names flicker to generic "Monster" and same-sprite mobs get mislabeled. Naming must be per-track and time-voted; the map only biases the *name*, never the *existence* of a box.

**Files:**
- `src/4rVivi.Core/Ocr/ByteTrackLite.cs` (`Track.Name`, a per-track vote buffer).
- `src/4rVivi.Core/Ocr/TemporalVotingService.cs` (reuse — vote keyed by `TrackId`).
- `src/4rVivi.Core/Data/` map focus source: `src/4rVivi.Core/Data/map_mobs.json` (built by `tools/build_map_mobs.py`).

**Change:**
1. On each confirmed track, run icon recognition on its crop, push the label into `TemporalVoting.Vote($"trk{TrackId}", label)`. Assign the majority to `Track.Name`.
2. Load `map_mobs.json[currentMap]` → the set of mobs that actually spawn on this map. When the icon result is ambiguous/low-score, **prefer a candidate present in the map set**. If still ambiguous, keep generic `"Monster"` — do NOT force a name.

```csharp
// naming pass (per confirmed track)
string raw = _icon.Classify(crop);                 // may be low-confidence
string voted = _voter.Vote($"trk{t.Id}", raw);     // per-track majority
if (_iconScore < 0.5f && _mapMobs.TryGetValue(curMap, out var allowed))
    voted = BiasToMap(voted, raw, allowed) ?? voted;  // map prior only when unsure
t.Name = voted;
```

**Acceptance:** with two different mobs on screen, each keeps a stable distinct `#id name`; on a known map, generic boxes resolve to map-appropriate names; on an unknown map they stay `Monster` rather than guessing.

---

### T6 — Smart Bot targets a TrackId, attacks only attackable tracks

**Why:** stable lock; the whole point of tracking.

**Files:**
- `src/4rVivi.Core/Automation/SmartBotEngine.cs`.
- `src/4rVivi.Core/Game/LiveScene.cs` (`Nearest` should accept an attackable filter and return the `TrackId`).

**Change:** target selection picks the nearest **attackable** track, then *sticks to that TrackId* until it dies or goes `Removed` (hysteresis) instead of re-choosing nearest every frame.

```csharp
// SmartBotEngine loop
var target = LiveScene.Instance.NearestAttackable(cx, ch/2, BuildTargetPredicate());
if (_lockedId != 0 && LiveScene.Instance.TryGet(_lockedId, out var locked) && locked.Attackable)
    target = locked;                       // keep the lock (hysteresis) — no per-frame flip
if (target is { } t)
{
    _lockedId = t.TrackId;
    string skillKey = PerMonsterSkillKey(t.Name);
    if (skillReady) { Keys.Tap(Hwnd, KeyName.ToVk(skillKey), 15); await Timing.DelayAsync(ArmDelayMs, ct); }
    ClickAt(t.Cx, t.Cy);                    // attack (click confirms the target)
    Log(BotLogKind.Movement, $"Attack trk#{t.TrackId} {t.Name} @ {t.Cx},{t.Cy} best={t.BestScore:0.00}");
    await Timing.DelayAsync(RotationMs, ct);
}
else { _lockedId = 0; /* roam */ }
```

**Acceptance:** bot log shows the same `trk#` targeted across many frames (a lock), not a new id every loop; refusal reasons logged when `attackable=0`.

---

## 3. Root-cause detector fix (model, not thresholds)

The threshold thrashing was a *symptom* of low, uncalibrated confidence — the model is never sure, so any auto-formula chases noise. Cause = **domain gap**: trained on 9,000 synthetic scenes that don't match the live client. Fix the model so a fixed `0.30/0.55` split is obviously correct.

### 3.1 Add real labeled frames (highest leverage)
Even 300–500 real boxes beat 9,000 synthetic for calibration.

```
tools/ocr-train/
  real_frames/            # drop real gameplay PNGs here (you already capture these)
  label_real.py           # NEW: assisted labeling using the current entity.onnx as pre-labeler
  yolo_real/              # merged dataset (existing)
```

`label_real.py` (pre-label with the current model, human corrects):

```python
# tools/ocr-train/label_real.py
# Pre-annotate real frames with the current detector so labeling is correction, not from-scratch.
import sys, json, glob, os
from pathlib import Path
from ultralytics import YOLO

ROOT = Path(__file__).resolve().parent.parent.parent          # repo root
MODEL = ROOT / "src/RapidOcrNet/models/yolo/entity.onnx"
FRAMES = Path(__file__).parent / "real_frames"
OUT = Path(__file__).parent / "yolo_real" / "labels_real"
OUT.mkdir(parents=True, exist_ok=True)

m = YOLO(str(MODEL), task="detect")
for img in glob.glob(str(FRAMES / "*.png")):
    r = m.predict(img, conf=0.20, iou=0.45, imgsz=640, verbose=False)[0]
    lines = []
    for b in r.boxes:
        cls = int(b.cls); xywhn = b.xywhn[0].tolist()      # normalized cx,cy,w,h
        lines.append(f"{cls} " + " ".join(f"{v:.6f}" for v in xywhn))
    Path(OUT, Path(img).stem + ".txt").write_text("\n".join(lines))
    print("pre-labeled", img, len(lines), "boxes")
# THEN: open in your labeler (LabelImg/Roboflow), fix boxes/classes, move corrected pairs into yolo_real/{images,labels}/train
```

### 3.2 Hard-negative mining (kills "boxes everywhere" at the source)
Your recurring false positives are free negatives. Crop them, add as background images with **empty** label files, retrain.

```python
# tools/ocr-train/mine_hard_negatives.py
# Save frames where the detector fires on NON-monster regions (UI, ground, effects) as negatives.
# A YOLO "negative" = an image in images/train with an EMPTY .txt in labels/train.
import glob, shutil
from pathlib import Path
from ultralytics import YOLO
ROOT = Path(__file__).resolve().parent.parent.parent
m = YOLO(str(ROOT / "src/RapidOcrNet/models/yolo/entity.onnx"), task="detect")
FP = Path(__file__).parent / "false_positive_frames"     # you drop known-bad frames here
dst_i = Path(__file__).parent / "yolo_real/images/train"
dst_l = Path(__file__).parent / "yolo_real/labels/train"
for img in glob.glob(str(FP / "*.png")):
    stem = Path(img).stem
    shutil.copy(img, dst_i / f"neg_{stem}.png")
    (dst_l / f"neg_{stem}.txt").write_text("")           # empty = "nothing here, learn it"
    print("negative added", stem)
```

Target ratio: keep **background/negative images at 10–20 %** of the training set (Ultralytics recommendation).

### 3.3 Retrain + calibrate (don't chase mAP — chase separation)

```bash
# from repo root
cd /d "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train"
python train_yolo.py --data yolo_real\data.yaml --model yolo11n.pt --epochs 60 --imgsz 640 --batch 16
python export_onnx.py --weights yolo_real\runs\entity\weights\best.pt --out ..\..\src\RapidOcrNet\models\yolo\entity.onnx
```

Add a **calibration check** (the real acceptance metric): on a held-out set of *real* frames, true mobs should score high and junk low with a clear gap around 0.45.

```python
# tools/ocr-train/check_calibration.py
# Prints score histograms for TP vs FP so you can SEE that 0.30/0.55 is a clean split.
from ultralytics import YOLO; from pathlib import Path; import numpy as np, glob
ROOT = Path(__file__).resolve().parent.parent.parent
m = YOLO(str(ROOT / "src/RapidOcrNet/models/yolo/entity.onnx"), task="detect")
scores = []
for img in glob.glob("yolo_real/images/val/*.png"):
    r = m.predict(img, conf=0.05, imgsz=640, verbose=False)[0]
    scores += [float(b.conf) for b in r.boxes]
s = np.array(scores)
print("n=",len(s)," p10/p50/p90=", np.percentile(s,[10,50,90]).round(3),
      " frac>=0.55=", (s>=0.55).mean().round(3), " frac<0.30=", (s<0.30).mean().round(3))
# GOAL: a bimodal split — most true detections >=0.55, most junk <0.30. If unimodal/low, add more real data.
```

**Model acceptance:** on real val frames, ≥80 % of true-positive detections score ≥0.55 and the mass below 0.30 is clearly junk. Then the fixed `TrackConf=0.30 / AttackConf=0.55` split is provably correct, and the auto-formula is never needed again.

---

## 4. Execution order & checkpoints

| Step | Task | Verify before moving on |
|---|---|---|
| 1 | T1 fixed threshold | `minScore` single value in log |
| 2 | T2 confirmation gate | `raw > entities` appears; `attackable=` logged |
| 3 | T3 client capture | `clientCoords=False` count == 0 |
| 4 | T4 single-source overlay | yellow "not attackable" boxes visible |
| 5 | T6 track-id targeting | bot log keeps one `trk#` locked |
| 6 | T5 per-track naming | two mobs → two stable names |
| 7 | §3 model retrain | calibration split ≥0.80 |

After steps 1–3 the flicker and "too many boxes" should already be gone in testing — those three are the money fixes. 4–6 make it correct and legible. §3 removes the root cause permanently.

## 5. Build & test gates (run after each task)
```bash
dotnet test  "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release --nologo
dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release --no-restore --nologo   # 0 warnings, 0 errors
```
Do not ship an `entity.onnx` that fails the calibration check in §3.3 (wire that into the existing "refuse to ship a bad detector" floor).

## 6. Files created / touched (index for Codex)
**New:** `src/4rVivi.Core/Ocr/VisionConfig.cs` · `src/4rVivi.Core/Ocr/ByteTrackLite.cs` (align if exists) · `tools/ocr-train/label_real.py` · `tools/ocr-train/mine_hard_negatives.py` · `tools/ocr-train/check_calibration.py`
**Modified:** `src/4rVivi.App/Services/OcrService.cs` · `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` · `src/4rVivi.App/Overlay/OcrOverlayWindow.cs` · `src/4rVivi.App/ViewModels/OverlayViewModel.cs` · `src/4rVivi.Core/Game/LiveScene.cs` · `src/4rVivi.Core/Automation/SmartBotEngine.cs` · `src/4rVivi.Core/Ocr/TemporalVotingService.cs` · `src/4rVivi.OcrServer/Program.cs` (emit BestScore/class already present) · `tools/ocr-train/train_yolo.py` (unchanged call, documented)
