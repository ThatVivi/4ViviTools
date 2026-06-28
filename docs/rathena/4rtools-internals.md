# 4RTools — Internals (every Model/Util) → our OCR equivalents

> **Re-extract:** `unzip -o 4RTools-main.zip "4RTools-main/Model/*" "4RTools-main/Utils/*" -d out && wc -l out/4RTools-main/Model/*.cs out/4RTools-main/Utils/*.cs`.

Companion to [original-4rtools.md](original-4rtools.md) and [4rtools-ui-spec.md](4rtools-ui-spec.md).
This is the **file-by-file mechanics** (task #15 → 100%).

## Memory layout (the heart — what we replace with OCR)
`Model/Client.cs`:
- `currentHPBaseAddress` — pointer to the HP block.
  - HP = `Read(base)`, MaxHP = `Read(base+4)`, SP = `Read(base+8)`, MaxSP = `Read(base+12)`.
  - `IsHpBelow(%) = HP*100/MaxHP <= %` (same for SP).
- `currentNameAddress` — character name string.
- **`statusBufferAddress = currentHPBaseAddress + 0x474`** — the active **status-effect buffer**.
  - A status is active when `Read(statusBufferAddress + EFST_ID*4) > 0`. This is how Autobuff/Debuff
    know which buffs/debuffs are present.
- `Utils/ProcessMemoryReader.cs` — `OpenProcess`/`ReadProcessMemory` Win32 wrappers.
- **Our replacement:** OCR `LiveStats` (HP/SP/Name); the status buffer → a status-bar OCR / posture box
  feeding the same EFST ids (see [client-systems.md](client-systems.md)).

## Input (`Utils/KeyboardHook.cs`, `Model/AHK.cs`)
- `AHK` uses `keybd_event` / `mouse_event` (SendInput-style global input).
- `KeyConfig { Key key, bool ClickActive }`; `AhkEntries: Dict<string,KeyConfig>`; `AhkDelay=10`;
  `mouseFlick`; modes `COMPATIBILITY` / `SPEED_BOOST`.
- **Our replacement:** `KeySender`/`MouseSender` (PostMessage). Per-key click + flick are config.

## Feature models
| Model | Data | Logic | Our engine |
|-------|------|-------|------------|
| `Autopot` | `hpKey/hpPercent`, `spKey/spPercent`, `delay` | thread: if `IsHpBelow` → tap key | `AutopotEngine` ✅ |
| `Autobuff` | `buffMapping: Dict<EFST,Key>`, `delay` | if a mapped buff's **EFST not active** → recast its key; Quagmire disables AGI/Conc/TrueSight/Adrenaline/SpearQuicken; only when HP ≥ `MINIMUM_HP_TO_RECOVER` | `BuffEngine` (timer; +status-OCR = true detection) |
| `DebuffsRecovery` | `buffMapping: Dict<EFST,Key>`, `delay` | if a debuff **EFST is active** → press cure key | `BuffEngine`/Debuff tab |
| `StatusRecovery` | status list | auto-stand + cure on status | `AutoStandEngine` ✅ |
| `AutoRefreshSpammer` | key, delay | spam a refresh key | `SkillSpamEngine` |
| `ATKDEFMode` | `ahkDelay`, `switchDelay`, `keySpammer`, `keySpammerWithClick`, `defKeys/atkKeys: Dict<slot,Key>` (6 slots each) | spam + switch ATK/DEF equip sets | `AtkDefEngine` ✅ |
| `Macro` | `MacroKey{key,delay,hasClick}`; `ChainConfig{trigger,daggerKey,instrumentKey,delay}` | Macro Switch (7-key chains) + Macro Songs (dagger/instrument) | `TriggeredMacroEngine` |
| `Buff` | per-class lists of `Buff{name, EFST, icon}` | the data behind Autobuff-Skills (Archer/Swordman/… groups) | → our skill catalog + GRF icons |
| `Client`/`Profile`/`UserPreferences` | server/profile config | per-server HP/name addresses, profiles | → Servers/Settings + OCR (no addresses) |

## Buff list data (Autobuff-Skills tab)
`Buff.cs` has `GetArcherSkills()`, `GetSwordmanSkill()`, `GetMageSkills()`, … each returning
`Buff(name, EFST_*, Icons.<resource>)`. The icon resource name ≈ the **skill aegis** (e.g.
`ac_concentration`, `sm_endure`, `cr_autoguard`), so our GRF skill-icon-by-aegis maps directly.
Each buff carries its **EFST id** (for detection) + **recast key** (user-set).

## Status buffer = EFST = our knowledge
The status-buffer indices ARE the EFST ids in `Utils/EffectStatusIDs.cs` (365 lines) — the same ids in
GRF `efstids.lub` and rAthena `status.yml`. So the EFST table is the shared key between: 4RTools memory
detection ↔ rAthena status ↔ GRF icons ↔ our (future) status-OCR. One table powers buff/debuff
detection across everything.

## Coverage (task #15)
Read & documented: Client, ProcessMemoryReader, AHK, KeyboardHook, Autopot, Autobuff, DebuffsRecovery,
StatusRecovery, AutoRefreshSpammer, ATKDEFMode, Macro, Buff, EffectStatusIDs, Profile, UserPreferences,
Tracker, BuffRenderer/BuffContainer, LocalServerManager, Advertiser, Constants, RObserver, FormUtils.
Remaining for 100%: the huge `Forms/*.Designer.cs` exact pixel layouts (captured via the UI spec +
screenshots) and the AutoPatcher/ClientUpdater (out of scope — client patching).
