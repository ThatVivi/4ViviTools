# 4ViviTools Smart Bot + OCR/GRF Vision Handoff for Second Opinion

Date: 2026-07-13  
Repo: `D:\vs code clone 4rtool\4ViviTools`  
Primary app: Avalonia 11 / .NET 8, `src\4rVivi.App`  
Core automation: `src\4rVivi.Core`  
Latest runnable folder publish: `D:\vs code clone 4rtool\4ViviTools\publish\win-x64`

This document is a detailed handoff for a second opinion. It describes the current architecture, recent changes, evidence from logs, remaining problems, hypotheses, and concrete commands/scripts to inspect the issue.

## Executive Summary

The Vision Assist GRF path is now active and OCR/vision is publishing monsters as `attackable`, but Smart Bot behavior still needs refinement. In the most recent log, the bot did not enter the combat target-selection path even though `LiveScene` had visible attackable entities. Instead, it repeatedly pressed key `1`, which is the active profile's teleport key. A code refinement was applied after reading the log so that:

- `ReturnAtWeightPercent = 0` means disabled.
- `FleeAtHpPercent = 0` means disabled.
- Low-HP flee now logs clearly and directly presses the teleport key before continuing.

This refinement built successfully, but it has not yet been field-tested in the game after the patch.

There are still deeper issues worth auditing:

- GRF raw detections are stable, but tracker labels can drift or carry old names into new tracks.
- Smart Bot target selection can be starved by pre-combat gates.
- Active profile configuration has likely conflicting entries.
- The skill rotation currently includes `F2` and `8`; `8` is marked as a skill with no skill name.
- `F2` skill delay is set to `10ms`, likely too aggressive for Ragnarok Online skill -> click -> after-cast timing.
- The current publish is still a folder publish; `4rVivi.exe` is an apphost launcher, not the one-file release described in `docs\ONE_EXE_PACKAGING.md`.

## Important Paths

### Source files to inspect

- Smart Bot engine:
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Automation\SmartBotEngine.cs`
- Smart Bot settings model:
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Settings\AppSettings.cs`
- Smart Bot UI/view model:
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\ViewModels\SmartBotViewModel.cs`
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Views\SmartBotView.axaml`
- OCR/vision services to inspect:
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\Services`
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.Core\Vision`
  `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.OcrServer`
- Packaging plan:
  `D:\vs code clone 4rtool\4ViviTools\docs\ONE_EXE_PACKAGING.md`

### Runtime logs and config

- Main debug log:
  `C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log`
- VIIPER log:
  `C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\VIIPER.log`
- Active settings:
  `C:\Users\Vivi\AppData\Roaming\4rVivi\settings.json`
- Smart Bot training logs:
  `C:\Users\Vivi\AppData\Roaming\4rVivi\Training`

### Latest build output

Folder publish output:

```text
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.exe
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.dll
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\4rVivi.Core.dll
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\OcrServer\
D:\vs code clone 4rtool\4ViviTools\publish\win-x64\OcrServerCuda\
```

Important note: in this publish mode, `4rVivi.exe` is only the launcher/apphost and may keep an older timestamp. The updated automation logic is in `4rVivi.Core.dll`.

Latest observed publish timestamps:

```text
4rVivi.Core.dll  7/13/2026 7:48:28 PM
4rVivi.exe       7/13/2026 7:32:11 PM
```

## Current Runtime Architecture

### Detection path

The current preferred vision path is Vision Assist GRF mode:

```text
Game renders baked red boxes and names from GRF
-> capture frame
-> OCR/vision scans GRF marker boxes
-> builds raw SceneItem detections with mobId/name/box
-> tracker stabilizes entities
-> LiveScene publishes confirmed/visible/attackable entities
-> SmartBotEngine selects target
-> SmartBot sends skill key, then mouse click
```

The goal of GRF mode is to bypass YOLO/OCR name guessing for monsters. The game itself draws the red box and name in real time, so tracking should be simpler than detector-only mode.

### Smart Bot action path

Main loop in `SmartBotEngine.LoopAsync`:

