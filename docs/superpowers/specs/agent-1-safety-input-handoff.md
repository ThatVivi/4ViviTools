AGENT: 1 Safety/FocusGate/Input audit

FILES INSPECTED:
- docs/superpowers/specs/CONTRACTS.md
- docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md sections 1, 2.1, 4-6
- 4rVivi.sln
- 4RTools.csproj
- src/4rVivi.Core/Game/FocusGate.cs
- tests/4rVivi.Core.Tests/FocusGateTests.cs
- src/4rVivi.Core/Automation/AutomationEngine.cs
- src/4rVivi.Core/Automation/EngineHub.cs
- src/4rVivi.Core/Automation/AtkDefEngine.cs
- src/4rVivi.Core/Automation/AutoDebuffEngine.cs
- src/4rVivi.Core/Automation/AutopotEngine.cs
- src/4rVivi.Core/Automation/AutoStandEngine.cs
- src/4rVivi.Core/Automation/AutoYggEngine.cs
- src/4rVivi.Core/Automation/BotFarmEngine.cs
- src/4rVivi.Core/Automation/BuffEngine.cs
- src/4rVivi.Core/Automation/SkillSpamEngine.cs
- src/4rVivi.Core/Automation/SmartBotEngine.cs
- src/4rVivi.Core/Automation/TriggeredMacroEngine.cs
- src/4rVivi.Core/Input/InputMethod.cs
- src/4rVivi.Core/Input/InputRuntimeStatus.cs
- src/4rVivi.Core/Input/KeySender.cs
- src/4rVivi.Core/Input/MouseSender.cs
- src/4rVivi.Core/Input/ReWasdController.cs
- src/4rVivi.Core/Input/ReWasdMouseMap.cs
- src/4rVivi.Core/Input/ViiperInput.cs
- src/4rVivi.Core/Input/VirtualHidInput.cs
- src/4rVivi.App/ViewModels/BuffsViewModel.cs
- src/4rVivi.App/ViewModels/MainWindowViewModel.cs
- src/4rVivi.App/ViewModels/MultiClientViewModel.cs
- src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
- src/4rVivi.App/ViewModels/SmartBotViewModel.cs
- src/4rVivi.App/Services/SmartBotTrainingRecorder.cs
- Legacy raw input files: Utils/Interop.cs, Model/AHK.cs, Model/ATKDEFMode.cs, Model/Autobuff.cs, Model/Autopot.cs, Model/AutoRefreshSpammer.cs, Model/DebuffsRecovery.cs, Model/Macro.cs, Model/StatusRecovery.cs, Features/AdvancedAutopot.cs, Macros/MacroRecorder.cs, Bot/BotPrimitives.cs

FILES CREATED (owned):
- docs/superpowers/specs/agent-1-safety-input-handoff.md

PROPOSED DIFFS FOR MAIN (shared files):

1. Add a real router/chokepoint and make EngineHub expose it. Exact implementation can reuse existing KeySender/MouseSender backend code, but CanAct must be checked once in InputRouter, not separately in KeySender and MouseSender.

```diff
diff --git a/src/4rVivi.Core/Automation/EngineHub.cs b/src/4rVivi.Core/Automation/EngineHub.cs
@@
     public FocusGate FocusGate { get; }
+    public IInputRouter Input { get; }
@@
         FocusGate = new FocusGate(session);
-        _keys.FocusGate = FocusGate;
-        _mouse.FocusGate = FocusGate;
+        Input = new InputRouter(session, FocusGate, _keys, _mouse);
```

```diff
diff --git a/src/4rVivi.Core/Input/KeySender.cs b/src/4rVivi.Core/Input/KeySender.cs
@@
-    public FocusGate? FocusGate { get; set; }
@@
-    private bool CanAct(string action)
-    {
-        if (FocusGate == null || FocusGate.CanAct(out _))
-            return true;
-        DebugTrace.Write("Input", $"Key action blocked by FocusGate action='{action}'.");
-        InputRuntimeStatus.SetLastKeyboard("Paused: focus RO client");
-        return false;
-    }
+    // Backend only. InputRouter owns FocusGate checks and blocked logging.
```

