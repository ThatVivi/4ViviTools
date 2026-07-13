# 4ViviTools — CODEX MAP (read this first when you get lost)
**Purpose:** the single orientation doc. Every path, pipeline, threshold, and delay that matters. Keep it current — if you change a path or constant, update it here in the same commit.
**Repo root:** `D:\vs code clone 4rtool\4ViviTools` · **Solution:** `4rVivi.sln` · **.NET 8 · Avalonia 11 · MVVM+DI**

---

## 1. Projects (src/)
| Project | Role |
|---|---|
| `4rVivi.Core` | Engine: Memory, Signatures, Game/LiveStats, Automation, Input, Ocr, Grf, Calc, Data, Trackers, Servers, Settings |
| `4rVivi.App` | Avalonia UI (Views/ViewModels), Services (capture, OCR client, overlay), Overlays |
| `4rVivi.OcrServer` | Out-of-process ONNX worker; stdio verbs `CFG / REC / DETECT / ICON / SCAN` |
| `RapidOcrNet` | ONNX OCR engine (det/cls/rec) |
| `4rVivi.Plugins.Abstractions` | `IPlugin` contract |

Build/test/publish:
```
dotnet build 4rVivi.sln -c Release --nologo
dotnet test  tests/4rVivi.Core.Tests/4rVivi.Core.Tests.csproj -c Release --nologo
dotnet publish src/4rVivi.App/4rVivi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 2. Models (src/RapidOcrNet/models/)
| Path | What |
|---|---|
| `v5/latin_PP-OCRv5_rec_mobile_infer.onnx` | text recognizer (fine-tuned) |
| `v5/ch_PP-OCRv5_mobile_det.onnx` | text detector |
| `v5/ch_ppocr_mobile_v2.0_cls_infer.onnx` | angle classifier |
| `yolo/entity.onnx` + `entity_meta.json` | YOLO11n entity detector; classes `[monster,player,loot,portal,target,target_hp,player_hp]` imgsz 640 |
| `icons/icon_embedder.onnx` | ArcFace MNv3-L, 256-D |
| `icons/icon_refs.bin` + `labels.txt` + `icon_meta.json` | 12,317 refs (items/skills/sprites); `emb 256,img 64` |
| `icons/map_names.json` | internal→display map names |

Game data: `src/4rVivi.Core/Data/gamedata.json` (2,675 mobs / 1,635 skills / 29,356 items / 1,295 maps) · `src/4rVivi.Core/Data/map_mobs.json` (map→mobs, from `tools/build_map_mobs.py`).

---

## 3. OCR / text path
```
OcrReaderViewModel (marks, cadence, toggles)
  -> OcrService  (src/4rVivi.App/Services/OcrService.cs)   [capture + preprocess + gate]
     -> RapidOcrClient (src/4rVivi.App/Services/RapidOcrClient.cs)  [stdio]
        -> 4rVivi.OcrServer/Program.cs  verbs: CFG(set params) REC(full OCR) DETECT(YOLO) ICON(classify crop) SCAN
           -> RapidOcrNet (det->cls->rec ONNX)   and   EntityDetector.cs (YOLO decode+NMS)
  -> LiveStats (src/4rVivi.Core/Game/LiveStats.cs)  [role values feed HealthReader, Discord, bot]
```
- **Capture:** client-window capture is default (`PrintWindow PW_CLIENTONLY|PW_RENDERFULLCONTENT`, `GetClientRect`+`ClientToScreen`); DXGI path in `src/4rVivi.App/Capture/DxgiDuplicationCapture.cs`.
- **Region preprocessing:** `src/4rVivi.Core/Ocr/RegionProfiles.cs` (per region: Scale/CLAHE/LAB/threshold + det thresholds).
- **Stability:** `src/4rVivi.Core/Ocr/TemporalVotingService.cs` (majority vote, window 20).
- **Log line:** `[OCR] Entity scan mode=window client ... raw= entities= minScore= otherMin= boxDet= codeReads= ...`.

---

## 4. Vision / entity path (two sources → one truth)
```
capture frame
  ├─ OFF-GRF: EntityDetector(YOLO) -> filters(conf/exclusion/player/map) -> ByteTrackLite -> LiveScene
  └─ ON-GRF : VisionAssistMarkerDetector (color scan) -> preconfirmed SceneItem -> ByteTrackLite -> LiveScene
