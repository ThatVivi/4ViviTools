# 4ViviTools — Next Steps + Vision Assist GRF (bonus)
**Date:** 2026-07-11 · **Builds on:** phantom/duplicate-box fix (F1–F5), vision-wiring plan (T1–T6)
**Two parts:** (A) verify the build just shipped (`release 19:47:53`), (B) the Vision Assist GRF.

> **PRIORITY: the Vision Assist GRF (Part B) is now the primary track.** Part A is a quick 5-minute verification gate, not a project. After it passes, the GRF is the main build target — ahead of further YOLO/tracker tuning and ahead of the model-retraining phase. Rationale: the GRF makes detection *correct by construction* (game-rendered boxes + authoritative rAthena names) and removes the flicker/phantom/duplicate class of bugs entirely, so effort spent hardening the inference path has a much lower ceiling than shipping the GRF source.

---

# PART A — Verify the latest build (do this first)

Codex implemented: loot/portal/player excluded from the monster tracker, detector-confidence (not icon-name) for track/attack gating, monster min `0.50`, `MaxAge=2`, overlay draws confirmed+visible only (LostGrace hidden), spatial-first matching, overlap merge, and new log fields `rejectClass / drawn / liveTracks / visible / lostHidden`, plus a lower diagnostic throttle.

**None of that is proven until a live log shows it.** Run one ~90 s standing-in-a-mob-pack + ~30 s walking session, then check:

### A1 — cadence actually fast now (F1)
```bash
grep 'Entity scan' DebugTrace.log | grep -oE '\+[0-9]+ms' \
 | awk -F'[+m]' 'NR>1{print $2-p} {p=$2}' | sort -n | awk '{a[NR]=$1} END{print "median gap ms =", a[int(NR/2)]}'
```
**PASS:** median gap ~120–250 ms (was ~3000). **FAIL:** still ~seconds → entity loop still coupled to text OCR; reopen F1.

### A2 — phantom boxes gone (F2): drawn ≤ visible, and drawn drops when coasting
```bash
grep -oE 'drawn=[0-9]+ liveTracks=[0-9]+' DebugTrace.log | sort | uniq -c | sort -rn | head
# and: any frame drawing more than are visible?
grep -oE 'drawn=[0-9]+ .*visible=[0-9]+' DebugTrace.log \
 | awk -F'[= ]' '{if($2>$NF)bad++} END{print "frames drawn>visible =", bad+0, "(must be 0)"}'
```
**PASS:** `drawn` never exceeds `visible`; `liveTracks` may be higher (hidden LostGrace) — that's fine, they're not drawn.

### A3 — loot no longer a monster (F4)
```bash
grep -oE 'rejectClass=[^ ]+' DebugTrace.log | sort | uniq -c | head
grep -c 'Loot' DebugTrace.log      # loot should appear under rejectClass, not as attackable scene entities
```
**PASS:** `Loot`/`portal`/`player_hp` show under `rejectClass`; no `Loot ... atk=True` in `sceneSample`.

### A4 — churn down (F3)
```bash
echo "distinct track ids:"; grep -oE 'trk#[0-9]+' DebugTrace.log | sort -u | wc -l
```
**PASS:** small multiple of the real mob count (was 220 for a few poporings). Target < ~40 on a poporing map.

### A5 — threshold frozen (F5)
```bash
grep -oE 'minScore=[0-9.]+' DebugTrace.log | sort -u   # exactly one line
```

**Decision:** all pass → the vision pipeline is stable; move to Part B and/or the model phase. Any fail → reopen only that item, rebuild, re-run. Send the log and I'll judge A1–A5.

---

# PART B — Vision Assist GRF (the bonus)

## B0 — Verdict and the design decision

**This is now the top-priority feature.** Instead of guessing a tiny moving sprite, the **game renders a stable, high-contrast marker + the real name**, and the tool just reads it. It sidesteps the entire domain-gap / calibration problem — and, per B1.5, it eliminates the flicker/phantom/duplicate defects by construction rather than by tuning. Treat YOLO/OCR as the *fallback path* from here on; the GRF is the primary.

**Answer to the open question (loud vs subtle): make it LOUD and readable — do NOT optimize for a subtle marker.**
- The whole value is a *stable, high-contrast* target. A subtle/faint marker re-introduces the exact small-low-contrast OCR problem you're fighting.
- It's the user's own client; the marker is cosmetic and local.
- A "subtle OCR-optimized" marker needs a bespoke CV reader — more code, more failure modes, zero benefit over a bold box + big-font name.
- Ship an **opacity/scale slider** later if a user wants it quieter. Default: bold red box + clear white-on-black name strip.

