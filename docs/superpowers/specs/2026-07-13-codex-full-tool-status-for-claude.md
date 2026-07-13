# 4ViviTools - Full Tool Status and Second-Opinion Request

Prepared: 2026-07-13  
Prepared by: Codex  
Project root: D:\vs code clone 4rtool\4ViviTools

## Purpose

This document is a complete second-opinion packet for Claude. It is not limited to the Smart Bot attack loop. It covers the whole 4ViviTools project state: OCR, GRF Vision Assist, monster boxes, timing, input routing, Smart Bot, AutoPot, calculator/data wiring, Discord/4RTools/ro-tools surfaces, packaging, training assets, and the current unresolved issues.

The user wants the application to feel simple for a new Ragnarok Online player:

- Pick the RO client.
- Mark HP/SP bars and useful OCR regions.
- Optionally use the Vision Assist GRF.
- Select hotbar keys and in-game skill/item names from pickers.
- Let the tool calculate most timing and thresholds automatically.
- Start Smart Bot and have it reliably attack monsters, cast selected skills, use pots/ammo/buffs, detect death, and move only when appropriate.

## Current Build and Verification

Fresh verification was run after the latest changes.

Commands:

```powershell
dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release --no-restore --nologo
dotnet test "D:\vs code clone 4rtool\4ViviTools\tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj" -c Release --no-build --nologo
dotnet publish "D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\4rVivi.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishProfile=ReleaseExe -o "D:\vs code clone 4rtool\4ViviTools\publish\win-x64" --no-restore --nologo
```

Results:

- Build: succeeded, 0 warnings, 0 errors.
- Core tests: 56 passed, 0 failed.
- Published app executable:

```text
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.exe
Size: 153,049,074 bytes
Timestamp: 2026-07-13 20:11 local time
```

Important packaging note: `dotnet build` still emits `src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0\4rVivi.dll`. The actual testable release app is now the published `4rVivi.exe` under `publish\win-x64`.

## Latest Patch Summary

### 1. HP/SP moved from flat text numbers to bar percent markers

Problem: OCR of flat `HP/MaxHP` and `SP/MaxSP` text became unreliable. The user requested removing dependence on flat numbers and using the top-left Basic Info bars instead.

Changed behavior:

- OCR Reader now exposes user-facing roles:
  - `HP Bar`
  - `SP Bar`
- These normalize internally to:
  - `Roles.HpPercent`
  - `Roles.SpPercent`
- Saved old markers are normalized on load.
- Bar roles force `IsBar=true`.
- Marker readiness now requires `HP Bar` and `SP Bar`, not flat HP/MaxHP/SP/MaxSP.
- Top bar, Stats, 4RTools shell, ro-tools shell, Smart Bot training recorder, and HealthReader now prefer `HpPercent`/`SpPercent`.
- Flat number fields remain only as fallback for older profiles and compatibility.

Changed files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Game\Roles.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Game\HealthReader.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\OcrService.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\RolePalette.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\MainWindowViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\StatsViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\FourRToolsShellViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\RoToolsShellViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\SmartBotTrainingRecorder.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\StatsView.axaml
```

Current implementation detail:

- `OcrService.ReadBarPercent` and `ReadBarPercentFrom` now accept an optional role.
- For HP/SP, `BarFill` first uses color-aware fill detection:
  - HP: red/pink fill.
  - SP: blue fill.
  - It ignores white text and dark borders better than the old brightness-only method.
- Brightness fallback remains for EXP/cast bars.

Open question for Claude:

- Is the color-threshold bar reader robust enough for RO skins, dark themes, and different Basic Info UI variants?
- Should bar reads be smoothed with a short temporal median or should raw bar percent be used immediately for flee/autopot?

### 2. Release packaging restored to `.exe`

Problem: the user expected a release app but saw only `.dll` output from `dotnet build`.

Changed behavior:

- `build.ps1` now restores, builds, then publishes a self-contained win-x64 app using a publish profile.
- Added publish profile:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Properties\PublishProfiles\ReleaseExe.pubxml
```

Changed file:

```text
D:\vs code clone 4rtool\4ViviTools\build.ps1
```

Current output:

```text
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.exe
```

Open question for Claude:

- Should we keep this as folder publish with `OcrServer` / `OcrServerCuda` sidecar folders for debugging, or move later to true one-exe packaging after behavior stabilizes?

### 3. Smart Bot loop diagnostics and safety fixes

