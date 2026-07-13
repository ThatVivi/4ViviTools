# Agent rules for 4ViviTools

- Search with `rg` first. Use `apply_patch` for manual edits.
- Windows and PowerShell: do not use bash heredocs such as `python - <<'PY'`. Use `python -c`, a temporary script, or existing files.
- This repo can be intentionally dirty. Never revert unrelated user changes.
- Deliver Release builds when asked to build: `dotnet build 4rVivi.sln -c Release` or publish the requested project.
- Keep Korean sprite paths and GRF paths encoding-safe. Use UTF-8 for docs/JSON and code page handling where GRF filenames require it.
- Do not touch VIIPER, FakerInput, ViGEm, or reWASD routing unless the user explicitly asks for input-backend work.
- Vision Assist GRF mode is primary when enabled. YOLO/OCR monster detection is the fallback.
- Generator and detector constants must stay paired: `BOX_PX`, `CODE_CELL`, `CODE_CELLS`, `CODE_LEVELS`, and `ColorCode`.
- Keep UI beginner-friendly. Show `-1 = Auto` where timing is configurable and hide OCR internals unless they are diagnostic.
- After changing constants, paths, runtime wiring, or workflows, update `docs/CODEX-MAP.md` and user docs in the same pass.
