# 4ViviTools — Vision Assist GRF: Implementation & Improvement Plan
**Date:** 2026-07-11 · **Status:** approved direction, ready to build · **Priority:** P0 (primary track)
**Premise (locked):** GRF mode is a **separate monster source** that bypasses YOLO, ByteTrack, icon matching, and monster-name OCR. The game renders a marker pinned to the sprite; the tool color-scans the red box, decodes the baked mob id, reads the authoritative rAthena name, and clicks the box center. Names come from `mobId → gamedata/mob_db`, never from `.spr/.act` filenames.

This plan formalizes the agreed 6-step path and adds depth + two corrections that matter for *your* dark-cave maps.

---

## 0. Two corrections before coding (read first)

### 0.1 GRF magic: accept-any on READ, but WRITE `Master of Magic`
The sample `ViviMobsBox.grf` opened with custom magic `Event Horizon`; `GrfArchive.cs` rejected it because it hard-checks `Master of Magic`. Loosening the **reader** is correct (GRFEditor and many custom GRFs use custom magic — reader should not gatekeep on the signature).

**But the generated `VisionAssist.grf` must be written with the standard `Master of Magic` signature** — because the *RO client itself* only loads standard-magic GRFs unless the client is Nemo-patched for custom magic. The whole premise is that **the game renders the marker**, so the client has to load the GRF. A custom-magic GRF the tool can read but the client won't load = no on-screen marker = the feature does nothing.
- **Reader (`GrfArchive.cs`):** accept any 15-byte signature; log it; parse the rest normally.
- **Writer (generator output):** emit `Master of Magic`.
- Only emit custom magic if the user has explicitly Nemo-patched their client for it (advanced, off by default). Surface this in the UI note.

