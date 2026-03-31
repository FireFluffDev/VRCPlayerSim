using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    /// <summary>
    /// Proxy-level reflection for UdonSharpBehaviour C# objects.
    ///
    /// UdonSharp creates a dual state for every field: the C# proxy field
    /// and the Udon program heap variable. In production, UdonSharp keeps
    /// them in sync. In tests, they can diverge because:
    ///   - VRCSim.SetVar writes to the Udon heap only
    ///   - C# methods called via reflection read C# proxy fields
    ///   - The proxy's Start() body doesn't run (Udon VM handles it)
    ///
    /// SimProxy bridges this gap with three capabilities:
    ///   1. InitProxy  — sync Udon heap → C# proxy fields (replaces manual ForceInit)
    ///   2. Field access — read/write C# proxy fields with type coercion
    ///   3. Method invocation — call any method (including private) with caching
    /// </summary>
    public static partial class SimProxy
    {
        // Per-type reflection caches (survive across tests within a play session)
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> _fieldCache = new();
        private static readonly Dictionary<Type, Dictionary<string, List<MethodInfo>>> _methodCache = new();

        /// <summary>Reset caches when entering play mode.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnPlayModeEnter()
        {
            _fieldCache.Clear();
            _methodCache.Clear();
        }

        // ══════════════════════════════════════════════════════════════
        //  Phase 1: InitProxy — sync Udon heap → C# proxy fields
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Initialize the C# proxy fields of an UdonSharpBehaviour by copying
        /// values from the Udon program heap.
        ///
        /// The Udon VM runs _start and populates the heap, but the C# proxy's
        /// Start() body never executes. This copies heap values into the
        /// corresponding C# fields so proxy methods work correctly in tests.
        ///
        /// Always sets _localPlayer = Networking.LocalPlayer (universal pattern).
        ///
        /// Returns the count of fields successfully synced.
        /// </summary>
        public static int InitProxy(Component behaviour)
        {
            if (behaviour == null)
                throw new ArgumentNullException(nameof(behaviour));

            var udon = SimReflection.GetUdonBehaviour(behaviour.gameObject);
            if (udon == null)
            {
                Debug.LogWarning($"[VRCSim] InitProxy: no UdonBehaviour on {behaviour.gameObject.name}");
                return 0;
            }

            var type = behaviour.GetType();
            var fields = GetOrBuildFieldCache(type);
            int synced = 0;

            foreach (var (name, field) in fields)
            {
                // _localPlayer is always set from Networking.LocalPlayer
                if (name == "_localPlayer")
                {
                    field.SetValue(behaviour, Networking.LocalPlayer);
                    synced++;
                    continue;
                }

                // Try to read from Udon heap and copy to proxy field
                if (!SimReflection.TryGetProgramVariable(udon, name, out var heapVal))
                    continue;

                if (heapVal == null) continue;

                try
                {
                    var coerced = CoerceValue(heapVal, field.FieldType);
                    if (coerced != null || !field.FieldType.IsValueType)
                    {
                        field.SetValue(behaviour, coerced);
                        synced++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[VRCSim] InitProxy: failed to set {type.Name}.{name}: {e.Message}");
                }
            }

            Debug.Log($"[VRCSim] InitProxy({type.Name}): synced {synced} fields from Udon heap");
            return synced;
        }

        /// <summary>
        /// Initialize proxy fields AND resolve common scene references that
        /// Start() typically sets via transform.Find / GetComponent / GetComponentInChildren.
        ///
        /// This handles the most common UdonSharp patterns:
        ///   _localPlayer = Networking.LocalPlayer        (always)
        ///   field of type Transform  → transform.Find(fieldNameWithout_) or GetComponentInChildren
        ///   field of type Component  → GetComponentInChildren(fieldType)
        ///   field of type Renderer   → GetComponentInChildren&lt;Renderer&gt;(true).transform
        ///
        /// Returns count of fields successfully initialized.
        /// </summary>
        public static int InitProxyDeep(Component behaviour)
        {
            int synced = InitProxy(behaviour);

            var type = behaviour.GetType();
            var fields = GetOrBuildFieldCache(type);
            var t = behaviour.transform;

            foreach (var (name, field) in fields)
            {
                // Skip if already set by InitProxy
                var current = field.GetValue(behaviour);
                if (current != null) continue;

                try
                {
                    // Transform fields: try transform.Find with common name patterns
                    if (field.FieldType == typeof(Transform))
                    {
                        string searchName = name.TrimStart('_');
                        var found = t.Find(searchName);
                        if (found != null)
                        {
                            field.SetValue(behaviour, found);
                            synced++;
                            continue;
                        }
                    }

                    // Component fields: try GetComponentInChildren
                    if (typeof(Component).IsAssignableFrom(field.FieldType))
                    {
                        var comp = t.GetComponentInChildren(field.FieldType, true);
                        if (comp != null)
                        {
                            field.SetValue(behaviour, comp);
                            synced++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[VRCSim] InitProxyDeep: {type.Name}.{name}: {e.Message}");
                }
            }

            return synced;
        }

        // ══════════════════════════════════════════════════════════════
        //  Phase 2: Field Access — read/write C# proxy fields
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Set a C# proxy field on an UdonSharpBehaviour.
        /// Handles type coercion (int→float, float→int, int→bool).
        /// Optionally syncs to Udon heap for consistency.
        /// </summary>
        public static void SetField(Component behaviour, string fieldName,
            object value, bool syncToHeap = true)
        {
            if (behaviour == null)
                throw new ArgumentNullException(nameof(behaviour));

            var field = ResolveField(behaviour.GetType(), fieldName);
            if (field == null)
            {
                Debug.LogError(
                    $"[VRCSim] SetField: {behaviour.GetType().Name}.{fieldName} not found — field does not exist on C# proxy. Use SetVar() for Udon heap variables.");
                return;
            }

            var coerced = CoerceValue(value, field.FieldType);
            field.SetValue(behaviour, coerced);

            if (syncToHeap)
            {
                var udon = SimReflection.GetUdonBehaviour(behaviour.gameObject);
                if (udon != null)
                {
                    try { SimReflection.SetProgramVariable(udon, fieldName, coerced); }
                    catch (Exception e)
                    {
                        Debug.LogWarning(
                            $"[VRCSim] SetField: heap sync failed for {fieldName}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Read a C# proxy field from an UdonSharpBehaviour.
        /// Returns null if the field doesn't exist.
        /// </summary>
        public static object GetField(Component behaviour, string fieldName)
        {
            if (behaviour == null) return null;

            var field = ResolveField(behaviour.GetType(), fieldName);
            if (field == null)
            {
                Debug.LogWarning(
                    $"[VRCSim] GetField: {behaviour.GetType().Name}.{fieldName} not found");
                return null;
            }

            return field.GetValue(behaviour);
        }

        /// <summary>
        /// Read a C# proxy field with type casting.
        /// Returns defaultValue if the field doesn't exist or the cast fails.
        /// </summary>
        public static T GetField<T>(Component behaviour, string fieldName,
            T defaultValue = default)
        {
            var raw = GetField(behaviour, fieldName);
            if (raw == null) return defaultValue;

            try
            {
                var coerced = CoerceValue(raw, typeof(T));
                return coerced is T typed ? typed : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Set multiple fields at once. Reduces boilerplate in test setup.
        /// </summary>
        public static void SetFields(Component behaviour,
            params (string name, object value)[] fields)
        {
            foreach (var (name, value) in fields)
                SetField(behaviour, name, value);
        }

        /// <summary>
        /// Check if a field exists on the C# proxy type.
        /// </summary>
        public static bool HasField(Component behaviour, string fieldName)
        {
            if (behaviour == null) return false;
            return ResolveField(behaviour.GetType(), fieldName) != null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Phase 3: Method Invocation — call any method with caching
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Call a method (including private) on a Component.
        /// Searches declared methods first, then inherited.
        /// Caches MethodInfo per type for subsequent calls.
        /// </summary>
        public static object Call(Component behaviour, string methodName,
            params object[] args)
        {
            if (behaviour == null)
                throw new ArgumentNullException(nameof(behaviour));

            var method = ResolveMethod(behaviour.GetType(), methodName,
                args ?? Array.Empty<object>());
            if (method == null)
            {
                Debug.LogError(
                    $"[VRCSim] Call: {behaviour.GetType().Name}.{methodName}" +
                    $"({args?.Length ?? 0} args) not found");
                return null;
            }

            return method.Invoke(behaviour, args);
        }

        /// <summary>
        /// Call a method with a typed return value.
        /// Renamed from Call&lt;T&gt; to CallAs&lt;T&gt; to avoid C# overload resolution
        /// preferring the generic over the params object[] overload when float/int
        /// literals are passed (exact match beats boxing).
        /// </summary>
        public static T CallAs<T>(Component behaviour, string methodName,
            T defaultValue, params object[] args)
        {
            var result = Call(behaviour, methodName, args);
            if (result is T typed) return typed;

            try
            {
                var coerced = CoerceValue(result, typeof(T));
                return coerced is T c ? c : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Check if a method exists on the type (including private).
        /// </summary>
        public static bool HasMethod(Component behaviour, string methodName,
            int paramCount = 0)
        {
            if (behaviour == null) return false;
            return ResolveMethod(behaviour.GetType(), methodName,
                new object[paramCount]) != null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Reflection Resolution (cached)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve a FieldInfo by name, searching the full type hierarchy.
        /// Results are cached per type.
        /// </summary>
        public static FieldInfo ResolveField(Type type, string name)
        {
            var cache = GetOrBuildFieldCache(type);
            return cache.TryGetValue(name, out var f) ? f : null;
        }

        /// <summary>
        /// Resolve a MethodInfo by name and actual arguments.
        /// First tries exact parameter type match, then falls back to count-only match.
        /// </summary>
        public static MethodInfo ResolveMethod(Type type, string methodName,
            object[] args)
        {
            var cache = GetOrBuildMethodCache(type);
            if (!cache.TryGetValue(methodName, out var overloads))
                return null;

            int paramCount = args?.Length ?? 0;

            // First pass: match by parameter count AND compatible types
            if (args != null && args.Length > 0)
            {
                foreach (var m in overloads)
                {
                    var parameters = m.GetParameters();
                    if (parameters.Length != paramCount) continue;

                    bool allMatch = true;
                    for (int i = 0; i < paramCount; i++)
                    {
                        if (args[i] == null) continue;
                        var argType = args[i].GetType();
                        var paramType = parameters[i].ParameterType;
                        if (!paramType.IsAssignableFrom(argType)
                            && CoerceValue(args[i], paramType) == null)
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch) return m;
                }
            }

            // Fallback: count-only match (original behavior for zero-arg calls)
            foreach (var m in overloads)
                if (m.GetParameters().Length == paramCount)
                    return m;

            return null;
        }

        /// <summary>Backward-compatible overload for callers passing just a count.</summary>
        public static MethodInfo ResolveMethod(Type type, string methodName,
            int paramCount) =>
            ResolveMethod(type, methodName, new object[paramCount]);

        // ── Cache builders ─────────────────────────────────────────

        private static Dictionary<string, FieldInfo> GetOrBuildFieldCache(Type type)
        {
            if (_fieldCache.TryGetValue(type, out var cache))
                return cache;

            cache = new Dictionary<string, FieldInfo>();
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Walk the hierarchy to include inherited fields
            var current = type;
            while (current != null && current != typeof(object))
            {
                foreach (var f in current.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (!cache.ContainsKey(f.Name))
                        cache[f.Name] = f;
                }
                current = current.BaseType;
            }

            _fieldCache[type] = cache;
            return cache;
        }

        private static Dictionary<string, List<MethodInfo>> GetOrBuildMethodCache(Type type)
        {
            if (_methodCache.TryGetValue(type, out var cache))
                return cache;

            cache = new Dictionary<string, List<MethodInfo>>();
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Walk hierarchy for inherited methods too
            var current = type;
            while (current != null && current != typeof(object))
            {
                foreach (var m in current.GetMethods(flags | BindingFlags.DeclaredOnly))
                {
                    if (!cache.TryGetValue(m.Name, out var list))
                    {
                        list = new List<MethodInfo>();
                        cache[m.Name] = list;
                    }
                    // Avoid duplicates from virtual overrides
                    bool duplicate = false;
                    foreach (var existing in list)
                    {
                        if (ParametersMatch(existing, m))
                        { duplicate = true; break; }
                    }
                    if (!duplicate) list.Add(m);
                }
                current = current.BaseType;
            }

            _methodCache[type] = cache;
            return cache;
        }

        // ── Value coercion ─────────────────────────────────────────

        /// <summary>
        /// Coerce a value to match the target field type.
        /// Handles the common UdonSharp mismatches:
        ///   int  → float, double → float, float → int, int → bool
        /// Returns null if coercion is not possible.
        /// </summary>
        public static object CoerceValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            // Numeric coercions
            if (targetType == typeof(float))
            {
                if (value is int i) return (float)i;
                if (value is double d) return (float)d;
                if (value is long l) return (float)l;
            }
            if (targetType == typeof(int))
            {
                if (value is float f) return Mathf.RoundToInt(f);
                if (value is double d) return (int)Math.Round(d);
                if (value is long l) return (int)l;
            }
            if (targetType == typeof(bool))
            {
                if (value is int i) return i != 0;
            }
            if (targetType == typeof(double))
            {
                if (value is float f) return (double)f;
                if (value is int i) return (double)i;
            }

            // Fallback: try Convert — return null on failure rather than
            // returning the original wrong-type value
            try { return Convert.ChangeType(value, targetType); }
            catch
            {
                Debug.LogWarning(
                    $"[VRCSim] CoerceValue: cannot convert {value.GetType().Name} to {targetType.Name}");
                return null;
            }
        }


        // ══════════════════════════════════════════════════════════════
        // ── Helpers ────────────────────────────────────────────────

        private static bool ParametersMatch(MethodInfo a, MethodInfo b)
        {
            var pa = a.GetParameters();
            var pb = b.GetParameters();
            if (pa.Length != pb.Length) return false;
            for (int i = 0; i < pa.Length; i++)
                if (pa[i].ParameterType != pb[i].ParameterType)
                    return false;
            return true;
        }
    }
}
