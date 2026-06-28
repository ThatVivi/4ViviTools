# Section 7 — Production Architecture / Workers / IPC / Telemetry / Auto-Learning (Status vs Code)

Guide §7 (ocr_guide.txt lines 3122–3918). Legend: **[DONE]** · **[PARTIAL]** · **[TODO]**.

## Service / process separation
Guide target: separate processes (Capture.exe, OCR.exe, Vision.exe, Knowledge.exe, Overlay.exe, Supervisor) so one crash restarts only that worker.

| Recommendation | Status | Evidence |
|---|---|---|
| OCR runs out-of-process | **[DONE]** | `4rVivi.OcrServer/Program.cs` — persistent worker, **stdio line protocol**, isolated process so SkiaSharp 3 won't clash with host Avalonia/Skia. Host talks via `RapidOcrClient` (`_rapid` in OcrService). Restartable: `OcrService.ReloadWorker()` → `_rapid.Restart()`. |
| Vision (YOLO) in worker | **[DONE]** | `EntityDetector` + `IconRecognizer` live **inside the same OcrServer process** (`DETECT`/`ICON`/`SCAN` commands), not a separate exe — but off the host process. |
| Capture / Overlay / Knowledge / State as separate services | **[TODO]** | Capture runs in-host (GDI in `OcrService`). Overlay (`OcrOverlayWindow`), Knowledge (`OcrNameCorrector`), and game state all in the Avalonia host process. Only OCR+Vision are split out. |
| Project layout (`4rVivi.Capture/Ocr/Vision/Knowledge/State/Telemetry/Overlay/PluginHost/Shared/Tools`) | **[PARTIAL]** | Actual projects: `4rVivi.App`, `4rVivi.Core`, `4rVivi.OcrServer`, `4rVivi.Plugins.Abstractions`, `RapidOcrNet`. One OCR worker + a plugin-abstractions assembly exist; the fine-grained per-service split does not. |
| Shared contracts library (`4rVivi.Shared` with DTOs/Interfaces/Events) | **[PARTIAL]** | `4rVivi.Core` acts as the shared lib (Settings, Game, Ocr, Data). `4rVivi.Plugins.Abstractions` holds plugin contracts. No dedicated `record OcrResult(Region,Text,Confidence,Timestamp)` DTO crossing a service boundary — IPC is ad-hoc tab-delimited strings. |

## IPC
| Recommendation | Status | Evidence |
|---|---|---|
| Small messages over Named Pipes | **[PARTIAL]** | Uses **stdio pipes** (stdin/stdout line protocol), not Windows Named Pipes, but functionally the "small messages" channel. `Program.cs`: `CFG`, `ICON`, `DETECT`, `SCAN`, `QUIT`, plus image-path OCR requests → `OK\t<text>`. |
| Large data (frames) over Shared Memory / MemoryMappedFile; never send screenshots through pipes | **[TODO]** | Images are passed **by file path on disk** (worker does `SKBitmap.Decode(path)` — see `Program.cs` ICON/DETECT/SCAN). No MemoryMappedFile / shared-memory frame transport. Guide explicitly warns against the screenshot-through-pipe / disk pattern. |
| Frame Bus (one capture → many subscribers) | **[TODO]** | No frame bus. Host captures and pushes individual region paths per request. |

## OCR worker internals
| Recommendation | Status | Evidence |
|---|---|---|
| Worker contains only ONNX/PP-OCR/Windows OCR/Tesseract | **[PARTIAL]** | Worker holds RapidOcr (PP-OCRv5/ONNX) + YOLO EntityDetector + IconRecognizer. Windows OCR & Tesseract live host-side (`OcrService`), not in the worker. |
| OCR queue / backpressure (`ConcurrentQueue<OcrTask>`) | **[TODO]** | Worker is synchronous request/response over stdio (`while ReadLine`); no internal queue, no backpressure. |
| Priority queue (HP/SP/Target High, Inventory Med, Chat Low) | **[TODO]** | No `PriorityQueue<OcrTask,int>`. All marks processed in list order. |
| Per-region refresh rates (HP 20Hz, Target 10Hz, Inventory 2Hz, Map 1Hz) + `RegionDefinition{Name,Bounds,RefreshRate,Priority}` | **[TODO]** | `OcrMark`/`OcrRegion` have Name+Bounds only — no RefreshRate/Priority fields. Only a global `SkipUnchanged` gate; no per-region Hz scheduler. |

