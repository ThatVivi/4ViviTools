# Original 4RTools (reference) ↔ our replica

> **Re-extract:** `unzip -l 4RTools-main.zip | grep -E "Forms/|Model/|Utils/"` ; read `Model/Autopot.cs`, `Utils/EffectStatusIDs.cs`, `Utils/ProcessMemoryReader.cs`.

Source: `4RTools-main.zip` — **C# .NET WinForms** app. We replicate its tabs/behaviour but feed it
**OCR (LiveStats)** instead of process-memory reads.

## How it reads the game (the part we replace)
- **`Utils/ProcessMemoryReader.cs`** — Win32 `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`
  against the RO client process. Reads HP/SP and the active **status-effect array** at known offsets.
- **`Utils/EffectStatusIDs.cs`** — enum of **EFST status IDs** (e.g. `POISON=883`, `CURSE=884`,
  `EFST_L_LIFEPOTION=294`, buff/song/potion ids). 4RTools checks which of these are present in memory
  to know what buffs/debuffs are active. **These are the same EFST ids in GRF `efstids.lub` and
  rAthena `status.yml` `Icon: EFST_*`** (see [client-systems.md](client-systems.md), [status-effects.md](status-effects.md)).
- **`Utils/KeyboardHook.cs`** / `Utils/Interop.cs` — global key send/hook. **`Model/_4RThread.cs`** —
  per-feature worker threads. **`Utils/RObserver.cs`** — pub/sub between models and forms.
- **`Model/Client.cs`** — wraps the RO process; `IsHpBelow(percent)` etc. read from memory.

## Feature models (`Model/`)
| Model | Feature |
|-------|---------|
| `Autopot.cs` | HP/SP auto-potion: `hpKey/hpPercent`, `spKey/spPercent`, delay; thread presses key when `IsHpBelow` |
| `Autobuff.cs` | auto recast of self/skill buffs on timers |
| `AutoRefreshSpammer.cs` | skill spammer / auto-refresh |
| `DebuffsRecovery.cs` | detects debuff EFST ids → uses cure item/skill |
| `StatusRecovery.cs` | status auto-recovery |
| `ATKDEFMode.cs` | ATK↔DEF macro switching |
| `Macro.cs` | macro engine |
| `Buff.cs` / `BuffContainer.cs` / `BuffRenderer.cs` | active-buff tracking + on-screen render |
| `Profile.cs` / `UserPreferences.cs` | per-server profiles & settings |

## Forms / tabs (`Forms/`)
`Container` (main shell), `AutopotForm`, `SkillAutoBuffForm`, `DebuffRecoveryForm`, `ATKDEFForm`,
`MacroSwitchForm`, `MacroSongForm`, `AHKForm`, `ServersForm`/`AddServerForm`, `ProfileForm`,
`AutoPatcher`/`ClientUpdaterForm`, `AdvertisementForm`.

## Mapping to our replica (`FourRToolsShellView` / `FourRToolsShellViewModel`)
| 4RTools | Ours |
|---------|------|
| ProcessMemoryReader (HP/SP/status) | **OCR `LiveStats`** (HP/SP/name from screen) |
| EffectStatusIDs in memory | EFST model (planned: read status bar via OCR/icons) |
| AutopotForm | Autopot tab (pot rules off OCR HP/SP) |
| SkillAutoBuff / Spammer | Autobuff-Skills / Skill Spammer tabs |
| DebuffRecovery | Debuff tab |
| ATKDEFForm | ATK×DEF tab |
| MacroSwitch / MacroSong | Macro Switch / Macro Songs tabs |
| Container (light WinForms) | dark-themed shell, same tab set |

The one substantive difference: **memory → OCR**. Everything else (tabs, keys, timers, profiles) mirrors.
