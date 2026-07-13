AGENT: 1 Safety/FocusGate/Input audit, follow-up task 2

OBJECTIVE:
- Produce an implementation-ready InputRouter design with exact new files/interfaces and a minimal shared-file patch plan.
- Audit every `SetForegroundWindow` call in active 4rVivi.App/Core runtime and classify it.
- Do not edit shared runtime files.

FILES INSPECTED:
- docs/CODEX-MAP.md
- docs/USER_GUIDE.md
- docs/NEWBIE_GUIDE.md
- docs/PROJECT_IMPROVEMENT_PLAN.md
- docs/PROJECT_KNOWLEDGE_BASE.md
- docs/CLAUDE_SECOND_OPINION_SMART_BOT_OCR_2026-07-13.md
- docs/superpowers/specs/CONTRACTS.md
- docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md
- docs/superpowers/specs/RUN-LOG-2026-07-13.md
- docs/superpowers/specs/2026-07-13-claude-hard-attach-reply.md
- docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-reply.md
- docs/superpowers/specs/2026-07-13-claude-second-opinion-reply.md
- docs/superpowers/specs/2026-07-13-claude-second-opinion-reply-2.md
- docs/superpowers/specs/2026-07-13-hard-attached-ocr-status-and-questions.md
- docs/superpowers/specs/2026-07-13-codex-full-tool-status-for-claude.md
- docs/superpowers/specs/agent-1-safety-input-handoff.md
- docs/rathena/client-systems.md
- docs/rathena/ro-tools-bot-and-states.md
- docs/rathena/skills.md
- src/4rVivi.Core/Automation/EngineHub.cs
- src/4rVivi.Core/Game/FocusGate.cs
- src/4rVivi.Core/Input/KeySender.cs
- src/4rVivi.Core/Input/MouseSender.cs
- src/4rVivi.Core/Input/VirtualHidInput.cs
- src/4rVivi.Core/Input/ViiperInput.cs
- src/4rVivi.Core/Input/ReWasdController.cs
- src/4rVivi.Core/Input/InputMethod.cs
- src/4rVivi.Core/Input/InputRuntimeStatus.cs
- src/4rVivi.App/ViewModels/BuffsViewModel.cs
- src/4rVivi.App/ViewModels/MultiClientViewModel.cs
- src/4rVivi.App/Services/SmartBotTrainingRecorder.cs
- src/4rVivi.App/Views/OcrReaderView.axaml.cs
- Bot/BotPrimitives.cs

FILES CREATED (owned):
- docs/superpowers/specs/agent-1-task2-inputrouter-plan.md

CURRENT STATE SUMMARY:
- No `IInputRouter` or `InputRouter` file exists under `src` or `tests`.
- `EngineHub` still creates one shared `KeySender` and one shared `MouseSender`, wires `FocusGate` into both, then passes those senders to all automation engines.
- `KeySender` and `MouseSender` still contain per-sender `CanAct` checks. That is safe-ish, but not contract-compliant because the frozen contract requires one router chokepoint.
- Another concurrent edit appears to have already removed active runtime `SetForegroundWindow` calls from `KeySender`, `MouseSender`, and `VirtualHidInput`. Do not revert that.
- Two app-side bypasses remain: `BuffsViewModel` owns a standalone `KeySender`, and `MultiClientViewModel` owns standalone `PostMessage` `KeySender`/`MouseSender` instances.
- `EngineHub.TestVirtualHidKey` still bypasses sender gating by directly calling `_keys.VirtualHid?.TapKey(key, 80)`.

