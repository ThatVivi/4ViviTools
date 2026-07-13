# Agent 4 Task 3 Slider/AutoPill/Advanced Nav Handoff

AGENT: 4 / Newton UI-UX

OBJECTIVE:
- Continue from Task 2.
- Audit `SliderWithBox` and `AutoPill` after main fixed SliderWithBox spacing.
- Propose exact integration points that avoid risky edits to the big AXAML files.
- Design the Advanced toggle/nav hiding patch with exact settings and ViewModel properties.
- Keep work legitimate UI/product research only.

FILES CREATED / EDITED BY THIS TASK:
- `docs/superpowers/specs/agent-4-task3-slider-autopill-advanced-nav.md`

FILES READ:
- `docs/CODEX-MAP.md`
- `docs/USER_GUIDE.md`
- `docs/PROJECT_IMPROVEMENT_PLAN.md`
- `docs/PROJECT_KNOWLEDGE_BASE.md` relevant UI sections
- `docs/superpowers/specs/CONTRACTS.md`
- `docs/superpowers/specs/RUN-LOG-2026-07-13.md`
- `docs/superpowers/specs/2026-07-13-claude-overnight-master-plan.md`
- `docs/superpowers/specs/2026-07-13-claude-hp-sp-percent-ocr-question.md` UI packet
- `docs/superpowers/specs/2026-07-13-claude-hard-attach-reply.md`
- `docs/superpowers/specs/agent-4-ui-cleanup-handoff.md`
- `docs/superpowers/specs/agent-4-task2-ui-leaf-controls-nav.md`
- `docs/rathena/4rtools-ui-spec.md`
- `docs/rathena/ro-tools-ui-spec.md`
- `src/4rVivi.App/Controls/SliderWithBox.axaml`
- `src/4rVivi.App/Controls/SliderWithBox.axaml.cs`
- `src/4rVivi.App/Styles/AutoPill.axaml`
- `src/4rVivi.App/ViewModels/MainWindowViewModel.cs`
- `src/4rVivi.App/Views/MainWindow.axaml`
- `src/4rVivi.Core/Settings/AppSettings.cs`
- `src/4rVivi.App/ViewModels/SettingsViewModel.cs`
- `src/4rVivi.App/Views/SettingsView.axaml`
- `src/4rVivi.App/App.axaml`

EXTERNAL UI RESEARCH READ:
- 4RTools GitHub README: all-in-one RO utility with ON/OFF, Autopot, AHK Spammer, Autobuff, Song Macro, Macro Switch/Chain, ATK x DEF.
  Source: https://github.com/4RTools/4RTools
- Official Ragnarok Online keyboard shortcuts: F1-F9 use items/skills in the Shortcut Keys window; F12 opens/closes Shortcut Keys.
  Source: https://renewal.playragnarok.com/gameguide/howtoplay_interface03_test.aspx
- Ragnarok Wiki hotkeys page: older RO used F1-F9 rows toggled by F12; Renewal supports custom hotkeys and up to four visible hotkey rows.
  Source: https://ragnarok.fandom.com/wiki/Hotkeys
- ro-tools GitHub profile/search result: automation tool focused on buffs, automatic item use, macros, and skills.
  Source: https://github.com/uniaodk

PRODUCT READOUT:
- RO players think in hotbar rows, item/skill slots, F1-F9, and skill/item icons.
- 4RTools/ro-tools style tools are dense but task-grouped: HP/SP, Skill Spammer, Buffs, Macro, Auto Element, Utilities.
- 4ViviTools should keep the hotbar/action-card mental model, but beginner mode should hide duplicate legacy shells and driver/OCR internals.
- Use `4ViviTools` branding in beginner mode; preserve 4RTools/ro-tools concepts only as Advanced -> Integrations or internal data-source wording.
- Keep AutoPot available as both a standalone manual-player tool and a compact Smart Bot surface, backed by one shared service/source of truth. Do not collapse standalone AutoPot into read-only status unless main explicitly chooses that product direction.
- Timing/threshold UI should consistently explain sentinel values as `-1 = Auto` and, where supported by a specific control, `0 = Off`.
- Longer-term beginner navigation can become a RO workflow such as `Play` / `Farm`, with OCR, overlay, and diagnostics pushed into focused subpanels. The immediate safe patch is just hiding legacy shells behind Advanced.

LEAF FILE AUDIT:

## SliderWithBox

Current file state:
- `SliderWithBox.axaml` uses margin-based spacing, not `Grid.ColumnSpacing`.
- It has stable columns: label, slider, numeric box, unit.
- `Value` is a two-way styled property.
- `Minimum`, `Maximum`, `Step`, `LargeStep`, `SnapToStep`, `ShowRange`, and `FormatString` cover the Smart Bot work-area use case.
- The range label updates through `RangeText`.

