# Task #35 — Persist bot + OCR config

**Status:** 🟡 in progress

## What it is
All OCR + bot settings survive restart.

## Done so far
OCR side DONE: OcrTogglesConfig persists detect toggles, zone/multipass/windows/ensemble/skip, confidence sliders, icon-cell, overlay size. Marks + OcrTuning already persisted.

## Next / how to continue
Bot side: add BotProfile to settings; load/save SmartBot monster rules, keys, walk-box, autopot. Build-checkpointed.

## Files
AppSettings.cs (OcrTogglesConfig); OcrReaderViewModel.cs; TODO SmartBotViewModel
