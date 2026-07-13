# 4ViviTools — Vision Assist GRF: Verify + Fix Plan (post-implementation)
**Date:** 2026-07-11 · **State:** S1–S6 implemented, build green, 49 tests, published. **Nothing tested in-client yet.**
**Read of the code (grounded):** the implementation is good. Box detection is ratio-based red (`r≥2g, r≥2b`) → survives map darkening. `NormalizedRgbDistance` normalizes each code cell by its own max channel → identity decode is brightness-invariant (my §0.2 lighting concern is handled). Generator↔detector contract matches (`BOX_PX=2`, `CODE_CELL=5`, base-5 dominant-channel code). Reject gate `bestScore>1.45→null`, IoU-0.45 dedup, and the `visionAssist/targetSource/boxDet/codeReads/nameUnknown` log line all exist.

The unknowns left are **empirical** (does the client render it, can the detector read 5px cells on screen) plus **one observability bug**. Do these in order.

---

## PART A — The in-client gate (P0, must be first)

Unit/smoke tests can't prove the premise: *the client renders the marker and the detector reads it off the screen.* Prove it once, end-to-end.

**A1 — Generate + load**
```bat
cd /d "D:\vs code clone 4rtool\4ViviTools\tools\vision-grf"
python build_vision_grf.py --client "<RO client root>" --scope map
```
Add to the client `DATA.INI` as the **first** entry so it overrides:
```
[Data]
0=VisionAssist.grf
```
- **PASS:** log in-game — the scoped monsters (e.g. Poporing) show a **red box** on the sprite. Confirm on a **bright map first**, then your **dark farm cave**.
- **FAIL (no box):** the client isn't loading the GRF. Verify magic is `Master of Magic` (`head -c 15 VisionAssist.grf`), verify DATA.INI order, and that the sprite path inside the GRF exactly matches the original (`data\sprite\몬스터\<kr>.spr`). No render = nothing downstream can work.

**A2 — Detector reads it (the 5px-cell question)**
Enable `Vision Assist GRF` + set the manifest path. Stand among boxed mobs ~60s in the **dark cave**. Grab `DebugTrace.log`.
```bash
grep -oE 'visionAssist=True targetSource=[a-z]+ boxDet=[0-9]+ codeReads=[0-9]+ nameUnknown=[0-9]+' DebugTrace.log | sort | uniq -c | head
```
- **PASS:** `targetSource=grf`, `boxDet` ≈ on-screen monster count, names correct in the overlay, works in the cave.
- **FAIL (boxDet=0 in-cave but >0 bright):** the red box renders but the ratio test or the 5px code cells are corrupted by lighting/edge blending → see Fix B3.
- **Also capture a full screenshot** of the cave with boxes on — I can eyeball whether the code cells survive on-screen.

**Decision:** A1+A2 pass → the feature works; move to polish (B1/B2) and Phase 2 (name strip). Any fail → the matching fix below.

---

## PART B — Fixes (some now, some after the test tells us)

### B1 — Observability bug: `boxDet`/`codeReads` are the same number, and partial reads vanish *(fix now)*
In `VisionAssistMarkerDetector.Detect`, a rectangle that passes the shape test but whose code **fails to decode** (`DecodeMob==null`) is `continue`-skipped — so it's counted in **neither** `boxDet` nor `nameUnknown`. And the log hardcodes `codeReads={markers.Count} nameUnknown=0`. Consequences:
1. You can't measure decode success rate (the exact thing A2 needs to debug).
2. **A real monster whose box is found but code is unreadable disappears entirely** — the bot can't attack it and YOLO fallback doesn't fire for it (a box *was* found).

**Fix:** split the counters and emit unnamed targets.
```csharp
// in Detect: keep rectangles that pass shape even if decode fails
int rawBoxes = 0, decoded = 0;
...
if (!LooksLikeRectangleBorder(...)) continue;
rawBoxes++;
var dec = DecodeMob(...);
if (dec == null) {
    // box is a real monster; we just don't know WHICH. Emit as unnamed target.
    markers.Add(new VisionAssistMarker(c.X,c.Y,c.W,c.H, -1, "Monster", 0.3f));
} else { decoded++; markers.Add(...named...); }
```
And in `OcrService.AddVisionAssistFinds` log the truth:
```csharp
DebugTrace.Write("OCR", $"visionAssist=True targetSource={(markers.Count>0?"grf":"yolo")} " +
  $"boxDet={rawBoxes} codeReads={decoded} nameUnknown={rawBoxes-decoded} manifest='...'.");
```
**Why:** now A2's grep tells you box-detect rate vs code-decode rate separately — the single most useful diagnostic for this feature. And a boxed-but-unidentified mob is still attackable.

### B2 — Confirm the bot actually targets GRF markers (no tracker path) *(verify now)*
GRF mode has **no ByteTrackLite**, so markers don't pass through `min_hits/Confirmed`. Verify the Smart Bot treats a GRF `SceneEntity` as immediately attackable (it should — the marker is engine-pinned ground truth), rather than waiting for a "confirmed" state it will never get.
```bash
grep -oE 'trk#-?[0-9]+|targetSource=grf|Attack .*@' DebugTrace.log | head
```
- **PASS:** with GRF on and a boxed mob in view, the bot logs an attack on it.
- **FAIL:** bot ignores GRF entities → they're being gated by tracker-only `attackable`. Mark GRF-sourced entities `Attackable=true` directly (optionally a 1-frame "seen twice" guard, not a tracker).

### B3 — If A2 fails in the cave: harden the on-screen code read *(conditional)*
Only if `boxDet` is low or names are wrong in dark maps:
- **Enlarge the code cells** `CODE_CELL 5 → 8` in the generator (and detector `cell`), and sample a **3×3 median per cell** instead of a single center pixel — 5px cells are fragile against sprite-edge alpha blending. (Regenerate the GRF after changing the constant.)
- **Tune the reject threshold** `1.45` from real data: log `bestScore` for known mobs and set the gate just above the true-match cluster.
- If still noisy, move the code cells **inside the body center** (less edge blending than the corner) or repeat the code on two corners and vote.

### B4 — Death/despawn frames *(minor, later)*
The box is baked into every frame incl. death animation → a dying mob stays boxed briefly. Optional: skip clicking a marker whose box center hasn't moved after a registered hit. Low priority.

---

## PART C — What to send back after the test
- The cave `DebugTrace.log` + one screenshot with boxes on.
- The A2 grep output (`boxDet/codeReads/nameUnknown`).
- Whether boxes render in-client at all (A1).

That tells us exactly which of: {client not loading GRF} · {box detect failing in low light} · {code cells unreadable on screen} · {bot not targeting}. Each maps to one fix above.

---

## Priority
1. **A1** render-in-client (gate — nothing matters until this is yes).
2. **B1** counter/observability fix (do it before A2 so A2's numbers are meaningful).
3. **A2** live decode test in the dark cave.
4. **B2** bot-targets-marker verify.
5. **B3** only if A2 shows weak reads. **B4** later. Then Phase 2 (baked readable name strip).

## Files
`src/4rVivi.App/Services/VisionAssistMarkerDetector.cs` (B1 counters, B3 cell/median) · `src/4rVivi.App/Services/OcrService.cs` (B1 log, B2 attackable) · `tools/vision-grf/build_vision_grf.py` (B3 CODE_CELL) · `src/4rVivi.Core/Automation/SmartBotEngine.cs` (B2 if needed).