Claude's prior review suggested the "only teleporting every ~8 seconds" symptom was likely control-flow, not input. The code now logs a structured per-cycle diagnostic line every ~500 ms.

Changed file:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\SmartBotEngine.cs
```

New diagnostic fields include:

```text
en
attached
statsFresh
hwnd
cw/ch
sceneFresh
sceneClientCoords
sceneAgeMs
statsAgeMs
entities
visible
confirmed
attackable
hp
hpFresh
flee
wt
skills
buffs
branch
sample
```

Branches currently logged:

```text
return
flee
ammo
client-size
target
no-target
scene-coords
scene-stale
roam
idle
```

Safety changes:

- `ClientSize` is resolved through `ResolveClientSize()`.
- If size is invalid, it attempts `Session.Reattach()` and logs the result.
- If client size remains invalid, bot no longer falls through to roam/unstuck blindly for that tick.
- Flee now requires fresh sane HP:

```text
FleeAtHpPercent > 0 && hpFresh && hp > 0 && hp <= FleeAtHpPercent
```

- Stale or unknown HP does not trigger flee.
- Skill rotation excludes enabled skill rows with blank skill names.
- Smart Bot profile save also prevents blank skill rows from being saved as real skills.
- UI sync warns if checked skill keys have no selected skill.

Changed file:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\SmartBotViewModel.cs
```

Open questions for Claude:

- Are these loop diagnostics enough to localize the current no-attack/no-skill behavior in one run?
- Should `cw/ch=0` temporarily disable unstuck teleport until the window reattach succeeds?
- Should `sceneFresh=false` in GRF mode use a looser freshness window because GRF boxes are engine-pinned?

## Major Current Problems Across the Whole Tool

### A. OCR / Vision / Monster Boxes

Observed user problems:

- Too many boxes appear on one real monster.
- Phantom boxes appear where no monster exists.
- Boxes flicker and do not remain pinned while the character moves.
- Boxes lag behind moving screen content.
- Confidence slider appeared ineffective in previous tests.
- Monster names drift or become wrong, especially with non-GRF mode and icon/name voting.
- When `Use Vision Assist GRF` is enabled, regular monster detector boxes should be off, but the Smart Bot must still target GRF marker entities.

Current architecture:

- Non-GRF path:
  - Capture frame.
  - OCR text + YOLO entity detection.
  - Icon/name recognition.
  - ByteTrackLite / LiveScene stabilization.
  - Smart Bot consumes `LiveScene`.
- GRF Vision Assist path:
  - Game itself draws red boxes and names baked into monster sprites.
  - Tool scans for the red marker boxes and color/name marker cues.
  - In ideal design, this should bypass YOLO/name voting and emit authoritative `SceneItem`s.

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\OcrService.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\VisionAssistMarkerDetector.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Game\LiveScene.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Game\ByteTrackLite.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Game\OcrNameCorrector.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Ocr\VisionConfig.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.OcrServer\EntityDetector.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.OcrServer\IconRecognizer.cs
```

Claude review request:

- In GRF mode, should we bypass ByteTrackLite entirely and publish marker detections directly with a small `seenTwice` guard?
- If keeping tracker in GRF mode, should matching include `mobId`/marker code before IoU so different mobs never merge?
- Should GRF mode disable name voting completely and trust the baked marker identity every scan?
- What is the best way to keep the visual overlay stable while preventing stale LostGrace boxes from being clickable?

### B. Vision Assist GRF

User-confirmed status:

- The GRF works visually in-game.
- It draws red boxes and readable monster names directly on sprites.
- This helps because the box moves with the sprite at the game engine frame rate.

Remaining problems:

- The runtime side still needs to be treated as authoritative marker mode, not as "YOLO plus extra labels."
- When the user checks `Use Vision Assist GRF`, the normal `Monsters` detector checkbox should automatically uncheck or stop generating detector boxes.
- The user does not want a GRF path input in the app. They know how to install the GRF; the runtime should use baked metadata in the app/models/json and only scan screen markers.
- The user asked for the GRF builder to remove death animation frames, keep one frame only plus the red box, and improve the text label font with bold/larger black outline. Some builder work exists but needs further validation.

Relevant paths:

```text
D:\vs code clone 4rtool\4ViviTools\tools\vision-grf
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\Grf
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\VisionAssistMarkerDetector.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Grf\GrfArchive.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Grf\SprWriter.cs
```

Claude review request:

- Should the GRF runtime identify monsters by color code only, OCR text only, or color code plus optional text verification?
- Should the GRF builder produce a manifest embedded into the app at build time, or should the app only need generic color detection?
- What is the safest way to handle shared sprites where multiple monster IDs use the same `.spr`/`.act`?

### C. Smart Bot Attack Loop

Observed user problems:

- Tool clicks/moves around but does not consistently press skills.
- It sometimes roams instead of engaging visible monsters.
- It sometimes teleports every `StuckSeconds`, implying the combat branch did not run.
- It must attack immediately when a monster is found.
- Skill sequence must be:

```text
press skill hotkey -> short arm delay -> left click monster -> wait skill/aftercast delay -> re-check target/death -> repeat
```

- If SP is too low for skill, use normal attack.
- If target cannot be killed or the bot is stuck, use teleport key.
- If no monster list is selected, attack all monsters.
- If GRF mode is on, use GRF marker boxes as target source.

Current relevant code:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\SmartBotEngine.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\SmartBotViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\SmartBotView.axaml
```

