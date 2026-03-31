# VRCSim API Reference

> **Auto-generated from source code.** Do not edit manually.
> Regenerate: `uv run gen_api.py` (or commit — pre-commit hook does it).

## `VRCSim.VRCSim`

VRC Player Simulator — public API. Simulates multiplayer interactions on top of ClientSim. Usage: VRCSim.Init(); var bot = VRCSim.SpawnPlayer("Alice"); VRCSim.SitInStation(bot, stationObj); VRCSim.RunAsPlayer(bot, () => { /* runs as Alice's client */ });

### Initialization

| Signature | Description |
|-----------|-------------|
| `string Init()` | Initialize the simulator. Call once per play mode session. Returns error string or null on success. |
| `bool IsReady` | True when Init() has succeeded and the simulator is operational. |

### Player Lifecycle

| Signature | Description |
|-----------|-------------|
| `VRCPlayerApi SpawnPlayer(string name)` | Spawn a remote player bot. Fires OnPlayerJoined on all UdonBehaviours. Returns the VRCPlayerApi for the new player. |
| `void RemovePlayer(VRCPlayerApi player)` | Remove a bot player. Fires OnPlayerLeft on all UdonBehaviours. If the removed player was master, auto-transfers master to the next player and fires _onNewMaster — matching real VRChat behavior. |
| `void RemoveAllPlayers(bool fireEvents = true)` | Remove all bots spawned in this session. By default fires OnPlayerLeft on all UdonBehaviours for each bot (matching real VRChat disconnect behavior). Pass fireEvents: false for fast teardown when you don't need event processing. |
| `List<VRCPlayerApi> GetBots()` | Get all bots. |
| `VRCPlayerApi GetBot(string name)` | Get a bot by exact display name. |
| `VRCPlayerApi GetBotByPrefix(string prefix)` | Get first bot whose name contains the given prefix (substring match). |

### Movement

| Signature | Description |
|-----------|-------------|
| `void Teleport(VRCPlayerApi player, Vector3 position, Quaternion? rotation = null)` | Teleport a player to a position. Works for both local and remote. |
| `void ApplyForce(GameObject obj, Vector3 force, ForceMode mode = ForceMode.Force)` | Apply a physics force to a GameObject's Rigidbody. Use this to simulate bumps, knockbacks, and other physics interactions that require continuous force rather than teleportation. Does nothing if the GameObject has no Rigidbody. |
| `void SetVelocity(GameObject obj, Vector3 velocity)` | Set the velocity of a GameObject's Rigidbody directly. Useful for simulating movement without acceleration curves. Does nothing if the GameObject has no Rigidbody. |
| `bool MoveToward(GameObject obj, Vector3 target, float speed = 5f)` | Move a GameObject toward a target position at a given speed. Sets velocity in the direction of the target. Returns true when the object is within 0.1 units of the target. Falls back to transform move if there is no Rigidbody. |
| `Vector3 GetVelocity(GameObject obj)` | Get the Rigidbody velocity of a GameObject. Returns Vector3.zero if there is no Rigidbody. |

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

### Persistent Possession

| Signature | Description |
|-----------|-------------|
| `void Possess(VRCPlayerApi bot)` | Persistently become a bot. After this call, Networking.LocalPlayer returns the bot, all UdonBehaviour _localPlayer fields point to the bot, and ClientSim's interaction pipeline (click stations, press buttons, walk around) acts as the bot. Unlike RunAsPlayer/RunAsClient (which are scoped to a callback), Possess stays active until you call Unpossess or Possess another bot. This is the API backing the Bot Controller EditorWindow's "Possess" button, but can also be called from test scripts. |
| `void Unpossess()` | Release possession — Networking.LocalPlayer returns Player_1 again. Safe to call even if not currently possessing (no-op). |
| `bool IsPossessing` | True if currently possessing a bot. |
| `VRCPlayerApi PossessedBot` | The currently possessed bot, or null. |

### Ownership

| Signature | Description |
|-----------|-------------|
| `void SetOwner(VRCPlayerApi player, GameObject obj)` | Transfer ownership of a GameObject to a player and enforce kinematic rules. |
| `VRCPlayerApi GetOwner(GameObject obj)` | Get the current owner of a GameObject. |

### Networking Rule Enforcement

