# 4ViviTools - Claude Questions For Overnight Run

Prepared: 2026-07-13
Project root: D:\vs code clone 4rtool\4ViviTools

## Purpose

This is not a small bugfix question packet. This is for the overnight Codex run: what should be finished, what should be stabilized, what should be deferred, and what architecture should guide the whole tool.

The goal is to make 4ViviTools feel like one reliable Ragnarok Online companion app, not a pile of OCR/input/calculator/debug tabs.

## Current Big Picture

4ViviTools is an Avalonia 11 / .NET 8 Windows app for Ragnarok Online:

1. OCR and vision read the selected RO client.
2. Vision Assist GRF can add red boxes/name markers directly in-game.
3. Smart Bot attacks monsters using user-selected hotbar skills and normal clicks.
4. Autopot/Ygg/flee use HP/SP percent.
5. Input can route through VIIPER, FakerInput/vmouse, ViGEm, or normal Windows fallback.
6. Calculator/database/skills/items/gears are meant to be RO-focused and rAthena-compatible.
7. Discord RPC, overlay, trackers, and data tabs should consume the same live state.

The user wants this to be beginner-friendly:

```text
Download -> pick RO client -> mark HP%/SP% -> enable Vision Assist GRF if used -> pick hotbar skills/items -> Start Smart Bot.
```

No confusing driver internals, no duplicate key boxes, no noisy advanced OCR settings unless the user opens Advanced.

## Current State Before Overnight

Recent changes already started:

1. HP/SP now has trusted stat metadata:

```text
LiveStatQuality = Trusted / Held / Suspect
LiveStatSource = PercentText / BarFill / Memory / etc.
TryGetTrustedNumber(...)
```

2. HP/SP is moving from colored bar-fill to percent-text OCR around `100%`.

3. Smart Bot/Autopot/Ygg are being changed to require trusted HP/SP and confirmation before emergency actions.

4. Vision Assist GRF mode works and can provide red boxes/names in-game.

5. A hard-attached FocusGate has started but is not finalized:

```text
READ permission = attached/capturable client
ACT permission = selected client is foreground
```

6. Claude's last hard-attach reply said:

```text
Do not pause OCR just because 4ViviTools is focused.
Only pause actions/input when the selected game is not foreground.
```

That is the current intended design.

## Overnight Run Goal

By the end of the run, the app should be safer, clearer, and testable:

1. It cannot teleport/pot from one bad HP read.
2. It cannot send input to a non-focused/non-selected window.
3. OCR reads stay attached to the selected client window, not random monitor pixels.
4. Smart Bot has one clear action source from the hotbar cards.
5. Vision Assist GRF path is primary when enabled; YOLO/OCR monster detection is fallback.
6. UI is less noisy by default, with Advanced hiding engineering controls.
7. Build produces the `.exe` release.
8. DebugTrace explains what happened without needing guesswork.

## Main Overnight Questions For Claude

### 1. What is the highest-value sequence for the overnight run?

Given the project state, should Codex spend the night in this order?

```text
1. Finish HP/SP trusted percent path.
2. Finish FocusGate / hard-attached input safety.
3. Force client-window capture for bot actions.
4. Add Smart Bot state machine/logging.
5. Compact Smart Bot/OCR UI into Beginner/Advanced.
6. Hide 4RTools/ro-tools shells and keep address-reading as internal service.
7. Improve calculator/data wiring.
8. Add digit-template matcher for HP/SP.
9. Build/publish exe.
```

Or should the order be different?

### 2. What should be considered "done enough" for HP/SP tonight?

Options:

1. Ship current safer OCR percent parser with trusted/suspect gating.
2. Block until deterministic digit-template matcher exists.
3. Use address/memory reader as corroboration when available.
4. Use HP/SP percent text as primary and memory as fallback.

My instinct:

Ship the trust gate + safer percent OCR now, then add digit-template matcher next. The trust gate stops the dangerous behavior even if OCR is imperfect.

