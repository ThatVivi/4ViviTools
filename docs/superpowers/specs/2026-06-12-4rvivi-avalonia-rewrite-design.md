# 4rVivi 2.0 — Avalonia rewrite design

Date: 2026-06-12 · Status: approved

## Goal
Rebuild 4rVivi from scratch on a modern stack with clean, testable code, carrying every
feature from the WinForms version with improvements throughout.

## Stack (approved)
- Avalonia 11 UI, .NET 8, custom XAML styling, dark + violet visual language.
- MVVM + DI (CommunityToolkit.Mvvm + Microsoft.Extensions.DependencyInjection).
- Full from-scratch rewrite. Memory offsets are user-discovered via the Scanner and stored
  per-profile, so no offset database ships.
- Single self-contained win-x64 .exe built by GitHub Actions; admin manifest for memory access.

## Solution layout
- `src/4rVivi.Core` (net8.0-windows, no UI) — Memory (reader, scanner, pointer chains),
  Input (PostMessage key sender), Game (process watcher, session, address book, health),
  Automation (autopot, buffs, skill spam/rotation, bot/farm; async + CancellationToken),
  Macros (player + DPAPI credentials), Data (gamedata.json db), Settings, Localization.
- `src/4rVivi.App` (Avalonia) — DI host, MainWindow shell (sidebar nav + top bar + status),
  one View/ViewModel per page via a ViewLocator, custom ControlThemes, click-through overlay.
- `tests/4rVivi.Core.Tests` (xUnit) — pure-logic tests; gate CI before the exe publishes.

## Feature map (all carried)
Dashboard · Autopot/AutoPots+ (dual HP/SP %+flat, reaction, use-delay) · Buffs (skill + item)
· Skills (spammer + rotation that subsumes switcher/songs) · Bot/Farm (attack/loot + flee%)
· Macros + reconnect (DPAPI creds) · RCX Overlay (cast-range/AoE/cross, click-through) ·
Database · Scanner (ArtMoney-style, admin, 64-bit aware, chunked) · Servers/Profiles · Stats
· Settings (language EN/AR, accent, opacity, humanize, acrylic).

## Architecture
A single `GameSession` holds the attached process, active profile, and address book; a
master ON/OFF flag gates every engine. `EngineHub` owns the engines and starts their async
loops once. ViewModels are constructor-injected and bind to the engines; edits to pot/buff
rules mutate the same instances the engines read. Settings persist to
`%AppData%/4rVivi/settings.json`.

## Improvements over 1.0
Async/cancellation loops (no raw threads), testable UI-free core, per-profile saved addresses,
live HP/SP polling, JSON settings, real Mica/acrylic + opacity, cleaner separation, CI tests.

## Known risks
XAML/binding errors surface only in CI (no local SDK here); click-through overlay uses a small
Win32 interop on the Avalonia handle; self-contained exe is larger than the old Costura build.
