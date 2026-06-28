# Elements

> **Re-extract:** `grep -vE '^\s*#|^\s*$' $RE/attr_fix.yml | sed -n '1,120p'`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/attr_fix.yml` (modifier table), `db/re/elemental_db.yml`.

## The 10 elements
Neutral, Water, Earth, Fire, Wind, Poison, Holy, Shadow (rAthena "Dark"), Ghost, Undead.

## Element levels
Monsters/armor have an element **level 1–4**. The damage modifier depends on attacker element
**and** defender element **and** defender level. Higher level = stronger weakness/resistance.

## Modifier table (attr_fix)
`attr_fix.yml` stores `t[attackerElement][defenderLevel][defenderElement] = percent`.
100 = neutral, >100 = bonus damage, <100 = resisted, 0 = immune, negative = heals (Undead/Holy on undead).

Key relationships (level 1):
- **Fire** > Earth/Undead, < Water.
- **Water** > Fire, < Wind.
- **Wind** > Water, < Earth.
- **Earth** > Wind, < Fire.
- **Holy** > Undead/Shadow, heals Holy.
- **Shadow** > Holy, immune-ish vs Undead at high level.
- **Ghost** > Ghost, neutral resists Ghost (and vice-versa).
- **Poison** > non-Holy/Undead, weak vs Holy/Shadow/Ghost/Undead.

## Best attack element
For a given defender element+level, the best attack element is the one with the highest modifier
in `attr_fix`. The tool computes this (`Elements.BestAttackElement`) and shows "Best: X (n%)".

## Tool mapping
`src/4rVivi.Core/Calc/Elements.cs` holds the full attr_fix table verbatim, plus `Modifier()`,
`TryParse()`, and `BestAttackElement()`. Used by the calculator and the MVP tracker
("what element to use vs this MVP").
