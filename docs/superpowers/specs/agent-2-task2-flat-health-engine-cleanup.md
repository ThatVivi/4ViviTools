# Agent 2 Task 2 - Flat Health and OCR Engine Cleanup

Date: 2026-07-13
Agent: Agent 2 / Plato scope
Mode: audit-only handoff, proposed diffs only

## Scope

Create a concrete cleanup checklist and proposed diffs for removing or quarantining flat HP/SP reads and stabilizing OCR engine state. This document intentionally does not edit shared runtime code.

## Current Snapshot

The main branch has already moved since the first OCR/health handoff.

- `OcrReaderViewModel` now converts stale HP/SP bar marks into percent-text readers before the generic bar-fill path.
- `OcrService` now blocks vital HP/SP roles from the non-vital bar fallback helpers.
- `LiveStatSource.BarFill` is now documented as non-vital only, with HP/SP safety consumers expected to use `PercentText`.

Remaining cleanup is mostly contract hardening:

- Flat HP/SP values are still public through `CharacterState`, `HealthReader`, `StatReader`, Discord presence, and several consumers.
- OCR engine selection is still per-mark and per-toggle, so HP/SP percent text can be routed through Windows or Ensemble unless explicitly pinned.
- Fallback OCR paths can still publish text from a different engine than the UI implies.

## Exact File and Line Map

### Discord RPC

`src/4rVivi.App/Services/DiscordPresenceBootstrap.cs`

- Lines 55-57 copy `cs.HpPct`, `cs.SpPct`, and also flat `cs.Hp`, `cs.MaxHp`, `cs.Sp`, `cs.MaxSp` into `RoPresence`.

`src/4rVivi.Core/Discord/RoPresence.cs`

- Lines 25-30 expose both percent and flat HP/SP fields.
- Lines 56-57 prefer flat `HP {Hp}/{MaxHp}` and `SP {Sp}/{MaxSp}` in `StateLine`.

Cleanup target: Discord RPC should display trusted percent text only. Flat HP/SP should not be a presence contract.

### Stats View Model

`src/4rVivi.App/ViewModels/StatsViewModel.cs`

- Line 51 reads flat `_stat.Hp`, `_stat.MaxHp`, `_stat.Sp`, `_stat.MaxSp`.
- Line 52 sends flat HP into `_session.Observe(hp)`.
- Lines 58-61 correctly render `_stat.HpPercent` and `_stat.SpPercent`.

Related:

`src/4rVivi.Core/Trackers/SessionTracker.cs`

- Lines 16-24 store and observe flat HP for death counting.

Cleanup target: `StatsViewModel` should stop reading flat HP/SP. Death counting should be based on trusted percent only, or disabled until a real death signal exists.

### Character State

`src/4rVivi.Core/Game/CharacterState.cs`

- Lines 13-16 expose flat `Hp`, `MaxHp`, `Sp`, `MaxSp`.
- Lines 22-23 expose trusted `HpPct`, `SpPct`.
- Lines 49-59 read flat max/current HP/SP from `LiveStats`.
- Line 76 treats drops in either flat HP/SP or percent HP/SP as fighting activity.

Cleanup target: `CharacterState` should snapshot trusted HP/SP percent only. Flat fields should be deleted or moved to diagnostic-only state that cannot drive safety, activity, or RPC.

### HealthReader and StatReader

`src/4rVivi.Core/Game/HealthReader.cs`

- Lines 13-16 expose flat `Hp`, `MaxHp`, `Sp`, `MaxSp`.
- Lines 18-19 expose trusted `HpPercent`, `SpPercent`.
- Lines 21-26 read any requested role from `LiveStats` or memory fallback.

`src/4rVivi.Core/Game/StatReader.cs`

- Lines 9-12 expose flat `Hp`, `MaxHp`, `Sp`, `MaxSp`.
- Lines 20-21 expose trusted `HpPercent`, `SpPercent`.

Cleanup target: public health readers should not expose flat HP/SP as normal safety signals. If needed for diagnostics, rename them to diagnostic APIs and keep them out of safety consumers.

### Multi Client

`src/4rVivi.App/ViewModels/MultiClientViewModel.cs`

- Lines 315-321 still parse `"HP / MaxHP"` and `"SP / MaxSP"` flat pairs.
- Lines 325 and 332 treat raw `"HP"` and `"SP"` roles as percent readers.
- Lines 358-363 normalize legacy `"HP Bar"` and `"SP Bar"` to percent roles.

Cleanup target: MultiClient should remove flat HP/SP pair display and require explicit percent-text roles for HP/SP. Legacy bar labels may normalize to percent text for old profiles, but must not imply a bar-fill read.

