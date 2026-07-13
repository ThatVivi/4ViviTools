# Input and OCR Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix VIIPER/FakerInput input recovery and add OCR monster-detection diagnostics.

**Architecture:** Keep changes scoped to the existing input abstractions and OCR reader loop. VIIPER owns virtual USB stream health, VirtualHID owns FakerInput DLL compatibility, MouseSender owns backend fallback, and OcrReaderViewModel owns scan diagnostics.

**Tech Stack:** C#/.NET 8, Avalonia, VIIPER TCP API, FakerInput P/Invoke, xUnit.

## Global Constraints

- Do not auto-install drivers on app startup.
- reWASD remains optional.
- VIIPER, FakerInput/vmouse, ViGEm, then normal fallback remain the intended ordering.
- Smart Bot may only auto-click vision targets when coordinates are client-relative.

---

### Task 1: VIIPER Stream Recovery

**Files:**
- Modify: `src/4rVivi.Core/Input/ViiperInput.cs`

**Interfaces:**
- Consumes: existing `EnsureConnected`, `ClickAtScreen`, `TapKey`.
- Produces: stream health checks and verified mouse movement.

- [ ] Add stream-health validation for keyboard and mouse clients.
- [ ] Recreate bus/devices when streams are dead.
- [ ] Return false from `ClickAtScreen` when the cursor cannot reach the target.

### Task 2: FakerInput DLL Compatibility

**Files:**
- Modify: `src/4rVivi.Core/Input/VirtualHidInput.cs`

**Interfaces:**
- Consumes: existing `FindFakerInputDll`.
- Produces: only compatible FakerInput DLL paths.

- [ ] Prefer `FakerInputDll.dll` over `FakerInput.dll`.
- [ ] Validate required exports before returning a DLL path.
- [ ] Log skipped incompatible DLLs once.

### Task 3: OCR Entity Diagnostics

**Files:**
- Modify: `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`

**Interfaces:**
- Consumes: existing scan loop and `LiveScene.SetEntities`.
- Produces: low-rate DebugTrace diagnostics and corrected client-coordinate publishing.

- [ ] Add throttled diagnostics for entity scan counts and publish decisions.
- [ ] Set `sceneClientCoords` to true only for window capture.
- [ ] Keep monitor capture overlay boxes raw instead of replacing them with client tracks.

### Task 4: Verification

**Files:**
- Test: existing test projects.

- [ ] Run `dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release`.
- [ ] Run `dotnet test "D:\vs code clone 4rtool\4ViviTools\tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj" -c Release`.
- [ ] Inspect compile errors and fix only scoped issues.
