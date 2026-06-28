# Mounts & Options

> **Re-extract:** `grep -nE "OPTION_(RIDING|DRAGON|WUG|MADOGEAR|MOUNTING|FALCON)" $SRC/pc.hpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/pc.hpp` (`OPTION_*`, `pc_isriding` etc.), `src/map/pc.cpp` (`pc_setoption`).

## Option flags (visual + mechanical state)
| Flag | Mount | Notes |
|------|-------|-------|
| OPTION_RIDING | PecoPeco (Knight/Crusader line) | +move speed; some skills require it |
| OPTION_DRAGON | Dragon (Rune Knight) | move speed; Dragon Breath related |
| OPTION_WUG / OPTION_WUGRIDER | Warg (Ranger) | Warg skills, riding |
| OPTION_MADOGEAR | Mado Gear (Mechanic) | changes skill set, fuel, no normal pots |
| OPTION_MOUNTING | generic cash mount | cosmetic + speed |

Also OPTION_FALCON (Hunter), OPTION_CART (Merchant line), OPTION_ORLEANS/… for misc states.

## Mechanical effects
- Move speed bonus while mounted.
- Some skills are **only usable while mounted** (Cavalry Mastery, Dragon Breath, Mado skills).
- Mado Gear blocks normal potions and uses magic-gear fuel; changes ASPD/weight.
- Falcon enables Hunter/Ranger falcon skills and auto-blitz.

## Tool mapping
Not currently modelled in the calculator (no damage impact beyond enabling certain skills). Relevant
later for skill availability per class/state.