1. Auto reconnect gate.
2. Map gate.
3. Refresh buffs.
4. HP/weight emergency gates.
5. Ammo gate.
6. Vision target selection.
7. Skill/normal attack.
8. Roam/walk if no vision action.
9. Progress/unstuck checks.

Relevant locations as of this handoff:

```text
SmartBotEngine.cs:149  MaybeRefreshBuffs
SmartBotEngine.cs:154  ReturnAtWeightPercent gate, now disabled when 0
SmartBotEngine.cs:162  FleeAtHpPercent gate, now disabled when 0
SmartBotEngine.cs:193  SelectTarget call
SmartBotEngine.cs:240  skill key press before click
SmartBotEngine.cs:382  Vision decision diagnostic log
SmartBotEngine.cs:428  TrackTargetGone
SmartBotEngine.cs:684  TapAction
SmartBotEngine.cs:872  ResolveNextMonsterDelayMs
```

## Recent Changes Made

### Time-to-kill and death tracking

Added/extended Smart Bot runtime fields and logic:

- `FocusKillSeconds` setting, default `-1` for auto.
- Tracks engaged target ID/name.
- Learns HP ratio drop per cast when monster HP bar is available.
- Looks up monster max HP from game database when possible.
- Estimates expected kill time and casts left.
- Counts kill on:
  - monster HP empty,
  - EXP gain,
  - target vanished after damage/casts,
  - expected kill deadline reached.

UI field:

```text
SmartBotView.axaml:251
Focus kill sec (-1 auto)
```

Settings:

```text
AppSettings.cs:69
public int FocusKillSeconds { get; set; } = -1;
```

### Next-monster delay

Added:

```text
AppSettings.cs:70
public int NextMonsterDelayMs { get; set; } = -1;
```

UI:

```text
SmartBotView.axaml:252
Next monster ms (-1 auto)
```

Engine:

```text
SmartBotEngine.cs:872
ResolveNextMonsterDelayMs(SceneItem? lastTarget, int clientW, int clientH)
```

Behavior:

- If user enters `0..5000`, use that exact ms delay.
- If `-1`, calculate from scene age, stat age, input backend penalty, hardware click mode, and target distance.
- Used after confirmed HP-empty kill and after a vanished-after-damage kill before selecting the next target.

### Emergency gate refinement after latest log

Problem seen in latest log: OCR sees attackable entities, but Smart Bot repeatedly sends key `1`, not target clicks. Active profile has `TeleportKey = 1`.

Applied code refinement:

```csharp
if (ReturnAtWeightPercent > 0 && wt >= 0 && wt >= ReturnAtWeightPercent)
```

and:

```csharp
if (FleeAtHpPercent > 0 && hp >= 0 && hp <= FleeAtHpPercent)
{
    Log(BotLogKind.Movement, $"HP {hp:0}% <= flee {FleeAtHpPercent}% - teleporting with {TeleportKey} before next target.");
    TapAction(TeleportKey, 20);
    ResetEngagedTarget();
    await Timing.DelayAsync(ResolveActionDelayMs(TeleportKey, AutoUtilityDelayMs(TeleportKey, 400), 120, 1800), ct);
    continue;
}
```

This prevents `0` thresholds from behaving like always-on emergency triggers and makes flee behavior explicit in logs.

## Active Profile Snapshot

From:

```text
C:\Users\Vivi\AppData\Roaming\4rVivi\settings.json
```

Important active profile values:

```json
{
  "Enabled": false,
  "TeleportKey": "1",
  "ReturnKey": "2",
  "FleeAtHpPercent": 25,
  "StuckSeconds": 8,
  "FocusKillSeconds": 3,
  "NextMonsterDelayMs": 3000,
  "ReturnAtWeightPercent": 0,
  "RotationMs": 10,
  "SkillSpamEnabled": true,
  "ClickToMove": true,
  "ClickAttack": true,
  "MoveWaitMs": 1000,
  "MoveStableMs": 850,
  "UseVision": true,
  "HardwareClick": true,
  "UseControllerButtons": true,
  "InputMethod": 5,
  "VirtualClickFallback": false,
  "AutopotEnabled": true,
  "AmmoKey": "9",
  "AmmoBagKey": "8",
  "AttackSkill": "Double Strafe"
}
```