Current design:

- Smart Bot consumes `LiveScene`.
- `SelectTarget` prefers named monsters over generic labels, nearest to center.
- It keeps an engaged track ID.
- It learns HP drop per cast when target HP bars exist.
- It estimates kill time from HP ratio, mob max HP, learned damage/cast, and configured/auto skill delay.
- Death detection currently uses HP-empty or engaged target disappearance after damage/casts/expected-kill timeout.

Remaining concerns:

- We still need a clean state machine:

```text
Idle
Hunting
Engaging
WaitingForDamage
RetargetDelay
Roaming
Emergency
```

- The loop currently has a lot of interleaved gates, which makes timing bugs hard to reason about.
- `WaitingForDamage` should own aftercast/death logic and prevent roam/emergency from interleaving mid-cast.
- Smart Bot must never attack LostGrace/coasting boxes; those are for drawing only.

Claude review request:

- Should we implement the state machine immediately after the new diagnostic run?
- What fields should define `Attackable` in each mode?
  - Non-GRF: Visible AND Confirmed AND min hits AND score threshold.
  - GRF: marker visible this scan or seen twice, no name voting, maybe no tracker.
- How should target-death be detected when monster HP bar is missing?

### D. Timing and Auto Formulas

User request:

- Hide complicated timing/threshold values from beginners.
- Use `-1` as Auto everywhere.
- Let user override only if needed.
- Formulas should consider:
  - client size
  - capture size
  - screen/monitor scale
  - mouse travel distance
  - target size/confidence
  - scene age
  - stats age
  - input backend
  - skill arm/cast/aftercast
  - learned Smart Bot Training data

Current code includes formula helpers:

```text
ResolveIdleDelayMs
ResolveSkillDelayMs
ResolveSkillArmDelayMs
ResolveNormalAttackDelayMs
ResolveActionDelayMs
AutoUtilityDelayMs
AutoMoveWaitMs
ResolveNextMonsterDelayMs
AutoMoveStableMs
TimingMetrics
```

File:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\SmartBotEngine.cs
```

Remaining concerns:

- These formulas compile, but they need real gameplay validation.
- `RotationMs`, per-skill delay, walk delay, next monster delay, teleport delay, and focus kill time must all default to `-1` in the UI/profile.
- User needs visible explanation:

```text
-1 = Auto
0 = Off where applicable
positive number = manual override
```

Claude review request:

- Are the current formula inputs sufficient, or should skill delay prefer rAthena `AfterCastActDelay` / `SkillDelay` data first?
- Should formula outputs be logged each time a target action is taken so we can tune with logs?

### E. Smart Bot Training

User request:

- Add a Smart Bot Training button that records what the player does:
  - keys pressed
  - mouse clicks
  - OCR scene
  - HP/SP
  - map
  - monster target
  - time to kill
  - teleport behavior
  - potion timing
- Use this to tune formulas in real time.

Current relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\SmartBotTrainingRecorder.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\SmartBotTrainingTuning.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\SmartBotViewModel.cs
```

Remaining concerns:

- Training data needs to be carefully separated from live automation behavior.
- Training should not introduce noisy learned timings from bad OCR/box state.
- Need a "reset learned timing" option if the profile becomes bad.

Claude review request:

- What minimal fields should the training recorder preserve to be useful without becoming a huge fragile telemetry system?
- Should training only learn when GRF marker mode is active because target identity is cleaner?

### F. Input / VIIPER / FakerInput / ViGEm / Mouse

User history:

- VIIPER installed and eventually keyboard input worked.
- Mouse input previously failed in several runs.
- FakerInput appeared unused at one point.
- reWASD is optional, not mandatory.
- ViGEm is acceptable/green, reWASD optional for profile import/direct profiles.

Current intent:

```text
VIIPER first
FakerInput / virtual mouse path next if available
ViGEm next
normal Windows input fallback last
```

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\InputMethod.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\KeySender.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\MouseSender.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\ViiperInput.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\VirtualHidInput.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\InputRuntimeStatus.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\ReWasdController.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Input\ReWasdMouseMap.cs
```

Current Smart Bot change:

- `TapAction` now sends keyboard-first for non-reWASD methods when a VK exists.
- Controller button mapping is fallback, not the primary skill path.
- This is intended because RO hotbar skills are normal keyboard hotkeys; controller remapping should not be required if VIIPER/keyboard input works.

Remaining concerns:

- Need log proof of keyboard engine and mouse engine per action.
- Need exact mouse movement/click result logging from `MouseSender`, especially if VIIPER returns success but cursor does not move.
- Need avoid scattering input selection across tabs.

Claude review request:

- Should the input stack be one explicit service with a single ordered route and one diagnostic result object per input action?
- Should Smart Bot consume only high-level `IInputRouter.ClickClient(hwnd,x,y)` / `TapKey(hwnd,key)` methods instead of calling KeySender and MouseSender separately?

### G. AutoPot / HP-SP Bars

User wants:

- AutoPot integrated into the Smart Bot key grid.
- HP pot, SP pot, Ygg, ammo, ammo bag all configured per key card.
- Potion thresholds are percentages.
- The tool should know potion/item names from pickers, not raw script IDs.

Current status:

- HP/SP percent now prefer bar roles.
- Smart Bot key cards support action kinds like HP pot/SP pot/Ygg/ammo/ammo bag.
- There are still old AutoPot views/engines that may duplicate the Smart Bot experience.

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\AutopotEngine.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\AutopotViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\PotRowViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\AutopotView.axaml
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\SmartBotViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\SmartBotView.axaml
```

Remaining concern:

- There may be duplicate pot configuration surfaces between AutoPot tab and Smart Bot cards.
- Need define whether AutoPot is global/standalone, or fully merged into Smart Bot.

Claude review request:

- Should AutoPot remain a standalone tab for users who only want pots without botting, while Smart Bot has a compact embedded pot section?
- Or should AutoPot be hidden under Advanced once Smart Bot cards cover it?

### H. Calculator / rAthena / Divine Pride / Database

User wants:

- Calculator stays all in one tab.
- It must be more complete and wired.
- It should use friendly in-game names, not rAthena script IDs like `ac_double`.
- It should connect to:
  - rAthena data
  - Divine Pride links
  - monster attack simulation
  - gears
  - skills
  - buffs
  - usable items
  - map/mob focus data
  - Smart Bot kill time estimation

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\CalculatorViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\CalculatorView.axaml
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Calc
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Data\GameDatabase.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Data\Models.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Data\gamedata.json
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Data\map_mobs.json
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\DivinePrideLinks.cs
D:\vs code clone 4rtool\4ViviTools\tools\build_gamedata.py
D:\vs code clone 4rtool\4ViviTools\tools\build_map_mobs.py
```

Remaining concerns:

- Need audit every picker to confirm it uses display names.
- Need confirm display-name-to-internal-ID conversion is reliable.
- Need ensure Smart Bot kill-time estimation and Calculator use the same database source.
- Need decide if Divine Pride should be link-only or cached/imported data.

Claude review request:

- What is the best source of truth hierarchy for names and stats?

Suggested hierarchy:

```text
gamedata.json built from rAthena -> Divine Pride link metadata -> user overrides
```

### I. 4RTools and ro-tools Tabs

User explicitly asked whether to remove or keep these.

Current status:

- There are shell/views inspired by 4RTools and ro-tools layouts.
- They may help familiar users.
- They also duplicate functionality and may confuse new users.

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\FourRToolsShellViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\RoToolsShellViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\FourRToolsShellView.axaml
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\RoToolsShellView.axaml
```

Claude review request:

- Should these tabs remain as "Legacy / Familiar Layouts" under Tools or Advanced?
- Should they be removed from primary navigation until the core flow is stable?
- Should they be merged into one "Classic Tools" page?

My current recommendation:

- Keep them for now, but remove them from the main beginner path.
- Primary nav should be:

```text
Home
Bot
OCR Reader
Overlay
Calculator
Data
Tools
System
```