| Signature | Description |
|-----------|-------------|
| `void EnforceKinematic(GameObject obj)` | Enforce VRChat's ForceKinematicOnRemote rule on a VRCObjectSync GameObject. |
| `List<SimNetwork.KinematicIssue> ValidateKinematic()` | Check all VRCObjectSync objects for incorrect kinematic state. Read-only. |
| `void SimulateDeserialization(GameObject obj)` | Fire OnDeserialization on a GameObject's UdonBehaviours. |
| `void SimulateLateJoiner(GameObject obj, VRCPlayerApi player = null)` | Simulate a late joiner receiving synced state on one object. |
| `void SimulateLateJoinerAll(VRCPlayerApi player = null)` | Simulate a late joiner on ALL synced objects in the scene. |
| `VRCPlayerApi SimulateLateJoin(string name = null)` | Simulate a full late join: spawn a new bot and fire OnDeserialization on ALL synced objects from the new player's perspective. This is what actually happens in VRChat when someone joins mid-game: they get a fresh client, connect, receive all current synced state via OnDeserialization, and then start running Update/FixedUpdate. Returns the newly spawned bot so you can possess or inspect it. |
| `void TransferMaster(VRCPlayerApi newMaster)` | Simulate master transfer. Changes master and fires _onNewMaster. |
| `bool SendNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, GameObject obj, string eventName)` | Simulate SendCustomNetworkEvent routing. Returns true if the event fired, false if skipped (e.g. Owner target but caller is not owner). |

### Udon Variable Access

| Signature | Description |
|-----------|-------------|
| `object GetVar(GameObject obj, string varName)` | Read an Udon program variable by name from the first UdonBehaviour on a GameObject. |
| `void SetVar(GameObject obj, string varName, object value)` | Write an Udon program variable by name. Writes to BOTH the Udon heap AND the C# proxy field so that VRCSim.Call() and GetVar() always see the same value. This matches how UdonSharp behaves in production, where both stores are always in sync. |
| `void SetVarHeapOnly(GameObject obj, string varName, object value)` | Write ONLY to the Udon heap, leaving the C# proxy field unchanged. Use this to simulate the gap between network data arriving and OnDeserialization firing — i.e. when testing the handler itself. Example: VRCSim.SetVarHeapOnly(obj, "gamePhase", 2); VRCSim.RunEvent(obj, "_onDeserialization"); // test the handler |
| `void SendEvent(GameObject obj, string eventName)` | Send a custom event to the first UdonBehaviour on a GameObject. |
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
| `void RunUpdate(GameObject obj, int frames)` | Tick multiple frames of the UdonBehaviour's Update loop. Equivalent to calling RunUpdate in a loop, but reads cleaner in tests. |

## `VRCSim.VRCSim`

### Validation & Reporting

| Signature | Description |
|-----------|-------------|
| `string GetStateReport()` | Build a human-readable report of all players, ownership, and kinematic issues. Useful as a last line in any test. |
| `string ValidateVars(GameObject obj, params (string varName, object expected)[] expectations)` | Validate expected synced var values on a GameObject. Returns a formatted pass/fail report string. |
| `bool CheckVars(GameObject obj, params (string varName, object expected)[] expectations)` | Validate expected synced var values. Returns true only if all expectations match. Cheaper than ValidateVars when you just need a pass/fail bool. |

### Batch Variable Access

| Signature | Description |
|-----------|-------------|
| `void SetVars(GameObject obj, params (string name, object value)[] vars)` | Set multiple Udon program variables on a single object in one call. Each write goes to BOTH heap and proxy (same as SetVar). Reduces boilerplate in test setup. |

### Physics Simulation

| Signature | Description |
|-----------|-------------|
| `void RunFixedUpdate(GameObject obj)` | Tick one frame of FixedUpdate on a specific object. Required for physics-dependent logic (ball elimination via Y-threshold, collision knockback, etc.). |
| `void RunFixedUpdate(GameObject obj, int frames)` | Tick multiple frames of FixedUpdate on a specific object. |

### Game Loop Simulation

| Signature | Description |
|-----------|-------------|
| `void TickAll(int frames = 1)` | Fire _update on ALL active UdonBehaviours in the scene. Simulates one full frame of the VRChat game loop. |
| `void TickFixedAll(int frames = 1)` | Fire _fixedUpdate on ALL active UdonBehaviours in the scene. Simulates one full physics tick of the VRChat game loop. |

