using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Xunit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

/// <summary>
///     AB#5192: <see cref="CkRecordGraph.GetBaseTypes" /> walked the DERIVED collection to produce base
///     ids. Because every entry in <c>_derivedRecords</c> has this record as its
///     <c>BaseCkRecordId</c>, the method returned its own id once per derived record — and an empty list
///     for a record that has base records but no derived ones, which is the case it exists for.
///     The fixture deliberately gives the record BOTH directions, so swapping them again fails here
///     instead of silently returning something plausible.
/// </summary>
public class CkRecordGraphInheritanceTests
{
    private static readonly CkId<CkRecordId> BaseId = new("Test-1.0.0/BaseRecord-1");
    private static readonly CkId<CkRecordId> MiddleId = new("Test-1.0.0/MiddleRecord-1");
    private static readonly CkId<CkRecordId> DerivedId = new("Test-1.0.0/DerivedRecord-1");

    private static CkRecordGraph CreateMiddleRecord()
    {
        return new CkRecordGraph(
            MiddleId,
            false,
            false,
            // Middle inherits from Base ...
            new[] { new CkGraphRecordInheritance(MiddleId, BaseId, 0) },
            BaseId,
            // ... and Derived inherits from Middle.
            new[] { new CkGraphRecordInheritance(DerivedId, MiddleId, 0) },
            new List<CkTypeAttributeDto>(),
            new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(),
            "Middle record with both a base and a derived record");
    }

    [Fact]
    public void GetBaseTypes_ReturnsBaseRecords_NotTheOwnIdPerDerivedRecord()
    {
        var middle = CreateMiddleRecord();

        var baseRecords = middle.GetBaseTypes(false);

        Assert.Equal(new[] { BaseId }, baseRecords.ToArray());
        Assert.DoesNotContain(MiddleId, baseRecords);
        Assert.DoesNotContain(DerivedId, baseRecords);
    }

    [Fact]
    public void GetBaseTypes_IncludeSelf_PutsTheRecordFirstAndThenItsBases()
    {
        var middle = CreateMiddleRecord();

        Assert.Equal(new[] { MiddleId, BaseId }, middle.GetBaseTypes(true).ToArray());
    }

    [Fact]
    public void GetAllDerivedRecords_StillReturnsTheOtherDirection()
    {
        // Pins the counterpart so a future "fix" cannot swap the two collections back.
        var middle = CreateMiddleRecord();

        Assert.Equal(new[] { DerivedId }, middle.GetAllDerivedRecords(false).ToArray());
        Assert.Equal(new[] { MiddleId, DerivedId }, middle.GetAllDerivedRecords(true).ToArray());
    }
}
