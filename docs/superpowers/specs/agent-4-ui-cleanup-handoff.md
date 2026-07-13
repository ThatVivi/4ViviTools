# Agent 4 UI Cleanup Handoff

AGENT: 4 UI/UX compaction

FILES INSPECTED:
- `docs/superpowers/specs/CONTRACTS.md`
- `docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md`
- `src/4rVivi.App/Views/SmartBotView.axaml`
- `src/4rVivi.App/ViewModels/SmartBotViewModel.cs`
- `src/4rVivi.App/Views/OcrReaderView.axaml`
- `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`
- `src/4rVivi.App/Views/MainWindow.axaml`
- `src/4rVivi.App/ViewModels/MainWindowViewModel.cs`
- `src/4rVivi.App/ViewModels/NavItems.cs`
- `src/4rVivi.App/Views/AutopotView.axaml`
- `src/4rVivi.App/Views/StatsView.axaml`
- `src/4rVivi.App/Services/RolePalette.cs`
- `src/4rVivi.App/Views/FourRToolsShellView.axaml`
- `src/4rVivi.App/Views/RoToolsShellView.axaml`

FILES CREATED (owned):
- `docs/superpowers/specs/agent-4-ui-cleanup-handoff.md`

PROPOSED DIFFS FOR MAIN (shared files):

## 1. Hide legacy 4RTools / ro-tools from primary nav

Current state: `MainWindowViewModel` places `Legacy 4rTools` and `Legacy ro-tools` under the main `Tools` category, so the legacy shells are first-class navigation pages. Master-plan Q11/Q4 says hide them from primary nav, keep internals under Advanced/Data/Integrations only.

Proposed behavior:
- Remove `Legacy 4rTools` and `Legacy ro-tools` from normal nav.
- Add a global `Advanced` toggle in the top bar or System/Settings.
- Only when advanced is enabled, expose a single `Data/Integrations` page or `Tools/Integrations` page containing links/status for legacy shells and address-reader services.
- Keep `FourRToolsShellViewModel` and `RoToolsShellViewModel` constructed for internal/service use until main can split them into `RoClientDataService`.

```diff
diff --git a/src/4rVivi.App/ViewModels/MainWindowViewModel.cs b/src/4rVivi.App/ViewModels/MainWindowViewModel.cs
@@
-        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
+        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools));
+        if (_settings.Current.ShowAdvancedUi)
+            AddCat("Integrations", ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
```

Main should prefer a real observable `ShowAdvancedUi` setting over this sketch, so nav can refresh live when the user toggles Advanced. If live refresh is too much tonight, hide the legacy shells unconditionally and leave a deep-link route for debug builds.

```diff
diff --git a/src/4rVivi.App/Views/MainWindow.axaml b/src/4rVivi.App/Views/MainWindow.axaml
@@
-      <Grid ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,*,Auto,Auto" VerticalAlignment="Center" Margin="14,0">
+      <Grid ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto" VerticalAlignment="Center" Margin="14,0">
@@
-        <Button Grid.Column="7" MinWidth="132" Command="{Binding StopAllCommand}">
+        <ToggleSwitch Grid.Column="7" Margin="0,0,12,0"
+                      OffContent="Beginner" OnContent="Advanced"
+                      IsChecked="{Binding ShowAdvancedUi}"/>
+        <Button Grid.Column="8" MinWidth="132" Command="{Binding StopAllCommand}">
```

## 2. Compact Smart Bot beginner view

Current state:
- Header exposes start/stop keys and training controls immediately (`SmartBotView.axaml` lines 38-84).
- Driver/backend details, reWASD/FakerInput/VIIPER controls, fallback tests, and install buttons appear inside the primary hunt card (`lines 102-231`).
- `ShowAdvancedTiming` is doing double duty as both timing visibility and backend/driver visibility.
- The hotbar is the best single config surface, but legacy fields still exist in the VM (`AttackKey`, `LootKey`, `TeleportKey`, `ReturnKey`, `BuffKeys`, `WeaponKey`, `AmmoKey`, `Pots`, `BuffButtons`) and sync into/out of hotbar rows.

