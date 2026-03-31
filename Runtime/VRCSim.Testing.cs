using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    // Testing convenience methods — validation, batch ops, method invocation,
    // physics ticking, and event simulation. Split from VRCSim.cs for
    // file-size hygiene; same public API surface.
    //
    // DESIGN RULES for this facade:
    //   SetVar    → canonical write  (heap + proxy, always in sync)
    //   GetVar    → canonical read   (heap — source of truth for synced vars)
    //   GetField  → post-Call read   (proxy — when Call() changed state not yet on heap)
    //   GetBoth   → read both stores at once; .Heap, .Proxy, .InSync (see VarState)
    //   Call      → void/object return method invocation (no type param)
    //   CallAs<T> → typed return method invocation
    //              NOTE: Call<T> was renamed CallAs<T> to prevent C# overload
    //              resolution from picking the generic over params object[] when
    //              float/int literals are passed (exact match beats boxing).
    //
    //   SetField / InitProxy / FindProxy live on SimProxy only —
    //   they are escape hatches, not primary API surface.
    public static partial class VRCSim
    {
        // ── Validation & Reporting ─────────────────────────────────

        /// <summary>
        /// Build a human-readable report of all players, ownership,
        /// and kinematic issues. Useful as a last line in any test.
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
        /// Validate expected synced var values on a GameObject. Returns
        /// a formatted pass/fail report string.
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

        /// <summary>
        /// Validate expected synced var values. Returns true only if
        /// all expectations match. Cheaper than ValidateVars when you
        /// just need a pass/fail bool.
        /// </summary>
        public static bool CheckVars(GameObject obj,
            params (string varName, object expected)[] expectations)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon == null) return false;
            foreach (var (varName, expected) in expectations)
            {
                var actual = SimReflection.GetProgramVariable(udon, varName);
                if (!Equals(actual, expected)) return false;
            }
            return true;
        }

        // ── Batch Variable Access ──────────────────────────────────

        /// <summary>
        /// Set multiple Udon program variables on a single object in one
        /// call. Each write goes to BOTH heap and proxy (same as SetVar).
        /// Reduces boilerplate in test setup.
        /// </summary>
        public static void SetVars(GameObject obj,
            params (string name, object value)[] vars)
        {
            EnsureReady();
            foreach (var (name, value) in vars)
                SetVar(obj, name, value);
        }

        // ── Physics Simulation ──────────────────────────────────────

        /// <summary>
        /// Tick one frame of FixedUpdate on a specific object.
        /// Required for physics-dependent logic (ball elimination via
        /// Y-threshold, collision knockback, etc.).
        /// </summary>
        public static void RunFixedUpdate(GameObject obj) =>
            RunEvent(obj, "_fixedUpdate");

        /// <summary>
        /// Tick multiple frames of FixedUpdate on a specific object.
        /// </summary>
        public static void RunFixedUpdate(GameObject obj, int frames)
        {
            for (int i = 0; i < frames; i++)
                RunEvent(obj, "_fixedUpdate");
        }

        // ── Game Loop Simulation ────────────────────────────────────

        /// <summary>
        /// Fire _update on ALL active UdonBehaviours in the scene.
        /// Simulates one full frame of the VRChat game loop.
        /// </summary>
        public static void TickAll(int frames = 1)
        {
            EnsureReady();
            for (int f = 0; f < frames; f++)
            {
                // Re-query each frame: an _update can destroy UdonBehaviours
                var udons = SimReflection.FindAllUdonBehaviours();
                foreach (var udon in udons)
                    if (udon != null) SimReflection.RunEvent(udon, "_update");
            }
        }

        /// <summary>
        /// Fire _fixedUpdate on ALL active UdonBehaviours in the scene.
        /// Simulates one full physics tick of the VRChat game loop.
        /// </summary>
        public static void TickFixedAll(int frames = 1)
        {
            EnsureReady();
            for (int f = 0; f < frames; f++)
            {
                var udons = SimReflection.FindAllUdonBehaviours();
                foreach (var udon in udons)
                    if (udon != null) SimReflection.RunEvent(udon, "_fixedUpdate");
            }
        }

        // ── Sync Propagation ────────────────────────────────────────

        /// <summary>
        /// Simulate a full sync cycle: fire OnDeserialization from every
        /// non-owner player's perspective.
        ///
        /// In real VRChat, after RequestSerialization(), all non-owner
        /// clients receive the synced data and fire OnDeserialization.
        /// ClientSim shares a single Udon heap so values are already
        /// present — this just fires the event from each perspective.
        /// </summary>
        public static void SyncToAll(GameObject obj)
        {
            EnsureReady();
            SimNetwork.SyncToAllClients(obj);
        }

        // ── Parameterized Events ────────────────────────────────────

        /// <summary>
        /// Fire an Udon event with named arguments on a specific object.
        /// Mirrors how VRChat passes parameters to lifecycle events.
        ///
        /// Usage:
        ///   VRCSim.RunEventWithArgs(gmObj, "_onPlayerLeft",
        ///       ("player", bobApi));
        /// </summary>
        public static void RunEventWithArgs(GameObject obj, string eventName,
            params (string name, object value)[] args)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon != null)
                SimReflection.RunEventWithArgs(udon, eventName, args);
        }

        // ── Station Query ──────────────────────────────────────────

        /// <summary>
        /// Check if a specific player is currently seated in a station.
        /// </summary>
        public static bool IsPlayerInStation(VRCPlayerApi player,
            GameObject stationObj)
        {
            var helper = SimReflection.GetStationHelper(stationObj);
            if (helper == null) return false;
            var user = SimReflection.GetStationUser(helper);
            return user != null && user.IsValid()
                && user.playerId == player.playerId;
        }

        // ── Method Invocation ──────────────────────────────────────
        //
        // Call() auto-discovers the UdonSharpBehaviour C# proxy from a
        // GameObject so you never need to GetComponent<GameManager>()
        // manually. The Component overloads are kept for cases where you
        // already have the component reference.
        //
        // IMPORTANT: Use CallAs<T> (not Call<T>) for typed return values.
        // The generic Call<T> overload was removed because C# overload
        // resolution prefers it over params object[] when float/int literals
        // are passed (T=float is an exact match; boxing to object is not).
        // Result: Call(obj, "method", 1f, 2f) silently became
        // Call<float>(obj, "method", defaultValue:1f, args:[2f]) -- wrong arg count.

        /// <summary>
        /// Call any method (including private) on the UdonSharpBehaviour
        /// proxy found on a GameObject. Auto-discovers the proxy.
        /// Returns object; use CallAs for typed return values.
        /// </summary>
        public static object Call(GameObject obj, string methodName,
            params object[] args) =>
            SimProxy.Call(obj, methodName, args);

        /// <summary>
        /// Call a method on a GameObject with a typed return value.
        /// Named CallAs (not Call) to prevent the C# generic overload
        /// resolution trap -- see the comment block above.
        /// </summary>
        public static T CallAs<T>(GameObject obj, string methodName,
            T defaultValue, params object[] args) =>
            SimProxy.CallAs(obj, methodName, defaultValue, args);

        /// <summary>
        /// Call any method (including private) on a Component directly.
        /// Use the GameObject overload unless you already have the component.
        /// </summary>
        public static object Call(Component behaviour, string methodName,
            params object[] args) =>
            SimProxy.Call(behaviour, methodName, args);

        /// <summary>
        /// Call a method on a Component with a typed return value.
        /// Named CallAs (not Call) -- see the comment block above.
        /// </summary>
        public static T CallAs<T>(Component behaviour, string methodName,
            T defaultValue, params object[] args) =>
            SimProxy.CallAs(behaviour, methodName, defaultValue, args);

        // ── State Reading ──────────────────────────────────────────
        //
        // Three ways to read state, each reading from a different store:
        //
        //   GetVar(obj, name)
        //     Reads the Udon VM heap via GetProgramVariable.
        //     Correct after: SetVar, RunEvent, RunFixedUpdate, deserialization.
        //
        //   GetField(obj, name)
        //     Reads the C# proxy field via FieldInfo.GetValue.
        //     Correct after: Call() -- C# methods update the proxy directly
        //     without flushing back to the heap.
        //
        //   GetBoth(obj, name)
        //     Reads both stores and returns a VarState.
        //     Use .Heap or .Proxy to pick whichever store you care about.
        //     Use .InSync to assert both stores agree (free invariant check).

        /// <summary>
        /// Read a C# proxy field from the UdonSharpBehaviour found on a
        /// GameObject. Use after Call() to assert on state the method changed.
        /// Use GetVar() for synced variables set by SetVar or deserialization.
        /// </summary>
        public static object GetField(GameObject obj, string fieldName) =>
            SimProxy.GetField(obj, fieldName);

        /// <summary>Read a C# proxy field from a GameObject with typed return.</summary>
        public static T GetField<T>(GameObject obj, string fieldName,
            T defaultValue = default) =>
            SimProxy.GetField<T>(obj, fieldName, defaultValue);

        /// <summary>
        /// Read a variable from BOTH the Udon heap and the C# proxy in one call.
        /// Returns a VarState exposing .Heap, .Proxy, .InSync, .HeapAs, .ProxyAs.
        ///
        /// Example -- assert heap after RunEvent, then confirm both stores agree:
        ///   VRCSim.RunEvent(gmObj, "_startGame");
        ///   var state = VRCSim.GetBoth(gmObj, "gamePhase");
        ///   Assert.AreEqual(1, state.HeapAs(0));   // VM ran, heap has truth
        ///   Assert.IsTrue(state.InSync);            // proxy should have caught up
        ///
        /// Example -- detect divergence after SetVarHeapOnly (intentional):
        ///   VRCSim.SetVarHeapOnly(gmObj, "gamePhase", 2);
        ///   Assert.IsFalse(VRCSim.GetBoth(gmObj, "gamePhase").InSync);
        /// </summary>
        public static VarState GetBoth(GameObject obj, string varName) =>
            new VarState(GetVar(obj, varName), SimProxy.GetField(obj, varName));

        // ── Player Events (without removal) ────────────────────────

        /// <summary>
        /// Fire OnPlayerLeft on ALL UdonBehaviours for a player WITHOUT
        /// removing them from the player list. Use this to test game
        /// logic's DC response without destroying the player object.
        /// For full removal, use VRCSim.RemovePlayer instead.
        /// </summary>
        public static void SimulatePlayerLeft(VRCPlayerApi player) =>
            SimNetwork.FirePlayerLeftOnAll(player);

        /// <summary>
        /// Fire OnPlayerJoined on ALL UdonBehaviours for a player.
        /// Useful for late-join scenarios or re-triggering join logic.
        /// </summary>
        public static void SimulatePlayerJoined(VRCPlayerApi player) =>
            SimNetwork.FirePlayerJoinedOnAll(player);
    }
}