### OcrReader EngineFor and WindowsForNumbers

`src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`

- Line 197 defines `WindowsForNumbers`.
- Line 198 defines `Ensemble`.
- Lines 249-250 persist both toggles.
- Lines 275-276 persist changes without stabilizing existing marks.
- Line 296 chooses engine per mark:

```csharp
private string EngineFor(OcrMark m) => !string.IsNullOrEmpty(m.Engine) ? m.Engine : (Ensemble ? "Ensemble" : (WindowsForNumbers && !m.IsText ? "Windows" : "Paddle"));
```

- Line 471 defaults `WindowsForNumbers = true`.
- Lines 476-477 restore both toggles.
- Lines 861-862 also set both toggles to true in the calibration/tuning path.
- Line 1262 passes `EngineFor(m)` into `ReadPercentTextFrom`.
- Lines 1496-1510 surface the last OCR engine and warning.

`src/4rVivi.App/Services/OcrService.cs`

- Lines 251-266 accept an engine string for `ReadPercentTextFrom`.
- Lines 916-954 contain Paddle first, Windows fallback when worker is down, and Tesseract fallback.
- Lines 1144-1172 allow explicit `Windows` and `Ensemble`; Ensemble can switch to Windows when Paddle is empty or low confidence.
- Line 180 still comments `ReadBarPercent` as HP/SP/EXP bar even though vital roles are blocked later.
- Lines 1219-1242 block vital HP/SP bar fallback.
- Lines 1244 onward contain a commented legacy vital color bar block that should be deleted from active source history or quarantined in docs.

Cleanup target: HP/SP percent text should use one stable engine path, preferably Paddle, and fallback results should not publish trusted HP/SP unless explicitly allowed by contract.

## Cleanup Checklist

- [ ] Discord RPC: remove flat HP/SP from `RoPresence` and stop assigning flat HP/SP in `DiscordPresenceBootstrap`.
- [ ] Stats UI: stop reading `_stat.Hp`, `_stat.MaxHp`, `_stat.Sp`, `_stat.MaxSp` in `StatsViewModel`.
- [ ] Session tracker: replace flat HP observation with trusted HP percent observation, or disable death counting until a trusted death signal exists.
- [ ] Character state: delete or quarantine flat HP/SP fields and make activity use trusted percent drops only.
- [ ] HealthReader: remove public flat HP/SP properties, or rename them to diagnostic-only APIs with no safety use.
- [ ] StatReader: remove public flat HP/SP properties, or rename them to diagnostic-only APIs with no safety use.
- [ ] MultiClient: remove HP/SP flat pair parsing and raw `"HP"`/`"SP"` aliases.
- [ ] OcrReaderViewModel: force HP/SP percent roles to the stable percent OCR engine before applying `WindowsForNumbers`, `Ensemble`, or per-mark engine overrides.
- [ ] OcrService: reject or hold HP/SP percent reads when fallback engine is not the stable trusted engine.
- [ ] OcrService: delete the commented legacy vital color bar block and fix the stale `ReadBarPercent` comment.
- [ ] Follow-on search: after the above, run `rg "MaxHP|MaxSP|Roles\\.Hp\\b|Roles\\.Sp\\b|\\.Hp\\b|\\.Sp\\b" src` and fix newly broken consumers deliberately.

## Proposed Diffs

These diffs are proposals for the main-thread owner. They are intentionally not applied by this audit.

### Discord RPC Percent-Only Presence

```diff
diff --git a/src/4rVivi.Core/Discord/RoPresence.cs b/src/4rVivi.Core/Discord/RoPresence.cs
--- a/src/4rVivi.Core/Discord/RoPresence.cs
+++ b/src/4rVivi.Core/Discord/RoPresence.cs
@@
     public int HpPct { get; set; } = -1;
     public int SpPct { get; set; } = -1;
-    public int Hp { get; set; } = -1;
-    public int MaxHp { get; set; } = -1;
-    public int Sp { get; set; } = -1;
-    public int MaxSp { get; set; } = -1;
@@
-    if (Hp > 0 && MaxHp > 0) parts.Add($"HP {Hp}/{MaxHp}");
-    if (Sp > 0 && MaxSp > 0) parts.Add($"SP {Sp}/{MaxSp}");
+    if (HpPct >= 0) parts.Add($"HP {HpPct}%");
+    if (SpPct >= 0) parts.Add($"SP {SpPct}%");
```