Proposed beginner Smart Bot screen, top to bottom:
- Status strip: `BotStateText`, `Input ready / needs setup`, `OCR ready / needs HP+SP`, `Vision Assist active` when applicable.
- Commands: `Start`, `Stop`, `Show work area`, `Manage input`.
- Hunt basics: `Flee at HP %`, `Return at weight %`, `Walk delay (ms)` with Auto pill.
- Action hotbar: keep as the only skill/buff/pot/ammo/teleport/reconnect config.
- Work area: replace four loose box number fields with slider+numeric pairs and live overlay.
- Advanced collapsible section: start/stop hotkeys, training, backend selection, controller assignments, debug log, raw address status.

Proposed AXAML-only sketch for main to apply after shared VM properties exist:

```diff
diff --git a/src/4rVivi.App/Views/SmartBotView.axaml b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-        <TextBlock Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0" Classes="muted" TextWrapping="Wrap"
-                   Text="Install ViGEm for the default virtual click path. reWASD is optional when you want imported or active controller profiles."/>
-        <WrapPanel Grid.Row="2" Grid.ColumnSpan="2" Margin="0,10,0,0">
+        <WrapPanel Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0">
+          <Border Classes="status-pill" Margin="0,0,8,8">
+            <TextBlock Text="{Binding InputReadinessText}"/>
+          </Border>
+          <Border Classes="status-pill" Margin="0,0,8,8">
+            <TextBlock Text="{Binding OcrReadinessText}"/>
+          </Border>
+          <Border Classes="status-pill" Margin="0,0,8,8" IsVisible="{Binding VisionAssistGrfActive}">
+            <TextBlock Text="Vision Assist active - monster OCR disabled"/>
+          </Border>
+          <Button Command="{Binding ToggleWorkAreaCommand}" Margin="0,0,8,8">
+            <StackPanel Orientation="Horizontal" Spacing="6">
+              <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE7F4;" VerticalAlignment="Center"/>
+              <TextBlock Text="Show work area"/>
+            </StackPanel>
+          </Button>
+          <Button Command="{Binding ManageInputCommand}" Margin="0,0,8,8">
+            <StackPanel Orientation="Horizontal" Spacing="6">
+              <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE713;" VerticalAlignment="Center"/>
+              <TextBlock Text="Manage input"/>
+            </StackPanel>
+          </Button>
+        </WrapPanel>
+        <WrapPanel Grid.Row="2" Grid.ColumnSpan="2" Margin="0,10,0,0" IsVisible="{Binding ShowAdvancedUi}">
           <TextBlock Text="Start key" Classes="muted" VerticalAlignment="Center" Margin="0,0,8,8"/>
@@
-        <WrapPanel Grid.Row="3" Grid.ColumnSpan="2" Margin="0,8,0,0">
+        <WrapPanel Grid.Row="3" Grid.ColumnSpan="2" Margin="0,8,0,0" IsVisible="{Binding ShowAdvancedUi}">
```

```diff
diff --git a/src/4rVivi.App/Views/SmartBotView.axaml b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-        <WrapPanel>
-          <TextBlock Text="Input" VerticalAlignment="Center" Classes="muted" Margin="0,0,8,8"/>
-          <ComboBox MinWidth="320" SelectedIndex="{Binding InputMethodIndex}" ItemsSource="{Binding InputMethods}"
-                    Margin="0,0,10,8"
-                    ToolTip.Tip="Choose how Smart Bot sends clicks. ViGEm virtual click works with the ViGEm driver; reWASD is optional and can import/use profiles for the same virtual controller."/>
+        <WrapPanel IsVisible="{Binding ShowAdvancedUi}">
+          <TextBlock Text="Input backend" VerticalAlignment="Center" Classes="muted" Margin="0,0,8,8"/>
+          <ComboBox MinWidth="320" SelectedIndex="{Binding InputMethodIndex}" ItemsSource="{Binding InputMethods}"
+                    Margin="0,0,10,8"/>
@@
-        <Border Padding="10" CornerRadius="6" BorderThickness="1"
+        <Border Padding="10" CornerRadius="6" BorderThickness="1"
                 BorderBrush="{DynamicResource BorderBrush}" Background="{DynamicResource Surface2Brush}">
+                IsVisible="{Binding ShowAdvancedUi}">
```

The second hunk needs a syntax-safe final edit; the intent is to hide the full driver/backend block behind `ShowAdvancedUi` and replace it in beginner mode with `InputReadinessText` plus `Manage input`.

## 3. Convert Smart Bot timers to milliseconds in UI

Current state:
- `Walk delay ms (-1 auto default)` binds `MoveWaitMs` (`SmartBotView.axaml` line 249).
- `Stuck seconds` binds `StuckSeconds` (`line 250`).
- `Focus kill sec (-1 auto)` binds `FocusKillSeconds` (`line 251`).
- `Next monster ms (-1 auto)` binds `NextMonsterDelayMs` (`line 252`).

