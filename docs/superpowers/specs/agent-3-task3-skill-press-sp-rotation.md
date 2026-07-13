# Agent 3 Task 3: Skill Press, SP Gate, Rotation Fairness

AGENT: 3 / Epicurus - Smart Bot Combat

SCOPE:
- Audit/proposed diffs only.
- Do not edit runtime files.
- Do not revert unrelated dirty workspace changes.

OBJECTIVE:
- Audit why Smart Bot can click targets but sometimes not press skills.
- Inspect `HasEnoughSpFor`, `SkillSpRequired`, hotbar cards, controller mapping, `TapAction`, and cooldown handling.
- Propose exact fixes so:
  - selected skill key is attempted first when SP is trusted/enough;
  - if SP is unknown, behavior is safe and visible;
  - cooldown does not skip a skill forever;
  - logs show why a skill was or was not pressed.

FILES INSPECTED:
- `docs/superpowers/specs/agent-3-task2-state-machine-grf-identity.md`
- `docs/CODEX-MAP.md`
- `docs/USER_GUIDE.md`
- `docs/PROJECT_IMPROVEMENT_PLAN.md`
- `docs/superpowers/specs/CONTRACTS.md`
- `docs/superpowers/specs/RUN-LOG-2026-07-13.md`
- `docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md`
- `docs/superpowers/specs/2026-07-13-claude-overnight-large-task-questions.md`
- `docs/CLAUDE_SECOND_OPINION_SMART_BOT_OCR_2026-07-13.md`
- `docs/superpowers/specs/2026-07-12-vision-grf-session-log.md`
- `docs/superpowers/specs/2026-07-11-next-steps-and-vision-assist-grf.md`
- `docs/superpowers/specs/2026-07-11-vision-grf-next-steps-and-fixes.md`
- `docs/rathena/client-grf-data.md`
- `docs/rathena/ro-tools-bot-and-states.md`
- `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- `src/4rVivi.Core/Game/LiveStats.cs`
- `src/4rVivi.Core/Game/StatReader.cs`
- `src/4rVivi.Core/Input/InputMethod.cs`
- `src/4rVivi.Core/Input/KeySender.cs`
- `src/4rVivi.Core/Input/MouseSender.cs`
- `src/4rVivi.Core/Settings/AppSettings.cs`
- `src/4rVivi.App/ViewModels/SmartBotViewModel.cs`
- `src/4rVivi.App/Views/SmartBotView.axaml`

FILES CREATED:
- `docs/superpowers/specs/agent-3-task3-skill-press-sp-rotation.md`

## Additional Docs And Gameplay Research Incorporated

Local project docs:
- `CODEX-MAP.md` and `USER_GUIDE.md` define the user-facing RO skill order as skill hotkey first, then target click, then a delay/cooldown wait. This handoff preserves that order and focuses on making the key press observable.
- `CONTRACTS.md` requires trusted vitals for bot safety decisions. For skill pressing, `SP needed` must not use the bare cached `_stat.Sp` value as proof that enough SP exists. Unknown SP must be visible instead of optimistic.
- `RUN-LOG-2026-07-13.md` says timing migration, two-scan kill confirmation, state transition logging, and cooldown-after-key/click already landed on main. This task intentionally does not re-propose those broad patches; it only tightens the skill key/SP/rotation path.
- `CLAUDE_SECOND_OPINION_SMART_BOT_OCR_2026-07-13.md` captured the likely field symptom: active profile had `F2` as Double Strafe, but also key `8` marked as a blank skill and 10 ms skill delays. Current VM code filters blank skill rows in the hotbar sync path, but legacy rotation and saved profile diagnostics still need visible warnings so a stale/conflicting row cannot masquerade as "skill press failed."
- Vision GRF docs confirm GRF markers are the stable target identity/location source when enabled, and undecoded boxed monsters should remain visible as attackable `Monster` targets. The skill patch should log target/source context but must not couple skill rotation fairness to name OCR.
- `PROJECT_IMPROVEMENT_PLAN.md` keeps input-backend changes out of this task. The proposed `TapAction` change is local Smart Bot observability/routing for hotbar keys, not a backend rewrite.

External rAthena references checked:
- rAthena `doc/skill_db.txt` documents `SpCost` as the SP required to cast, and documents `AfterCastActDelay` and `Cooldown` in milliseconds: https://github.com/rathena/rathena/blob/master/doc/skill_db.txt
- rAthena `db/re/skill_db.yml` carries the same live database fields for cast time, after-cast delay, cooldown, SP cost, ammo type, and ammo amount: https://github.com/rathena/rathena/blob/master/db/re/skill_db.yml
- rAthena `conf/battle/skill.conf` notes client-sided skill delays vary by skill and are commonly around 140-180 ms, which supports rejecting 10 ms skill delays as an unsafe manual profile value: https://github.com/rathena/rathena/blob/master/conf/battle/skill.conf
- rAthena `doc/item_db.txt` distinguishes Healing, Usable, DelayConsume, and Ammo item types. This supports keeping potion/ammo/bag keys separate from attack skill rotation and logging when an ammo or item gate, not the skill gate, blocked combat: https://github.com/rathena/rathena/blob/master/doc/item_db.txt

## Current Evidence

1. Skill rotation key is consumed before readiness/SP checks.
- `SmartBotEngine.cs:372-380` does `_skillIdx++` as soon as it selects a key, before cooldown, SP, and input-route checks.
- If the key is cooling down, SP-gated, unmapped, or not actually sent, the rotation has still advanced.
- Symptom: a skill can be skipped repeatedly while the bot still clicks normally.

2. SP gate treats unknown SP as enough.
- `HasEnoughSpFor(...)` at `SmartBotEngine.cs:760-766` returns `true` when `_stat.Sp < 0`.
- This hides the important difference between "SP is enough" and "SP is unknown".
- `ResolveSkillDelayMs(...)` calls `HasEnoughSpFor(...)` twice, so unknown SP is also folded into timing as if it were enough.

3. `TapAction(...)` does not report whether a skill key was actually attempted.
- It is `void`.
- It logs internal routing, but the combat branch always logs `Cast ...` after `TapAction(skillKey, 20)` and click.
- In `InputMethod.ReWasdClick`, `TapAction` tries controller mapping first. If there is no mapping and reWASD is running, it does not fall back to keyboard because of `if (!_mouse.IsReWasdRunning() && KeyName.ToVk(action) > 0)`.
- Symptom: bot clicks target, log may say "Cast", but the skill key may never have been attempted.

4. Hotbar card path is mostly correct.
- Skill hotbar cards persist `SpRequired` and `SkillDelayMs`.
- `SyncSkillButtons()` copies enabled skill rows into `SmartBot.SkillRotation`, `SkillSpRequired`, and `SkillDelayMsByKey`.
- If `SkillClickMode == "Deactivated"`, rotation is intentionally empty.
- Legacy `ApplyRotation()` can still populate `SkillRotation` without `SpRequired` or per-key delay; this is acceptable as a no-SP-gate fallback, but logs should say no SP gate configured.

5. rAthena skill metadata confirms `-1 = Auto` is the right default.
- Skill DB timing is in milliseconds and split across cast time, after-cast action delay, cooldown, and requirements.
- A single user-entered 10 ms delay cannot safely model that. Main should keep `SkillDelayMs = -1` as the preferred profile value and log when a manual delay below the RO/client delay floor is used.

## Root Cause Hypothesis

The bot clicks but does not press skills because the combat loop currently conflates four cases:
- no skill configured;
- selected skill cooling down;
- selected skill blocked by SP;
- selected skill route was not actually attempted.

The fix should not refactor the whole engine. It should make skill choice explicit, make SP gating tri-state, and make `TapAction` return a small attempt result so the combat log cannot claim a cast when no skill key was attempted.

## Proposed Fix 1: Replace Boolean SP Gate With Visible Tri-State

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: after target fields near `_targetLowHpScans`, or near `HasEnoughSpFor`.

Proposed types:
```diff
@@
     private int _targetGoneScans;
     private int _targetLowHpScans;
