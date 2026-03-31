/// <summary>
/// VRCSim self-tests — unit tests for VRCSim's own utility methods.
///
/// These tests do NOT require ClientSim or Play mode. They verify the
/// internal logic that all consumers depend on: type coercion, deep
/// equality, value formatting, and snapshot diffing.
///
/// Run via Unity Test Runner > EditMode.
/// </summary>
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCSim;

[TestFixture]
public class VRCSimSelfTests
{
    // ── CoerceValue ────────────────────────────────────────────

    [Test] public void Coerce_IntToFloat() =>
        Assert.AreEqual(42f, SimProxy.CoerceValue(42, typeof(float)));

    [Test] public void Coerce_FloatToInt_Rounds() =>
        Assert.AreEqual(3, SimProxy.CoerceValue(2.7f, typeof(int)));

    [Test] public void Coerce_DoubleToFloat() =>
        Assert.AreEqual(1.5f, SimProxy.CoerceValue(1.5d, typeof(float)));

    [Test] public void Coerce_IntToBool_Zero() =>
        Assert.AreEqual(false, SimProxy.CoerceValue(0, typeof(bool)));

    [Test] public void Coerce_IntToBool_NonZero() =>
        Assert.AreEqual(true, SimProxy.CoerceValue(1, typeof(bool)));

    [Test] public void Coerce_LongToInt() =>
        Assert.AreEqual(99, SimProxy.CoerceValue(99L, typeof(int)));

    [Test] public void Coerce_LongToFloat() =>
        Assert.AreEqual(99f, SimProxy.CoerceValue(99L, typeof(float)));

    [Test] public void Coerce_FloatToDouble() =>
        Assert.AreEqual(2.5d, SimProxy.CoerceValue(2.5f, typeof(double)));

    [Test] public void Coerce_Null_ReturnsNull() =>
        Assert.IsNull(SimProxy.CoerceValue(null, typeof(int)));

    [Test] public void Coerce_SameType_Passthrough() =>
        Assert.AreEqual(42, SimProxy.CoerceValue(42, typeof(int)));

    [Test] public void Coerce_Incompatible_ReturnsNull()
    {
        // String to int via Convert should work
        Assert.AreEqual(7, SimProxy.CoerceValue("7", typeof(int)));
        // Non-numeric string to int should fail gracefully
        Assert.IsNull(SimProxy.CoerceValue("not_a_number", typeof(int)));
    }

    [Test] public void Coerce_LongToInt_Overflow()
    {
        // long.MaxValue to int should still convert (via unchecked cast)
        // This tests that we don't crash on overflow
        var result = SimProxy.CoerceValue(long.MaxValue, typeof(int));
        Assert.IsNotNull(result);
        Assert.IsInstanceOf<int>(result);
    }

    // ── DeepEquals ─────────────────────────────────────────────

