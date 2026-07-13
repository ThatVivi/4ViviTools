# 4ViviTools — User Guide (start here)

A friendly, step-by-step guide. You don't need to be a programmer. Follow the parts in order.

---

## 1. What you need to download

| # | Thing | Why | Where |
|---|---|---|---|
| 1 | **4rVivi.exe** (the tool) | the app itself | your build/release folder, usually `src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0\publish\4rVivi.exe` |
| 2 | **Your Ragnarok client** | the game + its `data.grf` | you already have it |
| 3 | **NVIDIA driver** (latest) | GPU acceleration | nvidia.com |
| 4 | **CUDA Toolkit 12.9** | GPU OCR (fast) | developer.nvidia.com/cuda-downloads |
| 5 | **cuDNN 9.x for CUDA 12** | GPU OCR | developer.nvidia.com/cudnn-downloads |
| 6 | **Visual C++ Redistributable x64** | required runtime | Microsoft |
| 7 | *(optional)* GRFEditor / ActEditor / Nemo | advanced GRF editing | rАthena tools |

If you don't have an NVIDIA GPU, skip 3–5 — the tool still works on CPU (a bit slower).

---

## 2. First run

1. **Right-click `4rVivi.exe` → Run as administrator.** (The tool needs this to read the game.)
2. Go to **Settings** and set:
   - **Game folder** — your RO client folder (the one with `data.grf`).
   - **GRF path** — usually your main `data.grf` (the big one, not `gem.grf`).
3. Open your Ragnarok client and log in.
4. In the tool, top bar → **Process** → pick your RO window → **Refresh**. It should say "Attached to …".

---

## 3. Turn on GPU OCR (optional but recommended)

1. Install downloads 3–6 above. Restart Windows after.
2. In the tool → **OCR Reader** → set **runtime = CUDA**.
3. The status line should read **`engine: PaddleOCR runtime CUDA`**.
   - If it says **CPU** or **worker unavailable**: close the tool fully and reopen it; make sure CUDA/cuDNN finished installing.

---

## 4. OCR Reader — reading your screen

The OCR reads your HP/SP/name/position off the screen so the bot and Discord know what's happening.

1. Open **OCR Reader**.
2. Leave **Monitor capture UNCHECKED** (the tool reads the game window directly — more reliable).
3. Click **Verify and calibrate** and mark each field once (HP, SP, name, etc.) if prompted.
4. Tick **Monsters** to see monster boxes; **Text** for stats.
5. Press **F9** to toggle OCR, **F8** to toggle the overlay (boxes on screen).

If boxes flicker or look wrong, that's expected to keep improving — see Troubleshooting.

---

## 5. Vision Assist GRF (the accuracy booster) — optional

This makes the game itself draw a clean red box + real name on each monster, so the tool reads them perfectly instead of guessing. **Use only if your server allows custom data GRFs.**

