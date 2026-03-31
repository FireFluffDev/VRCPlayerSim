# VRCSim

Multiplayer simulation for VRChat worlds. Spawn remote player bots, swap perspectives, test what Player 2 actually sees — all from the Unity editor, no friends required. 🥲

Built on [ClientSim](https://clientsim.docs.vrchat.com/). Where ClientSim gives you one local player, VRCSim gives you as many as you need.

Designed to be driven by both humans (Editor scripts) and **AI agents** via [Unity MCP](https://github.com/IvanMurzak/Unity-MCP).

## Install

### Git URL (recommended)

In Unity: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/deolson/VRCPlayerSim.git
```

Or add directly to `Packages/manifest.json`:

```json
"com.fire.vrcsim": "https://github.com/deolson/VRCPlayerSim.git"
```

### Requirements

- VRChat SDK 3.x (Worlds)
- ClientSim 1.x
- UdonSharp
- Unity 2022.3.x

These should already be in any VRChat world project.

## Usage

VRCSim runs in **play mode only**. Call it from Editor scripts, `[MenuItem]` methods, or Unity MCP's `script-execute`.

```csharp
// Initialize (once per play session)
var err = VRCSim.VRCSim.Init();

// Spawn remote players
var alice = VRCSim.VRCSim.SpawnPlayer("Alice");
var bob   = VRCSim.VRCSim.SpawnPlayer("Bob");

// Sit a player in a station
VRCSim.VRCSim.SitInStation(alice, GameObject.Find("MyStation"));

// Read / write synced variables
var phase = VRCSim.VRCSim.GetVar(gameManagerObj, "gamePhase");
VRCSim.VRCSim.SetVar(gameManagerObj, "gamePhase", 2);

// Test non-master perspective
VRCSim.VRCSim.RunAsClient(bob, () => {
    // Networking.LocalPlayer == bob
    // _localPlayer on all UdonBehaviours == bob
    // Master gates evaluate correctly
    VRCSim.VRCSim.RunUpdate(gameManagerObj);
});

// Snapshot and diff synced state
var before = VRCSim.VRCSim.TakeSnapshot();
VRCSim.VRCSim.RunUpdate(gameManagerObj);
var after = VRCSim.VRCSim.TakeSnapshot();
var diff = VRCSim.VRCSim.DiffSnapshots(before, after);

// Cleanup
VRCSim.VRCSim.RemoveAllPlayers();
```

### `RunAsPlayer` vs `RunAsClient`

| Method | `Networking.LocalPlayer` | Cached `_localPlayer` fields | Use for |
|--------|-------------------------|------------------------------|---------|
| `RunAsPlayer` | Swapped | **Unchanged** | Ownership checks |
| `RunAsClient` | Swapped | **Swapped on all UdonBehaviours** | Everything else |

Most UdonSharp code caches `_localPlayer = Networking.LocalPlayer` in `Start()`. `RunAsPlayer` doesn't touch those cached refs — your code still thinks it's the master. **Use `RunAsClient` for almost all multiplayer tests.**

## AI Agent Integration

VRCSim is built to be called by AI coding agents through [Unity MCP](https://github.com/IvanMurzak/Unity-MCP)'s `script-execute` tool. An agent writes C# test code, MCP compiles and runs it in play mode, and the agent reads back the results.

**Typical agent workflow:**

1. Agent reads `API.md` from this repo to learn available methods
2. Agent enters play mode via MCP (`editor-application-set-state`)
3. Agent sends a C# snippet via MCP (`script-execute`) that uses VRCSim to spawn bots, test scenarios, and assert results
4. Agent reads console output and state reports to verify behavior

**Example `script-execute` snippet** (what an agent would send to MCP):

```csharp
using UnityEngine;
using VRC.Udon;

public class Script {
    public static object Main() {
        var sb = new System.Text.StringBuilder();

        var err = VRCSim.VRCSim.Init();
        if (err != null) return $"INIT FAILED: {err}";

        var bot = VRCSim.VRCSim.SpawnPlayer("Tester");
        var gmObj = GameObject.Find("GameManager");
        var gm = (UdonBehaviour)VRCSim.SimReflection.GetUdonBehaviour(gmObj);

        VRCSim.VRCSim.RunAsClient(bot, () => {
            gm.RunEvent("_update");
            var phase = gm.GetProgramVariable("gamePhase");
            sb.AppendLine($"Non-master sees gamePhase: {phase}");
        });

        VRCSim.VRCSim.RemoveAllPlayers();
        return sb.ToString();
    }
}
```

The full API reference is auto-generated at **[API.md](API.md)** — agents should read this file before writing VRCSim code, rather than relying on hardcoded method lists.

## API

See **[API.md](API.md)** for the complete, auto-generated API reference.

Summary of `VRCSim.VRCSim` entry points:

| Category | Methods |
|----------|---------|
| Lifecycle | `Init`, `IsReady`, `SpawnPlayer`, `RemovePlayer`, `RemoveAllPlayers`, `GetBots`, `GetBot` |
| Movement | `Teleport` |
| Stations | `SitInStation`, `ExitStation`, `IsPlayerInStation` |
| Perspective | `RunAsPlayer`, `RunAsClient`, `RunAsClient<T>` |
| Ownership | `SetOwner`, `GetOwner`, `TransferMaster` |
| Variables | `GetVar`, `SetVar`, `SetVars`, `SendEvent`, `GetSyncedVarNames`, `GetSyncedVars` |
| Proxy Fields | `InitProxy`, `InitProxyDeep`, `SetField`, `GetField`, `SetFields` |
| Method Invoke | `Call`, `Call<T>` |
| Snapshots | `TakeSnapshot`, `DiffSnapshots` |
| Simulation | `RunUpdate`, `RunFixedUpdate`, `TickAll`, `TickFixedAll`, `RunEvent`, `RunEventWithArgs` |
| Sync | `SyncToAll`, `SimulateDeserialization`, `SimulateLateJoiner`, `SimulateLateJoinerAll` |
| Player Events | `SimulatePlayerLeft`, `SimulatePlayerJoined` |
| Networking | `SendNetworkEvent`, `EnforceKinematic`, `ValidateKinematic` |
| Reporting | `GetStateReport`, `ValidateVars`, `CheckVars` |

Lower-level access is available through `SimReflection` and `SimNetwork` — see API.md for details.

## Architecture

```
Runtime/
  VRCSim.cs           Public API facade (core: players, stations, perspective)
  VRCSim.Testing.cs   Public API facade (testing: validation, proxy, ticking)
  SimProxy.cs         C# proxy field access and method invocation for UdonSharp
  SimNetwork.cs       Perspective swapping, ownership, kinematic enforcement
  SimReflection.cs    Cached reflection into ClientSim internals
  SimSnapshot.cs      Synced state capture and diffing
```

All ClientSim access goes through reflection, isolated in `SimReflection.cs`. When a VRChat SDK update breaks an internal API, you get `Required member not found: X` at init time — not a null ref mid-test.

## Contributing

```sh
git clone https://github.com/deolson/VRCPlayerSim.git
cd VRCPlayerSim
pip install pre-commit    # or: uv tool install pre-commit
pre-commit install
```

To use your local clone in a VRChat project, add it by path in `Packages/manifest.json`:

```json
"com.fire.vrcsim": "file:../path/to/VRCPlayerSim"
```

The pre-commit hook auto-regenerates `API.md` from source when you change `Runtime/*.cs` or `gen_api.py`. CI checks this on PRs too. Requires [uv](https://docs.astral.sh/uv/) on PATH for the generator script.

## Roadmap

VRCSim is currently API-only. Planned additions:

- **Editor Window** — a Unity panel to spawn bots, manage stations, and inspect synced state without writing code.
- **Scene View gizmos** — visualize bot positions and ownership in the Scene view.

## Limitations

- **Single-process** — all players share one Unity instance. No network latency or packet loss.
- **Cached `_localPlayer`** — `RunAsClient` swaps these; `RunAsPlayer` does not. This matches real VRChat behavior.
- **`PlayerObjectStorage` file locks** — rapid spawn/remove cycles can trigger harmless ClientSim console errors.
- **Station occupancy on remote bots** — `VRCStation.UseStation()` requires ClientSim's local player pipeline. VRCSim wraps this via `RunAsPlayer` internally. Perspective swaps support nesting, so `SitInStation` works inside `RunAsClient` blocks.

## License

MIT
