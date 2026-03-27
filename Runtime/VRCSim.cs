using System;
using System.Collections.Generic;
using System.Text;
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
    public static class VRCSim
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

            var newPlayer = VRCPlayerApi.AllPlayers[VRCPlayerApi.AllPlayers.Count - 1];
            _bots.Add(newPlayer);

            Debug.Log($"[VRCSim] Spawned: {newPlayer.displayName} (id={newPlayer.playerId})");
            return newPlayer;
        }

        /// <summary>
        /// Remove a bot player. Fires OnPlayerLeft on all UdonBehaviours.
        /// </summary>
        public static void RemovePlayer(VRCPlayerApi player)
        {
            EnsureReady();
            _bots.Remove(player);
            ClearStationsForPlayer(player);
            SimReflection.RemovePlayer(player);
            Debug.Log($"[VRCSim] Removed: {player.displayName}");
        }

        /// <summary>
        /// Remove all bots spawned in this session.
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

        /// <summary>Get a bot by name (partial match).</summary>
        public static VRCPlayerApi GetBot(string name)
        {
            foreach (var bot in _bots)
                if (bot != null && bot.IsValid() && bot.displayName.Contains(name))
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

        // ── Ownership ──────────────────────────────────────────────

        public static void SetOwner(VRCPlayerApi player, GameObject obj) =>
            SimNetwork.TransferOwnership(player, obj);

        public static VRCPlayerApi GetOwner(GameObject obj) =>
            Networking.GetOwner(obj);

        // ── Networking Rule Enforcement ────────────────────────────

        public static void EnforceKinematic(GameObject obj) =>
            SimNetwork.EnforceKinematicOnRemote(obj);

        public static List<SimNetwork.KinematicIssue> ValidateKinematic() =>
            SimNetwork.ValidateKinematicState();

        public static void SimulateDeserialization(GameObject obj) =>
            SimNetwork.SimulateDeserialization(obj);

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

        public static object GetVar(GameObject obj, string varName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            return udon != null ? SimReflection.GetProgramVariable(udon, varName) : null;
        }

        public static void SetVar(GameObject obj, string varName, object value)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SetProgramVariable(udon, varName, value);
        }

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

        // ── Validation & Reporting ─────────────────────────────────

        public static string GetStateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== VRCSim State Report ===");
            sb.AppendLine();

            var pm = SimReflection.GetPlayerManager();
            int masterId = pm != null ? SimReflection.GetMasterId(pm) : -1;

            sb.AppendLine("PLAYERS:");
            foreach (var player in VRCPlayerApi.AllPlayers)
            {
                bool isMaster = player.playerId == masterId;
                bool isBot = _bots.Contains(player);
                sb.AppendLine($"  [{player.playerId}] {player.displayName}" +
                    $" | local={player.isLocal}" +
                    $" | master={isMaster}" +
                    $" | bot={isBot}");
            }
            sb.AppendLine();

            var kinIssues = ValidateKinematic();
            sb.AppendLine($"KINEMATIC ISSUES: {kinIssues.Count}");
            foreach (var issue in kinIssues)
                sb.AppendLine($"  {issue}");
            sb.AppendLine();

            sb.AppendLine("OBJECT OWNERSHIP:");
            var syncs = UnityEngine.Object.FindObjectsByType<
                VRC.SDK3.Components.VRCObjectSync>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var sync in syncs)
            {
                var owner = Networking.GetOwner(sync.gameObject);
                var rb = sync.GetComponent<Rigidbody>();
                sb.AppendLine($"  {sync.gameObject.name}" +
                    $" | owner={owner?.displayName ?? "none"}" +
                    $" | kinematic={rb?.isKinematic}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Validate expected synced var values on a GameObject.
        /// </summary>
        public static string ValidateVars(GameObject obj,
            params (string varName, object expected)[] expectations)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"VALIDATION: {obj.name}");

            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon == null)
            {
                sb.AppendLine("  ERROR: No UdonBehaviour");
                return sb.ToString();
            }

            bool allPass = true;
            foreach (var (varName, expected) in expectations)
            {
                var actual = SimReflection.GetProgramVariable(udon, varName);
                bool match = Equals(actual, expected);
                if (!match) allPass = false;

                sb.AppendLine($"  [{(match ? "PASS" : "FAIL")}] {varName}: " +
                    $"actual={actual ?? "null"}, expected={expected ?? "null"}");
            }

            sb.AppendLine(allPass ? "  RESULT: ALL PASS" : "  RESULT: FAILURES DETECTED");
            return sb.ToString();
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
                catch { /* Best effort restore */ }
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