Potential configuration conflicts:

```json
[
  {
    "Key": "1",
    "IsTeleport": true,
    "ItemName": "Fly Wing",
    "UseDelayMs": 600
  },
  {
    "Key": "F2",
    "SkillName": "Double Strafe",
    "SkillLevel": 8,
    "IsSkill": true,
    "SkillDelayMs": 10
  },
  {
    "Key": "8",
    "SkillName": "",
    "IsSkill": true,
    "SkillDelayMs": 10
  },
  {
    "Key": "9",
    "IsAmmo": true,
    "ItemName": "Arrow",
    "AmmoCount": 900
  }
]
```

Why this matters:

- `8` is marked as a skill but has no skill name. It still enters `SkillRotation`, so the bot alternates between `F2` and `8`.
- `8` is also `AmmoBagKey`. This may confuse skill rotation vs ammo-bag behavior.
- `SkillDelayMs = 10` for Double Strafe is almost certainly too low for skill key -> mouse click -> game accept -> after-cast wait.
- `NextMonsterDelayMs = 3000` is manual, not auto. This may make the bot feel too slow after kill.
- `FocusKillSeconds = 3` is manual, not auto. This may force target timeout behavior instead of learned TTK.

Recommendation for next test profile:

```text
FocusKillSeconds = -1
NextMonsterDelayMs = -1
RotationMs = -1
F2 SkillDelayMs = -1
Disable/remove the "8 is skill" action unless it really is a skill.
Keep 8 only as AmmoBag if needed.
Set ReturnAtWeightPercent to 0 only if disabled is desired.
Set FleeAtHpPercent to 0 only if flee disabled is desired.
```

## Log Evidence

Latest log file:

```text
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log
Length: 6,634,644 bytes
Last write: 2026-07-13 19:44:35
```

### OCR/GRF path working

At `2026-07-13 19:43:32`, OCR saw GRF markers and published attackable entities:

```text
[OCR] visionAssist=True targetSource=grf boxDet=2 codeReads=2 nameUnknown=0
[OCR] Entity scan ... raw=2 entities=2 ... safeForBot=True clientCoords=True published=True
sourceGrf=2 sourceYolo=0 ... visible=2 confirmed=2 attackable=2
rawSample=[grf mobId=1756 name=Hydrolancer ...; grf mobId=3199 name=Wicked Mutant Dragon ...]
sceneSample=[#205:Hydrolancer ... atk=True; #206:Enriched Poporing ... atk=True]
```

At `2026-07-13 19:44:20`, OCR still had valid attackable entities:

```text
[OCR] Entity scan ... raw=3 entities=3 ... safeForBot=True clientCoords=True published=True
sourceGrf=3 ... visible=3 confirmed=3 attackable=3
rawSample=[grf Red Plant ...; grf Skeleton ...; grf Skeleton ...]
sceneSample=[#205:Red Plant ... atk=True; #219:Skeleton ... atk=True; #217:Skeleton ... atk=True]
```

### Smart Bot was not entering target selection in latest run

In the latest run, instead of `Vision decision reason=target`, Smart Bot only sends key `1` about every 8 seconds:

```text
[2026-07-13 19:43:23.773] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:43:32.295] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:43:40.366] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:43:48.494] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:43:56.683] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:44:11.610] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
[2026-07-13 19:44:20.089] [SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
```

This matches:

```json
"TeleportKey": "1",
"StuckSeconds": 8
```

Hypothesis:

- A pre-combat gate or unstuck/flee path is firing before target selection.
- The emergency path had poor logging before the patch, making this hard to see.
- After the latest code patch, the next run should include explicit `HP <= flee` logs if flee is the reason.

### Earlier run shows input and clicks can work

At `2026-07-13 19:41`, the bot successfully sent skill keys and VIIPER mouse clicks:

```text
[SmartBot] Vision decision reason=target ... chosen=trk#196:Familiar ...
[SmartBot] TapAction keyboard-first action='F2' vk=113 method=Viiper holdMs=20.
[Input] VIIPER key sent key=F2 holdMs=60.
[SmartBot] ClickAt requested hwnd=0x2309DC client=929,507 hardware=True mouseMethod=Viiper virtualButton=A hold=100.
[Input] Route step OK: VIIPER mouse screen=929,507 holdMs=100 moveMs=27.
```

This suggests VIIPER input was not globally broken in that run. The later failure is more likely control flow/configuration, not raw mouse driver failure.

## High-Priority Issues for Second Opinion

### 1. Combat path can be starved by gates before target selection

Current order:

```text
MaybeRefreshBuffs
HP/weight gates
Ammo gate
Vision target selection
Attack
Roam
TrackProgressAndUnstuck
```

Concern:

- If HP/weight/ammo/unstuck gates fire too often, valid vision targets are ignored.
- Logs previously did not clearly explain which gate skipped combat.

Suggested improvement:

- Add one structured "loop decision" log per cycle when Smart Bot is running, throttled to maybe 500 ms:

```text
SmartBotLoop enabled=True hp=... wt=... ammo=... entitiesFresh=True attackable=3 gate=None action=target
SmartBotLoop enabled=True hp=12 flee=25 action=teleport key=1
SmartBotLoop enabled=True wt=92 returnAt=90 action=return key=2
SmartBotLoop enabled=True ammo=0 stopAt=50 action=ammo-bag key=8
```

This would make it obvious why combat did or did not happen.

### 2. Tracker label drift in GRF mode

There are log cases where raw GRF detection says one name, but scene/tracker label carries another name. Example pattern:

```text
rawSample=[grf mobId=1750 name=Red Plant ...]
sceneSample=[#205:Hydrolancer ... same/near box ...]
```

This likely means the tracker is matching by IoU/position and preserving old label/name voting across different mob identities.

In GRF mode, raw names are authoritative. The tracker should not smear names across different `mobId`.

Suggested fixes to discuss:

- Include `mobId` as part of track association in GRF mode.
- Reject matches where both sides have known mobId and mobId differs.
- If a GRF raw detection is matched to an existing track but mobId differs, either:
  - start a new track, or
  - immediately update the track label/mobId and reset name vote history.
- Name voting should be per TrackId and reset on class/mobId change.
- Generic `mobId=-1` should be allowed to match only generic/unknown tracks, not overwrite a known mob.

### 3. Skill rotation includes invalid/no-name skill row

Active config has:

```json
{
  "Key": "8",
  "SkillName": "",
  "IsSkill": true,
  "SkillDelayMs": 10
}
```

The engine currently builds `SkillRotation` from enabled skill rows. It appears to accept any enabled `IsSkill` row with a key, even if `SkillName` is blank.

Concern:

- The bot alternates between `F2` and `8`.
- `8` may be intended as ammo bag, not attack skill.
- This directly affects skill -> click timing and kill loop.

Suggested fix:

- For Smart Bot attack rotation, include only `IsSkill && !string.IsNullOrWhiteSpace(SkillName)`.
- If user checks "Skill" but no skill selected, UI should show a warning and not wire it into attack rotation.
- If a key is assigned to AmmoBag, do not allow it to also be Skill unless user explicitly enables an advanced "multi-role key" override.

### 4. Manual delays are too aggressive

Active config:

```text
RotationMs = 10
F2 SkillDelayMs = 10
8 SkillDelayMs = 10
FocusKillSeconds = 3
NextMonsterDelayMs = 3000
```

Concern:

- `10ms` is too short for RO skill execution and after-cast delay.
- Manual `FocusKillSeconds = 3` may be too short or too long depending on monster HP and skill damage.
- Manual `NextMonsterDelayMs = 3000` makes next target selection feel delayed.

Suggested default/testing config:

```text
RotationMs = -1
F2 SkillDelayMs = -1
FocusKillSeconds = -1
NextMonsterDelayMs = -1
```