### Sync Propagation

| Signature | Description |
|-----------|-------------|
| `void SyncToAll(GameObject obj)` | Simulate a full sync cycle: fire OnDeserialization from every non-owner player's perspective. In real VRChat, after RequestSerialization(), all non-owner clients receive the synced data and fire OnDeserialization. ClientSim shares a single Udon heap so values are already present — this just fires the event from each perspective. |

### Parameterized Events

| Signature | Description |
|-----------|-------------|
| `void RunEventWithArgs(GameObject obj, string eventName, params (string name, object value)[] args)` | Fire an Udon event with named arguments on a specific object. Mirrors how VRChat passes parameters to lifecycle events. Usage: VRCSim.RunEventWithArgs(gmObj, "_onPlayerLeft", ("player", bobApi)); |

### Station Query

| Signature | Description |
|-----------|-------------|
| `bool IsPlayerInStation(VRCPlayerApi player, GameObject stationObj)` | Check if a specific player is currently seated in a station. |

### Method Invocation

| Signature | Description |
|-----------|-------------|
| `object Call(GameObject obj, string methodName, params object[] args)` | Call any method (including private) on the UdonSharpBehaviour proxy found on a GameObject. Auto-discovers the proxy. Returns object; use CallAs for typed return values. |
| `object Call(Component behaviour, string methodName, params object[] args)` | Call any method (including private) on a Component directly. Use the GameObject overload unless you already have the component. |

### State Reading

| Signature | Description |
|-----------|-------------|
| `object GetField(GameObject obj, string fieldName)` | Read a C# proxy field from the UdonSharpBehaviour found on a GameObject. Use after Call() to assert on state the method changed. Use GetVar() for synced variables set by SetVar or deserialization. |
| `VarState GetBoth(GameObject obj, string varName)` | Read a variable from BOTH the Udon heap and the C# proxy in one call. Returns a VarState exposing .Heap, .Proxy, .InSync, .HeapAs, .ProxyAs. Example -- assert heap after RunEvent, then confirm both stores agree: VRCSim.RunEvent(gmObj, "_startGame"); var state = VRCSim.GetBoth(gmObj, "gamePhase"); Assert.AreEqual(1, state.HeapAs(0)); // VM ran, heap has truth Assert.IsTrue(state.InSync); // proxy should have caught up Example -- detect divergence after SetVarHeapOnly (intentional): VRCSim.SetVarHeapOnly(gmObj, "gamePhase", 2); Assert.IsFalse(VRCSim.GetBoth(gmObj, "gamePhase").InSync); |

### Player Events (without removal)

| Signature | Description |
|-----------|-------------|
| `void SimulatePlayerLeft(VRCPlayerApi player)` | Fire OnPlayerLeft on ALL UdonBehaviours for a player WITHOUT removing them from the player list. Use this to test game logic's DC response without destroying the player object. For full removal, use VRCSim.RemovePlayer instead. |
| `void SimulatePlayerJoined(VRCPlayerApi player)` | Fire OnPlayerJoined on ALL UdonBehaviours for a player. Useful for late-join scenarios or re-triggering join logic. |

## `VRCSim.SimProxy`

Proxy-level reflection for UdonSharpBehaviour C# objects. UdonSharp creates a dual state for every field: the C# proxy field and the Udon program heap variable. In production, UdonSharp keeps them in sync. In tests, they can diverge because: - VRCSim.SetVar writes to the Udon heap only - C# methods called via reflection read C# proxy fields - The proxy's Start() body doesn't run (Udon VM handles it) SimProxy bridges this gap with three capabilities: 1. InitProxy — sync Udon heap → C# proxy fields (replaces manual ForceInit) 2. Field access — read/write C# proxy fields with type coercion 3. Method invocation — call any method (including private) with caching

### General

