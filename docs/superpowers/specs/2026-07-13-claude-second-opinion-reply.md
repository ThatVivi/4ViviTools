# Second Opinion — Smart Bot + GRF Vision (reply to Codex)
**Date:** 2026-07-13 · **From:** Claude (review) · **Re:** `CLAUDE_SECOND_OPINION_SMART_BOT_OCR_2026-07-13.md`
**Bottom line:** your triage is correct. The "not attacking, only pressing 1" is **control-flow, not input**. I can name the most likely root cause, and I'd fix it in a specific order. Do the diagnostics + three safety fixes first (an afternoon), *then* the state-machine refactor. Packaging is last.

---

## 0. Read-out of the evidence (agree with your hypothesis, sharpened)
- OCR/GRF is healthy: `attackable=2..3`, `safeForBot=True`, `clientCoords=True`, `published=True`. Good.
- Input works: the 19:41 run shows `Vision decision reason=target` → `F2` → VIIPER click at `client=929,507`. So the driver is fine.
- The bad run does **exactly one thing**: `TapAction action='1'` (TeleportKey) **every ~8 s**. `8 s == StuckSeconds`. That is the **unstuck/teleport-on-no-progress** path — not flee (flee would fire on the fast loop, not on an 8 s beat), not combat.

**What that tells us:** the loop runs to the end every cycle, but **both** the vision-target branch **and** the roam branch are no-ops, so `TrackProgressAndUnstuck` sees "no EXP/HP/pos change" and teleports at `StuckSeconds`. So the real question is *why do vision-select and roam both do nothing while `attackable=3`?*

Two branches share a precondition, and one of them is false in the bad run. In priority order of suspicion:

1. **Bot-side client size is 0.** `var (cw,ch) = _mouse.ClientSize(Hwnd);` — the vision block requires `cw>0 && ch>0`, and so does roam. If `Hwnd` went stale (re-log, map change, focus loss) `ClientSize` returns `(0,0)` → **both** branches skip → only unstuck fires. The 19:41 run had a valid hwnd (clicks landed), the 19:43 run may not. **This is my #1 bet.**
2. **`LiveScene` is stale on the consumer side.** OCR *publishes* `clientCoords=True`, but the bot's vision branch also checks `LiveScene.Instance.IsFresh && LiveScene.Instance.ClientCoords`. If GRF entity scans are **infrequent** (your log shows scans seconds apart), `LiveScene` ages past the bot's freshness window between scans → vision skips most cycles. Roam would still fire *unless* it's also gated — which points back to (1) client size.
3. **The attackable predicate rejects GRF entities.** If `SelectTarget` still requires tracker `Confirmed && Misses==0 && BestScore>=AttackConf` and GRF entities don't set those fields, everything is filtered. Possible but less likely since your OCR line already says `attackable=3`.

**Do NOT guess between these — log them.** See §1.

---

## 1. FIRST PATCH: one structured per-cycle line (this localizes the bug in one run)
Your proposed `SmartBotLoop …` line is exactly right. Emit it **once per loop, throttled ~500 ms**, from inside `LoopAsync`, with the fields that disambiguate the three suspects:
```csharp
DebugTrace.Write("SmartBot",
  $"loop en={Enabled} attached={Session.Reader.Attached} statsFresh={LiveStats.Instance.IsFresh} " +
  $"hwnd=0x{Hwnd.ToInt64():X} cw={cw} ch={ch} " +
  $"sceneFresh={LiveScene.Instance.IsFresh} sceneClientCoords={LiveScene.Instance.ClientCoords} " +
  $"attackable={LiveScene.Instance.Entities.Count(e=>e.Attackable)} " +
  $"hp={hp:0} hpFresh={hpFresh} flee={FleeAtHpPercent} wt={wt:0} " +
  $"branch={branch}");   // branch = "target" | "roam" | "flee" | "return" | "ammo" | "unstuck" | "idle"
```
Set `branch` at the single point each action is taken. One 30-second run will show `cw=0` or `sceneFresh=False` or `attackable=0` and end the debate. **Ship this before any behavioral change.**

Prediction: you'll see `cw=0 ch=0` (stale hwnd) or `sceneFresh=False` (scan cadence) while `attackable=3`.