+    private long _lastSkillBlockedLogTick;
     private static readonly Lazy<GameDatabase> _gameDb = new(() => new GameDatabase());
+
+    private enum SkillSpGateKind
+    {
+        NotRequired,
+        Enough,
+        Low,
+        Unknown
+    }
+
+    private readonly record struct SkillSpGate(
+        SkillSpGateKind Kind,
+        int Required,
+        int Current,
+        string Source)
+    {
+        public bool CanAttempt => Kind is SkillSpGateKind.NotRequired or SkillSpGateKind.Enough;
+        public string Summary => Kind switch
+        {
+            SkillSpGateKind.NotRequired => "sp-gate=none",
+            SkillSpGateKind.Enough => $"sp-gate=enough current={Current} required={Required} source={Source}",
+            SkillSpGateKind.Low => $"sp-gate=low current={Current} required={Required} source={Source}",
+            _ => $"sp-gate=unknown required={Required} source={Source}"
+        };
+    }
```

Replace `HasEnoughSpFor(...)` with:
```diff
-    private bool HasEnoughSpFor(string skillKey)
+    private SkillSpGate CheckSpForSkill(string skillKey)
     {
-        if (string.IsNullOrWhiteSpace(skillKey)) return true;
-        if (!SkillSpRequired.TryGetValue(skillKey, out var need) || need <= 0) return true;
-        int sp = _stat.Sp;
-        return sp < 0 || sp >= need;
+        if (string.IsNullOrWhiteSpace(skillKey))
+            return new SkillSpGate(SkillSpGateKind.NotRequired, 0, -1, "no-skill");
+
+        if (!SkillSpRequired.TryGetValue(skillKey, out var need) || need <= 0)
+            return new SkillSpGate(SkillSpGateKind.NotRequired, 0, -1, "no-cost-configured");
+
+        if (LiveStats.Instance.TryGetTrustedNumber(Roles.Sp, out var trustedFlatSp))
+        {
+            int flatSp = Math.Clamp(trustedFlatSp, 0, 999999);
+            return flatSp >= need
+                ? new SkillSpGate(SkillSpGateKind.Enough, need, flatSp, "trusted-flat-sp")
+                : new SkillSpGate(SkillSpGateKind.Low, need, flatSp, "trusted-flat-sp");
+        }
+
+        if (LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var spPct)
+            && LiveStats.Instance.TryGetTrustedNumber(Roles.MaxSp, out var trustedMaxSp)
+            && trustedMaxSp > 0)
+        {
+            int estimatedSp = (int)Math.Round(Math.Clamp(spPct, 0, 100) * trustedMaxSp / 100.0);
+            return estimatedSp >= need
+                ? new SkillSpGate(SkillSpGateKind.Enough, need, estimatedSp, "trusted-sp-percent")
+                : new SkillSpGate(SkillSpGateKind.Low, need, estimatedSp, "trusted-sp-percent");
+        }
+
+        return new SkillSpGate(SkillSpGateKind.Unknown, need, -1, "trusted-sp-missing");
     }
