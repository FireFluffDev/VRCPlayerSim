using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    /// <summary>
    /// VRC Player Simulator — public API.
    /// Simulates multiplayer interactions on top of ClientSim.
    ///
    /// Usage:
    ///   VRCSim.Init();
    ///   var bot = VRCSim.SpawnPlayer("Alice");
    ///   VRCSim.SitInStation(bot, stationObj);
    ///   VRCSim.RunAsPlayer(bot, () => { /* runs as Alice's client */ });
    /// </summary>
    public static partial class VRCSim
    {
        private static readonly List<VRCPlayerApi> _bots = new();
        private static bool _ready;

        // Reset static state when entering play mode to avoid stale refs
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnPlayModeEnter()
        {
            _ready = false;
            _bots.Clear();
            SimReflection.Reset();
        }

        // ── Initialization ─────────────────────────────────────────

        /// <summary>
        /// Initialize the simulator. Call once per play mode session.
        /// Returns error string or null on success.
        /// </summary>
        public static string Init()
        {
            if (_ready) return null;

            if (!SimReflection.Initialize())
                return SimReflection.InitError;

            _bots.Clear();
            _ready = true;
            Debug.Log("[VRCSim] Simulator initialized");
            return null;
        }

        /// <summary>True when Init() has succeeded and the simulator is operational.</summary>
        public static bool IsReady => _ready && SimReflection.IsReady;

        // ── Player Lifecycle ───────────────────────────────────────

        /// <summary>
        /// Spawn a remote player bot. Fires OnPlayerJoined on all UdonBehaviours.
        /// Returns the VRCPlayerApi for the new player.
        /// </summary>
        public static VRCPlayerApi SpawnPlayer(string name)
        {
            EnsureReady();

            int countBefore = VRCPlayerApi.AllPlayers.Count;
            SimReflection.SpawnRemotePlayer(name);

            if (VRCPlayerApi.AllPlayers.Count <= countBefore)
            {
                Debug.LogError("[VRCSim] SpawnPlayer failed — player count unchanged");
                return null;
            }

            // Find the player that was not there before (do not assume append order)
            VRCPlayerApi newPlayer = null;
            int highestId = int.MinValue;
            foreach (var p in VRCPlayerApi.AllPlayers)
            {
                if (p.playerId > highestId)
                {
                    highestId = p.playerId;
                    newPlayer = p;
                }
            }
            _bots.Add(newPlayer);

            Debug.Log($"[VRCSim] Spawned: {newPlayer.displayName} (id={newPlayer.playerId})");
            return newPlayer;
        }

        /// <summary>
        /// Remove a bot player. Fires OnPlayerLeft on all UdonBehaviours.
        /// If the removed player was master, auto-transfers master to the
        /// next player and fires _onNewMaster — matching real VRChat behavior.
        /// </summary>
        public static void RemovePlayer(VRCPlayerApi player)
        {
            EnsureReady();
            bool wasMaster = player.isMaster;
            int removedId = player.playerId;
            _bots.Remove(player);
            ClearStationsForPlayer(player);
            SimReflection.RemovePlayer(player);
            Debug.Log($"[VRCSim] Removed: {player.displayName}");

            if (wasMaster && VRCPlayerApi.AllPlayers.Count > 0)
            {
                // VRChat assigns master to the lowest-ID remaining player
                VRCPlayerApi newMaster = null;
                int lowestId = int.MaxValue;
                foreach (var p in VRCPlayerApi.AllPlayers)
                {
                    if (p.playerId < lowestId)
                    {
                        lowestId = p.playerId;
                        newMaster = p;
                    }
                }
                if (newMaster != null)
                    SimNetwork.SimulateMasterTransfer(newMaster);
            }
        }

        /// <summary>
        /// Remove all bots spawned in this session.
        /// Note: Does NOT fire OnPlayerLeft events. Use RemovePlayer() in a loop
        /// if you need disconnect event testing.
        /// </summary>
        public static void RemoveAllPlayers()
        {
            EnsureReady();
            foreach (var bot in new List<VRCPlayerApi>(_bots))
            {
                if (bot != null && bot.IsValid())
                {
                    ClearStationsForPlayer(bot);
                    SimReflection.RemovePlayer(bot);
                }
            }
            _bots.Clear();
            Debug.Log("[VRCSim] All bots removed");
        }

        /// <summary>Get all bots.</summary>
        public static List<VRCPlayerApi> GetBots() => new(_bots);

        /// <summary>Get a bot by exact display name.</summary>
        public static VRCPlayerApi GetBot(string name)
        {
            foreach (var bot in _bots)
                if (bot != null && bot.IsValid() && bot.displayName == name)
                    return bot;
            return null;
        }

        /// <summary>Get first bot whose name contains the given prefix (substring match).</summary>
        public static VRCPlayerApi GetBotByPrefix(string prefix)
        {
            foreach (var bot in _bots)
                if (bot != null && bot.IsValid() && bot.displayName.Contains(prefix))
                    return bot;
            return null;
        }

        // ── Movement ───────────────────────────────────────────────

        /// <summary>
        /// Teleport a player to a position. Works for both local and remote.
        /// </summary>
        public static void Teleport(VRCPlayerApi player, Vector3 position,
            Quaternion? rotation = null)
        {
            EnsureReady();
            if (player?.gameObject == null) return;
            player.gameObject.transform.position = position;
            if (rotation.HasValue)
                player.gameObject.transform.rotation = rotation.Value;
        }

        // ── Station Interaction ────────────────────────────────────

        /// <summary>
        /// Force a player to sit in a VRCStation.
        ///
        /// Fires events through the REAL ClientSim pipeline:
        ///   IClientSimStationHandler.OnStationEnter → ClientSimUdonHelper
        ///   → UdonBehaviour.RunEvent("_onStationEntered", ("Player", ...))
        ///
        /// By wrapping in RunAsPlayer, Networking.LocalPlayer returns the
        /// correct player, so Udon code sees the right perspective.
        /// </summary>
        public static bool SitInStation(VRCPlayerApi player, GameObject stationObj)
        {
            EnsureReady();

            var station = stationObj.GetComponent<VRC.SDK3.Components.VRCStation>();
            if (station == null)
            {
                Debug.LogError($"[VRCSim] No VRCStation on {stationObj.name}");
                return false;
            }

            var helper = SimReflection.GetStationHelper(stationObj);
            if (helper == null)
            {
                Debug.LogError($"[VRCSim] No StationHelper on {stationObj.name}");
                return false;
            }

            // Clear stale user references (from removed players)
            var currentUser = SimReflection.GetStationUser(helper);
            if (currentUser != null)
            {
                if (!currentUser.IsValid())
                {
                    SimReflection.SetStationUser(helper, null);
                }
                else
                {
                    Debug.LogWarning(
                        $"[VRCSim] {stationObj.name} occupied by {currentUser.displayName}");
                    return false;
                }
            }

            // Set _usingPlayer so IsOccupied() returns true
            SimReflection.SetStationUser(helper, player);

            // Move player to station position
            var enterLoc = station.stationEnterPlayerLocation;
            if (enterLoc != null)
                Teleport(player, enterLoc.position, enterLoc.rotation);

            // Fire events through the real pipeline as the sitting player
            SimNetwork.RunAsPlayer(player, () =>
            {
                SimReflection.FireStationEnterHandlers(stationObj, station);
            });

            Debug.Log($"[VRCSim] {player.displayName} sat in {stationObj.name}");
            return true;
        }

        /// <summary>
        /// Force a player to exit a VRCStation.
        /// </summary>
        public static bool ExitStation(VRCPlayerApi player, GameObject stationObj)
        {
            EnsureReady();

            var helper = SimReflection.GetStationHelper(stationObj);
            if (helper == null) return false;

            var currentUser = SimReflection.GetStationUser(helper);
            if (currentUser == null)
            {
                Debug.LogWarning($"[VRCSim] {stationObj.name} is empty, can't exit");
                return false;
            }

            // Allow exit if the user is invalid (removed) or matches the player
            if (currentUser.IsValid() && currentUser.playerId != player.playerId)
            {
                Debug.LogWarning(
                    $"[VRCSim] {player.displayName} not in {stationObj.name}" +
                    $" (occupied by {currentUser.displayName})");
                return false;
            }

            var station = stationObj.GetComponent<VRC.SDK3.Components.VRCStation>();

            SimReflection.SetStationUser(helper, null);

            SimNetwork.RunAsPlayer(player, () =>
            {
                SimReflection.FireStationExitHandlers(stationObj, station);
            });

            Debug.Log($"[VRCSim] {player.displayName} exited {stationObj.name}");
            return true;
        }

        // ── Perspective Simulation ─────────────────────────────────

        /// <summary>
        /// Run code from a specific player's perspective.
        /// Inside this block:
        ///   - Networking.LocalPlayer returns the specified player
        ///   - Networking.IsMaster returns false (if player isn't master)
        ///   - player.isLocal returns true
        ///
        /// NOTE: Does NOT swap cached _localPlayer on UdonBehaviours.
        /// Use RunAsClient for full client simulation.
        /// </summary>
        public static void RunAsPlayer(VRCPlayerApi player, Action action) =>
            SimNetwork.RunAsPlayer(player, action);

        /// <summary>
        /// Simulate running code as a specific player's VRChat client.
        /// Unlike RunAsPlayer, this also swaps the cached _localPlayer
        /// field on ALL scene UdonBehaviours that have one. This makes
        /// master-gated code (e.g. if (!_localPlayer.isMaster) return)
        /// behave correctly from the target player's perspective.
        ///
        /// Usage:
        ///   VRCSim.RunAsClient(bob, () => {
        ///       gm.RunEvent("_update");           // Bob's Update -- non-master gate fires
        ///       gm.SendCustomEvent("OnDeserialization");  // Bob receives sync
        ///   });
        /// </summary>
        public static void RunAsClient(VRCPlayerApi player, Action action)
        {
            EnsureReady();
            var swapped = SwapLocalPlayerRefs(player);
            try
            {
                SimNetwork.RunAsPlayer(player, action);
            }
            finally
            {
                RestoreLocalPlayerRefs(swapped);
            }
        }

        /// <summary>
        /// RunAsClient with a return value.
        /// var phase = VRCSim.RunAsClient(bob, () => VRCSim.GetVar(gm, "gamePhase"));
        /// </summary>
        public static T RunAsClient<T>(VRCPlayerApi player, Func<T> func)
        {
            EnsureReady();
            var swapped = SwapLocalPlayerRefs(player);
            try
            {
                T result = default;
                SimNetwork.RunAsPlayer(player, () => { result = func(); });
                return result;
            }
            finally
            {
                RestoreLocalPlayerRefs(swapped);
            }
        }

        // ── Ownership ──────────────────────────────────────────────

        /// <summary>Transfer ownership of a GameObject to a player and enforce kinematic rules.</summary>
        public static void SetOwner(VRCPlayerApi player, GameObject obj) =>
            SimNetwork.TransferOwnership(player, obj);

        /// <summary>Get the current owner of a GameObject.</summary>
        public static VRCPlayerApi GetOwner(GameObject obj) =>
            Networking.GetOwner(obj);

        // ── Networking Rule Enforcement ────────────────────────────

        /// <summary>Enforce VRChat's ForceKinematicOnRemote rule on a VRCObjectSync GameObject.</summary>
        public static void EnforceKinematic(GameObject obj) =>
            SimNetwork.EnforceKinematicOnRemote(obj);

        /// <summary>Check all VRCObjectSync objects for incorrect kinematic state. Read-only.</summary>
        public static List<SimNetwork.KinematicIssue> ValidateKinematic() =>
            SimNetwork.ValidateKinematicState();

        /// <summary>Fire OnDeserialization on a GameObject's UdonBehaviours.</summary>
        public static void SimulateDeserialization(GameObject obj) =>
            SimNetwork.SimulateDeserialization(obj);

        /// <summary>Simulate a late joiner receiving synced state on one object.</summary>
        public static void SimulateLateJoiner(GameObject obj,
            VRCPlayerApi player = null) =>
            SimNetwork.SimulateLateJoiner(obj, player);

        /// <summary>
        /// Simulate a late joiner on ALL synced objects in the scene.
        /// </summary>
        public static void SimulateLateJoinerAll(VRCPlayerApi player = null) =>
            SimNetwork.SimulateLateJoinerAll(player);

        /// <summary>
        /// Simulate master transfer. Changes master and fires _onNewMaster.
        /// </summary>
        public static void TransferMaster(VRCPlayerApi newMaster) =>
            SimNetwork.SimulateMasterTransfer(newMaster);

        /// <summary>
        /// Simulate SendCustomNetworkEvent routing.
        /// Returns true if the event fired, false if skipped (e.g. Owner target
        /// but caller is not owner).
        /// </summary>
        public static bool SendNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target,
            GameObject obj, string eventName) =>
            SimNetwork.SimulateNetworkEvent(target, obj, eventName);

        // ── Udon Variable Access ───────────────────────────────────

        /// <summary>Read an Udon program variable by name from the first UdonBehaviour on a GameObject.</summary>
        public static object GetVar(GameObject obj, string varName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            return udon != null ? SimReflection.GetProgramVariable(udon, varName) : null;
        }

        /// <summary>
        /// Write an Udon program variable by name. Writes to BOTH the Udon
        /// heap AND the C# proxy field so that VRCSim.Call() and GetVar()
        /// always see the same value. This matches how UdonSharp behaves in
        /// production, where both stores are always in sync.
        /// </summary>
        public static void SetVar(GameObject obj, string varName, object value)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SetProgramVariable(udon, varName, value);
            // Sync C# proxy so Call() always sees the correct value
            SimProxy.SetField(obj, varName, value, syncToHeap: false);
        }

        /// <summary>
        /// Write ONLY to the Udon heap, leaving the C# proxy field unchanged.
        /// Use this to simulate the gap between network data arriving and
        /// OnDeserialization firing — i.e. when testing the handler itself.
        /// Example:
        ///   VRCSim.SetVarHeapOnly(obj, "gamePhase", 2);
        ///   VRCSim.RunEvent(obj, "_onDeserialization"); // test the handler
        /// </summary>
        public static void SetVarHeapOnly(GameObject obj, string varName, object value)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SetProgramVariable(udon, varName, value);
        }

        /// <summary>Send a custom event to the first UdonBehaviour on a GameObject.</summary>
        public static void SendEvent(GameObject obj, string eventName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SendCustomEvent(udon, eventName);
        }

        /// <summary>
        /// Get the names of all [UdonSynced] variables on a GameObject.
        /// </summary>
        public static List<string> GetSyncedVarNames(GameObject obj)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            return udon != null
                ? SimReflection.GetSyncedVarNames(udon)
                : new List<string>();
        }

        /// <summary>
        /// Get all synced variables and their current values.
        /// </summary>
        public static Dictionary<string, object> GetSyncedVars(GameObject obj)
        {
            var result = new Dictionary<string, object>();
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon == null) return result;
            foreach (var name in SimReflection.GetSyncedVarNames(udon))
            {
                if (SimReflection.TryGetProgramVariable(udon, name, out var val))
                    result[name] = val;
            }
            return result;
        }

        // ── Snapshots ─────────────────────────────────────────

        /// <summary>
        /// Capture the current synced state of all UdonBehaviours.
        /// </summary>
        public static SimSnapshot TakeSnapshot() =>
            SimSnapshot.Take();

        /// <summary>
        /// Capture synced state for a single GameObject.
        /// </summary>
        public static SimSnapshot TakeSnapshot(GameObject obj) =>
            SimSnapshot.TakeFor(obj);

        /// <summary>
        /// Diff two snapshots and return the changes.
        /// </summary>
        public static List<SimSnapshot.SyncChange> DiffSnapshots(
            SimSnapshot before, SimSnapshot after) =>
            SimSnapshot.Diff(before, after);

        /// <summary>
        /// Run an Udon program event through the UdonBehaviour program.
        /// Events execute through program variable storage, NOT MonoBehaviour fields.
        /// Critical: station events write to program heap, so game logic methods
        /// must be called via RunEvent to see the correct state.
        /// </summary>
        public static void RunEvent(GameObject obj, string eventName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.RunEvent(udon, eventName);
        }

        /// <summary>
        /// Tick one frame of the UdonBehaviour's Update loop.
        /// </summary>
        public static void RunUpdate(GameObject obj) => RunEvent(obj, "_update");

        /// <summary>
        /// Tick multiple frames of the UdonBehaviour's Update loop.
        /// Equivalent to calling RunUpdate in a loop, but reads cleaner in tests.
        /// </summary>
        public static void RunUpdate(GameObject obj, int frames)
        {
            for (int i = 0; i < frames; i++)
                RunEvent(obj, "_update");
        }

        // ── Private Helpers ────────────────────────────────────────

        /// <summary>
        /// Clear station occupancy for a player being removed.
        /// Prevents stale references after RemovePlayer/RemoveAllPlayers.
        /// </summary>
        private static void ClearStationsForPlayer(VRCPlayerApi player)
        {
            var stations = UnityEngine.Object.FindObjectsByType<
                VRC.SDK3.Components.VRCStation>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var station in stations)
            {
                var helper = SimReflection.GetStationHelper(station.gameObject);
                if (helper == null) continue;

                var user = SimReflection.GetStationUser(helper);
                if (user != null && user.playerId == player.playerId)
                    SimReflection.SetStationUser(helper, null);
            }
        }

        /// <summary>
        /// Swap _localPlayer on every UdonBehaviour in the scene that has one.
        /// Returns the list of (component, original) pairs for restoration.
        /// </summary>
        private static List<(Component udon, object original)> SwapLocalPlayerRefs(
            VRCPlayerApi player)
        {
            var swapped = new List<(Component, object)>();
#pragma warning disable CS0618 // FindObjectsOfType is obsolete but the non-generic
            // FindObjectsByType(Type,...) doesn't exist in Unity 2022.3
            var allUdons = UnityEngine.Object.FindObjectsOfType(
                SimReflection.UdonBehaviourType);
#pragma warning restore CS0618

            foreach (Component udon in allUdons)
            {
                if (!SimReflection.TryGetProgramVariable(udon, "_localPlayer",
                        out var current))
                    continue;
                if (current == null) continue;
                swapped.Add((udon, current));
                SimReflection.SetProgramVariable(udon, "_localPlayer", player);
            }
            return swapped;
        }

        private static void RestoreLocalPlayerRefs(
            List<(Component udon, object original)> swapped)
        {
            foreach (var (udon, original) in swapped)
            {
                try { SimReflection.SetProgramVariable(udon, "_localPlayer", original); }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[VRCSim] RestoreLocalPlayerRefs: failed to restore {udon}: {e.Message}");
                }
            }
        }

        private static void EnsureReady()
        {
            if (!_ready)
            {
                var err = Init();
                if (err != null)
                    throw new InvalidOperationException($"[VRCSim] Not ready: {err}");
            }
        }
    }
}
