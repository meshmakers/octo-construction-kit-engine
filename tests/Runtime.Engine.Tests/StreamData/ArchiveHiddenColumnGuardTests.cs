using System;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     CK v2 (F1.2-S2, AB#5911, review M12): stream-data queries do not enforce attribute access, so an archive
///     whose ingested columns reach a Hidden attribute is refused on activation.
/// </summary>
public class ArchiveHiddenColumnGuardTests
{
    private const string TenantId = "tenant-hidden";
    private const string ModelId = "Test-1.0.0";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();

    private readonly IArchiveRuntimeStore _store = A.Fake<IArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly ICkCacheService _cache = A.Fake<ICkCacheService>();
    private readonly RtCkId<CkTypeId> _sensor;

    public ArchiveHiddenColumnGuardTests()
    {
        var reading = new CkRecordGraph(new CkId<CkRecordId>($"{ModelId}/Reading"), false, false, [], null, [], [],
            new[] { Attr("Value"), Attr("Calibration", CkAttributeAccessDto.Hidden) }.ToDictionary(a => a.CkAttributeId),
            "Reading");
        var sensor = new CkTypeGraph(new CkId<CkTypeId>($"{ModelId}/Sensor"), false, false, true, [], null, null, [], [],
            new[]
            {
                Attr("Temperature"), Attr("Secret", CkAttributeAccessDto.Hidden), Attr("Status", CkAttributeAccessDto.ReadOnly),
                Attr("Reading", recordId: reading.CkRecordId)
            }.ToDictionary(a => a.CkAttributeId), [], new CkGraphDirectedAssociations([]), "Sensor", false);
        _sensor = sensor.CkTypeId.ToRtCkId();
        A.CallTo(() => _cache.GetRtCkType(TenantId, _sensor)).Returns(sensor);
        A.CallTo(() => _cache.GetRtCkRecord(TenantId, reading.CkRecordId.ToRtCkId())).Returns(reading);
    }

    private static CkTypeAttributeGraph Attr(string name, CkAttributeAccessDto access = CkAttributeAccessDto.ReadWrite,
        CkId<CkRecordId>? recordId = null)
    {
        var attributeId = new CkId<CkAttributeId>($"{ModelId}/{name}");
        var definition = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = recordId == null ? AttributeValueTypesDto.String : AttributeValueTypesDto.Record,
            ValueCkRecordId = recordId
        };
        return new CkTypeAttributeGraph(attributeId,
            new CkTypeAttributeDto { CkAttributeId = attributeId, AttributeName = name, IsOptional = true },
            new CkAttributeGraph(attributeId, definition)) { Access = access };
    }

    private ArchiveLifecycleService Sut(bool withCache = true) =>
        new(TenantId, _store, _repo, A.Fake<IArchiveAuditTrail>(), NullLogger<ArchiveLifecycleService>.Instance,
            ckCacheService: withCache ? _cache : null);

    private void Stub(CkArchiveStatus status, params string[] paths) =>
        A.CallTo(() => _store.GetAsync(Rt)).Returns(new ArchiveSnapshot(Rt, _sensor, status, null,
            paths.Select(p => new CkArchiveColumnSpec(p, true, false)).ToList()));

    [Theory]
    [InlineData("Secret")]
    [InlineData("secret")] // case-insensitive, like the attribute resolution of the data store
    [InlineData("Reading.Calibration")]
    [InlineData("reading[*].calibration")]
    public async Task ColumnReachingAHiddenAttribute_IsRefused(string path)
    {
        Stub(CkArchiveStatus.Created, "Temperature", path);

        var ex = await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().ActivateAsync(Rt));

        Assert.Equal(path, ex.ColumnPath);
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task AlreadyActivatedArchive_GainingAHiddenColumn_IsRefused()
    {
        Stub(CkArchiveStatus.Activated, "Secret");

        await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().ActivateAsync(Rt));
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RetryActivation_IsGuardedToo()
    {
        Stub(CkArchiveStatus.Failed, "Secret");

        await Assert.ThrowsAsync<HiddenAttributeInArchiveException>(() => Sut().RetryActivationAsync(Rt));
    }

    [Fact]
    public async Task VisibleColumns_Activate()
    {
        Stub(CkArchiveStatus.Created, "Temperature", "Status", "Reading.Value", "Unknown.Path");

        await Sut().ActivateAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WithoutACkCache_TheCheckIsSkipped()
    {
        Stub(CkArchiveStatus.Created, "Secret");

        await Sut(withCache: false).ActivateAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }
}