```

Why this is safe:
- If a skill row has `SP needed = 0`, the bot attempts the skill as before.
- If the user configured a flat SP requirement, the bot only presses that skill when a trusted flat SP value, or trusted SP percent plus trusted MaxSP, can prove enough.
- If SP is unknown, the bot does not silently pretend it is enough. It falls back to normal attack and logs the reason.
- This keeps OCR internals hidden from beginner UI; the log is diagnostic.

Rejected shortcut:
- Do not use the current `_stat.Sp` fallback as proof of enough SP:
```csharp
int flatSp = _stat.Sp;
if (flatSp >= 0)
    return flatSp >= need
        ? new SkillSpGate(SkillSpGateKind.Enough, need, flatSp, "flat-sp-memory")
        : new SkillSpGate(SkillSpGateKind.Low, need, flatSp, "flat-sp-memory");
```
- That is exactly the current bug class: unknown SP is treated as enough and logs cannot tell the difference.

## Proposed Fix 2: Choose Skill Fairly And Advance Only After Press Attempt

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: near `HasEnoughSpFor` replacement.

Add:
```csharp
private readonly record struct SkillAttemptPlan(
    string Key,
    bool CanPress,
    int CooldownWaitMs,
    SkillSpGate Sp,
    string Reason);

private SkillAttemptPlan ChooseSkillToAttempt(long now)
{
    if (SkillRotation.Count == 0)
        return new SkillAttemptPlan("", false, 0, new SkillSpGate(SkillSpGateKind.NotRequired, 0, -1, "no-rotation"), "no-skill-configured");

    int start = SkillRotation.Count == 0 ? 0 : Math.Abs(_skillIdx) % SkillRotation.Count;
    int shortestCooldown = int.MaxValue;
    SkillAttemptPlan firstBlocked = default;

    for (int i = 0; i < SkillRotation.Count; i++)
    {
        int index = (start + i) % SkillRotation.Count;
        var key = SkillRotation[index];
        if (string.IsNullOrWhiteSpace(key))
            continue;

        if (_skillCdUntil.TryGetValue(key, out var until) && now < until)
        {
            int wait = Math.Clamp((int)(until - now), 25, 5000);
            shortestCooldown = Math.Min(shortestCooldown, wait);
            if (string.IsNullOrEmpty(firstBlocked.Key))
                firstBlocked = new SkillAttemptPlan(key, false, wait, CheckSpForSkill(key), "cooldown");
            continue;
        }

        var sp = CheckSpForSkill(key);
        if (!sp.CanAttempt)
        {
            if (string.IsNullOrEmpty(firstBlocked.Key))
                firstBlocked = new SkillAttemptPlan(key, false, 0, sp, sp.Kind == SkillSpGateKind.Low ? "sp-low" : "sp-unknown");
            continue;
        }

        return new SkillAttemptPlan(key, true, 0, sp, i == 0 ? "selected-ready" : "next-ready");
    }

    if (!string.IsNullOrEmpty(firstBlocked.Key))
        return shortestCooldown < int.MaxValue && firstBlocked.Reason == "cooldown"
            ? firstBlocked
            : firstBlocked with { CooldownWaitMs = shortestCooldown == int.MaxValue ? 0 : shortestCooldown };

    return new SkillAttemptPlan("", false, 0, new SkillSpGate(SkillSpGateKind.NotRequired, 0, -1, "empty-rotation"), "no-usable-skill");
}