Main must rename fields and migrate saved profiles. Agent 4 AXAML proposal after main exposes `StuckMs` and `FocusKillMs`:

```diff
diff --git a/src/4rVivi.App/Views/SmartBotView.axaml b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-          <StackPanel Margin="0,0,14,8"><TextBlock Text="Walk delay ms (-1 auto default)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="5000" Increment="100" Value="{Binding MoveWaitMs}"/></StackPanel>
-          <StackPanel Margin="0,0,14,8"><TextBlock Text="Stuck seconds" Classes="muted"/><NumericUpDown MinWidth="110" Minimum="2" Maximum="120" Value="{Binding StuckSeconds}"/></StackPanel>
-          <StackPanel Margin="0,0,14,8"><TextBlock Text="Focus kill sec (-1 auto)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="600" Increment="1" Value="{Binding FocusKillSeconds}"/></StackPanel>
-          <StackPanel Margin="0,0,14,8"><TextBlock Text="Next monster ms (-1 auto)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="5000" Increment="50" Value="{Binding NextMonsterDelayMs}"/></StackPanel>
+          <StackPanel Margin="0,0,14,8"><TextBlock Text="Walk delay (ms)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="5000" Increment="100" Value="{Binding MoveWaitMs}"/></StackPanel>
+          <StackPanel Margin="0,0,14,8"><TextBlock Text="Stuck (ms)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="120000" Increment="250" Value="{Binding StuckMs}"/></StackPanel>
+          <StackPanel Margin="0,0,14,8"><TextBlock Text="Focus kill (ms)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="600000" Increment="250" Value="{Binding FocusKillMs}"/></StackPanel>
+          <StackPanel Margin="0,0,14,8"><TextBlock Text="Next monster (ms)" Classes="muted"/><NumericUpDown MinWidth="150" Minimum="-1" Maximum="5000" Increment="50" Value="{Binding NextMonsterDelayMs}"/></StackPanel>
```

VM/main migration required:

```diff
diff --git a/src/4rVivi.App/ViewModels/SmartBotViewModel.cs b/src/4rVivi.App/ViewModels/SmartBotViewModel.cs
@@
-    [ObservableProperty] private int _stuckSeconds;
-    [ObservableProperty] private int _focusKillSeconds = -1;
+    [ObservableProperty] private int _stuckMs = -1;
+    [ObservableProperty] private int _focusKillMs = -1;
@@
-    partial void OnStuckSecondsChanged(int value) { _hub.SmartBot.StuckSeconds = Math.Max(2, value); SaveBotProfile(); }
-    partial void OnFocusKillSecondsChanged(int value) { _hub.SmartBot.FocusKillSeconds = value < 0 ? -1 : Math.Clamp(value, 1, 600); SaveBotProfile(); }
+    partial void OnStuckMsChanged(int value) { _hub.SmartBot.StuckMs = value < 0 ? -1 : Math.Clamp(value, 250, 120000); SaveBotProfile(); }
+    partial void OnFocusKillMsChanged(int value) { _hub.SmartBot.FocusKillMs = value < 0 ? -1 : Math.Clamp(value, 250, 600000); SaveBotProfile(); }
```

Migration rule: if a profile has old `StuckSeconds` / `FocusKillSeconds` and no `TimingUnitsMigratedToMs` flag, set `StuckMs = StuckSeconds * 1000`, set `FocusKillMs = FocusKillSeconds < 0 ? -1 : FocusKillSeconds * 1000`, then persist `TimingUnitsMigratedToMs = true`.

## 4. Replace loose roam box numbers with slider+box controls and live overlay

Current state:
- Smart Bot has `Walk only inside box`, `Show walk box overlay`, and `Box X/Y/W/H` numeric boxes (`SmartBotView.axaml` lines 483-489).
- These are not paired sliders and do not make the visible work area the primary beginner feedback.

Proposed new leaf control:
- `src/4rVivi.App/Controls/SliderWithBox.axaml`
- `src/4rVivi.App/Controls/SliderWithBox.axaml.cs`
- Bind `Value`, `Minimum`, `Maximum`, `Label`, `Unit`.
- Use for `BoxX`, `BoxY`, `BoxW`, `BoxH`, and later combat-region dimensions.

