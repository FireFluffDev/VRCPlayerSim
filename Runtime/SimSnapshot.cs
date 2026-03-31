using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VRCSim
{
    /// <summary>
    /// Captures and diffs the synced state of all UdonBehaviours in the scene.
    /// Used to verify what changed after an action (sit, phase transition, etc).
    /// </summary>
    public class SimSnapshot
    {
        /// <summary>
        /// Key: GameObject path (e.g. "_Systems/GameManager").
        /// Value: dictionary of synced var name → value at capture time.
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> State { get; }
            = new();

        /// <summary>Timestamp of when this snapshot was taken.</summary>
        public float Time { get; }

        private SimSnapshot(float time) => Time = time;

        // ── Factory ────────────────────────────────────────────────

        /// <summary>
        /// Capture the current synced state of all UdonBehaviours in the scene.
        /// Only includes UdonBehaviours that have at least one [UdonSynced] variable.
        /// </summary>
        public static SimSnapshot Take()
        {
            var snap = new SimSnapshot(UnityEngine.Time.time);
            var udons = SimReflection.FindAllUdonBehaviours();

            foreach (var udon in udons)
            {
                var syncedNames = SimReflection.GetSyncedVarNames(udon);
                if (syncedNames.Count == 0) continue;

                var path = SimReflection.GetPath(udon.transform);
                var vars = new Dictionary<string, object>();

                foreach (var varName in syncedNames)
                {
                    if (SimReflection.TryGetProgramVariable(udon, varName,
                            out var value))
                        vars[varName] = value;
                }

                snap.State[path] = vars;
            }

            return snap;
        }

        /// <summary>
        /// Capture synced state for a single GameObject only.
        /// </summary>
        public static SimSnapshot TakeFor(GameObject obj)
        {
            var snap = new SimSnapshot(UnityEngine.Time.time);
            var udons = SimReflection.GetUdonBehaviours(obj);

            foreach (var udon in udons)
            {
                var syncedNames = SimReflection.GetSyncedVarNames(udon);
                if (syncedNames.Count == 0) continue;

                var path = SimReflection.GetPath(udon.transform);
                var vars = new Dictionary<string, object>();

                foreach (var varName in syncedNames)
                {
                    if (SimReflection.TryGetProgramVariable(udon, varName,
                            out var value))
                        vars[varName] = value;
                }

                snap.State[path] = vars;
            }

            return snap;
        }

        // ── Diff ───────────────────────────────────────────────────

        /// <summary>
        /// Compare two snapshots and return a list of changes.
        /// </summary>
        public static List<SyncChange> Diff(SimSnapshot before, SimSnapshot after)
        {
            var changes = new List<SyncChange>();

            // Check everything in 'after' against 'before'
            foreach (var (path, afterVars) in after.State)
            {
                before.State.TryGetValue(path, out var beforeVars);

                foreach (var (varName, afterVal) in afterVars)
                {
                    object beforeVal = null;
                    bool existed = beforeVars != null
                        && beforeVars.TryGetValue(varName, out beforeVal);

                    if (!existed || !DeepEquals(beforeVal, afterVal))
                    {
                        changes.Add(new SyncChange
                        {
                            ObjectPath = path,
                            VarName = varName,
                            Before = existed ? beforeVal : null,
                            After = afterVal,
                            Type = existed ? ChangeType.Modified : ChangeType.Added
                        });
                    }
                }
            }

            // Check for vars/objects that existed in 'before' but not 'after'
            foreach (var (path, beforeVars) in before.State)
            {
                after.State.TryGetValue(path, out var afterVars);

                foreach (var (varName, beforeVal) in beforeVars)
                {
                    bool stillExists = afterVars != null
                        && afterVars.ContainsKey(varName);
                    if (!stillExists)
                    {
                        changes.Add(new SyncChange
                        {
                            ObjectPath = path,
                            VarName = varName,
                            Before = beforeVal,
                            After = null,
                            Type = ChangeType.Removed
                        });
                    }
                }
            }

            return changes;
        }

        // ── Display ────────────────────────────────────────────────

        /// <summary>Format the snapshot as a readable string.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Sync Snapshot (t={Time:F2}) ===");
            foreach (var (path, vars) in State)
            {
                sb.AppendLine($"  {path}:");
                foreach (var (name, val) in vars)
                    sb.AppendLine($"    {name} = {FormatValue(val)}");
            }
            return sb.ToString();
        }

        /// <summary>Format a diff as a readable string.</summary>
        public static string FormatDiff(List<SyncChange> changes)
        {
            if (changes.Count == 0) return "  (no changes)";

            var sb = new StringBuilder();
            string lastPath = null;
            foreach (var c in changes)
            {
                if (c.ObjectPath != lastPath)
                {
                    sb.AppendLine($"  {c.ObjectPath}:");
                    lastPath = c.ObjectPath;
                }
                sb.AppendLine($"    {c.VarName}: " +
                    $"{FormatValue(c.Before)} → {FormatValue(c.After)}");
            }
            return sb.ToString();
        }

        // ── Types ──────────────────────────────────────────────────

        public enum ChangeType { Added, Modified, Removed }

        public struct SyncChange
        {
            public string ObjectPath;
            public string VarName;
            public object Before;
            public object After;
            public ChangeType Type;

            public override string ToString() =>
                $"[{Type}] {ObjectPath}.{VarName}: " +
                $"{FormatValue(Before)} → {FormatValue(After)}";
        }

        // ── Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Deep equality that handles arrays by value (not reference).
        /// System.Object.Equals compares array references, which means
        /// two int[] with identical contents compare as NOT equal.
        /// </summary>
        internal static bool DeepEquals(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a is Array arrA && b is Array arrB)
            {
                if (arrA.GetType() != arrB.GetType()) return false;
                if (arrA.Length != arrB.Length) return false;
                for (int i = 0; i < arrA.Length; i++)
                    if (!Equals(arrA.GetValue(i), arrB.GetValue(i)))
                        return false;
                return true;
            }
            return Equals(a, b);
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is Vector3 v3)
                return $"({v3.x:F2}, {v3.y:F2}, {v3.z:F2})";
            if (val is Quaternion q)
                return $"({q.x:F2}, {q.y:F2}, {q.z:F2}, {q.w:F2})";
            if (val is Color c)
                return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            if (val is int[] ia)
                return $"int[{ia.Length}]{{{string.Join(",", ia)}}}";
            if (val is float[] fa)
                return $"float[{fa.Length}]{{{string.Join(",", fa)}}}";
            if (val is string[] sa)
                return $"string[{sa.Length}]{{{string.Join(",", sa)}}}";
            if (val is bool[] ba)
                return $"bool[{ba.Length}]{{{string.Join(",", ba)}}}";
            return val.ToString();
        }

    }
}