Assessment:
- Good for section 8.3: slider move updates the numeric box through the same `Value`; numeric entry updates slider through the same binding.
- Good for region dimensions and timing values where the target VM can expose `double` or Avalonia can convert numeric values.
- For current `int` VM properties such as `BoxX`, `BoxY`, `BoxW`, `BoxH`, prefer adding double proxy properties in the VM or keep using `NumericUpDown` until the VM is ready. Avalonia usually converts, but exact integer clamping should live in the VM for bot safety.

Recommended usage contract:

```xml
<ctrl:SliderWithBox Label="Width"
                    Unit="px"
                    Minimum="100"
                    Maximum="{Binding ClientWidth}"
                    Step="10"
                    LargeStep="100"
                    Value="{Binding WorkAreaWidth, Mode=TwoWay}"/>
```

Avoid binding this directly to a display-only copy of the region. The same source must feed:
- Smart Bot scan/roam logic.
- Overlay rectangle.
- SliderWithBox value.

## AutoPill

Current file state:
- `AutoPill.axaml` defines `Border.auto-pill`, `Border.auto-pill.manual`, `TextBlock.auto-pill`, and `TextBlock.auto-pill.manual`.
- It is not included in `App.axaml` yet.

Assessment:
- Good as a passive style resource.
- Not enough by itself to hide `-1`; main still needs either:
  - VM display properties such as `MoveWaitModeText`, or
  - a converter for `-1 -> Auto` and non-negative values -> `Manual`.

Recommended first integration:

```diff
diff --git a/src/4rVivi.App/App.axaml b/src/4rVivi.App/App.axaml
@@
     <StyleInclude Source="avares://4rVivi/Styles/Colors.axaml"/>
     <StyleInclude Source="avares://4rVivi/Styles/Controls.axaml"/>
+    <StyleInclude Source="avares://4rVivi/Styles/AutoPill.axaml"/>
```

Recommended minimal use:

```xml
<Border Classes="auto-pill">
  <TextBlock Classes="auto-pill" Text="Auto"/>
</Border>
```

Recommended manual use:

```xml
<Border Classes="auto-pill manual">
  <TextBlock Classes="auto-pill manual" Text="Manual"/>
</Border>
```

INTEGRATION POINTS THAT MINIMIZE BIG-AXAML RISK:

## A. Add new leaf panels, then mount with one line later

Avoid doing large in-place rewrites in `SmartBotView.axaml` and `OcrReaderView.axaml`. Instead, main should create small controls and then replace noisy sections with one `ContentControl` or one custom control line.

Recommended new leaf controls:
- `src/4rVivi.App/Controls/AutoTimingPill.axaml` + `.cs`
- `src/4rVivi.App/Controls/WorkAreaEditor.axaml` + `.cs`
- `src/4rVivi.App/Controls/AdvancedStatusStrip.axaml` + `.cs`

Suggested `WorkAreaEditor` internal layout:

```xml
<StackPanel Spacing="8">
  <WrapPanel>
    <ToggleSwitch Content="Show work area" IsChecked="{Binding ShowWorkAreaOverlay}"/>
    <Border Classes="auto-pill" Margin="8,0,0,0">
      <TextBlock Classes="auto-pill" Text="{Binding WorkAreaSummary}"/>
    </Border>
  </WrapPanel>
  <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto" IsVisible="{Binding ShowAdvancedUi}">
    <ctrl:SliderWithBox Label="X" Unit="px" Minimum="0" Maximum="{Binding ClientWidth}" Value="{Binding WorkAreaX}"/>
    <ctrl:SliderWithBox Grid.Column="1" Label="Y" Unit="px" Minimum="0" Maximum="{Binding ClientHeight}" Value="{Binding WorkAreaY}"/>
    <ctrl:SliderWithBox Grid.Row="1" Label="Width" Unit="px" Minimum="100" Maximum="{Binding ClientWidth}" Value="{Binding WorkAreaWidth}"/>
    <ctrl:SliderWithBox Grid.Row="1" Grid.Column="1" Label="Height" Unit="px" Minimum="100" Maximum="{Binding ClientHeight}" Value="{Binding WorkAreaHeight}"/>
  </Grid>
</StackPanel>
```

Then the eventual big-view integration is one line:

```xml
<ctrl:WorkAreaEditor DataContext="{Binding WorkArea}"/>
```

This is the safest path because the real layout/test work happens in leaf files, while the big AXAML change stays small and reviewable.

## B. Keep beginner controls RO-native

Beginner labels should use RO vocabulary:
- `Hotbar actions`, not `SkillButtons`.
- `F1-F9 / 1-9 / Q-O / A-L / Z-M` where key rows are shown.
- `HP %`, `SP %`, `Fly Wing / Teleport`, `Ygg`, `Buff`, `Ammo`.
- `Shortcut Keys`, with tooltip "RO opens these with F12."