## State / overlay / events / plugins
| Recommendation | Status | Evidence |
|---|---|---|
| Central `GameState{ Map,Target,HpPercent,SpPercent,... }`; everything writes state, overlay reads only | **[PARTIAL]** | OCR results feed view-model properties bound to overlay; no single authoritative `GameState` struct that all workers update. Overlay reads VM, not an isolated state engine. |
| Overlay isolation (overlay never calls OCR) | **[PARTIAL]** | Overlay window renders VM values; OCR loop drives the VM. Not a hard process boundary but overlay doesn't invoke OCR directly. |
| Event Bus (MapChanged/TargetChanged/HpChanged/BuffAdded) | **[TODO]** | No event-bus abstraction found. |
| Plugin system (`IPlugin{ Name; Initialize(); Update(GameState) }`); features as plugins; plugins read state not OCR | **[PARTIAL]** | `4rVivi.Plugins.Abstractions` project exists (plugin contracts). Trackers (MVP/Buff) exist (`MvpTrackerViewModel`, `BuffTimer`) but as in-app view-models, not state-driven `IPlugin.Update(GameState)` modules. |

## Telemetry / auto-learning / benchmarking
| Recommendation | Status | Evidence |
|---|---|---|
| Telemetry service: OCR/capture latency, FPS, mem, GPU, queue size; `MetricsCollector` | **[TODO]** | `grep` for Telemetry/MetricsCollector finds only the **MultiPass "keep best" comment** in the ViewModel — no metrics collection. `OcrService.LastRecScore`/`LastEngine` are surfaced for UI only, not stored. |
| OCR benchmarking table in SQLite (`OcrResults`) | **[TODO]** | No SQLite OCR-results logging. |
| **Screenshot harvesting** — auto-save image+result+timestamp+region when confidence < 70% to `datasets/hard_examples/` | **[TODO]** | No `hard_examples` / `datasets/` harvest path in code. (The single Telemetry/harvest grep hit was a comment, not an implementation.) |
| Auto-learning pipeline (failure → review queue → dataset → retrain → new model) | **[TODO]** | Not present. |
| Dataset Manager (`DatasetBuilder`) | **[TODO]** | Not present. |
| Model Registry (versioned `ro_rec_v1/v2/v3.onnx`, never overwrite) | **[TODO]** | Models live under `RapidOcrNet/models/{v5,icons,yolo}` — single versions, no registry/versioning scheme. |
| Automatic model testing / benchmark suite (compare old vs new on 1000 shots, deploy if better) | **[TODO]** | Not present. |
| Frame recorder / replay engine for offline debugging | **[TODO]** | Not present. |
| Health monitoring (per-worker heartbeat) | **[TODO]** | No heartbeat. Worker liveness only inferred by `ReloadWorker`/restart on demand. |
| Watchdog / `Supervisor.exe` auto-restart of crashed workers | **[PARTIAL]** | No supervisor process. Manual recovery exists: `OcrService.ReloadWorker()` / `_rapid.Restart()` restarts the OCR worker; nothing auto-monitors or restarts on crash. |

## Tally
- **[DONE]:** 2 (out-of-process OCR worker + stdio IPC; YOLO vision in worker)
- **[PARTIAL]:** 8 (project/shared-lib split, IPC channel, worker engines, GameState, overlay isolation, plugin system, watchdog-as-manual-restart)
- **[TODO]:** ~12 (shared-mem/frame-bus, queue+priority+refresh-rate scheduler, event bus, telemetry, SQLite bench, harvesting, auto-learn, dataset mgr, model registry, model A/B, recorder, heartbeat)
