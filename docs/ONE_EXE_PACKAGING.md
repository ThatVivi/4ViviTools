# 4ViviTools — Packaging to ONE Self-Contained .exe (smallest, no install, no extra files)
**Date:** 2026-07-12 · **Audience:** Codex + maintainer · **Target:** every shippable tool becomes a single `.exe` that runs on a clean Windows machine with **no .NET installed, no loose files, no setup** — and is as small as the tech honestly allows.

This is grounded in current .NET 8/9 packaging guidance (sources at the end). Read §1 for the decision, then apply the per-tool recipes in §5.

---

## 1. The honest decision (read first)
There are three levers, from safest to smallest/riskiest:

| Lever | Size effect | Risk | Use it for |
|---|---|---|---|
| **Single-file + self-contained + native-extract + compression** | large→medium (Brotli-compressed) | **none** | EVERYTHING (baseline) |
| **Trimming (`PublishTrimmed`)** | medium→small | medium (reflection can break) | Avalonia app *with* trimmer roots + testing; **NOT** WinForms |
| **Native AOT** | small + fastest start | high (reflection/XAML/native interop) | only an AOT-ready Avalonia build; **NOT** WinForms, and verify Skia/ONNX/Vortice |

**Do NOT use UPX.** UPX compresses *native machine code*; a .NET single-file bundle is mostly *managed assemblies*, so UPX gives little benefit, can corrupt the self-extractor, and frequently triggers antivirus/SmartScreen false positives. Use .NET's built-in `EnableCompressionInSingleFile` (Brotli) instead.

**Two facts that bound the result:**
- **WinForms cannot be trimmed or AOT'd reliably** (heavy reflection). The **VisionGrfPicker is WinForms** → baseline compression only.
- **Data doesn't compress like code.** The ONNX models (~40–60 MB) are the dominant weight of the main app and stay roughly that size whatever you do. So "one exe with everything" for the main app is model-dominated; plan for that (§4).

---

## 2. Baseline recipe (safe, works for every project)
This is the "one exe, no install, no extra files" combo that never breaks functionality:
```xml
<!-- in each shippable .csproj -->
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>   <!-- Brotli -->
  <DebugType>none</DebugType>                                           <!-- or embedded -->
  <DebugSymbols>false</DebugSymbols>
  <SatelliteResourceLanguages>en</SatelliteResourceLanguages>          <!-- drop other-locale resource dlls -->
  <InvariantGlobalization>true</InvariantGlobalization>                <!-- drop ICU (~) if you don't need cultures -->
  <AutoreleasePoolSupport>false</AutoreleasePoolSupport>
</PropertyGroup>
```
Publish:
```
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -o artifacts\<tool>
```
Result: a single `.exe`. `IncludeNativeLibrariesForSelfExtract` pulls native `.dll`s (SkiaSharp, ONNX Runtime, Vortice, sqlite, etc.) *inside* the exe; at launch they extract to a temp dir automatically. `EnableCompressionInSingleFile` Brotli-compresses the managed assemblies (typical 30–50% smaller) at a small first-start decompression cost — measure it.

