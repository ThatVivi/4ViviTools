# Icons — baked offline from the GRF (no GRF in release)

> **Re-extract / rebuild the pack:** `python tools/extract/bake_iconpack.py --kro "GRF/kRO Data/data/texture/유저인터페이스" --custom "GRF/data/texture/유저인터페이스" --res src/4rVivi.Core/Data/idnum2itemresnametable.txt --gamedata src/4rVivi.Core/Data/gamedata.json --out src/4rVivi.App/Assets/iconpack.zip`

## Principle
The GRF is **not shipped**. We take only the sprites we need and bake them into
`src/4rVivi.App/Assets/iconpack.zip` (embedded Avalonia asset). The app reads from the pack first, so
icons work **offline** with no GRF folder at runtime.

## Where icons live in the GRF
`data/texture/유저인터페이스/item/` (the "UserInterface" folder, CP949 name):
- **Item icons** — named by **resource name** (from `idnum2itemresnametable.txt`, e.g. `나이프.bmp`).
- **Skill icons** — named by **aegis** lowercase (e.g. `sm_bash.bmp`, `mg_firebolt.bmp`, `kn_bowlingbash.bmp`).
- `collection/` — large item illustrations.
Magenta (FF00FF) is the transparency key → made transparent on bake.

## Pack contents (current bake from kRO + custom GRF)
- `items/<itemId>.png` — ~7,746 item icons (id → resname → bmp).
- `skills/<aegis>.png` — ~1,309 skill icons.
- (existing `azzyai`/`job_icon` assets kept.)

## How the app resolves an icon
- **By item id**: `IconImageService.Get(id)` → pack `items/<id>.png`.
- **By item name** (`NameToIcon` converter): name → `GameDatabase.IconId(name)` → `Get(id)`.
- **By skill name** (`SkillNameToIcon` converter): name → `GameDatabase.SkillByName(name)` → aegis →
  `GetSkill(aegis)` → pack `skills/<aegis>.png`.
- **By aegis** (skill spammer rows): `GetSkill(aegis, id)` → pack, then divine-pride fallback by id.
- Fallbacks (dev only, if a sprite isn't in the pack): GRF folder (auto-detected) → divine-pride.

## Notes
- The id→resname table is **UTF-8** (our reader was fixed from CP949).
- Custom-server GRFs only contain custom items; the **base kRO `data.grf`** has the full ~10k item +
  skill icon set — bake from that for full coverage.
- The `유저인터페이스` folder is the only GRF folder needed for icons.
