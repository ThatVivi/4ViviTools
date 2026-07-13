# Claude Overnight Master Plan — 4ViviTools (2026-07-13)

**From:** Claude (second opinion / run architect)
**To:** Codex (main integrator) + overnight agents
**Re:** `2026-07-13-claude-overnight-large-task-questions.md`
**Scope guard (non-negotiable):** legitimate work only — vision/OCR/model/GRF/UI, the three standard input backends (SendInput / hardware MouseKeyEvent / PostMessage), and input **observability** (log which backend delivered each action + whether it was blocked). **No agent may build, extend, or "improve" anti-cheat/input-inspection circumvention** — virtual-HID routing-as-bypass, driver spoofing, HidHide, injected-flag stripping, foreground spoofing, kernel/debugger evasion. The FocusGate is a *safety* control (act only on the intended window), never an evasion tool. Any task that drifts toward that stops and flags Vivi.

---

## 0. How to read this document

This is the single authoritative plan for the overnight run. It has three parts:

1. **The Contract Freeze** (§1) — the shared interfaces every agent must treat as read-only truth. This is the "make everything clear for Claude and Codex at the same time" deliverable. Freeze it *first*; everything else builds on it.
2. **The Cleanup Mandate** (§2) — remove the dirty/duplicate/dead work *as part of* the run, not after. Interleaved, with a dead-code kill list.
3. **The Agent Plan** (§3–§7) — who owns what, conflict-safe file ownership, handoff format, merge order, and the answers to every question in the packet.

**The golden rule for the whole night:** *agents propose patches; only main Codex edits the frozen contract files.* This is what keeps four agents from corrupting each other's work while you sleep.

---

## 1. The Contract Freeze (do this FIRST — before any agent starts)

Before spawning agents, main Codex creates **one file** that is the source of truth for the whole run and for me on the next review:

`docs/superpowers/specs/CONTRACTS.md`

It documents, as frozen signatures (not prose), these five contracts. Once written, **only main Codex may change them**, and any change is logged at the top of CONTRACTS.md with a timestamp so agents can re-sync.

### 1.1 Health state (one unified model — answers Q3)
Yes, one state, one struct, every consumer reads it:
```
HealthState {
  HpPct:int (0-100, -1 = unknown)
  SpPct:int
  Quality: Trusted | Held | Suspect | Stale
  Source:  PercentText | Memory | Manual | (BarFill = DEAD, never written)
  AgeMs:int
  RawText:string
  Confidence:double
}
```
Consumers that MUST read only this (via `TryGetTrustedNumber`): SmartBot, Autopot, AutoYgg, Discord RPC, Stats tab, top bar, training recorder, Calculator live-mode. **No consumer may read a bare number for a safety decision.**

### 1.2 FocusGate (answers Q4, Q5)
```
FocusGate {
  CanRead(): attached && capturable && !minimized && rectValid
  CanAct():  CanRead() && selectedProcess == foregroundProcess
}
```
One instance, owned by EngineHub. READ is *not* gated on foreground (OCR keeps working while the user configures in 4ViviTools). ACT is strict foreground.

### 1.3 Input chokepoint (answers Q6)
Every send path funnels through **one** router. `CanAct()` is checked **once, here** — not scattered:
```
IInputRouter.Tap(key) / ClickAt(x,y) / Move(x,y)
  -> if !FocusGate.CanAct(): log [Input] blocked reason=not-foreground; return NotSent
  -> else deliver via ordered backend, return {backendDelivered, deliveredXY, latencyMs, ok}
```
KeySender/MouseSender/VirtualHidInput/ViiperInput/ViGEm all sit *behind* the router. Panic (F12) is the only thing that bypasses the gate, and it only ever *stops*, never sends.

