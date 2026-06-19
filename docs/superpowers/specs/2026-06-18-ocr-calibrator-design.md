# OCR calibrator: screenshot-marked regions drive all stats (Gepard-proof)
Design spec · 2026-06-18

## Idea
Memory reading is blocked by Gepard on many servers. Instead, read the SCREEN. The user loads a
screenshot of their client, draws a labeled box over each value (HP, MaxHP, SP, MaxSP, Base Lv, Job Lv,
Weight, MaxWeight, Zeny, Base EXP, Job EXP, Name). Boxes are stored as FRACTIONS of the window
(resize-proof). When OCR is ON, a loop captures each box from the live window every ~700ms, OCRs it,
parses the number/text, and writes to a shared LiveStats store that the top bar, Stats tab and Discord
all read from. No memory access -> works under Gepard.

## Components
- Core/Ocr/OcrMark.cs : { Role, X,Y,W,H (0..1 fractions), IsText }.
- Core/Game/LiveStats.cs : static Instance; numbers+texts dict keyed by role; Active + UpdatedUtc;
  IsFresh = Active && age < 3s; TryGetNumber/TryGetText.
- AppSettings: List<OcrMark> OcrMarks (persisted).
- OcrService.ReadRect(hwnd, fx,fy,fw,fh): fraction -> absolute via GetWindowRect, capture+preprocess+OCR.
- App/ViewModels/OcrReaderViewModel + Views/OcrReaderView: load screenshot, draw boxes per selected role,
  save marks, Start/Stop continuous read, live value display.
- Consumers read LiveStats first when fresh, else memory:
  - HealthReader (top bar), StatReader (Stats tab), CharacterStateReader (Discord).

## Flow
calibrate once (load shot -> pick role -> drag box -> save) ->
turn OCR ON -> loop reads each marked region -> LiveStats -> top bar/Stats/Discord show values.

## Honest limits
- Boxes are fractions of the window; the screenshot must match the window's aspect. Re-calibrate per UI scale.
- OCR reads digits/text shown on screen; it CANNOT get map name or X/Y (RO doesn't display coords).
- Basic Info box must stay visible/unobstructed in those spots while OCR runs.
- Accuracy depends on font/region; continuous OCR uses a little CPU (~700ms cadence).
- The draw-on-screenshot canvas is interactive UI that may need tuning after first use.

## Testing
- Unit: OcrMark fraction round-trip; LiveStats freshness; parse number/text.
- Manual: calibrate, turn on, watch top bar/Stats/Discord update from screen.
