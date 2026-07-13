# 4ViviTools - Upgrade Status & Roadmap

Snapshot after the latest Codex pass.

## Built / Fixed

### Vision & OCR
- PP-OCRv5 worker is shipped beside the app in `OcrServer`, including PP-OCRv5 text models, YOLO `entity.onnx`, icon embeddings, labels, and map names.
- YOLO multi-class entity detection is wired end to end for monsters, loot, portals, targets, and HP-related classes.
- Monster labels are protected from numeric item-id mistakes: only monster/sprite icon-bank labels can rename monster boxes; otherwise the overlay uses the safe `Monster` fallback.
- RegionProfiles are now used in two places:
  - OCR marks still pick role-specific preprocessing/scale.
  - Live full-screen scanning now adds extra profile-tuned anchor text scans over the user's OCR marks, then deduplicates the results.
- Template matching is now wired into SkillBar/BuffBar/StatusIcons scanning. Icon cells are refined through a local normalized-correlation match before each crop is sent to the icon recognizer, with the fixed grid kept as fallback.
- Temporal voting remains wired for marked text values so one bad frame does not flip stable OCR fields.
- Auto-detected/review/manual marker flow is present in OCR Reader.

### Smart Bot & Automation
- RO attack semantics are wired: normal attack = click monster; skill attack = skill hotkey, then click monster.
- Smart Bot skill grid is wired directly into the bot skill rotation/assignment path.
- Smart Bot attack-skill picker and skill rows now show rAthena/database metadata for the selected skill: type, element, hits, cast, delay, cooldown, and suggested spam delay. The user can press `Use delay` to copy the recommended delay into the spammer delay.
- Autopot is integrated into the Smart Bot setup flow and saved with the active profile.
- ViGEm virtual-click backend is selectable, with driver status visible in Smart Bot. ViGEm is the required driver path; reWASD is optional for importing or using profiles directly.
- Smart Bot profile persistence saves and restores the bot enable state, input backend, skill grid, buff buttons, autopot toggle, monster rules, ammo/gear keys, walk box, map gate, and reconnect keys.
- Walk/roam box overlay is now drawn by the OCR overlay when `Walk only inside box` and `Show walk box overlay` are enabled.
- Multi Client OCR page can watch several running RO client windows at once through hard-attached direct client-window capture, without requiring those clients to be focused or on top. Each row has a purpose (`Main`, `Buffer`, `Watcher`); main rows can send unfocused PostMessage skill keys/clicks, buffer rows can send buff keys by interval or command, and a row can be promoted to the active Smart Bot target.

### UI / UX
- Black and Red theme variants are available.
- Navigation is reorganized into Home, Bot (Smart Bot / Multi Client / OCR Reader / Overlay / Macros), Trackers, Data, Tools, and System, with older automation layouts kept under legacy tool shells.
- Window opacity supports a wider range, 15-100.
- Manual hotkey text fields in the main bot/macro surfaces were converted to recorder controls.
- Newbie guide updated for the release executable name, OCR marker auto-detect/review/manual flow, DXGI default capture, Smart Bot setup, ViGEm-required/reWASD-optional input, and Discord RPC.

### Data / Integrations
- Calculator engine check is wired through the shared damage engine.
- Database, Calculator, and MVP tracker open Divine Pride pages.
- Discord Rich Presence is wired to startup/settings/OCR live state and uses shipped map names for map art keys.

## Current State

| System | State |
|---|---|
| OCR text reader | Working through out-of-process PP-OCRv5 worker; tuned anchors added for marked regions |
| Monster/loot/player/portal detection | Trained YOLO model shipped and live full-frame scan wired |
| Icon naming | Multi-reference icon bank shipped; monster labels guarded against numeric false names |
| Smart Bot | Vision targeting, click-to-attack, skill-then-click, walk/wait, autopot, ammo/gear, walk box persistence |
| Multi Client OCR | Watches multiple running client windows by handle; reads each client size/client area directly; clients can stay covered/off-focus; optional per-row unfocused PostMessage clicks/keys; one row can become the active Smart Bot target |
| Input delivery | SendInput, mouse/keybd_event, PostMessage, and ViGEm virtual-click backend with optional reWASD profiles |
| Discord RPC | Wired to live stats, map resolver, activity labels, and optional server button |
| Release output | Release build creates runnable `4rVivi.exe` plus `OcrServer` model assets |

## Still Worth Doing

- Add SP-cost metadata into Smart Bot suggestions if/when the imported skill database includes SP cost by level.
- Expand template matching from cell refinement into automatic skill-bar/hotkey slot discovery once real session screenshots are available.
- Add a guided real-session verification checklist after testing against the user's RO client.
- Continue UI deduplication across old 4RTools-style surfaces now that the main Smart Bot flow is cleaner.
- Consider DirectML/CUDA provider support later if CPU OCR is too slow on weaker machines.

## Verified

```powershell
dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release
dotnet test "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release --no-build
```
