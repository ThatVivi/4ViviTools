# 4RTools v2.10.0 — EXACT UI Spec (rebuild blueprint)

> **Re-extract:** `unzip -o -j 4RTools-main.zip "4RTools-main/Forms/*.Designer.cs" -d out && grep -oE 'Text = \"[^\"]+\"' out/*.Designer.cs`. Visual: the user's screenshots.

Goal: our `FourRToolsShellView` matches this **control-for-control**, with **icons**, OCR-fed (memory → LiveStats).
Icons: **skill** icons = GRF sprite by AegisName (`IconImageService.GetSkill`); **item** icons = GRF by id/resource name.

## Window chrome (always visible, left panel)
- **Ragnarok Client**: process dropdown + refresh button. *(Ours: OCR/attach; process pick.)*
- **Profile**: dropdown (Default…).
- **Autopot / Yggdrasil** sub-tabs (top-left card):
  - *Autopot*: 🧪 **HP** [key=None][0]**%**, 🧪 **SP** [key=None][0]**%**, **Delay** [15] **ms**.
  - *Yggdrasil*: **HP** [key][0]%, **SP** [key][0]% (emergency Ygg).
- **ON / OFF** panel: big **OFF/ON** toggle button (red/green), ☑ **Sound**, **End** button, "Press the key to start!", **Character Name:** display. *(Ours: master ToggleSwitch + OCR name.)*

## Bottom tab strip (scrollable ◄►), in order
`Skill Spammer · Debuff · Autobuff - Skills · Autobuff - Stuffs · Skill timers · Macro Switch · Macro Songs · ATK x DEF Mode · Profiles · Servers`

### Skill Spammer
- Checkbox grid of keys: **F1–F9**, **1–9**, **Q W E R T Y U I O**, **A S D F G H J K L**, **Z X C V B N M** (each a ☐ key toggle).
- **Example** group (radio): ◉ With mouse click / ◯ No mouse click / ◯ Deactivated.
- **AHK Configuration** (radio): ◉ Compatibility / ◯ Speed boost.
- **Key Config**: ☐ Mouse Flick, ☐ No Shift.
- **Spammer Delay** [10] **ms**.

### Debuff
- **Status Recovery** group: 💊 **Status** [None], ☐ **Enable Auto Stand (when forced to sit)**.
- **Elvira Candy** group: 🍬 **Status** [None], **Cooldown** [0] **ms**.
- **Status Effects** group: grid of **~13 status icons**, each [None] textbox (cure-item per debuff).

### Autobuff - Skills
Grouped boxes **per class**, each a grid of **skill icon + [None]** (recast key):
`Archer · Swordsman · Mage · Merchant · Thief · Acolyte · Taekwon · Ninja · Gunslinger · Summoner · Soul Ascetic · Night Watch · Hyper Novice · Homunculus`. (Icons = GRF skill sprites.)

### Autobuff - Stuffs
Grouped boxes, each **item icon + [None]**:
`Potions · Elementals · Boxes / Speed / Status · Foods · Scroll Buffs · ETC · Candies · EXP`. (Icons = GRF item sprites.)

### Skill timers
**Skill timer 1 / 2 / 3**, each: **Delay** [5] **sec**, **Key** [None].

### Macro Switch
**Switch 1 / 2 / 3 …**, each: row of **7** **Keys** [None]→[None]→… (arrows between), **Delays(ms)** [0] under each, ☐ **Click** under each.

### Macro Songs
Two blocks; each: **Key** [None], **Delay** [50] **ms**, chain grid of None→None (5 cols) + 2 instrument rows, **Reset** button.

### ATK x DEF Mode
- **Configuration**: **Spammer Key** [None], **Spammer Delay** [10], **Switch Delay** [50], ☐ **With mouse click**.
- **DEF Switch**: column of **6** equip-icon + [None].
- **ATK Switch**: column of **6** equip-icon + [None].

### Profiles / Servers
Profile manager (add/rename/delete) and server list (add server, addresses) — ours maps to System → Servers/Settings.

## Our-replica mapping notes
- Memory reads → **OCR LiveStats** (HP/SP/name/state).
- Status detection (Debuff/Autobuff) → OCR posture/status box + EFST map (see [client-systems.md](client-systems.md)).
- Icon grids → bind `Image Source` to GRF skill/item sprite by aegis/id.
- Class skill groups → our `skillCatalog` / `ClassData` per class.
