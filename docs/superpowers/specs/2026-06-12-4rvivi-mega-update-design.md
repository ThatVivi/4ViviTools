# 4rVivi mega-update — design (approved)

Date 2026-06-12. Built in one pass on the Avalonia 2.0 base.

## Scope chosen
Smart input bot; MVP tracker, EXP/Zeny tracker, Buff/Debuff HUD, Loot log; mouseboost pot,
equip/skill switch + chain macros + auto-vend/storage; native GRF browse/extract, ACT/RSM viewers,
bundled external-editor launcher; expanded DB (mob/skill/quest/item), item-DB script editor,
damage/stat calculator, NPC snippet library, Homun/Pet AI generator. One modular app.
Declined: raw packet logger, OpenKore launcher, server @autoattack patch.

## Where each lives
Core: Automation/SmartBotEngine, TriggeredMacroEngine, ChainMacro, mouseboost in AutopotEngine;
Trackers/MvpTracker+SessionTracker+LootLog+BuffTimer; Tools/StatCalculator+NpcSnippets+HomunAiGenerator;
Grf/GrfArchive+SprReader+ModelInfo; Game/Roles+StatReader; bigger Data/gamedata.json from rAthena YAML.
App: one MVVM page per feature, nav regrouped (COMBAT/TRACKERS/MACROS/DATA/TOOLS/SYSTEM), top-bar HP/SP
bars, auto-attach, lazy DB load, scanner role dropdown.

## Honest limits (documented in README)
Input-level bot (no packets); GRF DES entries unsupported; ACT/RSM viewers are metadata-only; external
editors launched not embedded; memory addresses are user-discovered via the Scanner per profile.
