# Ammo (Arrows, Bullets, Kunai, Cannonballs)

> **Re-extract:** `grep -nE "e_ammo_type|AMMO_" $SRC/itemdb.hpp` ; `grep -E 'Type: Ammo' -A3 $RE/item_db_equip.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/itemdb.hpp` (`e_ammo_type`), `item_db_equip.yml` (Type: Ammo), `src/map/battle.cpp`.

## Ammo types (`e_ammo_type`)
Arrow (bow), Dagger/Knife (throw), Bullet (revolver/rifle/gatling/shotgun), Shell/Cannonball
(grenade launcher), Kunai (Kagerou/Oboro), Throwable (shuriken), Sling item.

## Mechanics
- Ranged weapons **require** the matching ammo equipped in the **Ammo** slot; no ammo = no/weak attack.
- Ammo carries its own **ATK** and often an **element** (e.g. Fire Arrow → Fire-property attacks),
  and can have `bonus2 bAddRace`/size scripts (Hunter arrows: Iron, Silver=Holy, Stun, Status arrows).
- The **ammo element overrides** the weapon's neutral element for that attack — this is how archers
  change attack element without endow.
- Ammo ATK adds to WeaponATK; some skills consume multiple.

## Element via ammo
Arrow element is the cheapest way to hit a monster's weakness for bow users (Fire/Water/Wind/Earth/
Holy/Shadow/Ghost arrows). Equivalent to a weapon endow for ranged.

## Tool mapping
The calculator's **Attribute** (endow) field already lets you set the attack element, which covers the
ammo-element case. A dedicated Ammo slot (with ammo ATK + element + status scripts) is a planned
addition — it slots cleanly into the existing `mods`/element pipeline.