*(You already moved the sample to Master-of-Magic — that's the right output. Keep the reader-side loosening anyway for robustness on arbitrary user GRFs.)*

### 0.2 Map ambient lighting will distort the marker colors
RO maps multiply sprite colors by the map's ambient light. Your logs/screens are **dark caves** — a baked pure red `(255,0,0)` can render as `(180,0,0)` and the color-code cells shift too. So:
- The **red-box detector must be hue/ratio-based, not exact-RGB** (`R high, G/B low`, tolerant), never `== (255,0,0)`.
- The **color-code decode must be lighting-invariant**: decode by **hue (HSV)**, and/or normalize the code cells against the box's own red as a per-frame brightness reference. Do not decode raw RGB — it will misread the mob id under map tint.

These two are the difference between "works in Prontera field" and "works in your actual farm cave."

---

## 1. Step S1 — GRF reader accepts custom magic  *(P0, small)*
**File:** `src/4rVivi.Core/Grf/GrfArchive.cs`
**Change:** replace the hard `signature == "Master of Magic"` guard with: read 15-byte signature into a field, log it, continue. Optionally keep a `KnownMagics` set for a soft warning. Header layout is unchanged (46-byte header: `signature[15] key[15] uint32 tableOffset, seed, fileCount, version`).
```csharp
// BEFORE: if (magic != "Master of Magic") throw ...
// AFTER:
Magic = Encoding.ASCII.GetString(hdr, 0, 15).TrimEnd('\0');
if (Magic != "Master of Magic")
    Log($"[grf] non-standard magic '{Magic}' — reading anyway (client needs a matching patch to load it).");
// ... parse tableOffset/version exactly as today
```
**Acceptance:** `ViviMobsBox.grf` (`Event Horizon`) opens and lists entries without throwing; a standard GRF still opens.

## 2. Step S2 — SPR writer for Phase 1  *(P0)*
**Files:** `src/4rVivi.Core/Grf/SprReader.cs` (+ new `SprWriter` alongside).
**Approach:** Phase 1 keeps **frame canvas size identical**, so `.act` is copied through untouched (no offset math). Re-emit each frame as a **truecolor (type-1 RGBA) SPR v2.1** frame — avoids re-quantizing the red box into the 256-color palette. (Eldrynn is a modern EP17.2 client → truecolor SPR is supported. If a target client is old, fall back to indexed + reserve a palette slot for the marker red.)
```
SprWriter.Write(frames: IReadOnlyList<Rgba32[]> , w, h) -> byte[]
  header: 'SP' , version=0x201 , indexedCount=0 , rgbaCount=N
  per frame: width(u16) height(u16) then w*h RGBA
  (validate byte layout against a GRFEditor-exported truecolor SPR once)
```
**Acceptance:** round-trip a monster sprite through `SprReader → SprWriter`, load the GRF in-client, sprite renders identically (minus the baked box) with correct animation (proves `.act` still aligns).

## 3. Step S3 — finish `tools/vision-grf/build_vision_grf.py`  *(P0)*
**File:** `tools/vision-grf/build_vision_grf.py` (skeleton already in repo; logic done, binary stubs remain).
**Wire the four stubs to the C# code (don't re-parse RO formats in Python):**
- `read_from_grf` / `spr_to_frames` → call a tiny CLI shim over `GrfArchive`/`SprReader` (e.g. `4rVivi.OcrServer --grf-extract` or a dedicated `VisionGrfTool` console) that returns frames as PNG.
- `frames_to_spr` → the new `SprWriter` (S2), same shim.
- `pack_grf` → `SprWriter`+GRF-pack in the shim, **output `Master of Magic`** (0.1). GRFEditor CLI is an acceptable interim packer.
**Keep as-is (already implemented):** name resolution (`mobId → gamedata.json` display name), map scoping via `map_mobs.json`, red-box + color-code bake, manifest, DATA.INI guidance, shared-sprite audit.
**Acceptance:** `python build_vision_grf.py --client <root> --scope map` produces a `Master of Magic` `VisionAssist.grf` + `VisionAssist.manifest.json`; loading it first in DATA.INI shows red boxes on the scoped monsters in-game.

## 4. Step S4 — `RedBoxMarkerDetector` runtime path  *(P0)*
**File (new):** `src/4rVivi.OcrServer/Ocr/RedBoxMarkerDetector.cs`
**Pipeline (per client frame, GRF mode):**
1. **Lighting-robust red mask** (0.2): threshold in HSV — hue≈red, high saturation, min value — not exact RGB. Vectorize (span/`SkiaSharp` pixels).
2. Connected-components → rectangles = monster boxes (client-pixel coords already; no conversion).
3. For each box, sample the **corner color-code** cells (center pixel per cell, small majority vote), **decode by hue** and match against `manifest.mobs[*].code` (nearest in hue-normalized space) → `mobId` → authoritative name.
4. If the code is occluded/ambiguous → mark identity `unknown` (still a valid *target*, just unnamed) or, in hybrid, let the icon bank name only that box.
5. Emit the **same** `SceneEntity{ ClassId=monster, Name, Box }`. **No tracker needed** — the box is the sprite; optional 1-frame "seen twice" click-guard only.
```csharp
public sealed class RedBoxMarkerDetector
{
    public IReadOnlyList<SceneEntity> Detect(SKBitmap frame, VisionManifest man);
    // HSV red mask -> CCL rects -> per-rect hue-decoded color-code -> man.Lookup(code)
}
```
**Acceptance:** on a farmed map with the GRF on, every on-screen boxed monster yields one `SceneEntity` with the correct name; works in a dark cave (lighting test), not just bright maps.

## 5. Step S5 — `Vision Assist GRF` checkbox + routing  *(P0)*
**Files:** `src/4rVivi.Core/Settings/AppSettings.cs` (`VisionAssistGrf` flag + `VisionManifestPath`), `src/4rVivi.App/ViewModels/SettingsViewModel.cs` + Settings view (checkbox + "Rebuild Vision GRF" button), `src/4rVivi.App/Services/OcrService.cs` (source strategy).
**Change:** introduce `IEntitySource` with `YoloEntitySource` and `GrfMarkerEntitySource`; `OcrService` selects by the flag. Downstream (LiveScene/overlay/bot) unchanged.
```csharp
IEntitySource src = _settings.VisionAssistGrf && _manifest != null
    ? _grfMarkerSource      // color-scan only, no YOLO/tracker/icon/OCR
    : _yoloSource;          // current path
var entities = src.Scan(frame);
```
**Acceptance:** toggling the checkbox live-switches sources; log shows `visionAssist=true targetSource=grf`.

## 6. Step S6 — fallback discipline  *(P0)*
**Rule:** GRF is primary **only** when (checkbox on) AND (manifest loaded) AND (≥1 marker detected this frame). Otherwise fall back to YOLO/OCR for that frame — covers off-scope mobs with no baked sprite, and users without the GRF.
```csharp
var grf = _grfMarkerSource.Scan(frame);
var entities = (_settings.VisionAssistGrf && grf.Count > 0) ? grf : _yoloSource.Scan(frame);
```
**Debug schema (per scan):** `visionAssist=<bool> targetSource=grf|yolo boxDet=<n> codeReads=<n> nameUnknown=<n>`.
**Acceptance:** with the GRF on but standing among an unbaked mob → that mob still gets a (YOLO) box; log shows `targetSource=yolo` for that frame.

---

## 7. Cross-cutting concerns
- **Shared sprites (recolors / `G_` variants):** one sprite → one baked name. Bake per-`.pal` palette for recolor families, or bake the base-family name and let the icon bank disambiguate only those; **log every sprite with >1 candidate mob** during generation (already stubbed in the generator).
- **Death/despawn frames:** the box is baked into all frames incl. death animation; a dying mob may stay boxed for a few frames. Acceptable; optionally skip clicking a box whose center hasn't changed after a successful hit.
- **Town/NPC safety:** bake **hostile monster sprites only** — never player/NPC/pet sprites, or you'll box the whole town.
- **Performance:** the HSV mask + CCL over 1080p is far cheaper than one YOLO inference; run the marker scan at high FPS (≥15) for accurate clicks on movers.
- **Regeneration:** new mobs / sprite renames need a rebuild → the "Rebuild Vision GRF" button re-runs the generator.

## 8. Verification run (end to end)
1. Generate: `python build_vision_grf.py --client <root> --scope map`; add `0=VisionAssist.grf` to DATA.INI.
2. In-client: confirm red boxes + (Phase 2) names render on the scoped monsters, **including in a dark cave**.
3. In-tool: enable `Vision Assist GRF`; farm 90 s. Check log: `targetSource=grf`, `boxDet` matches on-screen monster count, `codeReads==boxDet` (identity by code, not OCR), `nameUnknown≈0`.
4. Toggle off → behavior identical to today (YOLO/OCR).
5. **No flicker/phantom/duplicate boxes possible in GRF mode** — the marker is engine-rendered; confirm visually.

## 9. Risks / flags
- **Client must load the GRF** → output `Master of Magic` (0.1). If a user runs custom magic, they must have the matching client patch; default to standard.
- **Server rules / client integrity** — a custom sprite GRF is a client-side data mod (not input injection). Most servers allow cosmetic data GRFs; some run file-integrity checks. Confirm the server permits custom data GRFs; put the warning in the UI.
- **`mobId → sprite` map accuracy** is the #1 wrong-name source — validate against `gamedata.json` + divine-pride, log unmapped ids.
- **Lighting** (0.2) — if not handled hue-wise, identity decode fails in dark maps. Non-negotiable for your farm spots.

## 10. Build order (all P0 for the primary track)
`S1 (reader magic)` → `S2 (SPR writer)` → `S3 (finish generator)` → **checkpoint: a real GRF renders in-client** → `S4 (marker detector)` → `S5 (checkbox+routing)` → `S6 (fallback)` → §8 end-to-end. Phase 2 (baked readable name strip + `.act` offset write) comes after this ships.

## 11. Files (index)
**New:** `src/4rVivi.OcrServer/Ocr/RedBoxMarkerDetector.cs` · `src/4rVivi.Core/Grf/SprWriter.cs` · `src/4rVivi.Core/Vision/IEntitySource.cs` (+ `YoloEntitySource`, `GrfMarkerEntitySource`) · a small GRF/SPR CLI shim for the Python generator.
**Modified:** `src/4rVivi.Core/Grf/GrfArchive.cs` (accept custom magic) · `src/4rVivi.Core/Grf/SprReader.cs` · `tools/vision-grf/build_vision_grf.py` (wire stubs) · `src/4rVivi.App/Services/OcrService.cs` (source swap + fallback) · `src/4rVivi.Core/Settings/AppSettings.cs` · `src/4rVivi.App/ViewModels/SettingsViewModel.cs` + Settings view.
