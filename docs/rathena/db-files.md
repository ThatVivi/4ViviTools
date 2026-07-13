# db/re Files — What Each Holds

> **Re-extract:** `ls $RE/*.yml $RE/*.txt`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Only mechanics-relevant files are listed. ✅ = consumed by the tool today, ◻ = available for later.

| File | Contents | Used |
|------|----------|------|
| item_db_equip.yml | Weapons/armor: type, subtype, locations, jobs, ATK, MATK, DEF, weapon level, slots, **Script** | ✅ |
| item_db_etc.yml | Cards (+ etc items): location, **Script** | ✅ |
| item_db_usable.yml | Consumables: foods, potions, boxes (buff items) | ✅ |
| item_combos.yml | Multi-item set bonuses | ✅ |
| item_randomopt_db.yml | Random options / enchants + scripts | ✅ |
| item_enchant.yml | Enchant slot/grade rules (which options, costs) | ◻ |
| item_reform.yml | Item reform (transform gear) | ◻ |
| laphine_upgrade.yml / laphine_synthesis.yml | Laphine enchanting | ◻ |
| item_group_db.yml | Random box → item pools | ◻ |
| item_noequip.txt | Equip restrictions per map type (WoE/PvP/PvM) | ◻ |
| mob_db.yml | Monster stats, element, race, size, MVP, drops | ✅ |
| mob_skill_db.yml | Monster skill usage (AI) | ◻ |
| skill_db.yml | Skill names, cast/delay/cooldown | ✅ (names) |
| skill_tree.yml | Job → learnable skills | ◻ (next: class-filtered skills) |
| attr_fix.yml | Element modifier table | ✅ |
| elemental_db.yml | Elemental summon stats | ◻ |
| refine.yml | Refine bonus per weapon/armor level | ✅ |
| enchantgrade.yml | Enchant grade ATK multiplier on refine | ◻ |
| job_basepoints.yml | Base HP/SP per job/level | ◻ |
| job_aspd.yml | ASPD weapon delay per job | ◻ (next: ASPD) |
| job_stats.yml | Per-job stat bonuses | ◻ |
| level_penalty.yml | Damage penalty by level gap vs monster | ◻ |
| size_fix (in source) | Weapon size modifier | ◻ |

## Notes on relevance
- **Outgoing damage** needs: item_db_equip, item_db_etc (cards), item_combos, item_randomopt,
  refine, attr_fix, mob_db, skill ratios. Most are wired.
- **Character sheet accuracy** (HP/SP/ASPD) needs job_basepoints + job_aspd.
- **WoE/PvP/PvM differences** come from `battle_config` + map flags + item_noequip; modeled later.
