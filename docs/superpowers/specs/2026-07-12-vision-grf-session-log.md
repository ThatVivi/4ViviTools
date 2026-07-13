# Vision Assist GRF — Session Log & Engineering Notes (for Codex)
**Date:** 2026-07-12 · **Scope:** the Vision Assist GRF pipeline — generator, picker, SPR/GRF codec, runtime contract.
**Status:** WORKING. Verified in GRFEditor: Ifrit renders red, upright, with a fixed red box on every frame + name on top + color-code. Full builder produces the two-folder library.

---

## 1. What this feature is

Instead of guessing which monster is on screen with YOLO/OCR, we make the **game itself render a marker**: every monster sprite gets a red box + its real name + a machine-readable color-code baked in. 4ViviTools then color-scans the live frame (cheap, exact) and reads identity from the code. Names come from `mob_db`/`gamedata.json`, never the sprite filename.

**Pipeline:**
```
client data.grf (original sprites)
   │  build_vision_grf.py  --library      (bakes ALL monsters)
   ▼
VisionAssistLibrary.grf                    two folders:
   ├─ data\sprite\visionassistant\*.spr    (all baked, game never reads)
   └─ data\sprite\몬스터\  (EMPTY)          (the live folder the game reads)
   │  VisionGrfPicker.exe  (Load → pick → Apply)
   ▼   promotes picked mobs: visionassistant\X.spr  ->  몬스터\X.spr  (in place)
edited GRF  →  DATA.INI 0=...grf  →  client restart
   │  runtime: VisionAssistMarkerDetector.cs
   ▼   color-scan red boxes → decode color-code → mobId → name → SceneEntity
```

---

## 2. Who built what

- **Codex (runtime, in the app):** `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs` (scan red boxes on the live frame, decode the color-code, output monster entities), plus the OCR-source routing in `OcrService.cs` (`AddVisionAssistFinds`, `VisionAssistGrf` toggle, manifest path) and the settings/UI checkbox. This side was already solid — ratio-based red test (lighting-robust) and per-cell brightness-normalized code decode.
- **This session (generator + picker + codec):** `tools/vision-grf/build_vision_grf.py` (the baker/GRF writer), the standalone WinForms picker `tools/VisionGrfPicker/`, the SPR/GRF codec, and all the bug fixes below.

**The contract between the two sides** (must stay in sync):
- `BOX_PX = 2`, `CODE_CELL = 5`, `CODE_CELLS = 3`, `CODE_LEVELS = (48,96,144,192,240)`, box color pure red `(255,0,0)`.
- `color_code(mobId)` → 3 cells, base-5, one dominant channel per cell: cell0 `(255,a,b)`, cell1 `(a,255,b)`, cell2 `(a,b,255)`.
- Manifest `VisionAssist.manifest.json`: `{codeCells, codeCell, boxPx, boxColor, mobs:{ id:{name, sprite, code:[[r,g,b]x3]} }}`. The detector matches sampled cells against `mobs[*].code`.

---

## 3. The debugging journey (what broke, how it was fixed)

