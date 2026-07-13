# OCR Data Refresh from rAthena

This project can refresh a Ragnarok-specific OCR vocabulary directly from upstream rAthena data.
Use it before recognition fine-tuning, dictionary expansion, or hard-example cleanup.

## Refresh command

Run from the repo root:

```powershell
& "C:\Users\Vivi\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" tools\ocr-train\fetch_rathena_corpus.py --offline-ok
```

The script writes files into `tools/ocr-train/corpus/`.

## Generated files

| File | Use |
|------|-----|
| `rathena_monsters.txt` | Monster-name OCR, target-name correction, attack-bot target validation. |
| `rathena_items.txt` | Gear, usable item, ammo, pot, and picker suggestions. |
| `rathena_skills.txt` | Skill picker suggestions, bot skill slots, calculator skill matching. |
| `rathena_status.txt` | Buff/debuff names and status-icon text support. |
| `rathena_jobs.txt` | Class-name OCR and calculator class picker suggestions. |
| `rathena_maps.txt` | Map OCR and Discord RPC map context. |
| `rathena_hud.txt` | Common HUD words that show up around RO UI marks. |
| `rathena_ocr_words.txt` | Merged vocabulary for training/research. Do not use this whole file for monster-only correction. |
| `rathena_sources.json` | Source URLs, refresh timestamp, generated counts, and failures. |

Latest local refresh: `2026-06-29T18:25:13+0300`.

Counts from that refresh:

| Bucket | Count |
|--------|------:|
| Monsters | 4,334 |
| Items | 50,566 |
| Skills | 5,300 |
| Status | 1,915 |
| Jobs/classes | 172 |
| Maps | 2,580 |
| HUD | 42 |
| Merged | 64,351 |

## Sources

The refresh script reads these upstream rAthena files:

- `db/re/mob_db.yml`
- `db/re/item_db.yml`
- `db/re/item_db_equip.yml`
- `db/re/item_db_etc.yml`
- `db/re/item_db_usable.yml`
- `db/re/skill_db.yml`
- `db/re/status.yml`
- `db/re/job_stats.yml`
- `db/re/job_basepoints.yml`
- `db/re/job_aspd.yml`
- `db/map_index.txt`

The URLs used for the exact run are recorded in `tools/ocr-train/corpus/rathena_sources.json`.

## How to use it for faster and more reliable OCR

1. Use `rathena_monsters.txt`, not the merged list, for monster-name correction. The merged file is too broad and can make a monster read snap to an item or skill.
2. Use `rathena_skills.txt`, `rathena_items.txt`, and `rathena_jobs.txt` for searchable pickers. These improve user setup without forcing typing.
3. Keep collecting hard examples from the app when OCR sees monster labels as numbers. The best retraining set is: real bad crop, expected monster name, active preprocess profile, and OCR role.
4. Train with role-focused samples first: monsters, map names, class names, HP/SP digits, and short HUD terms. Long item names are useful for pickers and optional training, but they should not dominate the monster OCR model.
5. After each refresh or training run, test against captured screenshots before shipping. The correction dictionary can hide small OCR errors, but it should not mask wrong-role detection.

