using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Security;
using Xunit;
using static Meshmakers.Octo.Runtime.Engine.Tests.Security.TypeWriteAccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#8574: the type write restriction (<c>writeAccess</c>) at the engine chokepoint — allowed and refused per
///     principal kind and channel, unknown principal refused (fail closed), inheritance (derived types inherit; every
///     block along the chain must admit), association edges at either end, unrestricted types unaffected, and a refusal
///     message that names the type, the restricting type, the caller and what would have been admitted.
/// </summary>
public class TypeWriteAccessGuardTests
{
    private const string Tenant = "tenant1";

    private static ICkCacheService Cache(bool restricted = true)
    {
        var cache = A.Fake<ICkCacheService>();
        Register(cache, Tenant, restricted);
        return cache;
    }

    private static IReadOnlyList<OperationMessage> Find(ICkCacheService cache, RtPrincipalKind kind, string? channel,
        params TypeWriteTarget[] targets) =>
        TypeWriteAccessGuard.Find(cache, Tenant, kind, channel, targets);

    private static TypeWriteTarget Create(string type) =>
        new(Rt(type), OctoObjectId.GenerateNewId(), TypeWriteAccessGuard.OperationCreate);

    [Theory]
    [InlineData(RtPrincipalKind.Interactive, null, true)]
    [InlineData(RtPrincipalKind.Client, null, false)]
    [InlineData(RtPrincipalKind.Unknown, null, false)]
    [InlineData(RtPrincipalKind.Client, Channel, true)]
    [InlineData(RtPrincipalKind.Unknown, Channel, true)]
    [InlineData(RtPrincipalKind.Client, "System.Ai/OtherTools", false)]
    [InlineData(RtPrincipalKind.Client, "system.ai/knowledgetools", false)]
    public void RestrictedType_AllowsOnlyAdmittedPrincipalsAndChannels(RtPrincipalKind kind, string? channel,
        bool allowed)
    {
        var refusals = Find(Cache(), kind, channel, Create(KnowledgeType));

        Assert.Equal(allowed, refusals.Count == 0);
    }

    [Theory]
    [InlineData(RtPrincipalKind.Unknown)]
    [InlineData(RtPrincipalKind.Client)]
    [InlineData(RtPrincipalKind.Interactive)]
    public void UnrestrictedType_IgnoresThePrincipalKind(RtPrincipalKind kind)
    {
        Assert.Empty(Find(Cache(), kind, null, Create(NoteType)));
    }

    [Fact]
    public void TenantWithoutRestriction_IsUnaffected_AndReportedAsSuch()
    {
        var cache = Cache(restricted: false);

        Assert.False(TypeWriteAccessGuard.TenantHasRestrictedType(cache, Tenant));
        Assert.Empty(Find(cache, RtPrincipalKind.Unknown, null, Create(KnowledgeType), Create(LockedKnowledgeType)));
        Assert.True(TypeWriteAccessGuard.TenantHasRestrictedType(Cache(), Tenant));
    }

    [Fact]
    public void DerivedType_InheritsTheRestriction_AndNamesTheRestrictingBaseType()
    {
        var refusal = Assert.Single(Find(Cache(), RtPrincipalKind.Client, null, Create(SpecialKnowledgeType)));

        Assert.Equal(SpecialKnowledgeType, refusal.Parameters["ckTypeId"]);
        Assert.Equal(KnowledgeType, refusal.Parameters["restrictingCkTypeId"]);
        Assert.Empty(Find(Cache(), RtPrincipalKind.Interactive, null, Create(SpecialKnowledgeType)));
    }

    [Fact]
    public void DerivedTypeWithOwnBlock_EveryBlockAlongTheChainMustAdmit()
    {
        // The base admits Interactive, the derived type's own block admits only the channel.
        var refusal = Assert.Single(Find(Cache(), RtPrincipalKind.Interactive, null, Create(LockedKnowledgeType)));
        Assert.Equal(LockedKnowledgeType, refusal.Parameters["restrictingCkTypeId"]);

        // The channel is admitted by both blocks.
        Assert.Empty(Find(Cache(), RtPrincipalKind.Client, Channel, Create(LockedKnowledgeType)));
    }

    [Fact]
    public void AssociationChange_CountsAsAWriteOfBothEnds()
    {
        var note = new RtEntityId(Rt(NoteType), OctoObjectId.GenerateNewId());
        var knowledge = new RtEntityId(Rt(KnowledgeType), OctoObjectId.GenerateNewId());
        var role = new RtCkId<CkAssociationRoleId>("Test/Related");

        // Restricted entity at the target end.
        var toKnowledge = TypeWriteAccessGuard.TargetsOf([], [AssociationUpdateInfo.CreateInsert(note, knowledge, role)])
            .ToArray();
        var refusal = Assert.Single(Find(Cache(), RtPrincipalKind.Client, null, toKnowledge));
        Assert.Equal(KnowledgeType, refusal.Parameters["ckTypeId"]);
        Assert.Equal(TypeWriteAccessGuard.OperationAssociation, refusal.Parameters["operation"]);

        // Restricted entity at the origin end, also for a delete of the edge.
        var fromKnowledge = TypeWriteAccessGuard
            .TargetsOf([], [AssociationUpdateInfo.CreateDelete(knowledge, note, role)]).ToArray();
        Assert.Single(Find(Cache(), RtPrincipalKind.Client, null, fromKnowledge));

        // Edges between unrestricted entities are not affected.
        var other = new RtEntityId(Rt(NoteType), OctoObjectId.GenerateNewId());
        Assert.Empty(Find(Cache(), RtPrincipalKind.Unknown, null,
            TypeWriteAccessGuard.TargetsOf([], [AssociationUpdateInfo.CreateInsert(note, other, role)]).ToArray()));
    }

