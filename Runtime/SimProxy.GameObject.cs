using UnityEngine;

namespace VRCSim
{
    /// <summary>
    /// SimProxy GameObject overloads.
    /// Accept a GameObject instead of a Component, automatically finding
    /// the UdonSharpBehaviour C# proxy. Matches the pattern of
    /// VRCSim.GetVar/SetVar/RunEvent which also accept GameObjects.
    /// </summary>
    public static partial class SimProxy
    {
        //  GameObject overloads — auto-discover UdonSharpBehaviour proxy
        // ══════════════════════════════════════════════════════════════

        // UdonSharp objects have TWO components on the same GameObject:
        //   1. UdonBehaviour — the Udon VM runtime (has the heap)
        //   2. The C# proxy (e.g. GameManager) — has the actual methods/fields
        //
        // VRCSim's heap APIs (GetVar/SetVar/RunEvent) all take GameObjects
        // and internally find the UdonBehaviour.  The proxy APIs should match
        // that pattern — accept a GameObject and find the C# proxy.

        /// <summary>
        /// Find the UdonSharpBehaviour C# proxy component on a GameObject.
        /// Skips UdonBehaviour, Transform, and other Unity built-in components.
        /// Returns the first MonoBehaviour whose type inherits from
        /// UdonSharpBehaviour (detected by walking the type hierarchy —
        /// avoids hard dependency on UdonSharp assembly).
        /// Returns null if no proxy is found.
        /// </summary>
        public static Component FindProxy(GameObject obj)
        {
            if (obj == null) return null;

            var udonType = SimReflection.UdonBehaviourType;
            foreach (var comp in obj.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var t = comp.GetType();

                // Skip UdonBehaviour itself
                if (udonType != null && udonType.IsAssignableFrom(t)) continue;

                // Walk hierarchy looking for UdonSharpBehaviour base
                var current = t;
                while (current != null && current != typeof(MonoBehaviour))
                {
                    if (current.FullName == "UdonSharp.UdonSharpBehaviour")
                        return comp;
                    current = current.BaseType;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the UdonSharpBehaviour proxy that has a specific method.
        /// Useful when a GameObject has multiple UdonSharp components.
        /// </summary>
        public static Component FindProxyWithMethod(GameObject obj,
            string methodName, int paramCount = 0)
        {
            if (obj == null) return null;

            var udonType = SimReflection.UdonBehaviourType;
            foreach (var comp in obj.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var t = comp.GetType();
                if (udonType != null && udonType.IsAssignableFrom(t)) continue;

                if (ResolveMethod(t, methodName, paramCount) != null)
                    return comp;
            }

            return null;
        }

        /// <summary>
        /// Find the UdonSharpBehaviour proxy that has a specific field.
        /// </summary>
        public static Component FindProxyWithField(GameObject obj, string fieldName)
        {
            if (obj == null) return null;

            var udonType = SimReflection.UdonBehaviourType;
            foreach (var comp in obj.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var t = comp.GetType();
                if (udonType != null && udonType.IsAssignableFrom(t)) continue;

                if (ResolveField(t, fieldName) != null)
                    return comp;
            }

            return null;
        }

        // ── GameObject convenience overloads ───────────────────────

        /// <summary>
        /// Call a method on the UdonSharpBehaviour proxy found on a GameObject.
        /// Auto-discovers the proxy component — no manual GetComponent needed.
        /// </summary>
        public static object Call(GameObject obj, string methodName,
            params object[] args)
        {
            var proxy = FindProxyWithMethod(obj, methodName, args?.Length ?? 0);
            if (proxy == null)
            {
                Debug.LogError(
                    $"[VRCSim] Call: no component on '{obj.name}' has method " +
                    $"'{methodName}' with {args?.Length ?? 0} params.");
                return null;
            }
            return Call(proxy, methodName, args);
        }

        /// <summary>
        /// Call a method on a GameObject with typed return value.
        /// Renamed from Call to CallAs -- avoids C# overload resolution picking
        /// the generic over params object[] when float/int literals are passed.
        /// </summary>
        public static T CallAs<T>(GameObject obj, string methodName,
            T defaultValue, params object[] args)
        {
            var result = Call(obj, methodName, args);
            if (result is T typed) return typed;
            try
            {
                var coerced = CoerceValue(result, typeof(T));
                return coerced is T c ? c : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Set a C# proxy field on the UdonSharpBehaviour found on a GameObject.
        /// </summary>
        public static void SetField(GameObject obj, string fieldName,
            object value, bool syncToHeap = true)
        {
            var proxy = FindProxyWithField(obj, fieldName);
            if (proxy == null)
            {
                Debug.LogError(
                    $"[VRCSim] SetField: no component on '{obj.name}' has field '{fieldName}' -- field not on C# proxy. Use SetVar() for heap vars.");
                return;
            }
            SetField(proxy, fieldName, value, syncToHeap);
        }

        /// <summary>
        /// Read a C# proxy field from the UdonSharpBehaviour found on a GameObject.
        /// </summary>
        public static object GetField(GameObject obj, string fieldName)
        {
            var proxy = FindProxyWithField(obj, fieldName);
            if (proxy == null)
            {
                Debug.LogWarning(
                    $"[VRCSim] GetField: no component on '{obj.name}' has field '{fieldName}'");
                return null;
            }
            return GetField(proxy, fieldName);
        }

        /// <summary>
        /// Read a C# proxy field from a GameObject with typed return.
        /// </summary>
        public static T GetField<T>(GameObject obj, string fieldName,
            T defaultValue = default)
        {
            var raw = GetField(obj, fieldName);
            if (raw == null) return defaultValue;
            try
            {
                var coerced = CoerceValue(raw, typeof(T));
                return coerced is T typed ? typed : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Initialize the proxy on the UdonSharpBehaviour found on a GameObject.
        /// </summary>
        public static int InitProxy(GameObject obj)
        {
            var proxy = FindProxy(obj);
            if (proxy == null)
            {
                Debug.LogWarning(
                    $"[VRCSim] InitProxy: no UdonSharpBehaviour proxy on '{obj.name}'");
                return 0;
            }
            return InitProxy(proxy);
        }

        /// <summary>
        /// InitProxy + deep scene reference resolution on a GameObject.
        /// </summary>
        public static int InitProxyDeep(GameObject obj)
        {
            var proxy = FindProxy(obj);
            if (proxy == null)
            {
                Debug.LogWarning(
                    $"[VRCSim] InitProxyDeep: no UdonSharpBehaviour proxy on '{obj.name}'");
                return 0;
            }
            return InitProxyDeep(proxy);
        }

    }
}
