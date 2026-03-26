# VRCSim — VRChat Multiplayer Simulator

A Unity package that simulates multiplayer interactions on top of [ClientSim](https://docs.vrchat.com/docs/clientsim), enabling automated testing of VRChat worlds without requiring multiple VRChat clients.

## The Problem

ClientSim only simulates a single local player. Testing multiplayer mechanics (station seating, ownership transfer, synced variables, master-gated logic) requires manually launching VRChat with friends. This is slow, unrepeatable, and error-prone.

## The Solution

VRCSim extends ClientSim by:
- **Spawning remote player bots** via ClientSim's built-in `SpawnRemotePlayer`
- **Perspective swapping** — temporarily making a bot appear as the local player so Udon code sees the correct `Networking.LocalPlayer`
- **Station interaction** — sitting bots in `VRCStation` objects through the real ClientSim event pipeline
- **Ownership enforcement** — validating `ForceKinematicOnRemote` behavior
- **Synced variable inspection** — reading/writing UdonBehaviour program variables

## Installation

Add to your VRChat project's `Packages/manifest.json`:

```json
"com.fire.vrcsim": "file:../../VRCPlayerSim"
```

Or copy the `Runtime/` folder into your project's `Assets/`.

## Quick Start

```csharp
// In Play Mode (via Editor script, script-execute, or MonoBehaviour)
var err = VRCSim.VRCSim.Init();
var alice = VRCSim.VRCSim.SpawnPlayer("Alice");

// Sit Alice in a station
var stationObj = GameObject.Find("MyStation");
VRCSim.VRCSim.SitInStation(alice, stationObj);

// Check synced vars
var val = VRCSim.VRCSim.GetVar(gameManagerObj, "playerCount");

// Run code from Alice's perspective
VRCSim.VRCSim.RunAsPlayer(alice, () => {
    // Networking.LocalPlayer == alice
    // alice.isLocal == true
    // Networking.IsMaster == false
});

// Cleanup
VRCSim.VRCSim.RemoveAllPlayers();
```

## API Reference

See **[API.md](API.md)** for the full, auto-generated API reference.

`API.md` is generated from the C# source code by `gen_api.py` and kept in sync automatically via a pre-commit hook. Never edit it manually.

## Architecture

```
VRCSim.cs          — Public API (Init, Spawn, Sit, RunAsPlayer, etc.)
SimNetwork.cs      — Perspective swapping, ownership, kinematic enforcement
SimReflection.cs   — Cached reflection into ClientSim private internals
SimSnapshot.cs     — Synced state capture and diffing
gen_api.py         — Auto-generates API.md from source
hooks/pre-commit   — Regenerates API.md on commit
```

All ClientSim access is via reflection (isolated in `SimReflection.cs`) so SDK version breaks are easy to diagnose — you get a clear "Required member not found: X" error.

## Contributing

After cloning, run this once to enable the pre-commit hook:

```sh
git config core.hooksPath hooks
```

This ensures `API.md` is automatically regenerated whenever you change `Runtime/*.cs` or `gen_api.py`. The hook requires Python 3.10+ (via `uv`, `python`, or `py` on PATH).

## Known Limitations

1. **Single-process simulation** — All players share one Unity instance. True network latency and packet loss cannot be simulated.
2. **Cached `_localPlayer` pattern** — UdonSharp scripts that cache `Networking.LocalPlayer` at `Start()` won't see perspective swaps for that cached reference. This is actually correct behavior (the cached ref always points to the real local player).
3. **ClientSim PlayerObjectStorage** — Rapid spawn/remove cycles can trigger file-lock errors in ClientSim's persistence layer. These are harmless but noisy.
4. **SetWalkSpeed/SetRunSpeed for remote players** — ClientSim may throw if these are called on non-local players during perspective swaps. The cached `_localPlayer` pattern in most UdonSharp code avoids this.

## Compatibility

- Unity 2022.3.x (VRChat-compatible)
- VRChat SDK 3.x (Worlds)
- ClientSim 1.x
- UdonSharp

## License

MIT
