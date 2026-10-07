using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#5415: a loaded CK cache is only ever invalidated by a best-effort push (the controller's
///     CkModelChanged broadcast, the tenant update events). When one is lost, a type added by a CK
///     model import stays invisible and every execution fails with the error the import repaired.
///     <see cref="RuntimeRepositoryBase.GetCkTypeGraphAsync" /> therefore reloads the model once on a
///     miss - rate-limited, because a genuinely unknown type misses on every execution.
/// </summary>
public class StaleCkCacheSelfHealTests
{
    private static readonly RtCkId<CkTypeId> TypeId = new("Loxone/Control");

    [Fact]
    public async Task TypeInTheCache_IsReturnedWithoutReloading()
    {
        var cache = new CkCacheStub();
        cache.Add(TypeId);
        var repository = new StubRepository(cache);

        Assert.NotNull(await repository.GetCkTypeGraphAsync(TypeId));
        Assert.Equal(0, repository.RefreshCount);
    }

    [Fact]
    public async Task TypeMissingFromALoadedCache_ReloadsTheModelAndFindsIt()
    {
        // The import already repaired the repository; only this process's cache predates it.
        var cache = new CkCacheStub();
        var repository = new StubRepository(cache);
        repository.OnRefresh = () => cache.Add(TypeId);

        Assert.NotNull(await repository.GetCkTypeGraphAsync(TypeId));
        Assert.Equal(1, repository.RefreshCount);
        Assert.Equal(1, cache.UnloadCount);
    }

    [Fact]
    public async Task TypeMissingAfterTheReload_ThrowsAndDoesNotReloadAgainWithinTheCooldown()
    {
        // A pipeline referencing a type that does not exist anywhere must not pay a full model
        // resolve per execution - it misses at the adapter's full execution rate.
        var cache = new CkCacheStub();
        var repository = new StubRepository(cache);

        await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));
        await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));
        await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));

        Assert.Equal(1, repository.RefreshCount);
    }

    [Fact]
    public async Task OnceTheCooldownElapsed_AFurtherMissReloadsAgain()
    {
        var cache = new CkCacheStub();
        var repository = new StubRepository(cache);
        var cooldown = RuntimeRepositoryBase.StaleCkCacheReloadCooldown;
        RuntimeRepositoryBase.StaleCkCacheReloadCooldown = TimeSpan.Zero;
        try
        {
            await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));
            await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));
        }
        finally
        {
            RuntimeRepositoryBase.StaleCkCacheReloadCooldown = cooldown;
        }

        Assert.Equal(2, repository.RefreshCount);
    }

    [Fact]
    public async Task UnloadedCache_IsLoadedOnceAndAMissIsNotRetried()
    {
        // GetCkCacheServiceAsync has just read the model from the repository, so the cache is as
        // fresh as it gets - a miss is real and a second reload would only repeat it.
        var cache = new CkCacheStub { IsLoaded = false };
        var repository = new StubRepository(cache);
        repository.OnRefresh = () => cache.IsLoaded = true;

        await Assert.ThrowsAsync<CkCacheException>(() => repository.GetCkTypeGraphAsync(TypeId));

        Assert.Equal(1, repository.RefreshCount);
        Assert.Equal(0, cache.UnloadCount);
    }

    /// <summary>
    ///     Only the three members the self-heal touches are backed: the rest of
    ///     <see cref="ICkCacheService" /> is irrelevant here and left to the fake.
    /// </summary>
    private sealed class CkCacheStub
    {
        private readonly HashSet<RtCkId<CkTypeId>> _types = [];

        public CkCacheStub()
        {
            Service = A.Fake<ICkCacheService>();

            A.CallTo(() => Service.IsTenantLoaded(A<string>._)).ReturnsLazily(() => IsLoaded);
            A.CallTo(() => Service.Unload(A<string>._)).Invokes(() =>
            {
                UnloadCount++;
                IsLoaded = false;
            });

            CkTypeGraph? ignored;
            A.CallTo(() => Service.TryGetRtCkType(A<string>._, A<RtCkId<CkTypeId>>._, out ignored))
                .ReturnsLazily(call => _types.Contains((RtCkId<CkTypeId>)call.Arguments[1]!))
                .AssignsOutAndRefParametersLazily(call =>
                    [_types.Contains((RtCkId<CkTypeId>)call.Arguments[1]!) ? Graph : null]);

            // Mirrors the real cache: the throwing lookup is what callers catch as
            // "this tenant never imported that CK library".
            A.CallTo(() => Service.GetRtCkType(A<string>._, A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => _types.Contains((RtCkId<CkTypeId>)call.Arguments[1]!)
                    ? Graph
                    : throw new CkCacheException($"RtCkTypeId '{call.Arguments[1]}' not found in CkCache"));
        }

        public string TenantId { get; } = Guid.NewGuid().ToString();

        public ICkCacheService Service { get; }

        public bool IsLoaded { get; set; } = true;

        public int UnloadCount { get; private set; }

        private static CkTypeGraph Graph { get; } = new(new CkId<CkTypeId>("Loxone/Control"),
            new CkCompiledTypeDto());

        public void Add(RtCkId<CkTypeId> ckTypeId)
        {
            _types.Add(ckTypeId);
            IsLoaded = true;
        }
    }

    private sealed class StubRepository(CkCacheStub cache)
        : RuntimeRepositoryBase(cache.TenantId, cache.Service, null!, null!)
    {
        public int RefreshCount { get; private set; }

        public Action? OnRefresh { get; set; }

        protected override Task RefreshCkCacheServiceAsync(ICkCacheService ckCacheService)
        {
            RefreshCount++;
            // A real refresh reads the model and leaves the tenant loaded, whether or not the
            // wanted type turned up in it.
            cache.IsLoaded = true;
            OnRefresh?.Invoke();
            return Task.CompletedTask;
        }

        public override Task<IOctoSession> GetSessionAsync() => throw new NotSupportedException();

        public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByTypeAsync(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions,
            ICollection<NavigationPair> roleIdDirectionPairs, int? skip = null, int? take = null) =>
            throw new NotSupportedException();

        public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
            RtEntityQueryOptions rtEntityQueryOptions, IEnumerable<NavigationPair> roleIdDirectionPairs,
            int? skip = null, int? take = null) => throw new NotSupportedException();

        protected override Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
            RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null) =>
            throw new NotSupportedException();

        protected override Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

        protected override Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

        protected override Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions, int? skip = null,
            int? take = null) => throw new NotSupportedException();
    }
}
