using System.Linq.Expressions;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     A hand-built CK model for the released-document lock and an in-memory data source serving it. Invoice is numbered
///     from the range "InvoiceNumber" (attribute <c>Number</c>) and has the enum state <c>DocumentState</c> (Draft = 0,
///     Released = 1, Cancelled = 2), a changeable <c>PaymentStatus</c> and business attributes <c>Title</c>,
///     <c>Amount</c> and <c>Tags</c>. Note has no number. The range is a <c>System/AutoIncrement</c> entity whose
///     <c>Release</c> record each test configures. Every write of the data source is counted, so a test can assert that
///     a refused batch wrote nothing.
/// </summary>
internal sealed class ReleasedDocumentTestModel
{
    public const string TenantId = "released-documents";
    public const string Range = "InvoiceNumber";
    public const int Draft = 0;
    public const int Released = 1;
    public const int Cancelled = 2;
    private const string ModelId = "Doc-1.0.0";

    private readonly Dictionary<string, List<RtEntity>> _collections = new(StringComparer.Ordinal);

    public ReleasedDocumentTestModel()
    {
        var stateEnumId = new CkId<CkEnumId>($"{ModelId}/DocumentStates");
        StateEnum = new CkEnumGraph(stateEnumId, new CkEnumDto
        {
            EnumId = "DocumentStates",
            Values =
            [
                new CkEnumValueDto { Key = Draft, Name = "Draft" },
                new CkEnumValueDto { Key = Released, Name = "Released" },
                new CkEnumValueDto { Key = Cancelled, Name = "Cancelled" }
            ]
        });

        Invoice = BuildType($"{ModelId}/Invoice",
            Attr("Title", AttributeValueTypesDto.String),
            Attr("Amount", AttributeValueTypesDto.Double),
            Attr("Tags", AttributeValueTypesDto.StringArray),
            Attr("PaymentStatus", AttributeValueTypesDto.String),
            Attr("DocumentState", AttributeValueTypesDto.Enum, enumId: stateEnumId),
            Attr("Number", AttributeValueTypesDto.String, autoIncrementReference: Range));
        Note = BuildType($"{ModelId}/Note", Attr("Title", AttributeValueTypesDto.String));
        AutoIncrement = BuildType("System-2.9.0/AutoIncrement",
            Attr("CurrentValue", AttributeValueTypesDto.Int, model: "System-2.9.0"),
            Attr("Release", AttributeValueTypesDto.Record, model: "System-2.9.0"));
        AuditEntry = BuildType("System-2.9.0/DocumentAuditEntry",
            Attr("DocumentRtId", AttributeValueTypesDto.String, model: "System-2.9.0"));

        Cache = A.Fake<ICkCacheService>();
        A.CallTo(() => Cache.IsTenantLoaded(TenantId)).Returns(true);
        A.CallTo(() => Cache.GetCkTypes(TenantId)).Returns([Invoice, Note, AutoIncrement, AuditEntry]);
        foreach (var type in new[] { Invoice, Note, AutoIncrement, AuditEntry })
        {
            RegisterType(type);
        }

        CkEnumGraph? ignored;
        A.CallTo(() => Cache.TryGetCkEnum(TenantId, A<CkId<CkEnumId>>.That.Matches(id => id.FullName == stateEnumId.FullName),
                out ignored))
            .Returns(true).AssignsOutAndRefParameters(StateEnum);

        DataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => DataSource.TenantId).Returns(TenantId);
        A.CallTo(() => DataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._))
            .ReturnsLazily((CkTypeGraph graph) => FakeCollection(graph.CkTypeId.ToRtCkId().FullName));
        Associations = A.Fake<IDataSourceCollection<OctoObjectId, RtAssociation>>();
        A.CallTo(() => DataSource.RtAssociations).Returns(Associations);
        A.CallTo(() => Associations.FindManyAsync(A<IOctoSession>._, A<Expression<Func<RtAssociation, bool>>>._,
                A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, Expression<Func<RtAssociation, bool>> filter, int? _, int? _) =>
                Task.FromResult<ICollection<RtAssociation>>(StoredAssociations.Where(filter.Compile()).ToList()));
        A.CallTo(() => Associations.InsertManyAsync(A<IOctoSession>._, A<IEnumerable<RtAssociation>>._))
            .Invokes(() => Writes++);
        A.CallTo(() => Associations.DeleteOneAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .Invokes(() => Writes++);
    }

    public ICkCacheService Cache { get; }

    public IRepositoryDataSource DataSource { get; }

    public IDataSourceCollection<OctoObjectId, RtAssociation> Associations { get; }

    public List<RtAssociation> StoredAssociations { get; } = [];

    public CkTypeGraph Invoice { get; }

    public CkTypeGraph Note { get; }

    public CkTypeGraph AutoIncrement { get; }

    public CkTypeGraph AuditEntry { get; }

    public CkEnumGraph StateEnum { get; }

    public RtCkId<CkTypeId> InvoiceId => Invoice.CkTypeId.ToRtCkId();

    public RtCkId<CkTypeId> NoteId => Note.CkTypeId.ToRtCkId();

    /// <summary>Number of write calls on any collection of the data source.</summary>
    public int Writes { get; private set; }

    /// <summary>Stores the number range with a release configuration (or without one).</summary>
    public RtEntity StoreRange(IReadOnlyList<string>? releasedValues = null, IReadOnlyList<string>? changeable = null,
        string stateAttribute = "DocumentState", bool withRelease = true, long currentValue = 7)
    {
        var range = new RtEntity(AutoIncrement.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["CurrentValue"] = currentValue })
        {
            RtWellKnownName = Range
        };
        if (withRelease)
        {
            var release = new RtRecord();
            release.SetAttributeRawValue(NumberRangeRelease.StateAttributeAttribute, stateAttribute);
            release.SetAttributeRawValue(NumberRangeRelease.ReleasedValuesAttribute,
                (releasedValues ?? ["Released", "Cancelled"]).ToList());
            if (changeable != null)
            {
                release.SetAttributeRawValue(NumberRangeRelease.ChangeableAttributesAttribute, changeable.ToList());
            }

            range.SetAttributeRawValue(NumberRangeRelease.ReleaseAttribute, release);
        }

        Store(range);
        return range;
    }

    /// <summary>Stores an invoice in the given state (released ones with a number) and returns it.</summary>
    public RtEntity StoreInvoice(int state, string title = "invoice")
    {
        var invoice = NewInvoice(state, title);
        if (state != Draft)
        {
            invoice.SetAttributeRawValue("Number", "RE-2026-00007");
        }

        Store(invoice);
        return invoice;
    }

    public RtEntity NewInvoice(int state, string title = "invoice", OctoObjectId? rtId = null) =>
        new(InvoiceId, rtId ?? OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            ["Title"] = title,
            ["Amount"] = 120.5,
            ["Tags"] = new List<string> { "a", "b" },
            ["PaymentStatus"] = "Open",
            ["DocumentState"] = state
        });

    public RtEntity StoreNote(string title = "note")
    {
        var note = new RtEntity(NoteId, OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["Title"] = title });
        Store(note);
        return note;
    }

    public RtEntity Partial(RtEntity target, params (string Name, object? Value)[] attributes)
    {
        var partial = new RtEntity(target.GetRtCkTypeId(), target.RtId);
        foreach (var (name, value) in attributes)
        {
            partial.SetAttributeRawValue(name, value);
        }

        return partial;
    }

    /// <summary>A copy of the attributes of the stored entity, to compare after a refused write.</summary>
    public Dictionary<string, object?> Snapshot(RtEntity stored) =>
        new(stored.Attributes, StringComparer.Ordinal);

    public RtAssociation Association(RtEntity origin, RtEntity target, string role = "Doc/Customer")
    {
        return new RtAssociation
        {
            AssociationId = OctoObjectId.GenerateNewId(),
            AssociationRoleId = new RtCkId<CkAssociationRoleId>(role),
            OriginCkTypeId = origin.GetRtCkTypeId(),
            OriginRtId = origin.RtId,
            TargetCkTypeId = target.GetRtCkTypeId(),
            TargetRtId = target.RtId
        };
    }

    private void Store(RtEntity entity) => StoredList(entity.GetRtCkTypeId().FullName).Add(entity);

    private List<RtEntity> StoredList(string typeFullName)
    {
        if (!_collections.TryGetValue(typeFullName, out var list))
        {
            list = [];
            _collections[typeFullName] = list;
        }

        return list;
    }

    private IDataSourceCollection<OctoObjectId, RtEntity> FakeCollection(string typeFullName)
    {
        var list = StoredList(typeFullName);
        var collection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => collection.FindManyAsync(A<IOctoSession>._, A<Expression<Func<RtEntity, bool>>>._, A<int?>._,
                A<int?>._))
            .ReturnsLazily((IOctoSession _, Expression<Func<RtEntity, bool>> filter, int? _, int? _) =>
                Task.FromResult<ICollection<RtEntity>>(list.Where(filter.Compile()).ToList()));
        A.CallTo(collection).Where(call => IsWrite(call.Method.Name)).Invokes(() => Writes++);
        return collection;
    }

    private static bool IsWrite(string method) =>
        method.StartsWith("Insert", StringComparison.Ordinal) || method.StartsWith("Update", StringComparison.Ordinal) ||
        method.StartsWith("Replace", StringComparison.Ordinal) || method.StartsWith("Delete", StringComparison.Ordinal) ||
        method.StartsWith("BulkImport", StringComparison.Ordinal);

    private void RegisterType(CkTypeGraph type)
    {
        CkTypeGraph? ignored;
        var rtId = type.CkTypeId.ToRtCkId();
        A.CallTo(() => Cache.TryGetRtCkType(TenantId,
                A<RtCkId<CkTypeId>>.That.Matches(id => id != null && id.FullName == rtId.FullName), out ignored))
            .Returns(true).AssignsOutAndRefParameters(type);
        A.CallTo(() => Cache.GetRtCkType(TenantId,
                A<RtCkId<CkTypeId>>.That.Matches(id => id != null && id.FullName == rtId.FullName)))
            .Returns(type);
        A.CallTo(() => Cache.TryGetCkType(TenantId,
                A<CkId<CkTypeId>>.That.Matches(id => id != null && id.FullName == type.CkTypeId.FullName), out ignored))
            .Returns(true).AssignsOutAndRefParameters(type);
    }

    private static CkTypeAttributeGraph Attr(string name, AttributeValueTypesDto valueType,
        CkId<CkEnumId>? enumId = null, string? autoIncrementReference = null, string model = ModelId)
    {
        var attributeId = new CkId<CkAttributeId>($"{model}/{name}");
        var definition = new CkAttributeDto { AttributeId = name, ValueType = valueType, ValueCkEnumId = enumId };
        return new CkTypeAttributeGraph(attributeId,
            new CkTypeAttributeDto
            {
                CkAttributeId = attributeId, AttributeName = name, IsOptional = true,
                AutoIncrementReference = autoIncrementReference
            },
            new CkAttributeGraph(attributeId, definition));
    }

    private static CkTypeGraph BuildType(string id, params CkTypeAttributeGraph[] attributes)
    {
        return new CkTypeGraph(new CkId<CkTypeId>(id), false, false, true, [], null, null, [], [],
            attributes.ToDictionary(a => a.CkAttributeId, a => a), [], new CkGraphDirectedAssociations([]), id, false);
    }
}