| Signature | Description |
|-----------|-------------|
| `int InitProxy(Component behaviour)` | Initialize the C# proxy fields of an UdonSharpBehaviour by copying values from the Udon program heap. The Udon VM runs _start and populates the heap, but the C# proxy's Start() body never executes. This copies heap values into the corresponding C# fields so proxy methods work correctly in tests. Always sets _localPlayer = Networking.LocalPlayer (universal pattern). Returns the count of fields successfully synced. |
| `int InitProxyDeep(Component behaviour)` | Initialize proxy fields AND resolve common scene references that Start() typically sets via transform.Find / GetComponent / GetComponentInChildren. This handles the most common UdonSharp patterns: _localPlayer = Networking.LocalPlayer (always) field of type Transform → transform.Find(fieldNameWithout_) or GetComponentInChildren field of type Component → GetComponentInChildren(fieldType) field of type Renderer → GetComponentInChildren&lt;Renderer&gt;(true).transform Returns count of fields successfully initialized. |
| `void SetField(Component behaviour, string fieldName, object value, bool syncToHeap = true)` | Set a C# proxy field on an UdonSharpBehaviour. Handles type coercion (int→float, float→int, int→bool). Optionally syncs to Udon heap for consistency. |
| `object GetField(Component behaviour, string fieldName)` | Read a C# proxy field from an UdonSharpBehaviour. Returns null if the field doesn't exist. |
| `void SetFields(Component behaviour, params (string name, object value)[] fields)` | Set multiple fields at once. Reduces boilerplate in test setup. |
| `bool HasField(Component behaviour, string fieldName)` | Check if a field exists on the C# proxy type. |
| `object Call(Component behaviour, string methodName, params object[] args)` | Call a method (including private) on a Component. Searches declared methods first, then inherited. Caches MethodInfo per type for subsequent calls. |
| `bool HasMethod(Component behaviour, string methodName, int paramCount = 0)` | Check if a method exists on the type (including private). |
| `FieldInfo ResolveField(Type type, string name)` | Resolve a FieldInfo by name, searching the full type hierarchy. Results are cached per type. |
| `MethodInfo ResolveMethod(Type type, string methodName, object[] args)` | Resolve a MethodInfo by name and actual arguments. First tries exact parameter type match, then falls back to count-only match. |
| `MethodInfo ResolveMethod(Type type, string methodName, int paramCount)` | Backward-compatible overload for callers passing just a count. |

### Value coercion

| Signature | Description |
|-----------|-------------|
| `object CoerceValue(object value, Type targetType)` | Coerce a value to match the target field type. Handles the common UdonSharp mismatches: int → float, double → float, float → int, int → bool Returns null if coercion is not possible. |

## `VRCSim.SimProxy`

SimProxy GameObject overloads. Accept a GameObject instead of a Component, automatically finding the UdonSharpBehaviour C# proxy. Matches the pattern of VRCSim.GetVar/SetVar/RunEvent which also accept GameObjects.

### General

| Signature | Description |
|-----------|-------------|
| `Component FindProxy(GameObject obj)` | Find the UdonSharpBehaviour C# proxy component on a GameObject. Skips UdonBehaviour, Transform, and other Unity built-in components. Returns the first MonoBehaviour whose type inherits from UdonSharpBehaviour (detected by walking the type hierarchy — avoids hard dependency on UdonSharp assembly). Returns null if no proxy is found. |
| `Component FindProxyWithMethod(GameObject obj, string methodName, int paramCount = 0)` | Find the UdonSharpBehaviour proxy that has a specific method. Useful when a GameObject has multiple UdonSharp components. |
| `Component FindProxyWithField(GameObject obj, string fieldName)` | Find the UdonSharpBehaviour proxy that has a specific field. |

### GameObject convenience overloads

| Signature | Description |
|-----------|-------------|
| `object Call(GameObject obj, string methodName, params object[] args)` | Call a method on the UdonSharpBehaviour proxy found on a GameObject. Auto-discovers the proxy component — no manual GetComponent needed. |
| `void SetField(GameObject obj, string fieldName, object value, bool syncToHeap = true)` | Set a C# proxy field on the UdonSharpBehaviour found on a GameObject. |
| `object GetField(GameObject obj, string fieldName)` | Read a C# proxy field from the UdonSharpBehaviour found on a GameObject. |
| `int InitProxy(GameObject obj)` | Initialize the proxy on the UdonSharpBehaviour found on a GameObject. |
| `int InitProxyDeep(GameObject obj)` | InitProxy + deep scene reference resolution on a GameObject. |

## `VRCSim.SimNetwork`

Simulates VRChat networking rules that ClientSim skips: - Perspective swapping (run code as non-master) - ForceKinematicOnRemote enforcement - Ownership tracking - Deserialization simulation

### Perspective Swap State

