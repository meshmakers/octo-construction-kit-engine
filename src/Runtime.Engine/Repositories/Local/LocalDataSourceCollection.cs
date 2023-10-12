using System.Collections.Concurrent;
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


    public async Task InsertAsync(IOctoSession session, TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        var key = _dataSourceMapper.GetId(document);
        if (!_rtEntities.TryAdd(key, document))
        {
            throw RuntimeRepositoryException.DocumentAlreadyAdded(_tenantId, key);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    public async Task InsertMultipleAsync(IOctoSession session, IEnumerable<TDocument> documents)
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

    public async Task UpdateMultipleAsync(IOctoSession session, IEnumerable<TDocument> documents)
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
            
            if (!_rtEntities.TryUpdate(_dataSourceMapper.GetId(document), document, savedDocument))
            {
                throw RuntimeRepositoryException.DocumentAlreadyAdded(_tenantId, key);
            }
        }
        
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

    public IQueryable<TDocument> AsQueryable()
    {
        return _rtEntities.Values.AsQueryable();
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