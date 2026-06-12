# 4rVivi 2.x — Avalonia RO toolkit

Modern Avalonia + .NET 8 toolkit for Ragnarok Online (Windows). MVVM + DI, custom dark theme,
unit-tested engine core, single self-contained .exe.

## Build (GitHub Actions — recommended)
1. Push this repo to GitHub.
2. `.github/workflows/build.yml` restores, runs Core tests, then publishes a single self-contained `4rVivi.exe`.
3. Actions run → Artifacts → download **4rVivi** → run `4rVivi.exe` **as Administrator**.

## Build locally (needs .NET 8 SDK)
```
dotnet test
dotnet publish src/4rVivi.App/4rVivi.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Features
COMBAT — Autopot (ultra-fast mouseboost path), Buffs, Skills (spam + rotation), Smart Bot, basic Bot, RCX overlay.
TRACKERS — MVP timer/tracker (seed names from the DB), Buff/Debuff HUD countdowns, Loot log. (EXP/Zeny/hr live on the Stats page.)
MACROS — chain macros (equip/skill switch, auto-vend, auto-storage; on-demand or looped), login/reconnect (DPAPI-encrypted).
DATA — Database (2.6k mobs w/ drops, 1.6k skills, 29k items, 1.3k maps), Item-DB script editor, damage/stat calculator, NPC snippet library, Homunculus/Pet AI generator.
TOOLS — native GRF browse + extract, SPR/ACT/RSM viewer, launcher for external GRFEditor/ActEditor/Nemo.
SYSTEM — Scanner (parallel, aligned, admin, 64-bit aware), Servers/Profiles, Stats, Settings.

## How the bot & trackers get data
Memory addresses are server-specific. Open **Scanner**, find a value (HP, EXP, Zeny, Weight, PosX/Y…),
pick the role from the dropdown, click **Use as role** — it saves to the active profile. The Smart Bot,
Autopot, MVP-related EXP proxy, and Stats then read those addresses. Everything degrades gracefully when
an address isn't set (e.g. stuck-detection falls back to "no HP/EXP change → teleport").

## Honest scope notes
- **Smart Bot is input-level** (sends keys); nearest-monster targeting uses your scanned addresses, not packets.
- **GRF reader** handles standard 0x200 zlib archives; DES-encrypted entries are skipped.
- **SPR viewer** renders sprite frames; **ACT/RSM are metadata only** (use the bundled external editors for full editing/3D).
- **External editors are launched, not embedded** — set their paths on the External Editors page.
- The big game database is generated from the rAthena YAML you provided.

## Projects
- `src/4rVivi.Core` — engine: Memory, Input, Game, Automation, Trackers, Macros, Tools, Grf, Data, Settings, Localization.
- `src/4rVivi.App` — Avalonia UI (MVVM + DI), custom theme, overlay.
- `tests/4rVivi.Core.Tests` — xUnit (CI gate).
