# Agent 3 Task 2: Minimal Smart Bot State Machine + GRF Identity

AGENT: 3 / Epicurus - Smart Bot Combat

SCOPE:
- Audit/proposed diffs only.
- Do not edit `src/4rVivi.Core/Automation/SmartBotEngine.cs`.
- Do not revert unrelated dirty workspace changes.

OBJECTIVE:
- Design the minimal `SmartBotEngine` patch that emits `[SmartBotState] old= new= reason=` without over-refactoring the combat loop.
- Audit GRF target identity stability and propose the smallest safe fix.

FILES INSPECTED:
- `docs/superpowers/specs/agent-3-smartbot-state-handoff.md`
- `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- `src/4rVivi.Core/Game/LiveScene.cs`
- `src/4rVivi.App/Services/OcrService.cs`
- `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`
- `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs`

FILES CREATED:
- `docs/superpowers/specs/agent-3-task2-state-machine-grf-identity.md`

## Current Evidence

`SmartBotEngine.cs` already contains partial follow-up work from the prior handoff:
- Low-HP kill confirmation now requires `ConfirmLowHpScan(t)` at `SmartBotEngine.cs:307`.
- Vanish confirmation has `_targetGoneScans` and requires two scans at `SmartBotEngine.cs:564-568`.
- Skill cooldown stamping now uses `Environment.TickCount64` after the skill/click at `SmartBotEngine.cs:352`.

Still missing:
- No `SmartBotState` enum.
- No `[SmartBotState] old= new= reason=` log.
- Current loop branch logs (`LogLoopDiagnostic`) are useful diagnostics but do not satisfy the frozen state-machine contract.

## Minimal State Patch For Main

### 1. Insert enum + field after current target fields

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: after `_targetLowHpScans` field and before `_gameDb`.
- Current nearby lines: `_targetGoneScans`, `_targetLowHpScans`, then `_gameDb`.

Proposed diff:
```diff
@@
     private bool _targetHadDamageProgress;
     private int _targetGoneScans;
     private int _targetLowHpScans;
+    private SmartBotState _smartBotState = SmartBotState.Stopped;
     private static readonly Lazy<GameDatabase> _gameDb = new(() => new GameDatabase());
+
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
```

Why here:
- Keeps state machine private to `SmartBotEngine`.
- Avoids a new file or public contract.
- Does not change existing engine surface area.

### 2. Insert `Transition` helper after `Log(...)`

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: immediately after `private void Log(BotLogKind kind, string text)`.

Proposed diff:
```diff
@@
     private void Log(BotLogKind kind, string text) { Report(text); BotLog.Instance.Add(kind, text); }
