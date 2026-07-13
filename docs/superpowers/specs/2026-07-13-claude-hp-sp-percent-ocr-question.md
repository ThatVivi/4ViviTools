# 4ViviTools - Claude Question Packet: HP/SP Percent Text OCR

Prepared: 2026-07-13
Project root: D:\vs code clone 4rtool\4ViviTools
Current runtime log: C:\Users\Vivi\AppData\Roaming\4rVivi\Logs\DebugTrace.log

## Context

We need a second opinion before changing the HP/SP reader again.

The current bar-fill reader is not reliable enough for Ragnarok Online Basic Info HP/SP. The user wants to move to reading the visible percentage text next to the bars, for example `100%`. The user will manually draw a tight OCR marker around that `100%` text. The tool should publish that value as the live `HpPercent` or `SpPercent`.

The immediate bug is severe: a bad HP percent is being published as fresh, so Smart Bot believes HP is low and teleports nonstop instead of attacking.

The user also observed another instability while running OCR: the OCR status/engine appeared to switch between PaddleOCR and Windows OCR repeatedly, roughly every second. That needs investigation because HP/SP percent text will only be reliable if the OCR engine selection is stable and predictable. If the app is falling back to Windows OCR intermittently, or if different marks are being routed through different engines without a clear reason, the readout can flicker and publish inconsistent values.

## What The Latest Debug Log Shows

The latest run does not look like a monster detection failure. GRF entity detection is producing valid attackable monsters:

```text
[OCR] visionAssist=True targetSource=grf ...
[OCR] Entity scan ... sourceGrf=1 sourceYolo=0 ... visible=1 confirmed=1 attackable=1 ...
rawSample=[grf mobId=3654 name=Zombie score=0.95 box=1323,474,38x70]
```

But Smart Bot repeatedly sees HP as `2`, considers it fresh, and chooses the flee branch:

```text
[SmartBot] loop ... entities=1 visible=1 confirmed=1 attackable=1 hp=2 hpFresh=True flee=25 ... branch=flee ...
[SmartBot] TapAction keyboard-first action='1' vk=49 method=Viiper holdMs=20.
```

Autopot also fires repeatedly from the same bad stat chain:

```text
[Autopot] TapAction controller action='2' resolved='B' holdMs=15 mapCount=5.
[Autopot] Controller bridge is not running; sending real key fallback for '2'.
```

Root cause hypothesis:

1. `HP Bar` and `SP Bar` are still treated as colored bar-fill regions.
2. The crop is producing a false low value such as `2`.
3. `LiveStats` publishes that value as `Roles.HpPercent`.
4. `SmartBotEngine.IsHpFresh()` only checks whether `Roles.HpPercent` exists, not whether the read is credible.
5. Smart Bot and Autopot consume the bad value as trusted live state.
6. OCR engine routing may be unstable if the UI/status is switching between PaddleOCR and Windows OCR while OCR is running.

## Current Code Wiring

Relevant files:

```text
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
src\4rVivi.App\Services\OcrService.cs
src\4rVivi.Core\Automation\SmartBotEngine.cs
src\4rVivi.Core\Automation\AutopotEngine.cs
src\4rVivi.Core\Automation\AutoYggEngine.cs
src\4rVivi.Core\Game\HealthReader.cs
src\4rVivi.Core\Game\StatReader.cs
src\4rVivi.Core\Game\CharacterState.cs
src\4rVivi.App\ViewModels\MultiClientViewModel.cs
src\4rVivi.App\Services\SmartBotTrainingRecorder.cs
```

Current OCR role list includes:

```csharp
"HP Bar", "SP Bar", "Weight / MaxWeight",
```

Current role normalization:

```csharp
private static string NormalizeMarkRole(string role) => role switch
{
    "HP Bar" => FourRVivi.Core.Game.Roles.HpPercent,
    "SP Bar" => FourRVivi.Core.Game.Roles.SpPercent,
    _ => role
};
```

Current bar routing:

```csharp
private static bool IsBarRole(string role)
{
    role = NormalizeMarkRole(role);
    return role is "BaseExpBar"
        or "JobExpBar"
        or FourRVivi.Core.Game.Roles.HpPercent
        or FourRVivi.Core.Game.Roles.SpPercent
        or "CastBar";
}
```

Current read loop routes bar marks here:

```csharp
if (m.IsBar)
{
    int pct = frame != null
        ? _ocr.ReadBarPercentFrom(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide, m.Role)
        : _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx, m.Role);
    if (pct >= 0)
        pct = SmoothBarPercent(m.Role, pct);
    var stB = Resolve(m.Role, pct >= 0);
    if (stB == LockState.Publish)
        LiveStats.Instance.SetNumber(m.Role, pct);
    continue;
}
```

Current `SmartBotEngine` freshness check:

```csharp
private bool IsHpFresh(double hp)
{
    if (hp <= 0 || hp > 100)
        return false;
    return LiveStats.Instance.TryGetNumber(Roles.HpPercent, out _);
}
```

This means a wrong-but-published `2` is enough to trigger flee.

## Proposed Direction

I want to change HP/SP from "bar fill" to "percent text OCR" while keeping the downstream stat keys unchanged:

```text
User marker: HP % text around the visible "100%"
Internal stat: Roles.HpPercent
User marker: SP % text around the visible "100%"
Internal stat: Roles.SpPercent
```

The downstream Smart Bot, Autopot, Discord, Stats tab, 4RTools shell, ro-tools shell, and training recorder should still consume `Roles.HpPercent` and `Roles.SpPercent`. Only the upstream reader changes.

Proposed OCR pipeline:

1. Rename or relabel UI roles from `HP Bar` / `SP Bar` to `HP % Text` / `SP % Text`, or keep the old labels but update their notes to say "draw around 100% text next to the bar".
2. Normalize those roles to `Roles.HpPercent` / `Roles.SpPercent`.
3. Remove HP/SP percent roles from `IsBarRole`.
4. Add `IsPercentTextRole` for `Roles.HpPercent` and `Roles.SpPercent`.
5. Route HP/SP through a percent-specific text reader, not `ReadBarPercentFrom`.
6. Preprocess for RO percentage text:
   - tight crop;
   - pad a few pixels;
   - upscale 4x to 6x;
   - grayscale/high contrast;
   - optional inverted contrast pass;
   - numeric OCR path;
   - parse only a 0-100 percent.
7. Normalize OCR mistakes:
   - `O`, `o`, `D` -> `0` where numeric context makes sense;
   - `I`, `l`, `|` -> `1`;
   - `S` -> `5`;
   - strip spaces;
   - accept `100%`, `100 %`, `100`, maybe `l00%` after normalization.
8. Add a confidence and stability gate before publishing:
   - parse must be valid 0-100;
   - preferably text contains `%`, but maybe allow bare digits when confidence is high;
   - avoid publishing sudden impossible drops from high HP to very low HP unless confirmed by 2 reads or strong confidence;
   - if parse fails, hold previous value visually but mark stat stale for bot/autopot decisions.
9. Add precise debug logging for every HP/SP percent read:
   - role;
   - crop coordinates;
   - engine;
   - raw text;
   - normalized text;
   - parsed value;
   - confidence;
   - previous value;
   - decision: publish, hold, reject;
   - reject reason.
10. Update Smart Bot and Autopot to require a trusted/fresh percent state, not merely "a value exists".
11. Make OCR engine routing deterministic and visible:
   - log selected engine per mark;
   - log fallback reason when PaddleOCR is unavailable;
   - avoid switching engines every read unless explicitly in ensemble mode;
   - keep HP/SP percent on one chosen path, or use a controlled ensemble that votes without changing the displayed runtime every second.

## Main Questions For Claude

1. Should the UI labels be changed to `HP % Text` and `SP % Text`, or should we keep `HP Bar` / `SP Bar` for user familiarity and only change their behavior? My preference is to show `HP % Text` / `SP % Text` because the current names make the user draw the wrong crop.

2. Should HP/SP percent parsing require the `%` character, or accept bare digits from the crop? Requiring `%` reduces false positives, but OCR may miss the percent glyph. A compromise is: accept `%` at lower confidence; accept bare digits only if confidence is high and the marker is explicitly an HP/SP percent role.

3. What temporal filter would you use for HP/SP percent?
   - median of last 3 valid reads;
   - 2 consecutive confirmations before publishing large drops;
   - hysteresis for low HP triggers;
   - reject single-frame drops greater than a threshold unless followed by another low read.

4. Should `LiveStats` gain stat metadata, such as source, confidence, raw text, and timestamp, instead of only storing a number? This would let Smart Bot distinguish "latest trusted HP 100" from "OCR failed, holding old value" and "OCR read suspicious 2".

