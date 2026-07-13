# Agent 4 Task 2 UI Leaf Controls + Nav Handoff

AGENT: 4 / Newton UI-UX

OBJECTIVE:
- Continue from `docs/superpowers/specs/agent-4-ui-cleanup-handoff.md`.
- Implement only disjoint/new UI leaf files if safe.
- Do not edit `SmartBotView.axaml` or `OcrReaderView.axaml`.
- Audit MainWindow nav hiding for 4RTools / ro-tools under Advanced and produce exact patch guidance.

FILES CREATED:
- `src/4rVivi.App/Controls/SliderWithBox.axaml`
- `src/4rVivi.App/Controls/SliderWithBox.axaml.cs`
- `src/4rVivi.App/Styles/AutoPill.axaml`
- `docs/superpowers/specs/agent-4-task2-ui-leaf-controls-nav.md`

FILES INSPECTED:
- `src/4rVivi.App/Controls/SearchPicker.axaml`
- `src/4rVivi.App/Controls/SearchPicker.axaml.cs`
- `src/4rVivi.App/Styles/Controls.axaml`
- `src/4rVivi.App/App.axaml`
- `src/4rVivi.App/4rVivi.App.csproj`
- `src/4rVivi.App/ViewModels/MainWindowViewModel.cs`
- `src/4rVivi.App/Views/MainWindow.axaml`
- `src/4rVivi.Core/Settings/AppSettings.cs`
- `src/4rVivi.App/ViewModels/SettingsViewModel.cs`
- `src/4rVivi.App/Views/SettingsView.axaml`

LEAF IMPLEMENTATION:

## SliderWithBox

Created a reusable Avalonia `UserControl` with:
- `Label`
- `Unit`
- `Minimum`
- `Maximum`
- `Step`
- `LargeStep`
- `Value` with two-way binding
- `SnapToStep`
- `ShowRange`
- `FormatString`

Intended later use in Smart Bot / OCR big views:

```xml
<ctrl:SliderWithBox Label="Width"
                    Unit="px"
                    Minimum="100"
                    Maximum="4000"
                    Step="10"
                    LargeStep="100"
                    Value="{Binding BoxW}"/>
```

This is ready for the section 8.3 slider + numeric box requirement. It does not create any view-model dependency and does not touch the two big AXAML files.

## AutoPill style resource

Created `src/4rVivi.App/Styles/AutoPill.axaml` as a standalone style dictionary with:
- `Border.auto-pill`
- `Border.auto-pill.manual`
- `TextBlock.auto-pill`
- `TextBlock.auto-pill.manual`

The style is intentionally not wired into `App.axaml` from this task because that is app-level wiring, not a leaf-only change. Main can load it with:

```diff
diff --git a/src/4rVivi.App/App.axaml b/src/4rVivi.App/App.axaml
@@
     <StyleInclude Source="avares://4rVivi/Styles/Colors.axaml"/>
     <StyleInclude Source="avares://4rVivi/Styles/Controls.axaml"/>
+    <StyleInclude Source="avares://4rVivi/Styles/AutoPill.axaml"/>
```

Intended later use:

```xml
<Border Classes="auto-pill">
  <TextBlock Classes="auto-pill" Text="-1 = Auto"/>
</Border>
```

For manual timing values:

```xml
<Border Classes="auto-pill manual">
  <TextBlock Classes="auto-pill manual" Text="Manual"/>
</Border>
```

MAINWINDOW NAV AUDIT:

Current nav wiring in `MainWindowViewModel.cs`:

```csharp
AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
```

Findings:
- `Legacy 4rTools` and `Legacy ro-tools` are still exposed as first-class Tools tabs.
- There is no app-wide `ShowAdvancedUi` setting yet.
- The only existing advanced flag found is Smart Bot profile-specific: `SmartBotConfig.ShowAdvancedTiming`.
- `SettingsView` currently exposes language, theme, opacity, and humanized timing, but no global Beginner/Advanced toggle.

PATCH GUIDANCE FOR MAIN:

## 1. Add app-wide Advanced setting

```diff
diff --git a/src/4rVivi.Core/Settings/AppSettings.cs b/src/4rVivi.Core/Settings/AppSettings.cs
@@
     public bool HumanizeTiming { get; set; } = true;
+    public bool ShowAdvancedUi { get; set; }
     public bool AcrylicBackdrop { get; set; } = true;
```

## 2. Surface it in Settings

```diff
diff --git a/src/4rVivi.App/ViewModels/SettingsViewModel.cs b/src/4rVivi.App/ViewModels/SettingsViewModel.cs
@@
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

```diff
diff --git a/src/4rVivi.App/Views/SettingsView.axaml b/src/4rVivi.App/Views/SettingsView.axaml
@@
           <CheckBox Content="Humanize input timing" IsChecked="{Binding HumanizeTiming}"/>
+          <CheckBox Content="Advanced UI" IsChecked="{Binding ShowAdvancedUi}"/>
```

## 3. Mirror it in MainWindowViewModel and rebuild nav on toggle

Suggested approach: keep the injected legacy shell VMs in private fields, because the nav must be rebuilt when Advanced toggles.

```diff
diff --git a/src/4rVivi.App/ViewModels/MainWindowViewModel.cs b/src/4rVivi.App/ViewModels/MainWindowViewModel.cs
@@
     private readonly SmartBotViewModel _smartBot;
