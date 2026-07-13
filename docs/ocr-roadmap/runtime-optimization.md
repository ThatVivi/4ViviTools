# OCR Runtime Optimization

4ViviTools now uses the fastest available OCR transport first:

- Full-frame OCR/YOLO scans use memory-mapped BGRA frames, so normal capture no longer has to write PNG frames to disk.
- PNG commands remain in place as automatic fallback for compatibility.
- Monster boxes are stabilized through `ByteTrackLite`, which lets low-confidence YOLO boxes extend existing tracks without creating new click targets.
- When the whole RO scene shifts because the character walks, `ByteTrackLite` estimates the shared screen motion before matching. This keeps existing boxes pinned to the same monsters instead of spawning duplicate boxes after a fast camera/world shift.
- The YOLO class `target` is treated as a generic monster candidate at runtime. Some Roboflow/RO datasets annotate attackable or selected monsters as `Target`, so dropping that class makes the app look like it detected no monsters even when the model found them.
- The OCR worker now also runs sprite-name recognition on `target` boxes, so selected/attackable monsters can still get real monster names instead of staying generic.
- Fast monster-only frames skip the app-side duplicate icon retry. The worker already tries sprite recognition during detection, and avoiding repeated per-box icon round trips keeps the 8 ms / ~120 FPS overlay path lighter.
- If a farm map is selected, sprite names that are not in that map's rAthena spawn list are downgraded to generic `Monster`. This avoids confident but impossible labels from lookalike sprites.
- With a farm map selected, Smart Bot accepts generic `Monster` boxes at a lower confidence because map focus already narrows the world. Without a map, generic boxes stay stricter to avoid false attacks.
- When the monster HP bar is visible and falls to about empty, Smart Bot closes the engaged target immediately instead of wasting another skill cast while waiting for the sprite box to disappear.
- ONNX Runtime CUDA and DirectML are included. Leave `OCR_ONNX_EP` unset for safe auto mode:
  - NVIDIA: auto tries CUDA first when CUDA 12 runtime DLLs are present, then DirectML, then CPU.
  - AMD: select `amd/directml` or `directml` in Advanced OCR, or set `OCR_ONNX_EP=directml`.
  - DirectML is the Windows acceleration path for AMD/Intel GPUs in this build.
  - CPU fallback remains automatic if a GPU provider cannot start.

The smoke path to check after changes is:

```powershell
dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release
dotnet test "D:\vs code clone 4rtool\4ViviTools\tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj" -c Release --no-build
```