```diff
diff --git a/src/4rVivi.App/Views/SmartBotView.axaml b/src/4rVivi.App/Views/SmartBotView.axaml
@@
-        <StackPanel Orientation="Horizontal" Spacing="14">
-          <CheckBox Content="Walk only inside box" IsChecked="{Binding UseWalkBox}" VerticalAlignment="Bottom"/>
-          <CheckBox Content="Show walk box overlay" IsChecked="{Binding ShowWalkBoxOverlay}" VerticalAlignment="Bottom"/>
-          <StackPanel><TextBlock Text="Box X" Classes="muted"/><NumericUpDown MinWidth="110" Minimum="0" Maximum="4000" Value="{Binding BoxX}"/></StackPanel>
-          <StackPanel><TextBlock Text="Box Y" Classes="muted"/><NumericUpDown MinWidth="110" Minimum="0" Maximum="4000" Value="{Binding BoxY}"/></StackPanel>
-          <StackPanel><TextBlock Text="Box W" Classes="muted"/><NumericUpDown MinWidth="110" Minimum="0" Maximum="4000" Value="{Binding BoxW}"/></StackPanel>
-          <StackPanel><TextBlock Text="Box H" Classes="muted"/><NumericUpDown MinWidth="110" Minimum="0" Maximum="4000" Value="{Binding BoxH}"/></StackPanel>
-        </StackPanel>
+        <WrapPanel>
+          <CheckBox Content="Use work area" IsChecked="{Binding UseWalkBox}" VerticalAlignment="Center" Margin="0,0,14,8"/>
+          <ToggleSwitch Content="Show work area" IsChecked="{Binding ShowWorkAreaOverlay}" VerticalAlignment="Center" Margin="0,0,14,8"/>
+        </WrapPanel>
+        <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto" IsVisible="{Binding ShowAdvancedUi}">
+          <ctrl:SliderWithBox Label="X" Unit="px" Minimum="0" Maximum="4000" Value="{Binding BoxX}"/>
+          <ctrl:SliderWithBox Grid.Column="1" Label="Y" Unit="px" Minimum="0" Maximum="4000" Value="{Binding BoxY}"/>
+          <ctrl:SliderWithBox Grid.Row="1" Label="Width" Unit="px" Minimum="100" Maximum="4000" Value="{Binding BoxW}"/>
+          <ctrl:SliderWithBox Grid.Row="1" Grid.Column="1" Label="Height" Unit="px" Minimum="100" Maximum="4000" Value="{Binding BoxH}"/>
+        </Grid>
```

Main/Agent 3 dependency: expose one observable region model that both the bot and overlay consume. The drawn rectangle must read the same values as the bot scan/roam logic. Add DebugTrace line `[WorkArea] combat=x,y,wxh roam=x,y,wxh`.

## 5. OCR Reader beginner/advanced split

Current state:
- Beginner screen still exposes monitor capture and DXGI (`OcrReaderView.axaml` lines 78-80), window mode, low-level filter/sharp/top/side controls (`lines 93-103`), text/monster/skills detection toggles (`lines 164-168`), plus a full advanced panel (`lines 172-240`).
- Right rail always shows marks, hotkeys, engine status, and training log (`lines 283-330`).
- Contract says client-window capture is primary for bot and monitor capture is manual/debug only.

Proposed beginner OCR screen:
- First row: `Capture client`, `Auto-detect`, `Use markers`, `Start OCR`, `Overlay`, `Show work area`.
- Keep Vision Assist GRF banner visible, but replace setup-heavy copy with runtime state: `Vision Assist active - monster OCR disabled` when enabled.
- Hide monitor capture, DXGI, window mode, filter/sharp/top/side, OCR internals, confidence sliders, training, marks list, hotkeys, raw engine status behind global Advanced.
- Keep `Refresh (-1 auto) ms` visible only if Advanced; beginner uses auto.

