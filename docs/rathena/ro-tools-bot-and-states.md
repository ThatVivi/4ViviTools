# ro-tools — Character States & Combat Bot

> **Re-extract:** in `ro-tools-main/`: `grep -nE "STATE_" service/bot_combat_rules.py` ; `grep -oE "BOT_[A-Z_]+|AUTO_[A-Z_]+|FLY_WING[A-Z_]*" service/config_file.py | sort -u`.

## Character state codes (the key signal)
ro-tools reads the char's **state** (a small int from memory) and drives everything off it
(`service/bot_combat_rules.py`):

| Code | State | Meaning |
|------|-------|---------|
| 0 | `STATE_IDLE` | standing, doing nothing |
| 1 | `STATE_WALKING` | moving |
| 2 / 9 | `STATE_ATTACKING` / `_ALT` | attacking |
| 4 | `STATE_TAKING_DAMAGE` | being hit |
| 5 | `STATE_LOOTING` | picking up loot |
| **6** | **`STATE_SITTING`** | **sitting** (this is what Auto-Stand watches) |
| 7 | `STATE_DELAY` | post-action delay |
| 18 | `STATE_CASTING` | casting |

Sets: `ATTACKING_STATES={2,9}`, `BUSY_COMBAT_STATES={2,9,5,7,18}`, `PASSIVE_STATES={0,1,6}`.

**Connection to our project:** 4RTools/ro-tools read this from memory; **we read it via OCR** — the
character-motion box (`CharMotion`) or a "Posture/State" text box. `STATE_SITTING=6` is exactly the
trigger our `AutoStandEngine` replicates (see [original-ro-tools.md](original-ro-tools.md)). Mapping
the motion box to these states is the path to full state-aware automation.

## Combat bot config (`service/config_file.py`, `events/bot_event.py`)
A full state-machine combat bot. Key tunables:

| Group | Keys |
|-------|------|
| Core | `BOT_ENABLED`, `BOT_TOGGLE_HOTKEY`, `BOT_ATTACK_RANGE`, `BOT_ATTACK_DELAY`, `BOT_WALK_STEP_CELLS`, `BOT_CHASE_CLICK_RATIO` |
| Target | `BOT_TARGET_PRIORITY`, `BOT_MOB_PRIORITY`, `BOT_MOB_CONFIG`, `BOT_TARGET_REFRESH_*` (hotkey/interval), priorities `BOT_P_HIGH/MEDIUM/LOW/IGNORE/FLEE` |
| Kite / flee | `BOT_FLEE_DISTANCE`, `BOT_FLEE_OPPOSITE_TIME`, `BOT_LONG_RANGE_KITE`, `BOT_LONG_RANGE_MIN_CELLS` |
| Anti-KS | `BOT_KS_PLAYER_RADIUS` (skip mobs near other players) |
| AoE | `BOT_AOE_ACTIVE`, `BOT_AOE_KEY`, `BOT_AOE_RADIUS`, `BOT_AOE_MOB_COUNT`, `BOT_AOE_COOLDOWN`, `BOT_AOE_SKILLS` |
| Fly-wing | `BOT_FLY_WING_HP_ACTIVE`, `BOT_FLY_WING_HP_PERCENT`, `BOT_USE_FLY_WING`, `BOT_USE_FAST_FLY_WING` |
| Stuck | `BOT_STUCK_TIMEOUT`, `BOT_STUCK_ACTION`, `BOT_RANDOM_WALK_TIME` |
| Timings | `BOT_TIMING_*` (attacking switch, idle-fly, no-attack-confirm, corner-clamp, post-fly guard, …) |

## Combat loop (simplified)
`idle → pick target (priority, skip KS/ignore) → walk into range → attack (or AoE if ≥ mob count) →
kite/flee if too close or long-range → fly-wing if HP ≤ % → loot → repeat`. Uses
`bot_pathfinder` (A* over `map_gat` cells) + `bot_patrol` for movement, and `bot_combat_actions`
for the attack/skill sends.

## What ports to our OCR app — and what doesn't
- **Portable now:** the config/tuning UI, HP-based fly-wing (we read HP via OCR), attack-key spam,
  state-aware auto-stand/idle handling (via the motion/posture box → states above).
- **Not portable without more sensing:** target selection, ranging, kiting, anti-KS, AoE-on-count, and
  pathfinding all need **entity positions + map cells** — which come from memory or screen vision, not
  HP/SP OCR. A real combat bot needs a vision/memory layer; our Bot tab exposes the config and the
  HP-driven pieces, and is honest that full auto-hunt needs that layer.
