# 4ViviTools — Upgrade Status & Roadmap

_Snapshot of where the project stands after this work session, what changed, and what's left to upgrade._

---

## 1. What was built / fixed this session

### Vision & OCR
- **Trained YOLO11n multi-class detector** from the 21 paid Roboflow datasets (merged → 3,783 imgs, 7 classes: monster/player/loot/portal/target/target_hp/player_hp). Reached ~0.90 mAP50. Exported `entity.onnx` + class meta and **shipped into the app** (the export copy had silently failed; fixed by hand).
- **Multi-class detector plumbing** end-to-end: `EntityDetector` argmaxes the class; worker → app → `OcrService` carry it; loot/portal/target labelled by class, monsters named by the icon embedder.
- **Icon bank cleanup**: removed all `map__` refs (was colliding with monsters) and repaired a 1,318-row label/vector misalignment → 12,317 clean refs.
- **Multi-frame monster sprites**: `build_monster_names.py` now renders up to 6 poses per monster (idle + back/side) so a turned monster still matches; `build_icon_model.py` exports a multi-reference bank.
- **Detector thresholds** set to the guide's RO-exact values; **RegionProfiles** per-region pipelines wired into preprocessing; **TemporalVotingService** (last-20-frame majority vote) wired into the text readout.
- **Client-only capture**: `CaptureWindow` now grabs the **client area** (PW_CLIENTONLY) and the overlay + marking screenshot align to it.
- Raised icon-match (0.45) and YOLO box (0.55) confidence floors.

### Bot & automation
- **Attack model fixed** to RO semantics: skill-key → click for a skill kill, plain click for a normal attack (was backwards).
- **Move-then-wait cadence**: bot clicks to walk, then **waits on PosX/PosY** (`WaitUntilArrivedAsync`) until the character stops, then re-scans — no more re-clicking before it moves.
- **Autopot**: now **%-only** (Flat removed from logic + UI), HP% computed from **HP/MaxHP**, with an anti-spam guard against garbage reads.
- **player_hp** disabled (was feeding bad HP% → autopot spam).
- **Human mouse movement** (eased path + jitter) and a real held down→up click.
- **"Clear ALL keys"** button — blanks every hotkey across every engine (bot, autopot, spammer, buffs, ATK×DEF, auto-stand/ygg/debuff, bot-farm).
- **Input method selector** (SendInput / mouse_event+keybd_event / PostMessage) so input can be matched to a server that accepts it.
- Marking list cleaned: moving things (monster/target/loot/character/posture/pet) removed → detected live, not boxed.

### Other
- **Discord RPC** driven from live stats: HP/MaxHP, SP/MaxSP, pos, name, class, idle/moving/attacking, map-known gating, per-map art key.
- **Training pipeline** hardened: fixed the 4-day ETA bug, FP32 stability, batch 16, faster eval, clean YOLO11n runner, robust `merge_datasets.py`.
- Guide §1/§7/§8 status docs under `docs/ocr-roadmap/guide/`.

---

## 2. Current state of the major systems

| System | State |
|---|---|
| OCR text reader (HP/SP/levels/name/map/pos) | Working; stock PP-OCRv5 latin model (RO fine-tune optional via RUN_TRAINING.bat) |
| Monster/loot/player/portal detection (YOLO11n) | Trained + shipped; live full-frame scan |
| Icon naming (which monster) | Multi-frame bank, maps removed |
| Smart bot logic (target, attack, walk, loot, teleport, autopot) | Implemented + fixed; gated on OCR Reader running |
| Input delivery | 3 standard backends selectable; works on clients that accept OS input |
| Discord presence | Wired to live stats |
| Persistence (#35) | OCR toggles done; bot-config half still pending |

---

## 3. What still needs upgrading (pending)

**UI sweep (requested):**
- Make every numeric box 4-digit wide + expandable.
- Convert all free-text input boxes → searchable picker/dropdowns.
- Skill-Spammer key grid → wire as bot **skill instructions** (per-key config).
- **Click-box overlay**: checkbox to draw the walk/roam box the bot will click in.
- Dedup controls across merged Bot sections (#36); fit merged Bot UI to 1920×1080 (#37).

**Engine / features:**
- #35 finish bot-config persistence (save/restore the Smart Bot settings).
- #33 key grid + OCR-filled skill table; #34 verify full bot↔OCR wiring.
- #50 wire TemplateMatchService into a consumer (skill-bar/hotkey detect).
- #56 wire RegionProfiles per-region **detector thresholds** (preprocessing already wired).
- #38 DXGI capture (code shipped, excluded until Vortice packages added — see docs/ocr-roadmap/dxgi-wiring.md).
- #48 super-res, #49 YOLO buff icons, #53 PP-OCRv5 server rec, #54 DirectML/CUDA provider, #57 LAB-CLAHE.

**Guide implementation:** §2/§4/§5/§6 sections remain (status docs exist for §1/§7/§8).

---

## 4. Input layer — note

The input layer exposes one clean seam (`KeySender`/`MouseSender` behind `EngineHub.InputMethod`) with three standard OS backends. A virtual-controller / reWASD routing backend is **not** part of this package. On a server without an anti-cheat, the existing `SendInput` backend drives the character directly.

---

## 5. Suggested next step

Get a green build, confirm the bot acts on a server that accepts OS input, then do the **UI sweep** (4-digit boxes, picker dropdowns, skill table, click-box overlay) as one focused pass.