Then let Smart Bot Training and formula timing handle it.

### 5. No clear "attack cycle" state machine

Current Smart Bot behavior is mostly a loop with conditions. It may benefit from explicit combat states:

```text
Idle
AcquireTarget
EngageTarget
ArmSkill
ClickTarget
WaitSkillResult
ConfirmDamage
RepeatUntilDead
LootOrRetarget
EmergencyTeleport
Roam
```

Reason:

- TTK/death logic is easier when target lifecycle is explicit.
- Prevents roam/teleport/buff/ammo paths from interleaving at unsafe moments.
- Makes logs and debugging much clearer.

Possible minimal version:

```csharp
enum SmartBotState
{
    Idle,
    Hunting,
    Engaging,
    WaitingForDamage,
    RetargetDelay,
    Roaming,
    Emergency
}
```

### 6. Death detection still lacks reliable HP association in GRF mode

Current logs in GRF mode often show:

```text
hpBars=0
hp=none
```

That means TTK/death logic often falls back to:

- target vanished after damage/casts,
- expected deadline,
- EXP gain,
- track gone.

Concern:

- If no monster HP bar is associated, the bot cannot precisely know when the monster dies.
- GRF visual boxes may be enough to know when a marker disappears, but if tracker coasts or label drifts, death detection can lag.

Suggested improvements:

- In Vision Assist GRF mode, treat disappearance of the GRF marker for the engaged `mobId/trackId` as stronger death/retarget evidence than YOLO missed frames.
- Add a short "death grace" state:

```text
Engaged target missing raw GRF marker for 2 consecutive scans
AND target had at least one skill/click
AND no visible matching mobId within nearby area
=> finish target or retarget
```

- Do not use LostGrace for clicking.
- Draw LostGrace only for visual smoothness.

### 7. Packaging still not true one-exe

`docs\ONE_EXE_PACKAGING.md` explains the desired one-exe release. Current command used:

```powershell
dotnet publish src\4rVivi.App\4rVivi.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64 --no-restore --nologo
```

This is still a folder publish. It includes:

```text
4rVivi.exe
4rVivi.dll
4rVivi.Core.dll
OcrServer\
OcrServerCuda\
models/data/native dependencies
```

The one-exe target from the MD requires:

- `PublishSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- `EnableCompressionInSingleFile=true`
- embedding/extracting OcrServer and models/data, or an in-process worker
- likely content-hashed extraction to `%LocalAppData%\4rVivi`

This is not solved yet.

## Commands for Claude / Reviewer

Run from repo root:

```powershell
cd "D:\vs code clone 4rtool\4ViviTools"
```

### Build/test

```powershell
dotnet build 4rVivi.sln -c Release --no-restore --nologo
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --no-restore --no-build --nologo
```

### Publish current folder release

```powershell
dotnet publish src\4rVivi.App\4rVivi.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64 --no-restore --nologo
```

### Extract relevant Smart Bot log lines

```powershell
rg -n "\[(SmartBot|Input|VIIPER|Mouse|Hotkey)\].*(Cast|Attack|Walk|Kill|Lost target|Target exceeded|TapAction|Click|mouse|key|target|no-target|decision|SP low|Next|Wait|stuck|StopAll)" `
  "C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log" |
  Select-Object -Last 220
```

### Extract OCR/GRF entity scan lines

```powershell
rg -n "Entity scan|visionAssist=True|targetSource=grf" `
  "C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log" |
  Select-Object -Last 120
```

### Show active Smart Bot config

```powershell
$s = Get-Content -Raw -LiteralPath "C:\Users\Vivi\AppData\Roaming\4rVivi\settings.json" | ConvertFrom-Json
$s.Profiles | ForEach-Object {
  "PROFILE: " + $_.Name
  $_.SmartBot | ConvertTo-Json -Depth 8
}
```

### Check current changed code locations

```powershell
rg -n "ReturnAtWeightPercent > 0|FleeAtHpPercent > 0|ResolveNextMonsterDelayMs|TrackTargetGone|TapAction\(|MaybeRefreshBuffs|SelectTarget|Vision decision|NextMonsterDelayMs|FocusKillSeconds" `
  src\4rVivi.Core\Automation\SmartBotEngine.cs `
  src\4rVivi.Core\Settings\AppSettings.cs `
  src\4rVivi.App\ViewModels\SmartBotViewModel.cs `
  src\4rVivi.App\Views\SmartBotView.axaml
