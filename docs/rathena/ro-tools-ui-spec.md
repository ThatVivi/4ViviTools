# RO-Tools v1.8.0 FINAL — EXACT UI Spec (rebuild blueprint)

> **Re-extract:** `ls ro-tools-main/gui/widget/` ; read `gui/main_window.py`, `gui/central_widget.py`, the `painel_*` panels. Visual: the user's screenshots. Labels are Portuguese (PT → EN below).

Goal: our `RoToolsShellView` matches this **control-for-control**, dark, with **icons**, OCR-fed.

## Window chrome
- Top-left: GitHub avatar; **"Selecione o processo:"** (Select process) dropdown + refresh.
- Top-right: two **Atalho** (Shortcut) buttons + **power** button.
- Top tabs: **Home · Links · Debug · Settings**.

## Home
- **"Selecione a classe:"** (Select class) dropdown with class icon (default **Aprendiz** = Novice).
- Inner tab strip: **HP/SP · Stuffs · Debuff · Skill Spawmmer · Skill Buff · Equip. Buff · Auto Element · Hotkey · Macro · Utilidades**.

### HP/SP
- ☐ **"Bloquear uso em cidades?"** (Block use in towns).
- **Potions** group:
  - 🧪 **HP** [item icon][key] **[0%]**[spin] + **2 action icons** (use-on-self / use-on-party toggle).
  - 🧪 **SP** [icon][key] **[0%]**.
- **YGG** group (right): **HP** [icon][0%], **SP** [icon][0%].
- **Rédeas** (Halter Lead): "Usa automaticamente as rédeas quando andar uma quantidade X de células…" → [**N células** dropdown] + item icon + key. (Auto-mount after walking N cells.)
- **Asa de Mosca** (Fly Wing): "Configurar a tecla do item… evita o 'lock' do Auto Pot…" → item icon + key.

### Stuffs (buff items)
Dropdown of buff items grouped by category, each with **item icon + PT name**:
`APSD Potion` (Poção da Concentração/Despertar/Fúria), `Caixas` (Caixa do Ressentimento/Trovão/Sonolência/Escuridão), … Selected items get a key.

### Debuff (cure items)
Dropdown grouped `Básico`: Panacea, Poção Verde (Green Potion), Erva Verde (Green Herb) → key per item.

### Skill Spawmmer
Class skill list (icon + on + key) — spam selected skills. Filtered by the class selector.

### Skill Buff / Equip. Buff
Skill recast rows; equipment-swap buff rows (swap-in gear → cast → swap-out).

### Auto Element
Dropdown of **Equipes** (endow/element scrolls), each icon + PT name:
Equipes (Fogo/Água/Terra/Vento/Sagrado/Sombrio/Fantasma) = Fire/Water/Earth/Wind/Holy/Shadow/Ghost → key.

### Hotkey
Keybind list (action → key).

### Macro
Macro editor (sequence of keys/commands).

### Utilidades
**Auto Commands**: **"Tecla para Ativar"** (activation key) + **Commandos** textbox (one command/line) + **Rascunho** (Draft) reference panel listing `@aloottype`/`@aloottid` codes (Autoloot Types, item ids, boxes, etc.). Sends chat commands on the key.

## Our-replica mapping notes
- Memory → **OCR LiveStats** (HP/SP/name/state codes — see [ro-tools-bot-and-states.md](ro-tools-bot-and-states.md)).
- Item/skill dropdowns with icons → bind `Image` to GRF sprite; item lists from `gamedata.json` (by type/category).
- Rédeas/Asa/YGG → key+threshold engines (Ygg already built: `AutoYggEngine`).
- Auto Element "Equipes" → endow key per element (`Elements`).
- Utilidades Auto Commands → chat-command sender on a hotkey/timer.