```diff
diff --git a/src/4rVivi.App/Services/DiscordPresenceBootstrap.cs b/src/4rVivi.App/Services/DiscordPresenceBootstrap.cs
--- a/src/4rVivi.App/Services/DiscordPresenceBootstrap.cs
+++ b/src/4rVivi.App/Services/DiscordPresenceBootstrap.cs
@@
             HpPct = cs.HpPct,
             SpPct = cs.SpPct,
-            Hp = cs.Hp, MaxHp = cs.MaxHp, Sp = cs.Sp, MaxSp = cs.MaxSp,
             BaseExpPct = cs.BaseExpPct, JobExpPct = cs.JobExpPct,
```

### Stats View Model and Session Tracker

```diff
diff --git a/src/4rVivi.App/ViewModels/StatsViewModel.cs b/src/4rVivi.App/ViewModels/StatsViewModel.cs
--- a/src/4rVivi.App/ViewModels/StatsViewModel.cs
+++ b/src/4rVivi.App/ViewModels/StatsViewModel.cs
@@
-        int hp = _stat.Hp, maxHp = _stat.MaxHp, sp = _stat.Sp, maxSp = _stat.MaxSp, wt = _stat.Weight, maxWt = _stat.MaxWeight;
-        _session.Observe(hp);
+        int wt = _stat.Weight, maxWt = _stat.MaxWeight;
         OnlineText = _session.Online.ToString(@"hh\:mm\:ss");
         DeathsText = _session.Deaths.ToString();
 
         var hpPct = _stat.HpPercent;
         var spPct = _stat.SpPercent;
+        _session.ObserveTrustedHpPercent(hpPct);
```

```diff
diff --git a/src/4rVivi.Core/Trackers/SessionTracker.cs b/src/4rVivi.Core/Trackers/SessionTracker.cs
--- a/src/4rVivi.Core/Trackers/SessionTracker.cs
+++ b/src/4rVivi.Core/Trackers/SessionTracker.cs
@@
-    private int _lastHp = -1;
+    private int _lastHpPct = -1;
@@
-        _lastHp = -1;
+        _lastHpPct = -1;
@@
-    /// Feed current HP; increments death count on transition to 0.
-    public void Observe(int hp)
+    /// Feed trusted HP percent; increments death count on transition to 0.
+    public void ObserveTrustedHpPercent(int hpPct)
     {
-        if (_lastHp > 0 && hp <= 0) Deaths++;
-        if (hp >= 0) _lastHp = hp;
+        if (_lastHpPct > 0 && hpPct <= 0) Deaths++;
+        if (hpPct >= 0) _lastHpPct = hpPct;
     }
```

If OCR percent `0` is too noisy for death counting, replace the body with a no-op and leave a TODO for a real death signal.

### CharacterState Percent-Only Health

```diff
diff --git a/src/4rVivi.Core/Game/CharacterState.cs b/src/4rVivi.Core/Game/CharacterState.cs
--- a/src/4rVivi.Core/Game/CharacterState.cs
+++ b/src/4rVivi.Core/Game/CharacterState.cs
@@
-    public int Hp { get; set; }
-    public int MaxHp { get; set; }
-    public int Sp { get; set; }
-    public int MaxSp { get; set; }
+    public int HpPct { get; set; } = -1;
+    public int SpPct { get; set; } = -1;
@@
-    public int HpPct => LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out var hpPct) ? hpPct : -1;
-    public int SpPct => LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var spPct) ? spPct : -1;
@@
-        var maxHp = LiveStats.Instance.TryGetNumber(Roles.MaxHp, out var mh) && mh > 0 ? mh : 0;
-        var maxSp = LiveStats.Instance.TryGetNumber(Roles.MaxSp, out var ms) && ms > 0 ? ms : 0;
+        var hpPct = LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out var hpp) ? hpp : -1;
+        var spPct = LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var spp) ? spp : -1;
@@
-            MaxHp = maxHp,
-            MaxSp = maxSp,
-            Hp = LiveStats.Instance.TryGetNumber(Roles.Hp, out var hp) && hp >= 0 && (maxHp <= 0 || hp <= maxHp) ? hp : 0,
-            Sp = LiveStats.Instance.TryGetNumber(Roles.Sp, out var sp) && sp >= 0 && (maxSp <= 0 || sp <= maxSp) ? sp : 0,
+            HpPct = hpPct,
+            SpPct = spPct,
@@
-        bool fighting = s.Hp < _last.Hp || s.Sp < _last.Sp || s.HpPct < _last.HpPct || s.SpPct < _last.SpPct;
+        bool fighting =
+            (s.HpPct >= 0 && _last.HpPct >= 0 && s.HpPct < _last.HpPct) ||
+            (s.SpPct >= 0 && _last.SpPct >= 0 && s.SpPct < _last.SpPct);
```

