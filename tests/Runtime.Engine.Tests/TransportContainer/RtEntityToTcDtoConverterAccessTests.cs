using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;
using Xunit;
using static Meshmakers.Octo.Runtime.Engine.Tests.Access.AccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.TransportContainer;

/// <summary>
///     AB#7767 (H2) — the export half of the access model: <see cref="RtEntityToTcDtoConverter"/> never emits a
///     Hidden value (type attribute or record member), and still emits MethodOnly and ReadOnly values (readable).
/// </summary>
public class RtEntityToTcDtoConverterAccessTests
{
    private static RtEntity Account()
    {
        var entity = new RtEntity { RtId = OctoObjectId.GenerateNewId(), CkTypeId = AccountType };
        entity.SetAttributeRawValue("Name", "n");
        entity.SetAttributeRawValue("Created", "c");
        entity.SetAttributeRawValue("Email", "user@example.invalid");
        entity.SetAttributeRawValue("PasswordHash", "synthetic-hash-value");
        return entity;
    }

    [Fact]
    public void Convert_NeverEmitsAHiddenValue_ButEmitsMethodOnlyAndReadOnly()
    {
        var converter = new RtEntityToTcDtoConverter(Cache());

        var dto = converter.Convert(Tenant, Account());

        var names = dto.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.DoesNotContain("PasswordHash", names);
        Assert.Contains("Email", names);
        Assert.Contains("Created", names);
        Assert.Contains("Name", names);
        Assert.DoesNotContain(dto.Attributes, a => Equals(a.Value, "synthetic-hash-value"));
    }

    [Fact]
    public void Convert_HiddenRecordMember_IsDropped_AndTheVisibleMemberStays()
    {
        var cache = Cache();
        var converter = new RtEntityToTcDtoConverter(cache);
        var entity = Account();
        var record = new RtRecord { CkRecordId = CredentialRecord };
        record.SetAttributeRawValue("Label", "visible");
        record.SetAttributeRawValue("Token", "synthetic-token");
        entity.SetAttributeRawValue("Profile", record);

        var dto = converter.Convert(Tenant, entity);

        var exported = Assert.IsType<RtRecordTcDto>(dto.Attributes.Single(a => a.Id.ElementId.Name == "Profile").Value);
        var members = exported.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.Equal(["Label"], members);
    }

    [Fact]
    public void Convert_HiddenMemberInsideARecordArray_IsDropped()
    {
        var converter = new RtEntityToTcDtoConverter(Cache());
        var entity = Account();
        RtRecord Rec(string label, string token)
        {
            var r = new RtRecord { CkRecordId = CredentialRecord };
            r.SetAttributeRawValue("Label", label);
            r.SetAttributeRawValue("Token", token);
            return r;
        }

        entity.SetAttributeRawValue("Credentials", new List<object> { Rec("a", "t1"), Rec("b", "t2") });

        var dto = converter.Convert(Tenant, entity);

        var list = ((IEnumerable<object>)dto.Attributes.Single(a => a.Id.ElementId.Name == "Credentials").Value!)
            .Cast<RtRecordTcDto>().ToList();
        Assert.Equal(2, list.Count);
        Assert.All(list, r => Assert.Equal(["Label"], r.Attributes.Select(a => a.Id.ElementId.Name).ToList()));
    }
}