### 3.1 GRF writer wrote a ZEROED header → no reader could open it
**Symptom:** GRFEditor/client errored opening the output; the file's `tableOffset/fileCount/version` were all `0`, table offset pointed past EOF.
**Cause:** the writer padded the header with 16 zero bytes and the `seek(30)` back-patch never landed; the only post-write check validated *just the magic string*.
**Fix:** write header+body+table in ONE forward pass (no seek-back, can't half-finalize) + verify by re-parsing the header. Also write to a `.tmp` then atomic `os.replace` so a locked target never wastes a build.
```python
# tools/vision-grf/build_vision_grf.py  (pack_grf, essentials)
body = bytearray(); rows = []
for internal in sorted(entries):
    comp = zlib.compress(entries[internal], 9)
    rows.append((internal, len(body), len(comp), len(entries[internal]), 1)); body += comp
table = bytearray()
for name, off, comp, real, flags in rows:
    table += _encode_grf_name(name); table.append(0)
    table += struct.pack("<III", comp, comp, real); table.append(flags); table += struct.pack("<I", off)
table_comp = zlib.compress(bytes(table), 9); table_offset = len(body)
tmp = out_path.with_name(out_path.name + ".tmp")
with tmp.open("wb") as f:
    f.write(b"Master of Magic".ljust(15, b"\0")); f.write(b"\0"*15)          # sig[15] + key[15]
    f.write(struct.pack("<IIII", table_offset, 0, len(rows)+7, 0x200))       # offset/seed/count/version
    f.write(bytes(body)); f.write(struct.pack("<II", len(table_comp), len(table))); f.write(table_comp)
_verify_grf(tmp, len(rows)); os.replace(tmp, out_path)
```
**GRF 0x200 header:** `char sig[15]; char key[15]; u32 tableOffset; u32 seed; u32 fileCount(=realCount+seed+7); u32 version(0x200)`. Reader accepts any magic; **writer must emit `Master of Magic`** (stock client only loads that).

### 3.2 Sprite colors wrong + upside down (the big one)
**Symptoms over several rounds:** blue instead of red, upside-down, then muddy olive-green with no box.
**Key discovery:** RO SPR has two frame types and they differ:
- **Truecolor (type 1)** frames — semi-transparent effects. Byte order is NOT plain RGBA, and GRFEditor/client decode them in a way our writer never matched. Every attempt (RGBA / BGRA / flip combos) that looked right in *our* reader still rendered muddy in GRFEditor, because our reader was self-consistent with our writer — a trap.
- **Indexed (type 0)** frames — the palette format nearly all monsters use (Ifrit = 52 indexed frames, 0 truecolor). **Palette is RGB order, index 0 = transparent, rows are top-down (NO flip).**

The decisive test rendered 4 decode variants of the original indexed Ifrit; **RGB + no-flip** was the only correct one:
```python
# variant that matched GRFEditor's correct render:
r, g, b = palette[po], palette[po+1], palette[po+2]     # palette is R,G,B
a = 0 if idx == 0 else 255                                # index 0 transparent
img.putpixel((x, y), (r, g, b, a))                        # NO vertical flip for indexed
```
**Final fix — stop converting to truecolor entirely. Keep the sprite INDEXED** (the exact format GRFEditor/client render natively), and draw the box/name/code by adding colors to the palette. This guarantees original colors AND makes files ~10x smaller (Ifrit 4 MB truecolor → 351 KB indexed).

### 3.3 Box changed every frame → fixed uniform box
**Requirement:** the box/name/code must be identical on every frame (biggest bounding box), name on top.
**Fix:** two-pass, whole-animation bake. Compute `wb=max width, hb=max height`; make a uniform canvas `wo × ho` (`ho = hb + 2*strip` for the name); **center each frame** so the sprite center is preserved → `.act` offsets stay valid (no ACT rewrite). Draw the SAME box rect / name / code cells on every frame.

### 3.4 The indexed bake (the core algorithm, Python)
```python
def spr_to_index_frames(data):
    # returns (frames=[(w,h,bytearray idx)], palette=256*4 bytes) or (None,None) if truecolor
    ...
def bake_index_frames(frames, palette, mob_id, name, font):
    wb = max(w for w,h,_ in frames); hb = max(h for w,h,_ in frames)
    strip = name_height+3 if name else 0
    wo = max(wb, name_width+4); ho = hb + 2*strip
    used = set(); [used.update(idx) for _,_,idx in frames]
    free = [i for i in range(1,256) if i not in used]           # spare palette slots
    red_idx, white_idx, black_idx = free.pop(), free.pop(), free.pop()
    palette[red_idx*4:red_idx*4+4]   = bytes((255,0,0,255))
    palette[white_idx*4:white_idx*4+4]= bytes((255,255,255,255))
    palette[black_idx*4:black_idx*4+4]= bytes((0,0,0,255))
    code_idx = [free.pop() for _ in color_code(mob_id)]         # 1 palette slot per code cell
    bx0=(wo-wb)//2; by0=strip; bx1=bx0+wb-1; by1=by0+hb-1        # ONE fixed box
    for (w,h,idx) in frames:
        canvas = bytearray(wo*ho)                                # 0 = transparent
        # center original indices (preserves sprite center -> .act valid)
        for y in range(h): canvas[((ho-h)//2+y)*wo+(wo-w)//2 : ...] = idx[y*w:(y+1)*w]
        # draw red box border, code cells, and name (rasterized text -> white/black indices)
    return frames_out, palette
def index_frames_to_spr(frames, palette):
    # 'SP', ver 2.1, u16 nframes, u16 0(rgba); per frame u16 w,h, RLE(0-runs), u16 rle_len; palette at EOF
```
The C# picker has a **byte-for-byte port** of these in `tools/VisionGrfPicker/Vision.cs`: `Spr.DecodeIndexed`, `Baker.BakeIndexed` (+ `NameIndices` for text→index rasterization), `Spr.EncodeIndexed`. Both sides MUST stay identical.

### 3.5 Two-folder library (`--library`)
The full builder now writes baked sprites to `data\sprite\visionassistant\` and leaves `몬스터\` empty, so loading the library alone shows NOTHING boxed — the picker promotes chosen mobs into `몬스터\`.
```python
def _to_lib(path):     # data\sprite\몬스터\X.spr -> data\sprite\visionassistant\X.spr  (byte-agnostic)
    bs = chr(92); sep = bs if bs in path else "/"
    parts = path.split(sep)
    for i in range(len(parts)-1):
        if parts[i].lower() == "sprite" and i+1 < len(parts)-1:
            parts[i+1] = "visionassistant"; break
    return sep.join(parts)
```
> Gotcha that cost time: hardcoding the Korean folder `몬스터` via `\uXXXX` escapes through nested heredocs mangled the bytes. Solution: don't hardcode it — replace whatever folder sits right after `sprite`.

### 3.6 The picker UI (WinForms) fixes
- **Right list never populated / headers+search invisible:** the middle `Dock=Fill` panel overlapped the top bar (Form docking order). Fixed by putting everything in ONE root `TableLayoutPanel` with explicit rows (top auto / lists fill / buttons auto / status fixed) — no docking overlap.
- **Owner-draw list didn't repaint 0→1 items:** dropped owner-draw; plain dark `ListBox` (reliable) + `Invalidate()` after populate.
- Softer crimson theme `#C6404A` + tooltips on every control.
- **No output file** — Apply saves in place (`GrfArchive.Open` reads all bytes to memory first, so overwriting the same path is safe). Lock-safe via `.tmp` + `File.Move`.

---

## 4. File map
| Path | Role |
|---|---|
| `tools/vision-grf/build_vision_grf.py` | baker + GRF writer; `--only <id>`, `--library`, `--scope all` |
| `tools/vision-grf/mobid_sprite_map.json` | mobId → `data\sprite\몬스터\X.spr` |
| `tools/vision-grf/build_sprite_map.py` | builds the map from client Lua |
| `tools/VisionGrfPicker/` | standalone WinForms picker (`Vision.cs`, `GrfArchive.cs`, `MainForm.cs`, `Program.cs`) → one self-contained exe |
| `tools/ocr-train/Grf/BUILD_VISION_GRF_TO_OUTPUT.bat` | one-click full library build (`--library`, output `VisionAssistLibrary.grf`) |
| `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs` | RUNTIME: scan boxes, decode code → entities (Codex) |
| `src/4rVivi.App/Services/OcrService.cs` | RUNTIME routing + `VisionAssistGrf` toggle (Codex) |

---

## 5. Commands (examples)
```bat
:: fast single-monster test (any mob id)
python tools\vision-grf\build_vision_grf.py --client "tools\ocr-train\Grf" ^
  --source-grf "tools\ocr-train\Grf\ViviMobsBoxMasterofMagic.grf" --only 1832 ^
  --out "tools\ocr-train\Grf\output\test_ifrit.grf" --no-auto-sprite-map

:: full two-folder library (all monsters) — same as the bat
python tools\vision-grf\build_vision_grf.py --client "tools\ocr-train\Grf" ^
  --source-grf "<your clean data.grf>" --scope all --library ^
  --out "tools\ocr-train\Grf\output\VisionAssistLibrary.grf"

:: build the picker exe
tools\VisionGrfPicker\build_picker.bat   ->  publish\VisionGrfPicker.exe
```

---

## 6. Known limits / next steps
- **Source completeness:** `ViviMobsBoxMasterofMagic.grf` misses ~190 newer sprites; use the real client `data.grf` (or add it as a 2nd `--source-grf`) for full coverage.
- **Shared sprites** (626): recolors/`G_` variants share one `.spr` → one baked name wins; logged as `sharedSprites` in the manifest.
- **Palette space:** bake needs ~6 free palette indices (red/white/black + 3 code cells). Nearly always available; falls back to high indices if a palette is full.
- **Truecolor-origin sprites** (rare effects) still use the truecolor path whose exact byte order GRFEditor dislikes — acceptable for now (monsters are indexed); revisit only if a truecolor monster matters.
- **Runtime B1 (Codex side):** count `boxDet` (rectangles) separately from `codeReads` (decoded), and emit an undecoded red box as `MobId=-1, "Monster"` still attackable — so a boxed-but-unreadable mob isn't dropped.

---

# PART 2 — Marker-Layer Redesign (supersedes per-frame baking)

**Why we changed:** baking the box+name into EVERY body frame made the box jitter/stack across an animation, some monsters got multiple boxes, some none. The user's own `mobsWithName.grf` showed the right technique (verified by diffing `agav.act`): **leave every body frame untouched, add ONE marker sprite, and reference it as an extra `.act` layer on every frame.** The game composes body + marker, so the marker is game-rendered, always visible, consistent, and the body keeps perfect original colors.

## 2.1 New pipeline (per monster, indexed originals)
```
original .spr (N body frames, indexed)         original .act (v2.x)
        │  KEEP frames unchanged                        │
        ▼                                               ▼
new .spr = body frames + 1 MARKER frame (idx=N)   new .act = every frame gets +1 layer -> sprIdx=N at (bx,by)
```
- Marker frame = a red box (body-sized) + a **2-row label** + the color-code, drawn in the palette (indexed).
- The added layer positions the marker at the body's frame-0 offset so the box sits over the monster.
- `.act` is parsed → a layer appended to each frame → re-serialized (proven **byte-for-byte round-trip** first, then add).

## 2.2 The label (element-aware, two rows) — from `gamedata.json`
`gamedata.json` mobs have `element`, `race`, `size`. The label tells the player what to hit it with:
```
Row 1:  "<name> - <counter-element>"    e.g.  "Ifrit - Water"     (Ifrit is Fire -> attack with Water)
Row 2:  "<size> - <race>"               e.g.  "Large - Formless"
```
```python
_COUNTER = {"Fire":"Water","Water":"Wind","Wind":"Earth","Earth":"Fire",
            "Holy":"Shadow","Shadow":"Holy","Dark":"Holy","Undead":"Holy",
            "Ghost":"Ghost","Poison":"","Neutral":""}
def marker_lines(mob_id, name):
    meta = load_mobmeta().get(mob_id, {})               # element/race/size from gamedata.json
    use = _COUNTER.get(meta.get("element",""), "")
    line1 = f"{name} - {use}" if use else name
    line2 = " - ".join(p for p in (meta.get("size",""), meta.get("race","")) if p)
    return [x for x in (line1, line2) if x]
```

## 2.3 ACT parse / serialize (exact) — `build_vision_grf.py`
Layer size is version-dependent: base 32 bytes (`x,y,sprIdx,mirror,color,scaleX,rotation,sprType`), `+4` for scaleY (v>=2.4), `+8` for width/height (v>=2.5). Frame = `range1[16] range2[16] u32 nLayers  layers  i32 eventId  [v>=2.3: u32 nAnchors + anchors(16 each)]`. Header is 16 bytes (`AC` + minor + major + u16 nActions + 10 reserved), trailing bytes (events + intervals) are preserved verbatim.
```python
def _act_parse(act):        # -> ver, laysz, header16, actions[[ranges,nl,layers,eventId,nanchor,anc]...], trailing
def _act_first_layer_xy(act):   # (x,y) of the first real body layer -> where to place the marker
def _act_add_marker(act, marker_idx, off):
    # append ONE layer per frame: struct x,y,sprIdx,mirror + color=0xFFFFFFFF, scaleX/Y=1.0, rot=0, sprType=0 (+w,h=0 v2.5)
```
Round-trip test (do this before trusting any ACT edit): parse→serialize with no change must equal the input byte-for-byte. It does (agav v2.5: 45772 bytes identical).

## 2.4 Marker sprite (indexed) — `build_marker_index(lines, wb, hb, palette, font, mob_id)`
- `box_w,box_h = wb+6, hb+6`; symmetric vertical pad `ho = box_h + 2*strip` so the **image center == box center** (place the layer at the body offset and the box surrounds the body).
- Draw the red box border, the color-code cells in the box corner, and each label row (white text + black outline) rasterized to spare palette indices.
- Returns `(w, h, idx_bytes, palette)`; appended to the body frames as the last SPR frame.

## 2.5 Build-loop integration (indexed branch)
```python
fi, pal = spr_to_index_frames(raw_spr)                 # body frames + palette (unchanged)
mw,mh,midx,pal2 = build_marker_index(marker_lines(mob_id,name), wb, hb, pal, font, mob_id)
baked_bytes = index_frames_to_spr(fi + [(mw,mh,midx)], pal2)   # body + 1 marker
bx,by = _act_first_layer_xy(act_raw)
act_bytes = _act_add_marker(act_raw, len(fi), (bx,by))         # +1 layer/frame -> sprIdx = marker
```
Truecolor-origin sprites (rare effects) still use the per-frame bake fallback.

## 2.6 Bugs fixed this part (so Codex doesn't re-hit them)
- **Keep indexed, don't truecolor** — GRFEditor/client mis-decode our truecolor frames (muddy, no box). Indexed = native, exact colors. (Part 1 §3.2.)
- **`main()` was truncated** to a bare `return` and the `if __name__ == "__main__"` guard was lost during edits → running the script did nothing (no output, no file). Restored `return build(args)` + the guard.
- **Stale `__pycache__`** made `python build_vision_grf.py` run old code → delete `tools/vision-grf/__pycache__` if output looks stale.
- **`_to_lib` Korean folder** — never hardcode `몬스터` via `\uXXXX` through nested heredocs (bytes mangle); replace the folder right after `sprite` using `chr(92)` for the separator.
- **Variable shadow** — a local named `baked` collided with the `baked` counter (`baked += 1` → "'int' object is not iterable"). Renamed.

## 2.7 GRFEditor testing note (important)
**Image Preview** shows raw sprite frames only — the marker appears as its own frame and body frames look normal (no marker). To see the marker ON the monster, use the **Animation Preview** tab (it composes the `.act` layers).

## 2.8 Runtime implications for Codex (VisionAssistMarkerDetector.cs)
- The box is now a **fixed-size marker** placed over the body (not a tight per-body box). Detection is unchanged (scan red box → decode color-code → mobId → name). Click target = box center, which is centered on the body via the ACT layer offset, so clicking the box center still hits the monster.
- If box center vs body drifts on some monsters, expose a per-mob click y-offset later; not needed yet.
- The label now carries **counter-element / size / race** (for the human); the machine identity is still the color-code. Codex's decoder is unaffected.

## 2.9 TODO
- **Port the marker approach to the C# picker** (`tools/VisionGrfPicker/`): it still does the old per-frame indexed bake in `Baker.BakeIndexed`. Needs a C# `ActEditor` (parse/serialize + add layer) + `build_marker_index` equivalent, then `BuildLibraryWorker` uses body-frames + marker + ACT-edit instead of per-frame bake.
- Full coverage: add the real client `data.grf` as a second `--source-grf` (box GRF misses ~190 newer sprites).

## 2.10 Commands
```bat
:: single monster test (Animation Preview to view)
python tools\vision-grf\build_vision_grf.py --client "tools\ocr-train\Grf" ^
  --source-grf "tools\ocr-train\Grf\ViviMobsBoxMasterofMagic.grf" --only 1832 ^
  --out "tools\ocr-train\Grf\output\test_ifrit.grf" --no-auto-sprite-map

:: full two-folder library (all monsters, marker approach)
python tools\vision-grf\build_vision_grf.py --client "tools\ocr-train\Grf" ^
  --source-grf "<clean data.grf>" --scope all --library ^
  --out "tools\ocr-train\Grf\output\VisionAssistLibrary.grf"
```

---

# PART 3 — Unified frames + centering + biggest-frame box (final)

**Problem found after Part 2:** monsters with **truecolor or mixed** SPR frames (e.g. Ferre/PERE1 = 34 indexed + 1 truecolor) fell to the old per-frame bake fallback → off colors + name-only label. Also the box landed off to the side (placed on the first `.act` layer, often a small accessory).

## 3.1 Unified SPR handling — keep ALL original frames, add one indexed marker
RO SPR can hold **both** indexed and truecolor frames in one file (`indexedCount` + `rgbaCount`, palette at EOF only if there are indexed frames). So the marker approach now works for every monster:
```python
def spr_split(data):   # -> (indexed=[(w,h,idx)], rgba_raw=[bytes per frame], palette|None)
    # indexed frames decoded; TRUECOLOR frames kept RAW (byte-for-byte) so colors are exact
def spr_emit(indexed, rgba_raw, palette):   # SP v2.1 = indexed(RLE) + rgba(raw) + palette
```
Build loop (ALL monsters, no fallback):
```python
idxf, rgbaf, pal = spr_split(raw_spr)                    # body frames untouched
pal = bytearray(pal) if pal else bytearray(1024)         # pure-truecolor had no palette -> make one for the marker
wb = max(idx widths + rgba widths); hb = max(idx heights + rgba heights)
mw,mh,midx,pal2 = build_marker_index(marker_lines(mob_id,name), wb, hb, pal, font, mob_id)
baked_bytes = spr_emit(idxf + [(mw,mh,midx)], rgbaf, pal2)   # marker is an INDEXED frame at index len(idxf)
act_bytes = _act_add_marker(act_raw, len(idxf), _act_body_offset(act_raw, idxf, rgbaf))
```
- The marker is always an **indexed** frame → its `.act` layer uses `sprType=0`. Truecolor body layers keep `sprType=1`. They coexist.
- **Why truecolor is now correct:** we never re-encode truecolor pixels (that byte order is the bug). We copy the original truecolor frame bytes verbatim; the marker is separate and indexed.

## 3.2 Box centered on the monster — `_act_body_offset`
Placing the marker at the *first* `.act` layer put the box on a small side layer. Now it uses the **largest sprite layer** in action0/frame0 (the actual body), whose offset is the body's center:
```python
def _act_body_offset(act, idxf, rgbaf):
    # scan action0/frame0 layers; the layer referencing the biggest sprite (indexed or rgba) is the body;
    # return its (x,y). RO draws a layer centered at anchor+offset, so this centers the marker box on the body.
```
`sprType` field offset in a layer is 28 (v<2.4) or 32 (v>=2.4). Verified composited: the red box surrounds the body, label on top.

## 3.3 Label = two rows (final)
```
Row 1:  "<name> - <counter-element>"   e.g. "Ferre - Fire",  "Ifrit - Water"
Row 2:  "<size> - <race>"              e.g. "Small - Demon",  "Large - Formless"
```
Counter element = what to ATTACK WITH (from `_COUNTER` map vs the monster's `element` in gamedata.json). Box size = the biggest body frame (`box_w,box_h = wb,hb`).

## 3.4 Validation
- Composited render (body + marker layer) confirms centering + colors for Ifrit (indexed) and Ferre (mixed).
- Robustness sweep: **150/150 monsters** pass the full pipeline (147 indexed + 3 mixed), 0 failures.

## 3.5 Reminders
- Run via a clean cache: delete `tools/vision-grf/__pycache__` if a `python script.py` run looks stale (the main-module/`if __name__` guard + cache bit us; both fixed, but clear cache if unsure).
- **GRFEditor: use Animation Preview** (composes `.act`), not Image Preview (raw frames).
- C# picker still on the old per-frame indexed bake → port PART 2/3 (spr_split/spr_emit + marker + ActEditor + body-offset).
- Full coverage: add real client `data.grf` as a 2nd `--source-grf` (box GRF misses ~190 sprites).

---

# PART 4 — RUNTIME (Codex): get the best results from these markers immediately

The generator now bakes, per monster, a **steady red box** (game-rendered on every frame, centered on the body) + a **corner color-code** + a human label. This makes the runtime side simple and fast. Update `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs` + the OcrService routing to exploit it.

## 4.1 What the marker guarantees (so the detector can be dumb & fast)
- The box is **pure red `(255,0,0)`**, **fixed size** (= biggest body frame), **centered on the body**, drawn by the engine every frame → **no jitter, no flicker, no tracker needed** in GRF mode.
- The **color-code** is 3 cells in the box's **inner top-left corner**: first cell starts at `(box.x + BOX_PX, box.y + BOX_PX)`, each cell `CODE_CELL(5) × CODE_CELL(5)`, laid left-to-right. `BOX_PX=2, CODE_CELLS=3, CODE_LEVELS=(48,96,144,192,240)`.
- The label ("Name - Element" / "Size - Race") is **white text** → it does NOT trigger the red mask, so it never interferes with box detection. Machine ignores it.

## 4.2 Detector loop (recommended)
```
1) red mask = hue/ratio test, NOT exact RGB (survives dark maps):
      r >= 70 && r - max(g,b) >= 35 && r >= 2*g && r >= 2*b
2) connected components -> rectangles; keep those with a hollow border
   (edge-filled high, interior-filled low) and min size (>= ~12px).
3) for each box, decode identity from the corner cells:
      for i in 0..2:
        cx = box.x + BOX_PX + i*CODE_CELL + CODE_CELL/2
        cy = box.y + BOX_PX + CODE_CELL/2
        sample a 3x3 MEDIAN at (cx,cy)   # robust to AA/lighting
      match the 3 sampled RGBs against manifest mobs[*].code using a
      per-cell brightness-normalized distance; accept if best < gate.
4) emit SceneEntity { box, name (from manifest), attackable=true }.
   Click/attack target = BOX CENTER  == body center (box is centered on body).
```

## 4.3 Rules that matter
- **No tracker in GRF mode.** The marker is engine-pinned; just find→decode→click. (Optional: require the same box for 2 consecutive frames before attacking, to avoid a mid-teleport click — a 1-frame guard, not ByteTrack.)
- **Never drop a boxed-but-undecoded monster.** If the box is found but the code fails to match: emit `MobId=-1, Name="Monster", attackable=true`. A red box always means a monster is there.
- **Click the box CENTER** — it equals the body center now (generator centers the box via `_act_body_offset`), so the click lands on the monster.
- **Manifest path** must point at the `VisionAssist.manifest.json` that shipped with the loaded GRF (OCR Reader → manifest path). id→name lives there.

## 4.4 Observability (do this first to tune fast)
Log per scan, separately:
```
visionAssist=true boxDet=<rectangles> codeReads=<decoded> nameUnknown=<boxDet-codeReads> targetSource=grf
```
- `boxDet` high, `codeReads` low  ⇒ cells unreadable (dark map / AA): enlarge `CODE_CELL 5→8` + 3×3 median (regenerate the GRF after the constant change on BOTH sides), or move cells to the box interior.
- `boxDet` low  ⇒ red mask too strict, or the box render/threshold; loosen the ratio test.

## 4.5 Keep both sides in sync
Generator and detector MUST share: `BOX_PX=2`, `CODE_CELL=5`, `CODE_CELLS=3`, `CODE_LEVELS`, pure-red box color, and the exact `color_code(mobId)` mapping. Change one → change both → rebuild the GRF.

## 4.6 Net effect
With the steady game-rendered box + corner code, GRF mode should give **instant, exact** monster location + identity with almost no CPU (a color scan), and clicking the box center attacks the right monster — the whole point of the Vision Assist GRF.