ADDITIONAL DOCS / GAMEPLAY RESEARCH INCORPORATED:
- `CODEX-MAP.md` confirms the active Smart Bot action model: skill attack is `Keys.Tap(skillKey) -> about 45ms -> click target`; normal attack is target click. This belongs in SmartBot timing/state logic, not inside the router.
- `USER_GUIDE.md` and `NEWBIE_GUIDE.md` confirm the beginner workflow: attach RO, select hotbar keys, configure Smart Bot start/stop, use F12 as stop-all, and expect skill hotbar activation before target click.
- `PROJECT_IMPROVEMENT_PLAN.md` and `PROJECT_KNOWLEDGE_BASE.md` confirm this task should stay within supported input routing, diagnostics, OCR/UI reliability, and project cleanup. Driver/backend internals should remain behind the existing sender/backend layer.
- `RUN-LOG-2026-07-13.md` confirms main already removed automatic foregrounding from send paths; `FocusGate` expects the RO client to already be foreground before gameplay actions leave.
- Claude 2026-07-13 specs reinforce the same boundary: split `CanRead` from `CanAct`; OCR/capture reads may continue when 4ViviTools is foreground; gameplay actions require selected-client foreground; F12/panic bypasses only to stop; one input chokepoint; multi-client can read passively but should not send gameplay input to non-selected clients.
- Local `docs/rathena/skills.md` confirms skill metadata includes SP cost, cast time, after-cast delay, cooldown, range, target type, and requirements. Router should report input delivery, while SmartBot should own cooldown/SP/action-delay decisions.
- Local `docs/rathena/ro-tools-bot-and-states.md` maps common RO combat states: idle, walking, attacking, taking damage, looting, delay, casting, sitting. These support future SmartBot state decisions but do not change InputRouter responsibilities.
- Local `docs/rathena/client-systems.md` confirms the client owns UI names/icons/effects while server data owns mechanics. For this task, key relevance is that hotbar/skill names are UX/data concerns outside the router.
- External primary research checked rAthena source/docs and RO client text resources:
  - rAthena `source_doc.txt`: map server action logic includes walk/follow/skill-use units.
  - rAthena `battle.conf`: normal attack animation/movement delays are expected game timing; not a router concern.
  - rAthena `mob_skill_db.txt`: mob skill behavior uses millisecond delays, state conditions, and targets.
  - RO client `msgstringtable.txt`: function keys, quickspell, and battlemode keys are normal client hotkey concepts.

DESIGN IMPLICATIONS FROM DOCS / RESEARCH:
- Keep `FocusGate.CanRead()` and OCR/capture independent from `InputRouter`; only gameplay action delivery goes through `CanAct()`.
- `InputRouter` should validate the current selected window handle and client rect at delivery time, especially before clicks. Do not trust stale target coordinates if the client rect is zero, minimized, or mismatched.
- Do not add automatic focus changes to routing. If main later adds a focus affordance, it should be a user-clicked UI command outside automation delivery.
- Panic/stop path must remain available regardless of foreground, but only for stopping/disabling automation.
- Hotbar keys are not limited to F-keys. The router should accept any key that `KeyName.ToVk(...)` maps for RO hotbar/battlemode usage and return `InvalidInput` only when mapping fails.
- Router logs should make delivery facts explicit: action, source, hwnd, backend, target coords if any, latency, status, and reason. They should not claim a skill was cast; SmartBot should log skill decisions after it sees the router result.
- Multi-client should remain OCR/read-only for non-selected clients in this merge. Any later active-control design should start from the same selected-client `CanAct()` rule.
- When main implements the router, update `CODEX-MAP.md` and user docs in the same pass because runtime input workflow changes are visible to users and future agents.

EXACT NEW FILES / INTERFACES:

1. Create `src/4rVivi.Core/Input/InputRouteStatus.cs`

```csharp
namespace FourRVivi.Core.Input;

public enum InputRouteStatus
{
    Sent,
    NotSent,
    Blocked,
    InvalidTarget,
    InvalidInput,
    BackendUnavailable,
    Unsupported,
    Failed
}
```

2. Create `src/4rVivi.Core/Input/InputActionKind.cs`

```csharp
namespace FourRVivi.Core.Input;

public enum InputActionKind
{
    Tap,
    KeyDown,
    KeyUp,
    Click,
    Move,
    VirtualButton,
    VirtualLeftClick
}
```

3. Create `src/4rVivi.Core/Input/InputRouteResult.cs`

