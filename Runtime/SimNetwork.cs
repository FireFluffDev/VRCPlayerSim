using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    /// <summary>
    /// Simulates VRChat networking rules that ClientSim skips:
    /// - Perspective swapping (run code as non-master)
    /// - ForceKinematicOnRemote enforcement
    /// - Ownership tracking
    /// - Deserialization simulation
    /// </summary>
    public static class SimNetwork
    {
        // ── Perspective Swap State ─────────────────────────────────
        private static bool _inPerspectiveSwap;
        private static int _savedMasterId;
        private static int _savedLocalPlayerId;
        private static VRCPlayerApi _savedLocalPlayer;
        private static readonly List<(VRCPlayerApi player, bool wasLocal)> _localSwaps = new();

        /// <summary>
        /// Run an action from the perspective of a specific player.
        /// While inside this block:
        ///   - Networking.LocalPlayer returns the specified player
        ///   - Networking.IsMaster returns true ONLY if this player is actually master
        ///   - player.isLocal returns true for the specified player
        /// </summary>
        public static void RunAsPlayer(VRCPlayerApi player, Action action)
        {
            if (_inPerspectiveSwap)
                throw new InvalidOperationException(
                    "[VRCSim] Cannot nest RunAsPlayer calls");

            var pm = SimReflection.GetPlayerManager();
            if (pm == null)
                throw new InvalidOperationException(
                    "[VRCSim] ClientSim not running — cannot swap perspective");

            _inPerspectiveSwap = true;
            _localSwaps.Clear();

            // Save current state
            _savedMasterId = SimReflection.GetMasterId(pm);
            _savedLocalPlayerId = SimReflection.GetLocalPlayerId(pm);
            _savedLocalPlayer = SimReflection.GetLocalPlayer(pm);

            try
            {
                // Mark old local player as non-local
                if (_savedLocalPlayer != null)
                {
                    _localSwaps.Add((_savedLocalPlayer, true));
                    SimReflection.SetIsLocal(_savedLocalPlayer, false);
                }

                // Make the target player the "local" player
                SimReflection.SetLocalPlayerId(pm, player.playerId);
                SimReflection.SetLocalPlayer(pm, player);
                SimReflection.SetIsLocal(player, true);
                _localSwaps.Add((player, false));

                // Master stays unchanged — this is the real VRChat behavior
                // Player 1 is master regardless of whose perspective we're in

                action();
            }
            finally
            {
                // Restore everything
                SimReflection.SetLocalPlayerId(pm, _savedLocalPlayerId);
                SimReflection.SetLocalPlayer(pm, _savedLocalPlayer);

                // Restore isLocal on all swapped players
                foreach (var (p, wasLocal) in _localSwaps)
                {
                    if (p != null && p.IsValid())
                        SimReflection.SetIsLocal(p, wasLocal);
                }

                _localSwaps.Clear();
                _inPerspectiveSwap = false;
            }
        }

        /// <summary>
        /// Returns true if we're currently inside a RunAsPlayer block.
        /// </summary>
        public static bool InPerspectiveSwap => _inPerspectiveSwap;

        // ── ForceKinematicOnRemote ─────────────────────────────────

        /// <summary>
        /// Enforce ForceKinematicOnRemote on a GameObject with VRCObjectSync.
        /// Non-owners get kinematic=true, owners keep their coded state.
        /// Call this after ownership changes to simulate real VRChat behavior.
        /// </summary>
        public static void EnforceKinematicOnRemote(GameObject obj)
        {
            var objectSync = obj.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
            if (objectSync == null) return;

            var rb = obj.GetComponent<Rigidbody>();
            if (rb == null) return;

            var owner = Networking.GetOwner(obj);
            var localPlayer = Networking.LocalPlayer;

            // In real VRChat: non-owner rigidbodies are forced kinematic
            // In ClientSim: this is explicitly a TODO they never implemented
            bool isOwner = owner != null && localPlayer != null
                && owner.playerId == localPlayer.playerId;

            if (!isOwner)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        /// <summary>
        /// Check all VRCObjectSync objects in the scene and report
        /// which ones have incorrect kinematic state for non-owners.
        /// Does NOT modify anything — read-only validation.
        /// </summary>
        public static List<KinematicIssue> ValidateKinematicState()
        {
            var issues = new List<KinematicIssue>();
            var syncs = UnityEngine.Object.FindObjectsByType<VRC.SDK3.Components.VRCObjectSync>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var sync in syncs)
            {
                var rb = sync.GetComponent<Rigidbody>();
                if (rb == null) continue;

                var owner = Networking.GetOwner(sync.gameObject);
                int ownerId = owner?.playerId ?? -1;

                // Check each player's perspective
                foreach (var player in VRCPlayerApi.AllPlayers)
                {
                    if (player.playerId == ownerId) continue; // owners are fine

                    // For this non-owner player, the rigidbody SHOULD be kinematic
                    if (!rb.isKinematic)
                    {
                        issues.Add(new KinematicIssue
                        {
                            ObjectName = sync.gameObject.name,
                            ObjectPath = GetPath(sync.transform),
                            OwnerId = ownerId,
                            NonOwnerPlayerId = player.playerId,
                            IsKinematic = rb.isKinematic,
                            ShouldBeKinematic = true
                        });
                    }
                }
            }

            return issues;
        }

        // ── Deserialization Simulation ─────────────────────────────

        /// <summary>
        /// Simulate what happens when a non-master receives synced data.
        /// Fires OnDeserialization on the UdonBehaviour.
        /// </summary>
        public static void SimulateDeserialization(GameObject obj)
        {
            var udons = SimReflection.GetUdonBehaviours(obj);
            foreach (var udon in udons)
            {
                SimReflection.SendCustomEvent(udon, "OnDeserialization");
            }
        }

        /// <summary>
        /// Simulate a late joiner scenario: set all synced vars to their
        /// current values (as if received over the network), then fire
        /// OnDeserialization. This tests whether the system correctly
        /// reconstructs state from synced data alone.
        /// </summary>
        public static void SimulateLateJoiner(GameObject obj)
        {
            // OnDeserialization is what fires when synced data arrives
            // The synced vars are already set (they're in memory) — we just
            // need to trigger the reconstruction logic
            SimulateDeserialization(obj);
        }

        // ── Ownership Helpers ──────────────────────────────────────

        /// <summary>
        /// Transfer ownership and enforce kinematic rules.
        /// This is what should happen in real VRChat but ClientSim skips the kinematic part.
        /// </summary>
        public static void TransferOwnership(VRCPlayerApi newOwner, GameObject obj)
        {
            Networking.SetOwner(newOwner, obj);
            EnforceKinematicOnRemote(obj);
        }

        /// <summary>
        /// Validate that a synced variable was written by the owner.
        /// In real VRChat, non-owner writes to synced vars are local-only
        /// and get overwritten on next deserialization.
        /// </summary>
        public static bool ValidateOwnerWrite(GameObject obj, string varName)
        {
            var udon = SimReflection.GetUdonBehaviour(obj);
            if (udon == null) return true; // no udon = nothing to validate

            var owner = Networking.GetOwner(obj);
            var localPlayer = Networking.LocalPlayer;

            if (owner == null || localPlayer == null) return true;

            // If the local player isn't the owner, any write to a synced var
            // would be local-only in real VRChat
            return owner.playerId == localPlayer.playerId;
        }

        // ── Types ──────────────────────────────────────────────────

        public struct KinematicIssue
        {
            public string ObjectName;
            public string ObjectPath;
            public int OwnerId;
            public int NonOwnerPlayerId;
            public bool IsKinematic;
            public bool ShouldBeKinematic;

            public override string ToString() =>
                $"[MP-13] {ObjectName}: owner={OwnerId}, " +
                $"player {NonOwnerPlayerId} sees kinematic={IsKinematic} " +
                $"(should be {ShouldBeKinematic}) — path: {ObjectPath}";
        }

        // ── Helpers ────────────────────────────────────────────────

        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }
    }
}
