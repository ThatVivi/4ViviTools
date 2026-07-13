# Bot + OCR Merge — Design Spec

Date: 2026-06-27
Project: 4ViviTools (Avalonia 11 / .NET 8 Ragnarok Online companion)
Status: Approved for planning

## 1. Goal

Merge the OCR Reader and Smart Bot into a single **Bot** tab that hard-attaches to the
selected game client, reads only the things the tool needs (priority fields), detects monsters
/ skills / text / movement behind independent opt-in toggles, and drives the client with trusted
OS input so it plays like a real player. Move the app navigation from a left rail to a top bar so
the merged tab has the full width.

## 2. Non-goals / honest ceiling

- **No packet injection, no memory writes, no client patching.** Bannable and out of scope.
  Input ceiling = `SendInput` / `SetCursorPos` (HardwareClick) + synthesized key taps the client
  accepts as genuine. "Hard attach" = window-tracked overlay + continuous capture + trusted input.
- **No reading everything on screen.** OCR is restricted to the priority plan + opt-in detectors.
- No new ML training in this spec (uses the already-trained Paddle text, icon embedder, YOLO).

## 3. Priority fields (the only things OCR reads by default)

Ordered: CharName, Class, HP, MaxHP, SP, MaxSP, BaseLevel, JobLevel, Weight, BaseEXP bar,
JobEXP bar, Character movement. Monsters, skills, and free text are SEPARATE opt-in detectors.

## 4. Shell navigation change

Move the nav `ItemsControl` from the left dock region to a top horizontal strip in the main shell
(MainWindow / shell view). Content area spans full width below it. Selected-item styling carried
over. ~10 items fit 1920 wide; on narrowing, the strip scrolls horizontally (no wrap, never breaks).
"OCR Reader" entry is removed; **Bot** is the single merged entry.

## 5. Merged Bot tab layout (1920x1080 minus top nav)

- **Command strip:** Attach client · capture mode (Running capture ☑ default / Screenshot only ☐) ·
  Master ON/OFF · status (engine, capture FPS, attached process).
- **Left column — Vitals & priority reads:** live value + locked ✓/✗ per priority field;
  Mark / Calibrate (per-mark filter+sharpness) / Merge markers buttons.
- **Center column — Skills:** key grid (F1-F9, 1-9, QWERTY, ASDFGHJKL, ZXCVBNM) like the reference
  screenshot; ticking a key adds a row to the skill table.
- **Right column — Combat & Autopot:** detect toggles (Text / Monsters / Skills+buffs / Movement);
  monster rules; walk-box; auto-reconnect; autopot (8 slots); activity log.

## 6. Components & responsibilities

### 6.1 ClientAttachment (new, Core or App service)
Tracks the selected client window rect each tick; exposes rect + client size; the overlay mounts to
it. Capture modes: Running (continuous frame loop) vs Screenshot-only (single frame, no loop).
Monitor capture retained as a fullscreen fallback. Interface: `Rect? WindowRect`, `(int w,int h)
ClientSize`, `Bitmap? Capture()`, `bool Running`.

### 6.2 PriorityOcr (refactor of the current OcrReader tick)
Reads ONLY the marked priority regions per tick using each mark's stored filter/sharpness, with
stability gating, and publishes to `LiveStats`. No free-scan. Auto-exclusion rules:
(a) detectors opt-in, (b) per-detection confidence floor, (c) HUD **exclusion zone** ignored by the
monster detector, (d) unstable reads dropped. Cadence: priority reads every tick; detectors throttled
independently.

### 6.3 Detectors (4 independent, toggle-gated)
- Text: Paddle text finds (confidence floored), only when needed.
- Monsters: YOLO boxes named by icon recognizer (or floating GRF text when "GRF with names" on),
  filtered by confidence + exclusion zone -> `LiveScene` entities.
- Skills/buffs: SkillBar/BuffBar icon marks -> icon recognizer + buff timer OCR -> `LiveScene`.
- Movement: character-motion read -> `LiveStats` (drives stuck detection + "is moving").

### 6.4 Skill system
Key grid control -> each ticked key is a `SkillRow { Key, Name, Purpose, CooldownMs, Macro,
SpammerDelayMs, Enabled }`. `Purpose` enum: Attack, Buff, DebuffCure, Loot, Teleport, Return, Pot.
OCR SkillBar fills Name + cooldown; buff timers let buffs re-cast when they fall off. Engine runs
enabled rows by purpose.

### 6.5 Autopot (8 slots)
`PotRule { Trigger (HpPercent|SpPercent|HpValue), Threshold, Key, CooldownMs, Enabled }`. Runs
independent of combat. Numeric inputs sized for 4 digits, expandable.

### 6.6 Combat engine (extend SmartBotEngine; keep AutoPot/AutoBuff/AutoDebuff modular)
Per tick: reconnect-check -> autopot (HP/SP) -> buff upkeep -> select nearest valid monster (vision,
exclusion-zone filtered, per-monster rules) -> HardwareClick walk/attack -> attack-skills -> loot ->
stuck->teleport. All actions -> `BotLog`. One capture feeds all engines via `LiveStats`/`LiveScene`.

## 7. Data model & persistence

New profile-persisted config block `BotProfile`: marks, skill grid rows, monster rules, autopot
rules, walk-box, detect toggles, capture mode, exclusion zone, keys. Saved on change and on Save,
loaded on startup. Fixes the current "bot config resets on restart" gap.

## 8. Files (expected touch list)

- Shell: MainWindow / shell axaml (+ styles) — nav left->top.
- New: `BotStudioView.axaml` (+ .cs), `BotStudioViewModel.cs`, `KeyGrid` control, `SkillRow`,
  `PotRule`, `BotProfile`, `ClientAttachment`.
- Refactor: `OcrReaderViewModel` priority/detector split; `OcrService` exclusion zone + confidence
  floors; `SmartBotEngine` consume skill rows + pot rules + movement; settings model for `BotProfile`.
- Remove "OCR Reader" + old "Smart Bot" nav entries; route to Bot.

## 9. Build & verify

No build env here. All C#/XAML authored via the mount-safe path and statically verified (brace
balance, XML well-formedness, compiled-binding member existence, signature cross-checks). Vivi runs
one `dotnet build 4rVivi.sln -c Release`; issues fixed from the log. Each subsystem in its own files
so a breakage localizes.

## 10. Risks

- Large single-shot change with no local compile -> bisect from Vivi's build log. Mitigated by
  file-level isolation per subsystem.
- Exclusion-zone + confidence thresholds need tuning against the real client; exposed as settings.
- Skill-name OCR depends on a marked SkillBar region; without it, rows still work by key (name blank).
