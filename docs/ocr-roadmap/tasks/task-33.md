# Task #33 — Key grid + OCR-filled skill table

**Status:** ✅ done

## What it is
Key grid (F1-F9,1-9,QWERTY...) like the AHK screenshot; ticking a key adds a skill-table row; OCR SkillBar fills name+cooldown; per-row macro/cooldown/purpose toggles.

## Done so far
- Smart Bot has a key-card hotbar grid for F-keys, numbers, and letters.
- Each checked key can be assigned as Skill, Buff, TP, Ygg, HP, SP, Ammo, Ammo Bag, Loot, Return, Weapon, or Reconnect.
- Skill/buff rows use searchable RO skill pickers.
- Per-key settings include SP required, skill level, skill delay, buff interval, potion threshold, reaction/use delay, ammo count, stop threshold, bag count, and ammo-per-bag.
- The tool auto-assigns non-clashing controller buttons and exposes advanced assignment editing only when requested.
- Settings persist through `SmartSkillButtonConfig` in the active Smart Bot profile.

## Next / how to continue
Live-client verification: confirm each selected hotbar key fires through the selected input stack and that the Smart Bot uses the selected skill/pot/ammo action in combat.

## Files
new: KeyGrid control, SkillRow; SmartBotView/VM