LiveScene (src/4rVivi.Core/Game/LiveScene.cs)  -> overlay draws + SmartBot targets
```
- **Tracker:** `src/4rVivi.Core/Game/ByteTrackLite.cs` — states Visible/LostGrace/Confirmed; `MaxAge=2`, spatial-first match, overlap merge. **Overlay draws Visible&&Misses==0 only** (LostGrace hidden).
- **GRF marker detector:** `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs` — ratio red test `IsMarkerRed: r>=70 && r-max(g,b)>=35 && r>=2g && r>=2b`; code decode `NormalizedRgbDistance` (per-cell brightness-normalized); reject gate `bestScore>1.45`; dedup IoU>0.45; unreadable codes still emit `Monster` with low score so diagnostics show the failure instead of hiding it.
- **GRF routing:** `OcrService.AddVisionAssistFinds()` — GRF primary when markers>0 else YOLO fallback. GRF entities carry `Source=grf`; `OcrReaderViewModel.TryBuildSceneItem()` marks them confirmed on the first frame so Smart Bot can attack immediately.

### Confidence / thresholds (current)
| Name | Value | Where |
|---|---|---|
| monster track min (`minScore`) | 0.50 | `VisionConfig.DefaultTrackConfidence` / OcrService |
| other min (loot/hp) `otherMin` | 0.55 | OcrService |
| attackable min | 0.55 | `VisionConfig.DefaultAttackConfidence` / LiveScene |
| text min | auto, usually ~0.23-0.30 | `OcrReaderViewModel.ResolveAutoTextMinScore()` / OcrService |
| tracker low / match | 0.25 / 0.24 | `VisionConfig` |
| tracker MinHits / MaxAge | 2 / 2 | `VisionConfig` / ByteTrackLite |
| YOLO NMS IoU / cap | 0.45 / 300 | EntityDetector |
| GRF marker reject | bestScore>1.45 | VisionAssistMarkerDetector |
| GRF box/code cells | defaults BOX_PX=2, CODE_CELL=5, CODE_CELLS=3 | `build_vision_grf.py`; detector reads `boxPx`/`codeCellPx` from manifest |

---

## 5. Vision Assist GRF path
```
tools/vision-grf/build_vision_grf.py
  mobId -> gamedata.json display name (NEVER .spr filename)
  scope: map_mobs.json (farmed maps) | all
  read .spr/.act -> add marker frame + ACT marker layer -> pack GRF (Master of Magic)
  library outputs: VisionAssistLibrary.grf + VisionAssist.manifest.json (mobId->name,code)
Picker: promotes chosen mobs from data\sprite\visionassistant\ into the live monster folder
Client: DATA.INI  [Data] 0=VisionAssistLibrary.grf   (loads first, overrides)
Runtime: VisionAssistMarkerDetector reads boxes/codes -> names from built-in gamedata table, with manifest auto-detect as an optional override
```
GRF I/O: `src/4rVivi.Core/Grf/{GrfArchive,SprReader,SprWriter,ModelInfo}.cs`, `tools/vision-grf/build_vision_grf.py`, and `tools/VisionGrfPicker`. The generator self-tests SPR bake/pack/read, writes standard `Master of Magic`, writes manifest marker constants, and refuses output when more than 20% of scoped mobs cannot be mapped.

Standalone builder for Vivi's packaged GRF files:
`tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat` reads `tools\ocr-train\Grf\ViviMobsBoxMasterofMagic.grf` and writes `tools\ocr-train\Grf\output\VisionAssistLibrary.grf` plus `tools\ocr-train\Grf\output\VisionAssist.manifest.json`. The app does not build GRFs; at runtime the RO client consumes the picker-edited GRF and 4ViviTools uses the built-in marker table, falling back to an auto-detected manifest when present.

---

## 6. Smart Bot path
`src/4rVivi.Core/Automation/SmartBotEngine.cs`
```
loop: if Enabled and (attached or LiveStats fresh):
  auto-reconnect (OCR words) -> map gate (TargetMap) -> weight-return -> flee(HP)
  -> ammo gate -> VISION: LiveScene.Nearest(center, predicate)
       skill: PerMonsterSkillKey(name) -> Keys.Tap(skillKey) -> 45ms -> click target
       normal: click target
     -> weave SkillRotation -> loot key
  -> else roam: click roam point -> WaitUntilArrivedAsync (watch PosX/PosY)
