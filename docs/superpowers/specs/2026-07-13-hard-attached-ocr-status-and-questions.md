# 4ViviTools - Hard-Attached OCR Status And Questions

Prepared: 2026-07-13
Project root: D:\vs code clone 4rtool\4ViviTools

## Why This Note Exists

The user clarified a major behavior rule:

> OCR and Smart Bot should be hard-attached to the selected game client. They should not run freely over the whole monitor. If the game is not focused, OCR should turn off and Smart Bot should be off too. When the game moves on the monitor, the OCR/overlay should move with it and keep the same size as the game.

I started one small focus-gate patch before this note. I am pausing further hard-attach implementation until the questions below are answered.

## What I Changed Before This Note

### 1. Read Claude's HP/SP percent OCR reply

File read:

```text
docs\superpowers\specs\2026-07-13-claude-hp-sp-percent-reply.md
```

Key conclusion from Claude:

```text
The nonstop teleport is a trust bug first, OCR quality bug second.
```

Meaning: even a better OCR reader must not be allowed to publish one bad `2` as trusted HP and trigger teleport/autopot.

### 2. Added tests for trusted health and percent parsing

New file:

```text
tests\4rVivi.Core.Tests\HealthPercentSafetyTests.cs
```

Tests added:

1. A suspect HP value can exist in `LiveStats`, but `TryGetTrustedNumber()` rejects it.
2. A trusted HP value is accepted.
3. Percent text parser accepts safe forms like:

```text
100%
75 %
l00%
98
```

4. Percent text parser rejects unsafe forms like:

```text
2
200%
empty
```

I ran the targeted test and it passed:

```text
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj --filter HealthPercentSafetyTests --no-restore
Passed: 10
```

### 3. Added `LiveStats` metadata and trusted values

Modified file:

```text
src\4rVivi.Core\Game\LiveStats.cs
```

Added:

```csharp
LiveStatQuality.Trusted
LiveStatQuality.Held
LiveStatQuality.Suspect

LiveStatSource.Unknown
LiveStatSource.Ocr
LiveStatSource.BarFill
LiveStatSource.PercentText
LiveStatSource.Memory
LiveStatSource.Cache
LiveStatSource.User

LiveStatNumber
```

Added methods:

```csharp
SetNumber(role, value, source, confidence, rawText, quality)
HoldNumber(...)
TryGetNumberMeta(...)
TryGetTrustedNumber(...)
```

Compatibility note:

`TryGetNumber()` still exists for UI/readout compatibility, but automation can now require trusted stats.

### 4. Added HP/SP percent text parsing and OCR read method

Modified file:

```text
src\4rVivi.App\Services\OcrService.cs
```

Added:

```csharp
TryParsePercentText(...)
ReadPercentTextFrom(...)
```

Behavior:

1. Accepts `100%`, `75 %`, `l00%`.
2. Normalizes common OCR mistakes.
3. Rejects one-digit bare values like `2`, because that was the dangerous teleport trigger.
4. Reads HP/SP percent text with a stronger high-contrast/upscaled crop path.

Important limitation:

Claude recommended a deterministic bitmap digit-template matcher as the primary path. I have **not implemented that yet**. Current implementation is a safer OCR/parser path, not the final template matcher.

### 5. Changed HP/SP markers from bar-fill roles to percent-text roles

Modified file:

```text
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
```

Changed visible roles:

```text
HP Bar -> HP % Text
SP Bar -> SP % Text
```

Internal stat keys remain:

```text
Roles.HpPercent
Roles.SpPercent
```

Changed role behavior:

1. `HpPercent` and `SpPercent` are no longer `IsBarRole`.
2. `BaseExpBar`, `JobExpBar`, and `CastBar` remain bar-fill roles.
3. Saved old HP/SP bar marks are normalized to HP/SP percent roles but `IsBar` is cleared so they cannot publish dangerous bar-fill HP.
4. OCR status now tells the user to draw `HP % Text` / `SP % Text`.
5. HP/SP percent reads log a `[Vitals]` line with:

```text
role
parsed percent
engine
confidence
raw OCR text
normalized text
decision publish/hold/reject
quality Trusted/Held/Suspect
```

### 6. Added percent stability gate

Modified file:

```text
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
```

Behavior:

1. Valid HP/SP percent reads are median-smoothed.
2. Sudden large downward jumps greater than 25 points need confirmation before becoming trusted.
3. Failed reads can hold the last display value, but held values are not trusted by automation.

### 7. Routed multi-client HP/SP through the shared percent reader

Modified file:

```text
src\4rVivi.App\ViewModels\MultiClientViewModel.cs
```

Behavior:

HP/SP percent in multi-client now uses the same `ReadPercentTextFrom(...)` path instead of old bar/generic int parsing.

### 8. Updated health consumers to require trusted HP/SP

Modified files:

```text
src\4rVivi.Core\Game\HealthReader.cs
src\4rVivi.Core\Game\StatReader.cs
src\4rVivi.Core\Game\CharacterState.cs
```

Behavior:

HP/SP percent now returns `-1` unless the live stat is trusted.

### 9. Added safety confirmation to Smart Bot, Autopot, and AutoYgg

Modified files:

```text
src\4rVivi.Core\Automation\SmartBotEngine.cs
src\4rVivi.Core\Automation\AutopotEngine.cs
src\4rVivi.Core\Automation\AutoYggEngine.cs
```

Behavior:

1. Smart Bot HP freshness now requires `TryGetTrustedNumber(Roles.HpPercent)`.
2. Smart Bot flee requires confirmation before teleporting.
3. Smart Bot has flee hysteresis to avoid oscillation.
4. Autopot requires two low reads before firing, except extreme HP <= 1.
5. AutoYgg also requires confirmation before firing, except extreme HP <= 1.

### 10. Removed flat HP/MaxHP fallback from training recorder

Modified file:

```text
src\4rVivi.App\Services\SmartBotTrainingRecorder.cs
```

Behavior:

Training recorder no longer falls back to flat HP/MaxHP or SP/MaxSP for percent.

### 11. Updated some UI copy and palette aliases

Modified files:

```text
src\4rVivi.App\Services\RolePalette.cs
src\4rVivi.Core\Ocr\OcrMark.cs
src\4rVivi.App\Views\StatsView.axaml
```

Behavior:

1. Adds colors for `HP % Text` and `SP % Text`.
2. Updates comments so HP/SP no longer claim to be bar-fill.
3. Stats page text now says HP/SP come from `HP % Text` and `SP % Text`.

### 12. Started a small focus-gate patch

Modified files:

```text
src\4rVivi.Core\Game\GameSession.cs
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
```

What was added:

```csharp
GameSession.IsSelectedClientForeground
```

It checks whether the current Windows foreground window belongs to the selected attached process.

In the OCR loop, I started adding:

```text
If selected client is not foreground:
  clear LiveStats
  clear LiveScene
  log OCR paused
  status = "OCR paused - focus the attached RO client to resume."
```

Important:

This focus-gate patch is **started but not yet fully validated**. I paused here because the user asked me to write the status and questions before continuing hard-attach work.

## Build State

I ran a Release build after the HP/SP changes and got a compile error from local variable naming in `OcrReaderViewModel.cs`.

I fixed that naming collision by renaming percent-specific locals:

```text
raw -> percentRaw
conf -> percentConf
```

I have **not yet rerun the full build after the focus-gate patch**.

## Hard-Attached OCR Questions

### Q1. Focus rule

Should OCR and Smart Bot run only when the selected RO client process is the foreground process?

Proposed rule:

```text
Selected RO client focused:
  OCR runs
  overlay tracks client area
  Smart Bot can act

Anything else focused, including 4ViviTools itself:
  OCR pauses
  Smart Bot actions pause
  LiveStats/LiveScene become stale/cleared
```

Concern:

When the user clicks the 4ViviTools UI to configure markers or press Start/Stop, the RO client is no longer foreground. That means live OCR pauses while configuring. Is that acceptable?

