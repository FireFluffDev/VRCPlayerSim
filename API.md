# VRCSim API Reference

> **Auto-generated from source code.** Do not edit manually.
> Regenerate: `uv run gen_api.py` (or commit — pre-commit hook does it).

## `VRCSim.VRCSim`

VRC Player Simulator — public API. Simulates multiplayer interactions on top of ClientSim. Usage: VRCSim.Init(); var bot = VRCSim.SpawnPlayer("Alice"); VRCSim.SitInStation(bot, stationObj); VRCSim.RunAsPlayer(bot, () => { /* runs as Alice's client */ });

### Initialization

| Signature | Description |
|-----------|-------------|
| `string Init()` | Initialize the simulator. Call once per play mode session. Returns error string or null on success. |
| `bool IsReady` | — |

### Player Lifecycle

| Signature | Description |
|-----------|-------------|
| `VRCPlayerApi SpawnPlayer(string name)` | Spawn a remote player bot. Fires OnPlayerJoined on all UdonBehaviours. Returns the VRCPlayerApi for the new player. |
| `void RemovePlayer(VRCPlayerApi player)` | Remove a bot player. Fires OnPlayerLeft on all UdonBehaviours. |
| `void RemoveAllPlayers()` | Remove all bots spawned in this session. |
| `List<VRCPlayerApi> GetBots()` | Get all bots. |
| `VRCPlayerApi GetBot(string name)` | Get a bot by name (partial match). |

### Movement

| Signature | Description |
|-----------|-------------|
| `void Teleport(VRCPlayerApi player, Vector3 position, Quaternion? rotation = null)` | Teleport a player to a position. Works for both local and remote. |

### Station Interaction

| Signature | Description |
|-----------|-------------|
| `bool SitInStation(VRCPlayerApi player, GameObject stationObj)` | Force a player to sit in a VRCStation. Fires events through the REAL ClientSim pipeline: IClientSimStationHandler.OnStationEnter → ClientSimUdonHelper → UdonBehaviour.RunEvent("_onStationEntered", ("Player", ...)) By wrapping in RunAsPlayer, Networking.LocalPlayer returns the correct player, so Udon code sees the right perspective. |
| `bool ExitStation(VRCPlayerApi player, GameObject stationObj)` | Force a player to exit a VRCStation. |

### Perspective Simulation

