# Agent 3 Smart Bot Combat/State Handoff

AGENT: 3 Smart Bot combat/state audit

FILES INSPECTED:
- `docs/superpowers/specs/CONTRACTS.md`
- `docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md` sections 1.4, 2.1, 3 Q5/Q8, 8
- `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- `src/4rVivi.Core/Game/LiveScene.cs`
- `src/4rVivi.Core/Game/StatReader.cs`
- `src/4rVivi.Core/Game/FocusGate.cs`
- `src/4rVivi.Core/Input/KeySender.cs`
- `src/4rVivi.Core/Input/MouseSender.cs`
- `src/4rVivi.App/ViewModels/SmartBotViewModel.cs`
- `src/4rVivi.Core/Settings/AppSettings.cs`

FILES CREATED (owned):
- `docs/superpowers/specs/agent-3-smartbot-state-handoff.md`

PROPOSED DIFFS FOR MAIN (shared files):

1. Add explicit Smart Bot state machine and transition logging.

Current state:
- `SmartBotEngine.cs` logs loop branches via `LogLoopDiagnostic(...)`.
- It does not emit the required contract line: `[SmartBotState] old= new= reason=`.
- It does not have the frozen states `WaitingForClientFocus`, `WaitingForTrustedVitals`, `Buffing`, `SelectingTarget`, `EngagingTarget`, `ConfirmingKill`, `Roaming`, `RecoveringStuck`, or paused overlay semantics.

Patch guidance:
```diff
--- a/src/4rVivi.Core/Automation/SmartBotEngine.cs
+++ b/src/4rVivi.Core/Automation/SmartBotEngine.cs
@@
+    private enum SmartBotState
+    {
+        Stopped,
+        WaitingForClientFocus,
+        WaitingForTrustedVitals,
+        Buffing,
+        SelectingTarget,
+        EngagingTarget,
+        ConfirmingKill,
+        Roaming,
+        RecoveringStuck
+    }
+
+    private SmartBotState _state = SmartBotState.Stopped;
+    private bool _paused;
+
+    private void Transition(SmartBotState next, string reason)
+    {
+        if (_state == next && !_paused)
+            return;
+
+        var old = _state;
+        _state = next;
+        DebugTrace.Write("SmartBotState", $"old={old} new={next} reason={reason}");
+    }
```

Then call `Transition(...)` at the actual decision boundaries:
- not enabled: `Stopped`
- not capturable / cannot act: `WaitingForClientFocus`
- HP/SP trusted percent missing for bot safety: `WaitingForTrustedVitals`
- before/while `MaybeRefreshBuffs`: `Buffing`
- before selection only when no held target exists: `SelectingTarget`
- when actively pressing/clicking a held target: `EngagingTarget`
- when held target is missing/lost and kill is being confirmed: `ConfirmingKill`
- walking: `Roaming`
- teleport/stuck recovery: `RecoveringStuck`

2. Hold the active target before selecting a new one.

Current state:
- `LoopAsync` calls `SelectTarget(...)` first (`SmartBotEngine.cs:296-298`).
- `SelectTarget(...)` prefers `_engagedTrackId` only if the held entity is still visible, attackable, and matches (`SmartBotEngine.cs:441-448`).
- If the held target is in `LostGrace`, temporarily unconfirmed, or missing for the first scan, the bot can select another monster before `TrackTargetGone()` is allowed to confirm death (`SmartBotEngine.cs:373-388`).
- This violates the contract: one active target is held through `EngagingTarget -> ConfirmingKill` until death/disappear/timeout/invalid.

Patch guidance:
```diff
@@
-                    var pred = BuildTargetPredicate();
-                    var tgt = SelectTarget(cw / 2, ch / 2, pred);
+                    var pred = BuildTargetPredicate();
+                    var held = TryResolveHeldTarget(pred);
+                    if (held.State == HeldTargetState.ConfirmingGone)
+                    {
+                        Transition(SmartBotState.ConfirmingKill, held.Reason);
+                        if (TrackTargetGone())
+                        {
+                            visionActed = true;
+                            await Timing.DelayAsync(ResolveNextMonsterDelayMs(held.LastVisibleTarget, cw, ch), ct);
+                        }
+                        else
+                        {
+                            visionActed = true;
+                            await Timing.DelayAsync(80, ct);
+                        }
+                        continue;
+                    }
+
+                    var tgt = held.Target ?? SelectTarget(cw / 2, ch / 2, pred);
```

Suggested helper shape:
```csharp
private enum HeldTargetState { None, Visible, ConfirmingGone, Invalid }

private readonly record struct HeldTargetLookup(
    HeldTargetState State,
    SceneItem? Target,
    SceneItem? LastVisibleTarget,
    string Reason);