Avoid showing backend/vendor names in beginner mode:
- Use `Input ready` / `Input needs setup` / `Manage input`.
- Put `VIIPER`, `FakerInput`, `ViGEm`, `reWASD` in Advanced details/tooltips only.

## C. Status strip first, settings second

From the hard-attach reply, the visible status should be neutral and actionable:

```text
Client: Focused / Not focused
OCR: Running / Paused
Bot: Running / Paused
Input: Ready / Needs setup
```

Do not use red for normal not-focused/paused states; red is danger-only.

ADVANCED TOGGLE + NAV HIDING DESIGN:

Current code still has no app-wide `ShowAdvancedUi`, and `Legacy 4rTools` / `Legacy ro-tools` are still under `Tools`.

Recommended behavior:
- Beginner is default: `ShowAdvancedUi = false`.
- Top bar has `Beginner` / `Advanced` toggle.
- Settings also has the same persisted toggle.
- Legacy shells are hidden when `ShowAdvancedUi == false`.
- When `ShowAdvancedUi == true`, expose one `Integrations` category containing:
  - `Legacy 4rTools`
  - `Legacy ro-tools`
  - later `RO Client Data`

## Exact settings patch

```diff
diff --git a/src/4rVivi.Core/Settings/AppSettings.cs b/src/4rVivi.Core/Settings/AppSettings.cs
@@
     public bool HumanizeTiming { get; set; } = true;
+    public bool ShowAdvancedUi { get; set; } = false;
     public bool AcrylicBackdrop { get; set; } = true;
```

## Exact SettingsViewModel patch

```diff
diff --git a/src/4rVivi.App/ViewModels/SettingsViewModel.cs b/src/4rVivi.App/ViewModels/SettingsViewModel.cs
@@
     [ObservableProperty] private bool _humanizeTiming = true;
+    [ObservableProperty] private bool _showAdvancedUi;
@@
         WindowOpacity = c.WindowOpacity;
         HumanizeTiming = c.HumanizeTiming;
+        ShowAdvancedUi = c.ShowAdvancedUi;
@@
         c.WindowOpacity = Math.Clamp(WindowOpacity, 15, 100);
         c.HumanizeTiming = HumanizeTiming;
+        c.ShowAdvancedUi = ShowAdvancedUi;
```

## Exact SettingsView patch

```diff
diff --git a/src/4rVivi.App/Views/SettingsView.axaml b/src/4rVivi.App/Views/SettingsView.axaml
@@
           <CheckBox Content="Humanize input timing" IsChecked="{Binding HumanizeTiming}"/>
+          <CheckBox Content="Advanced UI" IsChecked="{Binding ShowAdvancedUi}"/>
```

## Exact MainWindowViewModel patch, safe restart-only version

This is the lowest-risk patch. It hides legacy shells by default and exposes them only on next launch after Advanced is saved.

```diff
diff --git a/src/4rVivi.App/ViewModels/MainWindowViewModel.cs b/src/4rVivi.App/ViewModels/MainWindowViewModel.cs
@@
-        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
+        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools));
+        if (_settings.Current.ShowAdvancedUi)
+            AddCat("Integrations", ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
```

Use this if main wants minimum merge risk tonight.

## Exact MainWindowViewModel patch, preferred live-toggle version

Add a small nav spec model so rebuilding nav does not require dozens of fields:

```csharp
private sealed record NavPageSpec(string Title, ViewModelBase ViewModel);
private sealed record NavCategorySpec(string Title, bool AdvancedOnly, NavPageSpec[] Pages);
private readonly List<NavCategorySpec> _navSpecs = new();
```

Add property:

```csharp
[ObservableProperty] private bool _showAdvancedUi;
```

In constructor, replace direct `AddCat(...)` calls:

```csharp
ShowAdvancedUi = _settings.Current.ShowAdvancedUi;
_navSpecs.Add(new("Home", false, new[] { new NavPageSpec("Dashboard", dashboard) }));
_navSpecs.Add(new("Bot", false, new[] {
    new NavPageSpec("Smart Bot", smartBot),
    new NavPageSpec("Multi Client", multiClient),
    new NavPageSpec("OCR Reader", ocrReader),
    new NavPageSpec("Overlay", overlay),
    new NavPageSpec("Macros", macros),
}));
_navSpecs.Add(new("Trackers", false, new[] {
    new NavPageSpec("MVP", mvp),
    new NavPageSpec("Buff HUD", hud),
    new NavPageSpec("Loot Log", loot),
    new NavPageSpec("Stats", stats),
}));
_navSpecs.Add(new("Data", false, new[] {
    new NavPageSpec("Calculator", calc),
    new NavPageSpec("Database", database),
}));
_navSpecs.Add(new("Tools", false, new[] {
    new NavPageSpec("GRF", grf),
    new NavPageSpec("Sprite", sprite),
    new NavPageSpec("Homun AI", homun),
    new NavPageSpec("External Editors", tools),
}));
_navSpecs.Add(new("Integrations", true, new[] {
    new NavPageSpec("Legacy 4rTools", fourRTools),
    new NavPageSpec("Legacy ro-tools", roTools),
}));
_navSpecs.Add(new("System", false, new[] {
    new NavPageSpec("Auto-Detect", autoDetect),
    new NavPageSpec("Servers", servers),
    new NavPageSpec("Settings", settingsVm),
}));
RebuildNavigation();
```

