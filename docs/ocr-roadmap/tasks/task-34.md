# Task #34 — Wire smart bot to OCR (skills/pots/ammo)

**Status:** 🟡 in progress

## What it is
Bot detects monsters, uses skills, pots, ammo, driven by OCR/LiveScene.

## Done so far
Engine already consumes LiveScene (vision targeting) + LiveStats; per-monster skills, walk-box, reconnect, ammo gate, autopot exist.
- Smart Bot action hotbar syncs enabled Skill rows into `PerMonsterSkillKey`, `SkillSpRequired`, and `SkillDelayMsByKey`.
- Buff rows sync into `BuffKeys` and per-key refresh intervals.
- HP/SP/Ygg rows sync into the Autopot engine as percentage-triggered actions.
- Ammo and ammo-bag rows sync into SmartBot ammo key, ammo name, manual ammo count, stop threshold, bag count, and ammo-per-bag logic.
- OCR role wiring includes `Roles.Ammo`, HP/SP/Weight/Pos, and LiveScene target boxes.

## Next / how to continue
Live-client verification: confirm selected skill key presses before monster click, ammo bag decrements/refills manual ammo, and OCR ammo count overrides manual count when available.

## Files
SmartBotEngine.cs; EngineHub.cs
