# 4ViviTools Runtime Contracts

Updated: 2026-07-13

This file is the current implementation contract for the overnight cleanup. Older specs are reference only when they conflict with this file.

## Health State

```csharp
HealthState {
  HpPct: int;        // 0-100, -1 = unknown
  SpPct: int;        // 0-100, -1 = unknown
  Quality: Trusted | Held | Suspect | Stale;
  Source: PercentText | Memory | Manual; // BarFill is dead for HP/SP
  AgeMs: int;
  RawText: string;
  Confidence: double;
}
```

Safety consumers must use trusted health only:

- Smart Bot
- Autopot
- AutoYgg
- Discord RPC
- Stats and top bar
- Smart Bot training recorder
- Calculator live mode

No teleport, potion, ygg, or bot safety decision may use a bare cached HP/SP number.

## FocusGate

```csharp
FocusGate {
  CanRead(): attached && capturable && !minimized && rectValid;
  CanAct(): CanRead() && selectedProcess == foregroundProcess;
}
```

OCR read/capture is not foreground-gated. It may keep reading while the user configures 4ViviTools. Input actions are foreground-gated and may only act on the selected RO client.

## Input Chokepoint

```csharp
InputRouter.Tap(key) / ClickAt(x, y) / Move(x, y)
  -> if !FocusGate.CanAct(): log blocked and return NotSent
  -> else deliver through the configured backend and log backend/latency/result
```

Panic/stop hotkeys may bypass CanAct only to stop or disable automation. They must never send gameplay input.

## Smart Bot State Machine

```text
Stopped -> WaitingForClientFocus -> WaitingForTrustedVitals -> Buffing
   -> SelectingTarget -> EngagingTarget -> ConfirmingKill -> Roaming
   -> RecoveringStuck -> SelectingTarget
Paused overlays any state.
```

The bot holds one active target through engagement and kill confirmation. It must not reselect every loop unless the target dies, disappears, times out, or becomes invalid.

## Vision Source Rule

```text
Vision Assist GRF ON:
  GRF markers are authoritative.
  Decode mob identity from the baked marker table/color code.
  Bypass YOLO, ByteTrackLite, icon bank, and OCR name guessing.
  Do not draw YOLO monster boxes.

Vision Assist GRF OFF:
  Use YOLO + ByteTrackLite + OCR/icon fallback.
```

The Smart Bot consumes the same LiveScene entity model in both modes. Only the upstream source changes.