private HeldTargetLookup TryResolveHeldTarget(Func<SceneItem, bool> match)
{
    if (_engagedTrackId <= 0 && string.IsNullOrWhiteSpace(_engagedTarget))
        return new(HeldTargetState.None, null, null, "none");

    SceneItem? byTrack = null;
    foreach (var e in LiveScene.Instance.Entities)
    {
        if (_engagedTrackId > 0 && e.TrackId == _engagedTrackId)
        {
            byTrack = e;
            break;
        }
    }

    if (byTrack is { } item && item.State == SceneTrackState.Visible && match(item))
        return new(HeldTargetState.Visible, item, item, "held-visible");

    return new(HeldTargetState.ConfirmingGone, null, _lastEngagedSceneItem, byTrack is null ? "held-missing" : $"held-{byTrack.Value.State}");
}
```

Main should add `_lastEngagedSceneItem` or equivalent to keep the last known center/box for next-monster delay and logs.

3. Make kill confirmation consecutive-scan based and prevent kill double-counting.

Current state:
- HP ratio `<= 0.04` immediately calls `FinishEngagedTarget("HP empty")` (`SmartBotEngine.cs:303-307`).
- `TrackTargetGone()` confirms after time since last seen and either damage progress, two casts, or deadline (`SmartBotEngine.cs:540-556`), but it does not require M consecutive missing scans.
- EXP change also increments `Kills` and resets the target (`SmartBotEngine.cs:852-858`), so a kill confirmed by HP/vanish can be counted again on later EXP update.

Patch guidance:
```diff
@@
     private int _castsOnTarget;