```diff
diff --git a/src/4rVivi.App/Views/OcrReaderView.axaml b/src/4rVivi.App/Views/OcrReaderView.axaml
@@
-  <Grid ColumnDefinitions="*,320" Margin="4">
+  <Grid ColumnDefinitions="*,320" Margin="4">
@@
-              <CheckBox Margin="0,0,10,8" Content="Monitor capture" IsChecked="{Binding UseMonitor}" VerticalAlignment="Center"/>
-              <CheckBox Margin="0,0,10,8" Content="DXGI default" IsChecked="{Binding UseDxgiCapture}" VerticalAlignment="Center"
+              <CheckBox Margin="0,0,10,8" Content="Monitor capture" IsChecked="{Binding UseMonitor}" VerticalAlignment="Center"
+                        IsVisible="{Binding ShowAdvanced}"/>
+              <CheckBox Margin="0,0,10,8" Content="DXGI default" IsChecked="{Binding UseDxgiCapture}" VerticalAlignment="Center"
+                        IsVisible="{Binding ShowAdvanced}"
@@
-              <ComboBox Width="200" Margin="0,0,10,8" ItemsSource="{Binding Monitors}" SelectedItem="{Binding SelectedMonitor}"/>
+              <ComboBox Width="200" Margin="0,0,10,8" ItemsSource="{Binding Monitors}" SelectedItem="{Binding SelectedMonitor}"
+                        IsVisible="{Binding ShowAdvanced}"/>
@@
-              <ComboBox Width="120" Margin="0,0,10,8" ItemsSource="{Binding WindowModes}" SelectedItem="{Binding WindowMode}"/>
+              <ComboBox Width="120" Margin="0,0,10,8" ItemsSource="{Binding WindowModes}" SelectedItem="{Binding WindowMode}"
+                        IsVisible="{Binding ShowAdvanced}"/>
```

```diff
diff --git a/src/4rVivi.App/Views/OcrReaderView.axaml b/src/4rVivi.App/Views/OcrReaderView.axaml
@@
-            <WrapPanel Grid.Row="2" Orientation="Horizontal">
+            <WrapPanel Grid.Row="2" Orientation="Horizontal" IsVisible="{Binding ShowAdvanced}">
@@
-            <CheckBox Margin="8,0,8,8" Content="Text" IsChecked="{Binding DetectText}" VerticalAlignment="Center"/>
-            <CheckBox Margin="0,0,8,8" Content="Monster overlay" IsChecked="{Binding DetectMonsters}" VerticalAlignment="Center"
+            <CheckBox Margin="8,0,8,8" Content="Text" IsChecked="{Binding DetectText}" VerticalAlignment="Center"
+                      IsVisible="{Binding ShowAdvanced}"/>
+            <CheckBox Margin="0,0,8,8" Content="Monster overlay" IsChecked="{Binding DetectMonsters}" VerticalAlignment="Center"
+                      IsVisible="{Binding ShowAdvanced}"
@@
-            <CheckBox Margin="0,0,8,8" Content="Skills and buffs" IsChecked="{Binding DetectSkills}" VerticalAlignment="Center"/>
+            <CheckBox Margin="0,0,8,8" Content="Skills and buffs" IsChecked="{Binding DetectSkills}" VerticalAlignment="Center"
+                      IsVisible="{Binding ShowAdvanced}"/>
+            <Button Margin="0,0,8,8" Command="{Binding ToggleWorkAreaCommand}">
+              <StackPanel Orientation="Horizontal" Spacing="6">
+                <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE7F4;" VerticalAlignment="Center"/>
+                <TextBlock Text="Show work area"/>
+              </StackPanel>
+            </Button>
```

```diff
diff --git a/src/4rVivi.App/Views/OcrReaderView.axaml b/src/4rVivi.App/Views/OcrReaderView.axaml
@@
-    <StackPanel Grid.Column="1" Spacing="10" Margin="12,0,0,0">
+    <StackPanel Grid.Column="1" Spacing="10" Margin="12,0,0,0" IsVisible="{Binding ShowAdvanced}">
```

## 6. Duplicate key/skill boxes cleanup

Current state:
- Hotbar cards cover skill/buff/teleport/Ygg/HP/SP/ammo/bag/loot/return/weapon/reconnect (`SmartBotView.axaml` lines 259-431).
- Separate `AutopotView` still exposes hotkey/pool/trigger rows, duplicating HP/SP potion config.
- Legacy shell views duplicate autopot, buffs, spammer, macro, and Smart Bot setup in `FourRToolsShellView.axaml` and `RoToolsShellView.axaml`.
- `SmartBotViewModel` still keeps legacy fields and sync paths, including `AttackKey`, `LootKey`, `TeleportKey`, `ReturnKey`, `BuffKeys`, `WeaponKey`, `AmmoKey`, `BuffButtons`, `Pots`, `LoadUnifiedActionsFromLegacy`, and `SyncBuffButtons`.