5. For safety, should Smart Bot and Autopot require two confirmed low HP reads before teleport/pot, except maybe a value of `0` or `1` with very high confidence? This would directly prevent nonstop teleport from one bad read.

6. Should HP/SP percent marks be invalidated or migrated in existing profiles? Old saved `HP Bar` markers probably surround the colored bar, not the `100%` text. If we silently change behavior, old profiles will fail until the user redraws the marker.

7. Should `BaseExpBar`, `JobExpBar`, and `CastBar` remain bar-fill roles while HP/SP become percent-text only? My preference is yes: only HP/SP are changing because the visible percentage text is more reliable for Basic Info.

8. What exact preprocess should we try first for RO's Basic Info percent text? The target is tiny light text on UI panel background. Would you use PaddleOCR numeric, Windows OCR numeric, an ensemble, or a custom template/digit matcher for `0-9%`?

9. Should we implement a tiny deterministic digit template reader for `100%` style text instead of general OCR? Since HP/SP percent is a very small character set, a template/matching reader may be faster and more stable than Paddle/Windows OCR.

10. Should the percent reader crop include only the `100%` text, or include a little of the bar/UI around it to help preprocessing? The user will draw the box manually, so we can instruct tight crop plus automatic padding.

11. Multi-client currently has its own mark reader path. Should multi-client HP/SP percent use the same shared percent reader to avoid two different behaviors?

12. Smart Bot Training Recorder still has fallback logic from flat HP/MaxHP and SP/MaxSP in some places. Should all consumers drop the flat-number fallback entirely now that the product direction is HP/SP percent text?

13. What should the user-facing default be when HP/SP percent is missing or stale?
   - disable flee/autopot until the marker is valid;
   - show warning in OCR tab and Bot tab;
   - keep bot attacking but block emergency actions;
   - stop Smart Bot completely.

14. Should the debug log include a compact health line every loop, for example:

```text
[Vitals] hp=100 src=percentText fresh=True conf=0.91 raw='100%' decision=publish ageMs=42
```

This would make future failures much easier to diagnose.

15. Is there any reason to keep colored HP/SP bar-fill as an advanced fallback? My current recommendation is no for bot decisions, maybe yes only as a visible debug helper, because the current bar-fill path can publish dangerous false lows.

16. What could cause the OCR status to switch between PaddleOCR and Windows OCR every second while OCR is running? Should the app expose one global engine state, or should each marker show its own engine? My concern is that unstable engine routing will make HP/SP percent reads inconsistent even after switching to percent text.

17. Should HP/SP percent text force one engine, such as PaddleOCR CUDA if available, and only fall back to Windows OCR after a logged hard failure? Or should it use a deterministic ensemble where both engines read the same crop and the parser/voter chooses the result?

## My Preferred Implementation Shape

I would implement it as a small vertical slice first:

1. Add a `ReadPercentTextFrom(...)` method in `OcrService`.
2. Add `ParsePercent(...)` with normalization and tests.
3. Change `OcrReaderViewModel` so `HpPercent` and `SpPercent` are no longer `IsBar`.
4. Route those two roles through the new percent reader.
5. Add stat quality metadata or at least a small `HealthPercentGate` in the OCR reader before `LiveStats.SetNumber`.
6. Update Smart Bot and Autopot to only act on trusted fresh percent reads.
7. Update UI copy and saved marker migration/warnings.
8. Add debug lines showing raw percent OCR and publish decisions.
9. Add debug lines showing OCR engine selection/fallback per mark so PaddleOCR/Windows OCR switching is explainable.

## Success Criteria

1. User draws a marker around `100%`.
2. OCR readout shows `HpPercent = 100%` and `SpPercent = 100%`.
3. If OCR fails, Smart Bot does not treat the failed read as low HP.
4. A single bad read like `2` does not trigger teleport.
5. A real sustained low HP, such as `24%`, still triggers teleport/autopot.
6. DebugTrace explains every HP/SP publish or reject decision.
7. DebugTrace explains which OCR engine read each HP/SP crop and why any fallback happened.

## Additional UI/UX Question Packet

The user will attach two screenshots with this question packet:

```text
C:\Users\Vivi\AppData\Local\Packages\MicrosoftWindows.Client.Core_cw5n1h2txyewy\TempState\ScreenClip\{8CA027B4-EBE3-49C2-B83E-4153BDBEAD08}.png
C:\Users\Vivi\AppData\Local\Packages\MicrosoftWindows.Client.Core_cw5n1h2txyewy\TempState\ScreenClip\{6021E7FC-4329-4799-9932-258E7F89397A}.png
```

The screenshots show the current Smart Bot "Hunt behavior" section and the current OCR/Capture screen. Both are functional, but too noisy for a new Ragnarok Online player. The product goal is a beginner-friendly tool where the user can:

1. attach a client;
2. set OCR markers;
3. enable Vision Assist GRF if they use it;
4. choose hotbar keys and skills;
5. start Smart Bot;
6. understand driver/input state without reading driver internals.

Current concern: the UI exposes too much engineering detail at the top level:

```text
- VIIPER, FakerInput, ViGEm, reWASD, virtual HID, driver folder, repair buttons, input stack text.
- Manual timing formula text and several numeric timing boxes.
- OCR capture controls, marker review controls, capture mode controls, filter/sharp/top/side settings, GRF controls, and marks list all visible at once.
- "HP Bar" label is now actively misleading because the new direction is HP/SP percent text.
```

Important UI source files for review:

```text
src\4rVivi.App\Views\SmartBotView.axaml
src\4rVivi.App\ViewModels\SmartBotViewModel.cs
src\4rVivi.App\Views\OcrReaderView.axaml
src\4rVivi.App\ViewModels\OcrReaderViewModel.cs
src\4rVivi.App\Views\MainWindow.axaml
src\4rVivi.App\ViewModels\MainWindowViewModel.cs
src\4rVivi.App\ViewModels\NavItems.cs
src\4rVivi.App\Styles\Colors.axaml
src\4rVivi.App\Styles\Controls.axaml
```

Other potentially relevant tabs/files:

```text
src\4rVivi.App\Views\AutopotView.axaml
src\4rVivi.App\ViewModels\AutopotViewModel.cs
src\4rVivi.App\Views\StatsView.axaml
src\4rVivi.App\ViewModels\StatsViewModel.cs
src\4rVivi.App\Views\FourRToolsShellView.axaml
src\4rVivi.App\ViewModels\FourRToolsShellViewModel.cs
src\4rVivi.App\Views\RoToolsShellView.axaml
src\4rVivi.App\ViewModels\RoToolsShellViewModel.cs
src\4rVivi.App\Views\MultiClientView.axaml
src\4rVivi.App\ViewModels\MultiClientViewModel.cs
src\4rVivi.App\Views\CalculatorView.axaml
src\4rVivi.App\ViewModels\CalculatorViewModel.cs
```

### 4RTools / ro-tools Direction

We are leaning toward removing the visible `4RTools` and `ro-tools` shell tabs from the normal app UI because they make the product feel fragmented and duplicate several features we already own. However, we do **not** want to throw away the useful address/memory-reader work.

The intended direction:

1. Remove or hide the user-facing 4RTools and ro-tools bridge/shell pages from top-level navigation.
2. Keep the address-reading strategy internally where it helps reliability.
3. Build our own clean "RO client data" service that can read the same kind of values 4RTools reads when the user/server setup supports it.
4. Use that internal data as a first-class source or fallback for HP %, SP %, buffs, map, position, weight, ammo, and other values.
5. Keep the UI branded as 4ViviTools only. The user should not have to understand 4RTools/ro-tools tabs.
6. If we still expose this, it should be under `Data` or `Advanced -> Integrations`, not in the beginner Smart Bot/OCR path.

Question for Claude: is this the right tradeoff? Should we delete the shell tabs now, hide them behind Advanced, or keep them temporarily while the internal address service is stabilized?

### Proposed UI Direction

I am leaning toward a two-layer UI:

1. **Beginner mode by default.**
   - Shows only the main player actions.
   - Smart Bot: Start, Stop, selected client, selected map, HP flee percent, hotbar/action cards, marker health, input status.
   - OCR: Attach client, capture/preview, mark HP %, mark SP %, mark map/name if needed, Vision Assist GRF toggle, Run OCR.
   - Driver section becomes one compact health row: "Input: VIIPER ready" with a single "Manage" button.

2. **Advanced drawer/panel.**
   - Contains driver repair buttons, folders, reWASD bridge, FakerInput, ViGEm, VIIPER test buttons, raw input stack, top/side pixel offsets, sharpen, filter, manual timing, threshold sliders, detailed marks list.
   - Existing controls are not deleted if still useful, but moved out of the default path.