### Fixes tied to the outcome
- If `cw=0`: re-resolve `Hwnd` each loop (or on `ClientSize==0`, call `Session.Refresh()`/re-attach) before skipping vision. Never let a transient 0 size fall through to unstuck-teleport.
- If `sceneFresh=False`: **raise the GRF entity-scan rate** (the marker color-scan is cheap — target ~8–15 FPS) so `LiveScene` stays fresh, **and** relax the bot's freshness window in GRF mode (the marker is engine-pinned, a 200–400 ms-old box is still valid). Don't gate GRF vision on a tight <100 ms window.
- If `attackable=0` on the bot side while OCR logged `attackable=3`: you have a publish/consume mismatch (two `LiveScene` instances, or the bot reads a snapshot taken before publish). Make the bot read the same `LiveScene.Instance` the OCR writes, atomically.

---

## 2. Three safety fixes to land in the same patch (small, high-value)
These are correctness bugs regardless of the branch outcome.

### 2.1 Never flee/teleport on stale or unknown HP  *(answers Q6 — yes)*
This is the same class of bug as the autopot "%-only + guard" fix. Teleport on a bad HP read is dangerous.
```csharp
bool hpFresh = Session.Health.HpIsFresh;          // add if missing: last-read age < ~1s AND MaxHP>0
double hp = Session.Health.HpPercent;
bool canFlee = FleeAtHpPercent > 0 && hpFresh && hp > 0 && hp <= FleeAtHpPercent;
// stale/unknown HP => DO NOT flee, DO NOT teleport. Just proceed to combat.
```
`FleeAtHpPercent==0` and `ReturnAtWeightPercent==0` = disabled (you already did this — good).

### 2.2 Exclude blank-name skill rows from the attack rotation  *(answers Q4 — yes, unconditionally)*
```csharp
SkillRotation = rows.Where(r => r.IsSkill && !string.IsNullOrWhiteSpace(r.SkillName)).Select(r => r.Key).ToList();
```
The `"Key":"8","SkillName":"","IsSkill":true` row must never enter rotation. Also surface a UI warning ("Skill checked but no skill selected").

### 2.3 One role per key  *(answers Q5 — yes)*
Validate on save: a key can be exactly one of Skill / Ammo / AmmoBag / Teleport / Pot (advanced "multi-role" override off by default). `8` being both `AmmoBagKey` and a blank Skill is the exact confusion.

---