**Build it (one time, and again when you add monsters):**
1. Close the tool and run `tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat`.
2. This creates **`tools\ocr-train\Grf\output\VisionAssistLibrary.grf`** and **`tools\ocr-train\Grf\output\VisionAssist.manifest.json`**.
3. Open **VisionGrfPicker** from `tools\VisionGrfPicker\publish\VisionGrfPicker.exe`, or build it with `tools\VisionGrfPicker\build_picker.bat`.
4. Load `VisionAssistLibrary.grf`, pick the monsters you want marked, then press **Apply**. The picker promotes selected monsters from the hidden `visionassistant\` library folder into the live monster sprite folder inside the same GRF.

The app does **not** build the Vision Assist GRF while you are playing. The RO client loads the picker-edited `VisionAssistLibrary.grf`; 4ViviTools has the marker table built in and can also use `VisionAssist.manifest.json` automatically when it is beside the GRF output.

**Turn it on:**
5. Copy `tools\ocr-train\Grf\output\VisionAssistLibrary.grf` into your RO client folder.
6. Open your client's **`DATA.INI`** and add it as the **first** entry so it loads first:
   ```
   [Data]
   0=VisionAssistLibrary.grf
   ```
5. Start the game - you should see red boxes on monsters.
6. In the tool, open **OCR Reader** and turn on **Use Vision Assist GRF**. No manifest path is needed.

When this is enabled, the normal **Monster overlay** checkbox is turned off because the GRF is already drawing the boxes in-game. The Smart Bot can still use those GRF boxes as targets.

---

## 6. Smart Bot — auto farming

1. Open **Bot → Smart Bot**.
2. Set your **Start / Stop** hotkeys (e.g. NumPad3 / F12).
3. In **Bot action hotbar**, tick the RO keys you actually use in-game.
   - For a skill key, tick **Skill**, pick the skill name (for example **Double Strafe**), set the level/SP if needed, and leave delay at `-1` for auto.
   - For buffs, potions, Ygg, teleport, ammo, ammo bags, loot, return, or weapon keys, tick the matching box on the same key card.
   - The tool assigns the virtual-controller buttons automatically; you only choose normal Ragnarok keyboard keys.
4. Set the simple limits:
   - **Flee at HP %** (e.g. 25), **Return at weight %** (e.g. 90), **Stuck seconds** (e.g. 8), **Walk delay** (leave `-1` for auto).
5. *(Optional)* tick **Show roam box** and draw the area the bot should wander in.
6. Press **Start** (or your start hotkey). **F12 stops everything.**

How it attacks: if no specific monster filter is selected, it attacks all visible monsters. Skill attacks are cast the RO way: press the selected skill key, then click the target. If SP is too low, it normal-clicks instead. If the monster disappears or its HP is gone, it moves to the next one.

---

## 7. Autopot

1. Open **Bot → Autopot** (or the Autopot section).
2. Add a rule: **key** (e.g. F1) + **HP %** to pot at (e.g. 50). For SP potions tick **Use SP**.
3. Turn **Autopot ON**.
It only pots when your HP/SP is at or below your %, computed from your real HP/MaxHP.

---

## 8. Discord (optional)
Turn on Discord Rich Presence in Settings to show your HP/SP, map, position, class, and whether you're fighting/moving. Works automatically once OCR is reading your stats.

---

## 9. Troubleshooting

| Problem | Fix |
|---|---|
| OCR says **CPU / worker unavailable** | Close the tool completely, reopen. Confirm CUDA + cuDNN installed. |
| **Too many / phantom monster boxes** | Make sure you're on the latest build; boxes should only show on real monsters. Send `DebugTrace.log` + a screenshot to support. |
| **Red boxes don't appear in-game** (Vision GRF) | `VisionAssistLibrary.grf` must be entry `0=` in `DATA.INI`, and it must be a standard "Master of Magic" GRF. Rebuild it, then use VisionGrfPicker to Apply the monsters you want marked. |
| **Names are wrong** (Vision GRF) | The monster→name map may be off for a few sprites; rebuild, and report the wrong ones. |
| **Bot walks but doesn't attack** | Make sure OCR shows monster boxes or Vision Assist red boxes; normal attack is a left-click, and skills come from the Skill hotbar cards; check Start/Stop is actually running. |
| **Autopot spams** | Use the % rule only; make sure HP and MaxHP are being read (OCR Reader). |
| Nothing reads | Run the tool **as administrator**; re-attach the process (top bar → Refresh). |

---

## 10. Files and artifacts
- `tools\ocr-train\Grf\output\VisionAssistLibrary.grf` + `tools\ocr-train\Grf\output\VisionAssist.manifest.json` - created by `tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat`, not by the app at runtime.
- `DebugTrace.log` — the tool's log (send this when reporting a problem).
- Your profile/settings — saved automatically under `%AppData%\4rVivi`.

---

### Quick reference (hotkeys)
- **F8** overlay on/off · **F9** OCR on/off · **F12** stop everything · your **Start/Stop** bot keys are set in Smart Bot.

*Need advanced/developer detail? See `docs/CODEX-MAP.md`.*