### Q2. Stop vs pause

When focus leaves the RO client, should Smart Bot:

1. pause and auto-resume when the game is focused again;
2. fully stop and require the user to press Start again;
3. stop only after focus has been lost for more than a few seconds?

My recommendation:

Pause immediately, stop after sustained focus loss if the bot was running for safety.

### Q3. Multi-client conflict

Earlier requirements included multiple clients. The new focus rule means only one focused client can run active Smart Bot at a time.

Question:

For multi-client mode, should non-focused clients:

1. stop OCR entirely;
2. keep passive OCR only if they are visible;
3. never run Smart Bot actions unless focused?

My recommendation:

For now, only the focused selected client can run Smart Bot actions. Passive multi-client OCR should be a separate advanced mode later.

### Q4. Monitor capture

Should monitor capture be disabled for live Smart Bot mode?

Proposed rule:

```text
Bot mode:
  force client-window capture
  do not use monitor capture for click decisions

Manual OCR/debug mode:
  monitor capture allowed
```

Reason:

Monitor capture can drift when the game moves or when another window overlaps it. Client-window capture stays attached to the game handle.

### Q5. Overlay behavior

The OCR overlay already tracks the client area using:

```text
GetClientRect
ClientToScreen
```

Question:

Should the overlay hide when the client loses focus, or remain visible but show "paused"?

My recommendation:

Hide or dim the overlay when not focused, with a small "paused - focus game" indicator.

### Q6. Client size and coordinate source

Should every bot click coordinate be derived from the current client size immediately before clicking?

Proposed rule:

```text
Before every bot click:
  read current client rect
  clamp target to current client size
  convert client coordinates to screen coordinates
```

This avoids stale positions when the game window moves or resizes.

### Q7. Attached process vs exact window handle

`IsSelectedClientForeground` currently checks the foreground process id.

Question:

Should it instead require the exact selected `MainWindowHandle`, or is same-process enough?

Same process is more flexible if the client recreates the window handle. Exact handle is stricter and safer.

My recommendation:

Use same process id, but also refresh `WindowHandle`/reattach if the main window handle changes.

### Q8. What about private server launchers/wrappers?

Some Ragnarok clients may spawn launcher/helper windows under the same process or a related process.

Question:

Should we allow a small list of acceptable foreground windows by process id/name, or only the exact game executable?

My recommendation:

Only exact selected game process for now. Add advanced exceptions later if needed.

### Q9. What should happen to input drivers on focus loss?

Should VIIPER/FakerInput/ViGEm actions be disabled immediately when the client loses focus?

My recommendation:

Yes. Even if drivers stay installed/enabled, 4ViviTools should not send any keyboard/mouse/controller action unless the selected game client is focused.

### Q10. How should the UI show this?

Suggested status text:

```text
Client focused: OCR and Smart Bot active.
Client not focused: paused. Focus the attached RO client to resume.
```

Suggested health row:

```text
Client: Focused / Not focused
OCR: Running / Paused
Bot: Running / Paused
```

### Q11. Should F12 panic work even when game is not focused?

My recommendation:

Yes. The panic/stop key should be global and should stop all automation even if the game is not focused.

### Q12. Should the app ever bring the game to foreground automatically?

My recommendation:

No, unless the user explicitly clicks a "Focus client" button. Hard-attached should mean "only act when the selected client is already focused," not "steal focus."

## Proposed Next Implementation Plan After Questions

1. Finish build after current HP/SP trust changes.
2. Decide focus rule.
3. Move the focus gate into a shared method/service so OCR, Smart Bot, Autopot, AutoYgg, and any input action use the same rule.
4. Force Smart Bot vision source to client-window capture, not monitor capture.
5. Clear or mark stale `LiveStats`/`LiveScene` when focus is lost.
6. Add DebugTrace lines:

```text
[FocusGate] active=True hwnd=... pid=... foregroundPid=...
[OCR] paused because selected client is not focused
[SmartBot] paused because selected client is not focused
```

7. Update UI status text.
8. Build and run tests.