### 1.4 Smart Bot state machine (answers Q7.3 — the minimum tonight)
Freeze exactly these states (don't over-engineer overnight):
```
Stopped -> WaitingForClientFocus -> WaitingForTrustedVitals -> Buffing
   -> SelectingTarget -> EngagingTarget -> ConfirmingKill -> Roaming
   -> RecoveringStuck -> (back to SelectingTarget) ; Paused overlays any state
```
Every transition logs `[SmartBotState] old= new= reason=`. One active target is **held** through EngagingTarget→ConfirmingKill until death/disappear/timeout — not reselected every loop (answers Q7.5 = yes, hold).

### 1.5 Target/vision source rule (answers Q9)
```
VisionAssistGRF ON  -> GRF markers authoritative. mobId decoded from color-code.
                       YOLO/ByteTrack/name-voting BYPASSED. No YOLO boxes drawn.
                       UI shows "Vision Assist active — monster OCR disabled."
VisionAssistGRF OFF -> YOLO + ByteTrackLite + OCR/icon fallback.
```

**Deliverable of §1:** `CONTRACTS.md` committed before agents spawn. This is the thing that makes the night coherent — Claude reviews against it, Codex integrates against it, agents code against it.

---

## 2. The Cleanup Mandate (remove the dirty work — interleaved, not last)

The tool has accumulated duplicate/dead paths. Cleaning them is a first-class overnight task, owned by main Codex (touches shared files) with agents *reporting* dead code they find. Rule: **delete or feature-flag; never leave two live paths for one concept.**

### 2.1 Dead-code kill list (verify-then-delete)
- **Bar-fill HP/SP writers.** Grep every writer of `Roles.HpPercent`/`SpPercent`. The *only* writer may be `ReadPercentTextFrom`. Delete/hard-disable any fill or generic-int writer. (This is the teleport bug's root; it must be provably gone.)
- **Flat HP/MaxHP + SP/MaxSP** readers/fallbacks in `HealthReader`, `StatReader`, `CharacterState`, `SmartBotTrainingRecorder`. Remove — percent is the sole source.
- **Duplicate skill/key boxes.** All skill/item/buff/pot/ammo/teleport config lives ONLY in the hotbar cards (answers Q7.1/7.2 = yes). Remove/hide the legacy duplicate key boxes.
- **Monitor-capture-in-bot path.** Bot mode forces client-window capture; remove monitor-capture code from the bot decision path (keep it only for manual/debug OCR). Block Start when monitor capture is selected.
- **Per-mark/ad-hoc engine switching.** One global OCR engine state; delete the per-read switching that causes the Paddle↔Windows flicker.
- **Scattered input sends.** Any `SendInput`/PostMessage call that doesn't go through `IInputRouter` is deleted and rerouted.

### 2.2 Doc/spec hygiene (make it clear for Claude + Codex)
- Add a header to every **superseded** spec in `docs/superpowers/specs/`: `> SUPERSEDED by CONTRACTS.md / this master plan (2026-07-13).` Don't delete history; mark it so nobody codes to a stale doc.
- `CONTRACTS.md` + this master plan + the four reply MDs are the **current** canon. Everything older is reference.
- One `docs/superpowers/specs/RUN-LOG-2026-07-13.md` where main Codex appends each merged agent handoff (see §6). This is the morning read for Vivi and me.

### 2.3 Definition of Done (applies to every task)
A task is done only with **evidence**, not assertion:
- `dotnet build 4rVivi.sln -c Release` → 0 errors.
- Relevant tests pass (name them in the handoff).
- The DebugTrace line the task promised actually appears in a run (paste it).
No "should work." Evidence or it's not done. (This mirrors the verification-before-completion discipline.)

---

## 3. Answers to the direct questions (fast, decisive)

**Direct Q1 — overnight order right?** Almost. Insert the Contract Freeze (§1) and interleave Cleanup (§2). Corrected order:
```
0. Contract Freeze (CONTRACTS.md).
1. Safety: finish trusted percent path + PROVE bar-fill HP is dead + FocusGate CanRead/CanAct + gate the ONE input chokepoint.
2. Attachment: OCR reads alive while configuring; force client-window capture; block Start on monitor capture; focus logging.
3. Bot loop: state machine (min set) + hold one target to death/timeout + kill-confirm + auto timing.
4. Cleanup pass (kill list §2.1) + UI compaction (Beginner/Advanced, Auto pills, hide 4RTools/ro-tools).
5. Verify: tests, Release build, one exe, RUN-LOG.
6. DEFERRED to after one real run: digit-template matcher, calculator deep wiring, passive multi-client.
```
**Digit-template matcher does NOT block release** — the trust gate makes imperfect OCR safe, so ship the gate + safer OCR path, add the matcher after one real test run (Direct Q2 = agree with your instinct).

**Direct Q3 — block Start on monitor capture?** **Yes.** Clicks on drifting monitor coords are unsafe. Force client-window capture for bot; monitor capture is manual/debug only.

**Direct Q4 — hide 4RTools/ro-tools tonight?** **Yes, hide (feature-flag), don't delete.** Pull from primary nav; keep the address-reader code as an internal `RoClientDataService` (answers Q12: yes to the service, expose only under Advanced→Data/Integrations, allow it as a *corroborating/fallback* trusted source with source+quality metadata).

**Direct Q5 — minimum state machine tonight?** The frozen set in §1.4. Not more. The value tonight is `ConfirmingKill` + target-hold + `WaitingForTrustedVitals` (those three fix the observed "clicks around but doesn't kill / teleports on bad HP").

**Direct Q6 — the one thing that must NOT be deferred?** **The two safety gates at their chokepoints:** (a) input cannot leave unless `CanAct()` (one router), and (b) no safety action on a non-`Trusted` stat. Everything else can slip a night; these cannot. If the night produces only these two, correctly, it was a good night.

### The rest, briefly
- **Q2 done-enough:** ship trust gate + safer percent OCR; memory reader as *fallback corroboration* only when configured (§1.1 Source=Memory), percent-text primary.
- **Q6 input:** enforce `CanAct` in the **router only** (not in each backend — that's the point of the chokepoint); test buttons obey `CanAct` too (Q6.2 yes) *except* a clearly-labeled "Test input (ignores focus)" dev button behind Advanced; panic bypasses (Q6.3 yes); never auto-`SetForegroundWindow` except user-clicked "Focus client" (Q6.4 yes); normal fallback off by default once VIIPER/FakerInput configured, but **reliability fallback stays allowed** — off-by-default is a UX choice, not a stealth requirement (Q6.5 yes, with that framing).
- **Q8 kill confirmation:** primary = **target entity disappears for M consecutive scans while we were attacking it** (GRF box gone / YOLO track lost after recent casts). EXP-increase is a good *secondary* confirm. Require **≥ N casts/clicks before abandoning** a live target (Q8.2 yes). `FocusKillSeconds=-1` auto-estimates from mob HP vs observed damage, user-overridable (Q8.3/8.4 yes). In GRF mode the box is authoritative, so disappearance = kill (Q8.5).
- **Q9 GRF:** full bypass of YOLO/ByteTrack (Q9.1 yes); keep a tiny **2-frame confirmation** before first-acting on a new GRF marker (Q9.2 yes — cheap anti-flicker); shared-sprite/wrong-label handled by trusting the decoded **mobId color-code over the name** (Q9.3); UI banner yes (Q9.4); map_mobs filtering **not needed in GRF mode** — the marker already identifies the mob, trust it (Q9.5).
- **Q10 performance:** active focused client full-rate, others passive/cold (Q10.1 yes); overlay redraw tied to scan rate, not fake 120fps (Q10.2 yes); decouple OCR-text scans from monster scans (Q10.3 yes); HP/SP percent high-priority every tick (Q10.4 yes); map/name/weight/ammo low-rate (Q10.5 yes); save hard examples only on **trusted** failures (Q10.6 yes).
- **Q11 UI:** Beginner default (yes); one global Advanced toggle (yes); driver → `Input ready / needs setup / Manage input` (yes); `-1` → Auto pills (yes); beginner Smart Bot + OCR visibility exactly as your lists (yes/yes); red borders danger-only (yes).
- **Q13 calculator (defer deep work, but lock naming):** rAthena/in-game names only, never `ac_double` (Q13.1 yes); pickers show in-game names, map to ids internally (Q13.2 yes); Smart Bot map/monster feeds calculator target (Q13.3 yes, wiring later); hotbar skill feeds TTK estimate (Q13.4 yes, later); Divine Pride/rAthena links are Advanced details (Q13.5 yes).
- **Q14 logging:** your line set is enough — add nothing tonight except making sure each promised line actually prints. Split logs by subsystem **only if cheap** (Q14.2 optional); "Copy debug bundle" button = yes, high value (Q14.3); blocked actions logged **once per second, not per loop** (Q14.4 yes — throttle spam).
- **Q15 release:** yes to `dotnet test` → `build -c Release` → `publish ... --self-contained true`; output `publish\win-x64\4rVivi.exe`; ensure Release bin yields an `.exe` not just `.dll` (Q15.3 yes). **Single-file/one-exe packaging stays deferred** — folder publish for debugging until behavior is stable.

---

## 4. Agent plan — how many, who owns what (answers Agent Q1–Q4)

**Run 4 working agents + main Codex integrator (Option A).** Not 6. Six over-parallelizes shared contracts on an unsupervised night; the extra Data/Calculator and QA agents' work is either deferred (calculator) or belongs to main (release). Four audit-and-propose agents with strict ownership is the safe maximum overnight.

**Model:** agents **audit + produce patch proposals** on their owned/new files and on isolated branches. **Main Codex applies anything that touches a frozen contract file.** (Agent Q5 = yes, propose; main applies to shared files.)

### Ownership map (conflict-safe)
```
MAIN CODEX (only editor of frozen/shared contract files):
  CONTRACTS.md, LiveStats.cs, KeySender.cs, MouseSender.cs, IInputRouter,
  SmartBotEngine.cs, OcrReaderViewModel.cs, SmartBotViewModel.cs,
  the two big .axaml (OcrReaderView, SmartBotView), build/test/publish, RUN-LOG.

AGENT 1 — Safety/FocusGate:
  OWNS (new): FocusGate.cs, InputRouter.cs (new chokepoint), FocusGate tests.
  AUDITS (read-only, reports to main): every input send site, panic path.
  Deliver: FocusGate + router as new files; a list of every scattered send for main to reroute.

AGENT 2 — OCR/HP-SP:
  OWNS (new): PercentText reader/parser refinements as new methods, digit-matcher DESIGN doc (not impl tonight), OCR engine-stability audit.
  AUDITS: all HpPercent/SpPercent writers (produce the "is bar-fill dead?" proof for main), all health consumers use TryGetTrustedNumber.
  Deliver: dead-writer proof + patch list; engine single-state proposal.

AGENT 3 — Smart Bot combat/state:
  OWNS (new): StateMachine.cs (new), kill-confirm + target-hold logic as new methods, timing auto-formula helpers.
  AUDITS: current loop for the branch that reselects targets every tick.
  Deliver: state machine + target-lifecycle patch proposal for main to wire into SmartBotEngine.

AGENT 4 — UI/UX compaction:
  OWNS: NEW beginner/advanced style resources, Auto-pill control, nav flag to hide 4RTools/ro-tools, copy/label changes in SMALL leaf views (AutopotView, StatsView, RolePalette).
  AUDITS + PROPOSES (does NOT directly edit the two big .axaml — hands diffs to main): SmartBotView, OcrReaderView compaction.
  Deliver: proposed axaml diffs + new style files.
```

**Conflict hotspots (Agent Q4)** = exactly the frozen files. Because only main edits them, agents never collide. Agents work on **new files + audits + proposed diffs**; main serializes application.

**Do NOT parallelize (Agent Q10):** anything editing LiveStats, the input chokepoint, the two big VMs/views, or the state machine wiring — those are one-coherent-design changes and belong to main, informed by agent proposals.

**Agent Q6 (a QA-only agent)?** No separate agent; QA is main's Definition-of-Done (§2.3) run after each merge. **Agent Q7 (docs-only agent)?** No; docs are cheap and belong with each agent's handoff + main's RUN-LOG.

---

## 5. Handoff format (Agent Q8 — use exactly this)
Every agent returns:
```
AGENT: <n> <name>
FILES INSPECTED: <paths>
FILES CREATED (owned): <paths>
PROPOSED DIFFS FOR MAIN (shared files): <unified diff blocks>
FINDINGS: <dead code / risks / contract mismatches>
TESTS TO RUN: <exact commands>
EVIDENCE: <build/test output, DebugTrace lines>
RISKS: <what could break>
DO NOT TOUCH: <files this agent must not have edited>
CONTRACT IMPACT: none | proposes-change-to <contract> (main decides)
```

## 6. Merge order (Agent Q9 — main applies in this order)
```
1. Agent 1 Safety (FocusGate + router)     — everything else depends on the gate existing.
2. Agent 2 OCR dead-writer proof + trusted  — makes health safe before the bot uses it.
3. Agent 3 state machine + target hold      — wires onto the now-safe health/gate.
4. Agent 4 UI compaction                    — cosmetic-last, lowest risk, rebases on the above.
After each merge: build + tests + append RUN-LOG. If red, stop and fix before next merge.
```

## 7. What each agent must be TOLD (paste into each spawn)
- The scope guard (§ top). Read `CONTRACTS.md` first; treat it as truth; do not edit frozen files — propose diffs.
- Your ownership block from §4. Stay in it.
- Definition of Done (§2.3): evidence, not assertion.
- Return the §5 handoff format. Nothing merges without build+test evidence.

---

## 8. Added scope — timing units + visible work-area (Vivi request, 2026-07-13)

Three concrete UI/behavior items. All are legitimate UI/observability work.

### 8.1 All timer boxes in milliseconds (not seconds)
Every timing control uses **ms** consistently. From the screenshot, `Stuck seconds` (8) and `Focus kill sec` (3) are in seconds while `Walk delay ms` (1000) and `Next monster ms` (3000) are in ms — mixed units are a bug and a footgun.
- Convert `StuckSeconds` → `StuckMs`, `FocusKillSeconds` → `FocusKillMs` (label "Stuck (ms)", "Focus kill (ms)").
- **Migration:** old saved profiles store seconds. On load, detect the old key and multiply by 1000 once (write a migration flag so it never double-converts). Don't silently reinterpret `8` seconds as `8` ms — that would make the bot unstuck-teleport almost instantly.
- Keep `-1 = Auto` semantics per box (Auto pill), unit label only applies to manual values.
- Owner: **Agent 4** (labels/pills) proposes the axaml; **main Codex** does the field rename + migration in `SmartBotViewModel`/`SmartBotEngine` (shared/contract files).

### 8.2 "Show work area" button — reveal where Smart Bot operates
The walking/attacking region is currently invisible, so the user can't see what the bot sees. Add a toggle button **"Show work area"** (Smart Bot + OCR tabs) that draws the bot's active regions as an overlay on the client:
- **Combat/scan region** (where monster detection + attacking happens).
- **Roam/walk region** (where the bot wanders when no target).
- Uses the SAME client-attached overlay path as OCR markers (`GetClientRect` → `ClientToScreen`), so it tracks/resizes with the game and respects the FocusGate (dim + "paused — focus game" when `CanAct` is false).
- Off by default; when on, draws labeled translucent rectangles (distinct colors per region) with the pixel dims shown in a corner label.
- Owner: **Agent 4** (button + overlay draw, new leaf code); the region *values* come from the combat-region source that **Agent 3** owns (see 8.3). Agent 4 consumes, Agent 3 provides.

### 8.3 Slider + live numeric box + real-time region (two-way bound)
For each adjustable region/size (combat region, roam region, and any box-size setting), the control is a **slider paired with a numeric box**, both bound to the same value, updating each other live, and the on-screen work-area rectangle (8.2) redraws **in real time** as the user drags:
```
[----o------]  [ 640 ] px      <- slider and box are two-way bound
   drag  <->  type              <- moving either updates the other AND the overlay
```
- Slider move → box number updates → overlay rectangle resizes instantly (no Apply needed).
- Box type → slider position updates → overlay resizes.
- Show min/max on the slider; clamp the numeric box to the same range.
- This is the "show the user the exact box the Smart Bot is working on" requirement: what they set is what's drawn, live.
- Owner: **Agent 4** builds the reusable `SliderWithBox` control + live overlay binding; **Agent 3** exposes the region model (position + size) as an observable the overlay binds to and the bot reads from — one source, so the drawn box and the box the bot actually uses are guaranteed identical (never a "shows one thing, does another" mismatch).

**Definition of Done for §8:** manual timing values round-trip in ms with a one-time migration verified by a test; toggling "Show work area" draws region rectangles that track the client on move/resize and hide/dim on focus loss; dragging a slider moves the number box AND resizes the on-screen rectangle in the same frame; the rectangle drawn equals the region the bot scans/roams (assert they read the same value). Evidence: a short screen capture note + the `[WorkArea] combat=x,y,wxh roam=x,y,wxh` DebugTrace line.

---

## The night in one paragraph
Freeze the five contracts into `CONTRACTS.md` so Claude and Codex share one truth. Then, with four audit-and-propose agents owning isolated/new files and main Codex as the sole editor of the frozen shared files, land the two safety gates that must never be deferred — no input leaves unless the selected client is foreground (one router chokepoint), and no flee/pot/teleport fires on a non-Trusted stat. Interleave the cleanup kill list so no concept keeps two live paths. Hold one target to death, add the minimal state machine, force client-window capture, compact the UI to Beginner-by-default, hide the legacy shells (keep their reader as an internal service), then build one `4rVivi.exe`. Defer the digit-matcher, calculator deep-wiring, and passive multi-client to after one real test run. Prove every claim with a build, a test, and the exact DebugTrace line.

*(Scope note restated: FocusGate and the input router are safety + observability. No anti-cheat/foreground-spoof/virtual-HID-bypass work is authorized in this run.)*