## B1 — What to bake into each monster sprite

Two layers, both generated from the user's own extracted files:
1. **Uniform red box** around the sprite body bounds → makes *detection* trivial and near-100% recall. A plain red rectangle is far easier and more reliable to find than a trained YOLO class. (You can even bypass YOLO in this mode and detect the box with simple color CV.)
2. **Identity, baked two ways** (redundant on purpose):
   - a **name strip** (real display name, large bold font, white-on-dark) above the body → human-readable + clean for OCR;
   - a **tiny machine color-code** (2–3 cells, `mobId → palette`) in a fixed corner of the box → the tool reads *identity by color*, no text OCR at all. Bulletproof and fast; the name strip is the human-friendly fallback.

This gives: detection (red box) + identity (color-code primary, baked-name OCR fallback) with **no guessing**.

## B1.5 — The real point: game-rendered boxes + authoritative names (brainstorm)

This mode is not "better OCR." It is a **different data source that removes OCR from the loop entirely** when enabled. Two properties make it categorically stronger than the YOLO/OCR path:

**1. The name is authoritative, not a filename and not a guess.**
The baked label is the **real rAthena display name** (`mob_db` `Name`/`JName`), resolved once at generation time — never the `.spr`/`.act` filename and never an icon-bank guess. Resolution chain the generator runs:

```
sprite file (data\sprite\몬스터\<kr>.spr)  ->  mobId  ->  rAthena mob_db.Name (display)
                                                    (via gamedata.json / mob_db, divine-pride cross-check)
```

So identity is **correct by construction**. A "Poporing" is labeled "Poporing" because the generator looked it up in the server's own DB, not because a 32px sprite scored 0.6 on an embedder. The whole calibration/confidence problem disappears in this mode.

**2. The box moves in real-time because it IS the sprite.**
The red box and name are baked **into the monster's own sprite frames**, so the game engine draws them at the monster's exact rendered position, at the client's framerate, through every walk/attack/knockback frame. There is **nothing to track**:

- No ByteTrackLite, no IoU matching, no `min_hits`, no `MaxAge` coast — the marker is pinned to the body by the engine, perfectly, always.
- No flicker, no phantom boxes, no duplicate boxes on one monster — the three defects this whole thread has been chasing **cannot occur**, because the tool is no longer *inferring* box positions between slow frames; the engine hands them over pre-placed.
- No memory addresses and no OCR position read for targeting — the on-screen red rectangle already sits at the monster's live client-pixel location.

**The tool's job collapses to three cheap steps:**
```
1. color-threshold the client frame for the marker red  -> connected components -> rectangles
2. read each rectangle's baked name-strip / corner color-code -> real mob name
3. move mouse to the rectangle center and click
```
That's a color scan + a click. Cost is a fraction of one YOLO inference, so it runs at very high FPS, which makes clicking a *moving* monster far more accurate than the current ~few-FPS detector ever could.

**Architectural consequence:** when `Vision Assist GRF` is ON, the `GrfMarkerEntitySource` can **bypass YOLO, ByteTrackLite, and the icon bank completely**. It still emits the same `SceneEntity` records, so the overlay and Smart Bot are unchanged — but everything upstream of `LiveScene` is replaced by the color-CV reader. (Optional: keep a *light* 1-frame temporal check only to avoid clicking a monster mid-teleport; not a tracker, just a "seen 2 frames" click guard.)

**One caveat to design around — shared sprites.** Sprite→mob is not always 1:1: palette recolors and `G_` summon variants can share one `.spr` with different `mob_db` entries. A single baked sprite can only carry one baked name. Handle it explicitly:
- If a sprite maps to exactly one mob → bake that exact name.
- If a sprite is shared by a recolor family → the recolors use different **`.pal` palettes**, so bake **per-palette** (one variant per palette) and key the color-code to palette; or, if that's too deep for Phase 1, bake the **base family name** and let the tool disambiguate recolors by the existing icon bank only for those few cases.
- Log any sprite with >1 candidate mob so the mapping can be audited.

## B2 — Architecture (hybrid, toggleable)

```
Settings: [x] Vision Assist GRF enabled
   ├─ ON  → detect red box (color CV) → read corner color-code → mobId → real name
   │        (fallback within GRF mode: if code unreadable, OCR the baked name strip)
   └─ OFF → current path: YOLO + ByteTrackLite + icon bank + OCR   (unchanged)

Debug log per scan: visionAssist=true  boxDet=<n>  codeReads=<n>  nameOcr=<n>
                    targetSource=grf|yolo
```