```diff
diff --git a/src/4rVivi.Core/Input/MouseSender.cs b/src/4rVivi.Core/Input/MouseSender.cs
@@
-    public FocusGate? FocusGate { get; set; }
@@
-    private bool CanAct(string action)
-    {
-        if (FocusGate == null || FocusGate.CanAct(out _))
-            return true;
-        DebugTrace.Write("Input", $"Mouse action blocked by FocusGate action='{action}'.");
-        InputRuntimeStatus.SetLastMouse("Paused: focus RO client");
-        return false;
-    }
+    // Backend only. InputRouter owns FocusGate checks and blocked logging.
```

2. Route direct EngineHub input tests through the same gate. Current `TestVirtualHidKey` calls `VirtualHid.TapKey` directly, bypassing KeySender/FocusGate.

```diff
diff --git a/src/4rVivi.Core/Automation/EngineHub.cs b/src/4rVivi.Core/Automation/EngineHub.cs
@@
-        return _keys.VirtualHid?.TapKey(key, 80) == true;
+        return Input.Tap(key, 80).Sent;
```

3. Remove automatic focus changes from normal send backends. FocusGate already proves the selected client is foreground before action. These calls are unnecessary and create a race where a sender can pull focus after the gate check.

```diff
diff --git a/src/4rVivi.Core/Input/KeySender.cs b/src/4rVivi.Core/Input/KeySender.cs
@@
-            SetForegroundWindow(hWnd);
             InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {key}");
@@
-                SetForegroundWindow(hWnd);
                 InputRuntimeStatus.SetLastKeyboard($"keybd_event {KeyName.FromVk(vk)}");
@@
-                SetForegroundWindow(hWnd);
                 InputRuntimeStatus.SetLastKeyboard($"SendInput {KeyName.FromVk(vk)}");
```

```diff
diff --git a/src/4rVivi.Core/Input/MouseSender.cs b/src/4rVivi.Core/Input/MouseSender.cs
@@
-        SetForegroundWindow(hwnd);
-        Thread.Sleep(20);
         HumanMoveTo(screenX, screenY);
```

```diff
diff --git a/src/4rVivi.Core/Input/VirtualHidInput.cs b/src/4rVivi.Core/Input/VirtualHidInput.cs
@@
-    public void FocusWindow(IntPtr hwnd)
-    {
-        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
-    }
+    public void FocusWindow(IntPtr hwnd) { }
```

4. Close app-side bypasses by replacing standalone sender instances with the hub/router. `BuffsViewModel` and `MultiClientViewModel` currently instantiate `KeySender`/`MouseSender` without FocusGate.

```diff
diff --git a/src/4rVivi.App/ViewModels/BuffsViewModel.cs b/src/4rVivi.App/ViewModels/BuffsViewModel.cs
@@
-    private readonly KeySender _keys = new();
@@
-            _keys.Tap(_session.WindowHandle, KeyName.ToVk(b.Key), 20);
+            _hub.Input.Tap(b.Key, 20);
```

```diff
diff --git a/src/4rVivi.App/ViewModels/MultiClientViewModel.cs b/src/4rVivi.App/ViewModels/MultiClientViewModel.cs
@@
-    private readonly MouseSender _mouse = new() { Method = InputMethod.PostMessage };
-    private readonly KeySender _keys = new() { Method = InputMethod.PostMessage };
+    // Keep multi-client OCR read-only until foreground-safe routing exists for a selected client.
@@
-            _keys.Tap(client.WindowHandle, KeyName.ToVk(client.SkillKey), 25);
+            return;
@@
-        _mouse.Click(client.WindowHandle, target.Cx, target.Cy);
+        return;
@@
-        _keys.Tap(client.WindowHandle, KeyName.ToVk(client.BuffKey), 35);
+        return;
```

