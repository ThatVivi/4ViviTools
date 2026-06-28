# Weapon Size Modifier

> **Re-extract:** `grep -vE '^\s*#|^\s*$' $RE/size_fix.yml` ; `grep -n battle_calc_sizefix $SRC/battle.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/size_fix.yml`, `src/map/battle.cpp` (`battle_calc_sizefix`).

## Concept
Physical damage is multiplied by `atkmods[targetSize] / 100`, a per-weapon-type penalty vs the
target's Small/Medium/Large size: `damage = damage × atkmods[size] / 100`.

## Renewal (important)
**Renewal removes almost all size penalties** — the default is **100% for every size**, and
`size_fix.yml` only lists the few exceptions (e.g. **Knuckle** and **Whip** deal **75% vs Large**).
This is why the calculator usually shows "Weapon Size Modifier 100%".

## Classic (pre-renewal) — full penalties existed
Each weapon type had distinct %vs S/M/L, e.g.:
- Dagger 100/75/50, Sword(1H) 75/100/75, Spear 75/75/100, Axe 50/75/100, Mace 75/100/100,
  Bow 100/100/75, Katar 75/100/75, Book 100/100/50.

## Tool mapping
Calculator shows 100% (renewal default); classic-mode size penalties are a planned refinement.
Size also matters for **Size% cards** (`bonus2 bAddSize,Size_Large,n`) which apply vs the target size.