Keep it a clean strategy swap: one `IEntitySource` interface with two implementations (`YoloEntitySource`, `GrfMarkerEntitySource`); `OcrService` picks by the checkbox. Both emit the same `SceneEntity`, so LiveScene/overlay/bot downstream are **unchanged**. The difference is upstream: `YoloEntitySource` runs YOLO+tracker+icon bank; `GrfMarkerEntitySource` runs only the color-CV marker reader (B1.5) and needs **no tracker at all**. GRF mode is a *source swap*, not a rewrite.

## B3 — Generator pipeline (ship the generator, not sprites)

Copyright-safe per the implementer's note: ship the tool that builds the GRF from the **user's own** client files.

```
tools/vision-grf/
  build_vision_grf.py         # NEW — the generator
  mobid_sprite_map.json       # mobId -> monster sprite path (built from client lua / gamedata)
  palette.json                # mobId -> color-code (deterministic)
Input : user's data.grf  (or extracted data\sprite\몬스터\*.spr/*.act)
Output: <client>\VisionAssist.grf   (registered FIRST in DATA.INI so it overrides)
```

Steps the generator performs, per target monster:
1. Resolve `mobId → sprite name` (from the client's `datainfo` lua or the tool's `gamedata.json` + a maintained map). Reuse `src/4rVivi.Core/Grf/GrfArchive.cs` + `SprReader.cs` to read `.spr`/`.act`.
2. For each `.act` action/frame: draw the **red border** on the frame bitmap (within existing bounds — no canvas resize, so `.act` offsets stay valid), and stamp the **corner color-code**.
3. For the **name strip**: expand the frame canvas upward by `H_name` px, draw the name, and **shift that frame's `.act` Y-offset by `H_name`** so the body still lands correctly. (This is the one fiddly bit — offsets must be recomputed, or the name will misalign. If ACT-write isn't available yet, ship Phase-1 red-box+color-code first, add name strip in Phase 2.)
4. Repack modified `.spr`/`.act` into `VisionAssist.grf` at the original internal paths.
5. Write `VisionAssist.manifest.json` (which mobIds were baked, palette used) so the runtime reader knows the code→name map.