    [Test] public void DeepEquals_BothNull() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(null, null));

    [Test] public void DeepEquals_OneNull() =>
        Assert.IsFalse(SimSnapshot.DeepEquals(42, null));

    [Test] public void DeepEquals_SameRef()
    {
        var arr = new int[] { 1, 2, 3 };
        Assert.IsTrue(SimSnapshot.DeepEquals(arr, arr));
    }

    [Test] public void DeepEquals_IntArrays_Equal() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(
            new int[] { 1, 2, 3 }, new int[] { 1, 2, 3 }));

    [Test] public void DeepEquals_IntArrays_Different() =>
        Assert.IsFalse(SimSnapshot.DeepEquals(
            new int[] { 1, 2, 3 }, new int[] { 1, 2, 4 }));

    [Test] public void DeepEquals_IntArrays_DiffLength() =>
        Assert.IsFalse(SimSnapshot.DeepEquals(
            new int[] { 1, 2 }, new int[] { 1, 2, 3 }));

    [Test] public void DeepEquals_FloatArrays_Equal() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(
            new float[] { 1.5f, 2.5f }, new float[] { 1.5f, 2.5f }));

    [Test] public void DeepEquals_StringArrays_Equal() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(
            new string[] { "a", "b" }, new string[] { "a", "b" }));

    [Test] public void DeepEquals_MixedTypes() =>
        Assert.IsFalse(SimSnapshot.DeepEquals(
            new int[] { 1, 2 }, new float[] { 1f, 2f }));

    [Test] public void DeepEquals_EmptyArrays() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(
            new int[0], new int[0]));

    [Test] public void DeepEquals_Scalars_Equal() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(42, 42));

    [Test] public void DeepEquals_Scalars_Different() =>
        Assert.IsFalse(SimSnapshot.DeepEquals(42, 43));

    [Test] public void DeepEquals_BoolArrays() =>
        Assert.IsTrue(SimSnapshot.DeepEquals(
            new bool[] { true, false }, new bool[] { true, false }));

    // ── FormatValue ────────────────────────────────────────────

    // FormatValue is private, so we test it indirectly through Diff output.
    // But we CAN test the snapshot-level logic directly:

    [Test] public void Snapshot_Diff_NoChanges()
    {
        var a = CreateTestSnapshot(("x", 1), ("y", 2));
        var b = CreateTestSnapshot(("x", 1), ("y", 2));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(0, diff.Count, "Identical snapshots should have zero diffs");
    }

    [Test] public void Snapshot_Diff_DetectsValueChange()
    {
        var a = CreateTestSnapshot(("x", 1));
        var b = CreateTestSnapshot(("x", 99));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(1, diff.Count);
        Assert.AreEqual("x", diff[0].VarName);
    }

    [Test] public void Snapshot_Diff_DetectsNewVar()
    {
        var a = CreateTestSnapshot(("x", 1));
        var b = CreateTestSnapshot(("x", 1), ("y", 2));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(1, diff.Count);
        Assert.AreEqual("y", diff[0].VarName);
    }

    [Test] public void Snapshot_Diff_DetectsRemovedVar()
    {
        var a = CreateTestSnapshot(("x", 1), ("y", 2));
        var b = CreateTestSnapshot(("x", 1));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(1, diff.Count);
        Assert.AreEqual("y", diff[0].VarName);
    }

    [Test] public void Snapshot_Diff_ArrayContents()
    {
        var a = CreateTestSnapshot(("flags", new int[] { 0, 0, 0 }));
        var b = CreateTestSnapshot(("flags", new int[] { 0, 1, 0 }));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(1, diff.Count, "Array content change must be detected");
    }

    [Test] public void Snapshot_Diff_ArrayUnchanged()
    {
        var a = CreateTestSnapshot(("flags", new int[] { 1, 2, 3 }));
        var b = CreateTestSnapshot(("flags", new int[] { 1, 2, 3 }));
        var diff = SimSnapshot.Diff(a, b);
        Assert.AreEqual(0, diff.Count,
            "Arrays with same contents should NOT produce a diff");
    }

    // ── VarState ───────────────────────────────────────────────

    [Test] public void VarState_InSync_WhenEqual()
    {
        var vs = new VarState(42, 42);
        Assert.IsTrue(vs.InSync);
    }

    [Test] public void VarState_NotInSync_WhenDifferent()
    {
        var vs = new VarState(42, 99);
        Assert.IsFalse(vs.InSync);
    }

    [Test] public void VarState_InSync_Arrays()
    {
        var vs = new VarState(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        Assert.IsTrue(vs.InSync, "Identical arrays should be InSync");
    }

    [Test] public void VarState_NotInSync_DifferentArrays()
    {
        var vs = new VarState(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 4 });
        Assert.IsFalse(vs.InSync, "Different arrays should NOT be InSync");
    }

    [Test] public void VarState_HeapAs_CoercesType()
    {
        var vs = new VarState(42, 42f); // heap=int, proxy=float
        float val = vs.HeapAs<float>();
        Assert.AreEqual(42f, val);
    }

    [Test] public void VarState_ProxyAs_CoercesType()
    {
        var vs = new VarState(5f, 5); // heap=float, proxy=int
        int val = vs.ProxyAs<int>();
        Assert.AreEqual(5, val);
    }

    // ── Helpers ────────────────────────────────────────────────

    private static SimSnapshot CreateTestSnapshot(
        params (string name, object value)[] vars)
    {
        // SimSnapshot constructor is private, so we use reflection to build
        // a test instance. This is a self-test, so accessing internals is OK.
        var snap = (SimSnapshot)Activator.CreateInstance(
            typeof(SimSnapshot),
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance,
            null, new object[] { 0f }, null);

        var dict = new Dictionary<string, object>();
        foreach (var (name, value) in vars)
            dict[name] = value;
        snap.State["TestObject"] = dict;
        return snap;
    }
}
#endif
