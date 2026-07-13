AGENT: 2 OCR/HP-SP

FILES INSPECTED:
- docs/superpowers/specs/CONTRACTS.md
- docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md
- src/4rVivi.Core/Game/LiveStats.cs
- src/4rVivi.Core/Game/HealthReader.cs
- src/4rVivi.Core/Game/StatReader.cs
- src/4rVivi.Core/Game/CharacterState.cs
- src/4rVivi.Core/Game/LiveScene.cs
- src/4rVivi.Core/Game/Roles.cs
- src/4rVivi.Core/Ocr/OcrMark.cs
- src/4rVivi.Core/Automation/SmartBotEngine.cs
- src/4rVivi.Core/Automation/AutopotEngine.cs
- src/4rVivi.Core/Automation/AutoYggEngine.cs
- src/4rVivi.Core/Automation/BotFarmEngine.cs
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
- src/4rVivi.App/ViewModels/MultiClientViewModel.cs
- src/4rVivi.App/ViewModels/MainWindowViewModel.cs
- src/4rVivi.App/ViewModels/StatsViewModel.cs
- src/4rVivi.App/ViewModels/FourRToolsShellViewModel.cs
- src/4rVivi.App/ViewModels/RoToolsShellViewModel.cs
- src/4rVivi.App/Services/OcrService.cs
- src/4rVivi.App/Services/SmartBotTrainingRecorder.cs
- src/4rVivi.App/Services/DiscordPresenceBootstrap.cs
- src/4rVivi.App/Services/VisionAssistMarkerDetector.cs
- src/4rVivi.App/Views/OcrReaderView.axaml
- Core/Fallback.cs
- Bot/BotEngine.cs
- Farm/FarmModules.cs
- Features/AdvancedAutopot.cs

FILES CREATED (owned):
- docs/superpowers/specs/agent-2-ocr-health-handoff.md

PROPOSED DIFFS FOR MAIN (shared files):
None generated in this agent pass. Scope was audit-only, no shared code edits. Main-thread patch list with exact files/lines is in FINDINGS.

FINDINGS:

1. Normal HP/SP writer is percent text, and held values cannot pass trusted reads.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1261-1304 is the normal HP/SP percent path. It calls `OcrService.ReadPercentTextFrom(...)`, publishes with `LiveStatSource.PercentText` and `LiveStatQuality.Trusted` at line 1285, and writes held values with `HoldNumber(..., LiveStatQuality.Held)` at line 1291.
- src/4rVivi.App/Services/OcrService.cs:251-266 is the percent-text reader and parser path.
- src/4rVivi.Core/Game/LiveStats.cs:65-66 stores held values as `LiveStatQuality.Held`.
- src/4rVivi.Core/Game/LiveStats.cs:87-105 only returns from `TryGetTrustedNumber` when quality is `Trusted` and age is within the max age.

2. Bar-fill HP/SP is not fully dead. There is a generic live writer that can still publish HP/SP if a stale/malformed mark has `IsBar=true`.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1250-1257 handles any `m.IsBar` mark and calls `LiveStats.Instance.SetNumber(m.Role, pct)` without source/quality metadata. If `m.Role` is `HpPercent` or `SpPercent`, this creates a trusted bare number through `LiveStats.SetNumber(string,int)`.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:508 normally resets loaded HP/SP marks to `IsBar=false`, and src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:381-384 excludes HP/SP from `IsBarRole`. That makes the normal UI path safe, but not provably dead against old JSON/manual mutation.
- src/4rVivi.App/Services/OcrService.cs:181-196 still advertises `ReadBarPercent` as HP/SP/EXP bar reading.
- src/4rVivi.App/Services/OcrService.cs:1208-1214 exposes `ReadBarPercentFrom`.
- src/4rVivi.App/Services/OcrService.cs:1220-1226 falls back from color fill to brightness fill.
- src/4rVivi.App/Services/OcrService.cs:1233-1234 explicitly treats `HpPercent`, `SpPercent`, `HP Bar`, and `SP Bar` as color bar roles.
- src/4rVivi.Core/Game/LiveStats.cs:10-17 still contains `LiveStatSource.BarFill`, while CONTRACTS.md says source is `PercentText | Memory | Manual` and BarFill is dead for HP/SP.

