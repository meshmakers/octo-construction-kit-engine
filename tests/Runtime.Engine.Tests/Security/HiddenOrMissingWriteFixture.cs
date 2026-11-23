using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     Shared setup of the hidden-or-missing write tests: the policies of the data-permission repro on the
///     <c>Required</c> type, the callers, payloads and the comparison of two answers.
/// </summary>
public abstract class HiddenOrMissingWriteFixture
{
    protected const string OtherUser = "user-other";
    protected const string WriterOwnedRead = "WriterOwnedRead";
    protected const string WriteOnly = "WriteOnly";
    protected const string Full = "Full";
    protected const string OwnedWriter = "OwnedWriter";

    private const string RequiredType = "Test/Required";
    private static readonly RtCkId<CkAssociationRoleId> ValidRoleId = new("Test/Related");

    private protected SecretTestModel Model { get; } = new();

    protected Dictionary<OctoObjectId, RtEntity> StoredEntities { get; } = new();

    protected IDataSourceCollection<OctoObjectId, RtEntity> Collection { get; }

    protected HiddenOrMissingWriteFixture()
    {
        Collection = HiddenOrMissingTestRepository.CreateCollection(StoredEntities);
    }

    protected static RtDataPolicyTable PolicyTable() =>
        new(
        [
            new RtDataPolicyRule("p1", new HashSet<string> { RequiredType }, [RtDataAction.Write, RtDataAction.Delete],
                false, false, new HashSet<string> { WriterOwnedRead, WriteOnly }),
            new RtDataPolicyRule("p2", new HashSet<string> { RequiredType }, [RtDataAction.Read], true, false,
                new HashSet<string> { WriterOwnedRead }),
            new RtDataPolicyRule("p3", new HashSet<string> { RequiredType },
                [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete], false, false,
                new HashSet<string> { Full }),
            new RtDataPolicyRule("p4", new HashSet<string> { RequiredType }, [RtDataAction.Read], false, false,
                new HashSet<string> { OwnedWriter }),
            new RtDataPolicyRule("p5", new HashSet<string> { RequiredType }, [RtDataAction.Write, RtDataAction.Delete],
                true, false, new HashSet<string> { OwnedWriter })
        ]);

    protected void RegisterByCkId(CkTypeGraph typeGraph)
    {
        var typeGraphOut = (CkTypeGraph?)typeGraph;
        A.CallTo(() => Model.Cache.TryGetCkType(SecretTestModel.TenantId,
                A<CkId<CkTypeId>>.That.Matches(id => id.FullName == typeGraph.CkTypeId.FullName), out typeGraphOut))
            .Returns(true).AssignsOutAndRefParameters(typeGraph);
    }