3. **Task-based grouping.**
   - Replace broad piles of settings with workflows:
     - "Client"
     - "OCR Markers"
     - "Vision Assist GRF"
     - "Hotbar Actions"
     - "Safety"
     - "Input Driver"
     - "Advanced"

4. **Status should be natural language.**
   - Instead of "Input stack: VIIPER virtual USB -> FakerInput/vmouse -> ViGEm virtual Xbox -> normal fallback off", show:
     - "Input ready: keyboard and mouse through VIIPER."
     - "Mouse driver missing: open Manage Input to install/test."

5. **HP/SP marker wording must change immediately.**
   - The role should say `HP % Text` and `SP % Text`.
   - The note should say "Draw tightly around the visible 100% text next to the HP/SP bar."
   - The readout should show raw OCR/debug only in advanced mode.

### UI/UX Questions For Claude

1. How would you reorganize the Smart Bot "Hunt behavior" section from the screenshot so it is less intimidating but still gives power users access to driver and timing controls?

2. Should input driver setup be its own small setup card/tab, or should it remain inside Smart Bot behind a "Manage input" drawer?

3. What controls should be visible in beginner mode for Smart Bot?
   - I think visible: Start/Stop, selected input status, Flee at HP %, selected map/client, action hotbar cards, walking delay only if manual override is needed.
   - I think hidden: raw input stack, repair buttons, driver folders, test buttons, manual focus kill timing, next monster timing, VIIPER/FakerInput/ViGEm internals.

4. What controls should be visible in beginner mode for OCR/Capture?
   - I think visible: Attach client, Capture preview, Mark dropdown, Use markers, Vision Assist GRF, Run OCR, Reset OCR defaults.
   - I think hidden: top/side offsets, sharpen, filter, DXGI details, monitor mode, detailed marks list, raw confidence sliders unless advanced is enabled.

5. Should the OCR marks list on the right be collapsed by default and only show problems, such as "HP % missing" or "SP % stale", instead of every saved mark?

6. What should the one-line "health" indicators be for the whole program?
   - OCR ready/not ready
   - HP/SP valid/stale
   - Vision Assist GRF on/off
   - Input ready/not ready
   - Smart Bot running/stopped

7. How should we show `-1 auto` timing values without making the UI look like an error? Current text says `-1 = Auto timing`, but it still feels technical. Should we render `-1` as an "Auto" pill and only show the numeric box after the user picks manual override?

8. Should all advanced values use an "Auto / Manual" segmented control instead of exposing `-1` directly?

9. Are 4RTools and ro-tools bridge tabs worth keeping as visible top-level navigation, or should they move under Data/Integrations so the main app does not look fragmented?

10. Should Calculator stay as one tab, but with a cleaner RO-focused flow: Character -> Gear -> Skills -> Monster -> Result, instead of many dense boxes?

11. Should we keep the black/red theme but reduce red borders on non-danger panels? The current red outline everywhere makes normal setup look like warnings.

12. What should be the ideal first-run path for a new user from download to first working Smart Bot run?

13. What is the smallest UI restructuring that would materially improve the app without causing a giant risky rewrite?

14. Should driver names like VIIPER/FakerInput/ViGEm be shown to normal users at all, or only as "Keyboard driver", "Mouse driver", and "Controller driver" with the real names in tooltips/advanced?

15. Can you propose a compact layout for the two attached screenshots, using the current Avalonia app and files above, not a full framework migration?

## Specific Advice Requested

Please review the direction above and answer:

1. Which UI naming/migration choice is safest?
2. Which percent OCR pipeline is most reliable for tiny RO `100%` text?
3. Which stability gate should protect Smart Bot and Autopot?
4. Should we extend `LiveStats` with metadata now, or keep the change smaller with role-specific gates?
5. Do you see any hidden risk in removing HP/SP from bar-fill reading entirely?
6. How should we compact the Smart Bot and OCR/Capture UI shown in the attached screenshots while keeping advanced power-user controls available?
7. Which UI files should be changed first for the highest impact with the least risk?
8. Should visible 4RTools/ro-tools shells be removed now while keeping their address-reading ideas internally as a 4ViviTools data service?
9. How should we stop or explain the observed PaddleOCR/Windows OCR switching while OCR is running?