Proposed UI rule:
- Beginner: only the Smart Bot action hotbar edits action keys.
- Autopot tab becomes a read-only summary plus button `Edit in Smart Bot hotbar` if hotbar migration is complete.
- Legacy shells hidden from nav.
- Main can keep legacy VM/config migration code until the next profile version, but no new beginner UI should bind those duplicate fields.

```diff
diff --git a/src/4rVivi.App/Views/AutopotView.axaml b/src/4rVivi.App/Views/AutopotView.axaml
@@
-    <Button Classes="accent" Command="{Binding AddPotCommand}" HorizontalAlignment="Left">
+    <Button Classes="accent" Command="{Binding GoToSmartBotHotbarCommand}" HorizontalAlignment="Left">
@@
-        <TextBlock Text="Add potion rule" VerticalAlignment="Center"/>
+        <TextBlock Text="Edit potion keys in Smart Bot hotbar" VerticalAlignment="Center"/>
@@
-    <ItemsControl ItemsSource="{Binding Pots}">
+    <ItemsControl ItemsSource="{Binding Pots}" IsEnabled="{Binding ShowAdvancedUi}">
```

If `AutopotView` must remain independently editable for non-bot users, gate editing behind global Advanced and label it `Advanced standalone autopot rules`.

FINDINGS:
- `SmartBotView.axaml` is close to the target hotbar model, but beginner mode is overloaded by driver setup, training, and debug controls. `ShowAdvancedTiming` is not a broad enough global Advanced gate.
- Timer labels are mixed and unsafe: `Stuck seconds` and `Focus kill sec` live next to ms fields. This needs a VM/config migration, not just label edits.
- The work-area requirement is partially present as `Box X/Y/W/H` and `Show walk box overlay`, but it is not a beginner-friendly `Show work area` button and not slider+numeric paired/live as requested.
- OCR Reader should default to client-window capture and hide monitor/DXGI details unless Advanced. Current copy advertises DXGI on the first screen.
- Legacy shells are still exposed in primary nav. They contain duplicate key/potion/buff/spam setup and should be hidden or moved to Advanced/Integrations.
- `RolePalette` still includes dead/legacy HP/SP roles (`HP`, `MaxHP`, `HP / MaxHP`, `SP / MaxSP`, `HP Bar`, `SP Bar`). UI cleanup should remove them from user-facing marker choices once Agent 2/main removes the dead writers.

TESTS TO RUN:
- `dotnet build 4rVivi.sln -c Release`
- Add or update a profile migration test for `StuckSeconds`/`FocusKillSeconds` to `StuckMs`/`FocusKillMs`.
- Manual UI smoke: open Smart Bot in beginner mode and confirm only Start/Stop, readiness, hotbar, hunt basics, and work-area controls are visible.
- Manual UI smoke: toggle Advanced and confirm driver/backend/debug/training/monitor/OCR internals appear.
- Manual UI smoke: enable Vision Assist GRF and confirm `Vision Assist active - monster OCR disabled` appears and monster overlay controls are not prominent.
- Manual UI smoke: drag each work-area slider and confirm the numeric value and overlay rectangle update in the same frame.

EVIDENCE:
- Audit-only handoff. No build or UI run was performed because Agent 4 scope is proposal-only.
- Required specs were read: `CONTRACTS.md` and master-plan sections 2.1, 3 Q11/Q13, and 8.
- Big AXAML files were inspected but not edited.

RISKS:
- Hiding `AutopotView` editing could surprise users who use standalone autopot without Smart Bot. If that workflow must stay, put it behind Advanced and keep the beginner message pointing to the hotbar.
- Removing legacy VM fields too early can break profile migration. Main should keep migration/read compatibility until profiles are rewritten with hotbar-only config.
- A global Advanced toggle needs a single shared setting; otherwise Smart Bot `ShowAdvancedTiming`, OCR `ShowAdvanced`, and nav visibility will drift.
- Work-area overlay must consume the same region model as bot scanning/roaming. A separate display-only copy would violate section 8.3.

DO NOT TOUCH:
- `src/4rVivi.App/Views/SmartBotView.axaml` directly from Agent 4 branch/session.
- `src/4rVivi.App/Views/OcrReaderView.axaml` directly from Agent 4 branch/session.
- VIIPER, FakerInput, ViGEm, reWASD routing/backends.
- Frozen contract files; main Codex owns them.

CONTRACT IMPACT: none. This handoff proposes UI/VM/profile migration work that main should apply under the existing contracts.