Add helper:

```csharp
private void RebuildNavigation(string? preferredPageKey = null)
{
    preferredPageKey ??= (CurrentPage as ViewModelBase) is null
        ? null
        : _pageByKey.FirstOrDefault(kvp => ReferenceEquals(kvp.Value.ViewModel, CurrentPage)).Key;

    Categories.Clear();
    _pageByKey.Clear();

    foreach (var spec in _navSpecs.Where(x => !x.AdvancedOnly || ShowAdvancedUi))
        AddCat(spec.Title, spec.Pages.Select(p => (p.Title, p.ViewModel)).ToArray());

    if (!string.IsNullOrWhiteSpace(preferredPageKey) && _pageByKey.TryGetValue(preferredPageKey, out var page))
    {
        var category = Categories.FirstOrDefault(c => c.Pages.Contains(page));
        if (category is not null)
        {
            OnCategorySelected(category);
            OnPageSelected(page);
            return;
        }
    }

    if (Categories.Count > 0)
        OnCategorySelected(Categories[0]);
}
```

Add property hook:

```csharp
partial void OnShowAdvancedUiChanged(bool value)
{
    _settings.Current.ShowAdvancedUi = value;
    _settings.Save();
    RebuildNavigation();
}
```

## Exact MainWindow.axaml patch for top-bar toggle

```diff
diff --git a/src/4rVivi.App/Views/MainWindow.axaml b/src/4rVivi.App/Views/MainWindow.axaml
@@
-      <Grid ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,*,Auto,Auto" VerticalAlignment="Center" Margin="14,0">
+      <Grid ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto" VerticalAlignment="Center" Margin="14,0">
@@
-        <Button Grid.Column="7" MinWidth="132" Command="{Binding StopAllCommand}">
+        <ToggleSwitch Grid.Column="7"
+                      Margin="0,0,12,0"
+                      OffContent="Beginner"
+                      OnContent="Advanced"
+                      IsChecked="{Binding ShowAdvancedUi}"/>
+        <Button Grid.Column="8" MinWidth="132" Command="{Binding StopAllCommand}">
```

RISKS / NOTES:
- Settings-only Advanced is lower-risk but not live. Top-bar Advanced is better UX and makes the mode obvious.
- If the user toggles Advanced off while currently on `Integrations`, `RebuildNavigation()` must navigate away to a safe default.
- Do not expose `Legacy 4rTools` / `Legacy ro-tools` names in beginner mode. The beginner mental model should be 4ViviTools + RO hotbar, not multiple products.
- `ShowAdvancedTiming` should remain a Smart Bot sub-option only if needed; it should not be the global mode.
- Advanced only reveals diagnostics and power-user settings; supported input-routing behavior should stay the same.
- After main integrates the millisecond labels, Advanced nav, and Auto/Off sentinel displays, update `docs/CODEX-MAP.md` and `docs/USER_GUIDE.md` in the same pass so user-facing docs do not keep saying only "seconds" or exposing legacy shells as primary flows.

TESTS TO RUN FOR MAIN INTEGRATION:
- `dotnet build 4rVivi.sln -c Release`
- Manual: default profile starts with no `Integrations` category.
- Manual: enabling Advanced shows `Integrations`; disabling Advanced hides it and navigates away if selected.
- Manual: `AutoPill.axaml` loads without missing-resource errors after adding the `StyleInclude`.
- Manual: `SliderWithBox` drag updates bound number and overlay source in the same frame.

EVIDENCE FROM THIS TASK:
- Read the requested docs and UI/UX specs listed above.
- Read the current leaf control/style files.
- Read current nav/settings files; `ShowAdvancedUi` is not yet present and legacy shells are still in `Tools`.
- No forbidden files were edited by this task.

DO NOT TOUCH CONFIRMATION:
- Did not edit `src/4rVivi.App/Views/SmartBotView.axaml`.
- Did not edit `src/4rVivi.App/Views/OcrReaderView.axaml`.
- Did not edit `src/4rVivi.App/Views/MainWindow.axaml`.
- Did not edit shared ViewModels or settings.

CONTRACT IMPACT:
- None. This is UI surfacing and navigation hiding under the existing contracts.
