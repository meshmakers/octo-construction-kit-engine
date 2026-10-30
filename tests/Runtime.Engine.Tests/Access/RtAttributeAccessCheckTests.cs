using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Xunit;
using static Meshmakers.Octo.Runtime.Engine.Tests.Access.AccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Access;

/// <summary>AB#7767: the one rule "which entities carry a value only a CK method may write".</summary>
public class RtAttributeAccessCheckTests
{
    [Theory]
    [InlineData(CkAttributeAccessDto.ReadWrite, true, true)]
    [InlineData(CkAttributeAccessDto.ReadOnly, true, true)]
    [InlineData(CkAttributeAccessDto.MethodOnly, true, false)]
    [InlineData(CkAttributeAccessDto.Hidden, false, false)]
    public void Helper_AnswersReadAndWriteOutsideAMethod(CkAttributeAccessDto access, bool readable, bool writable)
    {
        Assert.Equal(readable, AttributeAccess.IsReadableOutsideMethod(access));
        Assert.Equal(writable, AttributeAccess.IsWritableOutsideMethod(access));
    }

    [Fact]
    public void GraphAttribute_ExposesTheHelperForItsEffectiveAccess()
    {
        var attributes = AccountGraph().AllAttributesByName;

        Assert.True(attributes["Name"].IsReadableOutsideMethod && attributes["Name"].IsWritableOutsideMethod);
        Assert.True(attributes["Created"].IsReadableOutsideMethod && attributes["Created"].IsWritableOutsideMethod);
        Assert.True(attributes["Email"].IsReadableOutsideMethod && !attributes["Email"].IsWritableOutsideMethod);
        Assert.False(attributes["PasswordHash"].IsReadableOutsideMethod);
        Assert.False(attributes["PasswordHash"].IsWritableOutsideMethod);
    }

    [Fact]
    public void Find_ReportsHiddenAndMethodOnly_NotReadWriteOrReadOnly()
    {
        var cache = Cache();
        var entity = Dto(AccountType, null, ("Name", "n"), ("Created", "c"), ("Email", "e@x"), ("PasswordHash", "h"));

        var offenders = RtAttributeAccessCheck.Find(cache, Tenant, [entity]);

        Assert.Equal(["Email", "PasswordHash"], offenders.Select(o => o.AttributePath).Order().ToArray());
        Assert.All(offenders, o =>
        {
            Assert.Equal(entity.RtId, o.RtId);
            Assert.Equal(AccountType, o.CkTypeId);
        });
    }

    [Fact]
    public void Find_ReportsARecordMember_AndARecordArrayMemberWithItsIndex()
    {
        var cache = Cache();
        var entity = Dto(AccountType, null,
            ("Profile", RecordDto(("Label", "l"), ("Token", "t"))),
            ("Credentials", new List<object> { RecordDto(("Label", "a")), RecordDto(("Token", "t2")) }));

        var offenders = RtAttributeAccessCheck.Find(cache, Tenant, [entity]);

        Assert.Equal(["Credentials[1].Token", "Profile.Token"], offenders.Select(o => o.AttributePath).Order().ToArray());
    }

    [Fact]
    public void Find_AnExplicitNullForARestrictedAttribute_IsStillAnAttempt()
    {
        var offenders = RtAttributeAccessCheck.Find(Cache(), Tenant, [Dto(AccountType, null, ("PasswordHash", null))]);

        Assert.Single(offenders);
    }

    [Fact]
    public void Find_EntityWithoutRestrictedValues_AndUnknownTypes_AreClean()
    {
        var cache = Cache();

        Assert.Empty(RtAttributeAccessCheck.Find(cache, Tenant,
            [Dto(AccountType, null, ("Name", "n"), ("Created", "c")), Dto(PlainType, null, ("Name", "p")),
             Dto(new RtCkId<CkTypeId>("Test-1.0.0/Unknown"))]));
    }

    [Fact]
    public void ToMessage_CarriesCode139_AndTheThreeParameters()
    {
        var rtId = OctoObjectId.GenerateNewId();

        var message = RtAttributeAccessCheck.ToMessage(new RestrictedAttributeWrite(AccountType, rtId, "Profile.Token"));

        Assert.Equal(139, message.MessageNumber);
        Assert.Equal(rtId.ToString(), message.Parameters["rtId"]);
        Assert.Equal("Test-1.0.0/Account", message.Parameters["ckTypeId"]);
        Assert.Equal("Profile.Token", message.Parameters["attributePath"]);
    }

    [Fact]
    public void TenantHasRestrictedAttribute_IsFalseWithoutHiddenOrMethodOnly()
    {
        Assert.True(RtAttributeAccessCheck.TenantHasRestrictedAttribute(Cache(), Tenant));
        Assert.False(RtAttributeAccessCheck.TenantHasRestrictedAttribute(Cache(withRestricted: false), Tenant));
    }
}