5. Add focused router tests. Keep these as unit tests with a fake gate/backend, not live user32 input.

```diff
diff --git a/tests/4rVivi.Core.Tests/FocusGateTests.cs b/tests/4rVivi.Core.Tests/FocusGateTests.cs
@@
+    [Fact]
+    public void Router_returns_not_sent_when_can_act_is_false()
+    {
+        // fake gate: CanRead true, CanAct false
+        // assert no backend call, result NotSent, blocked reason logged/statused
+    }
+
+    [Fact]
+    public void Panic_stop_does_not_send_gameplay_input()
+    {
+        // call DisableAll/StopAll path and assert fake input backend saw no calls
+    }
```

FINDINGS:
- FocusGate itself matches the frozen contract shape: `CanRead` is attached/window-valid/not-minimized/rect-valid, and `CanAct` additionally requires selected process PID equals foreground PID. It logs throttled snapshots once per second.
- There is no `InputRouter` or `IInputRouter` in active source. The current gate is split between `KeySender.CanAct` and `MouseSender.CanAct`, so the code does not yet satisfy the "one router, one CanAct check" input chokepoint contract.
- EngineHub mostly centralizes active automation input: Autopot, buffs, spammer, bot farm, Smart Bot, macros, ATK/DEF, AutoStand, AutoYgg, and AutoDebuff all share the `_keys`/`_mouse` instances created in `EngineHub`, and EngineHub attaches the single `FocusGate` to those sender instances.
- `BuffsViewModel.RunBuffSequence` creates its own `new KeySender()` and calls `_keys.Tap(...)` without `FocusGate`. This is an active app-side bypass of the current gate.
- `MultiClientViewModel` creates standalone `MouseSender`/`KeySender` with `InputMethod.PostMessage` and no `FocusGate`, then sends skill keys/clicks/buff keys to unfocused clients. This directly conflicts with the current FocusGate contract for gameplay input. Keep multi-client OCR read-only or disable its input branch until main provides foreground-safe selected-client routing.
- `EngineHub.TestVirtualHidClick` and `TestViiperInput` mostly use sender paths, but `EngineHub.TestVirtualHidKey` directly calls `_keys.VirtualHid?.TapKey(key, 80)`, bypassing `KeySender.CanAct`.
- `KeySender.Up` has no CanAct check. This is usually reached after a gated `Down`, but if any caller uses `Down`/`Up` separately and focus changes between them, key-up can still leave after focus loss. A router should own atomic tap/down/up semantics or recheck every emitted event.
- `KeySender` and `MouseSender` call `SetForegroundWindow` after the gate check on SendInput/mouse_event fallback paths. Since `CanAct` already requires the selected client to be foreground, this should be removed or limited to a user-clicked "Focus client" command. It is a legitimate safety concern, not something to solve with foreground spoofing.
- `VirtualHidInput.FocusWindow` wraps `SetForegroundWindow` and is called from `KeySender.TryVirtualHidTap`. Same recommendation: remove automatic focus changes from send paths.
- ViGEm, FakerInput/vmouse, and VIIPER backend code is currently behind `MouseSender`/`KeySender` for EngineHub-owned automation, except for the direct test-key bypass above. No changes should be made to driver internals for this safety pass.
- Panic/stop path is safe by intent: F12 via window keydown, `RegisterHotKey`, and `GetAsyncKeyState` fallback calls `MainWindowViewModel.StopAll`, which disables EngineHub features, clears SmartBot enabled state, turns Master off, and sends no gameplay input.
- Smart Bot start/stop/toggle hotkeys do not send direct gameplay input. Starting while unfocused depends on the sender gate to block subsequent automation; this is OK only after the app-side bypasses and router contract are fixed.
- Root legacy `4RTools.csproj` is not included in `4rVivi.sln`, but it still compiles many raw input files if built directly: `Model/AHK.cs`, `Model/ATKDEFMode.cs`, `Model/Autopot.cs`, `Model/Macro.cs`, `Macros/MacroRecorder.cs`, `Bot/BotPrimitives.cs`, and others. Main should either leave it out of release paths or clearly mark it legacy/not safety-gated.
- `4rVivi.sln` Release build is green despite the wiring gaps. The risk is behavioral/contract compliance, not current compilation.

