# 4ViviTools — Project Improvement Plan (every perspective)
**Date:** 2026-07-12 · **Audience:** Codex + maintainer · **Basis:** `PROJECT_KNOWLEDGE_BASE.md`
**Goal:** turn a working-but-dirty repo into a lean, reproducible, self-contained, well-documented product. Ordered by impact. Each item = **What / Why / How (concrete)**.
**Scope guard:** the input-injection stack (VIIPER / FakerInput / ViGEm / reWASD virtual-HID) is intentionally **out of scope** — do not "improve" or extend it. Everything below is vision/OCR/tooling/build/docs.

---

## 0. Codex working agreement — add `AGENTS.md` at repo root (do this first)
**Why:** Codex reads a repo instruction file and behaves far better with explicit conventions. The KB §22 already lists them — promote them to a machine-followable file so every future run starts aligned.
**How:** create `AGENTS.md` (repo root):
```md
# Agent rules for 4ViviTools
- Search with ripgrep (`rg`) first. Edit with apply_patch.
- Windows/PowerShell: never use bash heredoc (`python - <<'PY'`). Use temp .py files or `python -c`.
- This repo is intentionally dirty — never revert unrelated user changes.
- Deliver RELEASE builds, not just source: `dotnet publish -c Release -r win-x64 --self-contained`.
- Korean sprite paths + Windows console: keep UTF-8; never hardcode `몬스터` bytes.
- Do NOT touch the input-injection backends (VIIPER/FakerInput/ViGEm/reWASD).
- Vision Assist GRF mode is PRIMARY when enabled; YOLO/OCR is the fallback.
- Generator + detector share BOX_PX=2, CODE_CELL=5, CODE_CELLS=3, CODE_LEVELS, color_code(). Change one -> change both -> rebuild GRF.
- Keep UI beginner-friendly; show `-1 = Auto`; hide OCR internals.
- After any code/const change, update docs/CODEX-MAP.md in the same commit.
```
Also add a short `.github/copilot-instructions.md` / `.cursorrules` pointing to `AGENTS.md` so any assistant picks it up.

---

## 1. Repo hygiene & skeleton (the repo is "very dirty")
**Why:** stale exes, duplicated artifacts, and mojibake docs cause "testing confusion" (KB §19). A clean tree makes every later improvement cheaper and makes Codex reason correctly.
**How:**
- **`.gitignore`** (add/verify): `bin/ obj/ publish/ **/__pycache__/ *.pyc artifacts/ **/output/*.grf **/_cache/ *.tmp *.bak *.prebak *.spr *.act *.grf` (keep sample GRFs only where needed), plus training data (`tools/ocr-train/{TrainingData,user_images,yolo_real,real_frames,false_positive_frames}/`), model binaries, and `ocr_export/` venv.
- **One artifacts dir**: `artifacts/` for all publish output; delete scattered `bin/Release/.../publish` copies. Ship from `artifacts/` only.
- **Kill mojibake**: convert `¸ó½ºÅÍ`/broken arrows to clean UTF-8 or ASCII (`->`). Save all docs as UTF-8 (no BOM).
- **Dead-code sweep** (evidence-based): list unreferenced C# types + unused Python scripts (`run.py` vs `run_patched.py`, `train_all.py` vs `train_export.py`, the truecolor `bake_frame/bake_frames` now that the marker path is universal). Produce `docs/CLEANUP-REPORT.md` with the grep proving each is unreferenced before deleting.
- **Target skeleton:**
```
/src            (4rVivi.Core, .App, .OcrServer, RapidOcrNet, Plugins.Abstractions)
/tests
/tools          (vision-grf, VisionGrfPicker, ocr-train)   <- dev tools only
/models         (or Git LFS)   <- entity.onnx, icons, v5 (NOT in normal git)
/docs           (KB, CODEX-MAP, USER_GUIDE, specs/)
/artifacts      (gitignored, release output)
AGENTS.md  .gitignore  README.md  Directory.Build.props  *.sln
```

---

## 2. Dependencies — pin, split, stop runtime self-install
**Why:** `requirements.txt` mixes training + tooling; the GRF builder self-pip-installs Pillow at runtime (bad in a shipped exe / offline); ONNX/CUDA versions are unpinned (fragile GPU).
**How:**
- **Split requirements**:
  - `tools/ocr-train/requirements.txt` — heavy training stack (paddle, ultralytics, supervision, lapx…), pinned exactly.
  - `tools/vision-grf/requirements.txt` — just `Pillow==<pin>`.
  - Add a top comment with the tested Python version (e.g. 3.12).
