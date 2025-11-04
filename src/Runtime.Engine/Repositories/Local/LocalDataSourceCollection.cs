using System.Collections.Concurrent;
using System.Linq.Expressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalDataSourceCollection<TKey, TDocument, TDto>(
    string tenantId,
    string filePath,
    IDataSourceMapper<TKey, TDocument, TDto> dataSourceMapper)
    : IDataSourceCollection<TKey, TDocument>
    where TDocument : new()
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TDocument> _rtEntities = new();
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private bool _isLoaded;


    public async Task<IBulkImportResult> BulkImportAsync(IOctoSession session, IEnumerable<TDocument> documents,
        BulkOperationOptions options)
    {
        await LoadAsync().ConfigureAwait(false);

        var hasError = false;
        long insertCount = 0;
        long updateCount = 0;
        foreach (var document in documents)
        {
            var key = dataSourceMapper.GetId(document);
            if (!_rtEntities.TryAdd(key, document))
            {
                if (options.InsertStrategy == BulkInsertStrategies.Upsert &&
                    _rtEntities.TryGetValue(key, out var comparisionDocument))
                {
                    if (!_rtEntities.TryUpdate(key, document, comparisionDocument))
                    {
                        hasError = true;
                        continue;
                    }

                    updateCount++;
                }
                else
                {
                    hasError = true;
                }
            }
            else
            {
                insertCount++;
            }
        }

        await SaveAsync().ConfigureAwait(false);

        var result = new LocalBulkImportResult(insertCount, 0, updateCount, hasError);
        return result;
    }

    public async Task InsertOneAsync(IOctoSession session, TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        var key = dataSourceMapper.GetId(document);
        if (!_rtEntities.TryAdd(key, document))
        {
            throw RuntimeRepositoryException.DocumentAlreadyAdded(tenantId, key);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task InsertManyAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = dataSourceMapper.GetId(document);
            if (!_rtEntities.TryAdd(key, document))
            {
                throw RuntimeRepositoryException.DocumentAlreadyAdded(tenantId, key);
            }
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task UpdateOneAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = dataSourceMapper.GetId(document);

            _rtEntities.TryGetValue(key, out var savedDocument);

            if (savedDocument == null)
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
            }

            dataSourceMapper.Apply(savedDocument, document);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task UpdateManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression,
        TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = _rtEntities.Values.AsQueryable().Where(expression);

        foreach (var savedDocument in result)
        {
            dataSourceMapper.Apply(savedDocument, document);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task ReplaceManyAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = dataSourceMapper.GetId(document);

            _rtEntities.TryGetValue(key, out var savedDocument);

            if (savedDocument == null)
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
            }

            dataSourceMapper.Apply(savedDocument, document);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task ReplaceByIdAsync(IOctoSession session, TKey key, TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        _rtEntities.TryGetValue(key, out var savedDocument);

        if (savedDocument == null)
        {
            throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
        }

        dataSourceMapper.Apply(savedDocument, document);

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task DeleteOneAsync(IOctoSession session, TKey key)
    {
        await LoadAsync().ConfigureAwait(false);

        if (!_rtEntities.TryRemove(key, out _))
        {
            throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task<bool> TryDeleteOneAsync(IOctoSession session, TKey key)
    {
        await LoadAsync().ConfigureAwait(false);

        if (!_rtEntities.TryRemove(key, out _))
        {
            return false;
        }

        await SaveAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TryDeleteOneAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = _rtEntities.Values.AsQueryable().FirstOrDefault(expression);
        if (result == null)
        {
            return false;
        }

        var key = dataSourceMapper.GetId(result);
        return _rtEntities.TryRemove(key, out _);
    }

    public async Task DeleteOneAsync(IOctoSession session, IEnumerable<TKey> keys)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var key in keys)
        {
            if (!_rtEntities.TryRemove(key, out _))
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
            }
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task DeleteManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = _rtEntities.Values.AsQueryable().Where(expression);
        foreach (var document in result)
        {
            var key = dataSourceMapper.GetId(document);

            if (!_rtEntities.TryRemove(key, out _))
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(tenantId, key, typeof(TDocument));
            }
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task<TDocument?> DocumentAsync(IOctoSession session, TKey key)
    {
        await LoadAsync().ConfigureAwait(false);

        _rtEntities.TryGetValue(key, out var document);

        return document;
    }

    public async Task<IReadOnlyList<TDocument>> DocumentsAsync(IOctoSession session, IEnumerable<TKey> keys)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = new List<TDocument>();
        foreach (var key in keys)
        {
            if (_rtEntities.TryGetValue(key, out var document))
            {
                result.Add(document);
            }
        }

        return result;
    }

    public async Task<TDerived?> DocumentAsync<TDerived>(IOctoSession session, TKey key)
        where TDerived : TDocument, new()
    {
        await LoadAsync().ConfigureAwait(false);

        _rtEntities.TryGetValue(key, out var document);

        return (TDerived?)document;
    }

    public async Task<IQueryable<TDocument>> AsQueryableAsync(IOctoSession? session = null)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.Values.AsQueryable();
    }

    public IQueryable<TDocument> AsQueryable(IOctoSession? session = null)
    {
        LoadAsync().Wait();

        return _rtEntities.Values.AsQueryable();
    }

    public async Task<TDocument?> FindSingleOrDefaultAsync(IOctoSession session,
        Expression<Func<TDocument, bool>> expression)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.Values.AsQueryable().SingleOrDefault(expression);
    }

    public async Task<ICollection<TDocument>> FindManyAsync(IOctoSession session,
        Expression<Func<TDocument, bool>> expression,
        int? skip = null, int? take = null)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = _rtEntities.Values.AsQueryable().Where(expression);
        if (skip != null)
        {
            result = result.Skip(skip.Value);
        }

        if (take != null)
        {
            result = result.Take(take.Value);
        }

        return result.ToList();
    }

    public async Task<long> GetTotalCountAsync(IOctoSession session)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.LongCount();
    }

    public async Task<long> GetTotalCountAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.Values.AsQueryable().LongCount(expression);
    }

    public async Task<IEnumerable<TDocument>> GetAsync(IOctoSession session, int? skip = null, int? take = null)
    {
        await LoadAsync().ConfigureAwait(false);

        var result = _rtEntities.Values.AsQueryable();
        if (skip != null)
        {
            result = result.Skip(skip.Value);
        }

        if (take != null)
        {
            result = result.Take(take.Value);
        }

        return result;
    }

    private async Task SaveAsync()
    {
        try
        {
            await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
            if (!_isLoaded)
            {
                return;
            }

#if NETSTANDARD2_0
            using var streamWriter = new StreamWriter(filePath);
#else
            await using var streamWriter = new StreamWriter(filePath);
#endif
            await dataSourceMapper.SerializeAsync(streamWriter, _rtEntities).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

            if (_isLoaded)
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                _isLoaded = true;
                return;
            }

            OperationResult operationResult = new();
#if NETSTANDARD2_0
            using var fileStream = File.OpenRead(filePath);
#else
            await using var fileStream = File.OpenRead(filePath);
#endif

            var rtEntities = await dataSourceMapper.DeserializeAsync(fileStream, filePath, operationResult)
                .ConfigureAwait(false);
            foreach (var keyValuePair in rtEntities)
            {
                _rtEntities.TryAdd(keyValuePair.Key, keyValuePair.Value);
            }

            _isLoaded = true;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}