+    private readonly GrfViewModel _grf;
+    private readonly SpriteViewerViewModel _sprite;
+    private readonly HomunAiViewModel _homun;
+    private readonly ToolsLauncherViewModel _tools;
+    private readonly FourRToolsShellViewModel _fourRTools;
+    private readonly RoToolsShellViewModel _roTools;
@@
     [ObservableProperty] private string _spText = "";
+    [ObservableProperty] private bool _showAdvancedUi;
@@
         _session = session; _hub = hub; _procs = procs; _settings = settings; _loc = loc; _nav = nav;
         _smartBot = smartBot;
+        _grf = grf; _sprite = sprite; _homun = homun; _tools = tools;
+        _fourRTools = fourRTools; _roTools = roTools;
 
-        AddCat("Home", ("Dashboard", dashboard));
-        AddCat("Bot", ("Smart Bot", smartBot), ("Multi Client", multiClient), ("OCR Reader", ocrReader), ("Overlay", overlay), ("Macros", macros));
-        AddCat("Trackers", ("MVP", mvp), ("Buff HUD", hud), ("Loot Log", loot), ("Stats", stats));
-        AddCat("Data", ("Calculator", calc), ("Database", database));
-        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
-        AddCat("System", ("Auto-Detect", autoDetect), ("Servers", servers), ("Settings", settingsVm));
-        if (Categories.Count > 0) OnCategorySelected(Categories[0]);
+        ShowAdvancedUi = _settings.Current.ShowAdvancedUi;
+        BuildNavigation(dashboard, smartBot, multiClient, ocrReader, overlay, macros, mvp, hud, loot, stats, calc, database, autoDetect, servers, settingsVm);
```

Add helper:

```csharp
private void BuildNavigation(
    DashboardViewModel dashboard,
    SmartBotViewModel smartBot,
    MultiClientViewModel multiClient,
    OcrReaderViewModel ocrReader,
    OverlayViewModel overlay,
    MacrosViewModel macros,
    MvpTrackerViewModel mvp,
    HudViewModel hud,
    LootViewModel loot,
    StatsViewModel stats,
    CalculatorViewModel calc,
    DatabaseViewModel database,
    AutoDetectViewModel autoDetect,
    ServersViewModel servers,
    SettingsViewModel settingsVm)
{
    Categories.Clear();
    _pageByKey.Clear();
    AddCat("Home", ("Dashboard", dashboard));
    AddCat("Bot", ("Smart Bot", smartBot), ("Multi Client", multiClient), ("OCR Reader", ocrReader), ("Overlay", overlay), ("Macros", macros));
    AddCat("Trackers", ("MVP", mvp), ("Buff HUD", hud), ("Loot Log", loot), ("Stats", stats));
    AddCat("Data", ("Calculator", calc), ("Database", database));
    AddCat("Tools", ("GRF", _grf), ("Sprite", _sprite), ("Homun AI", _homun), ("External Editors", _tools));
    if (ShowAdvancedUi)
        AddCat("Integrations", ("Legacy 4rTools", _fourRTools), ("Legacy ro-tools", _roTools));
    AddCat("System", ("Auto-Detect", autoDetect), ("Servers", servers), ("Settings", settingsVm));
    if (Categories.Count > 0) OnCategorySelected(Categories[0]);
}
```

Add a changed hook. Main may prefer a navigation service event; this is the smallest local patch:

```csharp
partial void OnShowAdvancedUiChanged(bool value)
{
    _settings.Current.ShowAdvancedUi = value;
    _settings.Save();
    // RebuildNavigation(); requires retaining all constructor VMs as fields or a lighter nav item model.
}
```

If main wants to avoid retaining all nav VMs, use the simpler tonight-safe patch:

```diff
diff --git a/src/4rVivi.App/ViewModels/MainWindowViewModel.cs b/src/4rVivi.App/ViewModels/MainWindowViewModel.cs
@@
-        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
+        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools));
+        if (_settings.Current.ShowAdvancedUi)
+            AddCat("Integrations", ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
```

This does not live-refresh nav, but it hides legacy shells by default and exposes them only after restart with Advanced enabled.

## 4. Optional top-bar toggle

If main wants the Beginner/Advanced switch in the top bar instead of Settings only:

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

VERIFICATION:
- Ran `dotnet build src\4rVivi.App\4rVivi.App.csproj -c Release --no-restore`
- Result: build succeeded with 0 warnings and 0 errors after removing unsupported `Grid.ColumnSpacing` from the new control.

DO NOT TOUCH CONFIRMATION:
- This task did not edit `src/4rVivi.App/Views/SmartBotView.axaml`.
- This task did not edit `src/4rVivi.App/Views/OcrReaderView.axaml`.
- Those files were already dirty in the worktree before Task 2 and remain outside this task's edits.

NEXT MAIN INTEGRATION STEPS:
1. Load `Styles/AutoPill.axaml` from `App.axaml`.
2. Add global `ShowAdvancedUi` to settings and main VM.
3. Hide legacy shells by default using the nav patch above.
4. Use `SliderWithBox` in Smart Bot work-area controls only when main is ready to edit `SmartBotView.axaml`.
5. Replace `-1 = Auto` helper borders with `auto-pill` style only when main is ready to edit target views.