    [Fact]
    public void Check_JudgesEveryEntityOperation_AndListsOneRefusalPerWrite()
    {
        var id = OctoObjectId.GenerateNewId();
        var knowledge = new RtEntityId(Rt(KnowledgeType), id);
        var entity = new RtEntity(Rt(KnowledgeType), id);
        var updates = new List<IEntityUpdateInfo<RtEntity>>
        {
            EntityUpdateInfo<RtEntity>.CreateInsert(Rt(KnowledgeType), new RtEntity(Rt(KnowledgeType), OctoObjectId.GenerateNewId())),
            EntityUpdateInfo<RtEntity>.CreateUpdate(knowledge, entity),
            EntityUpdateInfo<RtEntity>.CreateReplace(knowledge, entity),
            EntityUpdateInfo<RtEntity>.CreateDelete(knowledge),
            EntityUpdateInfo<RtEntity>.CreateInsert(Rt(NoteType), new RtEntity(Rt(NoteType), OctoObjectId.GenerateNewId()))
        };
        var result = new OperationResult();

        TypeWriteAccessGuard.Check(Cache(), Tenant, RtSecurityContext.ForUser("agent", ["TenantManagement"],
            RtPrincipalKind.Client), updates, [], result);

        Assert.Equal(["create", "update", "replace", "delete"],
            result.Messages.Select(m => m.Parameters["operation"]).ToArray());
        Assert.All(result.Messages, m => Assert.Equal(144, m.MessageNumber));
    }

    [Fact]
    public void RefusalMessage_NamesTypeCallerAndTheMissingPrincipalOrChannel()
    {
        var target = Create(KnowledgeType);

        var refusal = Assert.Single(Find(Cache(), RtPrincipalKind.Client, null, target));

        Assert.Equal(144, refusal.MessageNumber);
        Assert.Equal(MessageLevel.Error, refusal.MessageLevel);
        Assert.Equal(target.RtId.ToString(), refusal.Parameters["rtId"]);
        Assert.Equal("Client", refusal.Parameters["principalKind"]);
        Assert.Equal("none", refusal.Parameters["channel"]);
        Assert.Equal("Interactive", refusal.Parameters["principals"]);
        Assert.Equal(Channel, refusal.Parameters["channels"]);
        Assert.Contains($"type '{KnowledgeType}'", refusal.MessageText);
        Assert.Contains("principal 'Client' on channel 'none'", refusal.MessageText);
        Assert.Contains($"[Interactive] or the channels [{Channel}]", refusal.MessageText);
    }

    [Fact]
    public void Admits_ChannelOrListedPrincipal_NeverUnknownByPrincipal()
    {
        var block = new CkTypeWriteAccessDto { Principals = [CkWritePrincipals.Interactive, CkWritePrincipals.Client] };

        Assert.True(TypeWriteAccessGuard.Admits(block, RtPrincipalKind.Client, null));
        Assert.False(TypeWriteAccessGuard.Admits(block, RtPrincipalKind.Unknown, null));
        Assert.False(TypeWriteAccessGuard.Admits(new CkTypeWriteAccessDto { Channels = [Channel] },
            RtPrincipalKind.Interactive, null));
        Assert.True(TypeWriteAccessGuard.Admits(new CkTypeWriteAccessDto { Channels = [Channel] },
            RtPrincipalKind.Unknown, Channel));
    }

    [Fact]
    public void SecurityContext_DefaultsToUnknown_AndCarriesAChannelOnlyWhenSetByCode()
    {
        var plain = RtSecurityContext.ForUser("user", ["Admin"]);
        Assert.Equal(RtPrincipalKind.Unknown, plain.PrincipalKind);
        Assert.Null(plain.WriteChannel);
        Assert.Equal(RtPrincipalKind.Unknown, RtSecurityContext.System.PrincipalKind);

        var withChannel = RtSecurityContext.ForUser("user", ["Admin"], RtPrincipalKind.Interactive).WithWriteChannel(Channel);
        Assert.Equal(RtPrincipalKind.Interactive, withChannel.PrincipalKind);
        Assert.Equal(Channel, withChannel.WriteChannel);
        Assert.Throws<ArgumentException>(() => plain.WithWriteChannel(" "));
    }
}
