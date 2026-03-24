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
                Debug.LogError("[VRCSim] SpawnPlayer failed");
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
                    SimReflection.RemovePlayer(bot);
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
        /// Teleport a player to a position. Bypasses ClientSim's
        /// remote-teleport-does-nothing guard.
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
        /// This fires events through the REAL ClientSim pipeline:
        ///   IClientSimStationHandler.OnStationEnter → ClientSimUdonHelper
        ///   → UdonBehaviour.RunEvent(eventName, ("Player", Networking.LocalPlayer))
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

            var currentUser = SimReflection.GetStationUser(helper);
            if (currentUser != null)
            {
                Debug.LogWarning(
                    $"[VRCSim] {stationObj.name} occupied by {currentUser.displayName}");
                return false;
            }

            // Set _usingPlayer so IsOccupied() returns true
            SimReflection.SetStationUser(helper, player);

            // Move player to station position
            var enterLoc = station.stationEnterPlayerLocation;
            if (enterLoc != null)
                Teleport(player, enterLoc.position, enterLoc.rotation);

            // Fire events through the real pipeline as the sitting player.
            // Inside RunAsPlayer: Networking.LocalPlayer == player,
            // so UdonHelper.OnStationEnter fires RunEvent with the correct player.
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
            if (currentUser == null || currentUser.playerId != player.playerId)
            {
                Debug.LogWarning($"[VRCSim] {player.displayName} not in {stationObj.name}");
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
        /// This is the key unlock for testing non-master behavior.
        /// </summary>
        public static void RunAsPlayer(VRCPlayerApi player, Action action) =>
            SimNetwork.RunAsPlayer(player, action);

        // ── Ownership ──────────────────────────────────────────────

        /// <summary>
        /// Transfer ownership and enforce ForceKinematicOnRemote.
        /// </summary>
        public static void SetOwner(VRCPlayerApi player, GameObject obj) =>
            SimNetwork.TransferOwnership(player, obj);

        /// <summary>Check who owns an object.</summary>
        public static VRCPlayerApi GetOwner(GameObject obj) =>
            Networking.GetOwner(obj);

        // ── Networking Rule Enforcement ────────────────────────────

        /// <summary>
        /// Enforce ForceKinematicOnRemote on a single object.
        /// </summary>
        public static void EnforceKinematic(GameObject obj) =>
            SimNetwork.EnforceKinematicOnRemote(obj);

        /// <summary>
        /// Validate kinematic state across all VRCObjectSync objects.
        /// Returns issues where non-owners have non-kinematic rigidbodies.
        /// </summary>
        public static List<SimNetwork.KinematicIssue> ValidateKinematic() =>
            SimNetwork.ValidateKinematicState();

        /// <summary>Simulate deserialization on a GameObject's UdonBehaviours.</summary>
        public static void SimulateDeserialization(GameObject obj) =>
            SimNetwork.SimulateDeserialization(obj);

        /// <summary>Simulate a late joiner receiving current synced state.</summary>
        public static void SimulateLateJoiner(GameObject obj) =>
            SimNetwork.SimulateLateJoiner(obj);

        // ── Udon Variable Access ───────────────────────────────────

        /// <summary>Get a program variable from an UdonBehaviour.</summary>
        public static object GetVar(GameObject obj, string varName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            return udon != null ? SimReflection.GetProgramVariable(udon, varName) : null;
        }

        /// <summary>Set a program variable on an UdonBehaviour.</summary>
        public static void SetVar(GameObject obj, string varName, object value)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SetProgramVariable(udon, varName, value);
        }

        /// <summary>Send a custom event to an UdonBehaviour.</summary>
        public static void SendEvent(GameObject obj, string eventName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null) SimReflection.SendCustomEvent(udon, eventName);
        }

        // ── Validation & Reporting ─────────────────────────────────

        /// <summary>
        /// Comprehensive state report: players, ownership, stations, kinematic.
        /// </summary>
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
