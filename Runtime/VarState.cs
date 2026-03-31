using System;
using UnityEngine;

namespace VRCSim
{
    /// <summary>
    /// Snapshot of a single variable read from both the Udon VM heap
    /// and the C# proxy simultaneously. Returned by VRCSim.GetBoth().
    ///
    /// Use this when:
    ///   - You want to assert on heap state after RunEvent/RunFixedUpdate
    ///   - You want to assert on proxy state after Call()
    ///   - You want to verify both stores agree (InSync) after SetVar
    ///   - You want to detect unexpected heap/proxy divergence as a test failure
    ///
    /// InSync is itself a useful invariant: after any SetVar the stores must
    /// agree. After SetVarHeapOnly they intentionally don't — asserting
    /// !InSync there confirms the test setup is correct.
    /// </summary>
    public readonly struct VarState
    {
        /// <summary>Value read from the Udon VM heap (GetProgramVariable).</summary>
        public readonly object Heap;

        /// <summary>Value read from the C# proxy field (FieldInfo.GetValue).</summary>
        public readonly object Proxy;

        /// <summary>True when both stores hold equal values (array-aware).</summary>
        public bool InSync => SimSnapshot.DeepEquals(Heap, Proxy);

        public VarState(object heap, object proxy)
        {
            Heap  = heap;
            Proxy = proxy;
        }

        /// <summary>
        /// Return the heap value cast/coerced to T.
        /// Returns defaultValue if the heap value is null or the cast fails.
        /// </summary>
        public T HeapAs<T>(T defaultValue = default)
        {
            if (Heap == null) return defaultValue;
            if (Heap is T typed) return typed;
            try
            {
                var coerced = SimProxy.CoerceValue(Heap, typeof(T));
                return coerced is T c ? c : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Return the proxy value cast/coerced to T.
        /// Returns defaultValue if the proxy value is null or the cast fails.
        /// </summary>
        public T ProxyAs<T>(T defaultValue = default)
        {
            if (Proxy == null) return defaultValue;
            if (Proxy is T typed) return typed;
            try
            {
                var coerced = SimProxy.CoerceValue(Proxy, typeof(T));
                return coerced is T c ? c : defaultValue;
            }
            catch { return defaultValue; }
        }

        public override string ToString() =>
            $"[heap={Heap ?? "null"} | proxy={Proxy ?? "null"} | inSync={InSync}]";
    }
}