Main-thread changes needed:
- Guard `OcrReaderViewModel` before the `m.IsBar` branch: if `IsPercentTextRole(m.Role)`, force the percent-text branch or reject the mark. Do not allow `SetNumber(m.Role,pct)` for `Roles.HpPercent` or `Roles.SpPercent`.
- Remove HP/SP recognition from `OcrService.ColorBarFill`, update the `ReadBarPercent` comment, and keep bar-fill only for EXP/cast/non-vital bars.
- Remove or quarantine `LiveStatSource.BarFill` unless main wants to keep it only for non-HP/SP metadata with explicit comments.

3. Flat HP/MaxHP and SP/MaxSP fallback paths remain.
- src/4rVivi.Core/Game/HealthReader.cs:13-16 still exposes flat HP/MaxHP/SP/MaxSP, and src/4rVivi.Core/Game/HealthReader.cs:23-26 reads OCR bare numbers or memory addresses for those roles.
- src/4rVivi.Core/Game/StatReader.cs:9-12 still exposes flat HP/MaxHP/SP/MaxSP from `GameSession.ReadRole`.
- src/4rVivi.Core/Game/CharacterState.cs:49-59 still reads MaxHP, MaxSP, HP, and SP into `CharacterState`.
- src/4rVivi.Core/Game/CharacterState.cs:76 uses flat HP/SP deltas in activity detection alongside trusted percent deltas.
- src/4rVivi.App/ViewModels/StatsViewModel.cs:51 reads flat HP/MaxHP/SP/MaxSP and feeds `_session.Observe(hp)` for death tracking, while display text at lines 58-61 correctly uses trusted percent.
- src/4rVivi.App/Services/DiscordPresenceBootstrap.cs:57 still sends flat `Hp`, `MaxHp`, `Sp`, and `MaxSp` into Discord presence. Percent fields at lines 55-56 are trusted.
- src/4rVivi.Core/Automation/SmartBotEngine.cs:678 uses flat SP for skill SP gating.
- src/4rVivi.Core/Automation/SmartBotEngine.cs:846-862 uses flat HP as part of "progress changed" tracking.
- src/4rVivi.App/Services/SmartBotTrainingRecorder.cs:307-311 correctly uses trusted HP/SP percent. Its old flat helper at lines 313-319 appears dead and should be deleted to prevent re-use.
- src/4rVivi.App/ViewModels/MultiClientViewModel.cs:315-321 still parses `HP / MaxHP` and `SP / MaxSP` for row display. Lines 325-334 use percent-text for HP/SP percent rows.

Main-thread changes needed:
- Remove or mark non-safety-only the flat HP/SP properties in `HealthReader`, `StatReader`, and `CharacterState`.
- Replace `StatsViewModel` death tracking on flat HP with a trusted percent-safe signal or remove it until a trusted death signal exists.
- Remove flat HP/SP from Discord presence payload or keep it hidden/diagnostic only; the contract says Discord RPC consumes trusted health.
- Update SmartBot SP gating to consume a trusted `SpPct`-based rule, not flat SP, or make the feature explicitly "unknown SP means do not cast SP-gated skill".
- Delete `SmartBotTrainingRecorder.Percent(...)` at lines 313-319.
- Remove HP/SP flat row parsing from MultiClient if it feeds any downstream state; if display-only, label it legacy/diagnostic.

