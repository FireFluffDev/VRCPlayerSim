# Changelog

All notable changes to VRCSim will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- **Bot Controller EditorWindow** (`VRCSim > Bot Controller`): spawn bots, possess them (Game View becomes that bot — walk, click stations, interact), Tab to cycle, Esc to release. Scene View gizmos + click-to-possess.
- **Possession API**: `Possess`, `Unpossess`, `IsPossessing`, `PossessedBot` — persistent identity swap so ClientSim input acts as the bot
- **Movement API**: `ApplyForce`, `SetVelocity`, `MoveToward`, `GetVelocity` — physics-based movement for bots and GameObjects
- `gen_api.py` now includes `VarState.cs` and `SimProxy.GameObject.cs` source files
- `State Reading` row in README API summary table (`GetField`, `GetField<T>`, `GetBoth`)
- `SetVarHeapOnly` listed in README API summary table
- **Self-tests**: 30 unit tests for CoerceValue, DeepEquals, VarState, SimSnapshot.Diff — runs without ClientSim

### Fixed
- `_syncedVarNameCache` now cleared in `SimReflection.Reset()` — prevents stale cache after domain reload
- `TickAll`/`TickFixedAll` now re-query UdonBehaviours each frame AND null-check (previously captured array once before loop)
- `SimSnapshot.Diff` now uses deep equality for arrays — previously reported false positives for unchanged `int[]`/`float[]`/`string[]`/`bool[]` synced vars (reference equality vs value equality)
- `SimSnapshot.Diff` now detects per-variable removals within existing objects (previously only detected whole-object removals)
- `VarState.InSync` now correctly compares arrays by value
- `CoerceValue` returns `null` on failed coercion instead of silently returning the original wrong-type value
- `SetField` heap sync failure now logs a warning instead of being silently swallowed
- `RestoreLocalPlayerRefs` failure now logs a warning instead of being silently swallowed
- `FireOwnershipRequest` handler exceptions now log a warning instead of being silently swallowed
- `GetBot` now uses exact name match (was substring match — `GetBot("Alice")` could match `"Alice2"`)
- `SpawnPlayer` finds new player by highest ID instead of assuming array append order
- `RunAsPlayer` validates player is non-null and valid before swapping
- `FindProxy` now checks fully qualified `UdonSharp.UdonSharpBehaviour` type name instead of unqualified `Name`
- `GetPath` uses `Add` + `Reverse` instead of `Insert(0, ...)` — O(n) instead of O(n²)
- `ResolveMethod` now matches parameter types (not just count) when args are available

- `GetBotByPrefix` for substring name matching (replaces old `GetBot` behavior)
- `FormatValue` now handles `Vector3`, `Quaternion`, and `Color` types
- `DeepEquals` helper (internal) for array-aware equality
- `LICENSE` file (MIT)
- `CHANGELOG.md`

### Changed
- `RemoveAllPlayers` now fires OnPlayerLeft events by default (pass `fireEvents: false` for fast teardown)
- `DeepEquals` promoted from `internal` to `public` (useful utility for consumers)
- Assembly definition: `includePlatforms` set to `["Editor"]` — prevents VRCSim from shipping in player builds
- Assembly definition: `autoReferenced` set to `false` — consumers must explicitly reference VRCSim
- README API table: `Call<T>` → `CallAs<T>` (matches actual method name)
- `package.json`: added `repository` and `license` fields

## [0.1.0] - 2025-05-01

### Added
- Initial release: player spawning, perspective swapping, station management
- `RunAsPlayer` / `RunAsClient` with nesting support
- `SimProxy` for C# proxy field access and method invocation
- `SimSnapshot` for synced state capture and diffing
- `SimNetwork` for ownership and master simulation
- `SimReflection` for ClientSim internal access