- Put 4RTools/ro-tools under Tools as optional classic layouts.

### J. Multi-client

User wants:

- Attach to multiple RO clients.
- Capture each client by window, not whole monitor.
- Work even when clients are not focused.
- Four clients split on screen, each with OCR and possible bot role.
- Assign client role:
  - main farmer
  - buffer
  - support
  - command-only

Current concern:

- True multi-client at high FPS can overwhelm GPU/CPU if each client gets a full independent detector session.
- Prior recommendation was shared detector with round-robin client frame queue:
  - hot client high rate
  - support clients lower rate
  - one shared ONNX session

Relevant files:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\MultiClientViewModel.cs
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\MultiClientView.axaml
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Capture
```

Claude review request:

- Is multi-client better implemented as separate `ClientSession` objects sharing a capture/detector scheduler?
- Should only the active Smart Bot client run combat vision at full rate, with support clients polling buffs/HP/SP only?

### K. Discord RPC

User wants Discord RPC wired to OCR stats:

- HP/SP percent
- map
- position
- state: idle/attack/moving
- character name/class
- map art

Relevant file:

```text
D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services\DiscordPresenceBootstrap.cs
```

Remaining concerns:

- Now that HP/SP flat numbers are fallback only, Discord should use bar percentages.
- If OCR is stale, Discord should not show misleading stats.
- Need decide whether Smart Bot state should feed Discord activity.

### L. UI / Beginner Experience

User wants:

- Black/red theme.
- 4RTools/ro-tools-like clean icon-heavy UI.
- No unreadable white tabs.
- No duplicate boxes.
- No free-text key boxes; use key recorders.
- Skills/items/gears/buffs should be searchable pickers with in-game display names.
- Number boxes should fit 4 digits and be expandable.
- Advanced OCR settings hidden unless needed.
- `-1 = Auto` very visible.
- Avoid page jumping to top when focusing boxes.

Current concern:

- Many features were added quickly.
- Some tabs duplicate others.
- Smart Bot is the center of the product, but still competes with legacy Bot/AutoPot/Buffs/Skills/Macros surfaces.

Claude review request:

- What should be the final tab model?
- Which controls should be removed from beginner mode?
- Should there be a global "Beginner / Advanced" toggle?

### M. Training Data and Model Wiring

Existing training assets and scripts:

```text
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\TrainingData
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\Video
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\Grf
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\yolo_real
D:\vs code clone 4rtool\4ViviTools\src\RapidOcrNet\models
D:\vs code clone 4rtool\4ViviTools\src\RapidOcrNet\models\icons
D:\vs code clone 4rtool\4ViviTools\src\RapidOcrNet\models\yolo
D:\vs code clone 4rtool\4ViviTools\src\RapidOcrNet\models\v5
```

Important scripts:

```text
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\RUN_EVERYTHING_2060S.bat
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\RUN_OVERNIGHT_YOLO_2060S.bat
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\RUN_RESUME_YOLO_2060S.bat
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\train_everything_2060s.py
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\train_yolo.py
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\build_icon_model.py
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\build_map_mobs.py
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\yolo_qc_supervision.py
```

Previously installed/referenced tooling:

- .NET 8 SDK/runtime.
- Avalonia 11.
- ONNX Runtime.
- CUDA Toolkit 12.9.2 installed by user.
- cuDNN 9.24 installed by user.
- PaddleOCR / PP-OCRv5 training tools in Python environment.
- Ultralytics YOLO.
- Roboflow/supervision for dataset QC.
- OpenCV.
- Pillow.
- NumPy.
- PyTorch/CUDA for YOLO training.
- VIIPER installed by user.
- ViGEm driver optional/used.
- reWASD optional, not mandatory.

Remaining concerns:

- Need verify trained model locations are exactly what runtime loads.
- Need verify `entity.onnx`, `entity_meta.json`, icon bank, and PP-OCRv5 ONNX are all copied to publish output.
- Need verify map-mob focus data is wired to both detector/name-corrector and Smart Bot targeting.
- Need clarify whether the latest "huge accurate train" improved real gameplay precision or overfit/introduced false positives.

Claude review request:

- What is the best runtime validation checklist to prove model wiring?
- Should we add a startup model manifest log that prints every model path, hash, class count, and load status?

## Logs and Debugging Files

Runtime logs:

```text
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\VIIPER.log
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\crash.log
```

New expected Smart Bot diagnostic line pattern:

```text
[SmartBot] loop en=True attached=True statsFresh=True hwnd=0x... cw=... ch=... sceneFresh=True sceneClientCoords=True sceneAgeMs=... statsAgeMs=... entities=... visible=... confirmed=... attackable=... hp=... hpFresh=True flee=... wt=... skills=... buffs=... branch=target sample=[...].
```

What a good run should show:

- `cw/ch` valid and non-zero.
- `sceneFresh=True` often enough while OCR/GRF is running.
- `sceneClientCoords=True`.
- `attackable > 0` when monsters are visibly boxed.
- `branch=target` when monsters are visible and attackable.
- `TapAction keyboard-first` for skill hotkeys under VIIPER/FakerInput/VirtualHID/SendInput methods.
- `ClickAt requested` followed by the selected mouse backend log.

What a bad run may reveal:

- `cw=0 ch=0`: stale HWND/client size.
- `sceneFresh=False`: entity scan cadence too slow or publish/consume mismatch.
- `attackable=0` despite overlay boxes: LiveScene filtering/predicate mismatch.
- `branch=roam` while attackable exists: target selection predicate bug.
- `branch=client-size` repeatedly: capture/input HWND mismatch.
- skills count `0` even when key card is configured: SkillButton sync/config issue.

## Proposed Next Test Protocol

Use the fresh executable:

```text
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.exe
```

Test setup:

1. Close old elevated instances first.
2. Launch the fresh `.exe` elevated.
3. Select RO client.
4. OCR Reader:
   - Add `HP Bar` marker around top-left Basic Info HP bar.
   - Add `SP Bar` marker around top-left Basic Info SP bar.
   - Keep `Use Vision Assist GRF` enabled if the GRF is installed.
   - In GRF mode, normal monster detector should be disabled/not drawn.
5. Smart Bot:
   - Input method: VIIPER if installed.
   - Pick one real skill key like F2 and select a real skill display name, for example `Double Strafe`.
   - Ensure blank skill rows are not selected.
   - Leave timing fields as `-1` Auto.
6. Record:
   - 30 seconds standing near monsters.
   - 30 seconds walking near monsters.
   - 30 seconds Smart Bot running.
7. Upload:

```text
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\VIIPER.log
```

## Specific Questions for Claude

1. Is the new loop diagnostic enough to prove whether the attack loop failure is `cw/ch=0`, `sceneFresh=false`, `attackable=0`, or target predicate mismatch?
2. Should invalid client size prevent unstuck teleport entirely for that tick?
3. In GRF mode, should we bypass YOLO/ByteTrack/name voting completely and publish marker entities directly?
4. If retaining a tracker in GRF mode, should `mobId` or marker color-code be mandatory before IoU association?
5. Should HP/SP bar values use temporal median smoothing before AutoPot/flee?
6. Should Smart Bot be refactored now into the explicit state machine, or should we wait for one diagnostic run with the new log?
7. Should AutoPot remain a standalone tab or be folded fully into Smart Bot key cards?
8. Should 4RTools and ro-tools tabs be kept as optional classic layouts, merged, or removed from primary navigation?
9. What is the best source-of-truth hierarchy for Calculator/Smart Bot data: rAthena gamedata, Divine Pride, runtime OCR, user overrides?
10. Should we add startup model manifest logging with file paths/hashes/class counts to catch wrong model/data wiring?
11. For multi-client, should all clients share a single detector scheduler and only the active farming client run high-rate combat vision?
12. For one-exe packaging, should we postpone until behavior is stable, keeping sidecar worker/model folders for debugging?

## Current Codex Recommendation

Short term:

1. Test the fresh `.exe` and collect one new `DebugTrace.log`.
2. Use the new `SmartBot loop` diagnostics to identify the exact control-flow blocker.
3. Fix only the blocker revealed by the log.
4. In parallel, simplify GRF mode so it emits authoritative marker entities and does not rely on YOLO/name voting.
5. Keep `.exe` folder-publish output for testing until behavior is stable.

Medium term:

1. Refactor Smart Bot into explicit states.
2. Make GRF marker mode the primary beginner recommendation.
3. Hide advanced OCR/tracking sliders by default.
4. Use HP/SP bar markers as the main health resource source.
5. Move duplicate 4RTools/ro-tools/legacy bot controls into Tools/Advanced.
6. Add model manifest logging.
7. Add clearer beginner guide around:
   - install driver
   - select client
   - mark HP/SP bars
   - enable Vision Assist GRF
   - select skill keys
   - start Smart Bot