| Signature | Description |
|-----------|-------------|
| `void RunAsPlayer(VRCPlayerApi player, Action action)` | Run an action from the perspective of a specific player. While inside this block: - Networking.LocalPlayer returns the specified player - Networking.IsMaster returns true ONLY if this player is actually master - player.isLocal returns true for the specified player Supports nesting — SitInStation can be called inside RunAsClient. Each level saves and restores its own state. |
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
| `void TransferOwnership(VRCPlayerApi newOwner, GameObject obj)` | Transfer ownership with full VRChat event flow: 1. Fires OnOwnershipRequest on the UdonBehaviour (if it returns false, transfer is denied). 2. Calls Networking.SetOwner to change ownership. 3. Fires OnOwnershipTransferred on the UdonBehaviour. 4. Enforces ForceKinematicOnRemote rules. |
| `bool ValidateOwnerWrite(GameObject obj, string varName)` | Validate that a synced variable was written by the owner. In real VRChat, non-owner writes to synced vars are local-only and get overwritten on next deserialization. |

### Master Transfer

| Signature | Description |
|-----------|-------------|
| `void SimulateMasterTransfer(VRCPlayerApi newMaster)` | Simulate master transfer to a new player. Changes the master ID and fires _onNewMaster on all UdonBehaviours. |

### Sync Propagation

| Signature | Description |
|-----------|-------------|
| `void SyncToAllClients(GameObject obj)` | Simulate a full sync cycle: fire OnDeserialization from every non-owner player's perspective. In real VRChat, after the owner calls RequestSerialization(), all other clients receive the synced data and fire OnDeserialization. This replicates that. ClientSim uses a single shared Udon heap, so the synced var values are already "propagated" — we just need to fire the OnDeserialization event from each non-owner's perspective. |
| `void FirePlayerLeftOnAll(VRCPlayerApi player)` | Fire _onPlayerLeft on ALL UdonBehaviours in the scene for a specific player. Does NOT remove the player from the player list. Use this to test game logic's response to disconnection without destroying the player object. For full removal (with player list cleanup), use VRCSim.RemovePlayer instead. |
| `void FirePlayerJoinedOnAll(VRCPlayerApi player)` | Fire _onPlayerJoined on ALL UdonBehaviours for a specific player. Normally handled by ClientSim during SpawnRemotePlayer, but useful for testing late-join scenarios or re-triggering join logic. |

### Network Event Routing

| Signature | Description |
|-----------|-------------|
| `bool SimulateNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, GameObject obj, string eventName)` | Simulate SendCustomNetworkEvent routing. All: fires event on the UdonBehaviour (all clients see same instance). Owner: only fires if the current perspective is the owner. |

### Types

**`KinematicIssue`** (struct)

| Field | Type |
|-------|------|
| `ObjectName` | `string` |
| `ObjectPath` | `string` |
| `OwnerId` | `int` |
| `NonOwnerPlayerId` | `int` |
| `IsKinematic` | `bool` |
| `ShouldBeKinematic` | `bool` |

## `VRCSim.SimReflection`

Cached reflection accessors for ClientSim internals. Isolated here so SDK version breaks are easy to diagnose and fix.

### VRCPlayerApi

| Signature | Description |
|-----------|-------------|
| `bool IsReady` | True when reflection initialization completed without errors. |
| `string InitError` | Error message from initialization, or null on success. |
| `bool Initialize()` | Resolve all reflection targets. Returns true on success. |
| `void Reset()` | Reset init state so Init() can be called again. |
| `bool GetIsLocal(VRCPlayerApi player)` | Read the isLocal field on a VRCPlayerApi. |
| `void SetIsLocal(VRCPlayerApi player, bool value)` | Write the isLocal field on a VRCPlayerApi. |

### Public Accessors

| Signature | Description |
|-----------|-------------|
| `void SpawnRemotePlayer(string name)` | Call ClientSimMain.SpawnRemotePlayer to create a new bot. |
| `void RemovePlayer(VRCPlayerApi player)` | Call ClientSimMain.RemovePlayer to destroy a bot. |
| `object GetPlayerManager()` | Get the ClientSimPlayerManager instance via reflection. |
| `object GetClientSimInstance()` | Get the ClientSimMain singleton instance. |

### PlayerManager State

