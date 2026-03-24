# VRC Player Simulator

Simulates multiplayer interactions on top of VRChat's ClientSim. Spawns bot players that can sit in stations, own objects, and trigger Udon events — following VRChat networking rules.

Works with **any** VRChat world project. No SDK modifications required.

## Installation

Add to your project's `Packages/manifest.json`:

```json
"com.fire.vrcsim": "file:C:/Users/Fire/VRCPlayerSim"
```

## What It Simulates vs What It Can't

| Feature | Real VRChat | ClientSim | VRCSim |
|---------|-------------|-----------|--------|
| Remote players exist | ✅ | ✅ (skeletal) | ✅ (with actions) |
| Remote sits in station | ✅ | ❌ blocked | ✅ |
| Station events correct player | ✅ | ❌ always LocalPlayer | ✅ |
| Non-master perspective | ✅ | ❌ always master | ✅ RunAsPlayer |
| ForceKinematicOnRemote | ✅ | ❌ (TODO in SDK) | ✅ |
| Ownership + kinematic | ✅ | partial | ✅ |
| OnDeserialization | ✅ | ❌ for remote | ✅ manual |
| True network latency | ✅ | ❌ | ❌ |
| Per-client Udon VM | ✅ | ❌ | ❌ |

## Quick Start

```csharp
using VRCSim;

// Initialize (once per play mode session)
VRCSim.VRCSim.Init();

// Spawn players
var alice = VRCSim.VRCSim.SpawnPlayer("Alice");
var bob = VRCSim.VRCSim.SpawnPlayer("Bob");

// Sit in stations (fires events through real ClientSim pipeline)
var station = GameObject.Find("Station_0");
VRCSim.VRCSim.SitInStation(alice, station);

// Check state
Debug.Log(VRCSim.VRCSim.GetStateReport());

// Clean up
VRCSim.VRCSim.RemoveAllPlayers();
```

## The Perspective Swap (Key Feature)

`RunAsPlayer` temporarily makes Unity believe it's running on a different
player's client:

```csharp
var bob = VRCSim.VRCSim.SpawnPlayer("Bob");

// Inside RunAsPlayer:
//   Networking.LocalPlayer == bob
//   Networking.IsMaster == false (bob isn't master)
//   bob.isLocal == true
VRCSim.VRCSim.RunAsPlayer(bob, () => {
    // Any Udon code triggered here runs from Bob's perspective.
    // Master-gated code will correctly SKIP.
    VRCSim.VRCSim.SitInStation(bob, stationObj);
});

// SitInStation already calls RunAsPlayer internally, so this is equivalent:
VRCSim.VRCSim.SitInStation(bob, stationObj);
```

## How Station Events Work

```
VRCSim.SitInStation(bot, stationObj)
  └─ RunAsPlayer(bot, ...)              ← swap Networking.LocalPlayer to bot
       └─ FireStationEnterHandlers()     ← call IClientSimStationHandler on station
            └─ ClientSimUdonHelper.OnStationEnter()  ← REAL ClientSim code
                 └─ RunEvent(eventName, ("Player", Networking.LocalPlayer))
                                                      ↑ returns bot, not original player
```

This catches MP-11 bugs (master-gated station callbacks) because
`Networking.IsMaster` returns `false` inside the swap.

## API Reference

### Player Lifecycle
- `Init()` → error string or null
- `SpawnPlayer(name)` → VRCPlayerApi
- `RemovePlayer(player)`
- `RemoveAllPlayers()`
- `GetBots()` → List
- `GetBot(name)` → VRCPlayerApi

### Movement
- `Teleport(player, position, rotation?)`

### Stations
- `SitInStation(player, stationObj)` → bool
- `ExitStation(player, stationObj)` → bool

### Perspective
- `RunAsPlayer(player, action)` — the killer feature

### Ownership
- `SetOwner(player, obj)` — transfers + enforces kinematic
- `GetOwner(obj)` → VRCPlayerApi
- `EnforceKinematic(obj)` — ForceKinematicOnRemote
- `ValidateKinematic()` → List of issues

### Udon Access
- `GetVar(obj, varName)` → object
- `SetVar(obj, varName, value)`
- `SendEvent(obj, eventName)`

### Validation
- `GetStateReport()` → formatted string
- `ValidateVars(obj, (name, expected)...)` → pass/fail report
- `SimulateDeserialization(obj)`
- `SimulateLateJoiner(obj)`

## Files

```
Runtime/
  VRCSim.cs          (374 lines) — Public API facade
  SimNetwork.cs      (257 lines) — Perspective swap, kinematic, ownership
  SimReflection.cs   (307 lines) — Cached reflection into ClientSim internals
```

## Limitations

- **Single Udon VM** — all events fire in one process, can't simulate true per-client execution
- **Instant events** — no network delay simulation
- **Reflection-based** — clear error messages if SDK changes break targets
- **No VR input for bots** — bots act via API, not controllers