Do you agree?

### 3. What is the exact health-state model?

Should the app have one unified health state like:

```text
HpPct
SpPct
Quality: Trusted/Held/Suspect/Stale
Source: PercentText/Memory/Manual
AgeMs
RawText
Confidence
```

And should every consumer read from that one state?

Consumers:

```text
Smart Bot
Autopot
AutoYgg
Discord RPC
Stats tab
Top bar
Training recorder
Calculator simulation if live mode is enabled
```

### 4. Hard-attached OCR: what is the final rule?

Current preferred split:

```text
CanRead = selected client attached, capturable, not minimized, valid client rect.
CanAct = CanRead + selected process is foreground.
```

Questions:

1. Should OCR continue reading while 4ViviTools is focused for setup?
2. Should Smart Bot pause immediately when RO is not foreground?
3. Should Smart Bot hard-stop after 3-5 seconds unfocused?
4. Should Start be blocked if monitor capture is selected?
5. Should monitor capture be allowed only for manual/debug OCR?
6. Should overlay dim with "Paused - focus game" when CanAct is false?

### 5. Where should FocusGate live?

Current idea:

```text
FocusGate service in Core/Game
FocusGate.CanRead()
FocusGate.CanAct()
EngineHub owns one FocusGate
KeySender and MouseSender check CanAct before sending any input
OcrReaderViewModel checks CanRead before publishing live stats/scene
SmartBot checks CanAct before action loops and before Start
```

Question:

Is this sufficient, or should every `AutomationEngine` have a built-in `CanAct` helper so engines pause before doing expensive work too?

### 6. Input safety and input architecture

Current input paths:

```text
KeySender
MouseSender
VirtualHidInput
ViiperInput
ReWasdController / ViGEm path
```

Questions:

1. Should CanAct be enforced only in KeySender/MouseSender, or also directly in VirtualHidInput/ViiperInput/ReWasdController?
2. Should test buttons in the UI obey CanAct too?
3. Should global panic bypass CanAct?
4. Should the app never call SetForegroundWindow automatically except for a user-clicked "Focus client" button?
5. Should normal fallback be disabled by default once VIIPER/FakerInput is configured?

### 7. Smart Bot action model

The desired user model:

```text
User checks F2.
User marks it as Skill.
User picks Double Strafe.
Tool assigns the internal controller/input route.
Smart Bot uses: move mouse to monster -> press skill key -> click monster -> wait skill delay -> repeat until monster dead.
```

Questions:

1. Should all skill/item/buff/pot/ammo/teleport configuration live only in the hotbar cards?
2. Should old duplicate skill/key boxes be removed/hidden?
3. Should the bot use a state machine:

```text
Paused
WaitingForClientFocus
WaitingForTrustedVitals
Buffing
SelectingTarget
EngagingTarget
ConfirmingKill
Roaming
RecoveringStuck
Returning
Stopped
```

4. What state transitions should be logged?
5. Should one active target be held until death/disappear/timeout, instead of picking a new monster every loop?

### 8. Monster kill confirmation

Current issue:

The bot can click/move around but does not fully understand "keep attacking this monster until it dies."

Possible kill confirmation signals:

```text
Monster GRF/red box disappears.
Monster HP bar below it reaches zero.
Monster fade/death animation begins.
EXP increases.
Target lost after recent damage/casts.
Expected time-to-kill expires.
```

Questions:

1. What should be primary?
2. Should the bot require at least N casts/clicks before abandoning a target?
3. Should `FocusKillSeconds = -1` auto-estimate from mob HP, skill damage, SP, and observed damage?
4. Should the user be allowed to manually override focus kill seconds?
5. How should this behave with Vision Assist GRF where box/name is authoritative?

### 9. Vision Assist GRF vs YOLO/OCR

Current desired rule:

```text
Vision Assist GRF enabled:
  Use GRF marker boxes/names as primary.
  Do not draw YOLO monster boxes.
  Bot targets GRF entities.

Vision Assist GRF disabled:
  Use YOLO + ByteTrackLite + OCR/icon fallback.
```

Questions:

1. Should GRF mode completely bypass YOLO/ByteTrack for monsters?
2. Should there still be a tiny one-frame/two-frame confirmation guard for GRF markers?
3. How should shared sprites / wrong labels be handled?
4. Should the UI show "Vision Assist active - monster OCR disabled"?
5. Should map_mobs filtering still apply in GRF mode, or should GRF names be trusted directly?

### 10. OCR/vision performance

Target:

Fast enough for moving monsters and four visible clients eventually, but tonight prioritize one active client.

Questions:

1. Should active focused client run full-rate vision, while non-focused clients are passive/cold?
2. Should overlay redraw be tied to detector/GRF scan rate, not fake 120 FPS?
3. Should OCR text scans be decoupled from monster scans?
4. Should HP/SP percent text be read at high priority every tick?
5. Should map/name/weight/ammo be lower-rate?
6. Should hard examples be saved only on trusted failures, not every miss?

### 11. UI compaction

Main problem:

Smart Bot and OCR tabs are too noisy. Driver internals, OCR offsets, timing boxes, and duplicate controls scare new users.

Questions:

1. Should Beginner mode be default?
2. Should Advanced be one toggle across the app?
3. Should driver section collapse to:

```text
Input ready / Input needs setup / Manage input
```

4. Should `-1` timing values display as `Auto` pills instead of numeric `-1`?
5. Should only these be visible in beginner Smart Bot:

```text
Start/Stop
Client/focus status
HP flee %
Hotbar action cards
Map/client selection
Vision Assist status
```

6. Should OCR beginner mode show only:

```text
Attach client
Capture preview
Mark HP % Text
Mark SP % Text
Use markers
Vision Assist GRF toggle
Run OCR
Reset defaults
```

7. Should red borders be reserved for danger only, not every panel?

### 12. 4RTools and ro-tools

User direction:

Visible 4RTools and ro-tools shell tabs should be removed/hidden, but the address-reading idea should be kept internally.

Questions:

1. Should these tabs be hidden from main nav tonight?
2. Should address-reading become `RoClientDataService`?
3. Should that service be allowed to provide trusted HP/SP/map/buff/ammo if configured?
4. Should OCR and address data be merged with source/quality metadata?
5. Should the UI expose this only under Advanced -> Data/Integrations?

### 13. Calculator/data wiring

The calculator should stay one tab, but cleaner and more connected.

Questions:

1. Should the calculator use rAthena skill/item names only, never script ids like `ac_double`?
2. Should skill pickers show in-game names and internally map to ids?
3. Should selected map/monster from Smart Bot feed the calculator target?
4. Should selected hotbar skill feed damage/time-to-kill estimate?
5. Should Divine Pride/rAthena links be advanced details, not primary UI?

### 14. Logging and verification

DebugTrace should answer "why did the bot do this?"

Minimum important log lines:

```text
[FocusGate] read= act= reason= fgPid= selPid= hwnd= rectValid=
[Vitals] hp= sp= source= quality= conf= raw= decision=
[SmartBotState] old= new= reason=
[Target] selected= trackId= name= box= distance= source=grf/yolo
[Action] skill= key= inputRoute= target= result=
[Input] blocked/sent reason= route=
[OCR] engine= provider= fallbackReason=
```

Questions:

1. Is this enough?
2. Should logs be split into files by subsystem?
3. Should the UI expose a "Copy debug bundle" button?
4. Should every blocked action be logged once per second instead of every loop?

### 15. Release/build tonight

User asked: no zips until told, just build.

Questions:

1. Should overnight finish with:

```text
dotnet test
dotnet build 4rVivi.sln -c Release
dotnet publish src/4rVivi.App/4rVivi.App.csproj -c Release -r win-x64 --self-contained true
```