TESTS TO RUN:
- `dotnet build 4rVivi.sln -c Release`
- `dotnet test 4rVivi.sln -c Release --no-build`
- Add/run router tests proving `CanAct=false` returns NotSent and makes zero backend calls.
- Add/run tests for `EngineHub.TestVirtualHidKey`, `BuffsViewModel.RunBuffSequence`, and MultiClient input being routed or disabled under `CanAct=false`.
- Manual evidence after main wiring: with RO client not foreground, press Smart Bot start and each test input button; expected DebugTrace line is `[Input] blocked reason=not-foreground ... result=NotSent`, with no key/click delivered.

EVIDENCE:
- `dotnet build 4rVivi.sln -c Release` completed successfully: 0 warnings, 0 errors.
- `dotnet test 4rVivi.sln -c Release --no-build` completed successfully: 68 passed, 0 failed, 0 skipped.
- Source evidence:
  - `src/4rVivi.Core/Game/FocusGate.cs:22-45` computes snapshot; `:48-59` exposes `CanRead`/`CanAct`; `:65-72` logs throttled focus state.
  - `src/4rVivi.Core/Automation/EngineHub.cs:47-58` creates `VirtualHidInput`, `ViiperInput`, `KeySender`, `MouseSender`, `FocusGate`, and wires gate/backends into senders.
  - `src/4rVivi.Core/Input/KeySender.cs:36-43` blocks key actions via FocusGate; raw sends live at `:149`, `:154`, `:178`, `:193`.
  - `src/4rVivi.Core/Input/MouseSender.cs:54-61` blocks mouse actions via FocusGate; raw sends live at `:145`, `:147`, `:271`, `:273`, `:281`, `:283`.
  - `src/4rVivi.Core/Automation/EngineHub.cs:196` bypasses sender/gate with direct `VirtualHid.TapKey`.
  - `src/4rVivi.App/ViewModels/BuffsViewModel.cs:16` creates standalone `KeySender`; `:56` sends with no gate attached.
  - `src/4rVivi.App/ViewModels/MultiClientViewModel.cs:51-52` creates standalone PostMessage senders; `:404`, `:408`, `:444` send input to non-foreground clients.
  - `src/4rVivi.App/ViewModels/MainWindowViewModel.cs:245-247`, `:379-382`, `:436-440` route F12 panic to StopAll only.

RISKS:
- Until an actual `InputRouter` exists, future call sites can accidentally instantiate `KeySender`/`MouseSender` without a FocusGate, as already happened in Buffs and MultiClient.
- Direct backend test methods are user-visible and can emit input without the same safety checks as automation.
- Removing automatic `SetForegroundWindow` may expose any path that was relying on sender-side focus changes instead of explicit user focus. That is the correct safety tradeoff, but needs manual testing of SendInput/mouse_event modes with the selected client already focused.
- Multi-client auto input is contract-incompatible in its current form. Disabling it may surprise users who were using it, so pair the change with a clear UI status that multi-client OCR remains available but gameplay input requires selected foreground focus.
- Legacy root `4RTools.csproj` raw input paths are outside `4rVivi.sln` but still present. They are a release/process risk if someone builds or ships the legacy project by mistake.

DO NOT TOUCH:
- Do not edit `docs/superpowers/specs/CONTRACTS.md`.
- Do not edit `docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md`.
- Do not modify VIIPER, FakerInput/vmouse, ViGEm, or reWASD backend internals for this safety pass.
- Do not add foreground spoofing, driver changes, injected-flag work, or any anti-cheat/input-inspection circumvention.
- Do not revert unrelated dirty workspace changes.

CONTRACT IMPACT:
- none. The existing FocusGate and Input Chokepoint contracts are still valid; this audit proposes implementation work to satisfy them.