4. Current safety consumers mostly use trusted percent, with two legacy surfaces to watch.
- SmartBot flee: src/4rVivi.Core/Automation/SmartBotEngine.cs:238 reads `_stat.HpPercent`, line 136 validates `TryGetTrustedNumber`, and lines 252-264 gate teleport on `hpFresh`.
- Autopot: src/4rVivi.Core/Automation/AutopotEngine.cs:31 reads `Session.Health.HpPercent/SpPercent`; these resolve through `HealthReader.TryGetTrustedNumber`.
- AutoYgg: src/4rVivi.Core/Automation/AutoYggEngine.cs:27 reads `Session.Health.HpPercent/SpPercent`.
- BotFarm: src/4rVivi.Core/Automation/BotFarmEngine.cs:28 reads `Session.Health.HpPercent`.
- Top bar: src/4rVivi.App/ViewModels/MainWindowViewModel.cs:119 reads `Session.Health.HpPercent/SpPercent`.
- Stats display: src/4rVivi.App/ViewModels/StatsViewModel.cs:58-61 displays trusted percent.
- Training recorder: src/4rVivi.App/Services/SmartBotTrainingRecorder.cs:307-311 uses trusted percent.
- FourRTools shell and RoTools shell headers currently use trusted reads.

Legacy/non-4rVivi.Core paths needing main decision:
- Core/Fallback.cs:48-132 still implements memory-then-pixel HP percent chains, including `PixelBarSource` and `HpReadChainFactory`.
- Bot/BotEngine.cs:156-211 has `ReadHpPercent` safety with no unknown guard. If the delegate returns `-1`, line 211 treats it as `<= FleeHpPercent` and fires emergency.
- Farm/FarmModules.cs:175 keeps `ReadHpPercent` "from the fallback Chain<int>".
- Features/AdvancedAutopot.cs:22-39 and 73-83 use raw HP/MaxHP/SP/MaxSP memory for pot rules. This is outside the new trusted health model and should be hidden/deleted if compiled into any shipped surface.

5. OCR engine switching/fallback causes found.
- src/4rVivi.Core/Ocr/OcrMark.cs:20 stores a per-field `Engine` override.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:197-198 exposes `WindowsForNumbers` and `Ensemble`.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:309 computes engine per mark: explicit mark engine, otherwise Ensemble, otherwise Windows for numeric fields, otherwise Paddle.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1270 passes that per-mark engine into HP/SP `ReadPercentTextFrom`.
- src/4rVivi.App/Services/OcrService.cs:251-263 accepts the engine and routes the percent reader through `ReadRectBest`.
- src/4rVivi.App/Services/OcrService.cs:1144-1172 implements direct Windows mode and Ensemble mode. Ensemble can switch to Windows when Paddle output is empty or confidence is below 0.85 at lines 1165-1169.
- src/4rVivi.App/Services/OcrService.cs:921-953 falls back to Windows OCR or Tesseract when the Paddle worker is unavailable and sets `EngineWarning`.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1510-1528 displays only `_ocr.LastEngine`, so a single Windows/Tesseract field read can make the UI appear to flicker engines even when most reads are Paddle.

Main-thread changes needed:
- Replace per-mark OCR engine selection with one global engine state.
- HP/SP percent text should not inherit `WindowsForNumbers`; it is text percent, not a generic numeric field.
- If Paddle worker is down, publish HP/SP as suspect/held unless the global contract explicitly allows degraded trusted health. Current fallback can produce `PercentText/Trusted` if parsing succeeds.
- Keep `EngineWarning`, but expose a stable "global OCR engine state" rather than the last region's engine.

6. Vision Assist GRF bypasses YOLO and ByteTrack for the Smart Bot path when enabled.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:274-300 turns `DetectMonsters` off when `VisionAssistGrf` is enabled.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:930 scans entities for Smart Bot when `VisionAssistGrf && SmartBot.Enabled && SmartBot.UseVision`, even with `DetectMonsters=false`.
- src/4rVivi.App/Services/OcrService.cs:772-776 returns immediately from `ScanEntitiesOnly` after `AddVisionAssistFinds` when `VisionAssistGrf` is true. That bypasses the YOLO path below it.
- src/4rVivi.App/Services/OcrService.cs:654-693 builds `ScanFind` entries with `Source="grf"` and `MobId`.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1458-1459 publishes GRF mode through `LiveScene.SetAuthoritativeEntities`.
- src/4rVivi.Core/Game/LiveScene.cs:123-163 clears `_entityTracker` and publishes confirmed authoritative entities, bypassing ByteTrackLite updates.
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs:1462-1463 only calls `ReplaceRawEntityBoxesWithTracks(dets)` when `DetectMonsters` is true, which GRF mode disables. The normal YOLO overlay is therefore not drawn in the GRF-on path.
- src/4rVivi.App/Views/OcrReaderView.axaml:166-167 disables the monster overlay checkbox while Vision Assist is active.

