using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

/// <summary>
///     CK v2 Phase 0: id types and predicates of the contracts skeleton (AB#5667 / AB#5668 / AB#5669).
/// </summary>
public class CkV2ContractTests
{
    [Fact]
    public void CkInterfaceId_ParsesNameAndContractVersion()
    {
        var id = new CkInterfaceId("Named-2");

        Assert.Equal("Named", id.Name);
        Assert.Equal(2u, id.Version);
        Assert.Equal("Named-2", id.FullName);
        Assert.Equal(new CkInterfaceId("Named", 2), id);
        Assert.Equal("System.Identity/Named-1",
            new CkId<CkInterfaceId>("System.Identity-2.90.0/Named-1").ToRtCkId().FullName);
    }

    [Fact]
    public void CkMethodIds_QualifyAndParse_AreInverse()
    {
        var typeId = new RtCkId<CkTypeId>("System.Identity/User");

        var qualified = CkMethodIds.Qualify(typeId, "ChangePassword-1");

        Assert.Equal("System.Identity/User.ChangePassword-1", qualified);
        Assert.True(CkMethodIds.TryParse(qualified, out var parsedType, out var methodId));
        Assert.Equal(typeId, parsedType);
        Assert.Equal("ChangePassword-1", methodId);
    }

    [Fact]
    public void CkMethodIds_DottedTypeAndVersionedType_Parse()
    {
        var qualified = CkMethodIds.Qualify(new RtCkId<CkTypeId>("Basic.Energy/Meter.Smart-2"), "Read-3");

        Assert.Equal("Basic.Energy/Meter.Smart-2.Read-3", qualified);
        Assert.True(CkMethodIds.TryParse(qualified, out var parsedType, out var methodId));
        Assert.Equal("Basic.Energy", parsedType.ModelId);
        Assert.Equal(new CkTypeId("Meter.Smart-2"), parsedType.ElementId);
        Assert.Equal("Read-3", methodId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoSlash.Do-1")]
    [InlineData("Model/TypeWithoutMethod")]
    [InlineData("Model/Type.")]
    // Review L1: an invalid element version used to escape as TargetInvocationException (Activator).
    [InlineData("Model/Type-x.Do-1")]
    [InlineData("Model/-1.Do-1")]
    public void CkMethodIds_TryParse_RejectsMalformedValues(string? value)
    {
        Assert.False(CkMethodIds.TryParse(value, out _, out _));
    }

    [Theory]
    [InlineData(null, CkAttributeAccessDto.ReadWrite, true, true, true, true)]
    [InlineData(CkAttributeAccessDto.ReadWrite, CkAttributeAccessDto.ReadWrite, true, true, true, true)]
    [InlineData(CkAttributeAccessDto.ReadOnly, CkAttributeAccessDto.ReadOnly, true, true, true, false)]
    [InlineData(CkAttributeAccessDto.MethodOnly, CkAttributeAccessDto.MethodOnly, true, false, false, false)]
    [InlineData(CkAttributeAccessDto.Hidden, CkAttributeAccessDto.Hidden, false, false, false, false)]
    public void AttributeAccess_Predicates(CkAttributeAccessDto? declared, CkAttributeAccessDto effective,
        bool output, bool genericInput, bool writableOnCreate, bool writableOnUpdate)
    {
        var resolved = AttributeAccess.Resolve(declared);

        Assert.Equal(effective, resolved);
        Assert.Equal(output, AttributeAccess.IsExposedInOutput(resolved));
        Assert.Equal(genericInput, AttributeAccess.IsExposedInGenericInput(resolved));
        Assert.Equal(writableOnCreate, AttributeAccess.IsGenericallyWritable(resolved, isCreate: true));
        Assert.Equal(writableOnUpdate, AttributeAccess.IsGenericallyWritable(resolved, isCreate: false));
    }
}