### HealthReader and StatReader Quarantine

Preferred final shape:

```diff
diff --git a/src/4rVivi.Core/Game/HealthReader.cs b/src/4rVivi.Core/Game/HealthReader.cs
--- a/src/4rVivi.Core/Game/HealthReader.cs
+++ b/src/4rVivi.Core/Game/HealthReader.cs
@@
-    public int Hp => Read("HP");
-    public int MaxHp => Read("MaxHP");
-    public int Sp => Read("SP");
-    public int MaxSp => Read("MaxSP");
     public int HpPercent => LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out var hpPct) ? hpPct : -1;
     public int SpPercent => LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var spPct) ? spPct : -1;
 
-    private int Read(string role)
+    public int ReadDiagnosticRole(string role)
     {
         if (LiveStats.Instance.TryGetNumber(role, out var v)) return v;
         return MemoryReader.ReadInt(role);
     }
```

```diff
diff --git a/src/4rVivi.Core/Game/StatReader.cs b/src/4rVivi.Core/Game/StatReader.cs
--- a/src/4rVivi.Core/Game/StatReader.cs
+++ b/src/4rVivi.Core/Game/StatReader.cs
@@
-    public int Hp => _h.Hp;
-    public int MaxHp => _h.MaxHp;
-    public int Sp => _h.Sp;
-    public int MaxSp => _h.MaxSp;
     public int Exp => _h.Read("EXP");
     public int JobExp => _h.Read("JobEXP");
@@
     public int HpPercent => LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out var hpPct) ? hpPct : -1;
     public int SpPercent => LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var spPct) ? spPct : -1;
```

Practical sequencing note: this will expose downstream flat-health consumers at compile time. Main should intentionally update or remove those consumers in the same patch.

### MultiClient HP/SP Pair Removal

```diff
diff --git a/src/4rVivi.App/ViewModels/MultiClientViewModel.cs b/src/4rVivi.App/ViewModels/MultiClientViewModel.cs
--- a/src/4rVivi.App/ViewModels/MultiClientViewModel.cs
+++ b/src/4rVivi.App/ViewModels/MultiClientViewModel.cs
@@
-            if (mark.Role == "HP / MaxHP" || mark.Role == "SP / MaxSP" || mark.Role == "Weight / MaxWeight")
+            if (mark.Role == "Weight / MaxWeight")
             {
                 var pair = await _ocr.ReadPairNumberTextFromAsync(imagePath, mark.Rect, "Paddle", CancellationToken.None);
-                if (mark.Role == "HP / MaxHP")
-                    hp = pair.Item1 >= 0 && pair.Item2 > 0 ? $"{pair.Item1}/{pair.Item2}" : "-";
-                else if (mark.Role == "SP / MaxSP")
-                    sp = pair.Item1 >= 0 && pair.Item2 > 0 ? $"{pair.Item1}/{pair.Item2}" : "-";
-                else
-                    weight = pair.Item1 >= 0 && pair.Item2 > 0 ? $"{pair.Item1}/{pair.Item2}" : "-";
+                weight = pair.Item1 >= 0 && pair.Item2 > 0 ? $"{pair.Item1}/{pair.Item2}" : "-";
             }
 
-            if (role == Roles.HpPercent || mark.Role == "HP")
+            if (role == Roles.HpPercent)
             {
                 var val = await _ocr.ReadPercentTextFrom(imagePath, mark.Rect, role, "Paddle", CancellationToken.None);
                 hp = val >= 0 ? $"{val}%" : "-";
             }
-            if (role == Roles.SpPercent || mark.Role == "SP")
+            if (role == Roles.SpPercent)
             {
                 var val = await _ocr.ReadPercentTextFrom(imagePath, mark.Rect, role, "Paddle", CancellationToken.None);
                 sp = val >= 0 ? $"{val}%" : "-";
```

Keep or add a comment near `NormalizeRole` that `"HP Bar"` and `"SP Bar"` are legacy labels normalized to percent-text roles only. They must not reactivate bar-fill OCR.

### OcrReaderViewModel Engine Pinning for HP/SP

Minimum hardening:

```diff
diff --git a/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs b/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
--- a/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
+++ b/src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
@@
-    private string EngineFor(OcrMark m) => !string.IsNullOrEmpty(m.Engine) ? m.Engine : (Ensemble ? "Ensemble" : (WindowsForNumbers && !m.IsText ? "Windows" : "Paddle"));
+    private string EngineFor(OcrMark m)
+    {
+        if (IsPercentTextRole(m.Role)) return "Paddle";
+        return !string.IsNullOrEmpty(m.Engine)
+            ? m.Engine
+            : (Ensemble ? "Ensemble" : (WindowsForNumbers && !m.IsText ? "Windows" : "Paddle"));
+    }
```

Stronger follow-up:

- Replace `WindowsForNumbers` and `Ensemble` with a single global enum such as `OcrEngineMode = Paddle | Windows | Ensemble`.
- Keep HP/SP percent text pinned to `Paddle` regardless of the global mode.
- Make per-mark `Engine` a diagnostic override only and block it for `HpPercent` and `SpPercent`.
- Change `EngineInfo` to show both stable configured engine and last fallback warning, instead of implying the last individual region is the global engine.

### OcrService Trusted Percent Fallback Guard

Minimum guard for HP/SP:

```diff
diff --git a/src/4rVivi.App/Services/OcrService.cs b/src/4rVivi.App/Services/OcrService.cs
--- a/src/4rVivi.App/Services/OcrService.cs
+++ b/src/4rVivi.App/Services/OcrService.cs
@@
     public async Task<int> ReadPercentTextFrom(string imagePath, Rect rect, string role, string engine, CancellationToken ct)
     {
-        var (raw, usedEngine) = await ReadRectBest(imagePath, rect, engine, ct).ConfigureAwait(false);
+        var requestedEngine = IsVitalPercentRole(role) ? "Paddle" : engine;
+        var (raw, usedEngine) = await ReadRectBest(imagePath, rect, requestedEngine, ct).ConfigureAwait(false);
+        if (IsVitalPercentRole(role) &&
+            !string.Equals(usedEngine, "PaddleOCR", StringComparison.OrdinalIgnoreCase) &&
+            !string.Equals(usedEngine, "Paddle", StringComparison.OrdinalIgnoreCase))
+        {
+            DebugTrace.Write("Vitals", $"{role} percent read held because OCR fallback used {usedEngine}.");
+            return -1;
+        }
         var val = ParsePercent(raw);
         if (val < 0) return -1;
```

Stale comment and legacy block cleanup:

```diff
diff --git a/src/4rVivi.App/Services/OcrService.cs b/src/4rVivi.App/Services/OcrService.cs
--- a/src/4rVivi.App/Services/OcrService.cs
+++ b/src/4rVivi.App/Services/OcrService.cs
@@
-    /// Reads HP/SP/EXP bar percent from a marked rectangle.
+    /// Reads non-vital bar percent from a marked rectangle. HP/SP use percent text only.
@@
-    /*
-    private static int LegacyVitalColorBarFill(...)
-    {
-        ...
-    }
-    */
```

Main should delete the full commented legacy vital color bar block beginning near line 1244 rather than keeping dead HP/SP fallback code in runtime source.

## Verification Plan For Main Patch

1. Run targeted searches:

```powershell
rg "HP / MaxHP|SP / MaxSP|MaxHP|MaxSP|Roles\.Hp\b|Roles\.Sp\b|\.Hp\b|\.Sp\b" src
rg "WindowsForNumbers|Ensemble|EngineFor|ReadPercentTextFrom|ReadBarPercent|LegacyVital" src
```

2. Build:

```powershell
dotnet build 4rVivi.sln -c Release
```

3. Manual profile checks:

- HP/SP percent text marks publish only `LiveStatSource.PercentText`.
- HP/SP legacy bar labels are converted to percent-text readers and never call bar-fill.
- Toggling `WindowsForNumbers` or `Ensemble` does not change HP/SP engine selection.
- If Paddle worker is down, HP/SP trusted values hold or become unknown instead of publishing Windows/Tesseract fallback as trusted.
- Discord rich presence shows `HP nn%` and `SP nn%`, never `HP current/max`.
- Multi-client status shows HP/SP percent only.

## Risks and Coordination Notes

- Removing `StatReader.Hp` and `StatReader.Sp` can intentionally break callers that still use flat health for bot gating. Main should treat those compile errors as cleanup targets, not restore the old surface.
- `SessionTracker` death counting from OCR percent can be noisy at `0%`. If this cannot be made reliable, disable death counting until there is a non-OCR death signal.
- `WindowsForNumbers` currently defaults true and is also forced true in the tuning path. That is acceptable for non-vital OCR only after HP/SP are explicitly pinned away from it.
- Do not remove the existing HP/SP bar-to-percent migration guard in `OcrReaderViewModel`; it protects old profiles from reactivating bar-fill safety reads.