```csharp
using FourRVivi.Core.Game;

namespace FourRVivi.Core.Input;

public sealed record InputRouteResult(
    InputRouteStatus Status,
    InputActionKind Action,
    InputMethod RequestedMethod,
    string Backend,
    string Reason,
    long LatencyMs,
    IntPtr WindowHandle,
    int? ClientX,
    int? ClientY,
    bool Ok,
    FocusGateSnapshot? Focus)
{
    public bool Sent => Status == InputRouteStatus.Sent && Ok;

    public static InputRouteResult NotSent(
        InputRouteStatus status,
        InputActionKind action,
        InputMethod method,
        string backend,
        string reason,
        IntPtr hwnd,
        int? x,
        int? y,
        FocusGateSnapshot? focus = null)
        => new(status, action, method, backend, reason, 0, hwnd, x, y, false, focus);
}
```

4. Create `src/4rVivi.Core/Input/InputCanAct.cs`

```csharp
using FourRVivi.Core.Game;

namespace FourRVivi.Core.Input;

public delegate bool InputCanAct(out FocusGateSnapshot snapshot);
```

5. Create `src/4rVivi.Core/Input/IInputRouter.cs`

```csharp
namespace FourRVivi.Core.Input;

public interface IInputRouter
{
    InputMethod Method { get; set; }
    bool FallbackToNormalInput { get; set; }

    InputRouteResult Tap(string key, int holdMs = 0, string source = "");
    InputRouteResult Tap(IntPtr hwnd, int virtualKey, int holdMs = 0, string source = "");
    InputRouteResult KeyDown(IntPtr hwnd, int virtualKey, string source = "");
    InputRouteResult KeyUp(IntPtr hwnd, int virtualKey, string source = "");
    InputRouteResult ClickAt(int clientX, int clientY, bool hardware = false, string source = "");
    InputRouteResult ClickAt(IntPtr hwnd, int clientX, int clientY, bool hardware = false, string source = "");
    InputRouteResult Move(IntPtr hwnd, int clientX, int clientY, string source = "");
    InputRouteResult VirtualButton(string buttonName, int holdMs = 0, string source = "");
    InputRouteResult VirtualLeftClick(int holdMs = 0, string source = "");
}
```

6. Create `src/4rVivi.Core/Input/InputRouter.cs`

Implementation requirements:
- Own the single `FocusGate.CanAct` check for all gameplay input.
- Reject `hwnd == IntPtr.Zero`.
- Reject any `hwnd` that is not `GameSession.WindowHandle` with `InvalidTarget/window-mismatch`.
- Log blocked actions as `[Input] blocked action=... source=... reason=... hwnd=... fgPid=... selPid=...`.
- Throttle blocked logs to once per second per action+reason to avoid loop spam.
- Measure delivery latency with `Stopwatch.GetTimestamp()`.
- Call backend-only methods on `KeySender` and `MouseSender`; do not call public sender methods from inside the router or it will recurse.
- Return `InputRouteResult` for every path.

Constructor:

```csharp
public sealed class InputRouter : IInputRouter
{
    private readonly GameSession _session;
    private readonly InputCanAct _canAct;
    private readonly KeySender _keys;
    private readonly MouseSender _mouse;
    private readonly Dictionary<string, long> _lastBlockedLog = new(StringComparer.OrdinalIgnoreCase);

    public InputRouter(GameSession session, FocusGate focusGate, KeySender keys, MouseSender mouse)
        : this(session, focusGate.CanAct, keys, mouse)
    {
    }

    internal InputRouter(GameSession session, InputCanAct canAct, KeySender keys, MouseSender mouse)
    {
        _session = session;
        _canAct = canAct;
        _keys = keys;
        _mouse = mouse;
    }
}
```

Delivery behavior:

```csharp
public InputRouteResult Tap(string key, int holdMs = 0, string source = "")
    => Tap(_session.WindowHandle, KeyName.ToVk(key), holdMs, source);

public InputRouteResult Tap(IntPtr hwnd, int virtualKey, int holdMs = 0, string source = "")
{
    var validation = Validate(InputActionKind.Tap, hwnd, null, null, source);
    if (validation is not null) return validation;
    if (virtualKey <= 0)
        return InputRouteResult.NotSent(InputRouteStatus.InvalidInput, InputActionKind.Tap, Method, "none", "invalid-key", hwnd, null, null);

    var started = Stopwatch.GetTimestamp();
    bool ok = _keys.DeliverTapUnchecked(hwnd, virtualKey, holdMs);
    return Complete(InputActionKind.Tap, hwnd, null, null, started, ok, ok ? "sent" : "backend-failed");
}

public InputRouteResult ClickAt(IntPtr hwnd, int clientX, int clientY, bool hardware = false, string source = "")
{
    var validation = Validate(InputActionKind.Click, hwnd, clientX, clientY, source);
    if (validation is not null) return validation;

    var started = Stopwatch.GetTimestamp();
    bool ok = hardware
        ? _mouse.DeliverHardwareClickUnchecked(hwnd, clientX, clientY)
        : _mouse.DeliverClickUnchecked(hwnd, clientX, clientY);
    return Complete(InputActionKind.Click, hwnd, clientX, clientY, started, ok, ok ? "sent" : "backend-failed");
}
```

`Move` can exist in the interface immediately but should return `Unsupported` until there is a real non-click move caller and backend implementation:

```csharp
public InputRouteResult Move(IntPtr hwnd, int clientX, int clientY, string source = "")
{
    var validation = Validate(InputActionKind.Move, hwnd, clientX, clientY, source);
    if (validation is not null) return validation;
    return InputRouteResult.NotSent(InputRouteStatus.Unsupported, InputActionKind.Move, Method, "none", "move-not-implemented", hwnd, clientX, clientY);
}
```

7. Create `tests/4rVivi.Core.Tests/InputRouterTests.cs`

Test cases:
- `Tap_returns_blocked_and_does_not_call_backend_when_gate_denies`.
- `Click_returns_invalid_target_when_hwnd_does_not_match_session_window`.
- `Tap_returns_invalid_input_for_unknown_key`.
- `Blocked_log_is_throttled_per_reason`.
- `Compatibility_sender_without_router_does_not_send`.

The tests should use the `internal InputRouter(GameSession, InputCanAct, KeySender, MouseSender)` constructor with a fake `InputCanAct` delegate. Add test-visible backend counters through `internal` sender hooks if needed.

MINIMAL SHARED-FILE PATCH PLAN:

1. `src/4rVivi.Core/Automation/EngineHub.cs`
- Add `public IInputRouter Input { get; }`.
- After constructing `_keys`, `_mouse`, and `FocusGate`, construct `Input = new InputRouter(session, FocusGate, _keys, _mouse);`.
- Set `_keys.Router = Input; _mouse.Router = Input;`.
- Stop setting `_keys.FocusGate` and `_mouse.FocusGate` after the router is active.
- Make `InputMethod` get/set `Input.Method`, then mirror to `_keys.Method` and `_mouse.Method` inside `InputRouter.Method` setter.
- Make `VirtualClickFallback` get/set `Input.FallbackToNormalInput`.
- Change test methods:
  - `TestVirtualLeftClick` -> `Input.VirtualLeftClick(VirtualClickHoldMs, "EngineHub.TestVirtualLeftClick").Sent`
  - `TestVirtualButton` -> `Input.VirtualButton(buttonName, VirtualClickHoldMs, "EngineHub.TestVirtualButton").Sent`
  - `TestVirtualHidClick` -> temporarily set `Input.Method = InputMethod.VirtualHid`, call `Input.ClickAt(centerX, centerY, source: "EngineHub.TestVirtualHidClick")`, restore previous method.
  - `TestVirtualHidKey` -> temporarily set `Input.Method = InputMethod.VirtualHid`, call `Input.Tap(key, 80, "EngineHub.TestVirtualHidKey")`, restore previous method.
  - `TestViiperInput` -> temporarily set `Input.Method = InputMethod.Viiper`, call `Input.ClickAt(...)` then `Input.Tap("F2", 80, "EngineHub.TestViiperInput")`, restore previous method.