| Signature | Description |
|-----------|-------------|
| `void RunAsPlayer(VRCPlayerApi player, Action action)` | Run code from a specific player's perspective. Inside this block: - Networking.LocalPlayer returns the specified player - Networking.IsMaster returns false (if player isn't master) - player.isLocal returns true NOTE: Does NOT swap cached _localPlayer on UdonBehaviours. Use RunAsClient for full client simulation. |
| `void RunAsClient(VRCPlayerApi player, Action action)` | Simulate running code as a specific player's VRChat client. Unlike RunAsPlayer, this also swaps the cached _localPlayer field on ALL scene UdonBehaviours that have one. This makes master-gated code (e.g. if (!_localPlayer.isMaster) return) behave correctly from the target player's perspective. Usage: VRCSim.RunAsClient(bob, () => { gm.RunEvent("_update"); // Bob's Update -- non-master gate fires gm.SendCustomEvent("OnDeserialization"); // Bob receives sync }); |

### Ownership

| Signature | Description |
|-----------|-------------|
| `void SetOwner(VRCPlayerApi player, GameObject obj)` | — |
| `VRCPlayerApi GetOwner(GameObject obj)` | — |

### Networking Rule Enforcement

| Signature | Description |
|-----------|-------------|
| `void EnforceKinematic(GameObject obj)` | — |
| `List<SimNetwork.KinematicIssue> ValidateKinematic()` | — |
| `void SimulateDeserialization(GameObject obj)` | — |
| `void SimulateLateJoiner(GameObject obj, VRCPlayerApi player = null)` | — |
| `void SimulateLateJoinerAll(VRCPlayerApi player = null)` | Simulate a late joiner on ALL synced objects in the scene. |
| `void TransferMaster(VRCPlayerApi newMaster)` | Simulate master transfer. Changes master and fires _onNewMaster. |
| `bool SendNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, GameObject obj, string eventName)` | Simulate SendCustomNetworkEvent routing. Returns true if the event fired, false if skipped (e.g. Owner target but caller is not owner). |

### Udon Variable Access

| Signature | Description |
|-----------|-------------|
| `object GetVar(GameObject obj, string varName)` | — |
| `void SetVar(GameObject obj, string varName, object value)` | — |
| `void SendEvent(GameObject obj, string eventName)` | — |
| `List<string> GetSyncedVarNames(GameObject obj)` | Get the names of all [UdonSynced] variables on a GameObject. |
| `Dictionary<string, object> GetSyncedVars(GameObject obj)` | Get all synced variables and their current values. |

### Snapshots

| Signature | Description |
|-----------|-------------|
| `SimSnapshot TakeSnapshot()` | Capture the current synced state of all UdonBehaviours. |
| `SimSnapshot TakeSnapshot(GameObject obj)` | Capture synced state for a single GameObject. |
| `List<SimSnapshot.SyncChange> DiffSnapshots(SimSnapshot before, SimSnapshot after)` | Diff two snapshots and return the changes. |
| `void RunEvent(GameObject obj, string eventName)` | Run an Udon program event through the UdonBehaviour program. Events execute through program variable storage, NOT MonoBehaviour fields. Critical: station events write to program heap, so game logic methods must be called via RunEvent to see the correct state. |
| `void RunUpdate(GameObject obj)` | Tick one frame of the UdonBehaviour's Update loop. |

### Validation & Reporting

| Signature | Description |
|-----------|-------------|
| `string GetStateReport()` | — |
| `string ValidateVars(GameObject obj, params (string varName, object expected)[] expectations)` | Validate expected synced var values on a GameObject. |

## `VRCSim.SimNetwork`

Simulates VRChat networking rules that ClientSim skips: - Perspective swapping (run code as non-master) - ForceKinematicOnRemote enforcement - Ownership tracking - Deserialization simulation

### Perspective Swap State

| Signature | Description |
|-----------|-------------|
| `void RunAsPlayer(VRCPlayerApi player, Action action)` | Run an action from the perspective of a specific player. While inside this block: - Networking.LocalPlayer returns the specified player - Networking.IsMaster returns true ONLY if this player is actually master - player.isLocal returns true for the specified player |
| `bool InPerspectiveSwap` | Returns true if we're currently inside a RunAsPlayer block. |

### ForceKinematicOnRemote

| Signature | Description |
|-----------|-------------|
| `void EnforceKinematicOnRemote(GameObject obj)` | Enforce ForceKinematicOnRemote on a GameObject with VRCObjectSync. Non-owners get kinematic=true, owners keep their coded state. Call this after ownership changes to simulate real VRChat behavior. |
| `List<KinematicIssue> ValidateKinematicState()` | Check all VRCObjectSync objects in the scene and report which ones have incorrect kinematic state for non-owners. Does NOT modify anything — read-only validation. |

### Deserialization Simulation

| Signature | Description |
|-----------|-------------|
| `void SimulateDeserialization(GameObject obj)` | Simulate what happens when a non-master receives synced data. Fires OnDeserialization on the UdonBehaviour. |
| `void SimulateLateJoiner(GameObject obj, VRCPlayerApi player = null)` | Simulate a late joiner receiving synced state on a specific object. Fires OnDeserialization from the given player's perspective. If player is null, fires without perspective swap (local player view). |
| `void SimulateLateJoinerAll(VRCPlayerApi player = null)` | Simulate a late joiner on ALL synced UdonBehaviours in the scene. |

### Ownership Helpers

| Signature | Description |
|-----------|-------------|
| `void TransferOwnership(VRCPlayerApi newOwner, GameObject obj)` | Transfer ownership and enforce kinematic rules. This is what should happen in real VRChat but ClientSim skips the kinematic part. |
| `bool ValidateOwnerWrite(GameObject obj, string varName)` | Validate that a synced variable was written by the owner. In real VRChat, non-owner writes to synced vars are local-only and get overwritten on next deserialization. |

### Master Transfer

| Signature | Description |
|-----------|-------------|
| `void SimulateMasterTransfer(VRCPlayerApi newMaster)` | Simulate master transfer to a new player. Changes the master ID and fires _onNewMaster on all UdonBehaviours. |

### Network Event Routing

| Signature | Description |
|-----------|-------------|
| `bool SimulateNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, GameObject obj, string eventName)` | Simulate SendCustomNetworkEvent routing. All: fires event on the UdonBehaviour (all clients see same instance). Owner: only fires if the current perspective is the owner. |

## `VRCSim.SimReflection`

Cached reflection accessors for ClientSim internals. Isolated here so SDK version breaks are easy to diagnose and fix.

### VRCPlayerApi

| Signature | Description |
|-----------|-------------|
| `bool IsReady` | — |
| `string InitError` | — |
| `bool Initialize()` | — |
| `void Reset()` | Reset init state so Init() can be called again. |
| `bool GetIsLocal(VRCPlayerApi player)` | — |
| `void SetIsLocal(VRCPlayerApi player, bool value)` | — |

### Public Accessors

| Signature | Description |
|-----------|-------------|
| `void SpawnRemotePlayer(string name)` | — |
| `void RemovePlayer(VRCPlayerApi player)` | — |
| `object GetPlayerManager()` | — |
| `object GetClientSimInstance()` | — |

### PlayerManager State

| Signature | Description |
|-----------|-------------|
| `int GetMasterId(object pm)` | — |
| `void SetMasterId(object pm, int id)` | — |
| `int GetLocalPlayerId(object pm)` | — |
| `void SetLocalPlayerId(object pm, int id)` | — |
| `VRCPlayerApi GetLocalPlayer(object pm)` | — |
| `void SetLocalPlayer(object pm, VRCPlayerApi player)` | — |

### Station Helper

| Signature | Description |
|-----------|-------------|
| `VRCPlayerApi GetStationUser(Component helper)` | — |
| `void SetStationUser(Component helper, VRCPlayerApi player)` | — |
| `Component GetStationHelper(GameObject obj)` | — |
| `void FireStationEnterHandlers(GameObject stationObj, VRCStation station)` | — |
| `void FireStationExitHandlers(GameObject stationObj, VRCStation station)` | — |

### UdonBehaviour

| Signature | Description |
|-----------|-------------|
| `Type UdonBehaviourType` | — |
| `object GetProgramVariable(Component udon, string name)` | — |
| `bool TryGetProgramVariable(Component udon, string name, out object value)` | Check if variable exists and get its value without logging errors. Returns true if the variable exists, false otherwise. |
| `void SetProgramVariable(Component udon, string name, object value)` | — |
| `void SendCustomEvent(Component udon, string eventName)` | — |
| `Component[] GetUdonBehaviours(GameObject obj)` | — |
| `Component GetUdonBehaviour(GameObject obj)` | — |
| `void RunEvent(Component udon, string eventName)` | Execute an Udon program event (e.g. "_update", "_onDeserialization"). Unlike SendCustomEvent, this goes through the program runner. |
| `List<string> GetSyncedVarNames(Component udon)` | Get the names of all [UdonSynced] variables on an UdonBehaviour. Returns empty list if sync metadata is unavailable. |
| `Component[] FindAllUdonBehaviours()` | Find ALL UdonBehaviours in the scene (active only). |

## `VRCSim.SimSnapshot`

Captures and diffs the synced state of all UdonBehaviours in the scene. Used to verify what changed after an action (sit, phase transition, etc).

### Factory

| Signature | Description |
|-----------|-------------|
| `SimSnapshot Take()` | Capture the current synced state of all UdonBehaviours in the scene. Only includes UdonBehaviours that have at least one [UdonSynced] variable. |
| `SimSnapshot TakeFor(GameObject obj)` | Capture synced state for a single GameObject only. |

### Diff

| Signature | Description |
|-----------|-------------|
| `List<SyncChange> Diff(SimSnapshot before, SimSnapshot after)` | Compare two snapshots and return a list of changes. |

### Display

| Signature | Description |
|-----------|-------------|
| `string FormatDiff(List<SyncChange> changes)` | Format a diff as a readable string. |
