using System.Collections.Concurrent;
using System.Linq.Expressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalDataSourceCollection<TKey, TDocument, TDto> : IDataSourceCollection<TKey, TDocument> 
    where TDocument : new() 
    where TKey : notnull
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly string _tenantId;
    private readonly string _filePath;
    private readonly IDataSourceMapper<TKey, TDocument, TDto> _dataSourceMapper;

    private readonly ConcurrentDictionary<TKey, TDocument> _rtEntities;
    private bool _isLoaded;

    public LocalDataSourceCollection(string tenantId, string filePath, 
        IDataSourceMapper<TKey, TDocument, TDto> dataSourceMapper)
    {
        _tenantId = tenantId;
        _filePath = filePath;
        _dataSourceMapper = dataSourceMapper;

        _rtEntities = new ConcurrentDictionary<TKey, TDocument>();
        _isLoaded = false;
    }


    public async Task<IBulkImportResult> BulkImportAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        bool hasError = false;
        long insertCount = 0;
        long updateCount = 0;
        foreach (var document in documents)
        {
            var key = _dataSourceMapper.GetId(document);
            if (!_rtEntities.TryAdd(key, document))
            {
                if (_rtEntities.TryGetValue(key, out var comparisionDocument))
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

        var key = _dataSourceMapper.GetId(document);
        if (!_rtEntities.TryAdd(key, document))
        {
            throw RuntimeRepositoryException.DocumentAlreadyAdded(_tenantId, key);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task InsertManyAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = _dataSourceMapper.GetId(document);
            if (!_rtEntities.TryAdd(key, document))
            {
                throw RuntimeRepositoryException.DocumentAlreadyAdded(_tenantId, key);
            }
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task UpdateManyAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = _dataSourceMapper.GetId(document);
            
            _rtEntities.TryGetValue(key, out TDocument? savedDocument);

            if (savedDocument == null)
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(_tenantId, key, typeof(TDocument));
            }
            
            _dataSourceMapper.Apply(savedDocument, document);
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task ReplaceManyAsync(IOctoSession session, IEnumerable<TDocument> documents)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var document in documents)
        {
            var key = _dataSourceMapper.GetId(document);
            
            _rtEntities.TryGetValue(key, out TDocument? savedDocument);

            if (savedDocument == null)
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(_tenantId, key, typeof(TDocument));
            }
            
            _dataSourceMapper.Apply(savedDocument, document);
        }
        
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task ReplaceByIdAsync(IOctoSession session, TKey key, TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        _rtEntities.TryGetValue(key, out TDocument? savedDocument);

        if (savedDocument == null)
        {
            throw RuntimeRepositoryException.DocumentDoesNotExist(_tenantId, key, typeof(TDocument));
        }
        
        _dataSourceMapper.Apply(savedDocument, document);
        
        await SaveAsync().ConfigureAwait(false);

    }

    public async Task DeleteOneAsync(IOctoSession session, TKey key)
    {
        await LoadAsync().ConfigureAwait(false);
        
        if (!_rtEntities.TryRemove(key, out _))
        {
            throw RuntimeRepositoryException.DocumentDoesNotExist(_tenantId, key, typeof(TDocument));
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

    public async Task DeleteManyAsync(IOctoSession session, IEnumerable<TKey> keys)
    {
        await LoadAsync().ConfigureAwait(false);

        foreach (var key in keys)
        {
            if (!_rtEntities.TryRemove(key, out _))
            {
                throw RuntimeRepositoryException.DocumentDoesNotExist(_tenantId, key, typeof(TDocument));
            }
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task<TDocument?> DocumentAsync(IOctoSession session, TKey key)
    {
        await LoadAsync().ConfigureAwait(false);
        
        _rtEntities.TryGetValue(key, out TDocument? document);

        return document;
    }

    public async Task<TDerived?> DocumentAsync<TDerived>(IOctoSession session, TKey key) where TDerived : TDocument, new()
    {
        await LoadAsync().ConfigureAwait(false);

        _rtEntities.TryGetValue(key, out TDocument? document);
        
        return (TDerived?)document;
    }

    public async Task<IQueryable<TDocument>> AsQueryableAsync(IOctoSession? session = null)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.Values.AsQueryable();
    }

    public async Task<TDocument?> FindSingleOrDefaultAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression)
    {
        await LoadAsync().ConfigureAwait(false);

        return _rtEntities.Values.AsQueryable().SingleOrDefault(expression);
    }

    public async Task<ICollection<TDocument>> FindManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression, int? skip = null, int? take = null)
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

            await using var streamWriter = new StreamWriter(_filePath);
            await _dataSourceMapper.SerializeAsync(streamWriter, _rtEntities).ConfigureAwait(false);
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

            if (!File.Exists(_filePath))
            {
                _isLoaded = true;
                return;
            }

            OperationResult operationResult = new();
            await using var fileStream = File.OpenRead(_filePath);
            
            var rtEntities = await _dataSourceMapper.DeserializeAsync(fileStream, _filePath, operationResult).ConfigureAwait(false);
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