2. Should release output be:

```text
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.exe
```

3. Should we also ensure the normal Release bin has an `.exe`, not only `.dll`?

## My Proposed Overnight Plan

Unless Claude disagrees, I plan to do:

```text
Phase 1 - Safety:
  Finish HP/SP trusted percent path.
  Verify bar-fill HP path cannot publish health.
  Finish FocusGate with CanRead/CanAct.
  Gate KeySender/MouseSender and Smart Bot actions.

Phase 2 - Attachment:
  Keep OCR reads alive while configuring.
  Force Smart Bot to client-window capture.
  Block Start when monitor capture is selected.
  Add focus/client status logging.

Phase 3 - Bot loop:
  Add simple state logging.
  Hold one target until death/disappear/timeout.
  Improve focus kill / next monster timing with auto formulas.

Phase 4 - UI:
  Rename HP/SP roles.
  Collapse advanced input driver controls.
  Show Auto pills for -1 values.
  Hide 4RTools/ro-tools shells from top nav.

Phase 5 - Verification:
  Run tests.
  Build Release.
  Publish one exe.
  Write a final status MD with what changed and what still needs manual testing.
```

## Direct Questions To Answer First

Please answer these first if time is short:

1. Is the proposed overnight order right?
2. Should digit-template HP/SP matcher block tonight's release, or come after one real test run?
3. Should Smart Bot Start be blocked when monitor capture is selected?
4. Should visible 4RTools/ro-tools tabs be hidden tonight?
5. What is the minimum state machine we should add tonight?
6. What is the one thing that must not be deferred?

## Agent Allocation Questions

The user wants parallel agents for the overnight run. Please suggest how many agents should run, what each one should own, and what should stay with the main Codex agent to avoid merge conflicts.

### Proposed Agent Split

Option A - 4 agents:

```text
Main Codex:
  Integration owner.
  Final code edits that touch shared state, build, tests, publish.

Agent 1 - Safety / FocusGate:
  Audit FocusGate, KeySender, MouseSender, Smart Bot action gates.
  Verify no input can leave when selected client is not foreground.

Agent 2 - OCR / HP-SP:
  Audit HP/SP percent text path, trusted stat metadata, bar-fill dead paths, digit-template plan.
  Verify all health consumers use trusted values.

Agent 3 - Smart Bot / State Machine:
  Audit target lifecycle, attack order, skill timing, kill confirmation, stuck handling.
  Propose minimal state machine and required logs.

Agent 4 - UI / UX:
  Audit SmartBotView, OcrReaderView, nav, 4RTools/ro-tools visibility.
  Propose beginner/advanced compaction edits with least conflict.
```

Option B - 6 agents:

```text
Main Codex:
  Integration, final conflict resolution, build/test/publish.

Agent 1 - FocusGate/Input:
  Focus/read/act split, input chokepoints, panic behavior.

Agent 2 - OCR/Vision:
  HP/SP percent OCR, engine stability, Vision Assist GRF vs YOLO behavior.

Agent 3 - Smart Bot Combat:
  Target selection, target hold, skill loop, kill confirmation.

Agent 4 - Data/Calculator:
  rAthena naming, skill/item picker naming, calculator wiring, map mobs.

Agent 5 - UI/Navigation:
  Beginner/Advanced mode, screen compaction, nav cleanup, hidden legacy tabs.

Agent 6 - QA/Release:
  Tests to add, log checks, build/publish commands, regression checklist.
```

### Questions For Claude

1. How many agents should we run overnight: 3, 4, 5, or 6?
2. Which tasks are safe to parallelize without touching the same files?
3. Which tasks should only the main agent edit because they touch shared contracts?
4. Which files are likely conflict hotspots?

Likely hotspots:

```text
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
src\4rVivi.App\Views\OcrReaderView.axaml
src\4rVivi.App\ViewModels\SmartBotViewModel.cs
src\4rVivi.App\Views\SmartBotView.axaml
src\4rVivi.Core\Automation\SmartBotEngine.cs
src\4rVivi.Core\Game\LiveStats.cs
src\4rVivi.Core\Input\KeySender.cs
src\4rVivi.Core\Input\MouseSender.cs
```

5. Should agents mostly audit/propose patches, while main Codex applies them?
6. Should any agent be assigned only to tests and verification?
7. Should any agent be assigned only to documentation/user guide updates?
8. What is the best handoff format from each agent?

Suggested handoff format:

```text
Files inspected:
Findings:
Recommended edits:
Tests to run:
Risks:
Do not touch:
```

9. What order should the main agent merge agent results?
10. What tasks should we avoid parallelizing tonight because they require one coherent design?

## Codex Cleanup / Dirty Worktree Questions

The worktree is extremely dirty from many days of rapid iteration, training, generated files, experiments, docs, copied assets, and partially abandoned paths. Before the app can become reliable, Codex needs a cleanup plan that straightens the project without deleting useful work.

Important constraint:

Codex must not blindly delete user-created data, paid datasets, trained models, GRFs, or logs that may still be useful. Cleanup should be deliberate: classify first, remove or archive second.

### Current Dirty Worktree Categories

The dirty tree includes many categories:

```text
Modified source files in src/
Modified tests/
New source files from recent features
New docs/specs from Codex and Claude
Training scripts and generated training state
YOLO/PaddleOCR model outputs
GRF builder tools and generated GRF artifacts
Screenshots/previews/debug images
Driver research and driver binaries
reWASD / VIIPER / FakerInput support files
Large model/data files
Temporary zip/output files
Old roadmap/spec docs that may now contradict new direction
4RTools/ro-tools shell files that may be hidden or removed
```

### Cleanup Goals

The cleanup should produce:

```text
1. A clearer source tree.
2. No abandoned experimental code in active app paths.
3. No duplicate UI surfaces for the same feature.
4. No stale HP/SP bar-fill path feeding automation.
5. No visible 4RTools/ro-tools shell clutter if the new direction hides them.
6. Generated/training artifacts moved to ignored or documented locations.
7. A clean list of what must remain untracked because it is user data or generated output.
8. A buildable Release app.
9. Tests that cover the safety-critical paths.
```

### Questions For Claude / Codex About Cleanup

1. What is the safest cleanup workflow for this repo?

Proposed workflow:

```text
1. git status --short > cleanup_inventory.txt
2. classify every dirty/untracked path:
   - keep source
   - keep docs
   - keep data/model
   - archive
   - delete
   - add to .gitignore
   - unknown, ask user
3. build before deleting anything
4. delete only files clearly generated/abandoned
5. rebuild/test after cleanup
```

Is this the right workflow?

2. Should Codex create a `docs/CLEANUP_INVENTORY_2026-07-13.md` that lists every dirty/untracked category and decision?

3. Which abandoned features should be removed from active navigation now?

Candidates:

```text
Visible 4RTools shell
Visible ro-tools shell
Old HP/MaxHP and SP/MaxSP flat readers
Old HP Bar / SP Bar user-facing terminology
Duplicate Smart Bot skill/key boxes outside the hotbar cards
Duplicate autopot section if Smart Bot owns compact autopot config
Old monitor-capture bot action path
Old YOLO monster boxes when Vision Assist GRF is enabled
```

4. Which abandoned features should be kept but hidden under Advanced?

Candidates:

```text
Monitor capture
DXGI capture controls
OCR top/side offsets
OCR confidence sliders
FakerInput/VIIPER/ViGEm test buttons
reWASD optional import bridge
4RTools/ro-tools data/address readers
YOLO detector fallback settings
Hard examples/training export
```

5. Which files should never be deleted automatically?

Expected answer:

```text
tools\ocr-train\TrainingData
tools\ocr-train\Grf
tools\ocr-train\runs
tools\ocr-train\yolo_real
tools\ocr-train\Video
models shipped under src/RapidOcrNet/models
gamedata.json
map_mobs.json
Vision Assist GRF outputs
paid datasets
logs user may upload
```

Please add or correct this list.

6. Should generated image/debug files at repo root be moved to an archive folder?

Examples:

```text
IFRIT_fixed_preview.png
agav_frames.png
compose_*.png
marker_*.png
pipeline_check.png
variants_preview.png
test_ifrit_render.png
```

Suggested destination:

```text
artifacts\debug-images\
```

Or should they be deleted if reproducible?

7. Should old conflicting docs/specs be archived so they do not mislead future agents?

Problem:

Some docs still say HP/SP should be bar-fill, while newest direction says HP/SP percent text + trusted metadata.

Question:

Should Codex create:

```text
docs\archive\
```

and move obsolete specs there, or leave them in place but add a top warning?

8. How should `.gitignore` be updated?

Likely ignored paths:

```text
bin/
obj/
publish/
artifacts/
*.zip
*.log
tools/ocr-train/.env_ready
tools/ocr-train/.full_training_state.json
tools/ocr-train/overnight_yolo_keep_awake.lock
tools/ocr-train/video_frames/
tools/ocr-train/runs/
tools/ocr-train/yolo_real/
tools/ocr-train/hard_examples/
tools/ocr-train/ocr_export/
```

But some outputs may be intentionally kept. What should be ignored vs tracked?

9. Should model files be tracked or only copied into release artifacts?

The repo currently has model files under:

```text
src\RapidOcrNet\models\
```

Question:

Should this project track runtime model files in git, or keep them in release packaging only?

10. Should Codex split cleanup into two passes?

Proposed:

```text
Pass 1 tonight:
  Remove/hide abandoned UI paths.
  Ensure build/test/release.
  Add cleanup inventory.
  Add .gitignore for obvious generated artifacts.

Pass 2 later:
  Physically delete/archive large datasets and old artifacts after user confirms.
```

Is that safer?

11. How should Codex decide whether an untracked source file is real or abandoned?

Examples:

```text
VisionAssistMarkerDetector.cs
ModelManifestLogger.cs
SmartBotTrainingRecorder.cs
TemplateMatchService.cs
FourRToolsShellViewModel.cs
RoToolsShellViewModel.cs
MultiClientViewModel.cs
DamageCalcViewModel.cs
```

Some are active and should be kept; some may be hidden/renamed.

12. Should there be a `docs/CURRENT_ARCHITECTURE.md` that supersedes old specs?

Suggested contents:

```text
Current source of truth:
  HP/SP = percent text + trusted stat metadata
  GRF = primary monster source when enabled
  YOLO = fallback when no GRF
  CanRead/CanAct focus split
  Smart Bot hotbar cards are the only action config
  4RTools/ro-tools visible shells hidden; address work internal
```

13. What is the safest rule for deleting code?

Suggested:

```text
Only delete active code if:
  rg shows no references;
  build succeeds after deletion;
  feature is explicitly superseded;
  no user data is inside it.
Otherwise hide from nav/UI first.
```

14. Should cleanup be handled by a dedicated cleanup/QA agent?

If yes, what should that agent do?

Suggested cleanup agent task:

```text
Inventory dirty tree.
Classify files.
Find abandoned active code.
Find contradictory docs.
Suggest .gitignore updates.
Do not delete without main-agent review.
```

15. What should be left dirty at the end of tonight if anything?

Potential acceptable dirty items:

```text
User datasets
Generated training outputs
Logs
Large model artifacts
GRF outputs
Docs/specs from this session
```

Potential unacceptable dirty items:

```text
Build errors
Half-wired UI controls
Duplicate visible tabs
Old HP/SP path feeding automation
Input path bypassing FocusGate
Release output missing .exe
```
