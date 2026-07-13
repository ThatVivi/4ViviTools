# Claude Archive Research Index

Source archive: `D:\vs code clone 4rtool\claude`

This is the working index for the month-long Claude research archive. Treat it as a source library,
not as code to copy blindly. The archive contains generated 4ViviTools packages, original/third-party
tool source, client GRF data tables, OCR papers/guides, model packs, rAthena extracts, and Claude
session logs.

## Archive Shape

Top-level:

| Path | Meaning | Use |
|------|---------|-----|
| `.claude/` | Claude session state, tasks, plugins, project transcripts. | Timeline/context mining only. Do not ship. |
| `audit.jsonl` | Full audit/session log, about 77 MB. | Useful for reconstructing decisions and earlier fixes. Very noisy. |
| `outputs/` | Claude-generated reports, packages, extracted code, model packs, rAthena extracts. | Highest-value working area. |
| `uploads/` | Original files provided to Claude: zips, PDFs, GRF tables, logs, fonts, tools. | Source-of-truth inputs. |
| `uploads/all/` | Large extracted bundle of many RO-related repos and client/server data. | Search selectively; contains a lot of dependency noise. |
| `uploads/GRF INFO/` | Client-side GRF/Lua/text data tables. | High value for pickers, OCR dictionaries, icons, maps, skill metadata. |

Scale observed in the first inventory:

- More than 13,000 paths in the archive.
- Large binary anchors include `outputs/gem.grf` (~3.0 GB), `outputs/MyGRF.zip` (~719 MB),
  `uploads/all.zip` (~716 MB), and many extracted third-party repos.
- File-type hotspots: `.txt`, `.cpp`, `.hpp`, `.cs`, `.png`, `.bmp`, `.yml`, `.lua`, `.lub`,
  `.json`, `.md`, `.pdf`, `.zip`, `.rar`, `.grf`, `.spr`, `.act`, `.onnx`.

## Highest-Value Documents Read

| File | What it contributes |
|------|---------------------|
| `outputs/4ViviTools_Engineering_Report.md` | Current architecture and completed work: Avalonia/.NET 8 app, OCR worker, PP-OCRv5, YOLO11n, ArcFace icon bank, Smart Bot, autopot, Discord RPC, remaining UI/wiring tasks. |
| `outputs/PROJECT_PROGRESS.md` | Same work as a progress log with precise file-level changes and remaining tasks. |
| `outputs/2026-06-20-ocr-tuning-and-training-design.md` | OCR tuning/training architecture: in-app knobs, synthetic RO-font data, real crop export, train button, ONNX export. |
| `outputs/2026-06-20-ocr-tuning-and-training-PLAN.md` | Implementation task list for OCR training pipeline and app wiring. |
| `uploads/Ragnarok Online OCR Engineering Guide.pdf` | Strong architecture guidance: DXGI capture, frame manager, ROI manager, non-OCR CV paths, knowledge engine, temporal engine. |
| `outputs/4RTools-Improvement-Roadmap.md` | Legacy 4RTools improvement map: scanner, pointer chains, AoB, human timing, SP autopot, multi-client, offset cloud. |
| `outputs/4rVivi-Feature-Map-and-Fallbacks.md` | Fallback-chain idea: DXGI -> WGC/BitBlt, memory -> OCR -> cache, target provider fallbacks, feature status. |
| `outputs/4rVivi-Ultimate-Spec.md` | Data-service vision: rAthena DB, Divine Pride enrichment, skill delays, farm lookup, bot target intelligence. |
| `outputs/4rVivi-Scanner-Bot-Integration.md` | Earlier scanner/bot integration guide and Lemon/sickpot parity checklist. |
| `uploads/Improvement 1.md` | Older combined tool vision and feature comparison across LemonROTools, 4RTools, and ro-tools. |
| Claude session `outputs/4ViviTools_Change_Log.md` | Detailed June 10-28 change log: project phases, exact file changes, build/infra fixes, task registry, and prioritized remaining work. |

## Detailed Change Log Digest

The attached Claude change log is the most concrete chronology in the archive. It reconstructs:

| Phase | Dates | Main result |
|-------|-------|-------------|
| 0 | Jun 10-11 | Audited upstream 4RTools and wrote the improvement roadmap: scanner over hardcoded offsets, pointer/AOB signatures, humanized timing, CI, feature parity. |
| 1 | Jun 12 | Re-platformed to Avalonia 11 / .NET 8, MVVM + DI, dark theme, self-contained exe, renamed to 4rVivi. |
| 2 | Jun 14-15 | Added rAthena data layer, Divine Pride enrichment, class data, GRF icon extraction, calculator groundwork, and early targeted fixes. |
| 3 | Jun 17-18 | Added signature/pointer memory engine, scanner cleanup, crash logging/infra, and default Discord RPC application ID. |
| 4 | Jun 20 | Designed OCR tuning and one-click RO OCR training workflow. |
| 5 | Jun 23-27 | Finished calculator UI and assembled the first vision pack: YOLO entities + ArcFace icons. |
| Current session | Jun 27-28 | Productionized OCR/vision/bot work: faster OCR training, client-only capture, staged calibration, region profiles, temporal voting, YOLO11n detector, icon-bank repair, %-only autopot, Smart Bot attack/move model, input backends, clear-all-keys, Discord RPC, and build-copy fixes. |

Concrete fixes called out by the change log:

- `build_training_set.py`: fixed the cap bug that rendered the entire ~27k item pool.
- `train_export.py` / `RUN_TRAINING.bat`: stabilized training for an RTX 2060 SUPER 8 GB by using batch 16 and faster eval settings.
- `OcrService.cs`: client-area capture only via `PrintWindow` + `GetClientRect` + `ClientToScreen`; DXGI path exists.
- `CalibrateToValue`: reduced from about 4,950 reads to about 520 reads.
- `RegionProfiles.cs` and `TemporalVotingService.cs`: added role-specific preprocessing and per-region string voting.
- `merge_datasets.py` / `train_yolo.py`: merged 21 Roboflow datasets into 7 classes and trained YOLO11n.
- `EntityDetector.cs` / `Program.cs` / `RapidOcrClient.cs`: multi-class YOLO decode and app parsing, with icon lookup only for monsters.
- `build_icon_model.py` / `build_monster_names.py`: removed map sprite refs and added multi-frame monster refs.
- Icon bank: repaired a 1,318-row embedding/label misalignment, producing 12,317 aligned refs.
- `AutopotEngine.cs` / `HealthReader.cs`: removed flat HP box, compute HP percent from HP/MaxHP, reject bad reads.
- `SmartBotEngine.cs`: normal attack is click; skill attack is skill key then click; roam waits for OCR position arrival.
- `MouseSender.cs` / `KeySender.cs`: humanized mouse and three input backends: `SendInput`, `MouseKeyEvent`, `PostMessage`.
- `EngineHub.cs` and all engines: added global `ClearKeys()`.
- `DiscordPresenceBootstrap.cs`: presence comes from live OCR/role values.
- `4rVivi.App.csproj`: recovered `CopyOcrWorker`, Vortice refs, and unsafe settings after D: mount truncation.

The change log's task registry reports:

- Original roadmap pillars mostly landed: memory scanner, AoB/signatures, CI, human timing, autopot, pointer chains, Smart Bot, status/debuff engine, dark UI, crash logging/update service, and .NET 8 migration.
- Still incomplete or partial: multi-client manager UI, crowd-sourced offset cloud, Smart Bot/OCR config persistence, skill-grid wiring, bot layout/dedup, template matching consumers, per-region threshold consumers, and heavier OCR acceleration/model work.

Prioritized remaining work from the change log:

1. Persist Smart Bot + OCR config.
2. Convert the skill key grid into bot skill instructions with picker-backed skill names.
3. Wire Smart Bot to OCR roles for skills, pots, and ammo.
4. Complete the UI sweep: 4-digit numeric boxes, searchable pickers, and roam/click-box overlay toggle.
5. Deduplicate merged bot controls and fit the bot UI to 1920x1080.
6. Consume `TemplateMatchService` for fixed UI elements.
7. Apply `RegionProfiles` thresholds fully in the live OCR path.
8. Validate/re-run RO OCR fine-tune on the user's GPU.
9. Verify DXGI, super-resolution, DirectML/CUDA, and PP-OCRv5 server model on real hardware.
10. Push/upload the full local build to GitHub/release once green.

Important working constraint repeated by the change log:

- On the D: mount, `.cs` and `.axaml` edits were safer when authored through shell/python and then verified, because earlier editor writes corrupted/truncated files. For this Codex thread, continue to use careful patching and always verify build/project files after edits.

## Source/Tool Repositories Found

Representative high-value extracted repos under `uploads/all/`:

| Repo/folder | Why it matters |
|-------------|----------------|
| `4RTools-main`, `outputs/4rtsrc/4RTools-main` | Original C# 4RTools model/utils source: autopot, spammer, buffs, macros, memory reader, WinForms UI. |
| `ro-tools-main` | Python/PyQt RO automation with process selection, offsets, driver-mode notes, auto-tele/element, macros, sound alerts. |
| `rathena-master_2` | Server DB and mechanics source. Use for game data, formulas, mobs, skills, map flags. |
| `ROenglishRE-master`, `ClientSide-master` | Client translation/Lua data useful for names, item descriptions, UI/client metadata. |
| `GRFEditor-main`, `RagnarokSDE-master`, `ActEditor-main`, `HL-Texture-Tools-master` | GRF/sprite/ACT tooling references. |
| `RapidOcrNet-master` | OCR runtime reference. Compare only when debugging RapidOcrNet behavior. |
| `Nemo-master` | Client patching reference. Keep separate from 4ViviTools core. |
| `mvp_tracker-main`, `Ragnarok-MVP-Timer-main`, `ragnarok-mvp-tracker-master` | MVP timer UX/data references. |
| `Korangar-main`, `RagnarokRebuildTcp-master`, `roBrowserLegacy-master` | Client/render/protocol research references, not direct app dependencies. |
| `Interception-master`, `RagnaController-main`, `Ragnarok-Autoit-master`, `Autohotkey-master` | Input automation references. Use for UX/config ideas, not for anti-cheat bypass. |

## GRF/Client Data Notes

High-value files in `uploads/GRF INFO/`:

| File | Use |
|------|-----|
| `skillnametable.txt` | Client skill name mapping. Decodes as CP949. Useful for skill pickers and localized OCR dictionaries. |
| `leveluseskillspamount.txt` | Per-skill SP cost by level. Useful for smart bot SP gating and calculator. |
| `idnum2itemdisplaynametable.txt`, `num2itemdisplaynametable.txt` | Item display names. Useful for item/gear/pot/ammo pickers. |
| `idnum2itemdesctable.txt`, `num2itemdesctable.txt` | Item descriptions. Useful for search, gear suggestions, and calculator hints. |
| `mapnametable.txt`, `exceptionminimapnametable.txt` | Map display names. Some map files need non-UTF fallback decoding. |
| `iteminfo.lub`, `itemdbnametbl.lub` | Lua-side item info. Useful when building a richer client-data importer. |
| `jobname.lub`, `pcjobname.lub`, `pcidentity.lub` | Job/class display and identity mappings. |
| `skilldesctable.txt`, `skilldesctable2.txt`, `skilltreeview.txt` | Skill descriptions/tree metadata for UI suggestions. |
| `hotkey.lub`, `hotkey_v2.lub` | Client hotkey data reference. Useful for matching RO keyboard conventions. |

Encoding reminder:

- Some GRF text tables decode as `utf-8-sig`.
- Some decode as `cp949` / Korean Windows encodings.
- Importers should attempt `utf-8-sig`, `cp949`, `euc_kr`, then a loss-tolerant fallback.
- Do not judge table usefulness from mojibake shown by a default PowerShell console.

## OCR / Vision Takeaways

The local PDF guide and Claude docs agree on the main design:

1. Capture should be DXGI-first, pixel-stable, client-window scoped.
2. Never OCR the whole frame. Use ROI/marks and cache stable values.
3. Do not OCR bars, icons, target boxes, status icons, or skill icons. Use pixel reading,
   template matching, YOLO, and icon recognition.
4. Use OCR for text regions only: monster names, map names, class/name fields, chat-like text,
   and numeric HUD fields when pixel/bar methods are not better.
5. Add a knowledge layer: snap fuzzy OCR to real game strings per role.
6. Add temporal smoothing/voting so one bad frame does not flip bot state.
7. Hard-example harvesting is required for the "monster reads as numbers" problem: crop,
   expected label, OCR role, preprocess profile, and model output should be saved together.

Current repo alignment:

- Most architecture is already present: PP-OCRv5 ONNX worker, DXGI path, role profiles,
  hard-example staging, YOLO detector, icon bank, and temporal/knowledge concepts.
- Remaining high-impact wiring from the archive is still: template matching consumer,
  per-region thresholds end-to-end, OCR hard-example workflow, and picker dictionaries from
  GRF/rAthena data.

## Smart Bot / Automation Takeaways

From `ro-tools-main` and the Claude specs:

- End-user setup should avoid typing. Use process picker, key recorder boxes, skill/item pickers,
  and visible driver/virtual-device status.
- Smart Bot skill buttons should mirror familiar RO hotkey grids and be wired directly to attack logic.
- Useful RO-Tools features to preserve: auto-tele by monster ID, auto-element based on nearby monster,
  "use buffs/items only while attacking", sound alerts, debug tab, server links, and vote reminders.
- 4RTools legacy autopot used `PostMessage` and very small delays. 4ViviTools should keep the
  friendlier configuration but use guarded percent reads, cooldowns, and humanized input.
- reWASD/virtual-driver status belongs in Settings or Smart Bot as an explicit health indicator.

## Game Data / Calculator / Divine Pride Takeaways

The archive repeatedly points to the same data strategy:

- rAthena is the mechanics source: mobs, skills, items, formulas, map flags.
- Client GRF/Lua tables are the display/source-of-truth for what users see.
- Divine Pride is an enrichment layer: icons, sprites, spawn maps, MVP info, and web links.
- Calculator wiring should use one shared data service so Smart Bot, skill pickers, gear pickers,
  buffs, usable items, Discord RPC, and database views all resolve the same item/skill/mob/map IDs.
- `outputs/ratx/` contains generated `final.json`, `slim_gamedata.json`, `out_gamedata.json`, and
  scripts (`gen2.py`, `build_gamedata.py`, `combos.py`, `ratios.py`) that can be compared with the
  current repo's game-data builder.

## Model / Release Packaging Gotchas

The archive includes a prior fix in `audit.jsonl`: PaddleOCR fell back to Tesseract because the
worker/models were not copied next to the app. Keep this invariant in every release:

- Published app must include the OCR worker folder.
- Worker must contain `models/v5`, `models/icons`, and `models/yolo` when the full vision pack is used.
- Large runtime models should stay out of Git unless the repo intentionally allows them.
- `outputs/visionpack/README.txt` documents the standalone model pack layout.

Vision pack files observed in `outputs/visionpack/models/`:

- `v5/ch_PP-OCRv5_mobile_det.onnx`
- `v5/ch_ppocr_mobile_v2.0_cls_infer.onnx`
- `v5/latin_PP-OCRv5_rec_mobile_infer.onnx`
- `v5/ppocrv5_latin_dict.txt`
- `icons/icon_embedder.onnx`
- `icons/icon_refs.bin`
- `icons/labels.txt`
- `icons/icon_meta.json`
- `yolo/entity.onnx`
- `yolo/entity_meta.json`

## Actionable Backlog from Archive

Priority order for 4ViviTools:

1. Build a GRF/client-table importer for skill/item/map/job names with encoding fallback.
2. Feed imported names into searchable pickers and role-specific OCR dictionaries.
3. Finish Smart Bot skill grid wiring: key recorder + skill picker + cooldown/SP-cost metadata.
4. Add visible virtual driver/reWASD status and install guidance in Settings/Smart Bot.
5. Wire `TemplateMatchService` into concrete consumers for fixed RO UI elements.
6. Finish per-region OCR threshold routing and expose compact advanced tuning only where needed.
7. Save hard examples automatically when monster-name OCR emits mostly digits or low-confidence text.
8. Compare `outputs/ratx/*.json` and scripts with current `tools/build_gamedata.py` for missed formulas/data.
9. Use GRF item/skill descriptions to improve calculator suggestions and gear/buff/useable item search.
10. Add release validation that checks the worker/model files are present before publishing.

## What Not To Import Blindly

- Full extracted repos and dependency folders from `uploads/all/`.
- `.claude` session internals, plugin caches, or audit HMAC metadata.
- Client patching / injection / bypass ideas from Nemo or similar tools.
- Memory-write helpers from old 4RTools unless there is a clear, user-facing, non-destructive reason.
- Huge binaries like `gem.grf`, raw zip archives, RARs, and duplicate generated packages.
