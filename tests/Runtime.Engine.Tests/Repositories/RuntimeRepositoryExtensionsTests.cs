using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#8355: the lookup for "load by rtId to use" treats a deleted (archived) entity as not found, while
///     <see cref="IRuntimeRepository.GetRtEntityByRtIdAsync{TEntity}" /> keeps returning it.
/// </summary>
public class RuntimeRepositoryExtensionsTests
{
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly OctoObjectId _rtId = OctoObjectId.GenerateNewId();

    [Fact]
    public async Task Generic_ReturnsTheEntity_WhenItIsActive()
    {
        var entity = new RtEntity { RtId = _rtId, RtState = RtState.Active };
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync<RtEntity>(_session, _rtId)).Returns(entity);

        var result = await _repository.GetActiveRtEntityByRtIdAsync<RtEntity>(_session, _rtId);

        Assert.Same(entity, result);
    }

    [Fact]
    public async Task Generic_ReturnsTheEntity_WhenItHasNoState()
    {
        // Entities written before rtState existed carry no value; they are not deleted.
        var entity = new RtEntity { RtId = _rtId, RtState = null };
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync<RtEntity>(_session, _rtId)).Returns(entity);

        var result = await _repository.GetActiveRtEntityByRtIdAsync<RtEntity>(_session, _rtId);

        Assert.Same(entity, result);
    }

    [Fact]
    public async Task Generic_ReturnsNull_WhenTheEntityIsArchived()
    {
        var entity = new RtEntity { RtId = _rtId, RtState = RtState.Archived };
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync<RtEntity>(_session, _rtId)).Returns(entity);

        var result = await _repository.GetActiveRtEntityByRtIdAsync<RtEntity>(_session, _rtId);

        Assert.Null(result);
    }

    [Fact]
    public async Task Generic_ReturnsNull_WhenNoEntityExists()
    {
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync<RtEntity>(_session, _rtId)).Returns((RtEntity?)null);

        var result = await _repository.GetActiveRtEntityByRtIdAsync<RtEntity>(_session, _rtId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ByRtEntityId_ReturnsNull_WhenTheEntityIsArchived_AndTheEntityOtherwise()
    {
        var archivedId = new RtEntityId("Test/Archived", _rtId);
        var activeRtId = OctoObjectId.GenerateNewId();
        var activeId = new RtEntityId("Test/Active", activeRtId);
        var active = new RtEntity { RtId = activeRtId, RtState = RtState.Active };
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync(_session, archivedId))
            .Returns(new RtEntity { RtId = _rtId, RtState = RtState.Archived });
        A.CallTo(() => _repository.GetRtEntityByRtIdAsync(_session, activeId)).Returns(active);

        Assert.Null(await _repository.GetActiveRtEntityByRtIdAsync(_session, archivedId));
        Assert.Same(active, await _repository.GetActiveRtEntityByRtIdAsync(_session, activeId));
    }

    [Fact]
    public async Task NullRepository_Throws()
    {
        IRuntimeRepository? repository = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repository!.GetActiveRtEntityByRtIdAsync<RtEntity>(_session, _rtId));
    }
}