+    private int _targetGoneScans;
+    private int _targetLowHpScans;
+    private long _lastKillConfirmedTick;
+    private string _lastKillConfirmedTarget = "";
@@
-                        if (t.HasHp && t.HpRatio <= 0.04f)
+                        if (t.HasHp && t.HpRatio <= 0.04f && ConfirmLowHpScan(t))
                         {
-                            FinishEngagedTarget("HP empty");
+                            Transition(SmartBotState.ConfirmingKill, "hp-empty-consecutive");
+                            FinishEngagedTarget("HP empty confirmed");
```

Suggested constants:
```csharp
private const int KillGoneConfirmScans = 2;
private const int KillLowHpConfirmScans = 2;
private const int KillExpDedupMs = 2500;
```

Suggested helpers:
```csharp
private bool ConfirmLowHpScan(SceneItem item)
{
    if (!item.HasHp || item.HpRatio > 0.04f)
    {
        _targetLowHpScans = 0;
        return false;
    }

    _targetLowHpScans++;
    return _targetLowHpScans >= KillLowHpConfirmScans;
}

private bool IsDuplicateKillSignal()
{
    return Environment.TickCount64 - _lastKillConfirmedTick < KillExpDedupMs
        && string.Equals(_lastKillConfirmedTarget, _engagedTarget, StringComparison.OrdinalIgnoreCase);
}
```

Update `TrackTargetGone()` so a missing target increments `_targetGoneScans`, requires `>= KillGoneConfirmScans`, and only then calls `FinishEngagedTarget(...)`. Reset both counters in `MarkTargetSeen(...)` when the same target is visible, and in `ResetEngagedTarget()`.

Update `FinishEngagedTarget(...)` to stamp `_lastKillConfirmedTick` and `_lastKillConfirmedTarget` before resetting. Update the EXP path in `TrackProgressAndUnstuck(...)` so it records EXP progress as secondary confirmation instead of always incrementing:
```diff
@@
         if (exp > _lastExp && _lastExp >= 0)
         {
-            Kills++;
+            if (IsDuplicateKillSignal())
+            {
+                Log(BotLogKind.Kill, $"EXP confirmed previous kill ({_lastExp} -> {exp})");
+            }
+            else
+            {
+                Kills++;
+                Log(BotLogKind.Kill, $"Kill #{Kills} (EXP {_lastExp} -> {exp})");
+            }
             _lastCombatProgressTick = Environment.TickCount64;
             ResetEngagedTarget();
-            Log(BotLogKind.Kill, $"Kill #{Kills} (EXP {_lastExp} -> {exp})");
         }
```

4. Stabilize GRF target identity.

Current state:
- In `LiveScene.SetAuthoritativeEntities(...)`, authoritative GRF entities get sequential track ids each publish if no track id is supplied.
- `SmartBotEngine.TargetKey(...)` prefers track id whenever `TrackId > 0`.
- If GRF marker order changes across frames, `_engagedTrackId` can identify a different monster even though GRF mode is authoritative.

Patch guidance:
- Prefer a stable identity for GRF mode. Options:
  - Add a stable `EntityId`/`MobId`/`SourceId` to `SceneItem` and use that in `TargetKey(...)`.
  - Or, for authoritative sources without stable ids, hold by last known label plus nearest center within a tolerance before falling back to sequential track id.
- Main should choose the smaller shared-contract change. If `SceneItem` changes, update `docs/CODEX-MAP.md` and user docs in the same pass per repo instruction.

5. Skill press/click ordering is mostly right, but cooldown and UI semantics need tightening.

Current state:
- Correct RO ordering is present: skill hotkey first, short arm delay, then monster click (`SmartBotEngine.cs:344-348`).
- `_skillCdUntil[skillKey] = now + actionDelay` uses `now` captured before `TapAction`, arm delay, click, and post-action delay. This can make cooldown expire early.
- `_skillIdx` advances before readiness is known (`SmartBotEngine.cs:314-321`). A cooldown skill consumes a rotation slot, waits, and then the next loop may advance to another skill without ever casting the waiting skill.
- `ClickAttack` is configured by the viewmodel but `AttackTarget(...)` always clicks. If the UI exposes "No mouse click", main should either remove that promise for Smart Bot targeting or route it intentionally.

Patch guidance:
```diff
@@
-                                    _skillCdUntil[skillKey] = now + Math.Max(80, actionDelay);
+                                    _skillCdUntil[skillKey] = Environment.TickCount64 + Math.Max(80, actionDelay);
```

Suggested selection helper:
```csharp
private string NextReadySkill(long now)
{
    if (SkillRotation.Count == 0)
        return "";

    for (int i = 0; i < SkillRotation.Count; i++)
    {
        var key = SkillRotation[(_skillIdx + i) % SkillRotation.Count];
        if (string.IsNullOrWhiteSpace(key))
            continue;
        if (_skillCdUntil.TryGetValue(key, out var until) && now < until)
            continue;
        _skillIdx = (_skillIdx + i + 1) % SkillRotation.Count;
        return key;
    }

    return "";
}
```

If no skill is ready, normal attack the held target or wait a bounded short delay. Do not pick a new target solely because a skill is on cooldown.

6. Convert `StuckSeconds` and `FocusKillSeconds` to milliseconds with one-time migration.

Current state:
- `SmartBotEngine.StuckSeconds` and `FocusKillSeconds` are seconds (`SmartBotEngine.cs:22-23`, `SmartBotEngine.cs:623-626`, `SmartBotEngine.cs:868-875`, `SmartBotEngine.cs:1081-1086`).
- `NextMonsterDelayMs` is already ms and supports `-1 = auto`.
- Master-plan section 8.1 requires `StuckMs` and `FocusKillMs`, preserving `-1 = Auto`, and multiplying old saved profile seconds exactly once.

Patch guidance for main:
- In `SmartBotEngine.cs`: rename properties to `StuckMs` and `FocusKillMs`.
- In `SmartBotViewModel.cs`: rename `_stuckSeconds`/`OnStuckSecondsChanged` and `_focusKillSeconds`/`OnFocusKillSecondsChanged` to ms equivalents.
- In `SmartBotConfig`: add `StuckMs`, `FocusKillMs`, and migration flag, for example `TimingUnitsMigratedToMs`.
- On load:
```csharp
if (!c.TimingUnitsMigratedToMs)
{
    c.StuckMs = Math.Max(2000, c.StuckSeconds * 1000);
    c.FocusKillMs = c.FocusKillSeconds < 0 ? -1 : Math.Clamp(c.FocusKillSeconds, 1, 600) * 1000;
    c.TimingUnitsMigratedToMs = true;
}
```
- Keep obsolete fields temporarily for profile migration only. Do not silently reinterpret saved value `8` as `8 ms`.

7. Preserve and expose one work-area source for Agent 4 overlay.

Current state:
- Smart Bot has only walk/roam box properties (`BoxX`, `BoxY`, `BoxW`, `BoxH`).
- There is no combat/scan region model for the requested "Show work area" overlay.

Patch guidance:
- Add a small model owned by Smart Bot state, e.g. `SmartBotWorkArea` with `CombatRect` and `RoamRect`.
- The bot selection and roam code should read this model.
- Agent 4 overlay should bind to the same model. Do not duplicate region math in UI.
- Emit the required debug line when values change or when bot starts:
```text
[WorkArea] combat=x,y,wxh roam=x,y,wxh
```

FINDINGS:

1. Contract mismatch: no frozen Smart Bot state machine.
- Evidence: `SmartBotEngine.cs` has branch diagnostics (`LogLoopDiagnostic`) but no `SmartBotState` enum or `[SmartBotState] old= new= reason=` transition log.
- Risk: Overnight reviewers cannot tell whether the bot is waiting for vitals, selecting, engaging, confirming, roaming, or recovering. Bugs like "clicks around but doesn't kill" remain hard to prove.

2. Target hold is partial, not contract-complete.
- Evidence: `SelectTarget(...)` prefers `_engagedTrackId`, but only while it still matches `IsAttackable` and the monster predicate. Lost/missing held targets do not block selection of a new target.
- Risk: Bot can abandon a nearly-dead target or switch targets before kill confirmation, especially during tracker grace, low confidence, GRF order changes, or HP-bar flicker.

3. Kill confirmation is too eager and may double-count.
- Evidence: HP ratio `<= 0.04` immediately counts a kill; vanish confirmation does not require consecutive scans; EXP change independently increments `Kills`.
- Risk: False kill count, premature loot key, selecting next monster while current target is still alive, and misleading stats.

4. Skill ordering is correct in the happy path, but rotation/cooldown can stall or skip.
- Evidence: hotkey then click order exists. However, cooldown uses an old timestamp and `_skillIdx` advances before readiness.
- Risk: A configured skill can be skipped while the bot waits, or cooldowns can expire early enough to spam faster than intended.

5. Timing units still violate section 8.1.
- Evidence: `StuckSeconds` and `FocusKillSeconds` still exist in engine, config, and viewmodel; only `NextMonsterDelayMs` is already ms.
- Risk: UI and persisted profiles mix seconds and milliseconds; changing labels without migration would make existing profiles dangerously fast.

6. `NextMonsterDelayMs = -1` auto formula is reasonable but loses context after vanish.
- Evidence: HP-empty path passes the target into `ResolveNextMonsterDelayMs(...)`, but vanish path passes `null`.
- Risk: Auto delay after a vanished target ignores distance/target geometry and can select too quickly after edge-of-screen kills.

7. Smart Bot uses trusted HP percent for flee, which aligns with the health contract.
- Evidence: `StatReader.HpPercent` and `SpPercent` use `TryGetTrustedNumber`; `IsHpFresh(...)` also calls `TryGetTrustedNumber`.
- Residual risk: non-flee decisions still read bare Exp/Weight/Ammo/Position, which is acceptable for non-HP/SP safety but should remain clearly separated.

TESTS TO RUN:
- `dotnet test tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj -c Release`
- `dotnet build 4rVivi.sln -c Release`
- Add targeted tests if main applies code:
  - Target hold: held target LostGrace/missing for one scan does not select a new target.
  - Kill confirm: one missing scan does not count kill; two consecutive missing scans after damage/casts does.
  - Kill de-dupe: EXP increase after `FinishEngagedTarget(...)` logs confirmation but does not increment `Kills` twice.
  - Timing migration: old `StuckSeconds=8`, `FocusKillSeconds=3` load as `StuckMs=8000`, `FocusKillMs=3000`, with a migration flag preventing double conversion.
  - Manual timing: `FocusKillMs=-1` remains auto; manual ms values clamp to intended ranges.

EVIDENCE:
- Audit-only: no runtime/shared code was edited by Agent 3.
- The requested handoff file was created.
- Relevant source evidence:
  - `SmartBotEngine.cs:296-298` selects a target before kill-confirming held target disappearance.
  - `SmartBotEngine.cs:303-307` counts HP-empty immediately.
  - `SmartBotEngine.cs:540-556` confirms vanish without consecutive-scan counter.
  - `SmartBotEngine.cs:852-858` increments kills again on EXP increase.
  - `SmartBotEngine.cs:623-626` treats `FocusKillSeconds` as seconds.
  - `SmartBotEngine.cs:868-875` and `1081-1086` treat `StuckSeconds` as seconds.
  - `SmartBotViewModel.cs:790-792`, `1641-1643`, and `2493-2495` persist seconds for stuck/focus and ms for next monster.

RISKS:
- Adding a state machine inside the current large engine can increase complexity unless transition calls replace branch-only reasoning cleanly.
- Changing `SceneItem` identity for GRF mode may touch OCR publishing, overlay drawing, and tests; if main wants lower risk tonight, hold by label+nearest-center without altering `SceneItem` first.
- Migration must be one-time and profile-safe. Do not rename fields without preserving old-profile load.
- Kill de-dupe must not hide legitimate multi-kill EXP jumps from area damage. Use a short de-dupe window tied to the active target only.

DO NOT TOUCH:
- Agent 3 did not edit `src/4rVivi.Core/Automation/SmartBotEngine.cs`.
- Agent 3 did not edit `src/4rVivi.App/ViewModels/SmartBotViewModel.cs`.
- Agent 3 did not edit shared runtime, input, OCR, GRF, VIIPER, FakerInput, ViGEm, or reWASD routing files.

CONTRACT IMPACT:
- none from this handoff file.
- Proposed code changes implement the existing `CONTRACTS.md` Smart Bot state machine and master-plan section 8 timing requirements; they do not propose changing the frozen contract.
