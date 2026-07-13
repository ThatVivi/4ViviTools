# 4ViviTools — Fix Plan: Phantom Boxes + Duplicate Boxes On One Monster
**Date:** 2026-07-11 · **Evidence:** `DebugTrace.log` run2 (new `mode=window client` session, 109 scans)
**Reported symptom:** too many boxes on the same monster; boxes for monsters that aren't there.
**Verdict:** T1–T4 wiring largely landed (client capture 100%, gate active, per-track states, rich logs). The remaining defects are **detection cadence** and **LostGrace being drawn**, not NMS or thresholds.

---

## 1. Diagnosis (from run2, window-mode lines only)

Verification battery on the new session:

| Check | Result | Read |
|---|---|---|
| A threshold | `minScore` = 0.44/0.46/0.50/0.70 | **mostly frozen (0.50)** but small residual variation remains |
| B capture | `mode=window`, `clientCoords=True` ×109 (0 False) | ✅ client capture fixed |
| C gate | raw==ent 21%, coast(ent>raw) 83, gate(raw>ent) 3 | tracker is active, but **coasting dominates** |
| D attackable | `attackable ≤ entities` always | ✅ ghosts are not attackable (bot won't click them) |

**Two smoking guns:**

1. **Phantom monsters = LostGrace drawn with zero live detections.**
   Frames exist with `raw=0 entities=6`, `raw=0 entities=3`, `raw=0 entities=2`. Example (19:36:39.903):
   ```
   raw=1 entities=4 missed=3 lostGrace=3
   sceneSample=[#231 Loot Visible;
                #232 E Poporing Clover miss=2 state=LostGrace;
                #228 E Poporing Clover miss=4 state=LostGrace;
                #227 E Poporing Clover miss=4 state=LostGrace]
   ```
   Three poporing boxes are drawn at **old** positions while only one thing is actually detected. `miss=4` tracks are still alive and drawn.

2. **Duplicate boxes on one monster = track churn from slow cadence.**
   Scan timestamps: `29.3 → 30.3 → 33.5 → 36.8 → 39.9 → 42.4 → 45.8 → 49.0` ⇒ **~3 s between entity scans** (the detect itself is only `elapsedMs≈100–265`, so the loop is throttling, not the model). In 3 s a Poporing walks several tiles, so its new box has **zero IoU** with its last box → the tracker can't match it → it (a) keeps the old track coasting and (b) spawns a **new id**. Result over the run: **220 distinct `trk#` ids for a few poporings.**

**Conclusion:** raise detection rate so IoU matching works again, and stop drawing coasting boxes. NMS/thresholds are fine.

---

## 2. Fixes (ordered by impact)

### F1 — Decouple entity detection from text OCR and run it fast (THE root fix)
**Why:** 3 s between detections makes tracking a moving mob impossible. Detect can run ~5–8 FPS (`elapsedMs≈125`). Text/stat OCR can stay slow.

**Files:** `src/4rVivi.App/Services/OcrService.cs` (split the scan loops), `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` (separate cadence fields).

**Change:** two independent timers.
```csharp
// OcrService.cs — separate cadences
private int _entityIntervalMs = 120;   // ~8 FPS: monsters move, tracker needs frequent frames
private int _textIntervalMs   = 750;   // stats/name/map change slowly

// entity loop (vision) — runs on its own task, only YOLO + tracker + LiveScene publish
private async Task EntityLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var sw = Stopwatch.StartNew();
        await ScanEntitiesAsync();                 // detect -> filter -> tracker -> LiveScene
        int wait = _entityIntervalMs - (int)sw.ElapsedMilliseconds;
        await Task.Delay(Math.Max(5, wait), ct);   // aim for ~8 FPS, never busy-spin
    }
}
// text loop (stats/marks) stays on the slow timer, unchanged
```
**Acceptance:** in the log, consecutive `[OCR] Entity scan` timestamps are **~120–200 ms apart**, not ~3 s. Grep:
```bash
grep 'Entity scan mode=window' DebugTrace.log | grep -oE '\+[0-9]+ms' | \
 awk -F'[+m]' 'NR>1{d=$2-p; print d} {p=$2}' | sort -n | awk '{a[NR]=$1} END{print "median gap ms =", a[int(NR/2)]}'
# GOAL: median gap ~120-200 ms (was ~3000)
```

### F2 — Do not draw LostGrace boxes (overlay = live tracks only)
**Why:** coasting boxes at stale positions read as phantom monsters (`raw=0 entities=6`). The bot already ignores them; the damage is purely visual clutter. In RO, a held box at an old spot is *wrong*, not smoothing.

**Files:** `src/4rVivi.App/Overlay/OcrOverlayWindow.cs` (or `OverlayController.cs`).

**Change:** draw only `Visible && Misses==0`. Give at most **one** frame of grace, not four.
```csharp
foreach (var e in LiveScene.Instance.Entities)
{
    // draw ONLY currently-seen tracks; never coast a box onto screen
    if (e.State != "Visible" || e.Misses > 0) continue;
    var stroke = e.Attackable ? Brushes.Lime : Brushes.Yellow;
    DrawBox(e.X, e.Y, e.W, e.H, stroke, $"#{e.TrackId} {e.Name}");
}
```
If you want to keep a *tiny* anti-blink grace, gate it hard: `e.State=="Visible" && e.Misses<=1`. Do **not** draw `LostGrace`.

**Acceptance:** no frame draws more boxes than it detected. Assertion:
```bash
grep 'Entity scan mode=window' DebugTrace.log | grep -oE 'raw=[0-9]+ entities=[0-9]+' | \
 awk -F'[= ]' '{if($4>$2)ghost++} END{print "frames with entities>raw =", ghost+0, "(should trend to ~0 once overlay drops LostGrace and F3 shortens coast)"}'
```

### F3 — Tracker: kill churn (faster removal + distance-fallback match + merge)
**Why:** even at higher FPS, fast movers and same-sprite mobs cause id churn and stacked boxes.

**File:** `src/4rVivi.Core/Game/ByteTrackLite.cs` (existing — tune, don't replace).

**Three changes:**
1. **`MaxAge = 2`** (was allowing `miss=4`). A track missed 2 frames is Removed, not coasted.
2. **Distance-fallback match:** when IoU==0 (box jumped), match to the nearest same-class track whose center is within `R` px (scale R with frame gap). This stops a moving mob from spawning a new id.
```csharp
// inside Update(), after the IoU pass fails to match a detection:
if (best < 0)
{
    float rr = MatchRadius;                 // e.g. 90 px at 8 FPS; raise if cadence is slow
    float bestD = rr;
    foreach (var t in _tracks)
    {
        if (t.ClassId != d.cls || t.Misses > 1) continue;
        float dist = Dist(t.Cx, t.Cy, d.cx, d.cy);
        if (dist < bestD) { bestD = dist; match = t; }
    }
    // if matched by distance, update that track instead of spawning a new id
}
```
3. **Merge overlapping tracks:** at end of `Update`, if two same-class tracks overlap (IoU > 0.6), keep the one with more `Hits`, drop the other. Prevents two boxes on one monster.
```csharp
// dedup pass
for (int i = _tracks.Count - 1; i > 0; i--)
  for (int j = i - 1; j >= 0; j--)
    if (_tracks[i].ClassId == _tracks[j].ClassId && Iou(_tracks[i], _tracks[j]) > 0.6f)
    { var drop = _tracks[i].Hits < _tracks[j].Hits ? i : j; _tracks.RemoveAt(drop); break; }
```
**Acceptance:** distinct `trk#` count over a comparable run drops from ~220 to a small multiple of the real mob count. Grep:
```bash
echo "distinct track ids:"; grep -oE 'trk#[0-9]+' DebugTrace.log | sort -u | wc -l   # target: well under ~50 for a poporing map
```

### F4 — Don't render Loot as a monster box (separate concern)
**Why:** `loot/` detections become Visible/attackable boxes and add to the clutter the user sees. Loot is a pickup target, not a monster.

**Files:** `src/4rVivi.App/Overlay/OcrOverlayWindow.cs`, `src/4rVivi.Core/Automation/SmartBotEngine.cs`.

**Change:** render loot on a separate, thinner style (or behind a `ShowLoot` toggle, default off); keep it out of the monster-target predicate. It already isn't a monster class for attack, but it's being drawn like one.

**Acceptance:** with `ShowLoot=off`, loot boxes disappear from the overlay; monster count on screen matches actual mobs.

### F5 — Finish freezing the monster threshold
**Why:** `minScore` still shows 0.44/0.46/0.50/0.70 in the new run — mostly 0.50 but not constant. Residual auto-nudge.

**File:** `src/4rVivi.App/Services/OcrService.cs` / `OcrReaderViewModel.cs`.

**Change:** hard-set `monsterMin = 0.50` (track) and `otherMin = 0.55`; the "Auto" checkbox only sets these once. No per-frame recompute.

**Acceptance:** `grep -oE 'minScore=[0-9.]+' … | sort -u` prints exactly one line.

---

## 3. Why this ordering

F1 (cadence) is the multiplier — at ~8 FPS the tracker's IoU works, so churn and coasting shrink *before* you touch anything else. F2 removes the visible phantom boxes immediately. F3 hardens the tracker for the residual fast-mover case. F4/F5 are clutter/polish. Do **F1 + F2 first**, re-run one session, and both reported symptoms should largely disappear on their own.

---

## 4. Re-test loop (same as before)
1. Build, run the `-gpu` release elevated, `Monitor capture` unchecked, CUDA runtime.
2. Record ~90 s standing in a poporing pack + ~30 s walking.
3. Run `tools/verify_debugtrace.sh DebugTrace.log`, plus the F1 gap-median and F3 distinct-id greps above.
4. **Pass bar:** median entity-scan gap ~120–200 ms; frames with `entities>raw` ≈ 0; distinct `trk#` count small; overlay shows one box per real mob.
5. Only after this passes visually clean → proceed to the model phase (real frames + hard negatives + calibration) from the prior plan.

---

## 5. Files touched (index)
`src/4rVivi.App/Services/OcrService.cs` (split entity/text loops, freeze min) · `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` (cadence fields, ShowLoot) · `src/4rVivi.App/Overlay/OcrOverlayWindow.cs` (draw Visible-only, loot style) · `src/4rVivi.Core/Game/ByteTrackLite.cs` (MaxAge=2, distance match, merge) · `src/4rVivi.Core/Automation/SmartBotEngine.cs` (loot out of monster predicate)