**Staging (YAGNI — don't bake 2,600 mobs up front):**
- **Phase 1:** red box + corner color-code only (no canvas/offset changes) → detection + identity with zero `.act` surgery. Biggest win, least risk.
- **Phase 2:** add the human-readable baked name strip (needs `.act` offset recompute / ACT writer).
- **Scope:** bake only mobs that spawn on the user's farmed maps (use `src/4rVivi.Core/Data/map_mobs.json` from `build_map_mobs.py`) — a few dozen sprites, not thousands. Add a "bake all" option for completionists.

### B3.1 — `build_vision_grf.py` (Phase-1 example, saved at `tools/vision-grf/`)

Style matches the existing `tools/ocr-train/*.py` (argparse, `_ROOT` anchoring, `[tag]` progress, self-installing deps). The **name-resolution, map-scoping, red-box + color-code bake, manifest, and DATA.INI guidance are fully implemented**. The three binary I/O helpers (`read_from_grf`, `spr_to_frames`, `frames_to_spr`, `pack_grf`) are left as wired stubs — reuse the project's existing `src/4rVivi.Core/Grf/{GrfArchive,SprReader}.cs` (export a tiny shim) or the bundled GRFEditor rather than re-implementing RO's binary formats from scratch, and validate any encoder against GRFEditor.

```python
#!/usr/bin/env python3
"""
build_vision_grf.py -- Vision Assist GRF generator (Phase 1).

Bakes a bold RED BOX + a machine-readable CORNER COLOR-CODE into every monster
sprite frame, then repacks them into VisionAssist.grf. When the user loads that
GRF first in DATA.INI, the game itself renders a stable, real-time marker on each
monster -- so 4ViviTools can find monsters by a cheap color scan (no YOLO, no
tracker) and read their identity from the color-code, mapped to the AUTHORITATIVE
rAthena display name (never the .spr filename, never an icon guess).

Copyright-safe: we ship this generator, NOT sprites. The user builds the GRF from
their own extracted client files.

Phase 1 does NOT change frame canvas size, so .act files are copied through
unchanged (no offset recompute). Phase 2 adds the baked name strip + .act edits.

Usage:
  python build_vision_grf.py --client "D:\\Games\\EldrynnRO" ^
      --scope map --maps-json ..\\..\\src\\4rVivi.Core\\Data\\map_mobs.json
  python build_vision_grf.py --client "D:\\Games\\EldrynnRO" --scope all
"""
from __future__ import annotations

import argparse
import json
import struct
import subprocess
import sys
from pathlib import Path

# repo root, resolved from THIS file's location so cwd never matters
_ROOT = Path(__file__).resolve().parent.parent.parent          # tools/vision-grf -> repo root
_HERE = Path(__file__).resolve().parent


# --------------------------------------------------------------------------- deps
def _ensure(pkg: str, imp: str | None = None):
    try:
        return __import__(imp or pkg)
    except ImportError:
        print(f"[grf] installing {pkg} ...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", pkg])
        return __import__(imp or pkg)


Image = _ensure("Pillow", "PIL").Image if False else None
from PIL import Image, ImageDraw            # noqa: E402  (after _ensure has run in real use)

# marker constants -- LOUD and high-contrast on purpose (see spec B0)
BOX_RGBA   = (255, 0, 0, 255)      # pure red border
BOX_PX     = 2                     # border thickness
CODE_CELL  = 4                     # color-code cell size in px
CODE_CELLS = 3                     # 3 cells -> 24-bit id space, plenty for ~3k mobs


# --------------------------------------------------------------------------- data
def load_gamedata() -> dict[int, str]:
    """mobId -> authoritative rAthena display name (from the bundled gamedata.json)."""
    gd = json.loads((_ROOT / "src/4rVivi.Core/Data/gamedata.json").read_text(encoding="utf-8"))
    out: dict[int, str] = {}
    for m in gd.get("mobs", []):
        try:
            out[int(m["id"])] = m.get("name") or m.get("aegis") or f"mob_{m['id']}"
        except (KeyError, ValueError):
            continue
    return out


def load_sprite_map() -> dict[int, str]:
    """mobId -> monster sprite path inside the GRF (data\\sprite\\몬스터\\<kr>.spr).
    Built from the client lua (datainfo\\npcidentity + jobname) or a maintained table.
    See mobid_sprite_map.json next to this script."""
    p = _HERE / "mobid_sprite_map.json"
    if not p.exists():
        raise SystemExit(f"[grf] missing {p} -- generate it from the client lua first "
                         f"(mobId -> sprite path).")
    return {int(k): v for k, v in json.loads(p.read_text(encoding="utf-8")).items()}


def scope_mob_ids(args, gamedata: dict[int, str]) -> set[int]:
    """Which mobs to bake. 'all' = everything mapped; 'map' = only mobs that spawn on
    the maps the user farms (keeps it to a few dozen sprites, not thousands)."""
    if args.scope == "all":
        return set(gamedata.keys())
    maps = json.loads(Path(args.maps_json).read_text(encoding="utf-8"))
    ids: set[int] = set()
    name_to_id = {v.lower(): k for k, v in gamedata.items()}
    for rows in maps.values():
        for row in rows:
            nm = str(row.get("name", "")).lower()
            if nm in name_to_id:
                ids.add(name_to_id[nm])
    return ids


def color_code(mob_id: int) -> list[tuple[int, int, int]]:
    """Deterministic mobId -> CODE_CELLS RGB cells. Reversible: the runtime reader
    samples the cells and rebuilds the id. Uses high-separation channels."""
    v = mob_id & 0xFFFFFF
    return [((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)]  # 1 cell carries 24 bits; pad to CODE_CELLS below


# --------------------------------------------------------------------------- bake
def bake_frame(img: "Image.Image", mob_id: int) -> "Image.Image":
    """Draw the red border + corner color-code onto one RGBA frame IN PLACE of size.
    Canvas size is unchanged (Phase 1) so the .act offsets remain valid."""
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    d = ImageDraw.Draw(img)
    w, h = img.size
    for i in range(BOX_PX):
        d.rectangle([i, i, w - 1 - i, h - 1 - i], outline=BOX_RGBA)
    # color-code in the top-left inside the border
    cells = color_code(mob_id)
    while len(cells) < CODE_CELLS:
        cells.append((0, 0, 0))
    for idx, rgb in enumerate(cells):
        x0 = BOX_PX + idx * CODE_CELL
        d.rectangle([x0, BOX_PX, x0 + CODE_CELL - 1, BOX_PX + CODE_CELL - 1],
                    fill=(rgb[0], rgb[1], rgb[2], 255))
    return img


# --------------------------------------------------------------------------- SPR/GRF I/O
# NOTE: RO .spr = header b'SP', uint16 version, indexed frames (type0, RLE for v>=2.1),
# optional truecolor RGBA frames (type1, v>=2.0), 256*RGBA palette at EOF for indexed.
# We decode -> PIL RGBA, bake, and re-emit each frame as a TRUECOLOR (type1) frame,
# which sidesteps palette re-quantization. VALIDATE output against GRFEditor once.
def spr_to_frames(data: bytes) -> list["Image.Image"]:
    """Decode a .spr into a list of RGBA PIL frames. (Implement type0 indexed + RLE
    and type1 rgba per the format; helper kept short here -- see docs/rathena.)"""
    raise NotImplementedError(
        "Wire to the project's SprReader (src/4rVivi.Core/Grf/SprReader.cs) via a tiny "
        "export shim, OR port its decode here. SprReader already returns frame bitmaps.")


def frames_to_spr(frames: list["Image.Image"]) -> bytes:
    """Re-encode baked RGBA frames as a truecolor .spr (version 0x201, 0 indexed +
    N rgba frames). VALIDATE against GRFEditor before trusting in-game."""
    raise NotImplementedError("Emit SPR v2.1 truecolor; validate vs GRFEditor.")


def pack_grf(entries: dict[str, bytes], out_path: Path):
    """Write a GRF 0x200 (magic 'Master of Magic', zlib-per-file, zlib file table).
    Simplest reliable path for Phase 1: shell out to the bundled GRFEditor CLI if
    present; otherwise write the 0x200 container directly."""
    grfeditor = _ROOT / "tools" / "external" / "GRFEditor.exe"
    staging = out_path.parent / "_vision_staging"
    staging.mkdir(parents=True, exist_ok=True)
    for internal, blob in entries.items():
        p = staging / internal.replace("\\", "/")
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(blob)
    if grfeditor.exists():
        subprocess.check_call([str(grfeditor), "-pack", str(staging), "-o", str(out_path)])
    else:
        raise SystemExit("[grf] GRFEditor.exe not found -- point --grfeditor at it, or "
                         "implement the 0x200 writer. Staged files left in _vision_staging/.")


# --------------------------------------------------------------------------- main
def build(args) -> int:
    gamedata   = load_gamedata()
    sprite_map = load_sprite_map()
    targets    = scope_mob_ids(args, gamedata)
    print(f"[grf] baking {len(targets)} monsters (scope={args.scope})")

    client = Path(args.client)
    data_grf = client / "data.grf"            # source sprites live here (or extracted data/)
    manifest = {"version": 1, "codeCells": CODE_CELLS, "mobs": {}}
    entries: dict[str, bytes] = {}
    baked = skipped = 0

    for mob_id in sorted(targets):
        spr_path = sprite_map.get(mob_id)
        name = gamedata.get(mob_id, f"mob_{mob_id}")
        if not spr_path:
            skipped += 1
            continue
        try:
            spr_bytes = read_from_grf(data_grf, spr_path)          # + .act sibling
            frames = spr_to_frames(spr_bytes)
            frames = [bake_frame(f, mob_id) for f in frames]
            entries[spr_path] = frames_to_spr(frames)
            entries[spr_path[:-4] + ".act"] = read_from_grf(data_grf, spr_path[:-4] + ".act")  # unchanged
            manifest["mobs"][mob_id] = {"name": name, "sprite": spr_path,
                                        "code": color_code(mob_id)}
            baked += 1
        except NotImplementedError:
            raise
        except Exception as e:
            print(f"[grf] skip mob {mob_id} ({name}): {e}")
            skipped += 1

    out = client / "VisionAssist.grf"
    pack_grf(entries, out)
    (client / "VisionAssist.manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"[grf] baked={baked} skipped={skipped}")
    print(f"[grf] wrote {out}")
    print(f"[grf] ADD to DATA.INI as entry 0 (loads FIRST, overrides originals):")
    print(f"      [Data]\n      0=VisionAssist.grf")
    if baked and (len(manifest['mobs']) < len(targets)):
        print("[grf] NOTE: some mobs share a sprite (recolor/G_ variants) -- see spec B1.5; "
              "audit unmapped ids before trusting names on those.")
    return 0


def read_from_grf(grf_path: Path, internal: str) -> bytes:
    """Read one entry out of a GRF. Wire to SprReader/GrfArchive or a small GRF reader."""
    raise NotImplementedError("Wire to src/4rVivi.Core/Grf/GrfArchive.cs read path.")


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate the Vision Assist GRF from the user's own client files.")
    ap.add_argument("--client", required=True, type=Path, help="RO client root (contains data.grf / DATA.INI).")
    ap.add_argument("--scope", choices=("map", "all"), default="map",
                    help="'map' = only mobs on farmed maps (default); 'all' = every mapped mob.")
    ap.add_argument("--maps-json", default=str(_ROOT / "src/4rVivi.Core/Data/map_mobs.json"),
                    help="map_mobs.json from build_map_mobs.py (used when scope=map).")
    ap.add_argument("--grfeditor", type=Path, help="Optional path to GRFEditor.exe for packing.")
    return build(ap.parse_args())


if __name__ == "__main__":
    raise SystemExit(main())

```

## B4 — Runtime reader (GRF mode)

New `src/4rVivi.OcrServer/Ocr/RedBoxMarkerDetector.cs`:
- Color-threshold the frame for the exact marker red (fixed RGB) → connected components → rectangles = monster boxes (no YOLO needed).
- For each box, sample the corner color-code cells → look up `mobId` in the manifest palette → real name.
- If the code is occluded, crop the name strip and OCR it (clean bold font = easy).
- Emit the same `SceneEntity{ TrackId(after tracker), ClassId=monster, Name=realName, Box }`. Feed into the **same** ByteTrackLite + LiveScene.

## B5 — Risks / honest flags

- **Server rules & client integrity.** A custom GRF that overrides sprite files is a **client-side data mod**. Most RO servers allow cosmetic data-folder/GRF mods, but some run **client-file integrity checks** (and a few anti-cheats hash data). This is not input injection — it's modding — but **confirm your server permits custom sprite/data GRFs** before relying on it. Flag it in the UI ("use only if your server allows custom data GRFs").
- **Maintenance:** new monsters/sprite renames need a regenerate. Make the generator a one-click "Rebuild Vision GRF" button.
- **Correctness of `mobId→sprite` map:** the single biggest source of "wrong name baked." Validate against `gamedata.json` names and divine-pride where available; log unmapped mobIds.
- **Don't over-loud in town:** allow disabling for NPC/pet/player sprites — only bake hostile monster sprites, or you'll box the whole town.

## B6 — Debug / acceptance
- Log `visionAssist=true targetSource=grf boxDet=N codeReads=N nameOcr=N`.
- **Acceptance:** with the GRF enabled on a farmed map, every on-screen monster has one red box + correct real name, `codeReads == boxDet` (identity from code, not OCR), and `targetSource=grf`. Toggle off → behavior identical to today.

## B7 — Files (index)
**New:** `tools/vision-grf/build_vision_grf.py` · `tools/vision-grf/palette.json` · `tools/vision-grf/mobid_sprite_map.json` · `src/4rVivi.OcrServer/Ocr/RedBoxMarkerDetector.cs` · `src/4rVivi.Core/Vision/IEntitySource.cs` (+ `YoloEntitySource`, `GrfMarkerEntitySource`)
**Modified:** `src/4rVivi.App/Services/OcrService.cs` (source strategy swap) · `src/4rVivi.App/ViewModels/SettingsViewModel.cs` + Settings view (the checkbox + "Rebuild Vision GRF" button) · `src/4rVivi.Core/Grf/SprReader.cs` (+ ACT write for Phase 2) · `src/4rVivi.Core/Settings/AppSettings.cs` (`VisionAssistGrf` flag).

---

## Recommended order (GRF-first)
1. **Part A — build verification** *(P0, ~5 min gate).* Run one log, confirm A1–A5. This only protects the fallback path; it is not the project. Don't expand it.
2. **Vision GRF Phase 1** *(P0 — PRIMARY).* Generator (`build_vision_grf.py`) + `GrfMarkerEntitySource` + settings checkbox + `RedBoxMarkerDetector`. Red box + corner color-code, farmed-map scope, no `.act` surgery. This is the headline deliverable: correct-by-construction detection and identity, no tracker.
3. **Vision GRF Phase 2** *(P1).* Baked human-readable name strip (needs ACT-offset write). Ships the visible real names above monsters.
4. **Model phase** *(P2 — fallback only).* Real frames + hard negatives + calibration. Still worthwhile for users **without** the GRF, but it is no longer the main accuracy lever — the GRF is. Do it after the GRF path is shipping and stable.
5. **YOLO/tracker tuning** *(P3).* Only what the OFF-GRF fallback needs; stop over-investing here.