2. `src/4rVivi.Core/Input/KeySender.cs`
- Add `public IInputRouter? Router { get; set; }`.
- Keep `Method`, `VirtualHid`, `Viiper`, and fallback properties as backend configuration.
- Remove `FocusGate` property and `CanAct` method once router is wired.
- Convert public methods to compatibility shims:
  - `Tap(...)` calls `Router?.Tap(...)`; if `Router is null`, log `[Input] blocked action=Tap reason=no-router` and return without sending.
  - `TryVirtualHidTap(...)` calls `Router?.Tap(...)` and returns `result.Sent`; if `Router is null`, return false.
  - `TapSendInputFallback(...)` should not force SendInput directly; call `Router?.Tap(...)` and let the configured fallback rules decide.
  - `Down(...)` calls `Router?.KeyDown(...)`; `Up(...)` calls `Router?.KeyUp(...)`.
- Move the existing raw delivery code into `internal bool DeliverTapUnchecked(...)`, `internal bool DeliverKeyDownUnchecked(...)`, `internal bool DeliverKeyUpUnchecked(...)`.
- Backend-only methods must not call `FocusGate` and must not call `SetForegroundWindow`.

3. `src/4rVivi.Core/Input/MouseSender.cs`
- Add `public IInputRouter? Router { get; set; }`.
- Keep `Method`, `VirtualHid`, `Viiper`, and virtual-click configuration as backend configuration.
- Remove `FocusGate` property and `CanAct` method once router is wired.
- Convert public methods to compatibility shims:
  - `Click(...)` calls `Router?.ClickAt(...)`; if `Router is null`, log `[Input] blocked action=Click reason=no-router` and return.
  - `HardwareClick(...)` calls `Router?.ClickAt(..., hardware: true)`.
  - `TapVirtualButton(...)` calls `Router?.VirtualButton(...)`.
  - `TapVirtualLeftClick(...)` calls `Router?.VirtualLeftClick(...)`.
- Move existing raw delivery code into `internal bool DeliverClickUnchecked(...)`, `internal bool DeliverHardwareClickUnchecked(...)`, `internal bool DeliverVirtualButtonUnchecked(...)`, `internal bool DeliverVirtualLeftClickUnchecked(...)`.
- Backend-only methods must not call `FocusGate` and must not call `SetForegroundWindow`.

4. `src/4rVivi.App/ViewModels/BuffsViewModel.cs`
- Delete the standalone `private readonly KeySender _keys = new();`.
- In `RunBuffSequence`, call `_hub.Input.Tap(_session.WindowHandle, KeyName.ToVk(b.Key), 20, "BuffsViewModel.RunBuffSequence");`.

5. `src/4rVivi.App/ViewModels/MultiClientViewModel.cs`
- Delete standalone `_mouse` and `_keys` senders.
- For this merge, disable gameplay input and keep OCR only:
  - In `StartBots`, set status to `Multi-client OCR running. Input disabled until selected-client foreground routing is available.`
  - In `SendUnfocusedInput`, set row status to `Input blocked: select this client as the active client and focus it.`
  - In `SendBuffInput`, same blocked status.
- Do not route unfocused multi-client input through `InputRouter`; the contract permits gameplay input only to the selected foreground RO client.

6. `src/4rVivi.Core/Automation/*.cs`
- Minimal first merge can avoid touching every engine because the existing public sender methods become router shims.
- Follow-up cleanup may replace direct `Keys.Tap` / `_mouse.Click` calls with `Input.Tap` / `Input.ClickAt`, but it is not required for the first safe chokepoint merge if sender shims are in place and standalone sender bypasses are removed/blocked.

7. `src/4rVivi.Core/Input/VirtualHidInput.cs`, `src/4rVivi.Core/Input/ViiperInput.cs`, `src/4rVivi.Core/Input/ReWasdController.cs`
- No driver/internal behavior changes.
- Keep them as backend-only delivery providers behind `KeySender`/`MouseSender`.
- Do not add any focus manipulation or unexpected backend behavior.

SETFOREGROUNDWINDOW AUDIT:

Active `src/4rVivi.App` / `src/4rVivi.Core` runtime:
- `SetForegroundWindow`: no active matches found.
- `src/4rVivi.Core/Input/KeySender.cs`: current file has no `SetForegroundWindow` import or call. Classification: remove now already done by concurrent edit; do not reintroduce.
- `src/4rVivi.Core/Input/MouseSender.cs`: current file has no `SetForegroundWindow` import or call. Classification: remove now already done by concurrent edit; do not reintroduce.
- `src/4rVivi.Core/Input/VirtualHidInput.cs`: current file has no `SetForegroundWindow` import or call and no `FocusWindow` method. Classification: remove now already done by concurrent edit; do not reintroduce.

