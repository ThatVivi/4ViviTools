# Agent 4 Task 4 UI Noise Cleanup

AGENT: 4 / Newton UI-UX

OBJECTIVE:
- Read the current orientation/user/project docs, Task 3 handoff, and latest Smart Bot/OCR screenshots referenced in specs.
- Implement a bounded cleanup if safe, or prepare precise AXAML patch guidance if shared UI edits are too risky.
- Keep write scope to UI controls/styles/views plus this report.

FILES READ:
- `docs/CODEX-MAP.md`
- `docs/USER_GUIDE.md`
- `docs/PROJECT_IMPROVEMENT_PLAN.md`
- `docs/superpowers/specs/agent-4-task3-slider-autopill-advanced-nav.md`
- `docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-ocr-question.md`
- `docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-reply.md`
- `src/4rVivi.App/Views/SmartBotView.axaml`
- `src/4rVivi.App/Views/OcrReaderView.axaml`
- `src/4rVivi.App/Styles/AutoPill.axaml`

SCREENSHOTS INSPECTED:
- `C:\Users\Vivi\AppData\Local\Packages\MicrosoftWindows.Client.Core_cw5n1h2txyewy\TempState\ScreenClip\{8CA027B4-EBE3-49C2-B83E-4153BDBEAD08}.png`
- `C:\Users\Vivi\AppData\Local\Packages\MicrosoftWindows.Client.Core_cw5n1h2txyewy\TempState\ScreenClip\{6021E7FC-4329-4799-9932-258E7F89397A}.png`

DECISION:
- Did not edit `SmartBotView.axaml`, `OcrReaderView.axaml`, `MainWindow.axaml`, shared VMs, or settings in this task.
- Reason: current worktree already has many dirty shared UI files, including both target views and shared styles. A direct edit would risk overwriting or interleaving with other agents' UI changes.
- Deliverable is therefore an exact UI-only patch plan. It is intentionally AXAML-first and uses existing properties where possible.

LATEST SCREENSHOT FINDINGS:
- Smart Bot Hunt behavior: the visible driver cluster dominates the screen. Normal setup shows raw stack text, driver brand names, repair/folder/test buttons, and a red-bordered `-1 = Auto` note before the user reaches the real hunt settings.
- OCR/Capture: Advanced is visibly off, but monitor/DXGI/run mode/screen/window/zoom/filter/sharp/top/side controls are still visible. This is the strongest OCR noise leak.
- Red borders are overused for normal guidance. The `-1 = Auto` timing note reads like a warning even though it describes the recommended state.
- Current beginner goal from docs: attach client, capture/mark HP/SP percent text, optionally enable Vision Assist GRF, choose RO hotbar actions, start Smart Bot, and see a compact input/OCR/bot status.

BOUNDED CLEANUP PLAN:

## Patch A: Smart Bot Hunt Behavior, Highest Impact

Goal:
- Keep behavior unchanged.
- Keep every existing driver/input control available.
- Beginner view shows one compact input health row and one `Manage input / advanced timing` toggle.
- Raw driver stack, vendor-specific buttons, input method combo, and manual timing internals stay behind the existing `ShowAdvancedTiming` property.

Why this is safe:
- Uses only existing bindings and commands.
- No VM/settings changes.
- No routing behavior changes.
- The user can still reach all existing controls by toggling Advanced timing/input.

Exact AXAML patch shape:

```diff
diff --git a/src/4rVivi.App/Views/SmartBotView.axaml b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-        <TextBlock Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0" Classes="muted" TextWrapping="Wrap"
-                   Text="Install ViGEm for the default virtual click path. reWASD is optional when you want imported or active controller profiles."/>
+        <WrapPanel Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0">
+          <Border Padding="8,4" CornerRadius="6" BorderThickness="1"
+                  BorderBrush="{DynamicResource BorderBrush}" Background="{DynamicResource Surface2Brush}"
+                  Margin="0,0,10,8">
+            <StackPanel Orientation="Horizontal" Spacing="7" VerticalAlignment="Center">
+              <Ellipse Width="10" Height="10" Fill="{Binding VirtualDriverStatusBrush}" VerticalAlignment="Center"/>
+              <TextBlock Text="{Binding VirtualDriverStatus}" Classes="muted" VerticalAlignment="Center"/>
+            </StackPanel>
+          </Border>
+          <Button Command="{Binding RefreshVirtualDriverStatusCommand}" Margin="0,0,10,8">
+            <StackPanel Orientation="Horizontal" Spacing="6">
+              <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE72C;" VerticalAlignment="Center"/>
+              <TextBlock Text="Check input"/>
+            </StackPanel>
+          </Button>
+          <TextBlock Text="Use Advanced timing/input for driver setup and manual overrides."
+                     Classes="muted" TextWrapping="Wrap" VerticalAlignment="Center" Margin="0,0,0,8"/>
+        </WrapPanel>
@@
-          <Border Padding="10,6" CornerRadius="6" BorderThickness="1"
-                  BorderBrush="{DynamicResource AccentBrush}" Background="{DynamicResource Surface2Brush}"
-                  Margin="0,0,16,8">
-            <TextBlock Text="-1 = Auto timing. Leave timing boxes at -1 for the recommended formulas; enter a number only when you want a manual override." TextWrapping="Wrap" MaxWidth="920"/>
-          </Border>
-          <CheckBox Content="Advanced timing" IsChecked="{Binding ShowAdvancedTiming}" Margin="0,0,16,8"/>
+          <Border Classes="auto-pill" Margin="0,0,10,8">
+            <TextBlock Classes="auto-pill" Text="Auto timing"/>
+          </Border>
+          <TextBlock Text="Recommended formulas are active. Use manual timing only when tuning a specific setup."
+                     Classes="muted" TextWrapping="Wrap" MaxWidth="760" VerticalAlignment="Center" Margin="0,0,16,8"/>
+          <CheckBox Content="Advanced timing/input" IsChecked="{Binding ShowAdvancedTiming}" Margin="0,0,16,8"/>
@@
-        <WrapPanel>
+        <WrapPanel IsVisible="{Binding ShowAdvancedTiming}">
           <TextBlock Text="Input" VerticalAlignment="Center" Classes="muted" Margin="0,0,8,8"/>
           <ComboBox MinWidth="320" SelectedIndex="{Binding InputMethodIndex}" ItemsSource="{Binding InputMethods}"
@@
-        <Border Padding="10" CornerRadius="6" BorderThickness="1"
+        <Border Padding="10" CornerRadius="6" BorderThickness="1"
+                IsVisible="{Binding ShowAdvancedTiming}"
                 BorderBrush="{DynamicResource BorderBrush}" Background="{DynamicResource Surface2Brush}">
```

Important prerequisite:
- `AutoPill.axaml` must be loaded in `App.axaml` before using `Classes="auto-pill"`.

```diff
diff --git a/src/4rVivi.App/App.axaml b/src/4rVivi.App/App.axaml
@@
     <StyleInclude Source="avares://4rVivi/Styles/Colors.axaml"/>
     <StyleInclude Source="avares://4rVivi/Styles/Controls.axaml"/>
+    <StyleInclude Source="avares://4rVivi/Styles/AutoPill.axaml"/>
```

If `App.axaml` is also dirty, defer the `auto-pill` class and use the existing neutral border until the style include can be merged cleanly.

## Patch B: OCR/Capture Beginner Noise Leak

Goal:
- Keep Advanced toggle behavior meaningful.
- With Advanced off, show capture/mark/run workflow only.
- Move monitor/DXGI/run-mode/screen/window/refresh/zoom/filter/sharp/top/side controls behind existing `ShowAdvanced`.

Why this is safe:
- Uses the existing `ShowAdvanced` binding already present in `OcrReaderView.axaml`.
- No VM changes.
- No OCR behavior changes.

Exact AXAML patch shape:

```diff
diff --git a/src/4rVivi.App/Views/OcrReaderView.axaml b/src/4rVivi.App/Views/OcrReaderView.axaml
@@
-            <WrapPanel Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0">
+            <WrapPanel Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0">
               <Button Classes="accent" Margin="0,0,8,8" Click="OnCaptureMonitorClick">
@@
               <TextBlock Text="Mark" Classes="muted" Margin="8,0,6,8" VerticalAlignment="Center"/>
               <ComboBox Width="160" Margin="0,0,8,8" ItemsSource="{Binding Roles}" SelectedItem="{Binding SelectedRole}"/>
               <TextBlock Text="{Binding SelectedRoleHint}" Classes="muted" TextWrapping="Wrap" VerticalAlignment="Center" Margin="0,0,10,8"/>
+              <Button Command="{Binding ResetDefaultsCommand}" Margin="0,0,8,8">
+                <StackPanel Orientation="Horizontal" Spacing="6">
+                  <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE777;" VerticalAlignment="Center"/>
+                  <TextBlock Text="Reset OCR defaults"/>
+                </StackPanel>
+              </Button>
+            </WrapPanel>
+            <WrapPanel Grid.Row="2" Grid.ColumnSpan="2" Margin="0,2,0,0" IsVisible="{Binding ShowAdvanced}">
               <CheckBox Margin="0,0,10,8" Content="Monitor capture" IsChecked="{Binding UseMonitor}" VerticalAlignment="Center"/>
               <CheckBox Margin="0,0,10,8" Content="DXGI default" IsChecked="{Binding UseDxgiCapture}" VerticalAlignment="Center"
@@
-              <Button Command="{Binding ResetDefaultsCommand}" Margin="0,0,8,8">
-                <StackPanel Orientation="Horizontal" Spacing="6">
-                  <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE777;" VerticalAlignment="Center"/>
-                  <TextBlock Text="Reset OCR defaults"/>
-                </StackPanel>
-              </Button>
               <TextBlock Text="Refresh (-1 auto)" Classes="muted" Margin="0,0,6,8" VerticalAlignment="Center"/>
@@
-              <TextBlock Text="Zoom" Classes="muted" Margin="0,0,6,8" VerticalAlignment="Center"/>
+            </WrapPanel>
+            <WrapPanel Grid.Row="3" Grid.ColumnSpan="2" Margin="0,2,0,0" IsVisible="{Binding ShowAdvanced}">
+              <TextBlock Text="Zoom" Classes="muted" Margin="0,0,6,8" VerticalAlignment="Center"/>
```

Notes:
- The real file's row definitions must be checked before applying. If the grid has only two rows, add row definitions for the advanced rows or keep both advanced `WrapPanel`s in the same row inside a `StackPanel`.
- Do not hide `Use markers`, Vision Assist GRF, or Run OCR behind Advanced.
- Rename `HP Bar` / `SP Bar` to `HP % Text` / `SP % Text` only when VM role labels are in scope; that cannot be completed as view-only cleanup if the strings come from the VM/model.

## Patch C: Marks List Default

Goal:
- With Advanced off, the right marks pane should show only missing/stale problems or a compact count.
- Full marks list remains available in Advanced.

Current risk:
- This likely needs VM support for a problem-only collection. View-only filtering would be brittle.

Recommended next patch:
- Add `ProblemMarks` or `MarkerProblemSummary` in `OcrReaderViewModel`.
- Bind the beginner pane to problem summary.
- Keep existing full marks list inside `IsVisible="{Binding ShowAdvanced}"`.

Do not attempt this as a pure AXAML edit unless the VM already exposes problem-only marker state.

EXPECTED VISUAL RESULT:
- Smart Bot default Hunt behavior becomes one normal guidance line, one compact input status, and the actual hunt fields.
- All existing driver repair/test/folder details remain reachable by toggling `Advanced timing/input`.
- OCR default capture panel no longer shows DXGI, monitor, filter, sharpen, top/side offsets, or refresh internals while Advanced is off.
- The default UI reads like a RO workflow, not an engineering console.

FILES CREATED / EDITED IN TASK 4:
- `docs/superpowers/specs/agent-4-task4-ui-noise-cleanup.md`

FILES INTENTIONALLY NOT EDITED:
- `src/4rVivi.App/Views/SmartBotView.axaml`
- `src/4rVivi.App/Views/OcrReaderView.axaml`
- `src/4rVivi.App/Views/MainWindow.axaml`
- shared ViewModels/settings
- shared styles already dirty from other work

BUILD / VERIFICATION:
- No app build was run for this task because no app source file was edited.
- Before applying Patch A/B, run `dotnet build src\4rVivi.App\4rVivi.App.csproj -c Release --no-restore`.
- Current known worktree risk from prior verification: app build failed in unrelated dirty `src/4rVivi.App/ViewModels/StatsViewModel.cs` with a `double` to `int` conversion error. Re-check before attributing build failure to UI cleanup.

HANDOFF RECOMMENDATION:
- Apply Patch A first once the current SmartBot view edit owner is clear. It gives the biggest visible improvement with the least behavior risk.
- Apply Patch B second. It aligns OCR's existing Advanced toggle with what the screenshot and docs already promise.
- Defer Patch C until VM work is allowed.
