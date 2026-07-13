# Input and OCR Recovery Design

## Goal

Make the tool recover from dead virtual-input streams, show clear input-routing evidence, and expose why monster detection returns zero boxes.

## Findings

- VIIPER starts, creates keyboard and mouse devices, and opens streams, then the streams are closed by the app side. VIIPER removes the devices, but the app can still hold stale non-null stream objects.
- FakerInput probing currently accepts `FakerInput.dll`, which does not export the `fakerinput_alloc` API used by the app.
- Mouse movement reports are treated as successful after bytes are written, without checking that the cursor reached the target.
- The latest logs show VIIPER keyboard taps, but no matching mouse click requests after connection.
- Monitor capture can publish detections as client coordinates, which can confuse overlay and bot consumers.
- OCR entity scanning has too little diagnostics when it returns zero monsters.

## Design

1. VIIPER connection health must validate both streams and recreate the bus/devices if either stream is stale.
2. VIIPER mouse movement must log start, target, final cursor, elapsed time, and whether movement reached tolerance.
3. FakerInput discovery must only return DLLs with the exported functions used by `VirtualHidInput`.
4. OCR reader must log low-rate entity scan diagnostics: frame size, capture mode, counts, elapsed time, threshold, and whether tracked entities were published.
5. Monitor capture must not publish `LiveScene.ClientCoords=true`; overlay can draw raw capture boxes, while Smart Bot only clicks client-coordinate tracks.

## Acceptance

- A stale VIIPER stream causes a reconnect attempt before the next input action.
- A failed VIIPER mouse movement falls through to the next configured backend instead of claiming success.
- Incompatible FakerInput DLLs are skipped with a clear debug message.
- DebugTrace shows whether OCR got zero YOLO boxes, filtered boxes, or skipped publish due to slow scans.
- Smart Bot does not use monitor-space boxes as client-click coordinates.
