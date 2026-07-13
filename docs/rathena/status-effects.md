# Status Effects (Buffs & Debuffs)

> **Re-extract:** `grep -cE "^\s*SC_[A-Z]" $SRC/status.hpp` ; `grep -nE "Status:|Icon: EFST_" $RE/status.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/status.hpp` (`enum sc_type`, ~1010 `SC_*`), `src/map/status.cpp` (effects), `db/re/status.yml`.

## Concept
A status change (`SC_*`) modifies stats, damage, or behaviour for a duration. ~1010 exist. Each is
applied via `sc_start`, stored in the unit's `status_change`, and read during stat/damage calc.

## Common offensive buffs
| Buff | Effect |
|------|--------|
| SC_BLESSING | +STR/INT/DEX (cures curse/stone); halved on undead/demon |
| SC_INCREASEAGI | +AGI, +ASPD, +move speed |
| SC_TWOHANDQUICKEN | +ASPD for 2H swords |
| SC_CONCENTRATION (Spear Quicken / LK) | +ATK, +crit/hit, takes more damage |
| SC_MAGNUMBREAK | +20% Fire property ATK (short) |
| SC_ENDURE | uninterruptible by hits |
| SC_PROVOKE | +ATK, −DEF on target (also self-buff variant) |

## Common debuffs (on enemy or self)
| Debuff | Effect |
|--------|--------|
| SC_POISON | HP drain, −DEF |
| SC_CURSE | −LUK, −ATK, −move speed |
| SC_STUN / SC_FREEZE / SC_SLEEP / SC_STONE | disables (frozen = +Water-element weakness) |
| SC_BLIND | −HIT/−FLEE |
| SC_SILENCE | no skills |
| SC_BLEEDING | HP drain, no natural regen |
| SC_DECREASEAGI / SC_QUAGMIRE | −AGI/−ASPD/−move |

## Element interactions
Frozen/Stone targets take extra damage and are forced to an element (Frozen → Water lvl1) — relevant
to damage vs statused enemies.

## Tool mapping
The calculator's Buffs section lets you search and add buff items/skills (effects folding into damage
is incremental). The full SC table (1010) is reference-only; offensive ones (Blessing, Magnum, etc.)
are the priority for damage modelling.
