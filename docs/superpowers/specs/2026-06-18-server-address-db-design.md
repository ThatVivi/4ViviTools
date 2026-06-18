# Two data methods: Server-address auto-detect (4RTools model) + manual scanner
Design spec · 2026-06-18

## Why
Value-scanning common numbers (HP=91) is unreliable (binds wrong addresses). The proven method used by
4RTools is a per-client table of fixed absolute addresses. Confirmed from 4RTools `Model/Client.cs`:
- HP   = ReadUInt32(hpAddress)
- MaxHP= ReadUInt32(hpAddress + 4)
- SP   = ReadUInt32(hpAddress + 8)
- MaxSP= ReadUInt32(hpAddress + 12)
- Name = null-terminated string at nameAddress (40 bytes, default encoding)
- validity: HP read > 0
RO clients run at a fixed image base (ASLR off), so absolute VAs are stable across launches.
`supported_servers.json` keys entries by process (exe) name; the same address (e.g. 0x010DCE10)
covers many servers sharing one hexed client.

## Two tabs (one per method)
1. **Auto-Detect (new, primary)** — ships 4RTools `supported_servers.json` (MIT, attributed). On attach,
   match the running exe name, read HP at each candidate `hpAddress`, pick the one that reads HP>0,
   bind roles HP/MaxHP(+4)/SP(+8)/MaxSP(+12) + CharName(nameAddress). Manual override: a server dropdown
   and editable hpAddress/nameAddress boxes (RagnarokInfo style) with a live read preview + Save.
2. **Scanner (existing, fallback/discovery)** — value-scan + What changed + Auto-capture, with fixes:
   relabel "candidates" (not values), Weight ×10 handling, reject 0-value scans, and the scroll fix.

## Wiring (verified)
HealthReader (top bar), StatReader (Stats tab) and CharacterStateReader (Discord) all read the same
roles HP/MaxHP/SP/MaxSP(/CharName) from the session AddressBook. Binding those once updates all three.

## Components
- Core/Servers/ServerProfile.cs — { Name(process), Description, HpAddress, NameAddress } + hex parse.
- Core/Servers/ServerProfileDb.cs — loads supported_servers.json (shipped) + user overrides; MatchByProcess(name).
- Core/Servers/ServerBinder.cs — TryResolve(session, forced?): validate by reading HP>0, return role->absolute address map.
- App/ViewModels/AutoDetectViewModel.cs + Views/AutoDetectView.axaml — auto/manual UI, live preview, persists to active profile.
- supported_servers.json shipped to output; ServerProfileDb reads AppContext.BaseDirectory.

## Scroll fix
The whole page is inside one ScrollViewer (MainWindow), so Scanner tables get unbounded height and
never scroll internally. Fix: bound the three tables to a fixed height -> independent V/H scrollbars.

## Honest constraints
- Absolute addresses assume ASLR-off clients (true for RO). If a client rebases, auto-match fails -> manual.
- Gepard Shield can still block or scramble reads on some servers (banner warns).
- Not every server is in the table; unknown clients use manual paste or the Scanner.

## Testing
- Unit: ServerProfile hex parsing; ServerProfileDb match-by-process; offset math (hp+4/+8/+12).
- Manual: attach a supported client -> auto-binds -> top bar/Stats/Discord show values.
- CI build is the compile gate.

## Attribution
supported_servers.json and the HP/Max/SP offset layout are from 4RTools (MIT License), github.com/4RTools/4RTools.