private void AdvanceSkillRotationAfterAttempt(string skillKey)
{
    if (SkillRotation.Count == 0 || string.IsNullOrWhiteSpace(skillKey))
        return;

    int index = SkillRotation.FindIndex(k => string.Equals(k, skillKey, StringComparison.OrdinalIgnoreCase));
    _skillIdx = index >= 0 ? (index + 1) % SkillRotation.Count : _skillIdx % SkillRotation.Count;
}

private void LogSkillDecision(SkillAttemptPlan plan, SceneItem target)
{
    long now = Environment.TickCount64;
    if (now - _lastSkillBlockedLogTick < 700)
        return;
    _lastSkillBlockedLogTick = now;
    var key = string.IsNullOrWhiteSpace(plan.Key) ? "none" : plan.Key;
    Log(BotLogKind.Skill, $"Skill decision key={key} reason={plan.Reason} {plan.Sp.Summary} cooldownWaitMs={plan.CooldownWaitMs} target={TargetName(target)}.");
}
```

Why this fixes fairness:
- `_skillIdx` is now the next skill to consider, not a value consumed before checks.
- The selected key at `_skillIdx` is attempted first when it is ready and SP is enough.
- If the selected key is cooling down, the loop can use the next ready key, but the cooling key is reconsidered when the rotation wraps.
- If all keys are cooling down, `_skillIdx` does not advance, so a skill is not skipped forever.
- If all keys are SP-low/unknown, the bot falls back to normal attack and logs why.

## Proposed Fix 3: Return A Visible Result From `TapAction`

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: immediately before `TapAction(...)`.

Add:
```csharp
private readonly record struct ActionAttemptResult(bool Attempted, string Route, string Reason)
{
    public static ActionAttemptResult No(string reason) => new(false, "none", reason);
    public static ActionAttemptResult Yes(string route) => new(true, route, "attempted");
}
```

Replace `TapAction` signature and returns:
```diff
-    private void TapAction(string action, int holdMs)
+    private ActionAttemptResult TapAction(string action, int holdMs, bool keyboardFirstForSkill = false)
     {
-        if (string.IsNullOrWhiteSpace(action)) return;
+        if (string.IsNullOrWhiteSpace(action)) return ActionAttemptResult.No("empty-action");
         int vk = KeyName.ToVk(action);
 
+        if (keyboardFirstForSkill && vk > 0)
+        {
+            DebugTrace.Write("SmartBot", $"TapAction skill-keyboard-first action='{action}' vk={vk} method={Keys.Method} holdMs={holdMs}.");
+            Keys.Tap(Hwnd, vk, Math.Max(60, holdMs));
+            return ActionAttemptResult.Yes("keyboard-first-skill");
+        }
+
         // RO hotbar skills are keyboard hotkeys. When VIIPER/FakerInput is selected, send the
         // actual key first; a ViGEm controller button only works when an external profile maps it
         // back to the RO key, so it must be a fallback rather than the primary skill path.
         if (Keys.Method != InputMethod.ReWasdClick && vk > 0)
         {
             DebugTrace.Write("SmartBot", $"TapAction keyboard-first action='{action}' vk={vk} method={Keys.Method} holdMs={holdMs}.");
             Keys.Tap(Hwnd, vk, Math.Max(60, holdMs));
-            return;
+            return ActionAttemptResult.Yes("keyboard-first");
         }
@@
                 if (_mouse.TapVirtualButton(button, holdMs))
-                    return;
+                    return ActionAttemptResult.Yes("controller");
                 DebugTrace.Write("SmartBot", $"TapAction controller failed for '{action}', trying keyboard fallback.");
@@
                 DebugTrace.Write("SmartBot", $"Controller bridge is not running; sending real key fallback for '{action}'.");
                 Keys.TapSendInputFallback(Hwnd, KeyName.ToVk(action), holdMs);
+                return ActionAttemptResult.Yes("keyboard-fallback");
             }