- **Remove runtime `_ensure(...)` pip-install** from shipped paths. For dev, document `pip install -r`. For the shipped picker, Pillow isn't needed (C# port — see §4).
- **Pin the GPU chain** in a doc/table: NVIDIA driver ≥ X, CUDA 12.x, cuDNN 9.x, ORT 1.2x, VC++ redist — matching what §11/§ runtime needs. Add a `tools/check_gpu.ps1` that verifies `nvcc`, `cudnn64_9.dll`, `cublasLt64_12.dll` on PATH.
- **NuGet**: keep versions in `Directory.Packages.props` (central package management) so Core/App/tools don't drift. Pin `System.Text.Encoding.CodePages` (cp949), ORT, Vortice, CommunityToolkit.Mvvm, Avalonia.

---

## 3. Standalone .exe — make each shippable artifact truly one-file, no installs
**Why:** end users must install nothing. Today: main app (Avalonia self-contained — good), picker (C# self-contained — good, JSON embedded — good), but the **Python GRF builder still needs Python**, and the app relies on external `models/` + `OcrServer` copies.
**How:**
- **App (`4rVivi.App`)**: keep single-file self-contained publish; verify `CopyOcrWorker` stages `OcrServer` + `models/` next to the exe; add `PublishReadyToRun=true` for faster startup; set assembly/file `Version` from a single `Directory.Build.props`.
- **Picker (`VisionGrfPicker`)**: already single-file self-contained with embedded data. Keep. Add an icon + version. Verify `PublishSingleFile + IncludeNativeLibrariesForSelfExtract` bundles System.Drawing native bits.
- **GRF builder**: **port to C#** (the picker already has 90% — `Spr`, `GrfArchive`, `Baker`). Add the marker-layer + ACT editor + `spr_split/spr_emit` (see §5) so the **picker's "Build Library" is the single builder** and Python is dev-only. Result: zero Python for end users. (If you must keep Python short-term, ship a **PyInstaller** one-file exe built on Windows — end user installs nothing; but the C# port is the real fix.)
- **Signing**: unsigned exes trip SmartScreen. Note a future code-signing step (even a self-signed/EV cert) in the release doc.

---

## 4. Paths — one resolver, no hardcoded absolutes, frozen-aware
**Why:** scripts hardcode `tools\ocr-train\Grf\...`; data lookup differs dev vs frozen; Korean folder handling was fragile.
**How:**
- **Single path resolver** per side:
  - Python: `_find_data(name)` (already exists) — searches dev tree, script dir, PyInstaller `_MEIPASS`. Use it everywhere; never `Path("hardcoded")`.
  - C#: `AppContext.BaseDirectory` first, then embedded-resource fallback (already in `Catalog.ReadData`). Reuse a single `DataPaths` helper.
- **Config, not constants**: game folder, GRF path, manifest path all live in `%AppData%\4rVivi\settings.json` (already partly there). Never bake a user machine path into code or a bat; bats derive from `%~dp0` (strip trailing `\`).
- **Korean-safe**: never hardcode `몬스터`; derive the folder after `sprite` (as `_to_lib` now does). All file I/O uses cp949 for GRF names, UTF-8 for JSON/docs.
- **Normalize separators** at the boundary (`\` internally for GRF paths, `/` for display), one `Norm()` per language.

---

## 5. Vision Assist GRF — unify, harden, speed up
**Why:** two builders (Python + C# picker) can drift; the C# picker still uses the *old per-frame* bake; Python pixel loops are slow for 2,400 mobs; manifest has no schema version.
**How:**
- **Port the marker algorithm to C#** and make the picker the single builder (removes Python for users): `spr_split/spr_emit` (indexed + truecolor-raw + mixed), `build_marker_index` (2-row label, biggest-frame box, palette slots), an `ActEditor` (parse/serialize + add layer, proven by a **byte round-trip test**), and `_act_body_offset` centering. Keep the Python script as the reference/dev tool with a shared **golden test** so both produce identical output for a fixed mob set.
- **Manifest schema + version**: `{"schema":2, "codeCells":3, "codeCell":5, "boxPx":2, "codeLevels":[...], "boxColor":[255,0,0], "mobs":{...}}`. Detector checks `schema` and reads constants from the manifest instead of hardcoding — so a constant change only needs a rebuild, not a code edit on the runtime.
- **Performance**: vectorize the Python marker raster with `numpy` (replace per-pixel Python loops) — cuts full-library build from minutes to seconds. In C#, use `LockBits`/spans, not `GetPixel`.
- **Coverage**: accept multiple `--source-grf` and default to `DATA.INI` order + client `data.grf` so the ~190 missing sprites are covered. Log a coverage summary (`baked/total`, `sharedSprites`, `unmapped ids`).
- **Robustness gates**: keep the SPR/ACT/GRF round-trip + `verify OK`; add a `--selftest` that bakes a synthetic mob, packs, reopens, and asserts frame counts + code decode.
- **Lighting-proof code decode**: confirm per-cell brightness-normalized matching; if dark maps ever fail, bump `CODE_CELL 5->8` + 3×3 median sample (both sides + rebuild).

---

## 6. Testing & CI — make green mean something
**Why:** many fixes were "static-verified, not compiled"; regressions are easy in a dirty repo.
**How:**
- **Codec round-trip tests** (xUnit + pytest): SPR decode→encode identity; ACT parse→serialize identity (byte-for-byte, already proven manually — lock it in a test); GRF write→read identity; marker `color_code`→decode identity **including 0.6/0.75/0.9 brightness** (proves cave lighting).
- **Golden files**: check a tiny fixture GRF (1–2 mobs) into `tests/fixtures/`; assert the builder reproduces it byte-stable (or frame-count/label/code-stable).
- **CI (`.github/workflows/build.yml`)**: matrix — `dotnet test` (Core.Tests) + `dotnet build -c Release` + `python -m pytest tools/…` + `python -m py_compile tools/**/*.py`. Publish the self-contained exes as artifacts on tags. Fail on warnings for the shipping projects.
- **Detector eval**: a headless test that feeds a rendered baked frame to `VisionAssistMarkerDetector` and asserts `boxDet==1, codeReads==1, name==expected` (and an undecoded box → `MobId=-1`).

---

## 7. Runtime detector (GRF mode) — best results now
**Why:** the markers are steady + coded; the detector can be simple, fast, tracker-free. (Full detail in `specs/2026-07-12-vision-grf-session-log.md` §Part 4.)
**How (summary):** hue/ratio red mask → rectangles → sample the 3 corner cells (median) → brightness-normalized match vs manifest → emit `SceneEntity`, click **box center** (= body center). No tracker in GRF mode; optional 1-frame guard. Never drop a boxed-but-undecoded mob (`MobId=-1, "Monster"`). Log `boxDet/codeReads/nameUnknown` separately. Point the runtime at the matching `VisionAssist.manifest.json`.

---

## 8. Documentation — one source of truth, auto-checked
**Why:** stale `VisionAssist.grf` vs `VisionAssistLibrary.grf` references, mojibake, drift.
**How:**
- **`PROJECT_KNOWLEDGE_BASE.md` = canonical**; `CODEX-MAP.md` = quick index; `USER_GUIDE.md` = end-user; `specs/` = dated design/session logs. State this hierarchy at the top of each.
- **Rename flow docs**: use `VisionAssistLibrary.grf` where the library/picker flow is meant; keep `VisionAssist.grf` only for the final loaded file.
- **Generate what you can**: a small script emits the constant table + file map into `CODEX-MAP.md` from the actual source so it can't drift.
- **Screenshots** in `USER_GUIDE.md` for the picker + GRFEditor Animation Preview.
- **CHANGELOG.md**: one line per shipped release.

---

## 9. Observability & config
- **DebugTrace**: keep structured, greppable lines (`key=value`). Add a `--verbose` and a rotating log so `DebugTrace.log` doesn't grow unbounded.
- **Feature flags in settings**: `VisionAssistGrf`, `UseDxgiCapture`, runtime (`cuda|directml|cpu`), `entityIntervalMs`, thresholds — all in `settings.json`, surfaced in UI with `-1 = Auto`.
- **Worker health**: auto-restart the OCR/CUDA worker on crash; a single visible status; never silently fall to Windows OCR without a toast.

---

## 10. Prioritized roadmap
| P | Item | Effort | Payoff |
|---|---|---|---|
| P0 | `AGENTS.md` + `.gitignore` + artifacts dir | S | every future run cleaner |
| P0 | Port marker builder to C# → picker is the only builder (no Python for users) | L | true standalone |
| P0 | Codec round-trip + brightness-decode tests + CI | M | stop regressions |
| P1 | Manifest schema/version; detector reads constants from it | S | no dual-edit rebuilds |
| P1 | Split/pin requirements; remove runtime pip; GPU check script | S | reproducible env |
| P1 | Multi-source GRF (client data.grf) for full coverage | S | 100% monsters |
| P1 | numpy-vectorize / LockBits the marker raster | M | seconds not minutes |
| P2 | Dead-code + mojibake + stale-exe cleanup (`CLEANUP-REPORT.md`) | M | clarity |
| P2 | Doc source-of-truth + auto-generated CODEX-MAP + screenshots | M | onboarding |
| P3 | ReadyToRun + versioning + signing note | S | polish |

---

## 11. Explicitly out of scope (do not change)
- The virtual-HID input backends (VIIPER/FakerInput/ViGEm/reWASD) and any driver-routing to defeat input filtering. Legitimate work only: the three standard input backends, and input **observability** (log which backend delivered a click).

## 12. Definition of done for "best results immediately"
1. `dotnet build/test` green in CI; `pytest` green.
2. `VisionGrfPicker.exe` (one file) builds a library, promotes picks, saves in place — **no Python installed**.
3. In-client: baked monsters show a centered red box + "Name - Element" / "Size - Race" + corner code; `VisionAssistMarkerDetector` logs `targetSource=grf`, `boxDet==codeReads` on a clean map, and the bot clicks the box center onto the monster.
4. Docs updated (KB + CODEX-MAP) in the same commit as any constant/path change.