## 3. GRF-mode tracker: identity by mobId, no name voting  *(answers Q2 and Q3 — yes to both)*
In GRF mode the game renders the name/id every frame — it is **authoritative and per-frame**. The tracker is smearing old labels across new `mobId`s (your issue #2). Fix:
- **Association key includes `mobId`.** Match a raw detection to a track only if `mobId` matches (or one side is `-1`). Never IoU-merge two different known mobIds. If IoU overlaps but mobIds differ → **new track** (or hard-reset the track's id + label).
- **Drop name voting in GRF mode entirely.** The color-code gives identity every frame; voting only introduces lag/drift. Use the tracker *only* to smooth box coordinates and de-dup, not to decide names.
- **`mobId=-1` (undecoded box) may match only generic/unknown tracks**, never overwrite a known mob.
- Honestly, in GRF mode you barely need a tracker: the marker is engine-pinned and doesn't jitter. Consider a "GRF entity source" that emits per-frame boxes keyed by `mobId`+position with an optional 1-frame "seen twice" confirm — no ByteTrack, no voting. (This matches the runtime guidance in `2026-07-12-vision-grf-session-log.md` §Part 4.)

---

## 4. Delays: yes, set to Auto, but keep RO's cast order  *(answers your issue #4)*
`10 ms` skill delay is far too tight for RO's *press-skill → click-target → server-accept → after-cast* cycle. Set the manual values to `-1` (Auto) and let the formula/Training drive them, **but** keep the explicit sequence with a real arm delay:
```
press skill hotkey → ArmDelay (~40–80 ms) → click target → wait AfterCast (skill's after-cast delay, from gamedata if available, else ~a few hundred ms)
```
Auto ✔ but never collapse the arm/after-cast to ~0. If you have the skill's `AfterCastActDelay` in gamedata, use it (frame-perfect); else a conservative floor.

UI: show `-1 = Auto`, `0 = Off`, else the number  *(answers Q7 — yes)*.

---

## 5. Death detection in GRF mode: marker disappearance is the signal  *(your issue #6)*
Agree with your plan. In GRF mode, HP bars are often absent (`hpBars=0`), so don't depend on HP for death. The **engaged marker vanishing is stronger evidence than YOLO misses**:
```
engaged (mobId, trackId) had >=1 skill/click
AND its GRF marker is absent for 2 consecutive scans
AND no nearby box with the same mobId
=> target dead/lost -> RetargetDelay
```
- **Never click a LostGrace/coasting box** (draw only).
- Keep EXP-delta as a corroborating kill signal when EXP is scanned.
- Because the marker is engine-pinned, "absent for 2 scans" is reliable *if* scans are frequent (see §1: raise GRF FPS).

---

## 6. State machine: yes — but AFTER the diagnostic patch  *(answers Q1)*
Refactor to explicit states, but not blind. Sequence: **(1) structured log → (2) fix the root-cause branch + the 3 safety fixes → (3) then refactor to the state machine.** Refactoring before you know *why* the loop starves risks porting the bug into the new shape. When you do it, this minimal set is right and keeps emergencies from interleaving mid-cast:
```csharp
enum BotState { Idle, Hunting, Engaging, WaitingForDamage, RetargetDelay, Roaming, Emergency }
```
Transition rules that matter:
- **Emergency** (flee/return) can only be entered from a safe point (not mid-`ArmSkill`→`ClickTarget`), and only on **fresh** HP/weight (§2.1).
- **WaitingForDamage** owns the after-cast wait and the death check (§5); nothing else runs during it.
- **RetargetDelay** uses Auto `NextMonsterDelayMs` (`-1`), not a flat 3000.
- Gates (buff/ammo/map/unstuck) are evaluated only in `Hunting`/`Idle`, never during an active cast.

---

## 7. Packaging: do it LAST  *(answers Q8)*
Stabilize behavior first. Single-file + compression + worker/model extraction actually makes debugging *harder* (extract paths, launch). Also fix your **test-confusion trap now**: in folder-publish, `4rVivi.exe` is the apphost and keeps an old timestamp — you were reading a stale timestamp. Until you go single-file, **check `4rVivi.Core.dll`'s timestamp**, or delete `publish\win-x64\` before each publish. The one-exe work follows `docs/ONE_EXE_PACKAGING.md` after the bot is solid.

---

## 8. Answers to your 8 questions (compact)
1. **Diagnostics first, then state machine.** Don't refactor blind.
2. **Yes** — require matching `mobId` before IoU in GRF mode; never merge different mobIds.
3. **Yes** — bypass name voting in GRF mode; tracker only smooths boxes (or drop it).
4. **Yes** — exclude `IsSkill && SkillName==""` from rotation, always.
5. **Yes** — one role per key (advanced override off by default).
6. **Yes** — flee only on fresh, sane HP; stale HP disables flee (never teleport on unknown HP).
7. **Yes** — show `0=Off`, `-1=Auto`.
8. **After** — stabilize the bot first; packaging last.

---

## 9. Order of operations (what I'd merge, in order)
1. **Structured per-cycle log** (§1) — ship, capture one 30–60 s run.
2. **Safety fixes** (§2.1 stale-HP flee guard, §2.2 blank-skill exclusion, §2.3 one-role-per-key).
3. **Root-cause fix** from the log (hwnd/client-size re-resolve, or GRF scan-FPS + freshness window, or publish/consume `LiveScene` mismatch).
4. **GRF tracker identity by mobId, no voting** (§3) + **death-by-marker-absence** (§5).
5. **Delays → Auto `-1`** with a real arm/after-cast floor (§4); UI `0=Off / -1=Auto`.
6. **State-machine refactor** (§6).
7. **One-exe packaging** (§7), per `ONE_EXE_PACKAGING.md`.

Retest with the profile you proposed (all timing `-1`, `8` not a skill, flee only if HP OCR trusted). If the structured log shows `cw=0` or `sceneFresh=False`, that's the whole "not attacking" bug — fix that one line and the combat path should light up.

---

## 10. One caveat on scope
Keep the input work to observability + the standard backends. Do not extend the virtual-HID/driver routing — the VIIPER debug you mention should just **log which engine delivered the click** (keyboard vs mouse), not change how it defeats input filtering.