Active foreground-related calls that are not `SetForegroundWindow`:
- `src/4rVivi.Core/Game/FocusGate.cs:33` and `:91`: `GetForegroundWindow` is used to evaluate `CanAct`. Classification: keep.
- `src/4rVivi.App/Services/SmartBotTrainingRecorder.cs:268` and `:383`: `GetForegroundWindow` is read-only training/observability. Classification: keep unless Agent 2/3 changes training trust rules.
- `src/4rVivi.App/Views/OcrReaderView.axaml.cs:133`: Avalonia `win.Activate()` restores the 4ViviTools window after a monitor screenshot. It does not focus the RO client or send gameplay input. Classification: keep.

User-clicked focus button:
- No active App/Core `Focus client` / `FocusClient` command was found. Classification: no existing call to keep. If main adds a user-clicked focus button later, that should be the only allowed place to call `SetForegroundWindow`, and it must not be used by automation or router delivery.

Legacy ignored:
- `Bot/BotPrimitives.cs:25` declares `SetForegroundWindow`. This file belongs to the root legacy 4RTools project area, not active `4rVivi.sln` App/Core runtime. Classification: legacy ignored for this merge; do not edit unless main explicitly cleans the old project.

PROPOSED DIFFS FOR MAIN (shared files):

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
+        _keys.Router = Input;
+        _mouse.Router = Input;
@@
-        _mouse.TapVirtualLeftClick();
-        return true;
+        return Input.VirtualLeftClick(VirtualClickHoldMs, "EngineHub.TestVirtualLeftClick").Sent;
@@
-        _mouse.TapVirtualButton(buttonName);
-        return true;
+        return Input.VirtualButton(buttonName, VirtualClickHoldMs, "EngineHub.TestVirtualButton").Sent;
@@
-        return _keys.VirtualHid?.TapKey(key, 80) == true;
+        var prev = Input.Method;
+        try
+        {
+            Input.Method = InputMethod.VirtualHid;
+            return Input.Tap(key, 80, "EngineHub.TestVirtualHidKey").Sent;
+        }
+        finally { Input.Method = prev; }
```

```diff
diff --git a/src/4rVivi.App/ViewModels/BuffsViewModel.cs b/src/4rVivi.App/ViewModels/BuffsViewModel.cs
@@
-    private readonly KeySender _keys = new();
@@
-            _keys.Tap(_session.WindowHandle, KeyName.ToVk(b.Key), 20);
+            _hub.Input.Tap(_session.WindowHandle, KeyName.ToVk(b.Key), 20, "BuffsViewModel.RunBuffSequence");
```

```diff
diff --git a/src/4rVivi.App/ViewModels/MultiClientViewModel.cs b/src/4rVivi.App/ViewModels/MultiClientViewModel.cs
@@
-    private readonly MouseSender _mouse = new() { Method = InputMethod.PostMessage };
-    private readonly KeySender _keys = new() { Method = InputMethod.PostMessage };
@@
-        Status = "Multi-client OCR + unfocused PostMessage input running.";
+        Status = "Multi-client OCR running. Input disabled until selected-client foreground routing is available.";
@@
-            _keys.Tap(client.WindowHandle, KeyName.ToVk(client.SkillKey), 25);
-            Thread.Sleep(45);
+            Post(client, c => c.Status = "Input blocked: select this client as the active client and focus it.");
+            return;
@@
-        _mouse.Click(client.WindowHandle, target.Cx, target.Cy);
+        return;
@@
-        _keys.Tap(client.WindowHandle, KeyName.ToVk(client.BuffKey), 35);
+        Post(client, c => c.Status = "Input blocked: select this client as the active client and focus it.");
+        return;
```

FINDINGS:
- Current active runtime has no `SetForegroundWindow` calls left, which is good and should be preserved.
- The fastest safe path to a real chokepoint is compatibility shims: keep existing engine call sites working, but make `KeySender`/`MouseSender` public methods call `InputRouter`; move raw delivery into internal unchecked backend methods used only by the router.
- Standalone sender instances must become non-sending when they have no router. That closes current app-side bypasses and prevents future accidental ungated sends.
- MultiClient unfocused input is incompatible with the frozen `CanAct()` contract. Keep multi-client OCR but block gameplay input until a selected foreground-client flow exists.
- Test buttons must go through router too. `EngineHub.TestVirtualHidKey` is the clearest current bypass.

TESTS TO RUN:
- `dotnet test tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj -c Release --filter InputRouter`
- `dotnet test 4rVivi.sln -c Release`
- `dotnet build 4rVivi.sln -c Release`
- Manual after main implementation: select RO client, leave 4ViviTools foreground, press Smart Bot test input buttons. Expected `[Input] blocked ... reason=not-foreground ...` and no gameplay input.
- Manual after main implementation: selected RO client foreground, press a normal configured action. Expected `[Input] sent action=... backend=... latencyMs=... ok=True`.

EVIDENCE:
- `rg --files-with-matches -I -S -g '*.cs' -g '*.axaml' -g '!**/bin/**' -g '!**/obj/**' "SetForegroundWindow|GetForegroundWindow|Activate\\(" src/4rVivi.Core src/4rVivi.App` returned only `FocusGate.cs`, `SmartBotTrainingRecorder.cs`, and `OcrReaderView.axaml.cs`.
- Direct file check found no `SetForegroundWindow` import/call in active `KeySender.cs`, `MouseSender.cs`, or `VirtualHidInput.cs`.
- `rg --files-with-matches -I -S -g '*.cs' -g '!**/bin/**' -g '!**/obj/**' "SetForegroundWindow|GetForegroundWindow" Model Features Macros Utils Bot Farm Forms Core UI` returned only `Bot/BotPrimitives.cs` for legacy root code.
- `rg --files -g '*InputRouter*' -g '*IInputRouter*' -g '*InputResult*' src tests` returned no files.
- `EngineHub.cs:196` still directly calls `_keys.VirtualHid?.TapKey(key, 80)`.
- `BuffsViewModel.cs:16` still creates standalone `KeySender`; `:56` sends with it.
- `MultiClientViewModel.cs:51-52` still creates standalone PostMessage senders; `:404`, `:408`, and `:444` send with them.
- Local docs reviewed for incorporated context: `CODEX-MAP.md`, `USER_GUIDE.md`, `NEWBIE_GUIDE.md`, `PROJECT_IMPROVEMENT_PLAN.md`, `PROJECT_KNOWLEDGE_BASE.md`, `RUN-LOG-2026-07-13.md`, `CONTRACTS.md`, and Claude 2026-07-13 reply/spec files listed above.
- External gameplay references used:
  - https://github.com/rathena/rathena/blob/master/doc/source_doc.txt
  - https://github.com/rathena/rathena/blob/master/conf/battle/battle.conf
  - https://github.com/rathena/rathena/blob/master/db/import-tmpl/mob_skill_db.txt
  - https://github.com/zackdreaver/ROenglishRE/blob/master/Ragnarok/data/msgstringtable.txt

RISKS:
- Compatibility shims must avoid recursion. Router must call only `Deliver*Unchecked` internal methods, never public `Tap`/`Click`.
- Making standalone senders non-sending can change behavior in hidden UI paths. This is intentional for safety, but main should expect some buttons to become blocked until routed.
- If `KeySender.Down`/`Up` are used later for true held keys, router must track focus per event. Current source mostly uses `Tap`; add tests before expanding held-key behavior.
- `Move` exists in the interface for contract shape, but first merge should return `Unsupported` unless main adds a real backend move method.

DO NOT TOUCH:
- Do not reintroduce `SetForegroundWindow` in send paths.
- Do not modify VIIPER, FakerInput/vmouse, ViGEm, or reWASD internals.
- Do not implement unfocused multi-client gameplay input.
- Do not edit `CONTRACTS.md` from an agent branch.
- Do not revert concurrent input-file edits.

CONTRACT IMPACT:
- none. This plan implements the frozen FocusGate/Input Chokepoint contract without proposing contract changes.
