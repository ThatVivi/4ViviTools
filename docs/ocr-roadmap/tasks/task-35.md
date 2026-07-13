# Task #35 — Persist bot + OCR config

**Status:** ✅ done

## What it is
All OCR + bot settings survive restart.

## Done so far
OCR side DONE: OcrTogglesConfig persists detect toggles, zone/multipass/windows/ensemble/skip, confidence sliders, icon-cell, overlay size. Marks + OcrTuning already persisted.
- Bot side DONE: `ProfileConfig.SmartBot` persists Smart Bot keys, skill/action hotbar cards, monster rules, walk box, controller mapping choices, input method, autopot bridge, ammo/bag settings, reconnect keys, hotkeys, timing overrides, and target map.
- `SmartBotViewModel.SaveBotProfile()` writes active-profile state; constructor/profile load rehydrates the engine and UI.

## Next / how to continue
Live-client verification: restart the tool after changing Smart Bot and OCR settings and confirm the active profile restores them.

## Files
AppSettings.cs (OcrTogglesConfig); OcrReaderViewModel.cs; TODO SmartBotViewModel
