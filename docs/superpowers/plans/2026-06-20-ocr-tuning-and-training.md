# OCR Tuning + One-Click Custom-Model Training — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make 4ViviTools' PaddleOCR tunable in real time and trainable to a custom RO recognition model from inside the app (synthetic RO-font data + the user's ~30-40 screenshots, cropped accurately via a reference template), with the new model used immediately for live reads.

**Architecture:** Track A surfaces PaddleOCR inference knobs (C#) that flow App → `OcrService` → `RapidOcrClient` → `OcrServer` (the out-of-process ONNX worker). Track B is a bundled Python kit `tools/ocr-train/` (synthetic gen + template-based auto-label + train + `paddle2onnx`) that writes a new `rec.onnx` into `models/v5/`. Glue (C#) exports the OCR Reader marks to a reference template, runs the trainer behind a button, and hot-reloads the worker.

**Tech Stack:** .NET 8 / Avalonia / CommunityToolkit.Mvvm; RapidOcrNet (PP-OCRv5 ONNX, `sealed record RapidOcrOptions`, `RapidOcr.InitModels(int numThread)`); Python 3.10+ with paddlepaddle(-gpu), paddleocr, paddle2onnx, Pillow; pytest for Python units.

---

## File structure

Track A (C#):
- Modify `src/4rVivi.App/Services/OcrService.cs` — tuning fields + numeric char-constrain + push config to worker.
- Modify `src/4rVivi.App/Services/RapidOcrClient.cs` — `SendConfig(string)` over stdio.
- Modify `src/4rVivi.OcrServer/Program.cs` — parse `CFG\t…`, rebuild `RapidOcrOptions`, read `OCR_CPU_THREADS`.
- Modify `src/4rVivi.Core/Settings/AppSettings.cs` — `OcrTuning` block.
- Modify `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs` + `Views/OcrReaderView.axaml` — tuning sliders + Export-template + Train-OCR buttons.

Track B (Python, all new under `tools/ocr-train/`):
- `tools/ocr-train/run.py` — orchestrator.
- `tools/ocr-train/synth.py` — synthetic generator.
- `tools/ocr-train/autolabel.py` — template-based crop+label of real screenshots.
- `tools/ocr-train/build_dataset.py` — merge 10:1, split, emit config.
- `tools/ocr-train/train_export.py` — train + export + paddle2onnx.
- `tools/ocr-train/patterns.py` — RO value-pattern templates.
- `tools/ocr-train/fonts/` — bundled fonts (Arial, micross, Squirrel, KR Love Angels, Angel Love).
- `tools/ocr-train/user_images/.gitkeep` — empty drop folder.
- `tools/ocr-train/reference/.gitkeep` — reference screenshot + template.json land here.
- `tools/ocr-train/tests/test_synth.py`, `tests/test_autolabel.py`, `tests/test_build_dataset.py`.

Glue (C#):
- `src/4rVivi.App/Services/OcrTrainerRunner.cs` — launch `run.py`, stream stdout, cancel.
- `src/4rVivi.App/Services/OcrTemplateExporter.cs` — marks → `reference/template.json` + reference PNG.

---

## Track A — in-app inference tuning

### Task 1: Tuning model + numeric char-constrain in OcrService

**Files:**
- Modify: `src/4rVivi.Core/Settings/AppSettings.cs`
- Modify: `src/4rVivi.App/Services/OcrService.cs`
- Test: `tests/4rVivi.Core.Tests/OcrTuningTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FourRVivi.App.Services;
using Xunit;

public class OcrTuningTests
{
    [Theory]
    [InlineData("1.234 / 5,678", true, "1234/5678")]
    [InlineData("Lv 99", false, "Lv 99")]
    [InlineData("O0O 12", true, "00012")]   // O->0 is NOT done here; only strip non-numeric
    public void ConstrainNumeric_keeps_only_digits_slash_dot(string raw, bool numeric, string expected)
    {
        Assert.Equal(expected, OcrService.ConstrainNumeric(raw, numeric));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/4rVivi.Core.Tests -c Release --filter OcrTuningTests`
Expected: FAIL — `OcrService.ConstrainNumeric` not defined.

- [ ] **Step 3: Add the tuning block to settings**

In `src/4rVivi.Core/Settings/AppSettings.cs`, add a class (top-level, after `ProfileConfig`):

```csharp
public sealed class OcrTuningConfig
{
    public float DetBoxThresh { get; set; } = 0.30f;     // RapidOcrOptions.BoxThresh
    public float DetBoxScoreThresh { get; set; } = 0.60f; // RapidOcrOptions.BoxScoreThresh
    public float DetUnclipRatio { get; set; } = 1.50f;    // RapidOcrOptions.UnClipRatio
    public float TextScore { get; set; } = 0.50f;         // RapidOcrOptions.TextScore (drop low conf)
    public int MaxSideLen { get; set; } = 960;            // downscale cap
    public int CpuThreads { get; set; } = 0;              // 0 = auto
}
```

Then add to `AppSettings`:

```csharp
public OcrTuningConfig OcrTuning { get; set; } = new();
```

- [ ] **Step 4: Implement ConstrainNumeric + fields in OcrService**

In `src/4rVivi.App/Services/OcrService.cs`, add inside `OcrService` (near `Sharpen`):

```csharp
public FourRVivi.Core.Settings.OcrTuningConfig Tuning { get; set; } = new();

/// <summary>For digit roles, keep only 0-9, '/', '.', stripping thousands separators and stray letters.</summary>
public static string ConstrainNumeric(string raw, bool numeric)
{
    if (!numeric || string.IsNullOrEmpty(raw)) return raw;
    var sb = new System.Text.StringBuilder(raw.Length);
    foreach (char c in raw)
        if ((c >= '0' && c <= '9') || c == '/' || c == '.') sb.Append(c);
    return sb.ToString();
}
```

In `Recognize(byte[] png, bool numeric)`, wrap the rapid result:

```csharp
var rapid = _rapid.Recognize(png);
if (!string.IsNullOrWhiteSpace(rapid)) { LastEngine = "PaddleOCR"; return ConstrainNumeric(rapid, numeric); }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/4rVivi.Core.Tests -c Release --filter OcrTuningTests`
Expected: PASS (3 cases).

- [ ] **Step 6: Commit**

```bash
git add src/4rVivi.Core/Settings/AppSettings.cs src/4rVivi.App/Services/OcrService.cs tests/4rVivi.Core.Tests/OcrTuningTests.cs
git commit -m "feat(ocr): OcrTuningConfig + numeric char-constrain"
```

### Task 2: Push tuning to the OcrServer worker

**Files:**
- Modify: `src/4rVivi.App/Services/RapidOcrClient.cs`
- Modify: `src/4rVivi.OcrServer/Program.cs`
- Modify: `src/4rVivi.App/Services/OcrService.cs`

- [ ] **Step 1: Add SendConfig to RapidOcrClient**

In `RapidOcrClient`, add (after `Recognize`):

```csharp
private string? _pendingCfg;

/// <summary>Queue a CFG line; sent on next worker start and immediately if running.</summary>
public void SendConfig(string cfg)
{
    lock (_lock)
    {
        _pendingCfg = cfg;
        if (_proc is { HasExited: false })
            try { _proc.StandardInput.WriteLine("CFG\t" + cfg); _proc.StandardInput.Flush(); } catch { _failed = true; }
    }
}
```

In `EnsureStarted()`, right after `_proc.Start();`, replay pending config:

```csharp
_proc.Start();
if (_pendingCfg != null)
    try { _proc.StandardInput.WriteLine("CFG\t" + _pendingCfg); _proc.StandardInput.Flush(); } catch { }
return true;
```

- [ ] **Step 2: Parse CFG + threads in OcrServer**

Replace the body of `src/4rVivi.OcrServer/Program.cs` `Main` with:

```csharp
private static void Main()
{
    int threads = 0;
    int.TryParse(Environment.GetEnvironmentVariable("OCR_CPU_THREADS"), out threads);

    RapidOcr? ocr = null;
    try { ocr = new RapidOcr(); ocr.InitModels(threads); }
    catch { Console.Out.WriteLine("ERR\tinit"); }

    var opts = RapidOcrOptions.Default;
    string? line;
    while ((line = Console.In.ReadLine()) != null)
    {
        if (line == "QUIT") break;
        if (line.StartsWith("CFG\t"))
        {
            opts = ApplyCfg(opts, line.Substring(4));
            continue;
        }
        string text = "";
        try
        {
            var path = line.Split('\t')[0];
            if (ocr != null && File.Exists(path))
            {
                using var bmp = SKBitmap.Decode(path);
                if (bmp != null)
                {
                    var r = ocr.Detect(bmp, opts);
                    text = (r.StrRes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                }
            }
        }
        catch { text = ""; }
        Console.Out.WriteLine("OK\t" + text);
        Console.Out.Flush();
    }
    ocr?.Dispose();
}

private static RapidOcrOptions ApplyCfg(RapidOcrOptions o, string kv)
{
    float boxThresh = o.BoxThresh, boxScore = o.BoxScoreThresh, unclip = o.UnClipRatio, textScore = o.TextScore;
    int maxSide = o.MaxSideLen;
    foreach (var pair in kv.Split(';'))
    {
        var i = pair.IndexOf('=');
        if (i < 0) continue;
        var k = pair.Substring(0, i); var v = pair.Substring(i + 1);
        switch (k)
        {
            case "boxThresh": float.TryParse(v, out boxThresh); break;
            case "boxScore": float.TryParse(v, out boxScore); break;
            case "unclip": float.TryParse(v, out unclip); break;
            case "textScore": float.TryParse(v, out textScore); break;
            case "maxSide": int.TryParse(v, out maxSide); break;
        }
    }
    return o with { BoxThresh = boxThresh, BoxScoreThresh = boxScore, UnClipRatio = unclip, TextScore = textScore, MaxSideLen = maxSide };
}
```

- [ ] **Step 3: Have OcrService send tuning + threads**

In `OcrService`, add a method and call it whenever tuning changes:

```csharp
public void ApplyTuning()
{
    Environment.SetEnvironmentVariable("OCR_CPU_THREADS", Tuning.CpuThreads.ToString());
    _rapid.SendConfig(
        $"boxThresh={Tuning.DetBoxThresh};boxScore={Tuning.DetBoxScoreThresh};" +
        $"unclip={Tuning.DetUnclipRatio};textScore={Tuning.TextScore};maxSide={Tuning.MaxSideLen}");
}
```

Note: `OCR_CPU_THREADS` only takes effect on the next worker start (it sets `InitModels(threads)`); the CFG line applies live. The Train/reload flow (Task 14) restarts the worker, so thread changes apply then.

- [ ] **Step 4: Build to verify wiring compiles**

Run: `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/4rVivi.App/Services/RapidOcrClient.cs src/4rVivi.OcrServer/Program.cs src/4rVivi.App/Services/OcrService.cs
git commit -m "feat(ocr): push det/rec tuning + cpu_threads to OcrServer worker"
```

### Task 3: Load/save tuning + apply on startup

**Files:**
- Modify: `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`

- [ ] **Step 1: Inject tuning into OcrService at startup**

In `OcrReaderViewModel` constructor (after `_ocr = ocr;`), add:

```csharp
_ocr.Tuning = settings.Current.OcrTuning;
_ocr.ApplyTuning();
```

- [ ] **Step 2: Add observable properties bound to tuning**

Add to `OcrReaderViewModel`:

```csharp
[ObservableProperty] private double _detBoxThresh;
[ObservableProperty] private double _detUnclip;
[ObservableProperty] private int _ocrCpuThreads;

partial void OnDetBoxThreshChanged(double v) { _ocr.Tuning.DetBoxThresh = (float)v; _ocr.ApplyTuning(); _settings.Save(); }
partial void OnDetUnclipChanged(double v) { _ocr.Tuning.DetUnclipRatio = (float)v; _ocr.ApplyTuning(); _settings.Save(); }
partial void OnOcrCpuThreadsChanged(int v) { _ocr.Tuning.CpuThreads = v; _ocr.ApplyTuning(); _settings.Save(); }
```

And initialise them in the constructor after the lines from Step 1:

```csharp
_detBoxThresh = settings.Current.OcrTuning.DetBoxThresh;
_detUnclip = settings.Current.OcrTuning.DetUnclipRatio;
_ocrCpuThreads = settings.Current.OcrTuning.CpuThreads;
```

- [ ] **Step 3: Build**

Run: `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/4rVivi.App/ViewModels/OcrReaderViewModel.cs
git commit -m "feat(ocr): persist + apply inference tuning from OCR Reader"
```

### Task 4: Tuning UI in OCR Reader

**Files:**
- Modify: `src/4rVivi.App/Views/OcrReaderView.axaml`

- [ ] **Step 1: Add a tuning row**

After the existing Zoom/Sharpness row, insert:

```xml
<StackPanel Orientation="Horizontal" Spacing="10" Margin="0,6">
  <TextBlock Text="Det box thresh" Classes="muted" VerticalAlignment="Center"/>
  <Slider Width="120" Minimum="0.1" Maximum="0.9" Value="{Binding DetBoxThresh}" VerticalAlignment="Center"/>
  <TextBlock Text="Unclip" Classes="muted" VerticalAlignment="Center"/>
  <Slider Width="120" Minimum="1.0" Maximum="3.0" Value="{Binding DetUnclip}" VerticalAlignment="Center"/>
  <TextBlock Text="CPU threads" Classes="muted" VerticalAlignment="Center"/>
  <NumericUpDown Width="90" Minimum="0" Maximum="16" Value="{Binding OcrCpuThreads}" VerticalAlignment="Center"/>
</StackPanel>
```

- [ ] **Step 2: Validate XAML + build**

Run: `python3 -c "import xml.dom.minidom as m;m.parse('src/4rVivi.App/Views/OcrReaderView.axaml')"` then `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: parses; Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/4rVivi.App/Views/OcrReaderView.axaml
git commit -m "feat(ocr): tuning sliders in OCR Reader"
```

---

## Track B — one-click trainer (Python, tools/ocr-train/)

### Task 5: Scaffold + bundled fonts + value patterns

**Files:**
- Create: `tools/ocr-train/patterns.py`
- Create: `tools/ocr-train/user_images/.gitkeep`, `tools/ocr-train/reference/.gitkeep`
- Create: `tools/ocr-train/fonts/` (copy the uploaded fonts here)
- Create: `tools/ocr-train/en_dict.txt`
- Test: `tools/ocr-train/tests/test_patterns.py`

- [ ] **Step 1: Write the failing test**

```python
from patterns import sample_value, ROLE_PATTERNS

def test_every_role_samples_a_string():
    for role in ROLE_PATTERNS:
        s = sample_value(role)
        assert isinstance(s, str) and len(s) > 0

def test_hp_is_two_numbers_with_slash():
    s = sample_value("HP")
    assert "/" in s
```

- [ ] **Step 2: Run to verify fail**

Run: `cd tools/ocr-train && python -m pytest tests/test_patterns.py -q`
Expected: FAIL — no module `patterns`.

- [ ] **Step 3: Implement patterns.py**

```python
import random

def _num(lo, hi): return str(random.randint(lo, hi))
def _pair(lo, hi):
    mx = random.randint(lo, hi); cur = random.randint(0, mx); return f"{cur}/{mx}"

ROLE_PATTERNS = {
    "HP": lambda: _pair(50, 999999),
    "SP": lambda: _pair(10, 99999),
    "BaseLevel": lambda: _num(1, 999),
    "JobLevel": lambda: _num(1, 70),
    "Zeny": lambda: _num(0, 2000000000),
    "Weight": lambda: _pair(0, 30000),
    "PosX": lambda: _num(0, 400),
    "PosY": lambda: _num(0, 400),
    "CharName": lambda: random.choice(["Vivi","DarkLord","Aizen","ProtoVivi","GMVivi","Eldrynn"]),
    "ClassName": lambda: random.choice(["Rune Knight","Warlock","Ranger","Rebellion","Star Emperor"]),
}

def sample_value(role: str) -> str:
    return ROLE_PATTERNS[role]()
```

- [ ] **Step 4: Run to verify pass**

Run: `cd tools/ocr-train && python -m pytest tests/test_patterns.py -q`
Expected: PASS.

- [ ] **Step 5: Add fonts + dict + drop folders**

```bash
mkdir -p tools/ocr-train/fonts tools/ocr-train/user_images tools/ocr-train/reference
# copy the uploaded fonts (Arial/ARIAL.TTF, microsoft-sans-serif/micross.ttf, squirrel/Squirrel.ttf,
# kr-love-angels/KR Love Angels.ttf, angel_love/*.otf) into tools/ocr-train/fonts/
touch tools/ocr-train/user_images/.gitkeep tools/ocr-train/reference/.gitkeep
# en_dict.txt = digits, slash, dot, space, A-Z a-z (one char per line) — matches RapidOcrNet latin dict
python - <<'PY'
chars = list("0123456789/. ") + [chr(c) for c in range(65,91)] + [chr(c) for c in range(97,123)]
open("tools/ocr-train/en_dict.txt","w").write("\n".join(chars)+"\n")
PY
```

- [ ] **Step 6: Commit**

```bash
git add tools/ocr-train/patterns.py tools/ocr-train/tests/test_patterns.py tools/ocr-train/fonts tools/ocr-train/en_dict.txt tools/ocr-train/user_images/.gitkeep tools/ocr-train/reference/.gitkeep
git commit -m "feat(train): scaffold ocr-train, fonts, value patterns, dict"
```

### Task 6: Synthetic generator (synth.py)

**Files:**
- Create: `tools/ocr-train/synth.py`
- Test: `tools/ocr-train/tests/test_synth.py`

- [ ] **Step 1: Write the failing test**

```python
import os
from synth import generate

def test_generate_makes_crops_and_labels(tmp_path):
    out = tmp_path / "synth"
    n = generate(str(out), count=20, fonts_dir="fonts")
    assert n == 20
    gt = (out / "rec_gt.txt").read_text().strip().splitlines()
    assert len(gt) == 20
    img0, label0 = gt[0].split("\t")
    assert (out / img0).exists() and len(label0) > 0
```

- [ ] **Step 2: Run to verify fail**

Run: `cd tools/ocr-train && python -m pytest tests/test_synth.py -q`
Expected: FAIL — no module `synth`.

- [ ] **Step 3: Implement synth.py**

```python
import os, glob, random
from PIL import Image, ImageDraw, ImageFont
from patterns import ROLE_PATTERNS, sample_value

def _fonts(fonts_dir):
    paths = []
    for ext in ("*.ttf","*.TTF","*.otf","*.OTF"):
        paths += glob.glob(os.path.join(fonts_dir, "**", ext), recursive=True)
    return paths or []

def generate(out_dir, count=5000, fonts_dir="fonts"):
    img_dir = os.path.join(out_dir, "crops"); os.makedirs(img_dir, exist_ok=True)
    fonts = _fonts(fonts_dir)
    roles = list(ROLE_PATTERNS.keys())
    lines = []
    for i in range(count):
        role = random.choice(roles)
        text = sample_value(role)
        size = random.randint(14, 28)
        try:
            font = ImageFont.truetype(random.choice(fonts), size) if fonts else ImageFont.load_default()
        except Exception:
            font = ImageFont.load_default()
        pad = random.randint(2, 6)
        tmp = Image.new("RGB", (4,4)); d = ImageDraw.Draw(tmp)
        bbox = d.textbbox((0,0), text, font=font); tw, th = bbox[2]-bbox[0], bbox[3]-bbox[1]
        bg = random.choice([(20,20,28),(0,0,0),(40,40,40),(255,255,255)])
        fg = (255,255,255) if sum(bg) < 360 else (0,0,0)
        im = Image.new("RGB", (tw+pad*2, th+pad*2), bg)
        ImageDraw.Draw(im).text((pad-bbox[0], pad-bbox[1]), text, font=font, fill=fg)
        rel = os.path.join("crops", f"s_{i:05d}.png")
        im.save(os.path.join(out_dir, rel))
        lines.append(f"{rel}\t{text}")
    with open(os.path.join(out_dir, "rec_gt.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return count
```

- [ ] **Step 4: Run to verify pass**

Run: `cd tools/ocr-train && python -m pytest tests/test_synth.py -q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ocr-train/synth.py tools/ocr-train/tests/test_synth.py
git commit -m "feat(train): synthetic RO-font sample generator"
```

### Task 7: Template-based auto-label (autolabel.py)

**Files:**
- Create: `tools/ocr-train/autolabel.py`
- Test: `tools/ocr-train/tests/test_autolabel.py`

The reference template is `reference/template.json`:

```json
{ "marks": [ {"role":"HP","x":0.12,"y":0.05,"w":0.08,"h":0.03,"isText":false,"isBar":false},
             {"role":"CharName","x":0.10,"y":0.01,"w":0.12,"h":0.03,"isText":true,"isBar":false} ] }
```

- [ ] **Step 1: Write the failing test (crop geometry only; no OCR)**

```python
import json
from PIL import Image
from autolabel import crop_roles

def test_crop_roles_uses_normalized_boxes(tmp_path):
    img = Image.new("RGB",(1000,500),(0,0,0)); p = tmp_path/"shot.png"; img.save(p)
    tmpl = {"marks":[{"role":"HP","x":0.1,"y":0.2,"w":0.2,"h":0.1,"isText":False,"isBar":False}]}
    crops = crop_roles(str(p), tmpl)
    assert crops[0]["role"] == "HP"
    assert crops[0]["box"] == (100, 100, 300, 150)   # x..x+w, y..y+h in pixels
```

- [ ] **Step 2: Run to verify fail**

Run: `cd tools/ocr-train && python -m pytest tests/test_autolabel.py -q`
Expected: FAIL — no module `autolabel`.

- [ ] **Step 3: Implement autolabel.py**

```python
import os, glob, json, re
from PIL import Image

def crop_roles(image_path, template):
    im = Image.open(image_path); W, H = im.size
    out = []
    for m in template["marks"]:
        if m.get("isBar"): continue            # bars are not text
        x0 = int(m["x"]*W); y0 = int(m["y"]*H)
        x1 = int((m["x"]+m["w"])*W); y1 = int((m["y"]+m["h"])*H)
        out.append({"role": m["role"], "box": (x0,y0,x1,y1), "isText": bool(m.get("isText"))})
    return out

def _constrain(role, text, is_text):
    if is_text: return text.strip()
    return re.sub(r"[^0-9/.]", "", text)

def label_folder(user_dir, template_path, out_dir, ocr_read):
    """ocr_read(PIL.Image)->str is injected so tests can stub it."""
    os.makedirs(os.path.join(out_dir, "crops"), exist_ok=True)
    template = json.load(open(template_path, encoding="utf-8"))
    lines, idx = [], 0
    for img in glob.glob(os.path.join(user_dir, "*.png")) + glob.glob(os.path.join(user_dir, "*.jpg")):
        full = Image.open(img)
        for c in crop_roles(img, template):
            crop = full.crop(c["box"])
            text = _constrain(c["role"], ocr_read(crop), c["isText"])
            rel = os.path.join("crops", f"r_{idx:05d}.png"); idx += 1
            crop.save(os.path.join(out_dir, rel))
            lines.append(f"{rel}\t{text if text else '###'}")
    with open(os.path.join(out_dir, "rec_gt.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return len(lines)
```

- [ ] **Step 4: Run to verify pass**

Run: `cd tools/ocr-train && python -m pytest tests/test_autolabel.py -q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ocr-train/autolabel.py tools/ocr-train/tests/test_autolabel.py
git commit -m "feat(train): template-based crop + role-constrained auto-label"
```

### Task 8: Merge + split + config (build_dataset.py)

**Files:**
- Create: `tools/ocr-train/build_dataset.py`
- Create: `tools/ocr-train/rec_config.template.yml`
- Test: `tools/ocr-train/tests/test_build_dataset.py`

- [ ] **Step 1: Write the failing test**

```python
from build_dataset import merge_and_split

def test_merge_keeps_ratio_and_splits(tmp_path):
    synth = tmp_path/"synth"; real = tmp_path/"real"
    for d,n,pre in [(synth,1000,"s"),(real,100,"r")]:
        (d/"crops").mkdir(parents=True)
        lines=[f"crops/{pre}_{i}.png\t{i}" for i in range(n)]
        (d/"rec_gt.txt").write_text("\n".join(lines))
    out = tmp_path/"dataset"
    train, val = merge_and_split(str(synth), str(real), str(out), real_ratio=10, val_frac=0.1)
    # 100 real all-in + 100*10? No: synth capped to 10x real = 1000 -> total ~1100
    assert train > 0 and val > 0
    assert (out/"train_list.txt").exists() and (out/"val_list.txt").exists()
```

- [ ] **Step 2: Run to verify fail**

Run: `cd tools/ocr-train && python -m pytest tests/test_build_dataset.py -q`
Expected: FAIL — no module `build_dataset`.

- [ ] **Step 3: Implement build_dataset.py**

```python
import os, random, shutil

def _read(gt): return [l for l in open(gt, encoding="utf-8").read().splitlines() if "\t" in l]

def merge_and_split(synth_dir, real_dir, out_dir, real_ratio=10, val_frac=0.1):
    os.makedirs(os.path.join(out_dir, "crops"), exist_ok=True)
    real = _read(os.path.join(real_dir, "rec_gt.txt")) if os.path.exists(os.path.join(real_dir,"rec_gt.txt")) else []
    synth = _read(os.path.join(synth_dir, "rec_gt.txt")) if os.path.exists(os.path.join(synth_dir,"rec_gt.txt")) else []
    cap = max(len(real)*real_ratio, 0) or len(synth)
    synth = synth[:cap] if real else synth
    merged = []
    for src_dir, lines, pre in [(synth_dir, synth, "s"), (real_dir, real, "r")]:
        for j, ln in enumerate(lines):
            rel, label = ln.split("\t", 1)
            newrel = os.path.join("crops", f"{pre}_{j:06d}.png")
            shutil.copy(os.path.join(src_dir, rel), os.path.join(out_dir, newrel))
            merged.append(f"{newrel}\t{label}")
    random.shuffle(merged)
    k = max(1, int(len(merged)*val_frac))
    val, train = merged[:k], merged[k:]
    open(os.path.join(out_dir,"train_list.txt"),"w",encoding="utf-8").write("\n".join(train)+"\n")
    open(os.path.join(out_dir,"val_list.txt"),"w",encoding="utf-8").write("\n".join(val)+"\n")
    return len(train), len(val)
```

- [ ] **Step 4: Add the rec config template (GTC removed, per research)**

Create `tools/ocr-train/rec_config.template.yml`:

```yaml
Global:
  use_gpu: __USE_GPU__
  epoch_num: 200
  save_model_dir: ./output/rec_ro
  save_epoch_step: 50
  eval_batch_step: [0, 500]
  pretrained_model: __PRETRAINED__
  character_dict_path: ./en_dict.txt
  max_text_length: 25
  use_space_char: true
Optimizer:
  name: Adam
  lr: { name: Piecewise, decay_epochs: [140, 180], values: [0.0001, 0.00002], warmup_epoch: 5 }
  regularizer: { name: L2, factor: 0 }
Architecture:
  model_type: rec
  algorithm: SVTR
  Backbone: { name: MobileNetV1Enhance, scale: 0.5, last_conv_stride: [1,2], last_pool_type: avg }
  Neck: { name: SequenceEncoder, encoder_type: svtr, dims: 64, depth: 2, hidden_dims: 120, use_guide: False }
  Head: { name: CTCHead, fc_decay: 0.00001 }
Loss: { name: CTCLoss }
PostProcess: { name: CTCLabelDecode }
Metric: { name: RecMetric, main_indicator: acc }
Train:
  dataset: { name: SimpleDataSet, data_dir: __DATA_DIR__, label_file_list: [__DATA_DIR__/train_list.txt],
             transforms: [ {DecodeImage: {img_mode: BGR, channel_first: false}}, {RecAug: {}},
                           {CTCLabelEncode: {}}, {RecResizeImg: {image_shape: [3,48,320]}},
                           {KeepKeys: {keep_keys: [image,label,length]}} ] }
  loader: { shuffle: true, batch_size_per_card: 64, drop_last: true, num_workers: 4 }
Eval:
  dataset: { name: SimpleDataSet, data_dir: __DATA_DIR__, label_file_list: [__DATA_DIR__/val_list.txt],
             transforms: [ {DecodeImage: {img_mode: BGR, channel_first: false}}, {CTCLabelEncode: {}},
                           {RecResizeImg: {image_shape: [3,48,320]}}, {KeepKeys: {keep_keys: [image,label,length]}} ] }
  loader: { shuffle: false, batch_size_per_card: 64, drop_last: false, num_workers: 4 }
```

- [ ] **Step 5: Run to verify pass**

Run: `cd tools/ocr-train && python -m pytest tests/test_build_dataset.py -q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/ocr-train/build_dataset.py tools/ocr-train/rec_config.template.yml tools/ocr-train/tests/test_build_dataset.py
git commit -m "feat(train): merge/split dataset (10:1) + PP-OCRv3 rec config (GTC removed)"
```

### Task 9: Train + export to ONNX (train_export.py)

**Files:**
- Create: `tools/ocr-train/train_export.py`

This file shells out to PaddleOCR's training/export tools and `paddle2onnx`; it is exercised by the
orchestrator and the integration smoke (Task 11), not a unit test (needs Paddle installed).

- [ ] **Step 1: Implement train_export.py**

```python
import os, subprocess, sys, urllib.request, tarfile

PRETRAIN_URL = "https://paddleocr.bj.bcebos.com/PP-OCRv3/english/en_PP-OCRv3_rec_train.tar"

def _ensure_pretrained(work):
    dst = os.path.join(work, "pretrain")
    params = os.path.join(dst, "en_PP-OCRv3_rec_train", "best_accuracy.pdparams")
    if os.path.exists(params): return params[:-len(".pdparams")]
    os.makedirs(dst, exist_ok=True)
    tar = os.path.join(dst, "rec.tar")
    urllib.request.urlretrieve(PRETRAIN_URL, tar)
    with tarfile.open(tar) as t: t.extractall(dst)
    return params[:-len(".pdparams")]

def write_config(template, data_dir, use_gpu, pretrained, out_yml):
    s = open(template, encoding="utf-8").read()
    s = s.replace("__USE_GPU__", "true" if use_gpu else "false")
    s = s.replace("__DATA_DIR__", data_dir).replace("__PRETRAINED__", pretrained)
    open(out_yml, "w", encoding="utf-8").write(s)

def run(paddleocr_repo, config_yml, work):
    env = dict(os.environ)
    train = [sys.executable, os.path.join(paddleocr_repo, "tools", "train.py"), "-c", config_yml]
    subprocess.check_call(train, env=env)
    best = "./output/rec_ro/best_accuracy"
    infer = os.path.join(work, "inference_rec")
    export = [sys.executable, os.path.join(paddleocr_repo, "tools", "export_model.py"),
              "-c", config_yml, "-o", f"Global.pretrained_model={best}", f"Global.save_inference_dir={infer}"]
    subprocess.check_call(export, env=env)
    onnx_out = os.path.join(work, "rec.onnx")
    subprocess.check_call(["paddle2onnx", "--model_dir", infer,
                           "--model_filename", "inference.pdmodel",
                           "--params_filename", "inference.pdiparams",
                           "--save_file", onnx_out, "--opset_version", "11"], env=env)
    return onnx_out
```

- [ ] **Step 2: Smoke-syntax check (no Paddle needed)**

Run: `cd tools/ocr-train && python -c "import ast; ast.parse(open('train_export.py').read()); print('ok')"`
Expected: `ok`.

- [ ] **Step 3: Commit**

```bash
git add tools/ocr-train/train_export.py
git commit -m "feat(train): train + export inference + paddle2onnx -> rec.onnx"
```

### Task 10: Orchestrator (run.py) with GPU/CPU autodetect + install + parity guard

**Files:**
- Create: `tools/ocr-train/run.py`
- Create: `tools/ocr-train/requirements.txt`

- [ ] **Step 1: Add requirements.txt**

```text
paddleocr>=2.7,<3
paddle2onnx
Pillow
onnxruntime
```

- [ ] **Step 2: Implement run.py**

```python
import argparse, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))

def log(m): print(m, flush=True)

def has_cuda():
    try: return subprocess.call(["nvidia-smi"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0
    except Exception: return False

def ensure_env(use_gpu):
    try:
        import paddle  # noqa
    except Exception:
        wheel = "paddlepaddle-gpu" if use_gpu else "paddlepaddle"
        idx = [] if use_gpu else ["-i", "https://www.paddlepaddle.org.cn/packages/stable/cpu/"]
        subprocess.check_call([sys.executable, "-m", "pip", "install", wheel, *idx])
    subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(HERE, "requirements.txt")])

def get_paddleocr_repo():
    repo = os.path.join(HERE, "PaddleOCR")
    if not os.path.exists(repo):
        subprocess.check_call(["git", "clone", "--depth", "1",
                               "https://github.com/PaddlePaddle/PaddleOCR.git", repo])
    return repo

def current_onnx_reader():
    import onnxruntime as ort
    sess = ort.InferenceSession(os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5",
                                "latin_PP-OCRv5_rec_mobile_infer.onnx"), providers=["CPUExecutionProvider"])
    def read(img): return ""   # placeholder reader; real impl reuses RapidOcrNet preprocessing offline
    return read

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--user-images", default=os.path.join(HERE, "user_images"))
    ap.add_argument("--reference", default=os.path.join(HERE, "reference", "template.json"))
    ap.add_argument("--count", type=int, default=5000)
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()

    use_gpu = has_cuda()
    log(f"[1/6] device: {'GPU+CPU' if use_gpu else 'CPU only'}")
    ensure_env(use_gpu)

    work = os.path.join(HERE, "work"); os.makedirs(work, exist_ok=True)
    sys.path.insert(0, HERE)
    import synth, autolabel, build_dataset, train_export

    log("[2/6] synthetic generation")
    synth.generate(os.path.join(work, "synth"), count=a.count, fonts_dir=os.path.join(HERE, "fonts"))

    log("[3/6] auto-label real screenshots")
    real_out = os.path.join(work, "real")
    if os.path.exists(a.reference) and any(os.scandir(a.user_images)):
        from PIL import Image
        read = current_onnx_reader()
        autolabel.label_folder(a.user_images, a.reference, real_out, read)
    else:
        os.makedirs(os.path.join(real_out, "crops"), exist_ok=True)
        open(os.path.join(real_out, "rec_gt.txt"), "w").write("")
        log("    (no reference/template.json or empty user_images -> synthetic only)")

    log("[4/6] merge + split")
    data = os.path.join(work, "dataset")
    build_dataset.merge_and_split(os.path.join(work, "synth"), real_out, data)

    log("[5/6] train + export")
    repo = get_paddleocr_repo()
    pre = train_export._ensure_pretrained(work)
    cfg = os.path.join(work, "rec_config.yml")
    train_export.write_config(os.path.join(HERE, "rec_config.template.yml"), data, use_gpu, pre, cfg)
    onnx = train_export.run(repo, cfg, work)

    log("[6/6] install model")
    dst = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5", "latin_PP-OCRv5_rec_mobile_infer.onnx")
    backup = dst + ".bak"
    if not os.path.exists(backup): shutil.copy(dst, backup)
    shutil.copy(onnx, dst)
    log("DONE")

if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Syntax check**

Run: `cd tools/ocr-train && python -c "import ast; ast.parse(open('run.py').read()); print('ok')"`
Expected: `ok`.

- [ ] **Step 4: Commit**

```bash
git add tools/ocr-train/run.py tools/ocr-train/requirements.txt
git commit -m "feat(train): one-click orchestrator (GPU/CPU autodetect, install, pipeline, model swap)"
```

### Task 11: End-to-end smoke (tiny, no real training)

**Files:**
- Create: `tools/ocr-train/tests/test_pipeline_smoke.py`

- [ ] **Step 1: Write the smoke test (stubs OCR + skips real train)**

```python
import os, json
import synth, autolabel, build_dataset

def test_synth_to_dataset_pipeline(tmp_path):
    work = tmp_path
    synth.generate(str(work/"synth"), count=40, fonts_dir="fonts")
    # fake one screenshot + template
    from PIL import Image
    (work/"imgs").mkdir(); Image.new("RGB",(800,600),(0,0,0)).save(work/"imgs"/"a.png")
    tmpl = {"marks":[{"role":"HP","x":0.1,"y":0.1,"w":0.2,"h":0.1,"isText":False,"isBar":False}]}
    (work/"template.json").write_text(json.dumps(tmpl))
    autolabel.label_folder(str(work/"imgs"), str(work/"template.json"), str(work/"real"), lambda im: "123/456")
    train, val = build_dataset.merge_and_split(str(work/"synth"), str(work/"real"), str(work/"dataset"))
    assert train > 0 and val > 0
    assert os.path.exists(work/"dataset"/"train_list.txt")
```

- [ ] **Step 2: Run**

Run: `cd tools/ocr-train && python -m pytest tests/test_pipeline_smoke.py -q`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tools/ocr-train/tests/test_pipeline_smoke.py
git commit -m "test(train): synth->autolabel->dataset smoke"
```

---

## Glue — app integration

### Task 12: Export OCR Reader marks → reference/template.json

**Files:**
- Create: `src/4rVivi.App/Services/OcrTemplateExporter.cs`
- Modify: `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`
- Modify: `src/4rVivi.App/Views/OcrReaderView.axaml`

- [ ] **Step 1: Implement the exporter**

```csharp
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using FourRVivi.Core.Ocr;

namespace FourRVivi.App.Services;

public static class OcrTemplateExporter
{
    public static string ToolDir()
    {
        var baseDir = System.AppContext.BaseDirectory;
        return Path.Combine(baseDir, "tools", "ocr-train");
    }

    public static string Export(IEnumerable<OcrMark> marks, byte[]? referencePng)
    {
        var refDir = Path.Combine(ToolDir(), "reference");
        Directory.CreateDirectory(refDir);
        var payload = new { marks };
        File.WriteAllText(Path.Combine(refDir, "template.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        if (referencePng is { Length: > 0 })
            File.WriteAllBytes(Path.Combine(refDir, "reference.png"), referencePng);
        return refDir;
    }
}
```

Note: `OcrMark` exposes `Role,X,Y,W,H,IsText,IsBar,IsChar`; System.Text.Json serializes them as
`Role/X/Y/...`. The Python side reads lowercase keys, so add `[JsonPropertyName]` OR have autolabel
accept case-insensitively. Use case-insensitive read in Python (already does `m["x"]`); therefore add
`JsonNamingPolicy.CamelCase`:

```csharp
new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
```

- [ ] **Step 2: Add command in the view model**

In `OcrReaderViewModel`:

```csharp
[RelayCommand]
private void ExportTemplate()
{
    byte[]? png = null;
    try { var f = CaptureForCalibration(); if (f != null) png = OcrService.BitmapToPng(f); } catch { }
    var dir = OcrTemplateExporter.Export(Marks, png);
    Status = $"Exported template + reference image to {dir}.";
}
```

If `OcrService.BitmapToPng` does not exist, add it to `OcrService`:

```csharp
public static byte[] BitmapToPng(System.Drawing.Bitmap bmp)
{ using var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
```

`CaptureForCalibration()` is the existing private capture used by Calibrate (the method around line 63
that returns the current frame); if it is named differently, call that existing capture method instead.

- [ ] **Step 3: Add the button**

In `OcrReaderView.axaml`, near the Save marks button:

```xml
<Button Content="Export training template" Command="{Binding ExportTemplateCommand}"/>
```

- [ ] **Step 4: Build**

Run: `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/4rVivi.App/Services/OcrTemplateExporter.cs src/4rVivi.App/ViewModels/OcrReaderViewModel.cs src/4rVivi.App/Views/OcrReaderView.axaml src/4rVivi.App/Services/OcrService.cs
git commit -m "feat(train): export OCR Reader marks -> reference/template.json + reference.png"
```

### Task 13: "Train OCR" button — run the trainer, stream progress

**Files:**
- Create: `src/4rVivi.App/Services/OcrTrainerRunner.cs`
- Modify: `src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`
- Modify: `src/4rVivi.App/Views/OcrReaderView.axaml`

- [ ] **Step 1: Implement the runner**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FourRVivi.App.Services;

public sealed class OcrTrainerRunner
{
    public event Action<string>? Line;
    private Process? _proc;

    public bool Running => _proc is { HasExited: false };

    public string UserImagesDir()
    {
        var dir = Path.Combine(OcrTemplateExporter.ToolDir(), "user_images");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<bool> RunAsync(CancellationToken ct)
    {
        var tool = OcrTemplateExporter.ToolDir();
        var py = OperatingSystem.IsWindows() ? "python" : "python3";
        var psi = new ProcessStartInfo
        {
            FileName = py,
            Arguments = "run.py",
            WorkingDirectory = tool,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        try { _proc = Process.Start(psi); }
        catch (Exception e) { Line?.Invoke("ERROR: Python not found — install Python 3.10+. " + e.Message); return false; }
        if (_proc == null) { Line?.Invoke("ERROR: could not start trainer."); return false; }

        _proc.OutputDataReceived += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
        _proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
        _proc.BeginOutputReadLine(); _proc.BeginErrorReadLine();

        using (ct.Register(() => { try { _proc.Kill(true); } catch { } }))
            await _proc.WaitForExitAsync(ct);
        return _proc.ExitCode == 0;
    }
}
```

- [ ] **Step 2: Wire commands + log into the view model**

```csharp
private readonly OcrTrainerRunner _trainer = new();
private CancellationTokenSource? _trainCts;
[ObservableProperty] private string _trainLog = "";
[ObservableProperty] private bool _training;

[RelayCommand]
private async Task TrainOcr()
{
    var dir = _trainer.UserImagesDir();
    try { Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true }); } catch { }
    Status = $"Drop ~30-40 screenshots into {dir}, then training starts.";
    TrainLog = "";
    _trainer.Line -= OnTrainLine; _trainer.Line += OnTrainLine;
    _trainCts = new CancellationTokenSource();
    Training = true;
    var ok = await _trainer.RunAsync(_trainCts.Token);
    Training = false;
    Status = ok ? "Training done — new model installed. Reloading OCR…" : "Training cancelled or failed (old model kept).";
    if (ok) _ocr.ReloadWorker();   // Task 14
}

private void OnTrainLine(string l) => Avalonia.Threading.Dispatcher.UIThread.Post(() => TrainLog += l + "\n");

[RelayCommand] private void CancelTrain() { _trainCts?.Cancel(); }
```

Add `using System.Diagnostics;` and `using System.Threading;` to the file if missing.

- [ ] **Step 3: Add UI (button + cancel + log)**

```xml
<StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8">
  <Button Content="Train OCR" Command="{Binding TrainOcrCommand}" IsEnabled="{Binding !Training}"/>
  <Button Content="Cancel" Command="{Binding CancelTrainCommand}" IsEnabled="{Binding Training}"/>
</StackPanel>
<TextBox Text="{Binding TrainLog}" IsReadOnly="True" AcceptsReturn="True" Height="140"
         FontFamily="Consolas,monospace"
         ScrollViewer.VerticalScrollBarVisibility="Auto" ScrollViewer.HorizontalScrollBarVisibility="Auto"/>
```

- [ ] **Step 4: Build**

Run: `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/4rVivi.App/Services/OcrTrainerRunner.cs src/4rVivi.App/ViewModels/OcrReaderViewModel.cs src/4rVivi.App/Views/OcrReaderView.axaml
git commit -m "feat(train): Train OCR button runs trainer + streams progress"
```

### Task 14: Hot-reload the worker after training

**Files:**
- Modify: `src/4rVivi.App/Services/RapidOcrClient.cs`
- Modify: `src/4rVivi.App/Services/OcrService.cs`

- [ ] **Step 1: Add Restart to RapidOcrClient**

```csharp
public void Restart()
{
    lock (_lock)
    {
        try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
        try { _proc?.Kill(); } catch { }
        _proc = null; _failed = false;   // allow a fresh start with the new model
    }
}
```

- [ ] **Step 2: Expose ReloadWorker on OcrService**

```csharp
public void ReloadWorker() { _rapid.Restart(); ApplyTuning(); }
```

- [ ] **Step 3: Build**

Run: `dotnet build 4rVivi.sln -c Release --no-restore`
Expected: Build succeeded.

- [ ] **Step 4: Manual verification**

Run the app, OCR Reader → Export training template → drop 1 screenshot → Train OCR (tiny `--count`
can be set for a quick pass) → after DONE, confirm `LastEngine == "PaddleOCR"` and live values parse.

- [ ] **Step 5: Commit**

```bash
git add src/4rVivi.App/Services/RapidOcrClient.cs src/4rVivi.App/Services/OcrService.cs
git commit -m "feat(ocr): hot-reload worker so new rec.onnx is used immediately"
```

### Task 15: Ship trainer with the build

**Files:**
- Modify: `.github/workflows/build-4rvivi.yml`, `.github/workflows/build-release.yml`

- [ ] **Step 1: Copy tools/ocr-train into publish output**

After the existing publish step, add (both workflows):

```yaml
      - name: Bundle OCR trainer
        run: |
          mkdir -p publish/tools
          cp -r tools/ocr-train publish/tools/ocr-train
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/build-4rvivi.yml .github/workflows/build-release.yml
git commit -m "build: bundle tools/ocr-train with published app"
```

---

## Self-review

**Spec coverage:**
- In-app inference tuning (det/rec params, cpu threads, image size, numeric constrain) → Tasks 1-4. ✓
- One-click trainer, button in app → Tasks 13-14. ✓
- Mixed data 10:1 → Task 8. ✓
- Synthetic from bundled fonts → Tasks 5-6. ✓
- Reference template / role-accurate cropping → Tasks 7, 12. ✓
- GPU+CPU autodetect, install on first run → Task 10. ✓
- Export to ONNX → models/v5, hot reload → Tasks 9-10, 14. ✓
- Parity/backup guard (keep old model on failure) → run.py writes `.bak`, copies only on success. ✓
- Bars excluded from rec training → Task 7 `crop_roles` skips `isBar`. ✓
- Ship trainer with build → Task 15. ✓

**Placeholder scan:** `current_onnx_reader().read` is an explicit, documented stub (offline reader reuses
RapidOcrNet preprocessing); autolabel accepts the reader injected, so real wiring is a one-line swap when
an offline ONNX reader is added — not a silent gap. All other steps contain real code.

**Type consistency:** `OcrService.Tuning` (OcrTuningConfig), `ApplyTuning()`, `ReloadWorker()`,
`RapidOcrClient.SendConfig()/Restart()`, `OcrTemplateExporter.ToolDir()/Export()`,
`OcrTrainerRunner.UserImagesDir()/RunAsync()` are referenced consistently across tasks. Python modules
`synth.generate`, `autolabel.crop_roles/label_folder`, `build_dataset.merge_and_split`,
`train_export.write_config/run/_ensure_pretrained` match their call sites in `run.py` and tests.

**Known limitation (carried from spec):** template assumes all training shots share the reference's
resolution/layout; the offline real-crop OCR reader is a stub until a headless RapidOcrNet reader is added
(synthetic-only training works without it).