```

### Compare raw GRF labels against scene/tracker labels

This is a quick manual grep. Look for raw/scene mismatches in the same entity scan line:

```powershell
rg -n "rawSample=.*sceneSample=" "C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log" |
  Select-Object -Last 100
```

Review examples where `rawSample` names do not agree with `sceneSample` names near the same box/track.

## Suggested Test Plan After Next Patch

1. Use a fresh or cleaned profile:

```text
F2 = Skill, Double Strafe, delay -1
1 = Teleport/Fly Wing, delay -1 or 600
2 = HP pot, threshold as desired
8 = ammo bag only if needed, not Skill
9 = ammo only if needed
Focus kill sec = -1
Next monster ms = -1
Rotation ms = -1
Return at weight = 0 if disabled
Flee at HP = 0 for first combat test, or 25 only if HP OCR is known reliable
```

2. Start OCR with Vision Assist GRF checked.
3. Confirm overlay status shows:

```text
sourceGrf > 0
attackable > 0
safeForBot=True
clientCoords=True
```

4. Start Smart Bot for 60 seconds in a small mob pack.
5. Upload:

```text
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log
C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\VIIPER.log
C:\Users\Vivi\AppData\Roaming\4rVivi\settings.json
```

Expected healthy behavior:

```text
Vision decision reason=target
Engaged target=#...|MonsterName
TapAction keyboard-first action='F2'
ClickAt requested ... client=x,y
Input Route step OK: VIIPER mouse
Cast 1: F2 on ...
Kill #... or target vanished after damage
```

Unhealthy behavior to catch:

```text
Only repeated TapAction action='1' with no Vision decision
Vision has attackable > 0 but no SmartBot target decision
Raw GRF mobId/name differs from scene label for the same box
Skill row with blank SkillName enters SkillRotation
Low HP flee triggers when HP OCR is stale/wrong
```

## Questions for Claude

1. Should Smart Bot be refactored into an explicit state machine now, or should we patch the current loop with more gate diagnostics first?
2. In GRF mode, should the tracker association require matching `mobId` before IoU matching?
3. Should GRF mode bypass the tracker name voting entirely and use tracker only for smoothing box coordinates?
4. Should any `IsSkill` row without `SkillName` be excluded from attack rotation automatically?
5. Should the UI prevent a single key from being both `Skill` and `AmmoBag` unless an advanced override is enabled?
6. Should `FleeAtHpPercent` depend only on fresh HP OCR, with stale HP disabling flee instead of teleporting?
7. Should `ReturnAtWeightPercent=0` and `FleeAtHpPercent=0` be shown in UI as "Off" rather than numeric `0`?
8. Should one-exe packaging be done before or after stabilizing Smart Bot behavior?

## Current Best Hypothesis

The latest "not attacking" issue is primarily control-flow/configuration, not driver failure:

- OCR/GRF publishes valid attackable entities.
- VIIPER input worked earlier in the same log.
- Smart Bot later only sent key `1`, which is configured as teleport.
- This matches emergency or unstuck behavior starving target selection.

The deeper accuracy issue is likely tracker identity/name handling in GRF mode:

- Raw GRF names are authoritative.
- Tracker can retain old label/vote across changing raw detections.
- This causes wrong names and potentially wrong monster logic even when the red boxes are correct.

The next technical focus should be:

1. Add high-signal Smart Bot gate/action logs.
2. Clean profile action-role conflicts.
3. Fix GRF tracker identity by mobId/name.
4. Exclude invalid skill rows from attack rotation.
5. Retest with all manual timing values set to `-1`.

