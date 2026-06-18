# 4rViviTools — Signature Memory Engine + Scanner Cleanup + Discord RPC Default ID
Design spec · 2026-06-18

## Problem
- The OCR reader and the manual ArtMoney-style scanner are unreliable to the point of "not working at all."
- 4RTools / ro-tools feel instant because they do NOT scan at runtime: they apply known pointer/AOB signatures per client, so values resolve on every launch with no user effort. ro-tools also uses an Interception kernel driver for input.
- Discord RPC currently asks the user for an Application ID. Other tools "just work" because they bake in a default Application ID and auto-connect to the local Discord IPC pipe. The ID is still mandatory (it is sent in the IPC handshake) — it is simply shipped, not requested.

## Goals
- Server-agnostic auto-binding of HP/SP/MaxHP/MaxSP/Base level/Job level/X/Y/Zeny via a signature engine (approach A), built from a hybrid of shipped signatures (B) and a built-in pointer scanner (C).
- Drastically simplify the Scanner tab; remove OCR and redundant refine buttons.
- Discord RPC works with zero setup by shipping a default Application ID (1517200569486413954), overridable by advanced users.

## Non-goals / honest constraints
- No anti-cheat evasion. Clients protected by Gepard Shield or packed with Themida may block or flag ANY external memory reader (4RTools/ro-tools included). Target = unprotected clients.
- No Interception kernel driver in this iteration (input method unchanged).
- OCR is removed from the scanner flow, not improved.

## Architecture
Three isolated Core components plus a binder; all write into the existing role-based `AddressBook`, so HP reader, Discord state engine, and analytics light up unchanged.

### 1. PointerScanner (Core, engine — "C")
- `IReadOnlyList<PointerPath> Find(IntPtr target, ScanOptions opts)`
- `IntPtr Resolve(PointerPath path)`
- `PointerPath { string ModuleName; long BaseOffset; int[] Offsets; }`
- `ScanOptions { int MaxDepth = 3; int MaxOffset = 0x1000; bool AlignedOnly = true; int MaxResults = 20; }`
- Method: enumerate pointer-sized aligned values in loaded-module static regions to build an address→value map; BFS backwards from `target` finding static-anchored chains within offset range, up to MaxDepth. Every returned path is validated by re-resolving to `target`. Prefer shortest/most-stable.
- 32/64-bit aware (pointer size from `MemoryReader.TargetIs64Bit()`).

### 2. SignatureProfile + ProfileStore (Core — "B")
- `SignatureProfile { string ClientId; Dictionary<string, RoleBinding> Roles; }`
- `RoleBinding { PointerPath Path; string Type; }`
- `ClientId = exeName + "|" + fileSize + "|" + versionString` (from the process main module).
- `ProfileStore { SignatureProfile? Find(string clientId); void Save(profile); string Export(profile); SignatureProfile Import(json); }`
- JSON under `%AppData%/4rVivi/Profiles/`. Ships a starter `signatures.json` (read-only seed) merged with user profiles. Export/Import reuses the existing Marketplace/profile plumbing for sharing.

### 3. AobResolver (Core — "B" hardening)
- `IntPtr FindByPattern(byte[] signature, string mask)` over the module image.
- Used only when a plain module-offset base proves unstable across client patches; a RoleBinding may anchor its base to a pattern instead of a fixed offset.

### 4. SignatureBinder (Core)
- `BindResult TryAutoBind(GameSession session)`: identify client → `ProfileStore.Find` → resolve each RoleBinding → write into `session.AddressBook`. Returns which roles bound.
- Called on attach/re-attach.

## Data flow
attach → identify client → profile match?
- yes → resolve all paths → bind roles → done (no scanning).
- no → user pins ONE value (First scan + What changed) → "Make permanent" runs PointerScanner on that address → save RoleBinding into a profile → auto-binds on every future launch.

## Scanner tab (UI) changes
New flow: Attach → auto-bind if known → else pin one value → Make permanent.
- KEEP: First scan, What changed, Make permanent (new), Re-attach, Assign role + Apply, Found/Compare/Saved tables.
- REMOVE: Decreased, Increased, Unchanged, Snapshot, and ALL OCR controls (Auto-setup OCR, Read values OCR, Auto-find HP, OCR region + language pickers).
- ADD: status banner — "Auto-bound HP, SP, Level from profile ✓" or "No profile for this client — pin one value to create it."
- "Make permanent" command: takes the selected/last-pinned address + type, runs PointerScanner, saves the chosen path under the role, reports the resulting pointer path.

## Discord RPC changes
- Hardcode default `DEFAULT_APP_ID = "1517200569486413954"` in DiscordPresenceBootstrap.
- `DiscordAppId` setting becomes an optional override; when blank, use the default.
- Keep the existing always-on presence behavior (P5R pattern) and the Settings toggle.

## Affected code
- New: `src/4rVivi.Core/Memory/PointerScanner.cs`, `src/4rVivi.Core/Signatures/SignatureProfile.cs`, `ProfileStore.cs`, `AobResolver.cs`, `SignatureBinder.cs`, seed `signatures.json`.
- Edit: `GameSession`/`MemoryReader` (client identity, pointer-size read, ReadPointer), `ScannerViewModel` + `ScannerView.axaml` (cleanup + Make permanent + auto-bind banner), `DiscordPresenceBootstrap` (default ID), `OcrService` left in tree but unreferenced by the scanner.

## Testing / verification
- Unit tests (Core, xUnit): PointerScanner round-trip (plant a known pointer chain in a buffer, Find → Resolve == target); ProfileStore save/load/export/import; ClientId formatting.
- Static checks: brace balance, XAML well-formed, DI coverage, ReflectionBinding resolution.
- Manual (user, on a real unprotected client): auto-bind known client; pin+Make permanent on unknown client; relaunch → still bound.
- CI build is the real compile gate (no C#/Avalonia compiler in the design sandbox).

## Risks
- Pointer scanner depth/time vs reliability — bounded defaults, tunable.
- Protected/packed clients — out of scope, documented.
- Pointer-scan false positives — mitigated by validation + preferring shortest stable chain; user can pick from candidates.

## Prior art / references (GitHub)
Validates the chosen hybrid (B+C) — these do exactly this:
- **RagnarokInfo** (hsengu) — reads RO process memory from ONE user-editable address in an XML and calculates the other offsets. This is precisely the SignatureProfile idea; confirms approach B.
- **ragnarok-pybot** (diogoftp) — reads char name, map, coordinates, HP, MaxHP from RO memory. Same role set we auto-bind.
- **Squalr / Squalr-Sharp** — open-source Cheat-Engine-class scanner in C# (now Rust) with a real **pointer-scan** implementation; reference for our PointerScanner (C).
- **OpenKore** — RO automation, but PACKET-based not memory-based; noted as a different (out-of-scope) approach.
- Community note: RO entity structs commonly expose a pointer where offset 0x8 → Entity; useful seed for starter signatures.
- Memory plumbing references: erfg12/Memory.dll, Reloaded.Memory.Sigscan (AOB), for AobResolver.

Clarification: `versionString` comes from the process main module `FileVersionInfo` (falls back to empty if unavailable; ClientId still uses exeName+fileSize).