    protected void RegisterGraph(CkTypeGraph typeGraph)
    {
        var rtCkTypeId = typeGraph.CkTypeId.ToRtCkId();
        var typeGraphOut = (CkTypeGraph?)typeGraph;
        A.CallTo(() => Model.Cache.GetRtCkType(SecretTestModel.TenantId,
            A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtCkTypeId.FullName))).Returns(typeGraph);
        A.CallTo(() => Model.Cache.TryGetRtCkType(SecretTestModel.TenantId,
                A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtCkTypeId.FullName), out typeGraphOut))
            .Returns(true).AssignsOutAndRefParameters(typeGraph);
        RegisterByCkId(typeGraph);
    }

    private protected HiddenOrMissingTestRepository Repository(RtDataPolicyTable policyTable) =>
        HiddenOrMissingTestRepository.Create(Model, Collection, policyTable);

    private protected HiddenOrMissingTestRepository RepositoryWithTheBaseNotFoundMessage(
        RtDataPolicyTable policyTable) =>
        HiddenOrMissingTestRepository.Create(Model, Collection, policyTable, answersWithTheBaseNotFoundMessage: true);

    protected static IOctoSession UserSession(string role)
    {
        var session = A.Fake<IOctoSecureSession>();
        A.CallTo(() => session.SecurityContext).Returns(RtSecurityContext.ForUser("user-" + role, [role]));
        return session;
    }

    protected static string SubjectOf(string role) => "user-" + role;

    protected static IOctoSession SystemSession() => A.Fake<IOctoSession>();

    protected RtEntity Store(string? createdBy)
    {
        var entity = Payload(OctoObjectId.GenerateNewId(), true);
        entity.RtCreatedBy = createdBy;
        StoredEntities[entity.RtId] = entity;
        return entity;
    }

    private RtCkId<CkTypeId> RequiredTypeId => Model.Required.CkTypeId.ToRtCkId();

    protected RtEntity Payload(OctoObjectId rtId, bool valid)
    {
        var entity = new RtEntity(RequiredTypeId, rtId);
        entity.SetAttributeRawValue("Title", valid ? "changed" : null);
        return entity;
    }

    protected IEntityUpdateInfo<RtEntity> Change(string operation, OctoObjectId rtId, bool validPayload)
    {
        var entityId = new RtEntityId(RequiredTypeId, rtId);
        return operation switch
        {
            "update" => EntityUpdateInfo<RtEntity>.CreateUpdate(entityId, Payload(rtId, validPayload)),
            "replace" => EntityUpdateInfo<RtEntity>.CreateReplace(entityId, Payload(rtId, validPayload)),
            "delete" => EntityUpdateInfo<RtEntity>.CreateDelete(entityId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation")
        };
    }

    protected AssociationUpdateInfo AssociationFrom(OctoObjectId originRtId, bool validAssociation)
    {
        var origin = new RtEntityId(RequiredTypeId, originRtId);
        var target = new RtEntityId(RequiredTypeId, OctoObjectId.GenerateNewId());
        var roleId = validAssociation ? ValidRoleId : HiddenOrMissingTestRepository.InvalidRoleId;
        return AssociationUpdateInfo.CreateInsert(origin, target, roleId);
    }

    private protected static Task ApplyAsync(HiddenOrMissingTestRepository repository, IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> changes, IReadOnlyList<AssociationUpdateInfo>? associations = null) =>
        repository.ApplyChangesAsync(session, changes, associations ?? [], new DeleteOptions(), new OperationResult());

    protected const int MandatoryAttributeMissingMessageNumber = 2;
    protected const int MandatoryAttributeMissingAtUpdateMessageNumber = 5;
    protected const int InvalidRoleMessageNumber = HiddenOrMissingTestRepository.InvalidRoleMessageNumber;

    protected static int MandatoryTitleMessageNumberOf(string operation) =>
        operation == "update" ? MandatoryAttributeMissingAtUpdateMessageNumber : MandatoryAttributeMissingMessageNumber;

    protected sealed record Answer(string ExceptionType, string Message, string MessageNumbers);

    protected static async Task<Answer> AnswerOfAsync(Func<Task> write, OctoObjectId rtId)
    {
        var exception = await Record.ExceptionAsync(write);
        Assert.NotNull(exception);
        var messageNumbers = exception is RuntimeRepositoryException repositoryException
            ? string.Join(",", repositoryException.OperationResult.Messages.Select(message => message.MessageNumber))
            : string.Empty;
        return new Answer(exception.GetType().Name, exception.Message.Replace(rtId.ToString(), "<rtId>"),
            messageNumbers);
    }

    protected static void AssertNotFound(Answer answer) =>
        Assert.True(answer.ExceptionType == nameof(TestEntityNotFoundException), $"Expected not found, got {answer}");

    protected static void AssertValidationError(Answer answer, int expectedMessageNumber)
    {
        Assert.True(answer.ExceptionType == nameof(RuntimeRepositoryException),
            $"Expected a validation error, got {answer}");
        var messageNumbers = answer.MessageNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(expectedMessageNumber.ToString(), messageNumbers);
    }
}
