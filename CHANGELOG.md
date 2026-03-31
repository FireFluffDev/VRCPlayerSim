# Changelog

All notable changes to VRCSim will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Fixed
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
- `TickAll`/`TickFixedAll` null-check UdonBehaviours during iteration (guards against mid-loop destruction)
- `ResolveMethod` now matches parameter types (not just count) when args are available

### Added
- `GetBotByPrefix` for substring name matching (replaces old `GetBot` behavior)
- `FormatValue` now handles `Vector3`, `Quaternion`, and `Color` types
- `DeepEquals` helper (internal) for array-aware equality
- `LICENSE` file (MIT)
- `CHANGELOG.md`

### Changed
- Assembly definition: `includePlatforms` set to `["Editor"]` — prevents VRCSim from shipping in player builds
- Assembly definition: `autoReferenced` set to `false` — consumers must explicitly reference VRCSim
- `RemoveAllPlayers` doc comment now explicitly notes it does NOT fire OnPlayerLeft events
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
