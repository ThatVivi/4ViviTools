# Agent 2 Task 3 - OCR Health Tests and Engine Stability Seams

Date: 2026-07-13
Agent: Agent 2 / Plato scope
Mode: test additions in disjoint test file plus audit/proposed seams

AGENT: 2 OCR/HP-SP

FILES INSPECTED:
- docs/CODEX-MAP.md
- docs/USER_GUIDE.md
- docs/PROJECT_IMPROVEMENT_PLAN.md
- docs/superpowers/specs/CONTRACTS.md
- docs/superpowers/specs/RUN-LOG-2026-07-13.md
- docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md
- docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-reply.md
- docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-ocr-question.md
- docs/superpowers/specs/2026-07-13-claude-hard-attach-reply.md
- docs/superpowers/specs/2026-07-13-hard-attached-ocr-status-and-questions.md
- docs/superpowers/specs/2026-07-13-codex-full-tool-status-for-claude.md
- docs/superpowers/specs/2026-07-13-claude-second-opinion-reply.md
- docs/superpowers/specs/2026-07-13-claude-second-opinion-reply-2.md
- docs/superpowers/specs/agent-2-ocr-health-handoff.md
- docs/superpowers/specs/agent-2-task2-flat-health-engine-cleanup.md
- docs/rathena/ocr.md
- docs/rathena/ocr-data-refresh.md
- docs/rathena/hp-sp.md
- docs/rathena/skills.md
- docs/rathena/monsters.md
- docs/rathena/client-systems.md
- docs/rathena/client-grf-data.md
- docs/ocr-roadmap/README.md
- docs/ocr-roadmap/SESSION-PROGRESS.md
- docs/ocr-roadmap/guide/section-8-priority.md
- tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj
- tests/4rVivi.Core.Tests/HealthPercentSafetyTests.cs
- tests/4rVivi.Core.Tests/EngineLogicTests.cs
- tests/4rVivi.Core.Tests/FeatureTests.cs
- src/4rVivi.Core/Game/LiveStats.cs
- src/4rVivi.Core/Game/HealthReader.cs
- src/4rVivi.Core/Game/StatReader.cs
- src/4rVivi.Core/Game/Roles.cs
- src/4rVivi.App/Services/OcrService.cs
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
- src/4rVivi.App/Services/WindowsOcrEngine.cs
- src/4rVivi.App/Services/RapidOcrClient.cs

FILES CREATED (owned):
- tests/4rVivi.Core.Tests/OcrHealthSafetyContractTests.cs
- docs/superpowers/specs/agent-2-task3-ocr-health-tests.md

PROPOSED DIFFS FOR MAIN (shared files):
None applied. Proposed seams and test targets are listed below for main-thread runtime work.

## Local Canon and Research Findings

`CONTRACTS.md` is the current contract for this task: HP/SP health is percent state with `Quality`, `Source`, age, raw text, and confidence; HP/SP `BarFill` is dead for safety health. Some intermediate 2026-07-13 docs mention HP/SP bar markers again, but this handoff follows the frozen contract plus Task 2.

The local OCR roadmap explains why engine churn exists: per-field Windows OCR and Ensemble were added as accuracy features. For HP/SP safety, those features need a policy layer so they cannot silently become trusted health.

Legitimate RO/rAthena research used:

- [iRO Wiki Basic Game Control](https://irowiki.org/wiki/Basic_Game_Control) confirms the Basic Info window is the canonical player info surface and includes HP/SP, EXP, weight, and Zeny.
- [Official PlayRagnarok keyboard shortcuts](https://www.playragnarok.com/gameguide/howtoplay_interface03.aspx) confirms Basic Info is a client UI window and F1-F9 are the normal item/skill shortcut keys.
- [rAthena job_basepoints.yml](https://github.com/rathena/rathena/blob/master/db/re/job_basepoints.yml) documents base HP/SP tables, so static HP/SP capacity belongs to data/calculator logic, not live safety.
- [rAthena mob_db.yml](https://github.com/rathena/rathena/blob/master/db/re/mob_db.yml) carries monster `Hp`, `BaseExp`, and `JobExp`; useful for target/TTK tests, not player HP safety.
- [rAthena skill_db.yml](https://github.com/rathena/rathena/blob/master/db/re/skill_db.yml) is the source for skill metadata such as SP cost and timing; useful for skill/resource behavior seams.
- [rAthena map cache documentation](https://github.com/rathena/rathena/blob/master/doc/map_cache.txt) confirms map lists/cache are data inputs; useful for map-mob focus and OCR dictionaries.

Test implication: player HP/SP safety tests should stay centered on live OCR health metadata and trusted reads. rAthena data belongs in separate tests for skill SP cost, skill delay, monster HP, and map-mob focus.

## Tests Added

New file: `tests/4rVivi.Core.Tests/OcrHealthSafetyContractTests.cs`

1. `Vital_health_roles_never_return_bar_fill_percent`
   - Lines 10-23.
   - Calls `OcrService.ReadBarPercentFrom(...)` on a synthetic half-filled bar.
   - Asserts `Roles.HpPercent`, `Roles.SpPercent`, `"HP Bar"`, and `"SP Bar"` return `-1`.
   - This proves the current service-level bar-fill guard rejects vital health roles.

2. `Non_vital_bar_fill_still_reads_the_fixture`
   - Lines 25-34.
   - Uses the same synthetic bar and asserts `BaseExpBar` reads around 50%.
   - This validates the fixture and keeps non-vital bar-fill behavior from being accidentally disabled by the HP/SP guard.

3. `Held_percent_remains_visible_but_not_trusted`
   - Lines 36-56.
   - Publishes a trusted `100%`, then holds a failed `2` read as `LiveStatQuality.Held`.
   - Asserts raw visible value is still present via `TryGetNumber`, metadata is `Held`, `TryGetTrustedNumber` rejects it, and `HealthReader`/`StatReader` return `-1`.
   - This covers "held percent does not trigger safety" at the shared reader boundary.

Existing coverage still relevant:

- `tests/4rVivi.Core.Tests/HealthPercentSafetyTests.cs` lines 9-34 already cover suspect vs trusted health.
- `HealthPercentSafetyTests.cs` lines 36-55 cover safe/unsafe percent text parsing, including rejecting bare `"2"`.

Verification run:

```powershell
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --nologo
```

Result:

```text
Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74
```

## Current Exact Source Anchors

`tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj`

- Lines 13-15 reference both Core and App, so tests can safely call exposed `OcrService` APIs without new runtime edits.

`src/4rVivi.Core/Game/LiveStats.cs`

- Lines 3-8 define `Trusted`, `Held`, `Suspect`.
- Lines 10-18 define `LiveStatSource`; line 14 says `BarFill` is non-vital only.
- Lines 48-66 write metadata and held numbers.
- Lines 87-107 only return trusted numbers when quality is `Trusted` and age is within the max age.

`src/4rVivi.App/Services/OcrService.cs`

- Lines 180-181 still have stale public comment text saying HP/SP/EXP bar.
- Lines 218-248 parse percent text and reject low bare single-digit OCR.
- Lines 251-266 read percent text but accept an engine string and return an int only.
- Lines 1154-1170 can route explicit `Windows` or `Ensemble`; Ensemble can choose Windows when Paddle is empty or low confidence.
- Lines 1207-1215 expose `ReadBarPercentFrom`.
- Lines 1219-1227 reject vital roles before non-vital bar fill.
- Lines 1238-1242 define vital roles as `HpPercent`, `SpPercent`, `HP Bar`, and `SP Bar`.
- Lines 1244 onward still contain commented legacy vital color bar code.

`src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`

- Line 296 computes `EngineFor(m)` from per-mark override, `Ensemble`, and `WindowsForNumbers`.
- Lines 1237-1241 convert stale HP/SP bar marks to percent-text readers before generic bar-fill.
- Lines 1242-1251 still publish generic bar-fill values through bare `LiveStats.SetNumber(m.Role, pct)` for non-vital bars.
- Lines 1253-1285 read HP/SP percent text, publish `PercentText/Trusted`, and hold failed reads as `Held`.
- Lines 1496-1510 display `_ocr.LastEngine`, so any per-region read can change the global-looking engine display.

## Test Objectives and Status

### HP/SP Bar-Fill Never Trusted

Status: partially added and passing.

Added coverage proves `OcrService.ReadBarPercentFrom` rejects HP/SP vital roles. It does not yet prove there is no future call site that writes `LiveStats.SetNumber(Roles.HpPercent, pct)` through the bare compatibility setter.

Recommended main follow-up test after cleanup:

```csharp
[Theory]
[InlineData(Roles.HpPercent)]
[InlineData(Roles.SpPercent)]
public void Bare_set_number_cannot_create_trusted_vital_health(string role)
{
    LiveStats.Instance.Clear();
    LiveStats.Instance.Active = true;

    LiveStats.Instance.SetNumber(role, 2);

    Assert.False(LiveStats.Instance.TryGetTrustedNumber(role, out _));
}
```

Blocker: this requires a shared runtime change to either deprecate the bare setter for vital roles or add a dedicated `SetTrustedHealthPercent(...)` API. Do not add the test before main is ready to break/fix legacy callers.

### Held Percent Does Not Trigger Safety

Status: added and passing at the shared reader boundary.

Current test proves held HP is visible for display but not trusted, and the public `HealthReader`/`StatReader` percent accessors return `-1`.

Recommended main follow-up tests:

- `AutopotEngine_does_not_fire_on_held_hp_percent`
- `AutoYggEngine_does_not_fire_on_held_hp_percent`
- `SmartBotEngine_does_not_enter_flee_on_held_hp_percent`

Blocker: those engines currently require loop timing and input dependencies. A clean seam would expose pure decision helpers such as:

```csharp
public static bool ShouldUsePotion(int trustedPct, int threshold);
public static bool ShouldFlee(int trustedHpPct, int threshold, bool confirmedLow);
```

Then tests can assert `trustedPct=-1` does not fire without driving background loops or input senders.

### Windows OCR Fallback Cannot Publish Trusted HP/SP Unless Policy Allows It

Status: proposed only.

Current blocker:

- `OcrService.ReadPercentTextFrom(...)` returns an `int` and `usedEngine`, but trust is assigned later by `OcrReaderViewModel` lines 1273-1285.
- There is no global engine policy object and no pure method that answers whether `usedEngine="Windows OCR"` is allowed for HP/SP.
- `ReadRectFrom` lines 1154-1170 can use Windows or Ensemble paths; `Recognize` can also degrade when the worker is down.

Proposed seam:

```csharp
public enum OcrEngineMode { Paddle, Windows, Ensemble }
public enum OcrHealthTrustDecision { Trusted, Held, Suspect }

public sealed record OcrEnginePolicy(
    OcrEngineMode GlobalMode,
    bool AllowFallbackForTrustedVitals);

public static class OcrHealthTrustPolicy
{
    public static OcrHealthTrustDecision DecideVitalPercent(
        string role,
        int parsedPercent,
        double confidence,
        string requestedEngine,
        string usedEngine,
        OcrEnginePolicy policy)
    {
        if (parsedPercent < 0) return OcrHealthTrustDecision.Suspect;
        if ((role == Roles.HpPercent || role == Roles.SpPercent) &&
            IsFallbackEngine(usedEngine) &&
            !policy.AllowFallbackForTrustedVitals)
            return OcrHealthTrustDecision.Held;
        return OcrHealthTrustDecision.Trusted;
    }
}
```

Exact tests to add after seam:

```csharp
[Theory]
[InlineData(Roles.HpPercent)]
[InlineData(Roles.SpPercent)]
public void Windows_fallback_vital_percent_is_not_trusted_by_default(string role)
{
    var decision = OcrHealthTrustPolicy.DecideVitalPercent(
        role, 24, 0.92, "Paddle", "Windows OCR",
        new OcrEnginePolicy(OcrEngineMode.Paddle, AllowFallbackForTrustedVitals: false));

    Assert.Equal(OcrHealthTrustDecision.Held, decision);
}

[Fact]
public void Windows_fallback_vital_percent_can_be_trusted_only_when_policy_allows_it()
{
    var decision = OcrHealthTrustPolicy.DecideVitalPercent(
        Roles.HpPercent, 24, 0.92, "Paddle", "Windows OCR",
        new OcrEnginePolicy(OcrEngineMode.Paddle, AllowFallbackForTrustedVitals: true));

    Assert.Equal(OcrHealthTrustDecision.Trusted, decision);
}
```

Main wiring target:

- Call the policy before `OcrReaderViewModel` line 1277 publishes `LiveStatQuality.Trusted`.
- If policy returns held/suspect, call `HoldNumber` or do not publish.

### Engine Display Should Not Flicker Per Mark

Status: proposed only.

Current blocker:

- `OcrReaderViewModel` line 1496 reads `_ocr.LastEngine`.
- `EngineInfo` line 1507 displays that last per-region engine as if it were the stable engine state.
- `OcrService.LastEngine` changes on every read path, including Windows/Ensemble branches.
- `EngineFor(m)` is private and tied to ViewModel state, so it is not unit-testable without constructing the Avalonia VM.

Proposed seam:

```csharp
public sealed record OcrEngineStatus(
    OcrEngineMode ConfiguredMode,
    string RuntimeProvider,
    string LastRegionEngine,
    string Warning);

public static class OcrEngineStatusFormatter
{
    public static string Format(OcrEngineStatus status)
    {
        var configured = status.ConfiguredMode switch
        {
            OcrEngineMode.Paddle => "PaddleOCR",
            OcrEngineMode.Windows => "Windows OCR",
            OcrEngineMode.Ensemble => "Ensemble",
            _ => "PaddleOCR"
        };

        return "engine: " + configured
            + (string.IsNullOrWhiteSpace(status.RuntimeProvider) || status.RuntimeProvider == "unknown" ? "" : $" runtime: {status.RuntimeProvider}")
            + (string.IsNullOrWhiteSpace(status.Warning) ? "" : " warning: " + status.Warning);
    }
}
```

Exact tests to add after seam:

```csharp
[Fact]
public void Engine_display_uses_configured_mode_not_last_region_engine()
{
    var status = new OcrEngineStatus(
        OcrEngineMode.Paddle,
        "CUDA",
        LastRegionEngine: "Windows OCR",
        Warning: "");

    var text = OcrEngineStatusFormatter.Format(status);

    Assert.Contains("engine: PaddleOCR", text);
    Assert.DoesNotContain("engine: Windows OCR", text);
}

[Fact]
public void Engine_display_keeps_warning_without_relabeling_global_engine()
{
    var status = new OcrEngineStatus(
        OcrEngineMode.Paddle,
        "CUDA",
        LastRegionEngine: "Windows OCR",
        Warning: "Paddle worker unavailable; fallback used for non-vital OCR.");

    var text = OcrEngineStatusFormatter.Format(status);

    Assert.Contains("engine: PaddleOCR", text);
    Assert.Contains("warning:", text);
}
```

Main wiring target:

- Replace the global-looking `EngineInfo = "engine: " + _ocr.LastEngine` at `OcrReaderViewModel.cs:1507`.
- Surface last per-mark/last fallback engine only in diagnostics, not the primary engine status.

## Additional Proposed Test File After Main Adds Seams

Suggested file:

```text
tests/4rVivi.Core.Tests/OcrEnginePolicyTests.cs
```

Suggested tests:

- `EngineFor_vital_percent_ignores_WindowsForNumbers`
- `EngineFor_vital_percent_ignores_per_mark_Windows_override_unless_diagnostic_mode`
- `Windows_fallback_vital_percent_is_not_trusted_by_default`
- `Fallback_vital_percent_can_be_trusted_only_when_policy_allows_it`
- `Engine_display_uses_configured_mode_not_last_region_engine`
- `Engine_display_reports_warning_without_flickering_configured_engine`

Preferred implementation: put the pure engine-policy and display formatter in `src/4rVivi.Core/Ocr` so tests do not need Avalonia, dispatcher, OCR workers, or bitmap capture.

## Findings

1. Two safe tests were added now because they compile and run without touching runtime code.
2. `ReadBarPercentFrom` currently rejects HP/SP vital roles; this is now covered by a regression test.
3. Held HP remains visible through `TryGetNumber`, but cannot satisfy `TryGetTrustedNumber`, `HealthReader.HpPercent`, or `StatReader.HpPercent`; this is now covered.
4. Engine fallback trust cannot be tested cleanly yet because trust assignment is split across `OcrService` and `OcrReaderViewModel` with no policy seam.
5. Engine display flicker cannot be tested cleanly yet because the primary status uses mutable `_ocr.LastEngine`, which is a last-region fact, not a configured/global engine fact.
6. `OcrService` still has stale public comment text at lines 180-181 and a commented legacy vital bar block from line 1244 onward. Keep the Task 2 cleanup item to delete/quarantine it.
7. Local docs and external RO/rAthena references support separating live UI state (OCR HP/SP percent) from static gameplay data (monster HP, skill SP/timing, map mobs).

## Tests To Run

Already run:

```powershell
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --nologo
```

Result:

```text
Failed: 0, Passed: 74, Skipped: 0, Total: 74
```

Recommended after main adds seams:

```powershell
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --filter "OcrHealthSafetyContractTests|OcrEnginePolicyTests" --nologo
dotnet build 4rVivi.sln -c Release --nologo
```

## Risks

- The new HP/SP bar-fill test protects the service-level guard, but a future bare `LiveStats.SetNumber(Roles.HpPercent, value)` call could still create trusted vital health until main hardens the setter or introduces a dedicated health writer.
- Engine policy tests should not instantiate `OcrReaderViewModel` or real OCR workers. That path would make tests flaky and slow.
- The existing test project references App, which made these tests possible, but long-term pure health/engine policy should live in Core to keep tests cheap.
- Local docs contain older/stale statements that HP/SP bars are read by fill. `CONTRACTS.md` and this handoff treat that as superseded for safety health.

DO NOT TOUCH:
- Runtime shared files from this agent pass unless main explicitly takes the proposed seams.
- Input routing/backend files.
- Frozen contracts except by main.

CONTRACT IMPACT:
None. This task adds tests and proposes seams to enforce the existing Health State contract. It does not change the contract.
