using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The value comparison of the released-document lock is strict: only values that are certainly the same are equal,
///     so an uncertain case counts as a change and is refused.
/// </summary>
public class RtAttributeValueEqualityTests
{
    public static TheoryData<object?, object?> EqualValues => new()
    {
        { null, null },
        { "Text", "Text" },
        { 5, 5L },
        { (short)3, 3UL },
        { 1.5, 1.5f },
        { true, true },
        { new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.FromHours(1)) },
        { new List<string> { "a", "b" }, new[] { "a", "b" } },
        { new byte[] { 1, 2 }, new byte[] { 1, 2 } }
    };

    public static TheoryData<object?, object?> DifferentValues => new()
    {
        { null, "" },
        { "Text", "text" },
        { "Text", " Text" },
        { "5", 5 },
        { 5, 6L },
        { 5, 5.1 },
        { true, false },
        { new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 12, 0, 0, 1, DateTimeKind.Utc) },
        { new List<string> { "a", "b" }, new[] { "b", "a" } },
        { new List<string> { "a" }, new[] { "a", "b" } },
        { new byte[] { 1, 2 }, new byte[] { 1, 3 } }
    };

    [Theory]
    [MemberData(nameof(EqualValues))]
    public void EqualValues_AreEqual(object? a, object? b)
    {
        Assert.True(RtAttributeValueEquality.AreEqual(a, b));
        Assert.True(RtAttributeValueEquality.AreEqual(b, a));
    }

    [Theory]
    [MemberData(nameof(DifferentValues))]
    public void DifferentValues_AreNotEqual(object? a, object? b)
    {
        Assert.False(RtAttributeValueEquality.AreEqual(a, b));
        Assert.False(RtAttributeValueEquality.AreEqual(b, a));
    }

    [Fact]
    public void Records_AreComparedByTypeAndMembers()
    {
        var record = Record("Doc/Address", ("Street", "Main"), ("Zip", 1010));

        Assert.True(RtAttributeValueEquality.AreEqual(record, Record("Doc/Address", ("Zip", 1010L), ("Street", "Main"))));
        Assert.False(RtAttributeValueEquality.AreEqual(record, Record("Doc/Address", ("Street", "Main"), ("Zip", 1020))));
        Assert.False(RtAttributeValueEquality.AreEqual(record, Record("Doc/Other", ("Street", "Main"), ("Zip", 1010))));
        Assert.False(RtAttributeValueEquality.AreEqual(record,
            Record("Doc/Address", ("Street", "Main"), ("Zip", 1010), ("City", "Wien"))));
    }

    [Fact]
    public void AWrittenLinkedBinary_IsAlwaysAChange()
    {
        var binary = new EntityBinaryInfo();

        Assert.False(RtAttributeValueEquality.AreEqual(binary, binary));
    }

    private static RtRecord Record(string recordId, params (string Name, object? Value)[] members)
    {
        var record = new RtRecord { CkRecordId = new RtCkId<CkRecordId>(recordId) };
        foreach (var (name, value) in members)
        {
            record.SetAttributeRawValue(name, value);
        }

        return record;
    }
}