+            return ActionAttemptResult.No(string.IsNullOrWhiteSpace(button) ? "no-controller-mapping" : "controller-failed-no-fallback");
         }
         else
         {
             DebugTrace.Write("SmartBot", $"TapAction keyboard action='{action}' vk={vk} holdMs={holdMs}.");
             Keys.Tap(Hwnd, vk, holdMs);
+            return vk > 0 ? ActionAttemptResult.Yes("keyboard") : ActionAttemptResult.No("invalid-key");
         }
     }
```

Call-site impact:
- Existing `TapAction(...)` callers can ignore the returned result.
- C# allows ignoring returned values.
- The skill branch should use the result to avoid false "Cast" logs.

Why `keyboardFirstForSkill` matters:
- In `ReWasdClick` mode, mouse clicks use ViGEm/controller, but RO skill hotbar activation is still a keyboard hotkey.
- Current code can route skill activation through controller mapping and suppress keyboard fallback while reWASD is running.
- The proposed skill-only flag attempts the actual hotbar key first and logs that route. It does not change mouse click routing.

## Proposed Fix 4: Rewrite Only The Skill Sub-Branch

Insertion point:
- File: `src/4rVivi.Core/Automation/SmartBotEngine.cs`
- Anchor: inside target branch after `int tx...` and before normal/skill action.

Replace:
```csharp
string skillKey = "";
if (string.IsNullOrWhiteSpace(skillKey) && SkillRotation.Count > 0)
{
    skillKey = SkillRotation[_skillIdx++ % SkillRotation.Count];
}
long now = Environment.TickCount64;
bool skillReady = !string.IsNullOrEmpty(skillKey)
    && !(_skillCdUntil.TryGetValue(skillKey, out var until) && now < until);