```
### Bot knobs (defaults)
| Field | Default | Meaning |
|---|---|---|
| `AttackKey` | A | fallback normal-attack key; current Smart Bot normal attack is left-click |
| `MoveWaitMs` | 1000 | wait after walk-click (arrive + OCR re-read) |
| `RotationMs` | 350 | loop/skill spacing |
| `FleeAtHpPercent` | 25 | retreat below this HP% |
| `ReturnAtWeightPercent` | 90 | town return |
| `StuckSeconds` | 8 | no EXP/HP/pos change → teleport |
| `MoveRadius` | 180 | roam radius (no walk box) |
| `HardwareClick` | true | real-cursor click path |
| roam box | `UseWalkBox,BoxX/Y/W/H` | confine wandering |
Per-monster rules: `src/4rVivi.Core/Automation/MonsterRule.cs` (Name, Attack/avoid, SkillKey, SkillCooldownMs).

---

## 7. Skills / attack / delays
- **Attack model:** normal = left-click monster; skill = **press skill hotkey → ~45ms arm → click target** (RO cast order). Per-skill cooldown in `_skillCdUntil`.
- **Skill grid / spammer:** `SkillSpamEngine.cs` + `SpammerGridViewModel.cs` (→ wire into bot per #33).
- **Buffs:** `BuffEngine.cs` (Rules[].Key), one "run sequence" button.
- **Autopot:** `src/4rVivi.Core/Automation/AutopotEngine.cs` — **%-only**; `pct=UseSp?sp:hp` from HP/MaxHP (`HealthReader.HpPercent`); guard `pct<=0||pct>100 skip`; `pct>Percent skip`; per-rule `UseDelayMs`,`ReactionMs`; loop 40ms; optional Mouseboost memory write.
- **Input backends:** `src/4rVivi.Core/Input/{InputMethod,KeySender,MouseSender}.cs` — `SendInput / MouseKeyEvent / PostMessage`; `KeySender.Tap` holds ≥30ms; `MouseSender.HumanMoveTo` smoothstep+jitter. (Virtual-HID backends exist but are out of scope for changes.)

---

## 8. Engines (EngineHub, src/4rVivi.Core/Automation/EngineHub.cs)
Autopot · SkillBuffs · ItemBuffs · Spammer · BotFarm · SmartBot · Macros · AtkDef · AutoStand · AutoYgg · AutoDebuff.
Base: `AutomationEngine.cs` (one async loop, `Enabled` gate, `ClearKeys()`). `StartAllLoops / DisableAll / StopAll / ClearAllKeys / InputMethod`.

## 9. Roles (src/4rVivi.Core/Game/Roles.cs)
`HP MaxHP SP MaxSP BaseEXP JobEXP Zeny Weight MaxWeight BaseLevel JobLevel PosX PosY MapName CharName ClassName Ammo`. Set via Scanner (memory) or OCR (LiveStats). Everything degrades gracefully when a role is unset.

## 10. Memory / signatures
`src/4rVivi.Core/Memory/{MemoryReader,MemoryScanner,PointerScanner,StructLocator}.cs` · `src/4rVivi.Core/Signatures/{AobResolver,SignatureBinder,SignatureProfile,ProfileStore}.cs`. Client-specific offsets resolve per launch; assign to roles in the Scanner tab.

## 11. Tools / scripts
`tools/ocr-train/` (train/export/label/mine/calibration) · `tools/vision-grf/build_vision_grf.py` · `tools/vision-grf/build_sprite_map.py` · `tools/build_map_mobs.py` · `tools/verify_debugtrace.sh` · overnight bats `RUN_OVERNIGHT_YOLO_2060S.bat`, `RUN_RESUME_YOLO_2060S.bat`, `RUN_EVERYTHING_2060S.bat`.

## 12. Logs
`DebugTrace.log` — every OCR/entity/bot/input line has `[#seq Tthread +uptimeMs]`. Key fields: `mode raw entities boxDet codeReads nameUnknown minScore clientCoords targetSource trk# state atk`. Verify with `tools/verify_debugtrace.sh`.

## 13. Specs (docs/superpowers/specs/)
Vision wiring (T1–T6) · phantom/duplicate fix (F1–F6) · GRF impl + next-steps (S1–S6,B1–B4) · verification battery · MASTER overnight plan. Read the MASTER plan for current priorities.