| Signature | Description |
|-----------|-------------|
| `int GetMasterId(object pm)` | Read the master player ID from the PlayerManager. |
| `void SetMasterId(object pm, int id)` | Write the master player ID on the PlayerManager. |
| `int GetLocalPlayerId(object pm)` | Read the local player ID from the PlayerManager. |
| `void SetLocalPlayerId(object pm, int id)` | Write the local player ID on the PlayerManager. |
| `VRCPlayerApi GetLocalPlayer(object pm)` | Read the local player reference from the PlayerManager. |
| `void SetLocalPlayer(object pm, VRCPlayerApi player)` | Write the local player reference on the PlayerManager. |

### Station Helper

| Signature | Description |
|-----------|-------------|
| `VRCPlayerApi GetStationUser(Component helper)` | Read the _usingPlayer field from a ClientSimStationHelper. |
| `void SetStationUser(Component helper, VRCPlayerApi player)` | Write the _usingPlayer field on a ClientSimStationHelper. |
| `Component GetStationHelper(GameObject obj)` | Get the ClientSimStationHelper component on a station GameObject. |
| `void FireStationEnterHandlers(GameObject stationObj, VRCStation station)` | Invoke OnStationEnter on all IClientSimStationHandler components. |
| `void FireStationExitHandlers(GameObject stationObj, VRCStation station)` | Invoke OnStationExit on all IClientSimStationHandler components. |

### UdonBehaviour

| Signature | Description |
|-----------|-------------|
| `Type UdonBehaviourType` | The resolved System.Type for VRC.Udon.UdonBehaviour. |
| `object GetProgramVariable(Component udon, string name)` | Read an Udon program variable. Throws if the variable doesn't exist. |
| `bool TryGetProgramVariable(Component udon, string name, out object value)` | Check if variable exists and get its value without logging errors. Returns true if the variable exists, false otherwise. |
| `void SetProgramVariable(Component udon, string name, object value)` | Write an Udon program variable by name. |
| `void SendCustomEvent(Component udon, string eventName)` | Send a custom event to an UdonBehaviour (calls the named method). |
| `Component[] GetUdonBehaviours(GameObject obj)` | Get all UdonBehaviour components on a GameObject. |
| `Component GetUdonBehaviour(GameObject obj)` | Get the first UdonBehaviour component on a GameObject. |
| `void RunEvent(Component udon, string eventName)` | Execute an Udon program event (e.g. "_update", "_onDeserialization"). Unlike SendCustomEvent, this goes through the program runner. |
| `void RunEventWithArgs(Component udon, string eventName, params (string, object)[] args)` | Execute an Udon program event with named arguments. Mirrors how VRChat passes parameters to lifecycle events: _onPlayerLeft → ("player", VRCPlayerApi) _onPlayerJoined → ("player", VRCPlayerApi) _onStationEntered → ("player", VRCPlayerApi) Falls back to SetProgramVariable + RunEvent if the with-args overload is unavailable (older UdonBehaviour builds). |
| `List<string> GetSyncedVarNames(Component udon)` | Get the names of all [UdonSynced] variables on an UdonBehaviour. Returns empty list if sync metadata is unavailable. |
| `Component[] FindAllUdonBehaviours()` | Find ALL UdonBehaviours in the scene (active only). |
| `bool FireOwnershipRequest(Component udon, VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)` | Fire OnOwnershipRequest on an UdonBehaviour. Returns true if transfer is allowed (or if the event doesn't exist). Uses Udon's standard event parameter symbols: onOwnershipRequestRequester, onOwnershipRequestNewOwner. |
| `void FireOwnershipTransferred(Component udon, VRCPlayerApi newOwner)` | Fire OnOwnershipTransferred on an UdonBehaviour. In VRChat, this fires after ownership has changed and the new owner is passed as the "player" parameter. |

### Scene Helpers

| Signature | Description |
|-----------|-------------|
| `string GetPath(Transform t)` | Build the full hierarchy path of a Transform. Shared by SimNetwork and SimSnapshot — lives here to avoid duplication. |

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

### Types

**`ChangeType`** (enum)

Values: `Added`, `Modified`, `Removed`

**`SyncChange`** (struct)

| Field | Type |
|-------|------|
| `ObjectPath` | `string` |
| `VarName` | `string` |
| `Before` | `object` |
| `After` | `object` |
| `Type` | `ChangeType` |