bool hasEnoughSp = HasEnoughSpFor(skillKey);
var actionDelay = ResolveSkillDelayMs(skillKey, t, cw, ch);
```

With:
```csharp
long now = Environment.TickCount64;
var skillPlan = ChooseSkillToAttempt(now);
string skillKey = skillPlan.CanPress ? skillPlan.Key : "";
var actionDelay = ResolveSkillDelayMs(skillKey, t, cw, ch);
```

Then replace the `if (!string.IsNullOrEmpty(skillKey))` block with:
```csharp
if (!string.IsNullOrEmpty(skillKey))
{
    var press = TapAction(skillKey, 20, keyboardFirstForSkill: true);
    if (press.Attempted)
    {
        _castsOnTarget++;
        await Timing.DelayAsync(ResolveSkillArmDelayMs(skillKey, t, cw, ch), ct);
        ClickAt(tx, ty);
        _skillCdUntil[skillKey] = Environment.TickCount64 + Math.Max(80, actionDelay);
        AdvanceSkillRotationAfterAttempt(skillKey);
        Log(BotLogKind.Skill, $"Skill pressed key={skillKey} route={press.Route} cast={_castsOnTarget} {skillPlan.Sp.Summary} target={TargetStatus(t)} @ {tx},{ty}");
        visionActed = true;
        MaybeCombatUnstuck();
        await Timing.DelayAsync(Math.Clamp(actionDelay, 80, 5000), ct);
    }
    else
    {
        Log(BotLogKind.Skill, $"Skill not pressed key={skillKey} reason={press.Reason} route={press.Route}; normal attack fallback on {TargetName(t)}.");
        AttackTarget(tx, ty);
        visionActed = true;
        MaybeCombatUnstuck();
        await Timing.DelayAsync(ResolveNormalAttackDelayMs(t, cw, ch), ct);
    }
}
else if (skillPlan.Reason == "cooldown" && skillPlan.CooldownWaitMs > 0)
{
    int wait = Math.Clamp(skillPlan.CooldownWaitMs, 25, Math.Min(250, Math.Max(40, actionDelay)));
    LogSkillDecision(skillPlan, t);
    visionActed = true;
    await Timing.DelayAsync(wait, ct);
}
else
{
    if (SkillRotation.Count > 0)
        LogSkillDecision(skillPlan, t);
    AttackTarget(tx, ty);
    Log(BotLogKind.Movement, SkillRotation.Count > 0
        ? $"Normal attack {TargetStatus(t)} @ {tx},{ty} because skill unavailable ({skillPlan.Reason})."
        : $"Attack {TargetStatus(t)} ({t.Score:0.00}) @ {tx},{ty}");
    visionActed = true;
    MaybeCombatUnstuck();
    await Timing.DelayAsync(ResolveNormalAttackDelayMs(t, cw, ch), ct);
}
```

Important behavior:
- Skill press happens before target click.
- Cooldown-only blocks wait briefly and do not click as if a normal attack was desired.
- SP-low/unknown falls back to normal attack and logs the gate reason.
- A failed/missing controller route no longer logs a fake cast.
- Rotation advances only after `press.Attempted == true`.

## Proposed Fix 5: Stop Recomputing SP Gate Inside Timing Formula

Current:
- `ResolveSkillDelayMs(...)` calls `HasEnoughSpFor(skillKey)` twice.
- After tri-state SP, this would duplicate gate reads and possibly produce inconsistent logs/behavior.

Smallest fix:
```diff
-    private int ResolveSkillDelayMs(string skillKey, SceneItem target, int clientW, int clientH)
+    private int ResolveSkillDelayMs(string skillKey, SceneItem target, int clientW, int clientH, SkillSpGate? spGate = null)
@@
-        int castBase = HasEnoughSpFor(skillKey) ? 120 : 80;
+        var gate = spGate ?? CheckSpForSkill(skillKey);
+        bool spCanAttempt = gate.CanAttempt;
+        int castBase = spCanAttempt ? 120 : 80;
@@
-        int sp = HasEnoughSpFor(skillKey) ? 0 : -45;
+        int sp = spCanAttempt ? 0 : -45;
```

Then in the skill branch:
```csharp
var actionDelay = ResolveSkillDelayMs(skillKey, t, cw, ch, skillPlan.Sp);
```

Existing callers like `TargetStatus(...)` can keep using the optional default.

## Proposed Fix 6: Make Hotbar SP Gate Beginner-Visible

Current UI:
- The hotbar card says `SP needed`.
- It does not tell the user that `0` disables the SP gate or that nonzero values require trusted SP evidence to press skills.

Smallest UI copy proposal:
```diff
--- a/src/4rVivi.App/Views/SmartBotView.axaml
+++ b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-                                  <TextBlock Text="SP needed" Classes="muted"/>
+                                  <TextBlock Text="SP needed (0 = ignore)" Classes="muted"/>
```

Optional VM status when skills are deactivated:
```diff
@@
         if (blankSkillRows > 0)
             SkillSuggestionStatus = $"{blankSkillRows} checked skill key(s) have no skill selected, so they were skipped.";
