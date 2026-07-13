# Weapon Types & Dual-Wield

> **Re-extract:** `sed -n '/enum weapon_type/,/};/p' $SRC/pc.hpp` ; `sed -n '/void pc_calcweapontype/,/^}/p' $SRC/pc.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/pc.hpp` (`enum weapon_type`), `src/map/pc.cpp` (`pc_calcweapontype`, `pc_equippoint_sub`).

## weapon_type enum
`W_FIST`(0, bare), `W_DAGGER`, `W_1HSWORD`, `W_2HSWORD`, `W_1HSPEAR`, `W_2HSPEAR`, `W_1HAXE`,
`W_2HAXE`, `W_MACE`, `W_2HMACE`(unused), `W_STAFF`, `W_BOW`, `W_KNUCKLE`, `W_MUSICAL`, `W_WHIP`,
`W_BOOK`, `W_KATAR`, `W_REVOLVER`, `W_RIFLE`, `W_GATLING`, `W_SHOTGUN`, `W_GRENADE`, `W_HUUMA`,
`W_2HSTAFF` (…23). Then dual-wield virtuals and `W_SHIELD`.

## Ranged vs melee
Ranged weapons use **DEX** (not STR) in StatusATK and the weapon stat-bonus:
**Bow, Musical, Whip, Revolver, Rifle, Gatling, Shotgun, Grenade**. Everything else is melee (STR).

## Dual-wield (`pc_calcweapontype`)
If both hands hold a weapon, the result is a combined type:
- `W_DOUBLE_DD` 2 daggers, `W_DOUBLE_SS` 2 swords, `W_DOUBLE_AA` 2 axes,
- `W_DOUBLE_DS` dagger+sword, `W_DOUBLE_DA` dagger+axe, `W_DOUBLE_SA` sword+axe.
If one hand is empty → single-hand type of the other.

## Who can dual-wield (`pc_equippoint_sub`)
Off-handing a **Dagger / 1H Sword / 1H Axe** requires the `AS_LEFT` skill, or being an
**Assassin**, **Kagerou**, or **Oboro**. There is no `pc_can_switch_weapon`; weapon switching uses
`pc_equipitem(..., equipswitch=true)`.

## Tool mapping
The calculator reads each weapon's `SubType` from `item_db_equip`; if it is in the ranged set,
the engine uses the DEX branch of StatusATK. Dual-wield (`W_DOUBLE_*`) and the off-hand slot are a
planned addition.