**Caveats to verify after enabling:**
- `InvariantGlobalization=true` breaks culture-specific formatting/cp949 lookups **if** you rely on them. You DO use **cp949** for GRF names → either keep `InvariantGlobalization=false`, or keep it true but confirm `Encoding.GetEncoding(949)` (via `System.Text.Encoding.CodePages`) still resolves (it ships the code page data itself, so it's fine). **Test the picker's Korean paths after this flag.**
- Compression + self-extract can raise **SmartScreen**; see §6 (signing).

---

## 3. Trimming (optional size win) — Avalonia only, carefully
Trimming removes unused IL. It can cut tens of MB but **XAML/reflection frameworks break silently** if their types get stripped.
```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>partial</TrimMode>   <!-- start partial; 'full' is smaller but riskier -->
<ItemGroup>
  <TrimmerRootAssembly Include="Avalonia.Controls" />
  <TrimmerRootAssembly Include="Avalonia.Base" />
  <TrimmerRootAssembly Include="Avalonia.Markup.Xaml" />
  <TrimmerRootAssembly Include="4rVivi.App" />
  <TrimmerRootAssembly Include="4rVivi.Core" />
</ItemGroup>
```
Rules:
- **Do not trim the WinForms picker** (`.NET` refuses/breaks). Baseline compression only there.
- For the Avalonia app: replace reflection-based `ViewLocator` with an explicit `switch` (view→viewmodel) so trimming can't strip views; keep every View's public parameterless ctor. Add `[DynamicDependency]` / an `ILLink.Descriptors.xml` for any type built via `Activator.CreateInstance`, JSON `[JsonSerializable]` source-gen for `System.Text.Json` DTOs, and root anything used only by reflection.
- **Enable trimming last**, fix every `IL2xxx`/`IL3xxx` analyzer warning, and smoke-test the whole UI + OCR + bot before shipping.

## 3b. Native AOT (advanced, smallest + fastest) — only if it survives testing
AOT = smallest exe and instant startup, but it **forces trimming**, and Avalonia's reflection bindings + Skia/ONNX/Vortice interop may not be fully AOT-annotated. Treat as an experiment on the Avalonia app only:
```xml
<PublishAot>true</PublishAot>
```
Follow Avalonia's Native AOT doc (source-gen compiled bindings, no `Activator`, TrimmerRoots). If ONNX Runtime or Vortice throw at runtime, fall back to trimmed-single-file. **Never AOT the WinForms picker.**

---

## 4. Making it TRULY one file (no loose models / worker / data)
The main app currently ships extra pieces: the `OcrServer` worker exe, the ONNX `models/`, and data JSON. To reach "one exe, nothing beside it":

1. **Native libraries** → already covered by `IncludeNativeLibrariesForSelfExtract` (in the exe, auto-extracted).
2. **Small data (json/manifest/sprite-map)** → **embed as resources** (the picker already does this: `<EmbeddedResource>` + load via `Assembly.GetManifestResourceStream`). Do the same for `gamedata.json`, `map_names.json`, etc.
3. **Big ONNX models (~40–60 MB)** → two options:
   - **(A) Embed + extract-on-first-run (recommended for "everything ready, offline").** Add the `.onnx` as `<EmbeddedResource>`; on first launch, if `%LocalAppData%\4rVivi\models\<hash>\entity.onnx` is missing, write the resource there (idempotent, content-hashed) and point the worker at that path. One shipped file; models materialize once. Downsides: larger exe, ~1–2 s first-run extract.
   - **(B) Download-on-first-run.** Smallest exe, but needs internet once and a hosting URL. The user asked for **no extra files / everything ready** → prefer **(A)**.
4. **The `OcrServer` worker (separate process)** → to keep ONE distributable:
   - **Embed the worker exe** as a resource, extract it (with the models + native libs it needs) to the same per-user cache on first run, and launch it from there. Keeps process isolation AND one shipped file. OR
   - **In-process the worker** (compile its ONNX/OCR path as a library referenced by the app, run on a background thread) → truly one process, one file, but you lose crash-isolation. Given the OCR worker has crashed before, **prefer embed+extract** to keep isolation.

> Reality check: with models embedded, the app exe will be roughly **(compressed .NET runtime+code ~25–40 MB) + (models ~40–60 MB) ≈ 70–100 MB**. That is the honest floor for a fully-offline, everything-embedded single file. The **picker** (no models) compresses to a few MB.

---

## 5. Per-tool recipes
### 5.1 VisionGrfPicker (WinForms) — the easy, already-mostly-done case
- Baseline recipe (§2). **No trimming/AOT.** Data JSON already embedded.
- Verify cp949 still resolves if `InvariantGlobalization=true` (register `CodePagesEncodingProvider` in `Program.Main`, which you already do).
- One command: `tools\VisionGrfPicker\build_picker.bat` → `publish\VisionGrfPicker.exe` (one file, ~a few MB).

### 5.2 4rVivi.App (Avalonia) — the main app
- Baseline recipe (§2) → confirm it runs on a clean VM.
- Add §4 embedding for `models/` + `OcrServer` + data so nothing sits beside the exe.
- Optionally layer §3 trimming (Avalonia roots + source-gen ViewLocator) for a big size cut; ship trimmed only after full smoke-test.
- Keep the `CopyOcrWorker` MSBuild target for the *dev* run; the *release* uses the embed-extract path.

### 5.3 The Python GRF builder
- End users shouldn't need Python. **Port it into the picker** (C#) so the picker is the only builder (see the improvement plan). Interim: a **PyInstaller one-file exe** built on Windows (`pyinstaller --onefile --noconsole build_vision_grf.py` with `--add-data` for the JSONs) — the end user installs nothing, but the C# port is the real fix.

---

## 6. Antivirus / SmartScreen (don't skip)
Single-file + compression + self-extraction is a common malware shape, so unsigned builds may be flagged.
- **Sign the exe** (Authenticode; EV cert clears SmartScreen fastest). Even a standard cert + reputation over time helps.
- Avoid UPX (raises detections).
- Prefer extract-to-`%LocalAppData%` over extracting into system/temp roots.
- Provide a SHA-256 next to the download so users can verify.

---

## 7. Size-reduction checklist (apply in order, measure each)
1. `SelfContained + PublishSingleFile + IncludeNativeLibrariesForSelfExtract` → one file.
2. `EnableCompressionInSingleFile` → Brotli; measure size **and** startup.
3. `SatelliteResourceLanguages=en` + `InvariantGlobalization` (verify cp949) → drop locale/ICU baggage.
4. `DebugType=none`, `DebugSymbols=false`, `<PublishReadyToRun>false</...>` (R2R increases size; only enable if startup matters more than size).
5. Remove unused NuGet refs; central-manage versions; drop dev-only packages from Release.
6. (Avalonia) `PublishTrimmed` partial → roots + ILLink descriptors → test → optionally `full` or AOT.
7. Models: embed + extract-on-first-run (§4) rather than shipping loose.

## 8. Verification (definition of done)
On a **clean Windows VM with no .NET SDK/runtime and no other files**:
- Copy only the single `.exe`, double-click → app/picker starts, loads embedded data, runs OCR/vision/bot (app) or builds a GRF (picker).
- No "missing .dll", no "install .NET", no loose `models/` or `OcrServer` needed.
- `DebugTrace`/logs go to `%LocalAppData%\4rVivi\`, not next to the exe (writable-path rule).
- Record final size + first-start time in `CHANGELOG.md`.

---

## Sources
- [Create a single file for application deployment — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Trim self-contained applications — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)
- [Trimming options — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options)
- [Native AOT deployment overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Native AOT — Avalonia Docs](https://docs.avaloniaui.net/docs/deployment/native-aot)
- [Trim down the size of the compiled binary — Avalonia Discussion #9217](https://github.com/AvaloniaUI/Avalonia/discussions/9217)
- [.NET8/9 — Testing different Build/Deployment modes — Mark Pelf](https://markpelf.com/2621/net8-9-testing-different-build-deployment-modes-part1/)
- [Special properties in .NET projects — AlexandreHTRB](https://alexandrehtrb.github.io/posts/2024/12/special-properties-in-dotnet-projects/)
- [UPX — the Ultimate Packer for eXecutables](https://upx.github.io/)  (why NOT to use it for managed .NET)