Residual GRF risk:
- `OcrService.NameEntitiesByText` and `NameEntitiesByIcon` remain true/false based on `GrfNamesAbove`, but `ScanEntitiesOnly` returns before the naming block in GRF mode, so the Smart Bot path does not use OCR/icon guessing. Keep this invariant when main refactors entity scan.
- In GRF mode, if manifest load fails, `ScanEntitiesOnly` returns no YOLO fallback because it returns immediately after `AddVisionAssistFinds`. This matches "GRF authoritative" but should show a visible diagnostic.

TESTS TO RUN:
- dotnet build 4rVivi.sln -c Release
- rg -n "LiveStatSource\\.BarFill|ReadBarPercent\\(|ReadBarPercentFrom\\(|role\\.Equals\\(\"HpPercent\"|role\\.Equals\\(\"SpPercent\"|SetNumber\\(m\\.Role, pct\\)" src/4rVivi.App src/4rVivi.Core
- rg -n "TryGetNumber\\(Roles\\.(HpPercent|SpPercent)|TryGetNumber\\(\"HpPercent\"|TryGetNumber\\(\"SpPercent\"" src/4rVivi.App src/4rVivi.Core
- rg -n "HP / MaxHP|SP / MaxSP|ReadCurrentHp|ReadMaxHp|ReadCurrentSp|ReadMaxSp|PixelBarSource|HpReadChainFactory" src Core Bot Farm Features
- Manual run: enable OCR HP % Text/SP % Text, cause one rejected read, and verify DebugTrace shows `decision=hold quality=Held` and no safety consumer fires.
- Manual run: enable Vision Assist GRF with Smart Bot vision on and Detect Monsters off; verify OCR log has `sourceGrf>0 sourceYolo=0 grfBotOnlyScan=True` and overlay does not draw YOLO monster boxes.

EVIDENCE:
- `rg`/`Select-String` audits found the only normal trusted HP/SP writer at `OcrReaderViewModel.cs:1285` using `LiveStatSource.PercentText`.
- `LiveStats.HoldNumber` stores `Held`, and `TryGetTrustedNumber` only accepts `Trusted`.
- `OcrService.ScanEntitiesOnly` returns from the GRF branch before YOLO when `VisionAssistGrf` is true.
- No build or runtime trace was run by this agent; this was an audit-only pass.

RISKS:
- The highest safety risk is the remaining generic bar writer at `OcrReaderViewModel.cs:1256` plus HP/SP-aware bar-fill helpers in `OcrService.cs:1233-1234`.
- The biggest cleanup risk is that flat HP/SP fields are still used for non-safety display/activity/progress. Main should remove or explicitly quarantine them so future safety code cannot reuse them.
- Engine flicker will persist while per-mark engine overrides, WindowsForNumbers, Ensemble, and worker-down fallback can each overwrite `_ocr.LastEngine`.
- Legacy top-level 4RTools files still contain memory/pixel HP safety paths. If they are compiled or reachable, they conflict with CONTRACTS.md.

DO NOT TOUCH:
- CONTRACTS.md
- src/4rVivi.Core/Game/LiveStats.cs
- src/4rVivi.Core/Automation/SmartBotEngine.cs
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
- src/4rVivi.App/Views/OcrReaderView.axaml
- src/4rVivi.Core/Input/*
- VIIPER, FakerInput, ViGEm, reWASD routing

CONTRACT IMPACT: none. This handoff proposes enforcement work to match the existing Health State and Vision Source contracts; it does not propose changing those contracts.