+
+    private void Transition(SmartBotState next, string reason)
+    {
+        if (_smartBotState == next)
+            return;
+
+        var old = _smartBotState;
+        _smartBotState = next;
+        DebugTrace.Write("SmartBotState", $"old={old} new={next} reason={reason}");
+    }
```

Reason format:
- Use short kebab-case reasons.
- Do not include noisy coordinates or samples here; those already live in `LogLoopDiagnostic` and `LogVisionDecision`.

### 3. Add top-of-loop guards without flattening the loop

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: inside `while (!ct.IsCancellationRequested)`, before `if (Enabled && (Session.Reader.Attached || LiveStats.Instance.IsFresh))`.

Proposed diff:
```diff
@@
         while (!ct.IsCancellationRequested)
         {
+            if (!Enabled)
+            {
+                Transition(SmartBotState.Stopped, "disabled");
+                await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
+                continue;
+            }
+
+            if (!Session.Reader.Attached && !LiveStats.Instance.IsFresh)
+            {
+                Transition(SmartBotState.WaitingForClientFocus, "not-attached-no-live-stats");
+                await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
+                continue;
+            }
+
             if (Enabled && (Session.Reader.Attached || LiveStats.Instance.IsFresh))
             {
```

Why this is minimal:
- It preserves the existing large `if` block.
- The remaining `if (Enabled && ...)` becomes redundant but harmless; main can flatten later if desired.
- It logs the two states that previously only fell through to idle delay silently.

### 4. Log `Buffing` only when buff work may happen

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: immediately before `await MaybeRefreshBuffs(ct);`.

Proposed diff:
```diff
@@
-                await MaybeRefreshBuffs(ct);
+                if (BuffKeys.Count > 0)
+                    Transition(SmartBotState.Buffing, "buff-refresh-check");
+                await MaybeRefreshBuffs(ct);
```

Reason:
- `MaybeRefreshBuffs` currently returns `Task`, not a "did work" bool.
- This avoids changing its signature.
- It may log `Buffing` even if all buffs are still on interval, but only when the user configured buffs.

### 5. Block on missing trusted HP when flee safety is enabled

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: after `hpFresh` and client size are computed.

Proposed diff:
```diff
@@
                 double wt = _stat.WeightPercent;
                 bool hpFresh = IsHpFresh(hp);
                 var (cw, ch) = ResolveClientSize();
+
+                if (FleeAtHpPercent > 0 && !hpFresh)
+                {
+                    Transition(SmartBotState.WaitingForTrustedVitals, "trusted-hp-missing");
+                    LogLoopDiagnostic("trusted-vitals", cw, ch, hp, hpFresh, wt);
+                    await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
+                    continue;
+                }
```

Reason:
- Smart Bot is a safety consumer. If flee is configured, attacking while HP is untrusted means flee cannot be trusted.
- This keeps the change narrow: it does not redesign `StatReader` or SP logic.

### 6. Log client-size wait and HP flee recovery

Insertion points:
- `cw <= 0 || ch <= 0` branch.
- HP flee branch before `TapAction(TeleportKey, 20)`.

Proposed diff:
```diff
@@
                 if (ShouldFleeForHp(hp, hpFresh))
                 {
+                    Transition(SmartBotState.RecoveringStuck, "hp-flee-teleport");
                     LogLoopDiagnostic("flee", cw, ch, hp, hpFresh, wt);
                     Log(BotLogKind.Movement, $"HP {hp:0}% <= flee {FleeAtHpPercent}% - teleporting with {TeleportKey} before next target.");
@@
                 if (cw <= 0 || ch <= 0)
                 {
+                    Transition(SmartBotState.WaitingForClientFocus, "client-size-invalid");
                     LogLoopDiagnostic("client-size", cw, ch, hp, hpFresh, wt);
```

### 7. Log selection, engagement, and kill confirmation in the existing vision block

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: inside the `UseVision && ...` block around `BuildTargetPredicate`, `SelectTarget`, target action, and no-target branch.

Proposed diff:
```diff
@@
                 {
                     var pred = BuildTargetPredicate();
+                    if (_engagedTrackId <= 0 && string.IsNullOrWhiteSpace(_engagedTarget))
+                        Transition(SmartBotState.SelectingTarget, "vision-fresh-select");
                     var tgt = SelectTarget(cw / 2, ch / 2, pred);
                     if (tgt is { } t)
                     {
@@
                         LogLoopDiagnostic("target", cw, ch, hp, hpFresh, wt);
                         LogVisionDecision("target", cw, ch, t);
                         MarkTargetSeen(t);
                         if (t.HasHp && t.HpRatio <= 0.04f && ConfirmLowHpScan(t))
                         {
+                            Transition(SmartBotState.ConfirmingKill, "hp-empty-confirmed");
                             FinishEngagedTarget("HP empty confirmed");
@@
                         else
                         {
+                            Transition(SmartBotState.EngagingTarget, _castsOnTarget <= 0 ? "target-acquired" : "target-held");
                             int tx = Math.Clamp(t.Cx, 4, cw - 4), ty = Math.Clamp(t.Cy, 4, ch - 4);
@@
                     else
                     {
                         LogLoopDiagnostic("no-target", cw, ch, hp, hpFresh, wt);
                         LogVisionDecision("no-target", cw, ch, null);
                         bool hadEngagedTarget = _engagedTrackId > 0;
+                        if (hadEngagedTarget || !string.IsNullOrWhiteSpace(_engagedTarget))
+                            Transition(SmartBotState.ConfirmingKill, "held-target-not-visible");
+                        else
+                            Transition(SmartBotState.SelectingTarget, "no-target-visible");
                         bool finished = TrackTargetGone();
```

Notes:
- This does not yet solve the target-hold-before-reselect issue from Task 1. It only logs the current state accurately.
- Main should still apply the target-hold patch separately if the bot keeps switching targets.
- The reason `target-acquired` can repeat as `EngagingTarget` only once because `Transition` suppresses same-state logs.

### 8. Log stale scene and roam

Insertion points:
- `else if (UseVision)` stale/coords branch.
- `if (!visionActed && ClickToMove...)` roam branch.

Proposed diff:
```diff
@@
                 else if (UseVision)
                 {
+                    Transition(SmartBotState.SelectingTarget, LiveScene.Instance.EntitiesFresh ? "scene-not-client-coords" : "scene-stale");
                     LogLoopDiagnostic(LiveScene.Instance.EntitiesFresh ? "scene-coords" : "scene-stale", cw, ch, hp, hpFresh, wt);
                 }
 
                 if (!visionActed && ClickToMove && cw > 0 && ch > 0)
                 {
+                    Transition(SmartBotState.Roaming, "no-target-walk");
                     LogLoopDiagnostic("roam", cw, ch, hp, hpFresh, wt);
```

### 9. Log recovery inside existing timeout helpers

Insertion points:
- `UpdateTargetKillDeadline(...)` before teleport.
- `MaybeCombatUnstuck()` before teleport.
- `MaybeUnstuck(...)` before teleport.

Proposed diff:
```diff
@@
         if (now > _targetKillDeadlineTick && now - _lastCombatProgressTick > Math.Max(900, actionDelayMs * 2L))
         {
+            Transition(SmartBotState.RecoveringStuck, "target-kill-timeout");
             Log(BotLogKind.Movement, $"Target exceeded expected kill time ({expected / 1000.0:0.0}s) without HP/EXP progress - teleporting.");
@@
         if (string.IsNullOrWhiteSpace(_engagedTarget)) return;
         if (Environment.TickCount64 - _lastCombatProgressTick < Math.Max(2, StuckSeconds) * 1000L) return;
+        Transition(SmartBotState.RecoveringStuck, "combat-timeout");
         var focus = _expectedKillMs > 0 ? $"{_expectedKillMs / 1000.0:0.0}s expected" : "auto expected";
@@
         if (Environment.TickCount64 - _lastChangeTick < StuckSeconds * 1000L) return;
+        Transition(SmartBotState.RecoveringStuck, "no-progress-timeout");
         Log(BotLogKind.Movement, $"No progress for {StuckSeconds}s - teleporting.");
```

### Transition Reason Table

| State | Reason | Exact trigger |
| --- | --- | --- |
| `Stopped` | `disabled` | top of loop when `Enabled == false` |
| `WaitingForClientFocus` | `not-attached-no-live-stats` | top of loop when no attached reader and no fresh live stats |
| `WaitingForClientFocus` | `client-size-invalid` | client width/height invalid |
| `WaitingForTrustedVitals` | `trusted-hp-missing` | flee safety enabled but HP percent is not trusted |
| `Buffing` | `buff-refresh-check` | configured buff keys before `MaybeRefreshBuffs` |
| `SelectingTarget` | `vision-fresh-select` | fresh client-coordinate scene and no held target |
| `SelectingTarget` | `no-target-visible` | fresh scene has no target and no held target |
| `SelectingTarget` | `scene-stale` | vision enabled, entity scene is stale |
| `SelectingTarget` | `scene-not-client-coords` | scene exists but is not safe client coords |
| `EngagingTarget` | `target-acquired` | selected/held target enters attack branch with zero casts |
| `EngagingTarget` | `target-held` | same target remains in attack branch after casts |
| `ConfirmingKill` | `hp-empty-confirmed` | low HP confirmation passes |
| `ConfirmingKill` | `held-target-not-visible` | no target found while an engaged target exists |
| `Roaming` | `no-target-walk` | no vision action and bot clicks walk point |
| `RecoveringStuck` | `hp-flee-teleport` | trusted HP trips flee teleport |
| `RecoveringStuck` | `target-kill-timeout` | expected kill deadline exceeded |
| `RecoveringStuck` | `combat-timeout` | combat progress timeout |
| `RecoveringStuck` | `no-progress-timeout` | general no-progress timeout |

## GRF Target Identity Audit

Current GRF path:
- `VisionAssistMarkerDetector` decodes `MobId` and `Name`.
- `OcrService.AddVisionAssistFinds(...)` stores `Source = "grf"` and `MobId = m.MobId`.
- `OcrReaderViewModel.TryBuildSceneItem(...)` already assigns a stable GRF `TrackId` when `MobId > 0`.
- `LiveScene.SetAuthoritativeEntities(...)` preserves incoming `TrackId` when it is positive.
- `SmartBotEngine.TargetKey(...)` keys held targets by `TrackId` first.

This means the biggest prior risk, sequential GRF ids from `LiveScene.SetAuthoritativeEntities`, is already partially fixed in the current workspace.

Remaining risk:
- `StableGrfTrackId(...)` uses `HashCode.Combine(...)`. It is stable enough inside one process, but not guaranteed stable across runtimes and can theoretically produce `int.MinValue`, making `Math.Abs(...) + 1` non-positive or overflowing.
- It buckets top-left coordinates (`x / 24`, `y / 24`), so a marker box that changes size or jitters around a cell edge can flip ids even if the monster did not move much.

Smallest safe GRF fix:
- Do not change `SceneItem`.
- Do not change `LiveScene`.
- Replace `StableGrfTrackId(...)` in `OcrReaderViewModel.cs` with a deterministic positive hash over `MobId` and center-point buckets.

Proposed diff:
```diff
--- a/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
+++ b/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
@@
-            int trackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd) : 0;
+            int trackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd) : 0;
@@
-        int monitorTrackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd, x, y) : 0;
+        int monitorTrackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd, x + fnd.W / 2, y + fnd.H / 2) : 0;
@@
     private static int StableGrfTrackId(OcrService.ScanFind fnd)
-        => StableGrfTrackId(fnd, fnd.X, fnd.Y);
+        => StableGrfTrackId(fnd, fnd.Cx, fnd.Cy);
 
-    private static int StableGrfTrackId(OcrService.ScanFind fnd, int x, int y)
+    private static int StableGrfTrackId(OcrService.ScanFind fnd, int centerX, int centerY)
     {
         if (fnd.MobId <= 0)
             return 0;
-        return Math.Abs(HashCode.Combine(fnd.MobId, x / 24, y / 24)) + 1;
+        unchecked
+        {
+            int hash = 17;
+            hash = hash * 31 + fnd.MobId;
+            hash = hash * 31 + centerX / 32;
+            hash = hash * 31 + centerY / 32;
+            return (hash & 0x3FFFFFFF) + 1;
+        }
     }
```

Why this is the smallest safe fix:
- It keeps GRF `MobId` authoritative.
- It keeps stable ids local to the existing GRF publishing path.
- It avoids adding `MobId`/`SourceId` to `SceneItem`, which would touch core scene records, overlay labels, tests, and docs.
- Center buckets are less sensitive to marker-box size changes than top-left buckets.
- The deterministic positive hash avoids `Math.Abs(int.MinValue)` and runtime hash-seed ambiguity.

Optional log-only hardening:
```diff
@@
-        string sceneSample = string.Join("; ", scene.Take(8).Select(e => $"#{e.TrackId}:{e.Label} s={e.Score:0.00} hit={e.Hits} miss={e.Misses} state={e.State} conf={e.Confirmed} atk={e.IsAttackable} box={e.X},{e.Y},{e.W}x{e.H}"));
+        string sceneSample = string.Join("; ", scene.Take(8).Select(e => $"#{e.TrackId}:{e.Label} s={e.Score:0.00} hit={e.Hits} miss={e.Misses} state={e.State} conf={e.Confirmed} atk={e.IsAttackable} box={e.X},{e.Y},{e.W}x{e.H}"));
```

No code change is strictly needed for the sample because accepted GRF samples already include `mobId`, and scene samples already include `TrackId`.

## Tests To Run If Main Applies Patch

Build/tests:
- `dotnet test tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj -c Release`
- `dotnet build 4rVivi.sln -c Release`

Manual DebugTrace verification:
- Start Smart Bot disabled, then enabled without a client:
  - Expect `[SmartBotState] old=Stopped new=WaitingForClientFocus reason=not-attached-no-live-stats`
- Enable Smart Bot with flee configured and no trusted HP:
  - Expect `[SmartBotState] old=<previous> new=WaitingForTrustedVitals reason=trusted-hp-missing`
- Enable OCR/vision with fresh client-coordinate entities:
  - Expect `SelectingTarget -> EngagingTarget` with reasons `vision-fresh-select` then `target-acquired`.
- Kill/vanish a held target:
  - Expect `EngagingTarget -> ConfirmingKill reason=hp-empty-confirmed` or `held-target-not-visible`.
- Let no target exist and walking enabled:
  - Expect `Roaming reason=no-target-walk`.
- Force a combat timeout:
  - Expect `RecoveringStuck reason=combat-timeout` or `target-kill-timeout`.

GRF identity verification:
- Enable Vision Assist GRF and watch `Entity scan` lines.
- Confirm the same marker keeps the same `TrackId` while it jitters within a small area.
- Confirm two same-mob markers separated by more than one 32 px center bucket do not share a `TrackId`.

CONTRACT IMPACT:
- None. The proposed state patch implements the existing `CONTRACTS.md` Smart Bot state-machine logging requirement.
- The GRF identity patch preserves the current `SceneItem` contract and avoids shared-record changes.

DO NOT TOUCH:
- Agent 3 did not edit `SmartBotEngine.cs`.
- Agent 3 did not edit `OcrReaderViewModel.cs`, `LiveScene.cs`, `OcrService.cs`, or `VisionAssistMarkerDetector.cs`.