+        else if (skillRows.Count > 0 && keys.Count == 0)
+            SkillSuggestionStatus = "Skill rows are configured, but skill pressing is deactivated.";
```

## Proposed Fix 7: Keep Non-Skill Hotbar Roles Out Of Attack Rotation

Current state:
- `SyncSkillButtons()` and `ApplyPersistedConfig()` already filter hotbar skill rows by `Enabled && IsSkill && SkillName nonblank && Key nonblank`.
- `ApplyRotation()` is still a legacy path that writes arbitrary keys to `SkillRotation` without `SkillSpRequired`, `SkillDelayMsByKey`, or skill names.
- The field report had key `8` marked as blank skill and ammo bag. Even if the current hotbar filter skips it, main should keep this from coming back through legacy or migrated profile paths.

Smallest patch guidance:
```diff
@@
 private void ApplyRotation()
 {
+    DebugTrace.Write("SmartBotVM", "Legacy ApplyRotation used; hotbar skill metadata unavailable, SP gate and per-skill delay disabled for these keys.");
     _hub.SmartBot.SkillRotation.Clear();
```

Add a saved-profile diagnostic in `SyncSkillButtons()`:
```csharp
var conflictingSkillRows = SkillButtons
    .Where(b => b.Enabled && b.IsSkill && (b.IsAmmo || b.IsAmmoBag || b.IsHpPotion || b.IsSpPotion || b.IsTeleport))
    .Select(b => b.Key)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (conflictingSkillRows.Length > 0)
    SkillSuggestionStatus = $"Key(s) {string.Join(", ", conflictingSkillRows)} are marked as Skill and item/ammo actions; attack rotation will use only rows with a selected skill name.";
```

Do not block advanced multi-role profiles in this task. Just skip invalid attack skills and log the skip clearly.

## Tests To Add If Main Applies Patch

Recommended test file:
- `tests/4rVivi.Core.Tests/SmartBotSkillRotationTests.cs`

Test cases:
1. `ChooseSkillToAttempt` returns the current rotation key when cooldown expired and `SpRequired == 0`.
2. With `SpRequired > 0` and no trusted SP, plan is blocked with `Reason == "sp-unknown"` and `_skillIdx` does not advance.
3. With current key on cooldown and next key ready, next key is selected, and the cooled key is reconsidered after rotation wraps.
4. When all keys are cooling down, plan reason is `cooldown`, wait is bounded, and `_skillIdx` does not advance.
5. `TapAction(... keyboardFirstForSkill: true)` returns attempted route `keyboard-first-skill` for a valid hotbar key even when `Keys.Method == InputMethod.ReWasdClick`.

Verification commands:
- `dotnet test tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj -c Release`
- `dotnet build 4rVivi.sln -c Release`

Manual DebugTrace/BotLog checks:
- Skill configured with `SP needed = 0`: expect `Skill pressed key=F... route=keyboard-first-skill ... sp-gate=none`.
- Skill configured with nonzero SP and trusted SP missing: expect `Skill decision key=F... reason=sp-unknown sp-gate=unknown...`, followed by normal attack fallback.
- Skill cooling down: expect `Skill decision key=F... reason=cooldown cooldownWaitMs=...`; no fake `Cast` log.
- ReWasdClick mode with skill hotkey: expect `TapAction skill-keyboard-first ...` before `ClickAt requested ...`.

## Findings Summary

- The likely "clicks but no skills" root cause is not monster targeting. It is skill-path observability and rotation consumption.
- Current code can advance `_skillIdx` before a skill press is possible.
- Current SP logic says unknown SP is enough, which hides configuration/OCR problems.
- Current ReWasdClick/controller path can suppress keyboard fallback while still allowing target clicks.
- The proposed patch stays local to `SmartBotEngine` plus one optional UI label. It does not require changing input backends.

CONTRACT IMPACT:
- None. This implements Smart Bot behavior/observability within the existing contracts.

DO NOT TOUCH:
- Agent 3 did not edit runtime files.
- Agent 3 did not edit `SmartBotEngine.cs`, `SmartBotViewModel.cs`, `SmartBotView.axaml`, input backends, controller routing, or shared contracts.
